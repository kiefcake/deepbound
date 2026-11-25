using UnityEngine;

namespace Deepbound.Art
{
    public static class AbilityFx
    {
        private static Mesh cube, ball;
        private static Mesh Cube => cube ??= MeshKit.Box(Vector3.one);
        private static Mesh Ball => ball ??= MeshKit.Ball(0.5f, 8, 6);

        private const float Near = 30f;

        private static bool Close(Vector3 at)
        {
            var cam = ViewCamera.Current;
            return cam == null || (cam.transform.position - at).sqrMagnitude < Near * Near;
        }

        private static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static void Cast(Vector3 feet, Vector3 hand, Color c, float radius = 1.1f, float seconds = 0.8f)
        {
            if (Application.isBatchMode) return;
            FxSprite.Decal(FxSprite.AnyMagic, feet, radius * 1.3f, radius * 2f, seconds, A(c, 0.75f), Random.value < 0.5f ? 140f : -140f);
            FxSprite.Billboard("flare_01", hand, Vector3.zero, 0.3f, 0.25f, 1f, A(c, 0.9f), 120f);
            FxSprite.Billboard("twirl_02", hand, Vector3.zero, 0.4f, 0.2f, 1.1f, A(c, 0.7f), 520f);
            if (!Close(feet)) return;
            Spiral(feet, c, radius * 0.55f, 2f, 12, seconds);
        }

        public static void Spiral(Vector3 feet, Color c, float radius, float height, int count, float life)
        {
            if (Application.isBatchMode) return;
            float turn = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < count; i++)
            {
                float a = turn + i * Mathf.PI * 2f / count;
                var out_ = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var tangent = new Vector3(-out_.z, 0f, out_.x);
                var p = feet + out_ * radius + Vector3.up * (0.1f + 0.15f * (i % 3));
                var v = tangent * 1.6f + out_ * 0.5f + Vector3.up * (height / life) * Random.Range(0.7f, 1.1f);
                Vfx.Spawn(Cube, Mat.Glow, p, v, life * Random.Range(0.8f, 1.1f), 0.07f, 0f, c * 2.2f, -0.2f, 0.4f);
            }
        }

        public static void Burst(Vector3 at, float radius, Color c, float strength = 1f, bool mark = false)
        {
            if (Application.isBatchMode) return;
            var floor = Vfx.Floor(at);
            Vfx.Ring(floor + Vector3.up * 0.05f, radius, c, 0.4f + 0.1f * strength);
            FxSprite.Decal("circle_02", floor, radius * 0.4f, radius * 2.2f, 0.5f, A(c, 0.55f * strength));
            Vfx.AbilityFlash(at + Vector3.up * 0.8f, c, 3f + 2f * strength, Mathf.Max(4f, radius * 2.2f), 0.35f);
            if (mark) FxSprite.Decal("dirt_03", floor, radius * 1.4f, radius * 1.5f, 5f, A(c * 0.5f, 0.35f));
            if (!Close(at)) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(radius * 6f * strength), 8, 36);
            float speed = radius * 3.2f;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + Random.Range(-0.1f, 0.1f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vfx.Spawn(Cube, Mat.Glow, floor + dir * 0.3f + Vector3.up * 0.12f, dir * speed * Random.Range(0.7f, 1.1f) + Vector3.up * Random.Range(0.3f, 1.2f),
                    Random.Range(0.3f, 0.45f), 0.08f, 0f, c * 2.2f, 2f, 2.2f);
            }
            FxSprite.Billboard(FxSprite.AnyStar, at + Vector3.up * 0.6f, Vector3.zero, 0.25f, 0.4f, 1.2f + radius * 0.3f, A(c, 0.9f), Random.Range(-250f, 250f));
        }

        public static void Column(Vector3 at, float height, Color c, float life = 0.9f, float width = 1f)
        {
            if (Application.isBatchMode) return;
            var floor = Vfx.Floor(at);
            int steps = Mathf.Clamp(Mathf.RoundToInt(height / 0.8f), 2, 8);
            for (int i = 0; i < steps; i++)
            {
                float y = (i + 0.5f) * height / steps;
                float k = 1f - i / (float)steps;
                FxSprite.Billboard("light_01", floor + Vector3.up * y, Vector3.up * 0.6f, life * (0.7f + 0.3f * k), width * 0.6f, width * (1.1f + 0.5f * k), A(c, 0.45f * (0.5f + 0.5f * k)));
            }
            FxSprite.Decal("circle_05", floor, width * 0.8f, width * 1.6f, life, A(c, 0.6f));
            if (!Close(at)) return;
            for (int i = 0; i < 10; i++)
            {
                var p = floor + new Vector3(Random.Range(-0.4f, 0.4f) * width, Random.Range(0f, 0.5f), Random.Range(-0.4f, 0.4f) * width);
                Vfx.Spawn(Cube, Mat.Glow, p, Vector3.up * Random.Range(2f, 4.5f) * height / 4f, life * Random.Range(0.7f, 1.2f), 0.06f, 0f, c * 2.2f, -0.3f, 0.3f);
            }
        }

        public static void Streak(Vector3 from, Vector3 to, Color c, float width = 1f, float life = 0.45f)
        {
            if (Application.isBatchMode) return;
            var d = to - from;
            float len = d.magnitude;
            if (len < 0.1f) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(len * 2.5f), 4, 40);
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                var p = Vector3.Lerp(from, to, t) + Random.insideUnitSphere * 0.08f * width;
                Vfx.Spawn(Cube, Mat.Glow, p, Random.insideUnitSphere * 0.4f + Vector3.up * 0.3f, life * (0.6f + 0.6f * t), 0.07f * width, 0f, c * 2.1f, -0.1f, 1.5f);
            }
            int traces = Mathf.Clamp(Mathf.RoundToInt(len / 2.5f), 1, 6);
            for (int i = 0; i < traces; i++)
            {
                var p = Vector3.Lerp(from, to, (i + 0.5f) / traces);
                FxSprite.Billboard("trace_01", p, d.normalized * 2f, life * 0.8f, width * 1.2f, width * 0.6f, A(c, 0.6f));
            }
            FxSprite.Billboard("spark_01", from, Vector3.zero, 0.25f, 0.3f, 0.9f * width, A(c, 0.85f), 300f);
            FxSprite.Billboard("spark_01", to, Vector3.zero, 0.3f, 0.3f, 1.1f * width, A(c, 0.9f), -300f);
        }

        public static void Muzzle(Vector3 at, Vector3 dir, Color c, float size = 1f)
        {
            if (Application.isBatchMode) return;
            FxSprite.Billboard("flare_01", at, dir * 0.5f, 0.14f, 0.2f * size, 0.7f * size, A(c, 0.85f), Random.Range(-200f, 200f));
            if (!Close(at)) return;
            int n = Mathf.RoundToInt(4 * size) + 2;
            for (int i = 0; i < n; i++)
            {
                var v = (dir + Random.insideUnitSphere * 0.35f).normalized * Random.Range(3f, 6f) * size;
                Vfx.Spawn(Cube, Mat.Glow, at, v, Random.Range(0.12f, 0.22f), 0.04f * size, 0f, c * 2.4f, 4f, 3f);
            }
        }

        public static void Cone(Vector3 origin, Vector3 forward, float length, float arcDegrees, Color c, int count = 18)
        {
            if (Application.isBatchMode) return;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) return;
            forward.Normalize();
            if (!Close(origin)) count /= 3;
            for (int i = 0; i < count; i++)
            {
                var dir = Quaternion.Euler(Random.Range(-6f, 12f), Random.Range(-arcDegrees * 0.5f, arcDegrees * 0.5f), 0f) * forward;
                Vfx.Spawn(Cube, Mat.Glow, origin + Random.insideUnitSphere * 0.2f, dir * length * Random.Range(2.2f, 3.4f), Random.Range(0.25f, 0.4f), 0.07f, 0f, c * 2.1f, 1f, 2.5f);
            }
            var mid = origin + forward * length * 0.5f;
            FxSprite.Billboard(FxSprite.AnySmoke, mid, forward * length, 0.5f, 0.6f, length * 0.8f, new Color(0.3f, 0.27f, 0.25f, 0.4f), Random.Range(-30f, 30f), 2f);
        }

        public static void Bubbles(Vector3 at, float radius, Color c, int count = 10)
        {
            if (Application.isBatchMode || !Close(at)) return;
            var floor = Vfx.Floor(at);
            for (int i = 0; i < count; i++)
            {
                var p = floor + new Vector3(Random.Range(-radius, radius), 0.05f, Random.Range(-radius, radius)) * 0.8f;
                Vfx.Spawn(Ball, Mat.Glow, p, Vector3.up * Random.Range(0.6f, 1.6f), Random.Range(0.6f, 1.1f), Random.Range(0.08f, 0.16f), 0.02f, c * 1.4f, -0.4f, 0.8f);
            }
        }

        public static void Uplift(Vector3 feet, Color c, int count = 8)
        {
            if (Application.isBatchMode || !Close(feet)) return;
            for (int i = 0; i < count; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var p = feet + new Vector3(Mathf.Cos(a) * Random.Range(0.2f, 0.5f), Random.Range(0.1f, 1.4f), Mathf.Sin(a) * Random.Range(0.2f, 0.5f));
                Vfx.Spawn(Cube, Mat.Glow, p, Vector3.up * Random.Range(0.8f, 1.6f), Random.Range(0.6f, 1f), 0.06f, 0f, c * 2f, -0.2f, 0.5f);
            }
        }
    }
}
