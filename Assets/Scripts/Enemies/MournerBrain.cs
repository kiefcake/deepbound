using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Mourner (R.E.P.O.'s monster-with-one-rule): a weeping stone figure that moves only while
    /// nobody is looking at it. Watched, it is a statue — frozen mid-step, weeping into its hands,
    /// or reaching for you if it got close. Unwatched, it glides fast and strikes. The tell is sound:
    /// stone scraping behind you. The answer is eyes: keep someone facing it, back away while you
    /// look, and break it while it cannot move.
    ///
    /// Server-authoritative: "watched" is any player (downed ones too) facing it within
    /// <see cref="WatchAngle"/> with a clear line of sight.
    /// </summary>
    public class MournerBrain : EnemyBrain
    {
        public const float WatchAngle = 60f, WatchRange = 45f;

        /// <summary>Synced: someone is looking at it, so it is stone.</summary>
        public readonly NetworkVariable<bool> Seen = new NetworkVariable<bool>(true);

        private float nextScrape;
        private Vector3 lastPos;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Seen.OnValueChanged += (_, now) => ApplyPose(now);
            lastPos = transform.position;
            ApplyPose(Seen.Value);
        }

        /// <summary>Server: is anyone watching <paramref name="self"/> right now?</summary>
        public static bool Watched(Health self)
        {
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Health.IsAlive && !p.Downed.Value) continue;
                if (p.Soul.Value) continue; // the dead do not hold it
                var eye = p.transform.position + Vector3.up * 1.6f;
                var to = self.Center - eye;
                if (to.magnitude > WatchRange) continue;
                var flatTo = new Vector3(to.x, 0f, to.z);
                var flatFwd = new Vector3(p.transform.forward.x, 0f, p.transform.forward.z);
                if (flatTo.sqrMagnitude < 0.01f || Vector3.Angle(flatFwd, flatTo) > WatchAngle) continue;
                if (CombatQuery.ClearLine(eye, self)) return true;
            }
            return false;
        }

        protected override void Update()
        {
            if (HeldByStun()) return;
            if (Rig != null && IsDead && !Rig.enabled) Rig.enabled = true;

            // Everyone: the grinding of stone while it moves unseen.
            if (!IsDead && !Seen.Value)
            {
                float moved = (transform.position - lastPos).magnitude;
                if (moved > 0.02f && Time.time >= nextScrape)
                {
                    nextScrape = Time.time + 0.55f;
                    Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.75f, 0.42f);
                }
            }
            lastPos = transform.position;

            if (!IsServer || IsDead) { if (!IsServer) base.Update(); return; }

            bool seen = Watched(health);
            if (Seen.Value != seen) Seen.Value = seen;
            if (seen)
            {
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
                if (WindingUp) CancelWindup();
                return;
            }
            base.Update();
        }

        /// <summary>Every peer: watched, the statue holds a pose; unwatched, the rig animates (no one sees it).</summary>
        private void ApplyPose(bool seen)
        {
            if (Rig == null || IsDead) return;
            Rig.enabled = !seen;
            if (!seen || Rig.ArmL == null || Rig.ArmR == null) return;
            // Close to the watcher: caught reaching. Far: weeping into its hands.
            var local = NetworkPlayer.Local;
            bool close = local != null && Vector3.Distance(local.transform.position, transform.position) < 5f;
            if (close)
            {
                Rig.ArmL.localRotation = Quaternion.Euler(-95f, 8f, 0f);
                Rig.ArmR.localRotation = Quaternion.Euler(-85f, -8f, 0f);
                if (Rig.Head != null) Rig.Head.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            }
            else
            {
                Rig.ArmL.localRotation = Quaternion.Euler(-150f, -30f, 20f);
                Rig.ArmR.localRotation = Quaternion.Euler(-150f, 30f, -20f);
                if (Rig.Head != null) Rig.Head.localRotation = Quaternion.Euler(28f, 0f, 0f);
            }
            if (Rig.Torso != null) Rig.Torso.localRotation = Quaternion.Euler(close ? 12f : 8f, 0f, 0f);
        }
    }
}
