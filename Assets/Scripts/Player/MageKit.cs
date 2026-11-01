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
        public const float FrostboltInterval = 0.95f, FrostboltDamage = 50f, FrostboltSpeed = 28f, FrostboltMana = 20f, ChillSeconds = 5f, FrostboltCooldown = 2.2f;
        /// <summary>Tidal Wave: a <see cref="WaveArc"/>-degree wedge rolling out <see cref="WaveLength"/> m over <see cref="WaveSeconds"/>.</summary>
        public const float WaveCooldown = 8f, WaveMana = 18f, WaveLength = 8f, WaveArc = 70f, WaveSeconds = 0.5f, WaveDamage = 20f, WaveStun = 0.5f, WavePush = 7f;
        /// <summary>On a flooded floor (or knee-deep in a cistern) the wave is heavier.</summary>
        public const float WaveWetDamage = 1.6f, WaveWetStun = 1f;
        public const float MeteorCooldown = 14f, MeteorMana = 50f, MeteorDelay = 1.3f, MeteorRadius = 3.6f, MeteorDamage = 70f, MeteorRange = 30f;
        public const float ShatterMultiplier = 1.75f;
        /// <summary>Base reach of the bolts and of Meteor's aim (x the anvil / Farsight range multiplier).</summary>
        public const float FireboltRange = 20f, FrostboltRange = 22f, MeteorAimRange = 24f;
        public float AttackRange => FireboltRange * (progress != null ? progress.RangeMultiplier : 1f);
        private float RangeMul => progress != null ? progress.RangeMultiplier : 1f;

        /// <summary>Cryomancer mastery deepens the Shatter.</summary>
        private float Shatter => progress.Mastered(2) ? 2.5f : ShatterMultiplier;

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
        private struct PendingMeteor { public Vector3 At; public float When; }
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
        private float ManaRegen => 3f + progress.Value(StatType.WIS) * 0.12f;

        // ---- IClassKit ----

        public ClassId Class => ClassId.Mage;
        public string ResourceName => "Mana";
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
            if (UI.Binds.PrimaryHeld && Time.time >= nextCast && Mana >= FireboltMana)
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
            if (keyboard != null && UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= waveReadyAt && Mana >= WaveMana)
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
            if (keyboard != null && UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= meteorReadyAt && Mana >= MeteorMana &&
                TryAimGround(out var spot))
            {
                Mana -= MeteorMana;
                meteorReadyAt = Time.time + MeteorCooldown * progress.CooldownFor(AbilitySlot.E);
                player.ViewModelCast(false);
                MeteorRpc(spot);
            }
        }

        private bool TryAimGround(out Vector3 spot) => player.TryAimFloor(MeteorAimRange * RangeMul, out spot);

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
                // Cryomancer: the bolt pierces and chills longer.
                bool cryo = progress.OnPath(2);
                Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, FrostboltSpeed, h =>
                {
                    h.ServerDamage(FrostboltDamage * att, casterPos, me);
                    h.ServerChill(cryo ? 6f : ChillSeconds);
                    if (this != null) ImpactFxRpc(h.Center, true, false);
                }, cryo, FrostboltRange * RangeMul);
            }
            else
            {
                // Pyromancer: the bolt bursts, singeing everything around the target.
                bool pyro = progress.OnPath(1);
                float burstRadius = progress.Mastered(1) ? 3f : 2f, burstShare = progress.Mastered(1) ? 0.75f : 0.5f;
                float shatterMul = Shatter;
                Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, FireboltSpeed, h =>
                {
                    bool shatter = h.ServerConsumeChill();
                    if (shatter && this != null) Combos.Popup(h, ComboKind.Shatter, progress);
                    h.ServerDamage(FireboltDamage * att * (shatter ? shatterMul : 1f), casterPos, me);
                    if (pyro)
                    {
                        // Pyromancer fire scorches: the next frost to touch it sets off a Thermal Shock.
                        if (h.IsAlive) h.ServerScorch(4f, me);
                        foreach (var other in CombatQuery.Circle(h.Center, burstRadius, Team.Enemies).ToArray())
                            if (other != h) { other.ServerDamage(FireboltDamage * att * burstShare, h.Center, me); other.ServerScorch(4f, me); }
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
            meteors.Add(new PendingMeteor { At = spot, When = Time.time + MeteorDelay });
            // Rank 3: a second stone follows the first.
            bool twin = progress.Twist(AbilitySlot.E);
            if (twin) meteors.Add(new PendingMeteor { At = spot, When = Time.time + MeteorDelay + 0.7f });
            MeteorWarnRpc(spot, twin);
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
            float length = WaveLength * progress.Potency(AbilitySlot.Q) * RangeMul;
            WaveFxRpc(transform.position, direction, wet, length);
            StartCoroutine(RollWave(transform.position, direction, wet, length));
        }

        /// <summary>Server: the crest sweeps outward; everything it reaches is struck once, stunned and pushed.</summary>
        private System.Collections.IEnumerator RollWave(Vector3 from, Vector3 dir, bool wet, float length)
        {
            float damage = WaveDamage * progress.AttackMultiplier * progress.Potency(AbilitySlot.Q) * (wet ? WaveWetDamage : 1f);
            float stun = wet ? WaveWetStun : WaveStun;
            // Rank 3: the wave runs ice-cold and chills what it hits (Shatter's setup).
            bool cold = progress.Twist(AbilitySlot.Q);
            var struck = new HashSet<Health>();
            float start = Time.time;
            ulong me = NetworkObjectId;
            while (true)
            {
                float k = Mathf.Clamp01((Time.time - start) / WaveSeconds);
                float front = Mathf.Max(1.5f, length * k);
                foreach (var h in CombatQuery.Arc(from + Vector3.up * 0.9f, dir, front, WaveArc, Team.Enemies).ToArray())
                {
                    if (h == null || !h.IsAlive || !struck.Add(h)) continue;
                    h.ServerDamage(damage, from, me);
                    if (cold) h.ServerChill(ChillSeconds);
                    if (h.TryGetComponent<ICrowdControl>(out var cc))
                    {
                        cc.ServerStun(stun);
                        cc.ServerKnockback(dir * WavePush * (wet ? 1.3f : 1f));
                    }
                }
                if (k >= 1f) yield break;
                yield return null;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WaveFxRpc(Vector3 from, Vector3 dir, bool wet, float length)
        {
            TidalWave.Spawn(from, dir, length, WaveArc, WaveSeconds, wet);
            Sfx.Play(Sound.Splash, from + Vector3.up, 1f, 0.75f);
            Sfx.Play(Sound.Splash, from + dir * (length * 0.5f) + Vector3.up, 0.8f, 0.6f);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.4f);
        }

        private void ResolveMeteors()
        {
            for (int i = meteors.Count - 1; i >= 0; i--)
            {
                if (Time.time < meteors[i].When) continue;
                var at = meteors[i].At;
                meteors.RemoveAt(i);

                int shattered = 0;
                float potency = progress.Potency(AbilitySlot.E);
                foreach (var h in CombatQuery.Circle(at, MeteorRadius * (potency > 1f ? 1.15f : 1f), Team.Enemies).ToArray())
                {
                    bool shatter = h.ServerConsumeChill();
                    if (shatter)
                    {
                        shattered++;
                        Combos.Popup(h, ComboKind.Shatter, progress);
                    }
                    h.ServerDamage(MeteorDamage * progress.AttackMultiplier * potency * (shatter ? Shatter : 1f), at + Vector3.up, NetworkObjectId);
                    if (!shatter) h.ServerScorch(5f, NetworkObjectId);
                }
                MeteorImpactRpc(at, shattered > 0);

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
            if (IsOwner) Vfx.Flash(player.AimOrigin + player.AimForward * 0.6f, new Color(1f, 0.5f, 0.15f), 4f, 4f, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MeteorImpactRpc(Vector3 at, bool shattered)
        {
            Sfx.Play(Sound.Slam, at, 1f, 0.8f);
            if (shattered) Sfx.Play(Sound.Shatter, at + Vector3.up, 1f);
            var local = NetworkPlayer.Local;
            if (local != null)
            {
                float d = Vector3.Distance(local.transform.position, at);
                if (d < 20f) local.AddShake(Mathf.Lerp(0.6f, 0.1f, d / 20f));
            }
            MeteorImpact.Spawn(at, MeteorRadius, shattered);
        }
    }

}
