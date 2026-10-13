using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

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
        public readonly NetworkVariable<float> ReviveProgress = new NetworkVariable<float>();

        public Health Health { get; private set; }

        private PlayerLife reviver; // server: who is currently holding F on us
        private float lastReviverPing;

        private void Awake()
        {
            Health = GetComponent<Health>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer) Health.Died += HandleDied;
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
            if (!IsServer || !Downed.Value) return;

            bool beingRevived = reviver != null && Time.time - lastReviverPing < 0.35f && !reviver.Downed.Value &&
                                Vector3.Distance(reviver.transform.position, transform.position) <= ReviveRange + 0.5f;

            float progress = ReviveProgress.Value + (beingRevived ? Time.deltaTime : -Time.deltaTime * 0.5f);
            progress = Mathf.Clamp(progress, 0f, ReviveSeconds);

            if (progress >= ReviveSeconds)
            {
                ServerRevive(ReviveHealthFraction);
                return;
            }

            if (Mathf.Abs(progress - ReviveProgress.Value) > 0.01f || progress == 0f) ReviveProgress.Value = progress;
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
