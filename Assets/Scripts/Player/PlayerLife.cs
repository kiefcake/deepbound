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
    /// <summary>
    /// Downed and revive, R.E.P.O.-style (design doc Q11, decided 2026-10-13).
    ///
    /// <para>
    /// At zero health a player is not dead — they are downed: kneeling, unable to move or
    /// fight, waiting for a teammate. A teammate holding F nearby for
    /// <see cref="ReviveSeconds"/> brings them back at partial health. If every player is
    /// downed at once, the party has wiped and the run ends (see DungeonDirector).
    /// </para>
    ///
    /// Server-authoritative: the reviver only reports "I am holding F on this person"; the
    /// server checks distance and owns the progress bar.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerLife : NetworkBehaviour
    {
        public const float ReviveSeconds = 3f;
        public const float ReviveRange = 2.6f;
        private const float ReviveHealthFraction = 0.35f;

        /// <summary>Every player on this peer, for wipe checks, revive targeting and AI.</summary>
        public static readonly List<PlayerLife> All = new List<PlayerLife>();

        public readonly NetworkVariable<bool> Downed = new NetworkVariable<bool>();

        /// <summary>Downed and unrevived this long (while an ally still stands) and you bleed out.</summary>
        public const float BleedOutSeconds = 40f;

        /// <summary>Seconds bled so far while downed (server-written, for the HUD).</summary>
        public readonly NetworkVariable<float> Bleed = new NetworkVariable<float>();

        /// <summary>Bled out: only your Soul Skull, carried to the waystone, brings you back.</summary>
        public readonly NetworkVariable<bool> Soul = new NetworkVariable<bool>();

        /// <summary>Seconds left before a downed player bleeds out.</summary>
        public float BleedLeft => Mathf.Max(0f, BleedOutSeconds - Bleed.Value);

        /// <summary>
        /// Hidden from enemies (the Rogue's Smoke Bomb): no enemy will pick, keep, or be
        /// pulled onto this player. Server-written; teammates see a faint marker instead.
        /// </summary>
        public readonly NetworkVariable<bool> Hidden = new NetworkVariable<bool>();

        /// <summary>True if enemies may target this player right now.</summary>
        public bool Targetable => !Downed.Value && !Hidden.Value && Health.IsAlive && Time.time >= VeiledUntil;

        /// <summary>Server: inside a Rogue's smoke cloud (Smoke Screen) — enemies cannot pick you.</summary>
        public float VeiledUntil { get; set; }

        /// <summary>Whether an enemy may target this health: players must be <see cref="Targetable"/>.</summary>
        public static bool CanTarget(Health h) => h != null && h.IsAlive && (!h.TryGetComponent<PlayerLife>(out var p) || p.Targetable);
        public readonly NetworkVariable<float> ReviveProgress = new NetworkVariable<float>();
        private float reviveServer;
        private PlayerLife helper;
        private float lastHelperPing = -10f;
        /// <summary>Two revivers at once: x1.5.</summary>
        public const float TwoHandsRate = 1.5f;
        /// <summary>Server: a second reviver is helping right now (tests read it).</summary>
        public bool TwoHands { get; private set; }

        public Health Health { get; private set; }

        private PlayerLife reviver; // server: who is currently holding F on us
        private float lastReviverPing;
        private float reviveSendAt;
        private float lastDamagedAt;
        private float regenCarry;
        private PlayerProgress progress;
        private FirstPersonMotor motor;

        // Dodge: owner-side cooldown, server-side invulnerability window.
        public const float DodgeCooldown = 1.2f, DodgeInvulnerable = 0.35f, DodgeSpeed = 15f;
        public float DodgeReadyAt { get; private set; }
        public float DodgeCooldownNow => progress != null && progress.HasRelic(Loot.Relic.GhostLantern) ? DodgeCooldown * 0.5f : DodgeCooldown;
        private float invulnerableUntil;

        /// <summary>Server, tests: take no damage for a while (a stray blow resets a delivery).</summary>
        public void ServerDebugInvulnerable(float seconds) => invulnerableUntil = Time.time + seconds;

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
                // Registered first, so a dodged hit is gone before blocks or relics see it.
                Health.IncomingModifiers.Insert(0, (dmg, from) => Time.time < invulnerableUntil ? 0f : dmg);
                // The cat's whisker: a one-in-a-hundred chance to slip a hit entirely.
                Health.IncomingModifiers.Insert(1, (dmg, from) =>
                {
                    if (progress == null || !progress.CatWhisker.Value || dmg <= 0f || Random.value >= PlayerProgress.WhiskerEvade) return dmg;
                    EvadedRpc();
                    return 0f;
                });
                // Behind a blocking Knight's shield, blows from the front land at half strength.
                Health.IncomingModifiers.Add((dmg, from) => dmg > 0f && KnightKit.ShelterFor(this, from) ? dmg * KnightKit.ShelterScale : dmg);
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

        // Seated on Corwin's carriage, your hands stay in your lap (2026-11-01: players were cutting at Corwin from the bench).
        public bool CanAct => !Downed.Value && !HandsFull && !IsDragging(NetworkObjectId) && !Reviving && Seat.Value < 0 && !(debuffs != null && debuffs.Manic) && !(pouch != null && pouch.InStasis);

        /// <summary>Owner: kneeling over a downed ally with F held: no swinging or casting meanwhile (play-test 2026-11-01).</summary>
        public bool Reviving => IsOwner && Time.time < revivingUntil;
        private float revivingUntil, usePressAt = -10f;
        private bool useTapPending;
        /// <summary>With talk / drag on the use key: a tap shorter than this drags, a longer hold revives.</summary>
        public const float TapSeconds = 0.3f;

        /// <summary>
        /// Owner: does the use key have something in the world to do right now (under the crosshair, a downed ally,
        /// the carriage bench)? Then a stall or anvil it also stands by does not open on the same press.
        /// </summary>
        public bool UseHasWorldTarget()
        {
            if (DownedNear(transform.position, this) != null || IsDragging(NetworkObjectId)) return true;
            if (UI.Dialogue.Aimed != null || UI.Dialogue.Open) return true; // F talks to the person you look at
            var ride = World.CarriageRide.Instance;
            if (ride != null && DungeonDirector.InLobby && !ride.Riding && (Seat.Value >= 0 || NearestFreeSeat(ride) >= 0)) return true;
            var cam = GetComponent<NetworkPlayer>();
            var aimed = cam != null ? World.Interactable.Nearest(cam.AimOrigin, cam.AimForward, progress) : null;
            return aimed != null && !(ShiftHeld && (aimed is Loot.GearDrop || aimed is World.CampCat));
        }

        /// <summary>Shift + F: take gear into the pack (not wear it), pick the cat up (not pet it).</summary>
        public static bool ShiftHeld => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
        private Flasks pouch => cachedPouch != null ? cachedPouch : (cachedPouch = GetComponent<Flasks>());
        private Flasks cachedPouch;

        /// <summary>Carrying something in both hands (Lamplighter's Oil lets a torch-bearer fight).</summary>
        private bool HandsFull
        {
            get
            {
                if (!World.Carryable.IsCarrying(NetworkObjectId)) return false;
                // Lamplighter's Oil: a sure grip on whatever you carry, and a hand free to fight.
                return progress == null || !progress.HasRelic(Loot.Relic.LamplightersOil);
            }
        }

        // ---- Dragging a downed ally to safety (2026-10-20, Dark and Darker / R.E.P.O.) ----

        public const float DragSpeed = 0.55f, DragGap = 1.15f, DragBreak = 4f;

        /// <summary>Who is hauling this downed player along by the collar (MaxValue: nobody). Server-written.</summary>
        public readonly NetworkVariable<ulong> DraggedBy = new NetworkVariable<ulong>(ulong.MaxValue);
        public bool BeingDragged => DraggedBy.Value != ulong.MaxValue;

        /// <summary>The downed player this one is dragging, or null.</summary>
        public static PlayerLife DraggedByPlayer(ulong id)
        {
            foreach (var p in All) if (p != null && p.DraggedBy.Value == id) return p;
            return null;
        }

        public static bool IsDragging(ulong id) => DraggedByPlayer(id) != null;

        /// <summary>Any player → server, on the downed one: take hold (G), or let go if already holding.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
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

        /// <summary>Server: let go when the dragger falls, wanders off or this player gets up.</summary>
        private void ServerTickDrag()
        {
            if (!BeingDragged) return;
            var d = Dragger();
            if (!Downed.Value || Soul.Value || d == null || d.Downed.Value || Vector3.Distance(d.transform.position, transform.position) > DragBreak)
                DraggedBy.Value = ulong.MaxValue;
        }

        private bool wasDragged;

        /// <summary>Owner of the downed body: follow a step behind the dragger, feet first.</summary>
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

        // ---- The carriage: sit on its tailgate to say you are ready ----

        /// <summary>Which carriage seat this player sits in (-1: on their feet).</summary>
        public readonly NetworkVariable<sbyte> Seat = new NetworkVariable<sbyte>(-1);
        private bool wasSeated;

        /// <summary>Owner → server: sit in a carriage seat (at camp, before the carriage goes).</summary>
        [Rpc(SendTo.Server)]
        public void SitRpc(int seat)
        {
            var ride = World.CarriageRide.Instance;
            if (ride == null || !DungeonDirector.InLobby || ride.Riding || seat < 0 || seat >= World.CarriageRide.SeatCount || Downed.Value) return;
            foreach (var p in All) if (p != null && p != this && p.Seat.Value == seat) return;
            if (Vector3.Distance(transform.position, ride.SeatGround(seat)) > 3.5f) return;
            Seat.Value = (sbyte)seat;
        }

        /// <summary>Owner → server: get back down (not once the carriage is moving).</summary>
        [Rpc(SendTo.Server)]
        public void StandRpc()
        {
            var ride = World.CarriageRide.Instance;
            if (ride != null && ride.Riding && DungeonDirector.InLobby) return;
            Seat.Value = -1;
        }

        /// <summary>Owner: F at the back of the carriage sits you down (or stands you up); seated, you ride with it.</summary>
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

        /// <summary>The closest unclaimed seat within reach of the back of the carriage, or -1.</summary>
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

        /// <summary>Server: this player's ground velocity, smoothed (shooters use it to lead their shots).</summary>
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
            if (raw.sqrMagnitude > 400f) raw = Vector3.zero; // a teleport, not a run
            ServerVelocity = Vector3.Lerp(ServerVelocity, raw, 0.5f);
        }

        // ---- Death recap: the last hits before going down (League's death recap). ----
        private struct HitRecord { public string Who; public float Amount, Time; }
        private readonly List<HitRecord> recentHits = new List<HitRecord>();

        private void ServerRecordHit(float lost, ulong attackerId)
        {
            if (lost <= 0f) return;
            recentHits.Add(new HitRecord { Who = Combat.Health.NameOfHit(attackerId, Health), Amount = lost, Time = Time.time });
            if (recentHits.Count > 12) recentHits.RemoveAt(0);
        }

        /// <summary>Owner: what put you down, newest last, and the killing blow's source.</summary>
        public static event System.Action<string, string> DownedRecap;

        [Rpc(SendTo.Owner)]
        private void RecapRpc(string killer, string lines) => DownedRecap?.Invoke(killer, lines);

        private void ServerSendRecap()
        {
            // Sum by source over the last 10 seconds, the biggest first.
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

        /// <summary>Server: the bleed-out ran its course. Leave a Soul Skull for the others to carry.</summary>
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
            // Going down was silent: an ally falling out of sight could only be seen on the HUD.
            if (!was && now)
            {
                Sfx.Play(Sound.Toll, transform.position + Vector3.up, 0.8f, 1.25f);
                if (IsOwner) Sfx.Play2D(Sound.Heartbeat, 0.8f);
            }
        }

        // ------------------------------------------------------------------
        // Reviving
        // ------------------------------------------------------------------

        /// <summary>
        /// Sent by the reviving player's client every ~0.2s while F is held. Progress decays
        /// if the pings stop, so letting go — or getting knocked away — loses it gradually
        /// rather than instantly.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void ReviveHoldRpc(NetworkObjectReference reviverRef)
        {
            if (!Downed.Value || Soul.Value || !reviverRef.TryGet(out var reviverObject)) return;
            var r = reviverObject.GetComponent<PlayerLife>();
            if (r == null || r == this || r.Downed.Value) return;
            if (Vector3.Distance(r.transform.position, transform.position) > ReviveRange + 0.5f) return;

            // A second pair of hands speeds it up.
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
            // Smelling salts: a sharp breath and you are back on your feet.
            if (this.progress != null && this.progress.Salts.Value && Time.time - downedAt >= SaltsSeconds)
            {
                this.progress.Salts.Value = false;
                ServerRevive(0.3f);
                SaltsRpc();
                return;
            }
            bool beingRevived = reviver != null && Time.time - lastReviverPing < 0.35f && !reviver.Downed.Value &&
                                Vector3.Distance(reviver.transform.position, transform.position) <= ReviveRange + 0.5f;

            // Bleeding out: only while someone is still standing to save you, and not while they try.
            if (!beingRevived && !DungeonDirector.TestMode && AnyAllyStanding())
            {
                // Dragged to safety, the bleeding slows to half.
                bleedCarry += Time.deltaTime * (BeingDragged ? 0.5f : 1f);
                if (Bleed.Value + bleedCarry >= BleedOutSeconds) { bleedCarry = 0f; ServerBleedOut(); return; }
                // Synced in quarter-second steps, not every frame.
                if (bleedCarry >= 0.25f) { Bleed.Value += bleedCarry; bleedCarry = 0f; }
            }

            // The Cleric's passive, Grace: revives twice as fast.
            float rate = beingRevived && reviver.progress != null && reviver.progress.ClassId.Value == Classes.ClassId.Cleric ? 2f : 1f;
            bool helped = beingRevived && helper != null && Time.time - lastHelperPing < 0.35f && !helper.Downed.Value &&
                          Vector3.Distance(helper.transform.position, transform.position) <= ReviveRange + 0.5f;
            if (helped) rate *= TwoHandsRate;
            // Light Sleeper (Legacy): you get allies up 15% faster.
            if (beingRevived && Legacy.Has(reviver, Boon.LightSleeper)) rate *= Legacy.LightSleeperRate;
            TwoHands = helped;
            // Kept on the server at full precision (2026-10-28): it used to read back the synced value, which is only
            // written in 0.01 steps, so above ~100 fps a frame's progress was always too small and a revive never moved.
            float progress = Mathf.Clamp(reviveServer + (beingRevived ? Time.deltaTime * rate : -Time.deltaTime * 0.5f), 0f, ReviveSeconds);
            reviveServer = progress;

            if (progress >= ReviveSeconds)
            {
                if (reviver != null && reviver.progress != null) reviver.progress.Revives.Value++;
                // Got up by the last one standing: a CLUTCH.
                var saviour = Teamwork.InLastStand(reviver) ? reviver : null;
                ServerRevive(ReviveHealthFraction);
                if (saviour != null) Teamwork.ServerClutch(saviour, this);
                return;
            }

            if (Mathf.Abs(progress - ReviveProgress.Value) > 0.01f || progress == 0f) ReviveProgress.Value = progress;
        }

        /// <summary>
        /// Owner: Left Ctrl (or C) dashes in the direction you are moving — backwards if you
        /// are not — with a brief window in which nothing can hurt you. Every class has it;
        /// it is the universal answer to a telegraph you read too late.
        /// </summary>
        private void OwnerDodgeInput()
        {
            var keyboard = Keyboard.current;
            if (Downed.Value || motor == null || !motor.HasControl || Time.time < DodgeReadyAt) return;
            if (!UI.Binds.Down(UI.Bind.Dodge) && !UI.Binds.Down(UI.Bind.Slide)) return;

            var input = motor.MoveInput;
            // Dodging while sprinting forward is a slide instead: low and fast, and a kick
            // during it is a slide kick. Same i-frames, same cooldown.
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
            if (progress != null) progress.ServerOnDodge();
            DodgeFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DodgeFxRpc() => Sfx.Play(Sound.Dodge, transform.position + Vector3.up, 0.7f);

        /// <summary>Owner: hold F next to a downed teammate. Every class can revive.</summary>
        private void OwnerReviveInput()
        {
            var keyboard = Keyboard.current;
            if (Downed.Value) return;
            var target = DownedNear(transform.position, this);
            var dragging = DraggedByPlayer(NetworkObjectId);
            bool merged = UI.Binds.Merged;
            bool free = !UI.GameHud.MenuOpen && motor != null && motor.HasControl;

            // Taking hold of a downed teammate's collar, or letting go: its own key, or a TAP of the shared F.
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

            // F is contextual: a downed teammate takes priority over a chest or altar.
            if (target == null)
            {
                if (UI.Binds.Down(UI.Bind.Use) && free)
                {
                    if (UI.Dialogue.TryBegin()) return;
                    if (merged && World.CampStations.LocalAimed != null) return; // the HUD uses the workstation (CampStations)
                    var cam = GetComponent<NetworkPlayer>();
                    var use = World.Interactable.Nearest(cam.AimOrigin, cam.AimForward, progress);
                    // Shift + F on gear or the cat is the pack / the lift (GameHud), not wear / pet.
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

        /// <summary>
        /// Out-of-combat regeneration, scaled by VIT. Off for a few seconds after taking
        /// damage, so it heals you between fights, not during them.
        /// </summary>
        private void ServerRegen()
        {
            if (progress == null || progress.Stats == null || !Health.IsAlive) return;
            // No passive regeneration (play-test, 2026-10-16): healing comes from the potion pouch,
            // the fountain, Holy Water and the Cleric. Only the Thornhide Jerkin still mends you, when badly hurt.
            bool desperate = progress.DesperateRegen;
            if (!desperate || Health.Current.Value >= Health.Max.Value) return;

            regenCarry += (2f + progress.Value(StatType.VIT) * 0.12f) * 3f * Time.deltaTime;
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

        /// <summary>A downed player with smelling salts is about to get up: not a wipe yet.</summary>
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

        public void ServerRevive(float healthFraction)
        {
            if (!IsServer) return;
            Soul.Value = false;
            Bleed.Value = 0f;
            var skull = World.SoulSkull.For(NetworkObjectId);
            if (skull != null && skull.NetworkObject.IsSpawned) skull.NetworkObject.Despawn();
            Health.ServerSetFraction(healthFraction);
            Downed.Value = false;
            ReviveProgress.Value = 0f;
            reviveServer = 0f;
            reviver = null;
            // The Rallying Bell: the strongest bell within 15 m of whoever got up rallies everyone near.
            float bell = 0f;
            foreach (var p in All)
                if (p != null && p.progress != null && p.progress.HasRelic(Loot.Relic.RallyingBell) && Vector3.Distance(p.transform.position, transform.position) < 15f)
                    bell = Mathf.Max(bell, p.progress.RelicPower(Loot.Relic.RallyingBell));
            if (bell > 0f)
                foreach (var p in All)
                    if (p != null && p.progress != null && Vector3.Distance(p.transform.position, transform.position) < 15f) p.progress.ServerRally(5f, bell);
        }

        /// <summary>The nearest downed teammate within revive range of <paramref name="from"/>, or null.</summary>
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
