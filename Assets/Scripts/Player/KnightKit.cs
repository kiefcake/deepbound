using Deepbound.Art;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Core;
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
    public class KnightKit : NetworkBehaviour
    {
        // Timings in seconds. Wind-ups are short — the Knight is meant to feel responsive.
        public const float SwingWindup = 0.16f, SwingStrike = 0.14f, SwingRecovery = 0.3f;
        public const float SwingRange = 2.7f, SwingArc = 110f, SwingDamage = 34f;
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

        // Owner-side state.
        public float Stamina { get; private set; } = StaminaMax;
        public float BashReadyAt { get; private set; }
        public float WarCryReadyAt { get; private set; }
        private float swingBusyUntil, staminaRegenAt;
        private bool swingPending;
        private float swingStrikeAt;
        private float reviveSendAt;
        private float side = 1f;

        // Server-side state.
        public StatBlock Stats { get; private set; }
        private float warCryUntil;
        private float lastServerSwing;
        private float lastDamagedAt;
        private float regenCarry;

        /// <summary>Time the War Cry buff ends, for the HUD. Synced as a timestamp on the server clock.</summary>
        public readonly NetworkVariable<double> WarCryEnds = new NetworkVariable<double>();

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Stats = ClassDefinition.Knight.Build();
                ApplyStats(refill: true);
                health.IncomingModifiers.Add(ModifyIncoming);
                health.DamagedServer += (amount, attacker) => lastDamagedAt = Time.time;
            }
        }

        /// <summary>Pushes the stat block into Health. Server only; call after potions or gear change.</summary>
        public void ApplyStats(bool refill)
        {
            if (!IsServer) return;
            health.ServerSetMax(Stats.GetValue(StatType.HP), refill);
            health.Defense = Stats.GetValue(StatType.DEF);
        }

        /// <summary>RotMG-style ATT scaling: ATT 50 doubles the base ATT 0 damage.</summary>
        private float AttackMultiplier => 0.5f + (Stats != null ? Stats.GetValue(StatType.ATT) : 20f) / 50f;

        // ==================================================================
        // Owner: input, timers, local animation
        // ==================================================================

        private void Update()
        {
            if (IsServer) ServerTick();
            if (!IsOwner) return;

            bool canAct = life.CanAct && motor.HasControl;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            // --- Block (hold RMB) ---
            bool wantBlock = canAct && mouse != null && mouse.rightButton.isPressed && Stamina > 0f && !swingPending;
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
            motor.SpeedMultiplier = (wantBlock ? 0.5f : 1f) * SpeedFromStat();

            // --- Swing (LMB) ---
            if (canAct && !wantBlock && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                Time.time >= swingBusyUntil && Stamina >= SwingStamina)
            {
                Stamina -= SwingStamina;
                staminaRegenAt = Time.time + 0.5f;
                side = -side;
                swingPending = true;
                swingStrikeAt = Time.time + SwingWindup;
                swingBusyUntil = Time.time + SwingWindup + SwingStrike + SwingRecovery;
                player.ViewModelSwing(SwingWindup, SwingStrike + SwingRecovery, side);
                Sfx.Play(Sound.Swing, transform.position, 0.6f);
            }

            if (swingPending && Time.time >= swingStrikeAt)
            {
                swingPending = false;
                SwingRpc(player.AimOrigin, player.AimForward, side);
            }

            // --- Q: Shield Bash ---
            if (canAct && keyboard != null && keyboard.qKey.wasPressedThisFrame && Time.time >= BashReadyAt)
            {
                BashReadyAt = Time.time + BashCooldown;
                var forward = transform.forward;
                motor.AddImpulse(forward * 13f);
                player.ViewModelBash();
                BashRpc(transform.position + Vector3.up, forward);
            }

            // --- E: War Cry ---
            if (canAct && keyboard != null && keyboard.eKey.wasPressedThisFrame && Time.time >= WarCryReadyAt)
            {
                WarCryReadyAt = Time.time + WarCryCooldown;
                WarCryRpc();
            }

            // --- F: Revive ---
            if (canAct && keyboard != null && keyboard.fKey.isPressed && Time.time >= reviveSendAt)
            {
                var target = PlayerLife.DownedNear(transform.position, life);
                if (target != null)
                {
                    reviveSendAt = Time.time + 0.2f;
                    target.ReviveHoldRpc(NetworkObject);
                }
            }
        }

        /// <summary>SPD 50 is the reference speed; each point either side is ±1%.</summary>
        private float SpeedFromStat()
        {
            // The owner does not hold the authoritative StatBlock (only the server does), but
            // base SPD is class data, so it can be read locally. Potion-boosted SPD will need
            // syncing when potions land.
            float spd = ClassDefinition.Knight.BaseStats[(int)StatType.SPD];
            return 1f + (spd - 50f) / 100f;
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
            float minInterval = (SwingWindup + SwingStrike + SwingRecovery) * 0.7f;
            if (Time.time - lastServerSwing < minInterval) return;
            lastServerSwing = Time.time;

            // Trust the client's aim origin only if it is near where the server thinks the
            // player is — otherwise someone could swing from across the room.
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.5f) > 2f) origin = transform.position + Vector3.up * 1.5f;

            bool hitAny = false;
            foreach (var target in CombatQuery.Arc(origin, forward, SwingRange, SwingArc, Team.Enemies))
            {
                target.ServerDamage(SwingDamage * AttackMultiplier, transform.position, NetworkObjectId);
                hitAny = true;
            }

            SwingFxRpc(swingSide, hitAny);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SwingFxRpc(float swingSide, bool hit)
        {
            if (!IsOwner)
            {
                player.Rig?.Play(RigAction.Swing, SwingStrike + 0.15f);
                Sfx.Play(Sound.Swing, transform.position, 0.6f);
            }
            if (hit) Sfx.Play(Sound.Hit, transform.position + transform.forward * 1.5f + Vector3.up);
        }

        [Rpc(SendTo.Server)]
        private void BashRpc(Vector3 origin, Vector3 forward)
        {
            if (!life.CanAct) return;
            // The lunge carries the Knight ~1.5m, so the hit reaches a little further than
            // the shield itself.
            foreach (var target in CombatQuery.Arc(origin, forward, BashRange + 1.5f, 100f, Team.Enemies))
            {
                target.ServerDamage(BashDamage * AttackMultiplier, transform.position, NetworkObjectId);
                if (target.TryGetComponent<ICrowdControl>(out var cc))
                {
                    cc.ServerStun(BashStun);
                    var push = target.transform.position - transform.position;
                    push.y = 0f;
                    cc.ServerKnockback(push.normalized * 8f);
                }
            }
            BashFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BashFxRpc()
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Bash, 0.4f);
            Sfx.Play(Sound.Bash, transform.position + transform.forward + Vector3.up);
        }

        [Rpc(SendTo.Server)]
        private void WarCryRpc()
        {
            if (!life.CanAct) return;
            warCryUntil = Time.time + WarCryDuration;
            WarCryEnds.Value = NetworkManager.ServerTime.Time + WarCryDuration;

            foreach (var target in CombatQuery.Circle(transform.position, WarCryRadius, Team.Enemies))
            {
                if (target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerTaunt(health, WarCryDuration);
            }
            WarCryFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WarCryFxRpc()
        {
            player.Rig?.Play(RigAction.Cast, 0.7f);
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 1.6f);
        }

        /// <summary>Bulwark and War Cry. Runs inside Health.ServerDamage, after DEF.</summary>
        private float ModifyIncoming(float damage, Vector3 attackerPosition)
        {
            if (Time.time < warCryUntil) damage *= 0.5f;

            if (Blocking.Value)
            {
                var to = attackerPosition - transform.position;
                to.y = 0f;
                if (Vector3.Angle(transform.forward, to) < 75f)
                {
                    damage *= 0.2f;
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
            if (IsOwner) player.ViewModelBlockImpact();
        }

        /// <summary>
        /// Out-of-combat regeneration, scaled by VIT. Off while downed and for a few
        /// seconds after taking damage, so it heals you between fights, not during them.
        /// </summary>
        private void ServerTick()
        {
            if (Stats == null || life.Downed.Value || !health.IsAlive) return;
            if (Time.time - lastDamagedAt < 5f || health.Current.Value >= health.Max.Value) return;

            regenCarry += (2f + Stats.GetValue(StatType.VIT) * 0.12f) * Time.deltaTime;
            if (regenCarry >= 1f)
            {
                float whole = Mathf.Floor(regenCarry);
                regenCarry -= whole;
                health.ServerHeal(whole);
            }
        }
    }
}
