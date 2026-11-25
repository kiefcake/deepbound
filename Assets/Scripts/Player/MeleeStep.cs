using Deepbound.Enemies;
using UnityEngine;

namespace Deepbound.Player
{
    public static class MeleeStep
    {
        public const float Extra = 2.2f, Cone = 40f;
        private static float nextStep;

        public static bool TryStepIn(FirstPersonMotor motor, Transform me, Vector3 aimForward, float reach, float speed, float cooldown = 0.35f)
        {
            if (motor == null || Time.time < nextStep) return false;
            var fwd = aimForward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) return false;
            fwd.Normalize();
            EnemyBrain best = null;
            float bestGap = float.MaxValue;
            foreach (var e in EnemyBrain.All)
            {
                if (e == null || e.IsDead) continue;
                var to = e.transform.position - me.position;
                if (Mathf.Abs(to.y) > 1.6f) continue;
                to.y = 0f;
                float body = e.TryGetComponent<Combat.Health>(out var h) ? h.BodyRadius : 0.4f;
                float gap = to.magnitude - body;
                if (gap > reach + Extra || gap < 0.2f) continue;
                if (Vector3.Angle(fwd, to) > Cone) continue;
                if (gap < bestGap) { bestGap = gap; best = e; }
            }
            if (best == null || bestGap <= reach * 0.85f) return false;
            nextStep = Time.time + cooldown;
            var dir = best.transform.position - me.position; dir.y = 0f;
            float need = Mathf.Clamp01((bestGap - reach * 0.7f) / Extra);
            motor.AddImpulse(dir.normalized * speed * Mathf.Lerp(0.55f, 1f, need));
            return true;
        }
    }
}
