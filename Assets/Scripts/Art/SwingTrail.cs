using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// How others see a melee swing (replaced 2026-10-20: the glowing crescent is gone). A few motes of
    /// dust and grit are flung along the arc the blade passes through, with a single bright glint at
    /// its far end: enough to read the swing's reach and direction across a room, without a shape
    /// hanging in the air. Visual only, seen by everyone.
    /// </summary>
    public static class SwingTrail
    {
        /// <param name="side">+1 swings right-to-left, -1 left-to-right.</param>
        public static void Spawn(Transform who, float radius, float arcDegrees, float side, Color color)
        {
            var origin = who.position + Vector3.up * 1.25f;
            int n = Mathf.Clamp(Mathf.RoundToInt(arcDegrees / 22f), 3, 8);
            var dust = new Color(0.62f, 0.56f, 0.5f);
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : i / (float)(n - 1);
                float yaw = side * Mathf.Lerp(arcDegrees / 2f, -arcDegrees / 2f, t);
                var dir = Quaternion.AngleAxis(yaw, Vector3.up) * who.forward;
                var at = origin + dir * radius * Random.Range(0.75f, 0.95f) + Vector3.up * Mathf.Lerp(0.15f, -0.25f, t) * side;
                Vfx.Embers(at, 1, dust, 1.4f, 0.03f, 0.35f);
            }
            var far = origin + Quaternion.AngleAxis(-side * arcDegrees / 2f, Vector3.up) * who.forward * radius * 0.9f;
            Vfx.Embers(far, 2, color, 2.2f, 0.025f, 0.18f);
        }
    }
}
