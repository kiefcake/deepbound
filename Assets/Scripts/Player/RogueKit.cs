using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// The Rogue's kit: get behind them.
    ///
    /// <list type="bullet">
    /// <item><b>Passive — Ambush.</b> Hits from behind deal a further +50% on top of the normal
    ///   backstab bonus. The Rogue's damage lives or dies on positioning.</item>
    /// <item><b>LMB — Twin Daggers.</b> Very fast, very short stabs, alternating hands.</item>
    /// <item><b>RMB — Fan of Knives.</b> Three thrown knives in a narrow spread.</item>
    /// <item><b>Q — Shadowstep.</b> Vanish and reappear behind the enemy under your crosshair,
    ///   facing its back.</item>
    /// <item><b>E — Smoke Bomb.</b> Disappear for seven seconds: every enemy loses you, you move
    ///   faster, and your first strike out of the smoke deals double damage and stuns.</item>
    /// </list>
    /// Energy refills quickly; the Rogue is limited by cooldowns and nerve, not by resource.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class RogueKit : NetworkBehaviour, IClassKit
    {
        public const float StabInterval = 0.26f, StabRange = 2.2f, StabArc = 75f, StabDamage = 13f, StabEnergy = 8f;
        public const float FanCooldown = 1.8f, FanEnergy = 30f, FanDamage = 15f, FanSpeed = 30f, FanRange = 14f, FanGravity = 6f;
        public float AttackRange => StabRange * (progress != null ? progress.ReachMultiplier : 1f);
        private float RangeMul => progress != null ? progress.RangeMultiplier : 1f;
        private float Reach => StabRange * (progress != null ? progress.ReachMultiplier : 1f);
        public const float StepCooldown = 9f, StepRange = 13f;
        public const float SmokeCooldown = 18f, SmokeSeconds = 7f;
        public const float AmbushMultiplier = 1.5f, OpenerMultiplier = 2f, OpenerStun = 1.2f;
        public const float BossAmbushMultiplier = 1.3f, BossAssassinMultiplier = 1.6f;
        public const float EnergyMax = 100f, EnergyRegen = 22f;

        private Health health;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;

        public float Energy { get; private set; } = EnergyMax;
        private float nextStab, fanReadyAt, stepReadyAt, smokeReadyAt;
        private bool lastHitFromBehind;
        private Vector3 smokeCloudAt;
        private float smokeCloudUntil;
        public const float SmokeCloudRadius = 3.5f;
        private float side = 1f;
        private float lastServerStab;

        // Server: when the smoke ends, and whether the next hit is the opener.
        private float hiddenUntil;
        private bool openerArmed;
        /// <summary>The opener survives a moment past the smoke, so knives thrown from it still count.</summary>
        private float openerExpires;

        private GameObject hiddenMarker;

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        private bool Active => progress.ClassId.Value == ClassId.Rogue;

        // ---- IClassKit ----

        public ClassId Class => ClassId.Rogue;
        public string ResourceName => "Energy";
        public Color ResourceColor => new Color(0.85f, 0.9f, 0.3f);
        public float Resource => Energy;
        public float ResourceMax => EnergyMax;
        public void RestoreResource(float fraction) => Energy = Mathf.Min(ResourceMax, Energy + ResourceMax * fraction);
        public void ShaveCooldowns(float seconds) { stepReadyAt -= seconds; smokeReadyAt -= seconds; }

        public void FillAbilities(List<AbilityView> into)
        {
            float cd = progress.CooldownMultiplier;
            into.Add(new AbilityView { Key = "LMB", Name = "Daggers", Unaffordable = Energy < StabEnergy });
            into.Add(new AbilityView { Key = "RMB", Name = "Knives", Remaining = fanReadyAt - Time.time, Cooldown = FanCooldown, Unaffordable = Energy < FanEnergy });
            into.Add(new AbilityView { Key = "Q", Name = "Shadowstep", Remaining = stepReadyAt - Time.time, Cooldown = StepCooldown * progress.CooldownFor(AbilitySlot.Q) });
            into.Add(new AbilityView { Key = "E", Name = "Smoke", Remaining = smokeReadyAt - Time.time, Cooldown = SmokeCooldown * progress.CooldownFor(AbilitySlot.E), Active = life.Hidden.Value });
        }

        public override void OnNetworkSpawn()
        {
            life.Hidden.OnValueChanged += (_, now) => OnHiddenChanged(now);
        }

        // ==================================================================
        // Owner
        // ==================================================================

        private void Update()
        {
            if (IsServer && life.Hidden.Value && (Time.time >= hiddenUntil || !life.CanAct || !Active)) EndHidden();
            // Smoke Screen: the cloud stays where it burst and veils any ally standing in it.
            if (IsServer && Time.time < smokeCloudUntil)
                foreach (var ally in PlayerLife.All)
                    if (ally != null && ally != life && Vector3.Distance(ally.transform.position, smokeCloudAt) < SmokeCloudRadius)
                        ally.VeiledUntil = Time.time + 0.25f;

            if (!IsOwner || !Active) return;

            Energy = Mathf.Min(EnergyMax, Energy + EnergyRegen * Time.deltaTime);
            motor.SpeedMultiplier = progress.SpeedMultiplier * (life.Hidden.Value ? 1.3f : 1f);
            motor.CanSprint = true;

            if (!life.CanAct || !motor.ActionsReady) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            // --- LMB held: Twin Daggers ---
            if (UI.Binds.PrimaryHeld && Time.time >= nextStab && Energy >= StabEnergy)
            {
                Energy -= StabEnergy;
                side = -side;
                nextStab = Time.time + StabInterval * progress.AttackTimeMultiplier;
                player.ViewModelSwing(0.04f, StabInterval * 0.8f, side);
                Sfx.Play(Sound.Swing, transform.position, 0.4f, 1.5f);
                StabRpc(player.AimOrigin, player.AimForward, side);
            }

            // --- RMB: Fan of Knives ---
            if (UI.Binds.SecondaryDown && Time.time >= fanReadyAt && Energy >= FanEnergy)
            {
                Energy -= FanEnergy;
                fanReadyAt = Time.time + FanCooldown;
                player.ViewModelCast(false);
                FanRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward);
            }

            // --- Q: Shadowstep ---
            if (keyboard != null && UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= stepReadyAt && TryShadowstep())
            {
                stepReadyAt = Time.time + StepCooldown * progress.CooldownFor(AbilitySlot.Q);
            }

            // --- E: Smoke Bomb ---
            if (keyboard != null && UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= smokeReadyAt)
            {
                smokeReadyAt = Time.time + SmokeCooldown * progress.CooldownFor(AbilitySlot.E);
                SmokeRpc();
            }
        }

        /// <summary>Teleport behind the enemy under the crosshair, facing its back.</summary>
        private bool TryShadowstep()
        {
            Health best = null;
            float bestAngle = 10f;
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                var to = h.Center - player.AimOrigin;
                if (to.magnitude > StepRange * progress.Potency(AbilitySlot.Q) * RangeMul) continue;
                float angle = Vector3.Angle(player.AimForward, to);
                if (angle < bestAngle && CombatQuery.ClearLine(player.AimOrigin, h)) { bestAngle = angle; best = h; }
            }
            if (best == null) return false;

            var back = -best.transform.forward;
            back.y = 0f;
            back.Normalize();
            float radius = best.TryGetComponent<CapsuleCollider>(out var cap) ? cap.radius * best.transform.lossyScale.x : 0.4f;
            var spot = best.transform.position + back * (radius + 0.9f);

            // Not into a wall: if the space behind is blocked, land beside it instead.
            if (Physics.Raycast(best.Center, back, radius + 1.1f, ~0, QueryTriggerInteraction.Ignore))
                spot = best.transform.position + best.transform.right * (radius + 0.9f);
            if (!Physics.Raycast(spot + Vector3.up * 1.5f, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore)) return false;

            var from = transform.position;
            var face = best.transform.position - floor.point;
            face.y = 0f;
            motor.Teleport(floor.point + Vector3.up * 0.05f, Quaternion.LookRotation(face));
            StepFxRpc(from, transform.position);
            if (progress.Twist(AbilitySlot.Q)) ArmStepOpenerRpc();
            return true;
        }

        // ==================================================================
        // Server
        // ==================================================================

        /// <summary>Ambush and the smoke opener, applied on top of the normal backstab.</summary>
        private float Multiplier(Health target, out bool opener)
        {
            opener = false;
            float m = progress.AttackMultiplier;
            var toMe = transform.position - target.transform.position;
            toMe.y = 0f;
            bool behind = toMe.sqrMagnitude > 0.01f && Vector3.Angle(target.transform.forward, toMe) > 115f;
            lastHitFromBehind = behind;
            // Assassin: deadlier from behind. Duelist: better face to face.
            // Against a boss the bonus is smaller (2026-10-27 autoplay: a Rogue that lived behind them killed every
            // guardian in 12-36 s, three times the Knight's pace).
            bool boss = target.TryGetComponent<Enemies.BossBrain>(out _);
            if (behind) m *= (progress.OnPath(1) ? (boss ? BossAssassinMultiplier : 2f) : (boss ? BossAmbushMultiplier : AmbushMultiplier)) * Combos.TryShatter(target, progress);
            else if (progress.OnPath(2)) m *= 1.25f;
            if (openerArmed && (life.Hidden.Value || Time.time < openerExpires))
            {
                m *= OpenerMultiplier;
                opener = true;
            }
            return m;
        }

        private void AfterHit(Health target, bool opener)
        {
            // Assassin mastery: a kill from behind resets Shadowstep.
            if (lastHitFromBehind && !target.IsAlive && progress.Mastered(1)) ResetStepRpc();
            if (!opener) return;
            openerArmed = false;
            if (target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(OpenerStun);
            OpenerFxRpc(target.Center);
        }

        [Rpc(SendTo.Owner)]
        private void ResetStepRpc() => stepReadyAt = 0f;

        [Rpc(SendTo.Server)]
        private void StabRpc(Vector3 origin, Vector3 forward, float handSide)
        {
            if (!life.CanAct || Time.time - lastServerStab < StabInterval * 0.5f) return;
            lastServerStab = Time.time;
            ServerStab(origin, forward);
        }

        /// <summary>Server: one dagger stab. Public for the smoke test.</summary>
        public void ServerStab(Vector3 origin, Vector3 forward)
        {
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.5f) > 2f) origin = transform.position + Vector3.up * 1.5f;

            // One target per stab — daggers are precise, not sweeping — or two for the Duelist.
            int hits = 0, maxHits = progress.OnPath(2) ? 2 : 1;
            foreach (var target in CombatQuery.Arc(origin, forward, Reach, StabArc, Team.Enemies).ToArray())
            {
                float m = Multiplier(target, out bool opener);
                var zone = HitZones.Aimed(target, origin, forward, Reach, out var zonePoint);
                m *= HitZones.Multiplier(target, zone, false);
                target.ServerDamage(StabDamage * m, transform.position, NetworkObjectId);
                if (zone != HitZone.Body) Arrows.ReportZone(NetworkObjectId, zonePoint, zone);
                AfterHit(target, opener);
                if (++hits >= maxHits) break;
            }
            bool hitAny = hits > 0;
            if (life.Hidden.Value) EndHidden();
            StabFxRpc(hitAny);
        }

        [Rpc(SendTo.Server)]
        private void FanRpc(Vector3 origin, Vector3 direction)
        {
            if (!life.CanAct) return;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            ulong me = NetworkObjectId;
            int spread = progress.Mastered(2) ? 2 : 1; // Duelist mastery: five knives
            for (int i = -spread; i <= spread; i++)
            {
                var dir = Quaternion.Euler(0f, i * 7f, 0f) * direction;
                Arrows.ServerFire(origin, dir, 0f, Team.Enemies, me, FanSpeed, h =>
                {
                    if (this == null) return;
                    float m = Multiplier(h, out bool opener);
                    h.ServerDamage(FanDamage * m, transform.position, me);
                    AfterHit(h, opener);
                }, false, FanRange * RangeMul);
            }
            if (life.Hidden.Value) EndHidden();
            FanFxRpc(origin, direction);
        }

        /// <summary>Rank 3 Shadowstep: the first strike from the shadows is an opener.</summary>
        [Rpc(SendTo.Server)]
        private void ArmStepOpenerRpc()
        {
            openerArmed = true;
            openerExpires = Time.time + 2f;
        }

        [Rpc(SendTo.Server)]
        private void SmokeRpc() => ServerSmoke();

        /// <summary>Server: vanish. Public for the smoke test.</summary>
        public void ServerSmoke()
        {
            if (!life.CanAct) return;
            hiddenUntil = Time.time + SmokeSeconds * progress.Potency(AbilitySlot.E);
            smokeCloudAt = transform.position;
            smokeCloudUntil = hiddenUntil;
            openerArmed = true;
            life.Hidden.Value = true;
            // Everything hunting you loses you: they stop, look about, and pick someone else.
            var lost = new System.Collections.Generic.List<Vector3>();
            foreach (var brain in FindObjectsByType<Enemies.EnemyBrain>(FindObjectsSortMode.None))
                if (!brain.IsDead && Vector3.Distance(brain.transform.position, transform.position) < 25f && brain.ServerForget(health))
                    lost.Add(brain.GetComponent<Health>().Center + Vector3.up * 1.2f);
            if (lost.Count > 0) LostYouRpc(lost.ToArray());
            // Rank 3: the burst of smoke staggers everything close — Exposed for the party.
            if (progress.Twist(AbilitySlot.E))
                foreach (var h in CombatQuery.Circle(transform.position, 5f, Team.Enemies).ToArray())
                    if (h.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(1.5f);
            SmokeFxRpc(transform.position, SmokeSeconds * progress.Potency(AbilitySlot.E));
        }

        /// <summary>
        /// Leave the smoke. Attacking ends it at once; an opener in flight (thrown knives)
        /// still lands as one for a short grace window, after which it is gone.
        /// </summary>
        private void EndHidden()
        {
            if (!life.Hidden.Value) return;
            life.Hidden.Value = false;
            openerExpires = Time.time + 0.6f;
        }

        // ==================================================================
        // Presentation
        // ==================================================================

        private void OnHiddenChanged(bool hidden)
        {
            // Others see only a faint ring where the Rogue is, so teammates can follow;
            // the owner keeps their view model but sees it darkened through the smoke.
            if (player.Rig != null && !IsOwner) player.Rig.gameObject.SetActive(!hidden);
            if (hidden && hiddenMarker == null && !IsOwner)
            {
                hiddenMarker = MeshKit.Part(transform, "ShadowMark", MeshKit.Lathe("shadowRing", new[]
                {
                    new Vector2(0.4f, 0f), new Vector2(0.46f, 0.02f), new Vector2(0.4f, 0.04f),
                }, 16, false), Mat.Glow, Vector3.up * 0.04f);
                ArtLibrary.SetGlow(hiddenMarker.GetComponent<Renderer>(), new Color(0.55f, 0.35f, 0.9f) * 1.1f);
            }
            if (hiddenMarker != null) hiddenMarker.SetActive(hidden);
            if (!hidden) Sfx.Play(Sound.Whisper, transform.position + Vector3.up, 0.5f, 1.8f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StabFxRpc(bool hit)
        {
            SwingTrail.Spawn(transform, 1.5f, 70f, Random.value < 0.5f ? 1f : -1f, player.TrailColor(new Color(0.75f, 0.6f, 1f) * 0.9f));
            if (!IsOwner)
            {
                player.Rig?.Play(RigAction.Swing, 0.18f);
                Sfx.Play(Sound.Swing, transform.position, 0.4f, 1.5f);
            }
            if (hit)
            {
                Sfx.Play(Sound.Hit, transform.position + transform.forward * 1.2f + Vector3.up, 0.7f, 1.3f);
                if (IsOwner) player.ConfirmHit(0.6f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FanFxRpc(Vector3 origin, Vector3 direction)
        {
            for (int i = -1; i <= 1; i++) Arrows.SpawnVisual(origin, Quaternion.Euler(0f, i * 7f, 0f) * direction, ProjectileKind.Knife, FanSpeed, FanRange * RangeMul, FanGravity);
            Sfx.Play(Sound.Bow, origin, 0.6f, 1.6f);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.25f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StepFxRpc(Vector3 from, Vector3 to)
        {
            Sfx.Play(Sound.Whisper, from + Vector3.up, 0.7f, 2f);
            Sfx.Play(Sound.Dodge, to + Vector3.up, 0.8f, 0.8f);
            SmokePuff.Spawn(from, 0.8f);
            SmokePuff.Spawn(to, 0.6f);
            var shadow = new Color(0.55f, 0.35f, 0.9f);
            Vfx.Embers(from + Vector3.up, 10, shadow, 3f, 0.05f, 0.6f);
            Vfx.Embers(to + Vector3.up, 10, shadow, 3f, 0.05f, 0.6f);
            FxSprite.Billboard("twirl_02", from + Vector3.up, Vector3.zero, 0.35f, 1.1f, 0.1f, new Color(0.35f, 0.2f, 0.6f, 0.85f), 600f);
            FxSprite.Billboard("twirl_02", to + Vector3.up, Vector3.zero, 0.35f, 0.2f, 1.2f, new Color(0.35f, 0.2f, 0.6f, 0.7f), -600f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SmokeFxRpc(Vector3 at, float seconds)
        {
            // A sharp pop, a burst ring, then a cloud that hangs in the air while you work.
            Sfx.Play(Sound.Slam, at, 0.6f, 1.9f);
            Sfx.Play(Sound.Whisper, at + Vector3.up, 0.8f, 0.7f);
            Vfx.Ring(at, SmokeCloudRadius, new Color(0.5f, 0.42f, 0.65f), 0.35f);
            Vfx.Embers(at + Vector3.up * 0.5f, 12, new Color(0.6f, 0.4f, 0.95f), 4f, 0.05f, 0.6f);
            SmokeCloud.Spawn(at, SmokeCloudRadius, seconds);
            if (IsOwner) hiddenFor = seconds;
        }

        /// <summary>Owner: how long the current smoke lasts, for the HUD's countdown.</summary>
        public float HiddenDuration => hiddenFor;
        private float hiddenFor = SmokeSeconds;

        [Rpc(SendTo.ClientsAndHost)]
        private void LostYouRpc(Vector3[] at)
        {
            foreach (var p in at) UI.GameHud.ShowCombo(p, "?", new Color(0.75f, 0.6f, 1f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OpenerFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Shatter, at, 0.9f, 0.8f);
            UI.GameHud.Feed("Ambush!", new Color(0.75f, 0.55f, 1f));
            Vfx.Flash(at, new Color(0.7f, 0.4f, 1f), 5f, 5f, 0.3f);
            Vfx.Embers(at, 14, new Color(0.75f, 0.45f, 1f), 5f, 0.06f, 0.6f);
        }
    }
}
