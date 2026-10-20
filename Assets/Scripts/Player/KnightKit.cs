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
    /// The Knight's kit. League-style: one passive, a basic attack, a defensive button and
    /// two abilities, each with one clear job.
    ///
    /// <list type="bullet">
    /// <item><b>Passive — Bulwark.</b> Blocking stops 80% of damage from the front. Every
    ///   blocked hit costs stamina; run out and the guard breaks.</item>
    /// <item><b>LMB — Sword swing.</b> Wind-up, then a 110° arc. Alternates sides.</item>
    /// <item><b>RMB — Block.</b> Hold. Halves movement speed.</item>
    /// <item><b>Q — Shield Bash.</b> Lunge forward; stuns and knocks back what you hit.
    ///   Interrupts wind-ups, including the boss's.</item>
    /// <item><b>E — War Cry.</b> Taunts every enemy nearby onto you and halves the damage you
    ///   take for a few seconds. The tank button.</item>
    /// </list>
    ///
    /// <para>
    /// Split by authority. The owner reads input, runs timers and stamina, and animates its
    /// own view model immediately (no input lag). The server resolves every hit and owns
    /// all damage; other peers just play the animation they are told to.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class KnightKit : NetworkBehaviour, IClassKit
    {
        // Timings in seconds. Wind-ups are short — the Knight is meant to feel responsive.
        public const float SwingWindup = 0.16f, SwingStrike = 0.14f, SwingRecovery = 0.3f;
        /// <summary>Rebalanced 2026-10-17: the tank no longer out-damages the damage classes (34 -> 29); it holds rage instead.</summary>
        public const float SwingRange = 2.7f, SwingArc = 110f, SwingDamage = 29f;
        public float AttackRange => SwingRange * (progress != null ? progress.ReachMultiplier : 1f);
        private float ReachMul => progress != null ? progress.ReachMultiplier : 1f;
        public const float BashCooldown = 7f, BashRange = 2.4f, BashDamage = 18f, BashStun = 1.6f;
        public const float WarCryCooldown = 16f, WarCryRadius = 12f, WarCryDuration = 5f;
        public const float StaminaMax = 100f;
        private const float SwingStamina = 14f, SprintStaminaPerSec = 12f, BlockedHitStamina = 16f, StaminaRegen = 28f;

        /// <summary>Synced so other peers can draw the shield raised and the server can apply it.</summary>
        public readonly NetworkVariable<bool> Blocking = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private Health health;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;

        // Owner-side state.
        public float Stamina { get; private set; } = StaminaMax;
        public float BashReadyAt { get; private set; }
        public float WarCryReadyAt { get; private set; }
        private float swingBusyUntil, staminaRegenAt;
        private bool swingPending;
        private float swingStrikeAt;
        private float side = 1f;

        // Server-side state.
        private float warCryUntil;
        private float lastServerSwing;

        /// <summary>Time the War Cry buff ends, for the HUD. Synced as a timestamp on the server clock.</summary>
        public readonly NetworkVariable<double> WarCryEnds = new NetworkVariable<double>();

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                health.IncomingModifiers.Add(ModifyIncoming);
            }
        }

        private float AttackMultiplier => progress.AttackMultiplier;
        private bool riposteReady;

        /// <summary>True when the player's chosen class is the Knight. Otherwise this kit sleeps.</summary>
        private bool Active => progress.ClassId.Value == ClassId.Knight;

        // ---- IClassKit ----

        public ClassId Class => ClassId.Knight;
        public string ResourceName => "Stamina";
        public Color ResourceColor => new Color(0.9f, 0.68f, 0.18f);
        public float Resource => Stamina;
        public float ResourceMax => StaminaMax;
        public void RestoreResource(float fraction) => Stamina = Mathf.Min(ResourceMax, Stamina + ResourceMax * fraction);

        public void FillAbilities(List<AbilityView> into)
        {
            double warCryLeft = WarCryEnds.Value - NetworkManager.ServerTime.Time;
            into.Add(new AbilityView { Key = "LMB", Name = "Swing", Unaffordable = Stamina < SwingStamina });
            into.Add(new AbilityView { Key = "RMB", Name = "Block", Active = Blocking.Value });
            into.Add(new AbilityView { Key = "Q", Name = "Bash", Remaining = BashReadyAt - Time.time, Cooldown = BashCooldown * progress.CooldownFor(AbilitySlot.Q) });
            into.Add(new AbilityView { Key = "E", Name = "War Cry", Remaining = WarCryReadyAt - Time.time, Cooldown = WarCryCooldown * progress.CooldownFor(AbilitySlot.E), Active = warCryLeft > 0 });
        }

        // ==================================================================
        // Owner: input, timers, local animation
        // ==================================================================

        private void Update()
        {
            if (!IsOwner) return;
            if (!Active)
            {
                if (Blocking.Value) Blocking.Value = false;
                return;
            }

            bool canAct = life.CanAct && motor.ActionsReady;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            // --- Block (hold RMB) ---
            bool wantBlock = canAct && UI.Binds.SecondaryHeld && Stamina > 0f && !swingPending;
            if (Blocking.Value != wantBlock) Blocking.Value = wantBlock;

            // --- Stamina ---
            bool sprinting = motor.IsSprinting && motor.SpeedMultiplier > 0.9f;
            if (sprinting)
            {
                Stamina = Mathf.Max(0f, Stamina - SprintStaminaPerSec * Time.deltaTime);
                staminaRegenAt = Time.time + 0.6f;
            }
            else if (Time.time >= staminaRegenAt && !wantBlock)
            {
                Stamina = Mathf.Min(StaminaMax, Stamina + StaminaRegen * Time.deltaTime);
            }
            motor.CanSprint = Stamina > 5f && !wantBlock;
            motor.SpeedMultiplier = (wantBlock ? 0.5f : 1f) * progress.SpeedMultiplier;

            // --- Swing (LMB) ---
            if (canAct && !wantBlock && UI.Binds.PrimaryDown &&
                Time.time >= swingBusyUntil && Stamina >= SwingStamina)
            {
                Stamina -= SwingStamina;
                staminaRegenAt = Time.time + 0.5f;
                side = -side;
                swingPending = true;
                swingStrikeAt = Time.time + SwingWindup;
                // DEX shortens the recovery, not the wind-up: faster follow-ups, same tell.
                float recovery = SwingRecovery * progress.AttackTimeMultiplier;
                swingBusyUntil = Time.time + SwingWindup + SwingStrike + recovery;
                player.ViewModelSwing(SwingWindup, SwingStrike + recovery, side);
                Sfx.Play(Sound.Swing, transform.position, 0.6f);
            }

            if (swingPending && Time.time >= swingStrikeAt)
            {
                swingPending = false;
                SwingRpc(player.AimOrigin, player.AimForward, side);
            }

            // --- Q: Shield Bash ---
            if (canAct && keyboard != null && UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= BashReadyAt)
            {
                BashReadyAt = Time.time + BashCooldown * progress.CooldownFor(AbilitySlot.Q);
                var forward = transform.forward;
                motor.AddImpulse(forward * 13f);
                player.ViewModelBash();
                BashRpc(transform.position + Vector3.up, forward);
            }

            // --- E: War Cry ---
            if (canAct && keyboard != null && UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= WarCryReadyAt)
            {
                WarCryReadyAt = Time.time + WarCryCooldown * progress.CooldownFor(AbilitySlot.E);
                WarCryRpc();
            }

        }

        /// <summary>Called on the owner when the server reports a blocked hit.</summary>
        [Rpc(SendTo.Owner)]
        private void BlockedHitRpc()
        {
            Stamina = Mathf.Max(0f, Stamina - BlockedHitStamina);
            staminaRegenAt = Time.time + 0.8f;
            if (Stamina <= 0f)
            {
                // Guard broken: you cannot block again until stamina recovers a little.
                Blocking.Value = false;
                motor.AddImpulse(-transform.forward * 6f);
            }
        }

        // ==================================================================
        // Server: hit resolution
        // ==================================================================

        [Rpc(SendTo.Server)]
        private void SwingRpc(Vector3 origin, Vector3 forward, float swingSide)
        {
            if (!life.CanAct) return;

            // Loose rate limit: refuse swings faster than the animation allows. Generous,
            // because latency jitter can bunch two legitimate swings together.
            float minInterval = (SwingWindup + SwingStrike + SwingRecovery * progress.AttackTimeMultiplier) * 0.7f;
            if (Time.time - lastServerSwing < minInterval) return;
            lastServerSwing = Time.time;

            // Trust the client's aim origin only if it is near where the server thinks the
            // player is — otherwise someone could swing from across the room.
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.5f) > 2f) origin = transform.position + Vector3.up * 1.5f;

            // Cleave: a wider, longer arc (mastery chills). Riposte: a blocked hit empowers this swing.
            bool cleave = progress.OnPath(1);
            bool riposte = riposteReady && progress.OnPath(2);
            riposteReady = false;
            float range = (cleave ? SwingRange + 0.6f : SwingRange) * ReachMul, arc = cleave ? 160f : SwingArc;
            float damage = SwingDamage * AttackMultiplier * (riposte ? 2.2f : 1f);

            bool hitAny = false;
            foreach (var target in CombatQuery.Arc(origin, forward, range, arc, Team.Enemies))
            {
                var zone = HitZones.Aimed(target, origin, forward, range, out var zonePoint);
                target.ServerDamage(damage * HitZones.Multiplier(target, zone, false), transform.position, NetworkObjectId);
                if (zone != HitZone.Body) Arrows.ReportZone(NetworkObjectId, zonePoint, zone);
                if (progress.Mastered(1)) target.ServerChill(1.5f);
                if (riposte && progress.Mastered(2) && target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(1.2f);
                hitAny = true;
            }
            if (riposte) Sfx2Rpc();

            SwingFxRpc(swingSide, hitAny);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SwingFxRpc(float swingSide, bool hit)
        {
            SwingTrail.Spawn(transform, SwingRange * 0.85f, progress.OnPath(1) ? 160f : SwingArc, swingSide, new Color(1f, 0.85f, 0.6f) * 1.1f);
            if (!IsOwner)
            {
                player.Rig?.Play(RigAction.Swing, SwingStrike + 0.15f);
                Sfx.Play(Sound.Swing, transform.position, 0.6f);
            }
            if (hit)
            {
                Sfx.Play(Sound.Hit, transform.position + transform.forward * 1.5f + Vector3.up);
                if (IsOwner) player.ConfirmHit(1f);
            }
        }

        [Rpc(SendTo.Server)]
        private void BashRpc(Vector3 origin, Vector3 forward)
        {
            if (!life.CanAct) return;
            // The lunge carries the Knight ~1.5m, so the hit reaches a little further than
            // the shield itself.
            // Rank 2: harder and longer; rank 3: a shockwave half-circle, a metre further.
            float potency = progress.Potency(AbilitySlot.Q);
            bool wave = progress.Twist(AbilitySlot.Q);
            foreach (var target in CombatQuery.Arc(origin, forward, (BashRange + 1.5f + (wave ? 1f : 0f)) * ReachMul, wave ? 180f : 100f, Team.Enemies))
            {
                target.ServerDamage(BashDamage * AttackMultiplier * potency * Combos.TryShatter(target, progress), transform.position, NetworkObjectId);
                if (target.TryGetComponent<Enemies.EnemyBrain>(out var bashed)) bashed.ServerAddThreat(NetworkObjectId, 80f * potency); // a shield in the face
                if (target.TryGetComponent<ICrowdControl>(out var cc))
                {
                    cc.ServerStun(BashStun * potency);
                    if (target.TryGetComponent<Enemies.ShieldbearerBrain>(out var sb)) sb.ServerBreakGuard(3f);
                    var push = target.transform.position - transform.position;
                    push.y = 0f;
                    cc.ServerKnockback(push.normalized * 8f);
                    World.DungeonDirector.Instance?.ServerCheckShove(target, push.normalized * 10f, NetworkObjectId);
                }
            }
            BashFxRpc();
        }

        /// <summary>The riposte's ring of steel.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void Sfx2Rpc() => Sfx.Play(Sound.Block, transform.position + transform.forward + Vector3.up * 1.3f, 1f, 1.6f);

        [Rpc(SendTo.ClientsAndHost)]
        private void BashFxRpc()
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Bash, 0.4f);
            Sfx.Play(Sound.Bash, transform.position + transform.forward + Vector3.up);
            // A shove of force off the shield face: ring and grit, wider when ranked.
            bool wave = progress.Twist(AbilitySlot.Q);
            var front = transform.position + transform.forward * 1.6f;
            Vfx.Ring(front, wave ? 3.5f : 2.2f, new Color(1f, 0.9f, 0.7f), 0.3f);
            Vfx.Smoke(front, 2, 0.6f, 1f);
            Vfx.Embers(front + Vector3.up, 6, new Color(1f, 0.8f, 0.5f), 4f, 0.05f, 0.5f);
        }

        [Rpc(SendTo.Server)]
        private void WarCryRpc()
        {
            if (!life.CanAct) return;
            float potency = progress.Potency(AbilitySlot.E);
            float duration = WarCryDuration * potency;
            warCryUntil = Time.time + duration;
            WarCryEnds.Value = NetworkManager.ServerTime.Time + duration;

            foreach (var target in CombatQuery.Circle(transform.position, WarCryRadius * potency, Team.Enemies))
            {
                if (target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerTaunt(health, duration);
            }
            // Rank 3: the cry steels the party — a shield each (which can Aegis-burst).
            if (progress.Twist(AbilitySlot.E))
                foreach (var ally in CombatQuery.Circle(transform.position, WarCryRadius, Team.Players))
                    ally.ServerShield(40f, duration);
            WarCryFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WarCryFxRpc()
        {
            player.Rig?.Play(RigAction.Cast, 0.7f);
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 1.6f);
            // The cry as a pressure wave: two rings racing out to the taunt radius.
            float r = WarCryRadius * progress.Potency(AbilitySlot.E);
            Vfx.Ring(transform.position, r, new Color(1f, 0.45f, 0.2f), 0.55f);
            Vfx.Ring(transform.position, r * 0.6f, new Color(1f, 0.8f, 0.4f), 0.35f);
            Vfx.Flash(transform.position + Vector3.up * 1.6f, new Color(1f, 0.55f, 0.25f), 5f, 8f, 0.4f);
        }

        /// <summary>Bulwark and War Cry. Runs inside Health.ServerDamage, after DEF.</summary>
        private float ModifyIncoming(float damage, Vector3 attackerPosition)
        {
            if (!Active) return damage;
            if (Time.time < warCryUntil) damage *= 0.5f;

            if (Blocking.Value)
            {
                var to = attackerPosition - transform.position;
                to.y = 0f;
                if (Vector3.Angle(transform.forward, to) < 75f)
                {
                    // Turning a blow aside infuriates whoever threw it.
                    if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(health.LastHitFrom, out var foe) && foe.TryGetComponent<Enemies.EnemyBrain>(out var angry))
                        angry.ServerAddThreat(NetworkObjectId, damage * 3f + 15f);
                    damage *= 0.2f;
                    riposteReady = true;
                    BlockedHitRpc();
                    BlockFxRpc();
                }
            }
            return damage;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BlockFxRpc()
        {
            Sfx.Play(Sound.Block, transform.position + transform.forward * 0.6f + Vector3.up * 1.2f);
            // Sparks off the shield face, thrown back toward the attacker (seen by everyone, owner included).
            var face = transform.position + transform.forward * 0.75f + Vector3.up * 1.25f;
            Vfx.HitImpact(face, face + transform.forward, 0.8f, new Color(1f, 0.8f, 0.45f), Mat.Steel);
            if (IsOwner) player.ViewModelBlockImpact();
        }
    }
}
