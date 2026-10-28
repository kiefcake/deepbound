using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Abyssal Eye (2026-10-20, the Abyss's own dead): a floating eye that keeps its distance and stares.
    /// Every so often it LOCKS ON: a beam line is drawn on the floor toward where you are going and its iris
    /// flares for <see cref="LockSeconds"/>, then a beam lances down that line (to the first wall) and hurts
    /// everyone in it. Step off the line. Between beams it spits small motes. The eye itself is its head and
    /// its iris a weak point.
    /// </summary>
    public class AbyssEyeBrain : EnemyBrain
    {
        public const float LockSeconds = 1.1f, BeamCooldown = 6.5f, BeamLength = 22f, BeamWidth = 0.55f, BeamMultiplier = 2.4f;
        [SerializeField] private float preferredRange = 12f;

        private float nextBeam, fireAt = -1f;
        private Vector3 beamFrom, beamDir;

        /// <summary>Server, for tests.</summary>
        public int BeamsFired { get; private set; }
        public float LastBeamDealt { get; private set; }
        /// <summary>Server, for tests: the last beam's reach and what stopped it.</summary>
        public float LastBeamLength { get; private set; }
        public static string LastBlocker { get; private set; } = "";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextBeam = Time.time + 2.5f;
        }

        private Vector3 EyeAt => transform.position + Vector3.up * 1.75f;

        protected override void Strike()
        {
            if (target == null) return;
            var dir = AimDir(EyeAt, 16f);
            Arrows.ServerFire(EyeAt + transform.forward * 0.5f, dir, damage, Team.Players, NetworkObjectId, 16f);
            MoteRpc(EyeAt + transform.forward * 0.5f, dir);
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;
            if (Time.time < nextBeam || fireAt > 0f || distanceToTarget > BeamLength - 2f) return false;
            if (!CombatQuery.ClearLine(EyeAt, target)) return false;
            ServerLockOn(target);
            return true;
        }

        /// <summary>Server: lock on to a target (tests call it directly).</summary>
        public void ServerLockOn(Health who)
        {
            if (!IsServer || IsDead || who == null) return;
            nextBeam = Time.time + BeamCooldown;
            fireAt = Time.time + LockSeconds * Difficulty.Tell(World.DungeonDirector.Instance != null ? World.DungeonDirector.Instance.Floor.Value.y : 0);
            busyUntil = fireAt + 0.4f;
            beamFrom = EyeAt;
            // Aim where they are going (at their chest, up or down a ramp too); the floor line shows its path,
            // so a sidestep is the answer.
            // Locks onto the one it was told to (AimDir leads its current target, which can be someone else).
            target = who;
            var aim = AimDir(beamFrom, 40f);
            beamDir = aim.sqrMagnitude > 0.01f ? aim.normalized : transform.forward;
            var flat = new Vector3(beamDir.x, 0f, beamDir.z);
            if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(flat);
            float len = BeamReach(beamFrom, beamDir);
            LockRpc(transform.position, flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward, len * flat.magnitude, fireAt - Time.time);
        }

        private static float BeamReach(Vector3 from, Vector3 dir)
        {
            // To the first wall or prop (bodies do not stop it).
            float best = BeamLength;
            LastBlocker = "nothing";
            foreach (var hit in Physics.RaycastAll(from, dir, BeamLength, ~0, QueryTriggerInteraction.Ignore))
                if (hit.distance < best && hit.collider.GetComponentInParent<Health>() == null) { best = hit.distance; LastBlocker = hit.collider.name + " (" + hit.collider.GetType().Name + ")"; }
            return best;
        }

        protected override void Update()
        {
            if (IsServer && fireAt > 0f && IsStunned) { fireAt = -1f; LockRpc(Vector3.zero, Vector3.zero, 0f, -1f); }
            if (HeldByStun()) return;
            if (IsServer && !IsDead && fireAt > 0f)
            {
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
                if (Time.time < fireAt) return;
                fireAt = -1f;
                BeamsFired++;
                float len = BeamReach(beamFrom, beamDir);
                LastBeamLength = len;
                // The beam slopes from the eye down to chest height along its length.
                var end = beamFrom + beamDir * len;
                LastBeamDealt = 0f;
                foreach (var p in Player.PlayerLife.All)
                {
                    if (p == null || p.Downed.Value || p.Health == null || !p.Health.IsAlive) continue;
                    // The beam and a band under it (a slide does not duck it).
                    if (!p.Health.SegmentTouchesBody(beamFrom, end, BeamWidth) && !p.Health.SegmentTouchesBody(beamFrom + Vector3.down * 0.7f, end + Vector3.down * 0.7f, BeamWidth)) continue;
                    LastBeamDealt += p.Health.ServerDamage(damage * BeamMultiplier, beamFrom, NetworkObjectId);
                }
                BeamRpc(beamFrom, end);
                return;
            }
            base.Update();
        }

        public override void ServerStun(float seconds)
        {
            if (fireAt > 0f) { fireAt = -1f; LockRpc(Vector3.zero, Vector3.zero, 0f, -1f); }
            base.ServerStun(seconds);
        }

        private static readonly Color Violet = new Color(0.62f, 0.35f, 1f);

        [Rpc(SendTo.ClientsAndHost)]
        private void MoteRpc(Vector3 origin, Vector3 dir)
        {
            Arrows.SpawnVisual(origin, dir, ProjectileKind.ShadowBolt, 16f);
            Sfx.Play(Sound.Whisper, origin, 0.4f, 1.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LockRpc(Vector3 feet, Vector3 dir, float length, float seconds)
        {
            if (seconds < 0f) { Telegraph.Cancel(this); return; }
            Telegraph.ShowLine(feet, dir, length, BeamWidth * 2.4f, seconds, this, Violet);
            Rig?.Play(RigAction.Windup, seconds);
            Sfx.Play(Sound.Frost, transform.position + Vector3.up * 1.75f, 0.9f, 0.45f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BeamRpc(Vector3 from, Vector3 to)
        {
            var go = new GameObject("EyeBeam");
            var beam = MeshKit.Part(go.transform, "Beam", MeshKit.Box(new Vector3(0.22f, 0.22f, 1f)), Mat.Glow, Vector3.zero).transform;
            var r = beam.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.SetGlow(r, Violet * 2.2f);
            go.transform.position = (from + to) / 2f;
            go.transform.rotation = Quaternion.LookRotation(to - from);
            beam.localScale = new Vector3(1f, 1f, Vector3.Distance(from, to));
            Destroy(go, 0.28f);
            Vfx.Flash(to, Violet, 3f, 6f, 0.3f);
            Vfx.Ring(new Vector3(to.x, to.y - 1f, to.z), 1.2f, Violet, 0.4f);
            Sfx.Play(Sound.Fire, from, 0.9f, 0.6f);
            Sfx.Play(Sound.Shatter, to, 0.6f, 0.5f);
        }
    }
}
