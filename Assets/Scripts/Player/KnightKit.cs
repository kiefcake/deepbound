using System.Collections.Generic;
using System.Linq;
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
        public const float SwingRange = 2.7f, SwingArc = 110f, SwingDamage = 35f;
        /// <summary>Alone, the tank has nobody to hold the line for: swings hit harder (2026-11-15; solo bot Knights took 3-6 minutes a
        /// guardian and 40-50 near-downs on the worst floors, every other class 1-2 minutes). Like the lone Cleric's smite.</summary>
        public const float LoneSwingBonus = 1.25f;
        private static float LoneSwing => PlayerLife.All.Count <= 1 ? LoneSwingBonus : 1f;
        public float AttackRange => SwingRange * (progress != null ? progress.ReachMultiplier : 1f);
        private float ReachMul => progress != null ? progress.ReachMultiplier : 1f;
        public const float BashCooldown = 7f, BashRange = 2.4f, BashDamage = 24f, BashStun = 1.6f;
        public const float WarCryCooldown = 16f, WarCryRadius = 12f, WarCryDuration = 5f;
        public const float StaminaMax = 100f;
        // Swings are free (2026-11-01, the user: left click spends nothing); stamina is for blocking, sprinting and dodging.
        private const float HeldSwingReserve = 0f;
        private const float SwingStamina = 0f, SprintStaminaPerSec = 12f, BlockedHitStamina = 11f;
        /// <summary>Stamina back per second (2026-11-12, the user: lower from the start, trainable with Brann): 12 + VIT/4,
        /// 22/s at base VIT 40 (was a flat 28); Roadwork drills (+5 VIT) bring it back up.</summary>
        private float StaminaRegen => 12f + (progress != null ? progress.Value(StatType.VIT) : 40f) * 0.25f;

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
            // The raised shield's ward of light, for everyone else (your own view has the shield itself).
            if (!Application.isBatchMode) ward = ClassFx.KnightWard(transform, () => this != null && ShieldRaised, () => IsOwner && !NetworkPlayer.DebugShowSelf);
        }

        private ClassFx.Ward ward;

        /// <summary>
        /// Sheltered (2026-10-20, co-op glue): an ally within <see cref="ShelterRange"/> behind a Knight who is
        /// blocking takes <see cref="ShelterScale"/> from blows that come at the Knight's front. Stand behind
        /// the shield.
        /// </summary>
        public const float ShelterRange = 3.2f, ShelterScale = 0.5f;

        public bool ShieldRaised => Active && Blocking.Value && life != null && !life.Downed.Value;

        /// <summary>Server: is this player sheltered from a blow from <paramref name="attacker"/>?</summary>
        public static bool ShelterFor(PlayerLife who, Vector3 attacker)
        {
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p == who || !p.TryGetComponent<KnightKit>(out var k) || !k.ShieldRaised) continue;
                var kp = k.transform.position;
                var toMe = who.transform.position - kp; toMe.y = 0f;
                if (toMe.magnitude > ShelterRange) continue;
                var toFoe = attacker - kp; toFoe.y = 0f;
                // The blow comes at the shield's face, and I am on the other side of it.
                if (Vector3.Angle(k.transform.forward, toFoe) < 75f && Vector3.Dot(k.transform.forward, toMe) < 0.2f)
                {
                    k.ShelterFxRpc(who.transform.position + Vector3.up * 2f);
                    return true;
                }
            }
            return false;
        }

        private float lastShelterFx;

        [Rpc(SendTo.ClientsAndHost)]
        private void ShelterFxRpc(Vector3 at)
        {
            if (Time.time - lastShelterFx < 1.2f) return;
            lastShelterFx = Time.time;
            global::Deepbound.Art.Vfx.Embers(at, 6, new Color(0.7f, 0.85f, 1f), 2.5f, 0.05f, 0.4f);
        }

        private float AttackMultiplier => progress.AttackMultiplier;
        private float riposteUntil = -10f;
        /// <summary>COUNTER (2026-11-08, every Knight): a swing within <see cref="CounterWindow"/> s of turning a blow aside hits x<see cref="CounterDamage"/>; the Riposte path makes it x2.2 (and its mastery stuns).</summary>
        public const float CounterWindow = 1.6f, CounterDamage = 1.4f, RiposteDamage = 2.2f;
        /// <summary>The Spiked Shield path: a blocked blow from within reach comes back at x this (of the blow before the block).</summary>
        public const float SpikeReach = 3.5f, SpikeShare = 0.6f;
        /// <summary>Server: a counter is ready now.</summary>
        public bool CounterReady => Time.time < riposteUntil;
        /// <summary>Tests: hold the shield up as if RMB were down.</summary>
        public bool DebugHoldBlock { get; set; }
        /// <summary>Tests (server): a swing as if the owner had sent it.</summary>
        public void ServerDebugSwing(Vector3 origin, Vector3 forward) => SwingRpc(origin, forward, 1f);

        /// <summary>True when the player's chosen class is the Knight. Otherwise this kit sleeps.</summary>
        private bool Active => progress.ClassId.Value == ClassId.Knight;

        // ---- IClassKit ----

        public ClassId Class => ClassId.Knight;
        public string ResourceName => "Stamina";
        public string ResourceHint => Stamina < 15f ? "spent: guard and sprint falter" : "blocked hits and sprinting spend it";
        public Color ResourceColor => new Color(0.9f, 0.68f, 0.18f);
        public float Resource => Stamina;
        public float ResourceMax => StaminaMax;
        public void RestoreResource(float fraction) => Stamina = Mathf.Min(ResourceMax, Stamina + ResourceMax * fraction);
        public void ShaveCooldowns(float seconds) { BashReadyAt -= seconds; WarCryReadyAt -= seconds; }

        public void FillAbilities(List<AbilityView> into)
        {
            double warCryLeft = WarCryEnds.Value - NetworkManager.ServerTime.Time;
            into.Add(new AbilityView { Key = "LMB", Name = "Swing", Unaffordable = Stamina < SwingStamina });
            into.Add(new AbilityView { Key = "RMB", Name = progress != null && progress.OnPath(Classes.PathTriads.Path) ? (progress.ShieldCharge.Value >= 1f ? "Block  CHARGED" : "Block " + Mathf.RoundToInt(progress.ShieldCharge.Value * 100f) + "%") : "Block", Active = Blocking.Value });
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
            bool wantBlock = (canAct && UI.Binds.SecondaryHeld && Stamina > 0f && !swingPending) || DebugHoldBlock;
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

            // --- Swing (LMB) --- held, it keeps swinging (2026-10-21: every other class repeats on hold; the
            // Knight alone needed a click per swing). Swings cost no stamina (2026-11-01).
            // Holding keeps a reserve for the shield (a held button drained stamina as fast as it came back
            // and left nothing to block with); a fresh click may spend the last of it.
            if (canAct && !wantBlock && UI.Binds.PrimaryHeld &&
                Time.time >= swingBusyUntil && Stamina >= SwingStamina + (UI.Binds.PrimaryDown ? 0f : HeldSwingReserve))
            {
                Stamina -= SwingStamina;
                side = -side;
                swingPending = true;
                swingStrikeAt = Time.time + SwingWindup;
                // DEX shortens the recovery, not the wind-up: faster follow-ups, same tell.
                float recovery = SwingRecovery * progress.AttackTimeMultiplier;
                swingBusyUntil = Time.time + SwingWindup + SwingStrike + recovery;
                player.ViewModelSwing(SwingWindup, SwingStrike + recovery, side);
                Sfx.Play(Sound.Swing, transform.position, 0.6f);
                MeleeStep.TryStepIn(motor, transform, player.AimForward, SwingRange * progress.ReachMultiplier, 9f);
            }

            if (swingPending && Time.time >= swingStrikeAt)
            {
                swingPending = false;
                SwingRpc(player.AimOrigin, player.AimForward, side);
            }

            // --- Q: Shield Bash ---
            if (canAct && UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= BashReadyAt)
            {
                BashReadyAt = Time.time + BashCooldown * progress.CooldownFor(AbilitySlot.Q);
                var forward = transform.forward;
                motor.AddImpulse(forward * (progress.B(AbilitySlot.Q, 1) ? 22f : 13f));
                player.ViewModelBash();
                BashRpc(transform.position + Vector3.up, forward);
            }

            // --- E: War Cry ---
            if (canAct && UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= WarCryReadyAt)
            {
                WarCryReadyAt = Time.time + WarCryCooldown * progress.CooldownFor(AbilitySlot.E);
                WarCryRpc();
            }

        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BulwarkFxRpc()
        {
            Art.Sfx.Play(Art.Sound.Slam, transform.position, 1f, 0.8f);
            Art.Vfx.Ring(transform.position + Vector3.up * 0.05f, Classes.PathTriads.BulwarkRadius, new Color(1f, 0.85f, 0.45f), 0.45f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.2f, "BULWARK!", new Color(1f, 0.85f, 0.45f));
        }

        /// <summary>Called on the owner when the server reports a blocked hit.</summary>
        [Rpc(SendTo.Owner)]
        private void BlockedHitRpc()
        {
            UI.Tips.OfferNow("counter", "COUNTER: you turned a blow on your shield. Swing within " + CounterWindow + " s of a block and the swing hits x" + CounterDamage + " (x" + RiposteDamage + " on the Riposte path).");
            // Unbreakable (War Cry, rank 3 B): while the cry lasts, blocks cost nothing.
            bool unbreakable = progress != null && progress.B(AbilitySlot.E, 2) && WarCryEnds.Value > NetworkManager.ServerTime.Time;
            if (!unbreakable) Stamina = Mathf.Max(0f, Stamina - BlockedHitStamina * (progress != null ? progress.BuildBlockCostScale : 1f));
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

        private int chainSwings;
        private float lastChainSwing = -10f;

        [Rpc(SendTo.ClientsAndHost)]
        private void SlashWaveRpc(Vector3 from, Vector3 dir, float len, bool frost)
        {
            var c = frost ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.9f, 0.65f);
            for (int i = 1; i <= 10; i++)
            {
                var p = from + dir * (len * i / 10f);
                Vfx.Embers(p, 2, c, 1.5f, 0.07f, 0.35f);
            }
            FxSprite.Billboard("trace_01", from + dir * 1.5f, dir * len * 2.2f, len / (len * 2.2f) + 0.05f, 1.6f, 1.8f, new Color(c.r, c.g, c.b, 0.85f));
            Sfx.Play(Sound.Swing, from + dir * 2f, 0.9f, 0.6f);
        }

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
            bool counter = Time.time < riposteUntil;
            bool riposte = counter && progress.OnPath(2);
            riposteUntil = -10f;
            float range = (cleave ? SwingRange + 0.6f : SwingRange) * ReachMul, arc = cleave ? 160f : SwingArc;
            float damage = SwingDamage * AttackMultiplier * (riposte ? RiposteDamage : counter ? CounterDamage : 1f) * LoneSwing;

            bool hitAny = false;
            // The chain: forehand, backhand, OVERHEAD (the owner's view model plays the same order).
            chainSwings = Time.time - lastChainSwing < SwingWindup + SwingStrike + SwingRecovery * progress.AttackTimeMultiplier + 0.6f ? chainSwings + 1 : 1;
            lastChainSwing = Time.time;
            bool overhead = chainSwings % 3 == 0;
            foreach (var target in CombatQuery.Arc(origin, forward, range, arc, Team.Enemies))
            {
                var zone = HitZones.Aimed(target, origin, forward, range, out var zonePoint);
                // Hilt Strike (Bash, rank 3 B): the first swing on a bashed foe is a hilt-blow.
                bool hilt = hiltUntil.TryGetValue(target, out var hu) && Time.time < hu;
                if (hilt) hiltUntil.Remove(target);
                target.Tag(hilt ? "Hilt Strike" : "Swing").ServerDamage(damage * (hilt ? HiltDamage : 1f) * HitZones.Multiplier(target, zone, false), transform.position, NetworkObjectId);
                if (hilt)
                {
                    AbilityKit.Push(target, Flat(target.transform.position - transform.position) * 15f);
                    progress.AbilityTextRpc(target.Center + Vector3.up, "HILT STRIKE", PlayerProgress.FxKind.Steel);
                    progress.AbilityBurstRpc(target.Center, 1.2f, PlayerProgress.FxKind.Steel);
                }
                if (zone != HitZone.Body) Arrows.ReportZone(NetworkObjectId, zonePoint, zone);
                if (riposte && progress.Mastered(2) && target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(1.2f);
                hitAny = true;
            }
            if (counter && hitAny) Sfx2Rpc(riposte);
            // Greatblade: the overhead sends a slash wave rolling ahead.
            if (cleave && overhead)
            {
                var dir = Flat(forward);
                bool far = progress.Mastered(1);
                float len = (far ? 10f : 7f) * ReachMul;
                var start = transform.position + dir * 1.2f;
                foreach (var h in AbilityKit.Lane(start, dir, len, 0.9f))
                {
                    h.Tag("Slash Wave").ServerDamage(damage * 0.7f, start, NetworkObjectId);
                    if (far) h.ServerChill(2.5f);
                }
                SlashWaveRpc(start + Vector3.up * 1.1f, dir, len, far);
            }

            SwingFxRpc(swingSide, hitAny);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SwingFxRpc(float swingSide, bool hit)
        {
            SwingTrail.Spawn(transform, SwingRange * 0.85f, progress.OnPath(1) ? 160f : SwingArc, swingSide, player.TrailColor(new Color(1f, 0.85f, 0.6f) * 1.1f));
            // Others see the cut as a broad steel crescent (your own view has the blade and its trail).
            if (!IsOwner || NetworkPlayer.DebugShowSelf) ClassFx.KnightCut(transform, swingSide, SwingRange * ReachMul, progress.OnPath(1) ? 160f : SwingArc, player.TrailColor(new Color(1f, 0.9f, 0.7f)));
            if (hit) ClassFx.SteelBite(transform.position + transform.forward * 1.5f + Vector3.up * 1.1f, transform.position + Vector3.up * 1.2f);
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

        // ---- Ysolde's options for Shield Bash (2026-11-20) ----
        public const float RicochetRange = 6f, AftershockRadius = 3.5f, AftershockDelay = 0.7f, HiltDamage = 3f, HiltWindow = 2.5f;
        public const float DominoRadius = 2f, BracedSeconds = 2f, ThrowLength = 12f, SplitLength = 8f, SplitDelay = 0.5f;
        private readonly Dictionary<Health, float> hiltUntil = new Dictionary<Health, float>();
        private float bracedUntil = -10f;

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

        [Rpc(SendTo.Server)]
        private void BashRpc(Vector3 origin, Vector3 forward) => ServerBash(origin, forward);

        /// <summary>Server: the bash and every option taken for it. Public for the smoke test.</summary>
        public void ServerBash(Vector3 origin, Vector3 forward)
        {
            if (!life.CanAct) return;
            // The lunge carries the Knight ~1.5 m, so the hit reaches a little further than the shield itself.
            float potency = progress.Potency(AbilitySlot.Q);
            bool rush = progress.B(AbilitySlot.Q, 1);
            float reach = (BashRange + 1.5f + (rush ? 1.5f : 0f)) * ReachMul;
            var hits = CombatQuery.Arc(origin, forward, reach, 100f, Team.Enemies).ToList();
            var struck = new HashSet<Health>(hits);
            Health first = null;
            foreach (var target in hits)
            {
                if (first == null || (target.transform.position - origin).sqrMagnitude < (first.transform.position - origin).sqrMagnitude) first = target;
            }
            foreach (var target in hits) BashHit(target, forward, potency, rush && target == first, struck);

            // Ricochet (rank 2 A): the force leaps on to the nearest other enemy.
            if (progress.A(AbilitySlot.Q, 1) && first != null)
            {
                var next = AbilityKit.Nearest(first.transform.position, RicochetRange, struck);
                if (next != null)
                {
                    struck.Add(next);
                    progress.AbilityLinkRpc(first.Center, next.Center, PlayerProgress.FxKind.Steel);
                    BashHit(next, Flat(next.transform.position - first.transform.position), potency, false, struck);
                    progress.AbilityTextRpc(next.Center + Vector3.up, "RICOCHET", PlayerProgress.FxKind.Steel);
                }
            }

            var front = transform.position + Flat(forward) * 1.8f;
            // Aftershock (rank 3 A): the ground heaves a moment later.
            if (progress.A(AbilitySlot.Q, 2))
            {
                var at = Vfx.Floor(front);
                progress.AbilityCircleRpc(at, AftershockRadius, AftershockDelay, PlayerProgress.FxKind.Earth);
                float dmg = BashDamage * AttackMultiplier * potency * 0.6f;
                ulong me = NetworkObjectId;
                AbilityKit.Later(this, AftershockDelay, () =>
                {
                    AbilityKit.Blast(at, AftershockRadius, dmg, me, "Aftershock", h => AbilityKit.Stun(h, 1f));
                    progress.AbilityBurstRpc(at, AftershockRadius, PlayerProgress.FxKind.Earth);
                });
            }
            // Braced (rank 4 B): the shield guards every side for a moment.
            if (progress.B(AbilitySlot.Q, 3)) { bracedUntil = Time.time + BracedSeconds; progress.AbilityTextRpc(transform.position + Vector3.up * 2.3f, "BRACED", PlayerProgress.FxKind.Steel); }
            // Shield Throw (rank 5 A): a ghostly shield out along the aim and back.
            if (progress.A(AbilitySlot.Q, 4))
            {
                var dir = Flat(forward);
                float len = ThrowLength * ReachMul;
                if (Physics.Raycast(origin, dir, out var wall, len, ~0, QueryTriggerInteraction.Ignore) && wall.collider.GetComponentInParent<Health>() == null) len = Mathf.Max(2f, wall.distance - 0.3f);
                float dmg = BashDamage * AttackMultiplier * potency * 0.8f;
                ulong me = NetworkObjectId;
                var from = transform.position;
                progress.AbilityLinkRpc(origin, origin + dir * len, PlayerProgress.FxKind.Steel);
                foreach (var h in AbilityKit.Lane(from, dir, len, 0.9f)) { h.Tag("Shield Throw").ServerDamage(dmg, from, me); AbilityKit.Stun(h, 0.5f); }
                AbilityKit.Later(this, 0.6f, () =>
                {
                    progress.AbilityLinkRpc(origin + dir * len, transform.position + Vector3.up * 1.2f, PlayerProgress.FxKind.Steel);
                    foreach (var h in AbilityKit.Lane(from, dir, len, 0.9f)) h.Tag("Shield Throw").ServerDamage(dmg * 0.6f, from, me);
                });
            }
            // Earthsplitter (rank 5 B): a fissure runs ahead and bursts.
            if (progress.B(AbilitySlot.Q, 4))
            {
                var dir = Flat(forward);
                var start = Vfx.Floor(transform.position + dir * 0.8f);
                float len = SplitLength * ReachMul;
                progress.AbilityLaneRpc(start, dir, len, 2.4f, SplitDelay, PlayerProgress.FxKind.Earth);
                float dmg = BashDamage * AttackMultiplier * potency * 1.1f;
                ulong me = NetworkObjectId;
                AbilityKit.Later(this, SplitDelay, () =>
                {
                    foreach (var h in AbilityKit.Lane(start, dir, len, 1.2f)) { h.Tag("Earthsplitter").ServerDamage(dmg, start, me); AbilityKit.Stun(h, 1.2f); }
                    for (int i = 1; i <= 4; i++) progress.AbilityBurstRpc(start + dir * (len * i / 4f), 1.3f, PlayerProgress.FxKind.Earth);
                });
            }
            BashFxRpc();
        }

        /// <summary>Server: one enemy takes the bash.</summary>
        private void BashHit(Health target, Vector3 forward, float potency, bool rushed, HashSet<Health> struck)
        {
            if (target == null || !target.IsAlive) return;
            target.Tag("Bash").ServerDamage(BashDamage * AttackMultiplier * potency * Combos.TryShatter(target, progress), transform.position, NetworkObjectId);
            if (target.TryGetComponent<Enemies.EnemyBrain>(out var bashed)) bashed.ServerAddThreat(NetworkObjectId, 80f * potency); // a shield in the face
            if (progress.B(AbilitySlot.Q, 2)) hiltUntil[target] = Time.time + HiltWindow;
            if (!target.TryGetComponent<ICrowdControl>(out var cc)) return;
            cc.ServerStun(BashStun);
            if (target.TryGetComponent<Enemies.ShieldbearerBrain>(out var sb)) sb.ServerBreakGuard(3f);
            var push = Flat(target.transform.position - transform.position);
            // Shield Rush (rank 2 B): the first foe is driven before you and hurled back.
            float force = rushed ? 18f : 8f;
            cc.ServerKnockback(push * force);
            World.DungeonDirector.Instance?.ServerCheckShove(target, push * (rushed ? 16f : 10f), NetworkObjectId);
            // Domino (rank 4 A): hurled into the ones behind it.
            if (progress.A(AbilitySlot.Q, 3))
            {
                var landing = target.transform.position + push * (force * 0.22f);
                float dmg = BashDamage * AttackMultiplier * potency * 0.7f;
                ulong me = NetworkObjectId;
                AbilityKit.Later(this, 0.3f, () =>
                {
                    foreach (var h in CombatQuery.Circle(landing, DominoRadius, Team.Enemies).ToArray())
                    {
                        if (h == null || !h.IsAlive || struck.Contains(h)) continue;
                        struck.Add(h);
                        h.Tag("Domino").ServerDamage(dmg, landing, me);
                        AbilityKit.Stun(h, BashStun * 0.7f);
                        AbilityKit.Push(h, Flat(h.transform.position - landing) * 6f);
                        progress.AbilityTextRpc(h.Center + Vector3.up, "DOMINO", PlayerProgress.FxKind.Steel);
                    }
                });
            }
        }

        /// <summary>The riposte's ring of steel.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void Sfx2Rpc(bool riposte)
        {
            Sfx.Play(Sound.Block, transform.position + transform.forward + Vector3.up * 1.3f, 1f, 1.6f);
            if (IsOwner) UI.GameHud.ShowCombo(transform.position + transform.forward * 1.6f + Vector3.up * 1.6f, riposte ? "RIPOSTE" : "COUNTER", new Color(1f, 0.85f, 0.45f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BashFxRpc()
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Bash, 0.4f);
            Sfx.Play(Sound.Bash, transform.position + transform.forward + Vector3.up);
            // A shove of force off the shield face: ring and grit, wider when ranked.
            var front = transform.position + transform.forward * 1.6f;
            Vfx.Ring(front, 2.2f, new Color(1f, 0.9f, 0.7f), 0.3f);
            Vfx.Smoke(front, 2, 0.6f, 1f);
            // 2026-11-21 (distinct abilities): the kite of the shield itself, hurled forward in light, shattering at its reach.
            ClassFx.KnightBash(transform, BashRange + 1.5f);
        }

        public const float DreadSeconds = 1.5f, BastionRadius = 8f, BastionScale = 0.75f, EchoDelay = 3f;

        [Rpc(SendTo.Server)]
        private void WarCryRpc() => ServerWarCry();

        /// <summary>Server: the cry and every option taken for it. Public for the smoke test.</summary>
        public void ServerWarCry()
        {
            if (!life.CanAct) return;
            float duration = WarCryDuration * progress.Potency(AbilitySlot.E);
            warCryUntil = Time.time + duration;
            WarCryEnds.Value = NetworkManager.ServerTime.Time + duration;
            lastOathSpent = false;
            Cry(duration);
            // Rally (rank 2 B): the party runs faster and hits harder while it lasts.
            if (progress.B(AbilitySlot.E, 1))
                foreach (var p in PlayerLife.All)
                    if (p != null && !p.Downed.Value && Vector3.Distance(p.transform.position, transform.position) <= WarCryRadius && p.TryGetComponent<PlayerProgress>(out var pp))
                        pp.ServerRally(duration, 1f);
            // Bastion (rank 4 B): allies near you take a quarter less.
            if (progress.B(AbilitySlot.E, 3)) AbilityKit.ServerDamageZone(transform.position, BastionRadius, duration, BastionScale, health, "Bastion");
            // Echo (rank 5 A): the cry again, three seconds on.
            if (progress.A(AbilitySlot.E, 4))
                AbilityKit.Later(this, EchoDelay, () =>
                {
                    if (!health.IsAlive || life.Downed.Value) return;
                    warCryUntil = Mathf.Max(warCryUntil, Time.time + 2f);
                    Cry(duration * 0.6f);
                    progress.AbilityTextRpc(transform.position + Vector3.up * 2.4f, "ECHO", PlayerProgress.FxKind.Fire);
                });
        }

        /// <summary>Server: one shout: taunt, Challenge, Dread.</summary>
        private void Cry(float duration)
        {
            Health toughest = null;
            foreach (var target in CombatQuery.Circle(transform.position, WarCryRadius, Team.Enemies).ToArray())
            {
                if (target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerTaunt(health, duration);
                if (toughest == null || target.Current.Value > toughest.Current.Value) toughest = target;
                // Dread (rank 4 A): the rank and file cower before they come.
                if (progress.A(AbilitySlot.E, 3) && !AbilityKit.IsBoss(target)) AbilityKit.Stun(target, DreadSeconds);
            }
            // Challenge (rank 2 A): the toughest is marked for the party.
            if (progress.A(AbilitySlot.E, 1) && toughest != null)
            {
                toughest.ServerMark(duration + 3f);
                progress.AbilityTextRpc(toughest.Center + Vector3.up * 1.4f, "CHALLENGED", PlayerProgress.FxKind.Fire);
            }
            WarCryFxRpc();
        }

        private bool lastOathSpent;

        [Rpc(SendTo.ClientsAndHost)]
        private void WarCryFxRpc()
        {
            player.Rig?.Play(RigAction.Cast, 0.7f);
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 1.6f);
            // The cry as a pressure wave: two rings racing out to the taunt radius.
            float r = WarCryRadius;
            Vfx.Ring(transform.position, r, new Color(1f, 0.45f, 0.2f), 0.55f);
            Vfx.Ring(transform.position, r * 0.6f, new Color(1f, 0.8f, 0.4f), 0.35f);
            Vfx.AbilityFlash(transform.position + Vector3.up * 1.6f, new Color(1f, 0.55f, 0.25f), 5f, 8f, 0.4f);
            // 2026-11-21 (distinct abilities): walls of sound and a roar out of the mouth; a burning chevron over every foe
            // it pulls (the taunt you can see), snapped back to the Knight by a tether.
            ClassFx.KnightWarCry(transform, r);
            int marks = 0;
            foreach (var h in Health.All)
            {
                if (marks >= 12) break;
                if (h == null || h.Team != Team.Enemies || !h.IsAlive || Vector3.Distance(h.transform.position, transform.position) > r) continue;
                float tall = Mathf.Max(1.2f, (h.Center.y - h.transform.position.y) * 2f);
                ClassFx.TauntMark(h.transform, tall, WarCryDuration, transform.position + Vector3.up * 1.4f, h.Center);
                marks++;
            }
        }

        /// <summary>Bulwark and War Cry. Runs inside Health.ServerDamage, after DEF.</summary>
        private float ModifyIncoming(float damage, Vector3 attackerPosition)
        {
            if (!Active) return damage;
            bool crying = Time.time < warCryUntil;
            if (crying)
            {
                // Iron Thorns (War Cry, rank 3 A): whatever strikes you takes half the blow back.
                if (progress.A(AbilitySlot.E, 2)) AbilityKit.Thorn(health, damage * 0.5f);
                damage *= 0.5f;
            }

            bool braced = Time.time < bracedUntil;
            if (Blocking.Value || braced)
            {
                var to = attackerPosition - transform.position;
                to.y = 0f;
                if (braced || Vector3.Angle(transform.forward, to) < 75f)
                {
                    // Turning a blow aside infuriates whoever threw it.
                    if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(health.LastHitFrom, out var foe) && foe.TryGetComponent<Enemies.EnemyBrain>(out var angry))
                        angry.ServerAddThreat(NetworkObjectId, damage * 3f + 15f);
                    if (Classes.PathTriads.OnKnightBlock(progress, damage, SwingDamage)) BulwarkFxRpc();
                    // Spiked Shield: a melee blow on the spikes hurts the one who struck (and stuns, mastered).
                    if (progress.OnPath(2) && to.magnitude < SpikeReach)
                    {
                        AbilityKit.Thorn(health, damage * SpikeShare * AttackMultiplier);
                        if (progress.Mastered(2) && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(health.LastHitFrom, out var struck) && struck.TryGetComponent<Health>(out var sh)) AbilityKit.Stun(sh, 1f);
                    }
                    damage *= 0.2f;
                    riposteUntil = Time.time + CounterWindow;
                    if (!braced) BlockedHitRpc();
                    BlockFxRpc();
                }
            }
            // Last Oath (War Cry, rank 5 B): a downing blow leaves 1 health and ends the cry instead.
            if (crying && !lastOathSpent && progress.B(AbilitySlot.E, 4) && damage >= health.Current.Value && health.Current.Value > 1f)
            {
                lastOathSpent = true;
                damage = health.Current.Value - 1f;
                warCryUntil = 0f;
                WarCryEnds.Value = NetworkManager.ServerTime.Time;
                progress.AbilityTextRpc(transform.position + Vector3.up * 2.4f, "LAST OATH", PlayerProgress.FxKind.Holy);
                progress.AbilityBurstRpc(transform.position + Vector3.up, 2.5f, PlayerProgress.FxKind.Holy);
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
            ward?.Hit();
            if (IsOwner) player.ViewModelBlockImpact();
        }
    }
}
