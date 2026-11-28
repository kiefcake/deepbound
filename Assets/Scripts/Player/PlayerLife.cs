using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Core;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using Deepbound.World;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    [RequireComponent(typeof(Health))]
    public class PlayerLife : NetworkBehaviour
    {
        public const float ReviveSeconds = 3f;
        public const float ReviveRange = 2.6f;
        private const float ReviveHealthFraction = 0.35f;

        public static readonly List<PlayerLife> All = new List<PlayerLife>();

        public readonly NetworkVariable<bool> Downed = new NetworkVariable<bool>();

        [Rpc(SendTo.Server)]
        public void FallRpc(float drop)
        {
            if (Downed.Value) return;
            var h = GetComponent<Health>();
            if (h == null || !h.IsAlive) return;
            float share = Mathf.Clamp((Mathf.Min(drop, 30f) - FirstPersonMotor.SafeDrop) * FirstPersonMotor.FallShare, 0f, FirstPersonMotor.FallMax);
            if (share <= 0.001f) return;
            h.Tag("fall").ServerDamage(h.Max.Value * share + h.Defense * Health.DefenseWeight, transform.position + Vector3.up * 2f);
        }

        public const float DrownShare = 0.12f;
        private float nextDrownAt;

        [Rpc(SendTo.Server)]
        public void DrownRpc()
        {
            if (Downed.Value || !Health.IsAlive || Time.time < nextDrownAt || !Breath.HeadUnder(transform.position, Breath.EyeHeight * 0.6f)) return;
            nextDrownAt = Time.time + Breath.DrownEvery * 0.9f;
            Health.Unavoidable().Tag("drowning").ServerDamage(Health.Max.Value * DrownShare + Health.Defense * Health.DefenseWeight, transform.position + Vector3.up);
        }

        public const float BleedOutSeconds = 40f;

        public readonly NetworkVariable<float> Bleed = new NetworkVariable<float>();

        public readonly NetworkVariable<bool> Soul = new NetworkVariable<bool>();

        public float BleedLeft => Mathf.Max(0f, BleedOutSeconds - Bleed.Value);

        public readonly NetworkVariable<bool> Hidden = new NetworkVariable<bool>();

        public readonly NetworkVariable<bool> Crouched = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public const float CrouchSightScale = 0.5f, CrouchStepVolume = 0.3f;

        public bool Targetable => !Downed.Value && !Hidden.Value && Health.IsAlive && Time.time >= VeiledUntil;

        public float VeiledUntil { get; set; }

        public static bool CanTarget(Health h) => h != null && h.IsAlive && (!h.TryGetComponent<PlayerLife>(out var p) || p.Targetable);
        public readonly NetworkVariable<float> ReviveProgress = new NetworkVariable<float>();
        private float reviveServer;
        private PlayerLife helper;
        private float lastHelperPing = -10f;

        public const float TwoHandsRate = 2f;
        public const float ReviveGrace = 1.5f;

        public bool TwoHands { get; private set; }

        public Health Health { get; private set; }

        private PlayerLife reviver;
        private float lastReviverPing;
        private float reviveSendAt;
        private float lastDamagedAt;
        private float regenCarry;
        private PlayerProgress progress;
        private FirstPersonMotor motor;

        public const float DodgeCooldown = 1.2f, DodgeInvulnerable = 0.35f, DodgeSpeed = 15f;
        public float DodgeReadyAt { get; private set; }
        public float DodgeCooldownNow => progress != null && progress.HasRelic(Loot.Relic.GhostLantern) ? DodgeCooldown * 0.5f : DodgeCooldown;
        private float invulnerableUntil;

        private bool dodgeFrames, perfectThisDodge;

        public void ServerDebugInvulnerable(float seconds) { invulnerableUntil = Time.time + seconds; dodgeFrames = false; }

        public void ServerDebugDodge() { invulnerableUntil = Time.time + DodgeInvulnerable; dodgeFrames = true; perfectThisDodge = false; }

        private void ServerPerfectDodge()
        {
            if (progress != null) progress.PerfectStrikeUntil = Time.time + PlayerProgress.PerfectStrikeSeconds;
            PerfectDodges++;
            PerfectDodgeRpc();
        }

        public int PerfectDodges { get; private set; }

        [Rpc(SendTo.ClientsAndHost)]
        private void PerfectDodgeRpc()
        {
            if (IsOwner) DodgeReadyAt = Time.time;
            Sfx.Play(Sound.Graze, transform.position + Vector3.up, 0.9f, 0.8f);
            Sfx.Play(Sound.Dodge, transform.position + Vector3.up, 0.6f, 1.6f);
            Art.ClassFx.PerfectDodge(transform, PlayerProgress.PerfectStrikeSeconds);
            if (IsOwner && TryGetComponent<NetworkPlayer>(out var np)) np.AddShake(0.12f);
        }

        private void Awake()
        {
            Health = GetComponent<Health>();
            progress = GetComponent<PlayerProgress>();
            debuffs = GetComponent<PlayerDebuffs>();
            motor = GetComponent<FirstPersonMotor>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer)
            {
                Health.Died += HandleDied;
                Health.DamagedServer += (lost, from) => { lastDamagedAt = Time.time; ServerRecordHit(lost, from); };

                Health.IncomingModifiers.Insert(0, (dmg, from) =>
                {
                    if (Time.time >= invulnerableUntil || Health.InUnavoidable) return dmg;
                    if (dodgeFrames && !perfectThisDodge && dmg > 0f) { perfectThisDodge = true; ServerPerfectDodge(); }
                    return 0f;
                });

                Health.IncomingModifiers.Insert(1, (dmg, from) =>
                {
                    if (progress == null || !progress.CatWhisker.Value || dmg <= 0f || Health.InUnavoidable || Random.value >= PlayerProgress.WhiskerEvade) return dmg;
                    EvadedRpc();
                    return 0f;
                });

                Health.IncomingModifiers.Add((dmg, from) => dmg > 0f && KnightKit.ShelterFor(this, from) ? dmg * KnightKit.ShelterScale : dmg);

                Health.IncomingModifiers.Add((dmg, from) => Classes.AbilityKit.AllyIncoming(this, dmg, from));
            }
            Downed.OnValueChanged += HandleDownedChanged;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsServer) Health.Died -= HandleDied;
            Downed.OnValueChanged -= HandleDownedChanged;
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public bool CanAct => !Downed.Value && !HandsFull && !IsDragging(NetworkObjectId) && !Reviving && Seat.Value < 0 && !(debuffs != null && debuffs.Manic) && !(pouch != null && pouch.InStasis);

        public bool Reviving => IsOwner && Time.time < revivingUntil;
        private float revivingUntil, usePressAt = -10f;
        private bool useTapPending;

        public const float TapSeconds = 0.3f;

        public bool UseHasWorldTarget()
        {
            if (DownedNear(transform.position, this) != null || IsDragging(NetworkObjectId)) return true;
            if (UI.Dialogue.Aimed != null || UI.Dialogue.Open) return true;
            var ride = World.CarriageRide.Instance;
            if (ride != null && DungeonDirector.InLobby && !ride.Riding && (Seat.Value >= 0 || NearestFreeSeat(ride) >= 0)) return true;
            var cam = GetComponent<NetworkPlayer>();
            var aimed = cam != null ? World.Interactable.Nearest(cam.AimOrigin, cam.AimForward, progress) : null;
            return aimed != null && !(ShiftHeld && (aimed is Loot.GearDrop || aimed is World.CampCat));
        }

        public static bool ShiftHeld => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
        private Flasks pouch => cachedPouch != null ? cachedPouch : (cachedPouch = GetComponent<Flasks>());
        private Flasks cachedPouch;

        private bool HandsFull
        {
            get
            {
                if (!World.Carryable.IsCarrying(NetworkObjectId)) return false;

                return progress == null || !progress.HasRelic(Loot.Relic.LamplightersOil);
            }
        }

        public const float DragSpeed = 0.55f, DragGap = 1.15f, DragBreak = 4f;

        public readonly NetworkVariable<ulong> DraggedBy = new NetworkVariable<ulong>(ulong.MaxValue);
        public bool BeingDragged => DraggedBy.Value != ulong.MaxValue;

        public static PlayerLife DraggedByPlayer(ulong id)
        {
            foreach (var p in All) if (p != null && p.DraggedBy.Value == id) return p;
            return null;
        }

        public static bool IsDragging(ulong id) => DraggedByPlayer(id) != null;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DragRpc(NetworkObjectReference whoRef)
        {
            if (!whoRef.TryGet(out var o) || !o.TryGetComponent<PlayerLife>(out var r)) return;
            if (DraggedBy.Value == r.NetworkObjectId) { DraggedBy.Value = ulong.MaxValue; DragFxRpc(false); return; }
            if (!Downed.Value || Soul.Value || r == this || r.Downed.Value || BeingDragged || IsDragging(r.NetworkObjectId) || World.Carryable.IsCarrying(r.NetworkObjectId)) return;
            if (Vector3.Distance(r.transform.position, transform.position) > ReviveRange + 0.5f) return;
            DraggedBy.Value = r.NetworkObjectId;
            DragFxRpc(true);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DragFxRpc(bool grab) => Sfx.Play(grab ? Sound.Block : Sound.Step, transform.position + Vector3.up * 0.3f, 0.6f, grab ? 0.7f : 0.8f);

        private PlayerLife Dragger()
        {
            if (!BeingDragged || NetworkManager == null || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(DraggedBy.Value, out var o)) return null;
            return o.GetComponent<PlayerLife>();
        }

        private void ServerTickDrag()
        {
            if (!BeingDragged) return;
            var d = Dragger();
            if (!Downed.Value || Soul.Value || d == null || d.Downed.Value || Vector3.Distance(d.transform.position, transform.position) > DragBreak)
                DraggedBy.Value = ulong.MaxValue;
        }

        private bool wasDragged;

        private void OwnerDragged()
        {
            var d = Dragger();
            bool dragged = d != null && Downed.Value;
            if (dragged)
            {
                var back = d.transform.forward;
                back.y = 0f;
                var at = d.transform.position - back.normalized * DragGap;
                motor.PinTo(at);
                var face = d.transform.position - at;
                face.y = 0f;
                if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face);
            }
            else if (wasDragged) motor.Unpin(transform.position + Vector3.up * 0.1f);
            wasDragged = dragged;
        }

        private PlayerDebuffs debuffs;

        public readonly NetworkVariable<sbyte> Seat = new NetworkVariable<sbyte>(-1);
        private bool wasSeated;

        [Rpc(SendTo.Server)]
        public void SitRpc(int seat)
        {
            var ride = World.CarriageRide.Instance;
            if (ride == null || !DungeonDirector.InLobby || ride.Riding || seat < 0 || seat >= World.CarriageRide.SeatCount || Downed.Value) return;
            foreach (var p in All) if (p != null && p != this && p.Seat.Value == seat) return;
            if (Vector3.Distance(transform.position, ride.SeatGround(seat)) > 3.5f) return;
            Seat.Value = (sbyte)seat;
        }

        [Rpc(SendTo.Server)]
        public void StandRpc()
        {
            var ride = World.CarriageRide.Instance;
            if (ride != null && ride.Riding && DungeonDirector.InLobby) return;
            Seat.Value = -1;
        }

        private void OwnerSeat()
        {
            var ride = World.CarriageRide.Instance;
            var motor = GetComponent<FirstPersonMotor>();
            bool seated = Seat.Value >= 0 && ride != null && (DungeonDirector.InLobby || ride.Riding);
            if (seated)
            {
                if (!wasSeated) transform.rotation = Quaternion.Euler(0f, ride.SeatFacing.eulerAngles.y, 0f);
                motor.PinTo(ride.SeatPosition(Seat.Value));
            }
            else if (wasSeated)
            {
                motor.Unpin(ride != null && DungeonDirector.InLobby ? ride.SeatGround(Mathf.Max(0, lastSeat)) : transform.position);
            }
            if (seated) lastSeat = Seat.Value;
            wasSeated = seated;

            var keyboard = Keyboard.current;
            if (!UI.Binds.Down(UI.Bind.Use) || ride == null || !DungeonDirector.InLobby || ride.Riding || UI.GameHud.MenuOpen) return;
            if (Seat.Value >= 0) { StandRpc(); return; }
            int free = NearestFreeSeat(ride);
            if (free >= 0) SitRpc(free);
        }

        private int lastSeat;

        public int NearestFreeSeat(World.CarriageRide ride)
        {
            int best = -1;
            float bd = 2.6f;
            for (int i = 0; i < World.CarriageRide.SeatCount; i++)
            {
                bool taken = false;
                foreach (var p in All) if (p != null && p.Seat.Value == i) taken = true;
                if (taken) continue;
                float d = Vector3.Distance(transform.position, ride.SeatGround(i));
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public Vector3 ServerVelocity { get; private set; }
        private Vector3 velocitySampleAt;
        private float nextVelocitySample;

        private void TrackVelocity()
        {
            if (Time.time < nextVelocitySample) return;
            float dt = 0.1f;
            nextVelocitySample = Time.time + dt;
            var p = transform.position;
            var raw = (p - velocitySampleAt) / dt;
            velocitySampleAt = p;
            if (raw.sqrMagnitude > 400f) raw = Vector3.zero;
            ServerVelocity = Vector3.Lerp(ServerVelocity, raw, 0.5f);
        }

        private struct HitRecord { public string Who; public float Amount, Time; }
        private readonly List<HitRecord> recentHits = new List<HitRecord>();

        private void ServerRecordHit(float lost, ulong attackerId)
        {
            if (lost <= 0f) return;
            recentHits.Add(new HitRecord { Who = Combat.Health.NameOfHit(attackerId, Health), Amount = lost, Time = Time.time });
            if (recentHits.Count > 12) recentHits.RemoveAt(0);
        }

        public static event System.Action<string, string> DownedRecap;

        [Rpc(SendTo.Owner)]
        private void RecapRpc(string killer, string lines) => DownedRecap?.Invoke(killer, lines);

        private void ServerSendRecap()
        {
            var totals = new Dictionary<string, (float sum, int n)>();
            foreach (var r in recentHits)
            {
                if (Time.time - r.Time > 10f) continue;
                totals.TryGetValue(r.Who, out var t);
                totals[r.Who] = (t.sum + r.Amount, t.n + 1);
            }
            var order = new List<KeyValuePair<string, (float sum, int n)>>(totals);
            order.Sort((a, b) => b.Value.sum.CompareTo(a.Value.sum));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < order.Count && i < 5; i++)
                sb.Append(order[i].Key).Append(order[i].Value.n > 1 ? "  x" + order[i].Value.n : "").Append('\t').Append(Mathf.RoundToInt(order[i].Value.sum)).Append('\n');
            string killer = recentHits.Count > 0 ? recentHits[recentHits.Count - 1].Who : "the dark";
            recentHits.Clear();
            RecapRpc(killer, sb.ToString());
        }

        private void HandleDied(Health _)
        {
            if (progress != null && progress.ServerTryPhoenix())
            {
                Health.ServerSetFraction(progress.PhoenixFraction);
                return;
            }
            if (progress != null) progress.Downs.Value++;
            string by = Health.LastHitFrom == ulong.MaxValue ? "no attacker"
                : NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Health.LastHitFrom, out var killer) ? killer.name : "#" + Health.LastHitFrom;
            Debug.Log("[Down] " + name + " went down at " + transform.position.ToString("F1") + ", last hit by " + by + " from " + Health.LastHitAt.ToString("F1"));
            Downed.Value = true;
            downedAt = Time.time;
            Teamwork.ServerVengeance(this, Health.LastHitFrom);
            ReviveProgress.Value = 0f;
            reviveServer = 0f;
            Bleed.Value = 0f;
            bleedCarry = 0f;
            ServerSendRecap();
        }

        public void ServerBleedOut()
        {
            if (!IsServer || !Downed.Value || Soul.Value) return;
            Soul.Value = true;
            Bleed.Value = BleedOutSeconds;
            ReviveProgress.Value = 0f;
            reviveServer = 0f;
            reviver = null;
            Debug.Log("[Down] " + name + " bled out");
            if (DungeonDirector.Instance != null) DungeonDirector.Instance.ServerSpawnSoulSkull(this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EvadedRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.1f, "EVADED (whisker)", new Color(0.85f, 0.85f, 0.7f));
            Sfx.Play(Sound.Dodge, transform.position + Vector3.up, 0.6f, 1.4f);
        }

        private void HandleDownedChanged(bool was, bool now)
        {
            if (was && !now) Sfx.Play(Sound.Revive, transform.position);

            if (!was && now)
            {
                Sfx.Play(Sound.Toll, transform.position + Vector3.up, 0.8f, 1.25f);
                if (IsOwner) Sfx.Play2D(Sound.Heartbeat, 0.8f);
            }
        }

        [Rpc(SendTo.Server)]
        public void ReviveHoldRpc(NetworkObjectReference reviverRef)
        {
            if (!Downed.Value || Soul.Value || !reviverRef.TryGet(out var reviverObject)) return;
            var r = reviverObject.GetComponent<PlayerLife>();
            if (r == null || r == this || r.Downed.Value) return;
            if (Vector3.Distance(r.transform.position, transform.position) > ReviveRange + 0.5f) return;

            if (reviver != null && reviver != r && Time.time - lastReviverPing < 0.35f && !reviver.Downed.Value)
            {
                helper = r;
                lastHelperPing = Time.time;
                return;
            }
            reviver = r;
            lastReviverPing = Time.time;
        }

        private void Update()
        {
            if (IsOwner)
            {
                OwnerSeat();
                if (motor != null) OwnerDragged();
                OwnerReviveInput();
                OwnerDodgeInput();
            }
            if (!IsServer) return;
            TrackVelocity();
            ServerTickDrag();
            if (!Downed.Value)
            {
                ServerRegen();
                return;
            }

            if (Soul.Value) return;

            if (this.progress != null && this.progress.Salts.Value && Time.time - downedAt >= SaltsSeconds)
            {
                this.progress.Salts.Value = false;
                ServerRevive(0.3f);
                SaltsRpc();
                return;
            }
            bool beingRevived = reviver != null && Time.time - lastReviverPing < 0.35f && !reviver.Downed.Value &&
                                Vector3.Distance(reviver.transform.position, transform.position) <= ReviveRange + 0.5f;

            if (!beingRevived && !DungeonDirector.TestMode && AnyAllyStanding())
            {
                bleedCarry += Time.deltaTime * (BeingDragged ? 0.5f : 1f);
                if (Bleed.Value + bleedCarry >= BleedOutSeconds) { bleedCarry = 0f; ServerBleedOut(); return; }

                if (bleedCarry >= 0.25f) { Bleed.Value += bleedCarry; bleedCarry = 0f; }
            }

            float rate = beingRevived && reviver.progress != null && reviver.progress.ClassId.Value == Classes.ClassId.Cleric ? 2f : 1f;
            bool helped = beingRevived && helper != null && Time.time - lastHelperPing < 0.35f && !helper.Downed.Value &&
                          Vector3.Distance(helper.transform.position, transform.position) <= ReviveRange + 0.5f;
            if (helped) rate *= TwoHandsRate;

            if (beingRevived && Legacy.Has(reviver, Boon.LightSleeper)) rate *= Legacy.LightSleeperRate;
            TwoHands = helped;

            float progress = Mathf.Clamp(reviveServer + (beingRevived ? Time.deltaTime * rate : -Time.deltaTime * 0.5f), 0f, ReviveSeconds);
            reviveServer = progress;

            if (progress >= ReviveSeconds)
            {
                if (reviver != null && reviver.progress != null) reviver.progress.Revives.Value++;

                var saviour = Teamwork.InLastStand(reviver) ? reviver : null;
                ServerRevive(ReviveHealthFraction);
                ServerGrantReviveGrace();
                if (saviour != null) Teamwork.ServerClutch(saviour, this);
                return;
            }

            if (Mathf.Abs(progress - ReviveProgress.Value) > 0.01f || progress == 0f) ReviveProgress.Value = progress;
        }

        private void OwnerDodgeInput()
        {
            var keyboard = Keyboard.current;
            if (Downed.Value || motor == null || !motor.HasControl || Time.time < DodgeReadyAt) return;
            if (!UI.Binds.Down(UI.Bind.Dodge) && !(UI.Binds.Down(UI.Bind.Slide) && motor.IsSprinting)) return;

            var input = motor.MoveInput;

            if (!(input.y > 0.5f && Mathf.Abs(input.x) < 0.5f && motor.TryStartSlide()))
            {
                var dir = input.sqrMagnitude > 0.01f ? transform.right * input.x + transform.forward * input.y : -transform.forward;
                dir.y = 0f;
                motor.AddImpulse(dir.normalized * DodgeSpeed);
            }
            DodgeReadyAt = Time.time + DodgeCooldownNow;
            if (progress != null && progress.Wears(Loot.GearId.WraithstepCuirass)) progress.WraithstepUntil = Time.time + Progression.PlayerProgress.WraithstepSeconds;
            DodgeRpc();
        }

        [Rpc(SendTo.Server)]
        private void DodgeRpc()
        {
            invulnerableUntil = Time.time + DodgeInvulnerable;
            dodgeFrames = true; perfectThisDodge = false;
            if (progress != null) progress.ServerOnDodge();
            DodgeFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DodgeFxRpc() => Sfx.Play(Sound.Dodge, transform.position + Vector3.up, 0.7f);

        private void OwnerReviveInput()
        {
            var keyboard = Keyboard.current;
            if (Downed.Value) return;
            var target = DownedNear(transform.position, this);
            var dragging = DraggedByPlayer(NetworkObjectId);
            bool merged = UI.Binds.Merged;
            bool free = !UI.GameHud.MenuOpen && motor != null && motor.HasControl;

            if (!merged && UI.Binds.Down(UI.Bind.Interact) && free) ToggleDrag(target, dragging);
            if (merged && (target != null || dragging != null))
            {
                if (UI.Binds.Down(UI.Bind.Use) && free) { usePressAt = Time.time; useTapPending = true; }
                if (useTapPending && !UI.Binds.Held(UI.Bind.Use))
                {
                    useTapPending = false;
                    if (Time.time - usePressAt < TapSeconds && free) ToggleDrag(target, dragging);
                    return;
                }
                if (useTapPending && Time.time - usePressAt < TapSeconds) return;
                useTapPending = false;
            }

            if (target == null)
            {
                if (UI.Binds.Down(UI.Bind.Use) && free)
                {
                    if (UI.Dialogue.TryBegin()) return;
                    if (merged && World.CampStations.LocalAimed != null) return;
                    var cam = GetComponent<NetworkPlayer>();
                    var use = World.Interactable.Nearest(cam.AimOrigin, cam.AimForward, progress);

                    if (use != null && merged && ShiftHeld && (use is Loot.GearDrop || use is World.CampCat)) return;
                    if (use != null) use.UseRpc(NetworkObject);
                    else if (progress != null && progress.CanUseRope) progress.UseRopeRpc();
                }
                return;
            }

            if (!UI.Binds.Held(UI.Bind.Use)) return;
            revivingUntil = Time.time + 0.3f;
            if (Time.time < reviveSendAt) return;
            reviveSendAt = Time.time + 0.2f;
            target.ReviveHoldRpc(NetworkObject);
        }

        private void ToggleDrag(PlayerLife target, PlayerLife dragging)
        {
            if (dragging != null) dragging.DragRpc(NetworkObject);
            else if (target != null && !World.Carryable.IsCarrying(NetworkObjectId)) target.DragRpc(NetworkObject);
        }

        public const float VitRegenPerPoint = 0.02f, VitHurtPause = 4f, VitInCombat = 0.5f;

        public static bool VitRegenInTests;

        public static float VitShare(Classes.ClassId c)
        {
            switch (c)
            {
                case Classes.ClassId.Knight: case Classes.ClassId.Flagellant: return 1.3f;
                case Classes.ClassId.Rogue: return 1.05f;
                case Classes.ClassId.Cleric: return 0.85f;
                default: return 0.65f;
            }
        }

        public static float VitRegen(float vit, Classes.ClassId c) => Mathf.Max(0f, vit) * VitRegenPerPoint * VitShare(c);

        private void ServerRegen()
        {
            if (progress == null || progress.Stats == null || !Health.IsAlive) return;
            if (Health.Current.Value >= Health.Max.Value) { regenCarry = 0f; return; }

            if (World.DungeonDirector.TestMode && !VitRegenInTests && !progress.DesperateRegen) return;
            float rate = VitRegen(progress.Value(StatType.VIT), progress.ClassId.Value);
            rate *= progress.BuildRegenScale;
            if (Time.time - Health.LastHurtTime < VitHurtPause) rate *= VitInCombat;

            if (progress.DesperateRegen) rate += (2f + progress.Value(StatType.VIT) * 0.12f) * 3f;
            regenCarry += rate * Time.deltaTime;
            if (regenCarry >= 1f)
            {
                float whole = Mathf.Floor(regenCarry);
                regenCarry -= whole;
                Health.ServerHeal(whole);
            }
        }

        private float bleedCarry;
        private float downedAt;
        public const float SaltsSeconds = 5f;

        public bool GettingUp => Downed.Value && !Soul.Value && progress != null && progress.Salts.Value;

        [Rpc(SendTo.ClientsAndHost)]
        private void SaltsRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.1f, "SMELLING SALTS", new Color(0.9f, 0.95f, 0.8f));
            UI.GameHud.Feed(name + " comes round with a gasp (smelling salts)", new Color(0.85f, 0.95f, 0.8f));
            Sfx.Play(Sound.Relic, transform.position + Vector3.up, 0.6f, 1.4f);
        }

        private bool AnyAllyStanding()
        {
            foreach (var p in All) if (p != null && p != this && !p.Downed.Value) return true;
            return false;
        }

        public void ServerGrantReviveGrace()
        {
            invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + ReviveGrace);
            dodgeFrames = false;
        }

        public void ServerRevive(float healthFraction)
        {
            if (!IsServer) return;
            Progression.RunLog.NoteRevive(this);
            Soul.Value = false;
            Bleed.Value = 0f;
            var skull = World.SoulSkull.For(NetworkObjectId);
            if (skull != null && skull.NetworkObject.IsSpawned) skull.NetworkObject.Despawn();
            Health.ServerSetFraction(healthFraction);
            Downed.Value = false;
            ReviveProgress.Value = 0f;
            reviveServer = 0f;
            reviver = null;

            float bell = 0f;
            foreach (var p in All)
                if (p != null && p.progress != null && p.progress.HasRelic(Loot.Relic.RallyingBell) && Vector3.Distance(p.transform.position, transform.position) < 15f)
                    bell = Mathf.Max(bell, p.progress.RelicPower(Loot.Relic.RallyingBell));
            if (bell > 0f)
                foreach (var p in All)
                    if (p != null && p.progress != null && Vector3.Distance(p.transform.position, transform.position) < 15f) p.progress.ServerRally(5f, bell);
        }

        public static PlayerLife DownedNear(Vector3 from, PlayerLife exclude)
        {
            PlayerLife best = null;
            float bestDist = ReviveRange;
            foreach (var p in All)
            {
                if (p == exclude || !p.Downed.Value || p.Soul.Value) continue;
                float d = Vector3.Distance(p.transform.position, from);
                if (d <= bestDist) { best = p; bestDist = d; }
            }
            return best;
        }
    }
}
