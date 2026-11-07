using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Small local life in the dungeon (2026-11-07, the user: "dungeon life"): a SPIDER that creeps about its web and
    /// stops, FISH that circle slowly under cistern water, and MOTHS that wheel round a light (pale over Cathedral
    /// candles, violet round the Abyss's crystal). Local on every peer; it touches nothing and nobody can hit it.
    /// </summary>
    public class LittleLife : MonoBehaviour
    {
        public enum Kind { Spider, Fish, Moth }
        public Kind What;
        public Vector3 Home, AxisA = Vector3.right, AxisB = Vector3.up;
        public float Radius = 0.4f;
        private float phase, pauseUntil;
        private Vector3 target;

        public static LittleLife Make(Transform parent, Kind kind, Vector3 home, Color tint)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.position = home;
            var l = go.AddComponent<LittleLife>();
            l.What = kind;
            l.Home = home;
            l.phase = Random.value * 40f;
            l.target = home;
            if (kind == Kind.Spider)
            {
                var body = MeshKit.Part(go.transform, "Body", MeshKit.Ball(0.05f, 6, 4), Mat.Dark, Vector3.zero, default, new Vector3(1f, 0.7f, 1.3f));
                for (int i = 0; i < 4; i++)
                    foreach (float s in new[] { -1f, 1f })
                        MeshKit.Part(go.transform, "Leg", MeshKit.Box(new Vector3(0.11f, 0.006f, 0.006f)), Mat.Dark, new Vector3(s * 0.06f, 0f, -0.03f + i * 0.02f), new Vector3(0f, s * (i * 14f - 20f), s * -25f));
            }
            else if (kind == Kind.Fish)
            {
                var fish = MeshKit.Part(go.transform, "Fish", MeshKit.Ball(0.07f, 6, 4), Mat.Cloth, Vector3.zero, default, new Vector3(0.45f, 0.4f, 1.6f));
                ArtLibrary.Tint(fish.GetComponent<Renderer>(), tint);
                var tail = MeshKit.Part(go.transform, "Tail", MeshKit.Box(new Vector3(0.005f, 0.06f, 0.06f)), Mat.Cloth, new Vector3(0f, 0f, -0.13f), new Vector3(45f, 0f, 0f));
                ArtLibrary.Tint(tail.GetComponent<Renderer>(), tint);
            }
            else
            {
                var m = MeshKit.Part(go.transform, "Moth", MeshKit.Box(new Vector3(0.07f, 0.004f, 0.035f)), Mat.Glow, Vector3.zero);
                ArtLibrary.SetGlow(m.GetComponent<Renderer>(), tint);
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return l;
        }

        private void Update()
        {
            float t = Time.time;
            var cam = ViewCamera.Current;
            if (cam != null && (cam.transform.position - Home).sqrMagnitude > 30f * 30f) return;
            switch (What)
            {
                case Kind.Spider:
                    // Creep to a spot on the web, wait, creep on.
                    if (t >= pauseUntil && (transform.position - target).sqrMagnitude < 0.0004f)
                    {
                        var r = Random.insideUnitCircle * Radius;
                        target = Home + AxisA * r.x + AxisB * r.y;
                        pauseUntil = t + Random.Range(1.5f, 6f);
                    }
                    if (t >= pauseUntil - 99f) transform.position = Vector3.MoveTowards(transform.position, target, 0.08f * Time.deltaTime);
                    var to = target - transform.position;
                    if (to.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(to, Vector3.Cross(AxisA, AxisB));
                    break;
                case Kind.Fish:
                {
                    float a = phase + t * 0.45f;
                    var p = Home + new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(a * 2.3f) * 0.05f, Mathf.Sin(a) * Radius * 0.7f);
                    var dir = p - transform.position;
                    transform.position = p;
                    if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, Mathf.Sin(t * 9f + phase) * 12f, 0f);
                    break;
                }
                case Kind.Moth:
                {
                    float a = phase + t * 2.4f;
                    transform.position = Home + new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(a * 1.7f) * 0.25f + Mathf.PerlinNoise(t, phase) * 0.2f, Mathf.Sin(a * 1.3f) * Radius);
                    transform.rotation = Quaternion.Euler(Mathf.Sin(t * 30f + phase) * 35f, a * Mathf.Rad2Deg, 0f);
                    break;
                }
            }
        }
    }
}
