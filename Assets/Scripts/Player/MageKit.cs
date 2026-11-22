using System.Linq;
using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Core;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// The Mage's kit: set up, then cash in.
    ///
    /// <list type="bullet">
    /// <item><b>Passive — Shatter.</b> Fire that hits a chilled enemy deals +75% and
    ///   consumes the chill, with a glassy crack you learn to listen for.</item>
    /// <item><b>LMB — Firebolt.</b> Hold to fire fast bolts; free (no mana).</item>
    /// <item><b>RMB — Frostbolt.</b> A slower, costlier bolt that chills (slows) its target —
    ///   the setup for Shatter.</item>
    /// <item><b>Q — Tidal Wave.</b> A wave flows out in front of the Mage, washing the dead
    ///   back and stunning them for half a second (2026-11-01, replacing Blink). On a flooded
    ///   floor, or standing in water, it hits harder and stuns for longer.</item>
    /// <item><b>E — Meteor.</b> Call a meteor onto the spot under your crosshair. A blue
    ///   circle shows your party where it will land; then it hits everything inside, and
    ///   shatters every chilled enemy at once. The Mage's big moment.</item>
    /// </list>
    ///
    /// Mana is owner-side, like the Knight's stamina; every hit is resolved on the server.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class MageKit : NetworkBehaviour, IClassKit
    {
        // Mana costs up ~1.5x (play-test 2026-11-01: "casting is way too cheap on the resource bar").
        // Left click is free for every class (2026-11-01, the user): only the right click and abilities spend the bar.
        public const float FireboltInterval = 0.42f, FireboltDamage = 23f, FireboltSpeed = 32f, FireboltMana = 0f;
        // The frostbolt is a heavy, deliberate shot: buffed 2026-11-01 (34 -> 50, cooldown 3 -> 2.2 s, chill 4 -> 5 s).
        public const float FrostboltInterval = 0.95f, FrostboltDamage = 44f, FrostboltSpeed = 28f, FrostboltMana = 20f, ChillSeconds = 5f, FrostboltCooldown = 2.2f;
        /// <summary>Tidal Wave: a <see cref="WaveArc"/>-degree wedge rolling out <see cref="WaveLength"/> m over <see cref="WaveSeconds"/>.</summary>
        public const float WaveCooldown = 8f, WaveMana = 18f, WaveLength = 8f, WaveArc = 70f, WaveSeconds = 0.5f, WaveDamage = 20f, WaveStun = 0.5f, WavePush = 7f;
        /// <summary>On a flooded floor (or knee-deep in a cistern) the wave is heavier.</summary>
        public const float WaveWetDamage = 1.6f, WaveWetStun = 1f;
        public const float MeteorCooldown = 14f, MeteorMana = 50f, MeteorDelay = 1.3f, MeteorRadius = 3.6f, MeteorDamage = 60f, MeteorRange = 30f;
        public const float ShatterMultiplier = 1.75f;
        /// <summary>Base reach of the bolts and of Meteor's aim (x the anvil / Farsight range multiplier).</summary>
        public const float FireboltRange = 20f, FrostboltRange = 22f, MeteorAimRange = 24f;
        public float AttackRange => FireboltRange * (progress != null ? progress.RangeMultiplier : 1f);
        private float RangeMul => progress != null ? progress.RangeMultiplier : 1f;

        /// <summary>Cryomancer mastery deepens the Shatter.</summary>
        private float Shatter => ShatterMultiplier;

        // ---- Hobb's paths (2026-11-20): INFERNO breathes a flame stream, STORMCALLER calls forking lightning ----
        public const float FlameInterval = 0.15f, FlameRange = 6f, FlameArc = 40f, FlameShare = 0.36f, IgniteAfter = 1.1f;
        public const float StormShare = 1.1f, ForkRange = 6f, ForkShare = 0.6f, StormAim = 9f;
        private readonly Dictionary<Health, float> inFlame = new Dictionary<Health, float>();
        private readonly Dictionary<Health, float> lastFlamed = new Dictionary<Health, float>();
        private float lastServerFlame;

        private Health health;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;

        // Owner-side.
        public float Mana { get; private set; }
        private float nextCast, waveReadyAt, meteorReadyAt, frostReadyAt;
        private bool manaInitialised;

        // Server-side.
        private struct PendingMeteor { public Vector3 At, From; public float When, Radius, Scale; public bool Mini; }
        private readonly List<PendingMeteor> meteors = new List<PendingMeteor>();
        private float lastServerBolt;

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        private bool Active => progress.ClassId.Value == ClassId.Mage;
        private float ManaMax => progress.Value(StatType.MP);
        private float ManaRegen => 1.5f + progress.Value(StatType.WIS) * 0.09f; // 2026-11-12, the user: lower from the start (8.4/s -> 5.6/s at base WIS; Meditation matters)

        // ---- IClassKit ----

        public ClassId Class => ClassId.Mage;
        public string ResourceName => "Mana";
        public string ResourceHint => "Spent by Frostbolt and spells";
        public Color ResourceColor => new Color(0.3f, 0.5f, 1f);
        public float Resource => Mana;
        public float ResourceMax => ManaMax;
        public void RestoreResource(float fraction) => Mana = Mathf.Min(ResourceMax, Mana + ResourceMax * fraction);
        public void ShaveCooldowns(float seconds) { waveReadyAt -= seconds; meteorReadyAt -= seconds; }

        public void FillAbilities(List<AbilityView> into)
        {
            into.Add(new AbilityView { Key = "LMB", Name = "Firebolt", Unaffordable = Mana < FireboltMana });
            into.Add(new AbilityView { Key = "RMB", Name = "Frostbolt", Unaffordable = Mana < FrostboltMana, Remaining = frostReadyAt - Time.time, Cooldown = FrostboltCooldown * progress.AttackTimeMultiplier });
            into.Add(new AbilityView { Key = "Q", Name = "Tidal Wave", Remaining = waveReadyAt - Time.time, Cooldown = WaveCooldown * progress.CooldownFor(AbilitySlot.Q), Unaffordable = Mana < WaveMana });
            into.Add(new AbilityView { Key = "E", Name = "Meteor", Remaining = meteorReadyAt - Time.time, Cooldown = MeteorCooldown * progress.CooldownFor(AbilitySlot.E), Unaffordable = Mana < MeteorMana });
        }

        // ==================================================================
        // Owner
        // ==================================================================

        private void Update()
        {
            if (IsServer) ResolveMeteors();
            if (!IsOwner || !Active) return;

            if (!manaInitialised) { Mana = ManaMax; manaInitialised = true; }
            Mana = Mathf.Min(ManaMax, Mana + ManaRegen * Time.deltaTime);
            motor.SpeedMultiplier = progress.SpeedMultiplier;
            motor.CanSprint = true;

            bool canAct = life.CanAct && motor.ActionsReady;
            if (!canAct) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            float rate = progress.AttackTimeMultiplier;

            // --- LMB held: Firebolt ---
            if (UI.Binds.PrimaryHeld && Time.time >= nextCast && Mana >= FireboltMana && progress.OnPath(1))
            {
                // Inferno: a held stream of fire.
                nextCast = Time.time + FlameInterval * rate;
                if (Time.time - lastFlameAnim > 0.4f) { player.ViewModelCast(false); lastFlameAnim = Time.time; }
                FlameRpc(player.AimOrigin + player.AimForward * 0.5f, player.AimForward);
            }
            else if (UI.Binds.PrimaryHeld && Time.time >= nextCast && Mana >= FireboltMana)
            {
                Mana -= FireboltMana;
                nextCast = Time.time + FireboltInterval * rate;
                player.ViewModelCast(false);
                BoltRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward, false);
            }
            // --- RMB: Frostbolt ---
            else if (UI.Binds.SecondaryHeld && Time.time >= nextCast && Time.time >= frostReadyAt && Mana >= FrostboltMana)
            {
                Mana -= FrostboltMana;
                nextCast = Time.time + FrostboltInterval * rate;
                frostReadyAt = Time.time + FrostboltCooldown * rate;
                player.ViewModelCast(true);
                BoltRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward, true);
            }

            // --- Q: Tidal Wave ---
            if (UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= waveReadyAt && Mana >= WaveMana)
            {
                var dir = player.AimForward;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    Mana -= WaveMana;
                    waveReadyAt = Time.time + WaveCooldown * progress.CooldownFor(AbilitySlot.Q);
                    player.ViewModelCast(true);
                    player.AddShake(0.1f);
                    WaveRpc(dir.normalized);
                }
            }

            // --- E: Meteor at the crosshair ---
            if (UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= meteorReadyAt && Mana >= MeteorMana &&
                TryAimGround(out var spot))
            {
                Mana -= MeteorMana;
                meteorReadyAt = Time.time + MeteorCooldown * progress.CooldownFor(AbilitySlot.E);
                player.ViewModelCast(false);
                MeteorRpc(spot);
            }
        }

        private bool TryAimGround(out Vector3 spot) => player.TryAimFloor(MeteorAimRange * RangeMul, out spot);
        private float lastFlameAnim;

        [Rpc(SendTo.Server)]
        private void FlameRpc(Vector3 origin, Vector3 forward) => ServerFlame(origin, forward);

        /// <summary>Server: one breath of the Inferno's stream. Public for the smoke test.</summary>
        public void ServerFlame(Vector3 origin, Vector3 forward)
        {
            if (!life.CanAct || Time.time - lastServerFlame < FlameInterval * 0.6f) return;
            lastServerFlame = Time.time;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            float att = progress.AttackMultiplier;
            ulong me = NetworkObjectId;
            float range = FlameRange * RangeMul;
            foreach (var h in CombatQuery.Arc(origin, forward, range, FlameArc, Team.Enemies).ToArray())
            {
                if (!CombatQuery.ClearLine(origin, h)) continue;
                bool shatter = h.ServerConsumeChill();
                if (shatter) Combos.Popup(h, ComboKind.Shatter, progress);
                h.Tag(shatter ? "Flame shatter" : "Flame").ServerDamage(FireboltDamage * FlameShare * att * (shatter ? Shatter : 1f), transform.position, me);
                if (!h.IsAlive) continue;
                h.ServerScorch(2f, me);
                // Mastered: kept in the fire long enough, it catches and burns on.
                if (progress.Mastered(1))
                {
                    if (!lastFlamed.TryGetValue(h, out var last) || Time.time - last > 0.5f) inFlame[h] = Time.time;
                    lastFlamed[h] = Time.time;
                    if (Time.time - inFlame[h] >= IgniteAfter)
                    {
                        inFlame[h] = Time.time + 3f;
                        AbilityKit.Bleed(this, h, FireboltDamage * 0.8f * att, 3f, me, "Burning");
                        progress.AbilityTextRpc(h.Center + Vector3.up, "ABLAZE", PlayerProgress.FxKind.Fire);
                    }
                }
            }
            if (inFlame.Count > 40) { inFlame.Clear(); lastFlamed.Clear(); }
            FlameFxRpc(origin, forward, range);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FlameFxRpc(Vector3 origin, Vector3 forward, float range)
        {
            var c = new Color(1f, 0.5f, 0.15f);
            for (int i = 0; i < 3; i++)
            {
                var dir = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(-FlameArc * 0.4f, FlameArc * 0.4f), 0f) * forward;
                FxSprite.Billboard(i == 0 ? "flame_03" : "fire_01", origin + forward * 0.3f, dir * range * 2.2f, 0.45f, 0.25f, 1.4f, new Color(1f, 0.6f, 0.25f, 0.85f), 90f, 1.5f);
            }
            Vfx.Embers(origin + forward * range * 0.4f, 3, c, 4f, 0.06f, 0.4f);
            if (Time.time - lastFlameSound > 0.3f) { lastFlameSound = Time.time; Sfx.Play(Sound.Fire, origin + forward * 2f, 0.5f, 0.8f); }
            if (!IsOwner && Random.value < 0.3f) player.Rig?.Play(RigAction.Cast, 0.25f);
        }
        private float lastFlameSound;

        /// <summary>Server: Stormcaller: the bolt strikes at once and forks. Public for the smoke test.</summary>
        public void ServerStorm(Vector3 origin, Vector3 direction)
        {
            float att = progress.AttackMultiplier;
            ulong me = NetworkObjectId;
            float range = FireboltRange * RangeMul;
            Health first = null;
            float bestAngle = StormAim;
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                var to = h.Center - origin;
                if (to.magnitude > range) continue;
                float a = Vector3.Angle(direction, to);
                if (a < bestAngle && CombatQuery.ClearLine(origin, h)) { bestAngle = a; first = h; }
            }
            if (first == null)
            {
                var end = Physics.Raycast(origin, direction, out var wall, range, ~0, QueryTriggerInteraction.Ignore) ? wall.point : origin + direction * range;
                progress.AbilityLinkRpc(origin, end, PlayerProgress.FxKind.Storm);
                return;
            }
            bool master = progress.Mastered(2);
            var struck = new HashSet<Health>();
            var from = origin;
            var target = first;
            for (int i = 0; i <= (master ? 4 : 2) && target != null; i++)
            {
                struck.Add(target);
                bool shatter = target.ServerConsumeChill();
                if (shatter) Combos.Popup(target, ComboKind.Shatter, progress);
                target.Tag(i == 0 ? "Lightning" : "Fork").ServerDamage(FireboltDamage * att * (i == 0 ? StormShare : ForkShare) * (shatter ? Shatter : 1f), transform.position, me);
                if (master) AbilityKit.Stun(target, 0.15f);
                progress.AbilityLinkRpc(from, target.Center, PlayerProgress.FxKind.Storm);
                from = target.Center;
                target = AbilityKit.Nearest(target.transform.position, ForkRange, struck);
            }
            ImpactFxRpc(first.Center, true, false);
        }

        // ==================================================================
        // Server
        // ==================================================================

        [Rpc(SendTo.Server)]
        private void BoltRpc(Vector3 origin, Vector3 direction, bool frost)
        {
            if (!life.CanAct) return;
            if (Time.time - lastServerBolt < FireboltInterval * 0.5f) return;
            lastServerBolt = Time.time;
            ServerBolt(origin, direction, frost);
        }

        /// <summary>Server: launch a bolt. Public for the smoke test, which fires without input.</summary>
        public void ServerBolt(Vector3 origin, Vector3 direction, bool frost)
        {
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;

            float att = progress.AttackMultiplier;
            ulong me = NetworkObjectId;
            // Captured by value: the bolt may land after the caster has left the session.
            var casterPos = transform.position;
            if (frost)
            {
                Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, FrostboltSpeed, h =>
                {
                    h.Tag("Frostbolt").ServerDamage(FrostboltDamage * att, casterPos, me);
                    h.ServerChill(ChillSeconds);
                    if (this != null) ImpactFxRpc(h.Center, true, false);
                }, false, FrostboltRange * RangeMul);
            }
            else if (progress.OnPath(2))
            {
                ServerStorm(origin, direction);
                return;
            }
            else
            {
                const bool pyro = false;
                float burstRadius = 2f, burstShare = 0.5f;
                float shatterMul = Shatter;
                Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, FireboltSpeed, h =>
                {
                    bool shatter = h.ServerConsumeChill();
                    if (shatter && this != null) Combos.Popup(h, ComboKind.Shatter, progress);
                    h.Tag(shatter ? "Firebolt shatter" : "Firebolt").ServerDamage(FireboltDamage * att * (shatter ? shatterMul : 1f), casterPos, me);
                    // Scalding Steam (Tidal Wave, rank 5 B): fire on a soaked foe bursts into steam.
                    if (this != null && soaked.TryGetValue(h, out var wetUntil) && Time.time < wetUntil)
                    {
                        soaked.Remove(h);
                        AbilityKit.Blast(h.Center, SteamRadius, FireboltDamage * att * SteamShare, me, "Steam", s => s.ServerScorch(3f, me));
                        progress.AbilityBurstRpc(h.Center, SteamRadius, PlayerProgress.FxKind.Water);
                        progress.AbilityTextRpc(h.Center + Vector3.up, "STEAM", PlayerProgress.FxKind.Frost);
                    }
                    if (pyro)
                    {
                        // Pyromancer fire scorches: the next frost to touch it sets off a Thermal Shock.
                        if (h.IsAlive) h.ServerScorch(4f, me);
                        foreach (var other in CombatQuery.Circle(h.Center, burstRadius, Team.Enemies).ToArray())
                            if (other != h) { other.Tag("Firebolt burst").ServerDamage(FireboltDamage * att * burstShare, h.Center, me); other.ServerScorch(4f, me); }
                    }
                    if (this != null) ImpactFxRpc(h.Center, false, shatter || pyro);
                }, false, FireboltRange * RangeMul);
            }
            BoltFxRpc(origin, direction, frost);
        }

        [Rpc(SendTo.Server)]
        private void MeteorRpc(Vector3 spot) => ServerMeteor(spot);

        /// <summary>Server: call down a meteor. Public for the screenshot run.</summary>
        public void ServerMeteor(Vector3 spot)
        {
            if (!life.CanAct || Vector3.Distance(spot, transform.position) > MeteorAimRange * RangeMul + 3f) return;
            var from = transform.position;
            meteors.Add(new PendingMeteor { At = spot, From = from, When = Time.time + MeteorDelay, Radius = MeteorRadius, Scale = 1f });
            // Twin Star (rank 3 A): a second stone follows the first.
            bool twin = progress.A(AbilitySlot.E, 2);
            if (twin) meteors.Add(new PendingMeteor { At = spot, From = from, When = Time.time + MeteorDelay + 0.7f, Radius = MeteorRadius, Scale = 1f });
            MeteorWarnRpc(spot, twin);
            // Comet Trail (rank 2 A): the furrow it will plough shows from the start.
            if (progress.A(AbilitySlot.E, 1)) progress.AbilityLaneRpc(Vfx.Floor(from), spot - from, Flat(spot - from).magnitude, 2f, MeteorDelay, PlayerProgress.FxKind.Fire);
            // Gravity Well (rank 2 B): just before it lands, it drags everything in.
            if (progress.B(AbilitySlot.E, 1))
                AbilityKit.Later(this, MeteorDelay - 0.45f, () =>
                {
                    foreach (var h in CombatQuery.Circle(spot, GravityRadius, Team.Enemies).ToArray())
                        if (!AbilityKit.IsBoss(h)) AbilityKit.PullTo(h, spot, 1.1f);
                    progress.AbilityBurstRpc(spot, GravityRadius * 0.5f, PlayerProgress.FxKind.Shadow);
                });
        }

        [Rpc(SendTo.Server)]
        private void WaveRpc(Vector3 direction) => ServerWave(direction);

        /// <summary>Is the Mage on a flooded floor, or standing in water? The wave is heavier there.</summary>
        public bool WaveWet
        {
            get
            {
                var layout = World.DungeonDirector.Layout;
                if (layout == null || World.DungeonDirector.InLobby) return false;
                return World.DungeonTheme.ForDepth(layout.Depth).WaterDepth > 0f || World.DungeonDirector.InWater(transform.position);
            }
        }

        /// <summary>Server: roll a Tidal Wave out along <paramref name="direction"/>. Public for the smoke test.</summary>
        public void ServerWave(Vector3 direction)
        {
            if (!life.CanAct) return;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;
            direction.Normalize();
            bool wet = WaveWet;
            float length = WaveLength * RangeMul;
            // Maelstrom Ring (rank 4 A): all round you, a little shorter.
            bool ring = progress.A(AbilitySlot.Q, 3);
            if (ring) length *= RingShare;
            WaveFxRpc(transform.position, direction, wet, length, ring);
            StartCoroutine(RollWave(transform.position, direction, wet, length, ring, true));
            // Second Swell (rank 3 B): another wave just after.
            if (progress.B(AbilitySlot.Q, 2))
                AbilityKit.Later(this, SwellDelay, () =>
                {
                    if (!health.IsAlive) return;
                    WaveFxRpc(transform.position, direction, wet, length, ring);
                    StartCoroutine(RollWave(transform.position, direction, wet, length, ring, false));
                });
        }

        // ---- Ysolde's options for Tidal Wave and Meteor (2026-11-20) ----
        public const float RingShare = 0.7f, SwellDelay = 0.6f, FloodSeconds = 5f, WhirlSeconds = 3f, WhirlRadius = 4.5f, SoakSeconds = 4f;
        public const float SteamRadius = 2.5f, SteamShare = 0.8f, GravityRadius = 7f, CraterSeconds = 5f, AftermathDelay = 2f;
        private readonly Dictionary<Health, float> soaked = new Dictionary<Health, float>();
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

        /// <summary>Server: Floodwater: the strip the wave left chills whoever wades in it.</summary>
        private System.Collections.IEnumerator Flood(Vector3 from, Vector3 dir, float length)
        {
            float end = Time.time + FloodSeconds;
            while (Time.time < end)
            {
                foreach (var h in AbilityKit.Lane(from, dir, length, 1.6f)) h.ServerChill(1.5f);
                yield return new WaitForSeconds(0.5f);
            }
        }

        /// <summary>Server: Whirlpool: drag everything near into the eye, then a last surge.</summary>
        private System.Collections.IEnumerator Whirl(Vector3 at)
        {
            progress.AbilityCircleRpc(at, WhirlRadius, WhirlSeconds, PlayerProgress.FxKind.Water);
            float end = Time.time + WhirlSeconds;
            while (Time.time < end)
            {
                foreach (var h in CombatQuery.Circle(at, WhirlRadius + 1f, Team.Enemies).ToArray())
                    if (!AbilityKit.IsBoss(h)) AbilityKit.PullTo(h, at, 0.5f);
                progress.AbilityBurstRpc(at, 1.2f, PlayerProgress.FxKind.Water);
                yield return new WaitForSeconds(0.5f);
            }
            AbilityKit.Blast(at, WhirlRadius * 0.6f, WaveDamage * progress.AttackMultiplier, NetworkObjectId, "Whirlpool", h => AbilityKit.Stun(h, 1f));
            progress.AbilityBurstRpc(at, WhirlRadius, PlayerProgress.FxKind.Water);
        }

        /// <summary>Server: Molten Crater: the hole burns.</summary>
        private System.Collections.IEnumerator Crater(Vector3 at, float radius)
        {
            progress.AbilityCircleRpc(at, radius, CraterSeconds, PlayerProgress.FxKind.Fire);
            float end = Time.time + CraterSeconds;
            while (Time.time < end)
            {
                yield return new WaitForSeconds(0.5f);
                AbilityKit.Blast(at, radius, 9f * progress.AttackMultiplier * 0.5f, NetworkObjectId, "Molten Crater", h => h.ServerScorch(2f, NetworkObjectId));
                progress.AbilityBurstRpc(at + Random.insideUnitSphere * radius * 0.5f, 0.4f, PlayerProgress.FxKind.Fire);
            }
        }

        /// <summary>Server: a small star (Starshower, Chain of Stars).</summary>
        private void MiniMeteor(Vector3 at, float delay)
        {
            meteors.Add(new PendingMeteor { At = at, From = at, When = Time.time + delay, Radius = MeteorRadius * 0.5f, Scale = 0.45f, Mini = true });
            MiniWarnRpc(at, delay);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MiniWarnRpc(Vector3 at, float delay)
        {
            Telegraph.Show(at, MeteorRadius * 0.5f, delay, null, Telegraph.Friendly);
            MeteorFall.Spawn(at, delay, (int)(at.x * 31f + at.z * 17f));
        }

        /// <summary>Server: the crest sweeps outward; everything it reaches is struck once, stunned and pushed.</summary>
        private System.Collections.IEnumerator RollWave(Vector3 from, Vector3 dir, bool wet, float length, bool ring, bool first)
        {
            float damage = WaveDamage * progress.AttackMultiplier * progress.Potency(AbilitySlot.Q) * (wet ? WaveWetDamage : 1f);
            float stun = wet ? WaveWetStun : WaveStun;
            // Ice-Cold (rank 3 A): the wave chills what it hits (Shatter's setup).
            bool cold = progress.A(AbilitySlot.Q, 2);
            bool undertow = progress.A(AbilitySlot.Q, 1);
            bool soak = progress.B(AbilitySlot.Q, 4);
            var struck = new HashSet<Health>();
            float start = Time.time;
            ulong me = NetworkObjectId;
            while (true)
            {
                float k = Mathf.Clamp01((Time.time - start) / WaveSeconds);
                float front = Mathf.Max(1.5f, length * k);
                foreach (var h in CombatQuery.Arc(from + Vector3.up * 0.9f, dir, front, ring ? 360f : WaveArc, Team.Enemies).ToArray())
                {
                    if (h == null || !h.IsAlive || !struck.Add(h)) continue;
                    h.Tag("Tidal Wave").ServerDamage(damage, from, me);
                    if (cold) h.ServerChill(ChillSeconds);
                    if (soak) soaked[h] = Time.time + SoakSeconds;
                    if (h.TryGetComponent<ICrowdControl>(out var cc))
                    {
                        cc.ServerStun(stun);
                        var away = ring ? Flat(h.transform.position - from) : dir;
                        // Undertow (rank 2 A): dragged back toward you instead.
                        cc.ServerKnockback((undertow ? -away * 0.75f : away) * WavePush * (wet ? 1.3f : 1f));
                    }
                }
                if (k >= 1f) break;
                yield return null;
            }
            // Crushing Weight (rank 4 B): the more it caught, the harder each one is hit.
            if (progress.B(AbilitySlot.Q, 3) && struck.Count > 1)
            {
                float extra = damage * 0.25f * Mathf.Min(4, struck.Count - 1);
                foreach (var h in struck) if (h != null && h.IsAlive) h.Tag("Crushing Weight").ServerDamage(extra, from, me);
                progress.AbilityTextRpc(from + dir * (length * 0.5f) + Vector3.up * 2f, "CRUSHING x" + struck.Count, PlayerProgress.FxKind.Water);
            }
            if (!first) yield break;
            // Floodwater (rank 2 B): the strip stays wet and chills.
            if (progress.B(AbilitySlot.Q, 1) && !ring)
            {
                progress.AbilityLaneRpc(Vfx.Floor(from), dir, length, 3.2f, FloodSeconds, PlayerProgress.FxKind.Water);
                StartCoroutine(Flood(from, dir, length));
            }
            // Whirlpool (rank 5 A): where it breaks, a whirlpool turns.
            if (progress.A(AbilitySlot.Q, 4)) StartCoroutine(Whirl(Vfx.Floor(ring ? from : from + dir * length * 0.85f)));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WaveFxRpc(Vector3 from, Vector3 dir, bool wet, float length, bool ring)
        {
            if (ring) for (int i = 0; i < 4; i++) TidalWave.Spawn(from, Quaternion.Euler(0f, i * 90f, 0f) * dir, length, 90f, WaveSeconds, wet);
            else TidalWave.Spawn(from, dir, length, WaveArc, WaveSeconds, wet);
            Sfx.Play(Sound.Splash, from + Vector3.up, 1f, 0.75f);
            Sfx.Play(Sound.Splash, from + dir * (length * 0.5f) + Vector3.up, 0.8f, 0.6f);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.4f);
        }

        private void ResolveMeteors()
        {
            for (int i = meteors.Count - 1; i >= 0; i--)
            {
                if (Time.time < meteors[i].When) continue;
                var m = meteors[i];
                var at = m.At;
                meteors.RemoveAt(i);

                int shattered = 0;
                float potency = progress.Potency(AbilitySlot.E);
                bool frost = progress.B(AbilitySlot.E, 3) && !m.Mini;
                var killed = new List<Health>();
                foreach (var h in CombatQuery.Circle(at, m.Radius, Team.Enemies).ToArray())
                {
                    bool shatter = h.ServerConsumeChill();
                    if (shatter)
                    {
                        shattered++;
                        Combos.Popup(h, ComboKind.Shatter, progress);
                    }
                    h.Tag(shatter ? "Meteor shatter" : m.Mini ? "Star" : "Meteor").ServerDamage(MeteorDamage * progress.AttackMultiplier * potency * m.Scale * (shatter ? Shatter : 1f), at + Vector3.up, NetworkObjectId);
                    if (!h.IsAlive) { killed.Add(h); continue; }
                    // Frostfall (rank 4 B): ice, not fire: frozen solid, then left chilled.
                    if (frost) { AbilityKit.Stun(h, 1.5f); h.ServerChill(ChillSeconds); }
                    else if (!shatter) h.ServerScorch(5f, NetworkObjectId);
                }
                MeteorImpactRpc(at, shattered > 0 || frost, m.Radius);
                if (frost) progress.AbilityBurstRpc(at, m.Radius, PlayerProgress.FxKind.Frost);
                if (!m.Mini)
                {
                    // Comet Trail (rank 2 A): the furrow burns.
                    if (progress.A(AbilitySlot.E, 1))
                    {
                        var dir = Flat(at - m.From);
                        float len = Flat(at - m.From).magnitude;
                        len = new Vector2(at.x - m.From.x, at.z - m.From.z).magnitude;
                        foreach (var h in AbilityKit.Lane(m.From, dir, len, 1f)) { h.Tag("Comet Trail").ServerDamage(MeteorDamage * progress.AttackMultiplier * potency * 0.45f, m.From, NetworkObjectId); h.ServerScorch(4f, NetworkObjectId); }
                        for (int k = 1; k <= 4; k++) progress.AbilityBurstRpc(Vector3.Lerp(m.From, at, k / 5f), 0.8f, PlayerProgress.FxKind.Fire);
                    }
                    // Molten Crater (rank 3 B).
                    if (progress.B(AbilitySlot.E, 2)) StartCoroutine(Crater(at, m.Radius * 0.8f));
                    // Starshower (rank 4 A): three small stars round it.
                    if (progress.A(AbilitySlot.E, 3))
                        for (int s = 0; s < 3; s++)
                        {
                            float ang = (s * 120f + Random.Range(-25f, 25f)) * Mathf.Deg2Rad;
                            MiniMeteor(Vfx.Floor(at + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * Random.Range(3f, 5f)), 0.6f + s * 0.2f);
                        }
                    // Chain of Stars (rank 5 A): each kill calls a star on the nearest other foe.
                    if (progress.A(AbilitySlot.E, 4))
                    {
                        var taken = new HashSet<Health>(killed);
                        for (int c = 0; c < Mathf.Min(3, killed.Count); c++)
                        {
                            var next = AbilityKit.Nearest(killed[c].transform.position, 10f, taken);
                            if (next == null) break;
                            taken.Add(next);
                            MiniMeteor(next.transform.position, 0.7f + c * 0.15f);
                        }
                    }
                    // Aftermath (rank 5 B): it erupts again, wider.
                    if (progress.B(AbilitySlot.E, 4))
                    {
                        float r2 = m.Radius * 1.5f;
                        progress.AbilityCircleRpc(at, r2, AftermathDelay, PlayerProgress.FxKind.Fire);
                        AbilityKit.Later(this, AftermathDelay, () =>
                        {
                            AbilityKit.Blast(at, r2, MeteorDamage * progress.AttackMultiplier * potency * 0.7f, NetworkObjectId, "Aftermath", h => h.ServerScorch(4f, NetworkObjectId));
                            MeteorImpactRpc(at, false, r2);
                        });
                    }
                }

                // Hallowed Fire: a meteor into consecrated ground mends everyone standing on it.
                if (ClericKit.InConsecration(at))
                {
                    Health first = null;
                    foreach (var ally in CombatQuery.Circle(at, MeteorRadius + 1f, Team.Players).ToArray())
                    {
                        ally.ServerHeal(ally.Max.Value * 0.15f);
                        if (first == null) first = ally;
                    }
                    if (first != null) Combos.Popup(first, ComboKind.Hallowed, progress);
                }
            }
        }

        // ==================================================================
        // Presentation
        // ==================================================================

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltFxRpc(Vector3 origin, Vector3 direction, bool frost)
        {
            Arrows.SpawnVisual(origin, direction, frost ? ProjectileKind.Frostbolt : ProjectileKind.Firebolt, frost ? FrostboltSpeed : FireboltSpeed, (frost ? FrostboltRange : FireboltRange) * RangeMul);
            AbilityFx.Muzzle(player.FxHand, direction, frost ? new Color(0.55f, 0.85f, 1f) : new Color(1f, 0.5f, 0.15f), player.FxHandScale + (frost ? 0.3f : 0f));
            Sfx.Play(frost ? Sound.Frost : Sound.Fire, origin, 0.55f);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.25f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ImpactFxRpc(Vector3 at, bool frost, bool shatter)
        {
            if (shatter) Sfx.Play(Sound.Shatter, at, 1f);
            else Sfx.Play(frost ? Sound.Frost : Sound.Hit, at, 0.6f, frost ? 1.4f : 1.1f);
            var fire = new Color(1f, 0.5f, 0.15f);
            var ice = new Color(0.55f, 0.85f, 1f);
            Vfx.Embers(at, frost ? 6 : 8, frost ? ice : fire, 4f, 0.05f, 0.5f);
            if (!frost) Vfx.Fireball(at, shatter ? 1.4f : 0.8f, fire);
            if (shatter) Vfx.Embers(at, 14, ice, 7f, 0.08f, 0.8f);
            // Where a bolt lands it marks the floor: a rime crown and a snowflake for frost, a scorch for fire; a SHATTER
            // breaks like glass (2026-11-22, its own shape: the Mage's cash-in should be unmistakable).
            if (shatter) ClassFx.ShatterBurst(at);
            else if (frost) ClassFx.Snowflake(at, 0.9f);
            if (frost) Vfx.IceSpikes(Vfx.Floor(at), shatter ? 1.4f : 0.7f, shatter ? 12 : 6);
            else FxSprite.Scorch(at, shatter ? 0.8f : 0.45f);
            if (IsOwner) player.ConfirmHit(shatter ? 1.5f : 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MeteorWarnRpc(Vector3 at, bool twin)
        {
            Telegraph.Show(at, MeteorRadius, MeteorDelay, null, Telegraph.Friendly);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.6f);
            Sfx.Play(Sound.Fire, at + Vector3.up * 8f, 0.9f, 0.5f);
            int seed = (int)(at.x * 13f + at.z * 7f + Time.time * 100f);
            MeteorFall.Spawn(at, MeteorDelay, seed);
            if (twin) MeteorFall.Spawn(at, MeteorDelay, seed + 1, 0.7f);
            // The caster's hand flares as the sky answers.
            if (IsOwner) Vfx.AbilityFlash(player.AimOrigin + player.AimForward * 0.6f, new Color(1f, 0.5f, 0.15f), 4f, 4f, 0.5f);
            // 2026-11-22: a ring of fire closes on the blast radius as the rock falls: when it lands, not only where.
            ClassFx.MeteorRing(at, MeteorRadius, MeteorDelay);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MeteorImpactRpc(Vector3 at, bool shattered, float radius)
        {
            Sfx.Play(Sound.Slam, at, 1f, 0.8f);
            if (shattered) Sfx.Play(Sound.Shatter, at + Vector3.up, 1f);
            var local = NetworkPlayer.Local;
            if (local != null)
            {
                float d = Vector3.Distance(local.transform.position, at);
                if (d < 20f) local.AddShake(Mathf.Lerp(0.6f, 0.1f, d / 20f));
            }
            MeteorImpact.Spawn(at, radius, shattered);
            ClassFx.Cracks(at, radius * 0.9f, 8, shattered ? ClassFx.Ice : ClassFx.Fire, 4f);
            ClassFx.Rubble(Vfx.Floor(at), 8, 4f);
            if (shattered) ClassFx.ShatterBurst(at + Vector3.up);
        }
    }

}
