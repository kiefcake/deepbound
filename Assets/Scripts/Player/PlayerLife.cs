using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Core;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
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

        /// <summary>
        /// Hidden from enemies (the Rogue's Smoke Bomb): no enemy will pick, keep, or be
        /// pulled onto this player. Server-written; teammates see a faint marker instead.
        /// </summary>
        public readonly NetworkVariable<bool> Hidden = new NetworkVariable<bool>();

        /// <summary>True if enemies may target this player right now.</summary>
        public bool Targetable => !Downed.Value && !Hidden.Value && Health.IsAlive;

        /// <summary>Whether an enemy may target this health: players must be <see cref="Targetable"/>.</summary>
        public static bool CanTarget(Health h) => h != null && h.IsAlive && (!h.TryGetComponent<PlayerLife>(out var p) || p.Targetable);
        public readonly NetworkVariable<float> ReviveProgress = new NetworkVariable<float>();

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

        private void Awake()
        {
            Health = GetComponent<Health>();
            progress = GetComponent<PlayerProgress>();
            motor = GetComponent<FirstPersonMotor>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer)
            {
                Health.Died += HandleDied;
                Health.DamagedServer += (_, __) => lastDamagedAt = Time.time;
                // Registered first, so a dodged hit is gone before blocks or relics see it.
                Health.IncomingModifiers.Insert(0, (dmg, from) => Time.time < invulnerableUntil ? 0f : dmg);
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

        public bool CanAct => !Downed.Value;

        private void HandleDied(Health _)
        {
            if (progress != null && progress.ServerTryPhoenix())
            {
                Health.ServerSetFraction(0.5f);
                return;
            }
            if (progress != null) progress.Downs.Value++;
            Downed.Value = true;
            ReviveProgress.Value = 0f;
        }

        private void HandleDownedChanged(bool was, bool now)
        {
            if (was && !now) Sfx.Play(Sound.Revive, transform.position);
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
            if (!Downed.Value || !reviverRef.TryGet(out var reviverObject)) return;
            var r = reviverObject.GetComponent<PlayerLife>();
            if (r == null || r == this || r.Downed.Value) return;
            if (Vector3.Distance(r.transform.position, transform.position) > ReviveRange + 0.5f) return;

            reviver = r;
            lastReviverPing = Time.time;
        }

        private void Update()
        {
            if (IsOwner)
            {
                OwnerReviveInput();
                OwnerDodgeInput();
            }
            if (!IsServer) return;
            if (!Downed.Value)
            {
                ServerRegen();
                return;
            }

            bool beingRevived = reviver != null && Time.time - lastReviverPing < 0.35f && !reviver.Downed.Value &&
                                Vector3.Distance(reviver.transform.position, transform.position) <= ReviveRange + 0.5f;

            // The Cleric's passive, Grace: revives twice as fast.
            float rate = beingRevived && reviver.progress != null && reviver.progress.ClassId.Value == Classes.ClassId.Cleric ? 2f : 1f;
            float progress = ReviveProgress.Value + (beingRevived ? Time.deltaTime * rate : -Time.deltaTime * 0.5f);
            progress = Mathf.Clamp(progress, 0f, ReviveSeconds);

            if (progress >= ReviveSeconds)
            {
                if (reviver != null && reviver.progress != null) reviver.progress.Revives.Value++;
                ServerRevive(ReviveHealthFraction);
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
            if (Downed.Value || keyboard == null || motor == null || !motor.HasControl || Time.time < DodgeReadyAt) return;
            if (!keyboard.leftCtrlKey.wasPressedThisFrame && !keyboard.cKey.wasPressedThisFrame) return;

            var input = motor.MoveInput;
            var dir = input.sqrMagnitude > 0.01f ? transform.right * input.x + transform.forward * input.y : -transform.forward;
            dir.y = 0f;
            motor.AddImpulse(dir.normalized * DodgeSpeed);
            DodgeReadyAt = Time.time + DodgeCooldownNow;
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
            if (Downed.Value || keyboard == null) return;
            var target = DownedNear(transform.position, this);

            // F is contextual: a downed teammate takes priority over a chest or altar.
            if (target == null)
            {
                if (keyboard.fKey.wasPressedThisFrame && motor != null && motor.HasControl)
                {
                    var cam = GetComponent<NetworkPlayer>();
                    var use = World.Interactable.Nearest(cam.AimOrigin, cam.AimForward, progress);
                    if (use != null) use.UseRpc(NetworkObject);
                }
                return;
            }

            if (!keyboard.fKey.isPressed || Time.time < reviveSendAt) return;
            reviveSendAt = Time.time + 0.2f;
            target.ReviveHoldRpc(NetworkObject);
        }

        /// <summary>
        /// Out-of-combat regeneration, scaled by VIT. Off for a few seconds after taking
        /// damage, so it heals you between fights, not during them.
        /// </summary>
        private void ServerRegen()
        {
            if (progress == null || progress.Stats == null || !Health.IsAlive) return;
            bool desperate = progress.DesperateRegen;
            if ((!desperate && Time.time - lastDamagedAt < 5f) || Health.Current.Value >= Health.Max.Value) return;

            regenCarry += (2f + progress.Value(StatType.VIT) * 0.12f) * (desperate ? 3f : 1f) * Time.deltaTime;
            if (regenCarry >= 1f)
            {
                float whole = Mathf.Floor(regenCarry);
                regenCarry -= whole;
                Health.ServerHeal(whole);
            }
        }

        public void ServerRevive(float healthFraction)
        {
            if (!IsServer) return;
            Health.ServerSetFraction(healthFraction);
            Downed.Value = false;
            ReviveProgress.Value = 0f;
            reviver = null;
        }

        /// <summary>The nearest downed teammate within revive range of <paramref name="from"/>, or null.</summary>
        public static PlayerLife DownedNear(Vector3 from, PlayerLife exclude)
        {
            PlayerLife best = null;
            float bestDist = ReviveRange;
            foreach (var p in All)
            {
                if (p == exclude || !p.Downed.Value) continue;
                float d = Vector3.Distance(p.transform.position, from);
                if (d <= bestDist) { best = p; bestDist = d; }
            }
            return best;
        }
    }
}
