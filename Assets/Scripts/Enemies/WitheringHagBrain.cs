using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Withering Hag (2026-11-13; the user: the Hexer's curse is now WEAKNESS, and a new CURSE means "player takes 40%
    /// more damage"). A bent crone of the Hollow Throne and below, mossy shawl, hair to her knees, a charm of knotted
    /// bone. She fixes her EVIL EYE on you: a sickly lane opens from her toward where you stood, LOCKED once drawn; when it
    /// fills, whoever is in it is CURSED (+40% damage taken) until she dies or the curse runs out. Step out of the lane
    /// (dodge, sidestep), break her cast with a stun, or kill her to lift it. Holy Water washes it off.
    /// </summary>
    public class WitheringHagBrain : EnemyBrain
    {
        [SerializeField] private float preferredRange = 10f, eyeCooldown = 10f, eyeWindup = 1.3f;
        public const float EyeLength = 16f, EyeWidth = 1.7f, CurseSeconds = 9f;
        public static readonly Color EyeColor = new Color(0.62f, 0.85f, 0.25f);
        private float nextEye, eyeAt = -1f;
        private Vector3 eyeFrom, eyeDir;
        /// <summary>For tests: gazes that landed, and players they cursed.</summary>
        public int Gazes { get; private set; }
        public int Cursed { get; private set; }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextEye = Time.time + 3f;
        }

        protected override void Strike()
        {
            if (target == null) return;
            var origin = transform.position + Vector3.up * 1.5f + transform.forward * 0.4f;
            var dir = AimDir(origin, 14f);
            Arrows.ServerFire(origin, dir, damage, Team.Players, NetworkObjectId, 14f);
            BoltRpc(origin, dir);
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;
            if (Time.time < nextEye || distanceToTarget > EyeLength - 1f || target == null) return false;
            if (!CombatQuery.ClearLine(transform.position + Vector3.up * 1.5f, target)) return false;
            BeginGaze(target.transform.position, eyeWindup);
            return true;
        }

        private void BeginGaze(Vector3 at, float windup)
        {
            nextEye = Time.time + eyeCooldown;
            eyeFrom = transform.position;
            eyeDir = at - eyeFrom; eyeDir.y = 0f;
            if (eyeDir.sqrMagnitude < 0.01f) eyeDir = transform.forward;
            eyeDir.Normalize();
            transform.rotation = Quaternion.LookRotation(eyeDir);
            eyeAt = Time.time + windup;
            busyUntil = eyeAt + 0.4f;
            PlayActionRpc(RigAction.Cast, windup);
            GazeWarnRpc(eyeFrom, eyeDir, windup);
        }

        /// <summary>For tests: fix the eye on <paramref name="at"/> now.</summary>
        public void ServerDebugGaze(Vector3 at, float windup) => BeginGaze(at, windup);

        /// <summary>Is <paramref name="p"/> inside the lane (flat distance to the segment, body included)?</summary>
        private bool InLane(Health h)
        {
            var d = h.transform.position - eyeFrom; d.y = 0f;
            float along = Vector3.Dot(d, eyeDir);
            if (along < 0f || along > EyeLength) return false;
            float side = (d - eyeDir * along).magnitude;
            return side <= EyeWidth * 0.5f + h.BodyRadius;
        }

        protected override void Update()
        {
            if (HeldByStun()) return;
            if (IsServer && !IsDead && eyeAt > 0f && Time.time >= eyeAt)
            {
                eyeAt = -1f;
                Gazes++;
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable || !InLane(p.Health)) continue;
                    p.Health.Tag("Evil eye").ServerDamage(damage * 0.5f, eyeFrom + Vector3.up, NetworkObjectId);
                    if (p.TryGetComponent<PlayerDebuffs>(out var deb) && deb.ServerAccurse(CurseSeconds, NetworkObjectId)) Cursed++;
                }
                GazeRpc(eyeFrom, eyeDir);
            }
            base.Update();
        }

        public override void ServerStun(float seconds)
        {
            if (eyeAt > 0f) { eyeAt = -1f; CancelRpc(); }
            base.ServerStun(seconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltRpc(Vector3 origin, Vector3 dir)
        {
            Arrows.SpawnVisual(origin, dir, ProjectileKind.ShadowBolt, 14f);
            Sfx.Play(Sound.Whisper, origin, 0.5f, 1.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GazeWarnRpc(Vector3 from, Vector3 dir, float seconds)
        {
            Telegraph.ShowLine(from, dir, EyeLength, EyeWidth, seconds, this, new Color(EyeColor.r, EyeColor.g, EyeColor.b, 0.26f));
            Sfx.Play(Sound.Whisper, transform.position + Vector3.up * 1.5f, 0.8f, 0.7f);
            Vfx.Embers(transform.position + Vector3.up * 1.7f + transform.forward * 0.2f, 6, EyeColor, 1.5f, 0.04f, 0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CancelRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        private void GazeRpc(Vector3 from, Vector3 dir)
        {
            for (float z = 1f; z < EyeLength; z += 1.4f)
                Vfx.Embers(Vfx.Floor(from + dir * z) + Vector3.up * Random.Range(0.4f, 1.6f), 1, EyeColor, 1.2f, 0.05f, 0.7f);
            Sfx.Play(Sound.Shade, from + Vector3.up * 1.5f, 0.8f, 0.6f);
        }
    }
}
