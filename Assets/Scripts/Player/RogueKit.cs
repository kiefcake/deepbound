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
    [RequireComponent(typeof(Health))]
    public class RogueKit : NetworkBehaviour, IClassKit
    {
        public const float StabInterval = 0.26f, StabRange = 2.2f, StabArc = 75f, StabDamage = 13f, StabEnergy = 6f;

        public const float BehindRefund = 3f, WindedSlow = 1.8f;
        private bool winded;
        public string ResourceHint => winded ? "Winded: attack from behind" : LocalBehind ? "Behind target: stabs are free" : "Frontal stabs cost Energy";
        public const float FanCooldown = 1.8f, FanEnergy = 30f, FanDamage = 15f, FanSpeed = 30f, FanRange = 14f, FanGravity = 6f;
        public float AttackRange => StabRange * (progress != null ? progress.ReachMultiplier : 1f);
        private float RangeMul => progress != null ? progress.RangeMultiplier : 1f;
        private float Reach => StabRange * (progress != null ? progress.ReachMultiplier : 1f);
        public const float StepCooldown = 9f, StepRange = 13f;
        public const float SmokeCooldown = 18f, SmokeSeconds = 7f;
        public const float AmbushMultiplier = 1.5f, OpenerMultiplier = 2f, OpenerStun = 1.2f;
        public const float BossAmbushMultiplier = 1.3f, BossAssassinMultiplier = 1.6f;
        public const float EnergyMax = 100f, EnergyRegen = 22f;

        public const float DashRange = 7f, DashCooldownShare = 0.5f;

        public const int FlourishEvery = 5, FlourishEveryMastered = 4;
        public const float FlourishDamage = 2.5f, FlourishStun = 0.5f, FlourishGap = 1.2f;

        public bool LocalBehind { get; private set; }
        private float nextBehindCheck;

        public bool DebugDash() => TryShadowDash();

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
        public const float SmokeCloudRadius = 3.5f, SelfVeilSeconds = 3f;
        private float selfVeilUntil;
        private float side = 1f;
        private float lastServerStab;

        private ulong flourishTarget;
        private int flourishCount;
        private float flourishLast;

        public int Flourishes { get; private set; }

        private float hiddenUntil;
        private bool openerArmed;

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
            if (IsServer) Health.AnyDied += OnSomethingDied;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) Health.AnyDied -= OnSomethingDied;
            base.OnNetworkDespawn();
        }

        public const float FreeStepWindow = 3f, DanceRange = 8f, UnseenSeconds = 1.5f, SmokeChainGap = 4f;
        private float freeStepUntil, smokeWindowUntil, lastSmokeChain = -10f;

        [Rpc(SendTo.Server)]
        private void StepArrivedRpc(ulong targetId, Vector3 from)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var obj) || !obj.TryGetComponent<Health>(out var t)) return;
            ServerStepArrived(t, from);
        }

        public void ServerStepArrived(Health t, Vector3 from)
        {
            if (t == null || !t.IsAlive) return;
            float att = progress.AttackMultiplier;
            ulong me = NetworkObjectId;

            if (progress.A(AbilitySlot.Q, 1)) { AbilityKit.Bleed(this, t, StabDamage * att * 1.3f, 4f, me, "Shadow Gash"); progress.AbilityTextRpc(t.Center + Vector3.up, "GASHED", PlayerProgress.FxKind.Blood); }

            if (progress.A(AbilitySlot.Q, 2)) { openerArmed = true; openerExpires = Time.time + 2f; }

            if (progress.B(AbilitySlot.Q, 2) && !AbilityKit.IsBoss(t)) { AbilityKit.PullTo(t, from, 1.2f); AbilityKit.Stun(t, 1f); progress.AbilityTextRpc(t.Center + Vector3.up, "SWITCH", PlayerProgress.FxKind.Shadow); }

            if (progress.A(AbilitySlot.Q, 3)) { t.ServerMark(6f); progress.AbilityTextRpc(t.Center + Vector3.up * 1.4f, "DEATH MARK", PlayerProgress.FxKind.Shadow); }

            if (progress.B(AbilitySlot.Q, 3)) t.ServerChill(4f);

            if (progress.A(AbilitySlot.Q, 4))
            {
                var cut = new HashSet<Health> { t };
                var last = t;
                for (int i = 0; i < 2; i++)
                {
                    var next = AbilityKit.Nearest(last.transform.position, DanceRange, cut);
                    if (next == null) break;
                    cut.Add(next);
                    next.Tag("Blade Dance").ServerDamage(StabDamage * OpenerMultiplier * att, last.transform.position, me);
                    progress.AbilityLinkRpc(last.Center, next.Center, PlayerProgress.FxKind.Shadow);
                    last = next;
                }
            }

            if (progress.B(AbilitySlot.Q, 4)) ServerUnseen(UnseenSeconds);
        }

        private void ServerUnseen(float seconds)
        {
            life.VeiledUntil = Mathf.Max(life.VeiledUntil, Time.time + seconds);
            foreach (var brain in FindObjectsByType<Enemies.EnemyBrain>(FindObjectsSortMode.None))
                if (!brain.IsDead && Vector3.Distance(brain.transform.position, transform.position) < 20f) brain.ServerForget(health);
            progress.AbilityTextRpc(transform.position + Vector3.up * 2.2f, "UNSEEN", PlayerProgress.FxKind.Shadow);
        }

        private void OnSomethingDied(Health dead)
        {
            if (!Active || dead == null || dead.Team != Team.Enemies || dead.LastHitFrom != NetworkObjectId) return;

            if (progress.OnPath(Classes.PathTriads.Path)) ServerUnseen(1.5f);
            if (!progress.A(AbilitySlot.E, 4) || Time.time > smokeWindowUntil || Time.time - lastSmokeChain < SmokeChainGap) return;
            lastSmokeChain = Time.time;
            progress.AbilityTextRpc(transform.position + Vector3.up * 2.2f, "SMOKE CHAIN", PlayerProgress.FxKind.Shadow);
            ServerSmoke(true);
        }

        private System.Collections.IEnumerator SmokeHazard(Vector3 at, float seconds, bool choke, bool caltrops)
        {
            float end = Time.time + seconds;
            float att = progress.AttackMultiplier;
            while (Time.time < end)
            {
                yield return new WaitForSeconds(0.5f);
                foreach (var h in CombatQuery.Circle(at, SmokeCloudRadius, Team.Enemies).ToArray())
                {
                    if (choke) h.Tag("Choking Smoke").ServerDamage(StabDamage * 0.6f * att * 0.5f, at, NetworkObjectId);
                    if (caltrops) { h.Tag("Caltrops").ServerDamage(StabDamage * 0.35f * att * 0.5f, at, NetworkObjectId); h.ServerChill(1f); }
                }
            }
        }

        private void Update()
        {
            if (IsServer && life.Hidden.Value && (Time.time >= hiddenUntil || !life.CanAct || !Active)) EndHidden();

            if (IsServer && Time.time < smokeCloudUntil)
                foreach (var ally in PlayerLife.All)
                    if (ally != null && ally != life && Vector3.Distance(ally.transform.position, smokeCloudAt) < SmokeCloudRadius)
                        ally.VeiledUntil = Time.time + 0.25f;

            if (IsServer && Time.time < selfVeilUntil && Vector3.Distance(transform.position, smokeCloudAt) < SmokeCloudRadius)
                life.VeiledUntil = Mathf.Max(life.VeiledUntil, Time.time + 0.25f);

            if (!IsOwner || !Active) return;

            Energy = Mathf.Min(EnergyMax, Energy + EnergyRegen * Time.deltaTime);
            motor.SpeedMultiplier = progress.SpeedMultiplier * (life.Hidden.Value ? 1.3f : 1f);
            motor.CanSprint = true;

            if (Time.time >= nextBehindCheck) { nextBehindCheck = Time.time + 0.1f; LocalBehind = life.CanAct && FacingABack(); }
            if (!life.CanAct || !motor.ActionsReady) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (Energy <= 0.5f && !winded)
            {
                winded = true;
                Sfx.Play2D(Sound.Heartbeat, 0.6f);
                UI.GameHud.ShowCombo(player.AimOrigin + player.AimForward * 2.5f, "WINDED", new Color(0.75f, 0.7f, 0.68f));
            }
            else if (Energy >= EnergyMax / 3f) winded = false;
            if (UI.Binds.PrimaryHeld && Time.time >= nextStab)
            {
                if (LocalBehind) Energy = Mathf.Min(EnergyMax, Energy + BehindRefund);
                else Energy = Mathf.Max(0f, Energy - StabEnergy);
                side = -side;
                nextStab = Time.time + StabInterval * progress.AttackTimeMultiplier * (winded ? WindedSlow : 1f);
                player.ViewModelSwing(0.04f, StabInterval * 0.8f, side);
                Sfx.Play(Sound.Swing, transform.position, 0.4f, 1.5f);

                MeleeStep.TryStepIn(motor, transform, player.AimForward, AttackRange, 12f, 0.9f);
                StabRpc(player.AimOrigin, player.AimForward, side);
            }

            if (UI.Binds.SecondaryDown && Time.time >= fanReadyAt && Energy >= FanEnergy)
            {
                Energy -= FanEnergy;
                fanReadyAt = Time.time + FanCooldown;
                player.ViewModelCast(false);
                FanRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward);
            }

            bool freeStep = Time.time < freeStepUntil;
            if (UI.Binds.Down(UI.Bind.AbilityQ) && (Time.time >= stepReadyAt || freeStep))
            {
                bool stepped = false;
                if (TryShadowstep()) { if (!freeStep) stepReadyAt = Time.time + StepCooldown * progress.CooldownFor(AbilitySlot.Q); stepped = true; }
                else if (TryShadowDash()) { if (!freeStep) stepReadyAt = Time.time + StepCooldown * DashCooldownShare * progress.CooldownFor(AbilitySlot.Q); stepped = true; }

                if (stepped) freeStepUntil = freeStep ? 0f : progress.B(AbilitySlot.Q, 1) ? Time.time + FreeStepWindow : 0f;
            }

            if (UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= smokeReadyAt)
            {
                smokeReadyAt = Time.time + SmokeCooldown * progress.CooldownFor(AbilitySlot.E);
                SmokeRpc();
            }
        }

        private bool TryShadowstep()
        {
            Health best = null;
            float bestAngle = 10f;
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                var to = h.Center - player.AimOrigin;
                if (to.magnitude > StepRange * RangeMul) continue;
                float angle = Vector3.Angle(player.AimForward, to);
                if (angle < bestAngle && CombatQuery.ClearLine(player.AimOrigin, h)) { bestAngle = angle; best = h; }
            }
            if (best == null) return false;

            var back = -best.transform.forward;
            back.y = 0f;
            back.Normalize();
            float radius = best.TryGetComponent<CapsuleCollider>(out var cap) ? cap.radius * best.transform.lossyScale.x : 0.4f;
            var spot = best.transform.position + back * (radius + 0.9f);

            if (Physics.Raycast(best.Center, back, radius + 1.1f, ~0, QueryTriggerInteraction.Ignore))
                spot = best.transform.position + best.transform.right * (radius + 0.9f);
            if (!Physics.Raycast(spot + Vector3.up * 1.5f, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore)) return false;

            var from = transform.position;
            var face = best.transform.position - floor.point;
            face.y = 0f;
            motor.Teleport(floor.point + Vector3.up * 0.05f, Quaternion.LookRotation(face));
            StepFxRpc(from, transform.position);
            StepArrivedRpc(best.NetworkObjectId, from);
            return true;
        }

        private bool TryShadowDash()
        {
            var flat = player.AimForward; flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return false;
            flat.Normalize();
            var chest = transform.position + Vector3.up * 1.0f;
            float dist = DashRange;
            if (Physics.SphereCast(chest, 0.3f, flat, out var wall, dist, ~0, QueryTriggerInteraction.Ignore)) dist = wall.distance - 0.4f;
            if (dist < 1.5f) return false;
            var spot = chest + flat * dist;
            if (!Physics.Raycast(spot + Vector3.up * 0.8f, Vector3.down, out var floor, 3.5f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(floor.point.y - transform.position.y) > 1.6f) return false;
            var from = transform.position;
            motor.Teleport(floor.point + Vector3.up * 0.05f, Quaternion.LookRotation(flat));
            StepFxRpc(from, transform.position);
            return true;
        }

        private bool FacingABack()
        {
            float reach = Reach * 1.4f;
            foreach (var h in CombatQuery.Arc(player.AimOrigin, player.AimForward, reach, 30f, Team.Enemies))
            {
                if (h == null || !h.IsAlive) continue;
                var toMe = transform.position - h.transform.position; toMe.y = 0f;
                return toMe.sqrMagnitude > 0.01f && Vector3.Angle(h.transform.forward, toMe) > 115f;
            }
            return false;
        }

        private float Multiplier(Health target, out bool opener)
        {
            opener = false;
            float m = progress.AttackMultiplier;
            var toMe = transform.position - target.transform.position;
            toMe.y = 0f;
            bool behind = toMe.sqrMagnitude > 0.01f && Vector3.Angle(target.transform.forward, toMe) > 115f;
            lastHitFromBehind = behind;

            bool boss = target.TryGetComponent<Enemies.BossBrain>(out _);
            if (behind) m *= (progress.OnPath(1) ? (boss ? BossAssassinMultiplier : 2f) : (boss ? BossAmbushMultiplier : AmbushMultiplier)) * Combos.TryShatter(target, progress);
            if (openerArmed && (life.Hidden.Value || Time.time < openerExpires))
            {
                m *= OpenerMultiplier;
                opener = true;
            }
            return m;
        }

        private void AfterHit(Health target, bool opener)
        {
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

        public void ServerStab(Vector3 origin, Vector3 forward)
        {
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.5f) > 2f) origin = transform.position + Vector3.up * 1.5f;

            int hits = 0, maxHits = progress.OnPath(2) ? 2 : 1;
            foreach (var target in CombatQuery.Arc(origin, forward, Reach, StabArc, Team.Enemies).ToArray())
            {
                float m = Multiplier(target, out bool opener);
                bool flourish = hits == 0 && progress.OnPath(2) && CountFlourish(target);
                if (flourish) m *= FlourishDamage;
                var zone = HitZones.Aimed(target, origin, forward, Reach, out var zonePoint);
                m *= HitZones.Multiplier(target, zone, false);
                target.Tag("Stab").ServerDamage(StabDamage * m, transform.position, NetworkObjectId);

                if (progress.OnPath(1) && lastHitFromBehind && target.IsAlive) AbilityKit.Bleed(this, target, StabDamage * progress.AttackMultiplier * 0.8f, 3f, NetworkObjectId, "Bleed");
                if (zone != HitZone.Body) Arrows.ReportZone(NetworkObjectId, zonePoint, zone);
                AfterHit(target, opener);
                if (flourish)
                {
                    Flourishes++;
                    if (target.IsAlive && target.TryGetComponent<ICrowdControl>(out var fcc)) fcc.ServerStun(FlourishStun);
                    FlourishFxRpc(target.Center);
                }
                if (++hits >= maxHits) break;
            }
            bool hitAny = hits > 0;
            if (life.Hidden.Value) EndHidden();
            StabFxRpc(hitAny);
        }

        private bool CountFlourish(Health target)
        {
            if (target.NetworkObjectId != flourishTarget || Time.time - flourishLast > FlourishGap) { flourishTarget = target.NetworkObjectId; flourishCount = 0; }
            flourishLast = Time.time;
            flourishCount++;
            int every = progress.Mastered(2) ? FlourishEveryMastered : FlourishEvery;
            if (flourishCount < every) return false;
            flourishCount = 0;
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FlourishFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Block, at, 0.8f, 1.9f);
            Vfx.AbilityFlash(at, new Color(1f, 0.85f, 0.5f), 3f, 3f, 0.2f);
            if (IsOwner) UI.GameHud.ShowCombo(at + Vector3.up * 0.5f, "FLOURISH", new Color(1f, 0.85f, 0.5f));
        }

        [Rpc(SendTo.Server)]
        private void FanRpc(Vector3 origin, Vector3 direction)
        {
            if (!life.CanAct) return;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            ulong me = NetworkObjectId;
            int spread = progress.Mastered(2) ? 2 : 1;
            for (int i = -spread; i <= spread; i++)
            {
                var dir = Quaternion.Euler(0f, i * 7f, 0f) * direction;
                Arrows.ServerFire(origin, dir, 0f, Team.Enemies, me, FanSpeed, h =>
                {
                    if (this == null) return;
                    float m = Multiplier(h, out bool opener);
                    h.Tag("Knives").ServerDamage(FanDamage * m, transform.position, me);
                    AfterHit(h, opener);
                }, false, FanRange * RangeMul);
            }
            if (life.Hidden.Value) EndHidden();
            FanFxRpc(origin, direction, spread);
        }

        [Rpc(SendTo.Server)]
        private void SmokeRpc() => ServerSmoke();

        public void ServerSmoke() => ServerSmoke(false);

        private void ServerSmoke(bool chained)
        {
            if (!life.CanAct) return;
            float smokeFor = SmokeSeconds * progress.Potency(AbilitySlot.E);
            hiddenUntil = Time.time + smokeFor;
            smokeWindowUntil = hiddenUntil + 1f;
            smokeCloudAt = transform.position;
            smokeCloudUntil = hiddenUntil;
            selfVeilUntil = Time.time + SelfVeilSeconds;
            openerArmed = true;
            life.Hidden.Value = true;

            var lost = new System.Collections.Generic.List<Vector3>();
            foreach (var brain in FindObjectsByType<Enemies.EnemyBrain>(FindObjectsSortMode.None))
                if (!brain.IsDead && Vector3.Distance(brain.transform.position, transform.position) < 25f && brain.ServerForget(health))
                    lost.Add(brain.GetComponent<Health>().Center + Vector3.up * 1.2f);
            if (lost.Count > 0) LostYouRpc(lost.ToArray());
            var at = transform.position;

            if (progress.B(AbilitySlot.E, 1))
                foreach (var h in CombatQuery.Circle(at, 5f, Team.Enemies).ToArray())
                    AbilityKit.Stun(h, 1.5f);

            if (progress.A(AbilitySlot.E, 1) || progress.A(AbilitySlot.E, 2)) StartCoroutine(SmokeHazard(at, smokeFor, progress.A(AbilitySlot.E, 1), progress.A(AbilitySlot.E, 2)));

            if (progress.B(AbilitySlot.E, 2) && !chained)
            {
                health.ServerHeal(health.Max.Value * 0.15f);
                if (TryGetComponent<PlayerDebuffs>(out var db)) db.ServerCleanse();
            }

            if (progress.A(AbilitySlot.E, 3))
            {
                AbilityKit.Blast(at, 4f, StabDamage * 3f * progress.AttackMultiplier, NetworkObjectId, "Flashpowder", h => h.ServerScorch(4f, NetworkObjectId));
                progress.AbilityBurstRpc(at, 4f, PlayerProgress.FxKind.Fire);
            }

            if (progress.B(AbilitySlot.E, 3)) progress.ServerRally(smokeFor, 1f);

            if (progress.B(AbilitySlot.E, 4))
                foreach (var h in CombatQuery.Circle(at, 8f, Team.Enemies).ToArray()) { h.ServerMark(6f); progress.AbilityTextRpc(h.Center + Vector3.up, "MARKED", PlayerProgress.FxKind.Shadow); }
            SmokeFxRpc(at, smokeFor);
        }

        private void EndHidden()
        {
            if (!life.Hidden.Value) return;
            life.Hidden.Value = false;
            openerExpires = Time.time + 0.6f;
        }

        private void OnHiddenChanged(bool hidden)
        {
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

        private float stabHand = 1f;

        [Rpc(SendTo.ClientsAndHost)]
        private void StabFxRpc(bool hit)
        {
            SwingTrail.Spawn(transform, 1.5f, 70f, Random.value < 0.5f ? 1f : -1f, player.TrailColor(new Color(0.75f, 0.6f, 1f) * 0.9f));

            stabHand = -stabHand;
            if (!IsOwner || NetworkPlayer.DebugShowSelf) ClassFx.RogueThrust(transform, stabHand);
            if (hit) ClassFx.RogueNick(transform.position + transform.forward * 1.2f + Vector3.up * 1.15f);
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
        private void FanFxRpc(Vector3 origin, Vector3 direction, int spread)
        {
            for (int i = -spread; i <= spread; i++) Arrows.SpawnVisual(origin, Quaternion.Euler(0f, i * 7f, 0f) * direction, ProjectileKind.Knife, FanSpeed, FanRange * RangeMul, FanGravity);
            Sfx.Play(Sound.Bow, origin, 0.6f, 1.6f);
            if (!IsOwner) player.Rig?.Play(RigAction.Fan, 0.35f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StepFxRpc(Vector3 from, Vector3 to)
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Vanish, 0.35f);
            Sfx.Play(Sound.Whisper, from + Vector3.up, 0.7f, 2f);
            Sfx.Play(Sound.Dodge, to + Vector3.up, 0.8f, 0.8f);
            SmokePuff.Spawn(from, 0.8f);
            SmokePuff.Spawn(to, 0.6f);
            var shadow = new Color(0.55f, 0.35f, 0.9f);
            Vfx.Embers(to + Vector3.up, 10, shadow, 3f, 0.05f, 0.6f);

            ClassFx.Afterimage(transform, player.Rig != null ? player.Rig.transform : null, from, 1.1f);
            ClassFx.ShadowTear(from, to);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SmokeFxRpc(Vector3 at, float seconds)
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Vanish, 0.55f);

            Sfx.Play(Sound.Slam, at, 0.6f, 1.9f);
            Sfx.Play(Sound.Whisper, at + Vector3.up, 0.8f, 0.7f);
            SmokeCloud.Spawn(at, SmokeCloudRadius, seconds);

            ClassFx.SmokeDome(at, SmokeCloudRadius);
            if (IsOwner) hiddenFor = seconds;
        }

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
            Vfx.AbilityFlash(at, new Color(0.7f, 0.4f, 1f), 5f, 5f, 0.3f);
            Vfx.Embers(at, 14, new Color(0.75f, 0.45f, 1f), 5f, 0.06f, 0.6f);

            ClassFx.CrossCut(at, 2f);
        }
    }
}
