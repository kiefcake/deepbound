using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Deepbound.Art;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Progression;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// Every class's ultimate, on R. It does not recharge on a timer: it fills from the
    /// fight itself — damage you deal, damage you take, a slow trickle while you are down
    /// in the dungeon — and when the bar is full, R spends it. One big, readable,
    /// party-changing moment per fight or two.
    ///
    /// <list type="bullet">
    /// <item><b>Knight — Rallying Banner.</b> Plant a war banner: for 8 s allies near it take
    ///   35% less damage and deal 20% more.</item>
    /// <item><b>Mage — Cataclysm.</b> Five meteors rain around the crosshair over two seconds.</item>
    /// <item><b>Cleric — Hymn of Return.</b> Every downed ally within 18 m rises; the whole
    ///   party is healed to at least 70% and shielded.</item>
    /// <item><b>Rogue — Death From Shadow.</b> Step behind the enemy under the crosshair and
    ///   strike for a huge backstab that leaves it Exposed (stunned).</item>
    /// <item><b>Ranger — Twin Wyrms</b> (2026-11-20, replaced Rain of Arrows; the user asked for something like a
    ///   certain archer's dragon volley): a spirit arrow splits into two coiling wyrms that run straight ahead
    ///   through walls for 44 m, tearing at everything they pass (a heavy first bite, then every 0.2 s) and chilling it.</item>
    /// </list>
    ///
    /// <para>Charge is server-owned; the owner reads input and sends the aim.</para>
    /// </summary>
    public class Ultimates : NetworkBehaviour
    {
        public const float ChargeMax = 100f;
        // Damage is measured against the floor's own scale (deeper hits and health are bigger), taken counts for
        // less, and nothing charges for a while after a cast: on floor 6 the autoplay Knight cast every ~12 s.
        private const float PerDamageDealt = 0.02f, PerDamageTaken = 0.06f, PerSecond = 0.15f, LockAfterCast = 30f;
        private float lockedUntil;
        public const float RogueChargeScale = 1.6f, RogueLockAfterCast = 18f;
        private static float DepthScaleHealth => Enemies.Difficulty.DepthHealth(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0);
        private static float DepthScaleDamage => Enemies.Difficulty.DepthDamage(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0);

        public readonly NetworkVariable<float> Charge = new NetworkVariable<float>();

        private PlayerProgress progress;
        private PlayerLife life;
        private NetworkPlayer player;
        private FirstPersonMotor motor;
        private Health health;
        private float lastCast;

        /// <summary>Live banners (server): where they stand and when they fall.</summary>
        private struct Banner { public Vector3 At; public float Until; }
        private static readonly List<Banner> banners = new List<Banner>();
        public const float BannerRadius = 7f;

        public static string Name(ClassId cls)
        {
            switch (cls)
            {
                case ClassId.Mage: return "Cataclysm";
                case ClassId.Cleric: return "Hymn of Return";
                case ClassId.Rogue: return "Death From Shadow";
                case ClassId.Ranger: return "Twin Wyrms";
                case ClassId.Flagellant: return "The Red Litany";
                case ClassId.PlagueDoctor: return "Pandemic";
                default: return "Rallying Banner";
            }
        }

        public static string Describe(ClassId cls)
        {
            switch (cls)
            {
                case ClassId.Mage: return "Five meteors strike around the target point over two seconds.";
                case ClassId.Cleric: return "Revive all downed allies within 18 m, heal the party to at least 70%, and shield it.";
                case ClassId.Rogue: return "Teleport behind the targeted enemy for a massive backstab that leaves it Exposed.";
                case ClassId.Ranger: return "Fire a spirit arrow that splits into two wyrms, passing through walls for 44 m and damaging and chilling every enemy in their path.";
                case ClassId.Flagellant: return "For 6 s your flail whirls around you, striking nearby enemies. You move faster, take 20% less damage and deal 30% more. Ends in a stunning Crash.";
                case ClassId.PlagueDoctor: return "Infect all enemies within 12 m with plague: heavy damage over 10 s and 25% more damage taken from all sources. It spreads on death. Nearby allies are healed for 20%.";
                default: return "Plant a war banner: nearby allies take 35% less damage and deal 20% more for 8 s.";
            }
        }

        public bool Ready => Charge.Value >= ChargeMax;

        // ---- CONVERGENCE (2026-11-22, "perma cook new things"; the user wants the classes to play well together) ----
        /// <summary>
        /// Two different players casting their ultimates within <see cref="ConvergeWindow"/> s and <see cref="ConvergeRange"/> m
        /// of each other CONVERGE: a burst at the point between them (<see cref="ConvergeRadius"/> m, damage scaling with depth)
        /// stuns every enemy for <see cref="ConvergeStun"/> s, and every player within 25 m gains Bloodlust for
        /// <see cref="ConvergeBloodlust"/> s. A cast converges once.
        /// </summary>
        public const float ConvergeWindow = 4f, ConvergeRange = 20f, ConvergeRadius = 9f, ConvergeStun = 1.5f, ConvergeBloodlust = 8f, ConvergeDamage = 45f;
        private struct UltCast { public ulong Who; public Vector3 At; public float Time; public byte Cls; public bool Used; public float Star; }
        private static readonly List<UltCast> recentCasts = new List<UltCast>();

        /// <summary>Server: note a cast; true (and the partner) if it converges with another player's recent one.</summary>
        public static bool ServerNoteCast(ulong who, Vector3 at, byte cls, float now, out Vector3 partnerAt, out byte partnerCls) =>
            ServerNoteCast(who, at, cls, now, 0f, out partnerAt, out partnerCls, out _);

        /// <summary><paramref name="star"/>: this caster's Convergent Star power (0 without it); the stronger of the pair is returned.</summary>
        public static bool ServerNoteCast(ulong who, Vector3 at, byte cls, float now, float star, out Vector3 partnerAt, out byte partnerCls, out float pairStar)
        {
            partnerAt = at; partnerCls = cls; pairStar = star;
            recentCasts.RemoveAll(c => now - c.Time > ConvergeWindow);
            int found = -1;
            for (int i = recentCasts.Count - 1; i >= 0; i--)
            {
                var c = recentCasts[i];
                if (c.Used || c.Who == who || Vector3.Distance(c.At, at) > ConvergeRange) continue;
                found = i; break;
            }
            if (found >= 0)
            {
                var c = recentCasts[found];
                c.Used = true; recentCasts[found] = c;
                partnerAt = c.At; partnerCls = c.Cls; pairStar = Mathf.Max(star, c.Star);
            }
            recentCasts.Add(new UltCast { Who = who, At = at, Time = now, Cls = cls, Used = found >= 0, Star = star });
            return found >= 0;
        }

        private void ServerConverge(Vector3 partnerAt, byte partnerCls, float star = 0f)
        {
            // Convergent Star: +50% radius and stun (scaled by its tier) when either caster carries it.
            float grow = 1f + 0.5f * star;
            var mid = (transform.position + partnerAt) * 0.5f;
            if (UnityEngine.AI.NavMesh.SamplePosition(mid, out var nh, 3f, UnityEngine.AI.NavMesh.AllAreas)) mid = nh.position;
            int depth = DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0;
            float dmg = ConvergeDamage * (1f + 0.6f * DungeonDirector.Tier(depth));
            AbilityKit.Blast(mid, ConvergeRadius * grow, dmg, NetworkObjectId, "Convergence", h => AbilityKit.Stun(h, ConvergeStun * grow));
            double until = NetworkManager.ServerTime.Time + ConvergeBloodlust;
            foreach (var p in PlayerLife.All)
                if (p != null && !p.Downed.Value && Vector3.Distance(p.transform.position, mid) < 25f && p.TryGetComponent<PlayerProgress>(out var pp))
                    pp.BloodlustEnds.Value = System.Math.Max(pp.BloodlustEnds.Value, until);
            Combos.Popup(life.Health, ComboKind.Convergence, progress);
            ConvergenceFxRpc(mid, (byte)progress.ClassId.Value, partnerCls, ConvergeRadius * grow);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ConvergenceFxRpc(Vector3 at, byte clsA, byte clsB, float radius)
        {
            Sfx.Play(Sound.Toll, at + Vector3.up, 0.9f, 1.3f);
            Sfx.Play(Sound.Slam, at, 0.8f, 0.7f);
            ClassFx.Convergence(Vfx.Floor(at), UI.CharacterSelect.Accent((ClassId)clsA), UI.CharacterSelect.Accent((ClassId)clsB), radius);
            if (NetworkPlayer.Local != null && Vector3.Distance(NetworkPlayer.Local.transform.position, at) < 25f) NetworkPlayer.Local.AddShake(0.35f);
        }

        /// <summary>Dev menu 'ult' (host, 2026-11-22, the user: "a command for full ultimate, let me test them in spawn"):
        /// the bar refills itself and ultimates cast in camp too.</summary>
        public static bool DevInfinite;
        private float devCastAt = -99f;

        /// <summary>Tests: fill the bar and cast at a point (owner only).</summary>
        public void DebugCast(Vector3 aim, ulong targetId = ulong.MaxValue)
        {
            if (IsServer) Charge.Value = ChargeMax;
            CastRpc(aim, targetId);
        }

        private void Awake()
        {
            progress = GetComponent<PlayerProgress>();
            life = GetComponent<PlayerLife>();
            player = GetComponent<NetworkPlayer>();
            motor = GetComponent<FirstPersonMotor>();
            health = GetComponent<Health>();
        }

        private static bool hooked;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            if (!hooked)
            {
                hooked = true;
                Health.ServerAnyDamaged += OnServerAnyDamaged;
            }
            health.IncomingModifiers.Add((dmg, from) => InBanner(transform.position) ? dmg * 0.65f : dmg);
        }

        /// <summary>Server: who charges from a hit — the attacker (dealt) and a player victim (taken).</summary>
        private static void OnServerAnyDamaged(Health target, float dealt, ulong attackerId)
        {
            if (target.Team == Team.Players)
            {
                if (target.TryGetComponent<Ultimates>(out var victim)) victim.Add(dealt * PerDamageTaken / DepthScaleDamage);
                return;
            }
            if (target.Team != Team.Enemies) return;
            var net = NetworkManager.Singleton;
            if (net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj)) return;
            if (obj.TryGetComponent<Ultimates>(out var u)) u.Add(dealt * PerDamageDealt / DepthScaleHealth);
        }

        /// <summary>Server: charge from elsewhere (grazes).</summary>
        public void ServerAddCharge(float amount) { if (IsServer) Add(amount); }

        private void Add(float amount)
        {
            if (DungeonDirector.InLobby || Time.time < lockedUntil) return;
            if (progress.HasRelic(Relic.WarlordsHorn)) amount *= 1f + 0.5f * progress.RelicPower(Relic.WarlordsHorn);
            if (progress.HasRelic(Relic.EchoStone)) amount *= 1f + 0.2f * progress.RelicPower(Relic.EchoStone);
            if (progress.Wears(GearId.RosaryOfAsh)) amount *= 1.35f;
            amount *= DungeonDirector.OmenUltCharge * DungeonDirector.EventUltCharge;
            // The Rogue's Death From Shadow fills faster (2026-11-12, the user): an assassin lives on the opening.
            if (progress.ClassId.Value == ClassId.Rogue) amount *= RogueChargeScale;
            Charge.Value = Mathf.Min(ChargeMax, Charge.Value + amount);
        }

        /// <summary>Server: inside any live Rallying Banner?</summary>
        public static bool InBanner(Vector3 at)
        {
            for (int i = banners.Count - 1; i >= 0; i--)
            {
                if (Time.time > banners[i].Until) { banners.RemoveAt(i); continue; }
                var d = at - banners[i].At;
                d.y = 0f;
                if (d.magnitude <= BannerRadius) return true;
            }
            return false;
        }

        /// <summary>Server: the banner's +20% for allies fighting beside it (fed into the outgoing chain).</summary>
        public static float OutgoingMultiplier(PlayerProgress attacker) => InBanner(attacker.transform.position) ? 1.2f : 1f;

        private void Update()
        {
            if (IsServer && !DungeonDirector.InLobby && life.Health.IsAlive && !life.Downed.Value && DungeonDirector.Instance != null)
                Add(PerSecond * Time.deltaTime);
            if (IsServer && DevInfinite) { if (Charge.Value < ChargeMax && Time.time >= devCastAt + 1.5f) Charge.Value = ChargeMax; }
            else if (IsServer && DungeonDirector.InLobby && Charge.Value > 0f && DungeonDirector.Instance != null && !DungeonDirector.Instance.RunInProgress)
                Charge.Value = 0f;

            if (!IsOwner) return;
            var keyboard = Keyboard.current;
            if (!UI.Binds.Down(UI.Bind.Ultimate) || !Ready || !life.CanAct || !motor.ActionsReady || Time.time - lastCast < 1f) return;
            if (DungeonDirector.InLobby && !DevInfinite) return;
            lastCast = Time.time;

            var cls = progress.ClassId.Value;
            var aim = AimGround();
            if (cls == ClassId.Rogue)
            {
                // Pick the enemy under the crosshair and step behind it, here, on the owner.
                var target = AimedEnemy();
                if (target == null) { GameHudFeed("No target in reach"); lastCast = 0f; return; }
                var behind = target.transform.position - target.transform.forward * 1.3f;
                if (UnityEngine.AI.NavMesh.SamplePosition(behind, out var hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) behind = hit.position;
                var look = target.transform.position - behind;
                look.y = 0f;
                motor.Teleport(behind + Vector3.up * 0.05f, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : transform.forward));
                CastRpc(target.transform.position, target.NetworkObjectId);
            }
            else CastRpc(aim, ulong.MaxValue);
            player.ViewModelCast(cls == ClassId.Mage);
        }

        private static void GameHudFeed(string text) => UI.GameHud.Feed(text, new Color(0.8f, 0.75f, 0.7f));

        private Vector3 AimGround()
        {
            if (player.AimRay(30f, out var hit)) return hit.point;
            return player.TryAimFloor(30f, out var spot) ? spot : transform.position + transform.forward * 6f;
        }

        private Health AimedEnemy()
        {
            Health best = null;
            float bestScore = float.MaxValue;
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                var to = h.Center - player.AimOrigin;
                float d = to.magnitude;
                if (d > 18f) continue;
                float angle = Vector3.Angle(player.AimForward, to);
                if (angle > 14f) continue;
                if (!CombatQuery.ClearLine(player.AimOrigin, h)) continue;
                float score = angle + d * 0.4f;
                if (score < bestScore) { bestScore = score; best = h; }
            }
            return best;
        }

        // ==================================================================
        // Server
        // ==================================================================

        /// <summary>Server, tests and the gallery: cast now, charged or not.</summary>
        public void ServerDebugCast(Vector3 aim)
        {
            Charge.Value = ChargeMax;
            lockedUntil = 0f;
            CastRpc(aim, ulong.MaxValue);
        }

        /// <summary>Server, the gallery: cast at a chosen target (the Rogue's Death From Shadow needs one).</summary>
        public void ServerDebugCast(Vector3 aim, Health target)
        {
            Charge.Value = ChargeMax;
            lockedUntil = 0f;
            CastRpc(aim, target != null ? target.NetworkObjectId : ulong.MaxValue);
        }

        [Rpc(SendTo.Server)]
        private void CastRpc(Vector3 aim, ulong targetId)
        {
            if (!Ready || !life.CanAct) return;
            if (Vector3.Distance(aim, transform.position) > 34f) aim = transform.position + transform.forward * 4f;
            Charge.Value = 0f;
            devCastAt = Time.time;
            lockedUntil = Time.time + (progress.ClassId.Value == ClassId.Rogue ? RogueLockAfterCast : LockAfterCast);
            var cls = progress.ClassId.Value;
            if (progress.HasRelic(Relic.WarlordsHorn))
                foreach (var ally in CombatQuery.Circle(transform.position, 12f, Team.Players).ToArray())
                    ally.ServerHeal(ally.Max.Value * 0.25f * progress.RelicPower(Relic.WarlordsHorn));
            float att = progress.AttackMultiplier;
            switch (cls)
            {
                case ClassId.Mage: StartCoroutine(Cataclysm(aim, att)); break;
                case ClassId.Cleric: Hymn(); break;
                case ClassId.Rogue: DeathFromShadow(targetId, att); break;
                case ClassId.Ranger: StartCoroutine(TwinWyrms(aim, att)); break;
                case ClassId.Flagellant: GetComponent<FlagellantKit>().ServerLitany(); break;
                case ClassId.PlagueDoctor: GetComponent<PlagueDoctorKit>().ServerBlackDeath(aim); break;
                default:
                    var at = transform.position;
                    banners.Add(new Banner { At = at, Until = Time.time + 8f });
                    BannerFxRpc(at);
                    break;
            }
            CastFxRpc((byte)cls);
            float myStar = progress.HasRelic(Relic.ConvergentStar) ? progress.RelicPower(Relic.ConvergentStar) : 0f;
            if (ServerNoteCast(OwnerClientId, transform.position, (byte)cls, Time.time, myStar, out var partnerAt, out var partnerCls, out var pairStar)) ServerConverge(partnerAt, partnerCls, pairStar);
        }

        private IEnumerator Cataclysm(Vector3 center, float att)
        {
            var spots = new List<Vector3> { center };
            for (int i = 0; i < 4; i++)
            {
                var p = center + Quaternion.Euler(0f, i * 90f + Random.Range(-25f, 25f), 0f) * Vector3.forward * Random.Range(2.5f, 4.5f);
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) p = hit.position;
                spots.Add(p);
            }
            for (int i = 0; i < spots.Count; i++)
            {
                MeteorWarnRpc(spots[i], i);
                yield return new WaitForSeconds(0.4f);
            }
            yield return new WaitForSeconds(MageKit.MeteorDelay - 0.4f);
            for (int i = 0; i < spots.Count; i++)
            {
                foreach (var foe in CombatQuery.Circle(spots[i], MageKit.MeteorRadius, Team.Enemies).ToArray())
                {
                    foe.Tag("Ultimates").ServerDamage(MageKit.MeteorDamage * 0.85f * att * Combos.TryShatter(foe, progress), spots[i], NetworkObjectId);
                    if (foe.TryGetComponent<ICrowdControl>(out var cc))
                    {
                        var push = foe.transform.position - spots[i];
                        push.y = 0f;
                        cc.ServerKnockback(push.normalized * 6f);
                    }
                }
                MeteorImpactRpc(spots[i]);
                yield return new WaitForSeconds(0.4f);
            }
        }

        private void Hymn()
        {
            foreach (var ally in PlayerLife.All)
            {
                if (ally == null) continue;
                bool near = Vector3.Distance(ally.transform.position, transform.position) <= 18f;
                if (ally.Downed.Value && near) ally.ServerRevive(0.7f);
                else if (ally.Health.IsAlive && ally.Health.Fraction < 0.7f) ally.Health.ServerSetFraction(0.7f);
                if (ally.Health.IsAlive) ally.Health.ServerShield(50f * progress.AttackMultiplier, 6f);
            }
        }

        private void DeathFromShadow(ulong targetId, float att)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var obj) || !obj.TryGetComponent<Health>(out var target) || !target.IsAlive) return;
            if (Vector3.Distance(target.transform.position, transform.position) > 24f) return;
            target.Tag("Ultimates").ServerDamage(150f * att * Combos.TryShatter(target, progress), target.transform.position - target.transform.forward, NetworkObjectId);
            if (target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(1.8f);
            StrikeFxRpc(target.Center);
        }

        // ---- Twin Wyrms ----
        public const float WyrmSpeed = 15f, WyrmRange = 44f, WyrmRadius = 2.3f, WyrmLength = 6f, WyrmTick = 0.2f, WyrmWindup = 0.35f;
        public const float WyrmBite = 55f, WyrmGnaw = 26f;

        private IEnumerator TwinWyrms(Vector3 aim, float att)
        {
            var dir = aim - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.25f) dir = transform.forward;
            dir.y = 0f;
            dir.Normalize();
            var origin = transform.position + Vector3.up * 1.15f + dir * 0.8f;
            WyrmFxRpc(origin, dir);
            yield return new WaitForSeconds(WyrmWindup);
            var bitten = new HashSet<Health>();
            float travelled = 0f;
            while (travelled < WyrmRange + WyrmLength)
            {
                travelled += WyrmSpeed * WyrmTick;
                float head = Mathf.Min(travelled, WyrmRange), tail = Mathf.Clamp(travelled - WyrmLength, 0f, WyrmRange);
                var a = origin + dir * tail;
                var b = origin + dir * head;
                foreach (var h in Health.All.ToArray())
                {
                    if (h == null || !h.IsAlive || h.Team != Team.Enemies) continue;
                    if (!h.SegmentTouchesBody(a, b, WyrmRadius)) continue;
                    // The first touch is the bite; while it is inside the coils it is gnawed every tick.
                    bool first = bitten.Add(h);
                    h.Tag("Ultimates").ServerDamage((first ? WyrmBite : WyrmGnaw) * att, b, NetworkObjectId);
                    if (h.IsAlive) h.ServerChill(2f);
                }
                yield return new WaitForSeconds(WyrmTick);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WyrmFxRpc(Vector3 origin, Vector3 dir)
        {
            var c = new Color(0.45f, 1f, 0.85f);
            SpiritWyrms.Spawn(origin, dir, WyrmWindup, WyrmSpeed, WyrmRange, WyrmLength, c);
            Sfx.Play(Sound.Bow, origin, 1f, 0.8f);
            Sfx.Play(Sound.Roar, origin + dir * 3f, 0.9f, 1.35f);
            Vfx.AbilityFlash(origin, c, 5f, 8f, 0.35f);
            if (IsOwner) player.AddShake(0.25f);
        }

        private IEnumerator RainOfArrows(Vector3 at, float att)
        {
            const float radius = 5f;
            RainWarnRpc(at, radius);
            for (int tick = 0; tick < 8; tick++)
            {
                yield return new WaitForSeconds(0.5f);
                foreach (var foe in CombatQuery.Circle(at, radius, Team.Enemies).ToArray())
                {
                    foe.Tag("Ultimates").ServerDamage(16f * att, at + Vector3.up * 6f, NetworkObjectId);
                    foe.ServerChill(1.5f);
                }
                RainTickRpc(at, radius);
            }
        }

        // ==================================================================
        // Effects
        // ==================================================================

        [Rpc(SendTo.ClientsAndHost)]
        private void CastFxRpc(byte cls)
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.7f);
            var c = UltColor((ClassId)cls);
            Sfx.Play(Sound.Relic, transform.position + Vector3.up * 1.5f, 1f, 0.8f);
            Vfx.AbilityFlash(transform.position + Vector3.up * 1.6f, c, 6f, 8f, 0.5f);
            Vfx.Ring(transform.position, 3f, c, 0.45f);
            // 2026-11-21: the Mage's and Ranger's ultimates are announced with a great rune circle and a pillar on the caster;
            // the others' ultimates are their own shapes (banner wall, halos, flails, the death mark, the veins: Art/ClassFx).
            if ((ClassId)cls == ClassId.Mage || (ClassId)cls == ClassId.Ranger)
            {
                AbilityFx.Cast(transform.position, player.FxHand, c, 2.2f, 1.2f);
                AbilityFx.Column(transform.position, 8f, c, 1.2f, 1.8f);
                AbilityFx.Spiral(transform.position, c, 1.2f, 3.5f, 20, 1.3f);
            }
            if ((ClassId)cls == ClassId.Cleric)
                foreach (var ally in PlayerLife.All)
                    if (ally != null && Vector3.Distance(ally.transform.position, transform.position) <= 18f)
                        ClassFx.HymnHalo(ally.transform, ally.Downed.Value);
            // Your own ultimate needs no feed line (it filled the feed); an ally's does.
            if (!IsOwner) UI.GameHud.Feed(name + ": " + Name((ClassId)cls).ToUpperInvariant() + "!", c);
        }

        public static Color UltColor(ClassId cls) =>
            cls == ClassId.Mage ? new Color(1f, 0.5f, 0.15f)
            : cls == ClassId.Cleric ? new Color(1f, 0.88f, 0.5f)
            : cls == ClassId.Rogue ? new Color(0.65f, 0.45f, 1f)
            : cls == ClassId.Ranger ? new Color(0.55f, 0.95f, 0.5f)
            : cls == ClassId.Flagellant ? new Color(0.9f, 0.15f, 0.12f)
            : cls == ClassId.PlagueDoctor ? new Color(0.45f, 0.9f, 0.25f)
            : new Color(0.95f, 0.3f, 0.2f);

        [Rpc(SendTo.ClientsAndHost)]
        private void BannerFxRpc(Vector3 at)
        {
            RallyBanner.Spawn(at, NetworkPlayer.SlotColors[(int)(OwnerClientId % 4)], 8f, BannerRadius);
            FxSprite.Decal("circle_02", at, BannerRadius * 1.9f, BannerRadius * 2f, 8f, new Color(1f, 0.8f, 0.4f, 0.35f), 10f);
            // The banner's ground walled in gold light for as long as it stands (stand inside it).
            ClassFx.KnightBanner(at, BannerRadius, 8f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MeteorWarnRpc(Vector3 at, int i)
        {
            Telegraph.Show(at, MageKit.MeteorRadius, MageKit.MeteorDelay + 0.4f, null, new Color(1f, 0.5f, 0.15f, 0.3f));
            MeteorFall.Spawn(at, MageKit.MeteorDelay + 0.4f * 0f, 1000 + i);
            // The rock lands when the ring closes (Cataclysm drops all five together, 2.9 s after the first warning).
            ClassFx.MeteorRing(at, MageKit.MeteorRadius, Mathf.Max(0.5f, 2.9f - 0.4f * i));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MeteorImpactRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 1f, 0.8f);
            MeteorImpact.Spawn(at, MageKit.MeteorRadius, false);
            ClassFx.Cracks(at, MageKit.MeteorRadius * 0.9f, 8, ClassFx.Fire, 4f);
            ClassFx.Rubble(Vfx.Floor(at), 8, 4f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 14f) local.AddShake(0.4f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StrikeFxRpc(Vector3 at)
        {
            var c = UltColor(ClassId.Rogue);
            Sfx.Play(Sound.Hit, at, 1f, 0.7f);
            Sfx.Play(Sound.Whisper, at, 0.8f, 0.6f);
            Vfx.AbilityFlash(at, c, 5f, 6f, 0.35f);
            // A star of three cuts and a pit of black closing on the mark.
            ClassFx.DeathMark(at);
            if (IsOwner) player.ConfirmHit(2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RainWarnRpc(Vector3 at, float radius)
        {
            Telegraph.Show(at, radius, 4.3f, null, new Color(0.55f, 0.95f, 0.5f, 0.22f));
            FxSprite.MagicCircle(at, radius * 0.7f, 4.3f, new Color(0.55f, 0.95f, 0.5f));
            Sfx.Play(Sound.Bow, at + Vector3.up * 3f, 1f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RainTickRpc(Vector3 at, float radius)
        {
            Sfx.Play(Sound.Bow, at + Vector3.up * 2f, 0.5f, Random.Range(1.1f, 1.4f));
            for (int i = 0; i < 7; i++)
            {
                var p = at + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0f, radius);
                Arrows.SpawnVisual(p + new Vector3(0.6f, 9f, 0.4f), Vector3.down + new Vector3(-0.06f, 0f, -0.04f), ProjectileKind.Arrow, 30f);
                if (i < 2) Vfx.Smoke(p, 1, 0.25f, 0.7f);
            }
        }
    }
}
