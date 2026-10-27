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
    /// <item><b>Ranger — Rain of Arrows.</b> A storm of arrows on the crosshair for 4 s:
    ///   damage every half second, and everything in it is chilled.</item>
    /// </list>
    ///
    /// <para>Charge is server-owned; the owner reads input and sends the aim.</para>
    /// </summary>
    public class Ultimates : NetworkBehaviour
    {
        public const float ChargeMax = 100f;
        // Damage is measured against the floor's own scale (deeper hits and health are bigger), taken counts for
        // less, and nothing charges for a while after a cast: on floor 6 the autoplay Knight cast every ~12 s.
        private const float PerDamageDealt = 0.055f, PerDamageTaken = 0.15f, PerSecond = 0.45f, LockAfterCast = 20f;
        private float lockedUntil;
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
                case ClassId.Ranger: return "Rain of Arrows";
                case ClassId.Flagellant: return "Martyrdom";
                case ClassId.PlagueDoctor: return "Black Death";
                default: return "Rallying Banner";
            }
        }

        public static string Describe(ClassId cls)
        {
            switch (cls)
            {
                case ClassId.Mage: return "Five meteors rain around the crosshair over two seconds.";
                case ClassId.Cleric: return "Raise every downed ally within 18 m; heal the party to at least 70% and shield it.";
                case ClassId.Rogue: return "Step behind the enemy you aim at and strike: a huge backstab that leaves it Exposed.";
                case ClassId.Ranger: return "A storm of arrows on the crosshair for 4 s, chilling all it hits.";
                case ClassId.Flagellant: return "For 8 s you cannot fall below 1 health, hit 30% harder and heal for a quarter of it. Then you mend 30%.";
                case ClassId.PlagueDoctor: return "A vast plague cloud at the crosshair for 8 s: everything inside rots and takes 25% more damage.";
                default: return "Plant a war banner: allies near it take 35% less damage and deal 20% more for 8 s.";
            }
        }

        public bool Ready => Charge.Value >= ChargeMax;

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
            amount *= DungeonDirector.OmenUltCharge;
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
            if (IsServer && DungeonDirector.InLobby && Charge.Value > 0f && DungeonDirector.Instance != null && !DungeonDirector.Instance.RunInProgress)
                Charge.Value = 0f;

            if (!IsOwner) return;
            var keyboard = Keyboard.current;
            if (keyboard == null || !UI.Binds.Down(UI.Bind.Ultimate) || !Ready || !life.CanAct || !motor.ActionsReady || Time.time - lastCast < 1f) return;
            if (DungeonDirector.InLobby) return;
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
            var origin = player.AimOrigin;
            var dir = player.AimForward;
            if (Physics.Raycast(origin, dir, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
            var far = origin + dir * 30f;
            return Physics.Raycast(far, Vector3.down, out var down, 40f, ~0, QueryTriggerInteraction.Ignore) ? down.point : transform.position;
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

        [Rpc(SendTo.Server)]
        private void CastRpc(Vector3 aim, ulong targetId)
        {
            if (!Ready || !life.CanAct) return;
            if (Vector3.Distance(aim, transform.position) > 34f) aim = transform.position + transform.forward * 4f;
            Charge.Value = 0f;
            lockedUntil = Time.time + LockAfterCast;
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
                case ClassId.Ranger: StartCoroutine(RainOfArrows(aim, att)); break;
                case ClassId.Flagellant: GetComponent<FlagellantKit>().ServerMartyrdom(8f); break;
                case ClassId.PlagueDoctor: GetComponent<PlagueDoctorKit>().ServerBlackDeath(aim); break;
                default:
                    var at = transform.position;
                    banners.Add(new Banner { At = at, Until = Time.time + 8f });
                    BannerFxRpc(at);
                    break;
            }
            CastFxRpc((byte)cls);
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
                    foe.ServerDamage(MageKit.MeteorDamage * 0.85f * att * Combos.TryShatter(foe, progress), spots[i], NetworkObjectId);
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
            target.ServerDamage(150f * att * Combos.TryShatter(target, progress), target.transform.position - target.transform.forward, NetworkObjectId);
            if (target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(1.8f);
            StrikeFxRpc(target.Center);
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
                    foe.ServerDamage(16f * att, at + Vector3.up * 6f, NetworkObjectId);
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
            Vfx.Flash(transform.position + Vector3.up * 1.6f, c, 6f, 8f, 0.5f);
            Vfx.Ring(transform.position, 3f, c, 0.45f);
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
        private void BannerFxRpc(Vector3 at) => RallyBanner.Spawn(at, NetworkPlayer.SlotColors[(int)(OwnerClientId % 4)], 8f, BannerRadius);

        [Rpc(SendTo.ClientsAndHost)]
        private void MeteorWarnRpc(Vector3 at, int i)
        {
            Telegraph.Show(at, MageKit.MeteorRadius, MageKit.MeteorDelay + 0.4f, null, new Color(1f, 0.5f, 0.15f, 0.3f));
            MeteorFall.Spawn(at, MageKit.MeteorDelay + 0.4f * 0f, 1000 + i);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MeteorImpactRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 1f, 0.8f);
            MeteorImpact.Spawn(at, MageKit.MeteorRadius, false);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 14f) local.AddShake(0.4f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StrikeFxRpc(Vector3 at)
        {
            var c = UltColor(ClassId.Rogue);
            Sfx.Play(Sound.Hit, at, 1f, 0.7f);
            Sfx.Play(Sound.Whisper, at, 0.8f, 0.6f);
            Vfx.Flash(at, c, 5f, 6f, 0.35f);
            Vfx.Embers(at, 18, c, 5f, 0.05f, 0.6f);
            if (IsOwner) player.ConfirmHit(2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RainWarnRpc(Vector3 at, float radius)
        {
            Telegraph.Show(at, radius, 4.3f, null, new Color(0.55f, 0.95f, 0.5f, 0.22f));
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
            }
        }
    }
}
