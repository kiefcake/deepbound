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
    /// <item><b>LMB — Firebolt.</b> Hold to fire fast, cheap bolts.</item>
    /// <item><b>RMB — Frostbolt.</b> A slower, costlier bolt that chills (slows) its target —
    ///   the setup for Shatter.</item>
    /// <item><b>Q — Blink.</b> Teleport a short way in the direction you are looking. The
    ///   Mage has no armour; this is its defence.</item>
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
        public const float FireboltInterval = 0.42f, FireboltDamage = 20f, FireboltSpeed = 32f, FireboltMana = 5f;
        // The frostbolt is a heavy, deliberate shot now: much harder hitting, on its own 3 s cooldown.
        public const float FrostboltInterval = 0.95f, FrostboltDamage = 34f, FrostboltSpeed = 26f, FrostboltMana = 14f, ChillSeconds = 4f, FrostboltCooldown = 3f;
        public const float BlinkCooldown = 8f, BlinkDistance = 7f;
        public const float MeteorCooldown = 14f, MeteorMana = 35f, MeteorDelay = 1.3f, MeteorRadius = 3.6f, MeteorDamage = 70f, MeteorRange = 30f;
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
        private float nextCast, blinkReadyAt, meteorReadyAt, frostReadyAt;
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
        private float ManaRegen => 4f + progress.Value(StatType.WIS) * 0.15f;

        // ---- IClassKit ----

        public ClassId Class => ClassId.Mage;
        public string ResourceName => "Mana";
        public Color ResourceColor => new Color(0.3f, 0.5f, 1f);
        public float Resource => Mana;
        public float ResourceMax => ManaMax;
        public void RestoreResource(float fraction) => Mana = Mathf.Min(ResourceMax, Mana + ResourceMax * fraction);

        public void FillAbilities(List<AbilityView> into)
        {
            into.Add(new AbilityView { Key = "LMB", Name = "Firebolt", Unaffordable = Mana < FireboltMana });
            into.Add(new AbilityView { Key = "RMB", Name = "Frostbolt", Unaffordable = Mana < FrostboltMana, Remaining = frostReadyAt - Time.time, Cooldown = FrostboltCooldown * progress.AttackTimeMultiplier });
            into.Add(new AbilityView { Key = "Q", Name = "Blink", Remaining = blinkReadyAt - Time.time, Cooldown = BlinkCooldown * progress.CooldownFor(AbilitySlot.Q) });
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

            // --- Q: Blink ---
            if (keyboard != null && UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= blinkReadyAt && TryBlink())
            {
                blinkReadyAt = Time.time + BlinkCooldown * progress.CooldownFor(AbilitySlot.Q);
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

        /// <summary>
        /// Blink along the look direction, flattened, stopping short of walls, and only
        /// onto solid floor — never into a wall or off a ledge into nothing.
        /// </summary>
        private bool TryBlink()
        {
            var dir = player.AimForward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return false;
            dir.Normalize();

            var chest = transform.position + Vector3.up * 1.2f;
            float reach = BlinkDistance * progress.Potency(AbilitySlot.Q);
            float distance = reach;
            if (Physics.SphereCast(chest, 0.35f, dir, out var wall, reach, ~0, QueryTriggerInteraction.Ignore) &&
                wall.collider.GetComponentInParent<Health>() == null)
            {
                distance = Mathf.Max(0f, wall.distance - 0.4f);
            }
            if (distance < 1f) return false;

            var target = chest + dir * distance;
            if (!Physics.Raycast(target, Vector3.down, out var floor, 4f, ~0, QueryTriggerInteraction.Ignore)) return false;

            var from = transform.position;
            motor.Teleport(floor.point + Vector3.up * 0.05f, transform.rotation);
            BlinkFxRpc(from, transform.position);
            if (progress.Twist(AbilitySlot.Q)) FrostNovaRpc(from);
            return true;
        }

        private bool TryAimGround(out Vector3 spot)
        {
            spot = default;
            var origin = player.AimOrigin;
            var dir = player.AimForward;
            Vector3 point;
            float reach = MeteorAimRange * RangeMul;
            if (Physics.Raycast(origin, dir, out var hit, reach, ~0, QueryTriggerInteraction.Ignore)) point = hit.point;
            else point = origin + dir * reach;

            // Drop to the floor beneath wherever the crosshair landed (a wall, an enemy).
            if (!Physics.Raycast(point + Vector3.up * 0.5f - dir * 0.3f, Vector3.down, out var floor, 12f, ~0, QueryTriggerInteraction.Ignore)) return false;
            spot = floor.point;
            return true;
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
                        foreach (var other in CombatQuery.Circle(h.Center, burstRadius, Team.Enemies).ToArray())
                            if (other != h) other.ServerDamage(FireboltDamage * att * burstShare, h.Center, me);
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

        /// <summary>Rank 3 Blink: the air you left freezes.</summary>
        [Rpc(SendTo.Server)]
        private void FrostNovaRpc(Vector3 at)
        {
            if (!life.CanAct || Vector3.Distance(at, transform.position) > BlinkDistance * 1.5f + 2f) return;
            foreach (var h in CombatQuery.Circle(at, 3.5f, Team.Enemies).ToArray()) h.ServerChill(ChillSeconds);
            FrostNovaFxRpc(at);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FrostNovaFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Frost, at + Vector3.up, 1f, 0.8f);
            Vfx.IceSpikes(at, 3.5f);
            Vfx.Ring(at, 3.5f, new Color(0.5f, 0.85f, 1f), 0.4f);
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
        private void BlinkFxRpc(Vector3 from, Vector3 to)
        {
            var arcane = new Color(0.6f, 0.55f, 1f);
            // Departure: the mage folds into a violet implosion and leaves a fading afterimage.
            Sfx.Play(Sound.Summon, from + Vector3.up, 0.6f, 2.2f);
            Vfx.Flash(from + Vector3.up, arcane, 4f, 6f, 0.25f);
            Vfx.Ring(from, 1.6f, arcane, 0.35f);
            Vfx.Embers(from + Vector3.up, 16, arcane, 4f, 0.06f, 0.7f);
            Art.BlinkGhost.Spawn(from, transform.rotation, arcane);
            // The path: a streak of motes from there to here.
            var path = to - from;
            int steps = Mathf.Clamp(Mathf.RoundToInt(path.magnitude * 2f), 3, 18);
            for (int i = 1; i < steps; i++)
                Vfx.Embers(from + path * (i / (float)steps) + Vector3.up * Random.Range(0.6f, 1.6f), 1, arcane, 0.6f, 0.05f, 0.45f);
            // Arrival: a crack of light and a ring on the floor.
            Sfx.Play(Sound.Frost, to + Vector3.up, 0.5f, 1.8f);
            Vfx.Flash(to + Vector3.up, arcane, 5f, 7f, 0.3f);
            Vfx.Ring(to, 2f, arcane, 0.45f);
            Vfx.Embers(to + Vector3.up, 18, arcane, 5f, 0.06f, 0.7f);
            if (IsOwner) player.AddShake(0.12f);
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
