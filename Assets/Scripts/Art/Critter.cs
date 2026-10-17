using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Small life in the world, local to each peer and never networked: rats that nose along the
    /// dungeon walls and bolt from you, bats looping under high ceilings, and the ravens of Castle
    /// Ravenhold, perched on the walls and the road, that take off when you come close. Built by
    /// <see cref="AmbientLife"/>; asleep whenever the viewer is far away.
    /// </summary>
    public class Critter : MonoBehaviour
    {
        public enum Kind { Rat, Bat, Raven }

        public Kind Type;
        /// <summary>Rats: the floor rect they keep to (x/z); bats: the loop centre; ravens: their perches.</summary>
        public Rect Bounds;
        public float Floor, LoopRadius, LoopHeight;
        public Vector3[] Perches;

        private Transform body, head, tail, wingL, wingR;
        private Vector3 target;
        private float stateUntil, seed, speed;
        private int state; // rat: 0 idle, 1 scurry, 2 flee, 3 hidden | raven: 0 perched, 1 flying
        private Vector3 flyFrom, flyTo;
        private float flyStart, flyTime, nextCaw = 5f;

        private static Mesh ball, box, cone, stick;

        public static Critter Make(Transform parent, Kind kind, Vector3 at)
        {
            ball ??= MeshKit.Ball(0.5f, 8, 6);
            box ??= MeshKit.Box(Vector3.one);
            cone ??= MeshKit.Lathe("critterBeak", new[] { new Vector2(0.5f, 0f), new Vector2(0f, 1f) }, 5);
            stick ??= MeshKit.Cylinder(0.5f, 0.25f, 1f, 5);
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var c = go.AddComponent<Critter>();
            c.Type = kind;
            c.seed = Random.value * 100f;
            c.Build();
            return c;
        }

        private GameObject P(Transform parent, Mesh mesh, Vector3 pos, Vector3 scale, Color tint, Vector3 euler = default)
        {
            var g = MeshKit.Part(parent, "Bit", mesh, Mat.Cloth, pos, euler, scale);
            ArtLibrary.Tint(g.GetComponent<Renderer>(), tint);
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        private void Build()
        {
            switch (Type)
            {
                case Kind.Rat:
                {
                    var fur = new Color(0.22f, 0.17f, 0.13f);
                    body = MeshKit.Pivot(transform, "Body", new Vector3(0f, 0.05f, 0f));
                    P(body, ball, Vector3.zero, new Vector3(0.11f, 0.09f, 0.2f), fur);
                    head = MeshKit.Pivot(body, "Head", new Vector3(0f, 0.01f, 0.1f));
                    P(head, ball, new Vector3(0f, 0f, 0.03f), new Vector3(0.07f, 0.06f, 0.1f), fur);
                    P(head, ball, new Vector3(0f, -0.005f, 0.08f), new Vector3(0.02f, 0.02f, 0.02f), new Color(0.75f, 0.45f, 0.45f));
                    foreach (float s in new[] { -1f, 1f })
                    {
                        P(head, ball, new Vector3(s * 0.025f, 0.035f, 0.01f), new Vector3(0.03f, 0.035f, 0.01f), new Color(0.55f, 0.38f, 0.35f));
                        var eye = MeshKit.Part(head, "Eye", MeshKit.Ball(0.006f, 4, 3), Mat.Glow, new Vector3(s * 0.02f, 0.015f, 0.065f));
                        ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), new Color(0.9f, 0.3f, 0.15f) * 0.8f);
                    }
                    tail = MeshKit.Pivot(body, "Tail", new Vector3(0f, -0.01f, -0.09f));
                    P(tail, stick, Vector3.zero, new Vector3(0.012f, 0.18f, 0.012f), new Color(0.6f, 0.42f, 0.4f), new Vector3(-90f, 0f, 0f));
                    speed = Random.Range(2.2f, 3f);
                    transform.localScale = Vector3.one * 1.3f;
                    PickRatSpot(true);
                    break;
                }
                case Kind.Bat:
                {
                    var hide = new Color(0.06f, 0.05f, 0.06f);
                    body = MeshKit.Pivot(transform, "Body", Vector3.zero);
                    P(body, ball, Vector3.zero, new Vector3(0.07f, 0.06f, 0.12f), hide);
                    P(body, ball, new Vector3(0f, 0.02f, 0.07f), new Vector3(0.05f, 0.05f, 0.05f), hide);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        var w = MeshKit.Pivot(body, s < 0 ? "WingL" : "WingR", new Vector3(s * 0.03f, 0.01f, 0f));
                        P(w, box, new Vector3(s * 0.11f, 0f, -0.01f), new Vector3(0.22f, 0.006f, 0.12f), hide * 1.4f);
                        if (s < 0) wingL = w; else wingR = w;
                    }
                    break;
                }
                default:
                {
                    var black = new Color(0.09f, 0.09f, 0.13f);
                    body = MeshKit.Pivot(transform, "Body", new Vector3(0f, 0.12f, 0f));
                    P(body, ball, Vector3.zero, new Vector3(0.16f, 0.15f, 0.3f), black, new Vector3(-15f, 0f, 0f));
                    head = MeshKit.Pivot(body, "Head", new Vector3(0f, 0.09f, 0.13f));
                    P(head, ball, Vector3.zero, new Vector3(0.1f, 0.1f, 0.11f), black);
                    P(head, cone, new Vector3(0f, -0.005f, 0.05f), new Vector3(0.035f, 0.08f, 0.03f), new Color(0.12f, 0.12f, 0.13f), new Vector3(90f, 0f, 0f));
                    foreach (float s in new[] { -1f, 1f })
                    {
                        var eye = MeshKit.Part(head, "Eye", MeshKit.Ball(0.008f, 4, 3), Mat.Glow, new Vector3(s * 0.035f, 0.015f, 0.03f));
                        ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), new Color(0.6f, 0.6f, 0.65f) * 0.6f);
                        var w = MeshKit.Pivot(body, s < 0 ? "WingL" : "WingR", new Vector3(s * 0.07f, 0.03f, 0f));
                        P(w, box, new Vector3(s * 0.15f, 0f, -0.02f), new Vector3(0.3f, 0.01f, 0.16f), black * 1.3f);
                        if (s < 0) wingL = w; else wingR = w;
                    }
                    tail = MeshKit.Pivot(body, "Tail", new Vector3(0f, 0f, -0.15f));
                    P(tail, box, new Vector3(0f, 0f, -0.07f), new Vector3(0.08f, 0.01f, 0.14f), black);
                    // Legs.
                    foreach (float s in new[] { -1f, 1f }) P(transform, stick, new Vector3(s * 0.03f, 0.04f, 0f), new Vector3(0.012f, 0.08f, 0.012f), new Color(0.15f, 0.15f, 0.15f));
                    stateUntil = Time.time + Random.Range(3f, 20f);
                    // A raven is a big bird: size it up, and let the feathers catch the light.
                    transform.localScale = Vector3.one * 1.6f;
                    foreach (var r in GetComponentsInChildren<Renderer>()) if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("Cloth")) r.sharedMaterial = ArtLibrary.Get(Mat.Leather);
                    foreach (var r in GetComponentsInChildren<Renderer>()) if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("Leather")) ArtLibrary.Tint(r, black);
                    break;
                }
            }
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            MeshKit.MergeParts(transform);
        }

        /// <summary>Screenshots: hold still and in view (out of hiding, perched), optionally at a spot.</summary>
        public void DebugHold(Vector3? at = null)
        {
            if (at.HasValue) transform.position = at.Value;
            state = 0;
            if (body != null) body.gameObject.SetActive(true);
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            enabled = false;
        }

        private bool ViewerFar(float range)
        {
            var cam = ViewCamera.Current;
            return cam == null || (cam.transform.position - transform.position).sqrMagnitude > range * range;
        }

        /// <summary>The nearest standing player (or the camera), flat distance.</summary>
        private float Threat(out Vector3 from)
        {
            float best = float.MaxValue; from = transform.position;
            foreach (var p in Player.PlayerLife.All)
            {
                if (p == null) continue;
                var d = p.transform.position - transform.position; d.y = 0f;
                if (d.sqrMagnitude < best) { best = d.sqrMagnitude; from = p.transform.position; }
            }
            return Mathf.Sqrt(best);
        }

        private void Update()
        {
            if (ViewerFar(Type == Kind.Raven ? 60f : 32f)) return;
            switch (Type)
            {
                case Kind.Rat: UpdateRat(); break;
                case Kind.Bat: UpdateBat(); break;
                default: UpdateRaven(); break;
            }
        }

        // ---------------- Rats ----------------
        private void PickRatSpot(bool teleport)
        {
            var p = transform.position;
            for (int tries = 0; tries < 8; tries++)
            {
                p = new Vector3(Random.Range(Bounds.xMin, Bounds.xMax), Floor, Random.Range(Bounds.yMin, Bounds.yMax));
                // Rats keep to the edges: snap toward the nearest wall of the rect.
                float dl = p.x - Bounds.xMin, dr = Bounds.xMax - p.x, db = p.z - Bounds.yMin, dt = Bounds.yMax - p.z;
                float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
                if (m == dl) p.x = Bounds.xMin; else if (m == dr) p.x = Bounds.xMax; else if (m == db) p.z = Bounds.yMin; else p.z = Bounds.yMax;
                // ...but never inside a crate, a barrel or a pillar.
                if (!Physics.CheckSphere(p + Vector3.up * 0.15f, 0.14f, ~0, QueryTriggerInteraction.Ignore)) break;
            }
            target = p;
            if (teleport) transform.position = p;
        }

        private void UpdateRat()
        {
            float near = Threat(out var from);
            float t = Time.time;
            if (state == 3)
            {
                // In a crack in the wall; out again once the coast is clear.
                if (t >= stateUntil && near > 7f) { PickRatSpot(true); body.gameObject.SetActive(true); state = 0; stateUntil = t + Random.Range(1f, 3f); }
                return;
            }
            if (state != 2 && near < 3.2f)
            {
                state = 2; stateUntil = t + 1.1f;
                Sfx.Play(Sound.Squeak, transform.position, 0.35f, Random.Range(0.9f, 1.2f));
                var away = transform.position - from; away.y = 0f;
                target = transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : transform.forward) * 6f;
                target.x = Mathf.Clamp(target.x, Bounds.xMin, Bounds.xMax);
                target.z = Mathf.Clamp(target.z, Bounds.yMin, Bounds.yMax);
            }
            if (state == 0)
            {
                // Sniffing: the head bobs, the tail twitches.
                head.localRotation = Quaternion.Euler(Mathf.Sin(t * 9f + seed) * 10f, Mathf.Sin(t * 2.3f + seed) * 25f, 0f);
                tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 3f + seed) * 25f, 0f);
                if (t >= stateUntil) { state = 1; PickRatSpot(false); }
                return;
            }
            float spd = state == 2 ? 5.5f : speed;
            var to = target - transform.position; to.y = 0f;
            if (to.magnitude < 0.08f || (state == 2 && t >= stateUntil))
            {
                if (state == 2) { state = 3; body.gameObject.SetActive(false); stateUntil = t + Random.Range(8f, 16f); return; }
                state = 0; stateUntil = t + Random.Range(1.2f, 4.5f);
                return;
            }
            var dir = to.normalized;
            transform.position += dir * Mathf.Min(to.magnitude, spd * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 14f);
            body.localPosition = new Vector3(0f, 0.05f + Mathf.Abs(Mathf.Sin(t * 28f)) * 0.012f, 0f);
            tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 14f) * 18f, 0f);
        }

        // ---------------- Bats ----------------
        private void UpdateBat()
        {
            float t = Time.time * 0.55f + seed;
            var c = new Vector3(Bounds.center.x, LoopHeight, Bounds.center.y);
            // A wandering figure-of-eight under the ceiling, with the odd dip.
            var p = c + new Vector3(Mathf.Sin(t) * LoopRadius, Mathf.Sin(t * 2.3f) * 0.6f - Mathf.Max(0f, Mathf.Sin(t * 0.37f) - 0.8f) * 6f, Mathf.Sin(t * 2f) * LoopRadius * 0.6f);
            var v = p - transform.position;
            transform.position = p;
            if (v.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(v.normalized) * Quaternion.Euler(0f, 0f, -Mathf.Cos(t) * 25f);
            float flap = Mathf.Sin(Time.time * 22f + seed) * 55f;
            wingL.localRotation = Quaternion.Euler(0f, 0f, flap);
            wingR.localRotation = Quaternion.Euler(0f, 0f, -flap);
        }

        // ---------------- Ravens ----------------
        private void UpdateRaven()
        {
            float t = Time.time;
            if (state == 0)
            {
                // Perched: the head jerks about, now and then a little hop.
                float jerk = Mathf.Floor(t * 1.3f + seed);
                head.localRotation = Quaternion.Euler(Mathf.Sin(jerk * 7.1f) * 15f, Mathf.Sin(jerk * 3.7f) * 60f, 0f);
                wingL.localRotation = Quaternion.Euler(0f, 0f, -10f);
                wingR.localRotation = Quaternion.Euler(0f, 0f, 10f);
                bool startled = Threat(out _) < 4.5f;
                if (t >= nextCaw)
                {
                    nextCaw = t + Random.Range(12f, 40f);
                    if (!ViewerFar(30f)) Sfx.Play(Sound.Caw, transform.position, 0.45f, Random.Range(0.85f, 1.1f));
                }
                if ((startled || t >= stateUntil) && Perches != null && Perches.Length > 1)
                {
                    state = 1;
                    flyFrom = transform.position;
                    flyTo = Perches[Random.Range(0, Perches.Length)];
                    if ((flyTo - flyFrom).sqrMagnitude < 4f) flyTo = Perches[(System.Array.IndexOf(Perches, flyTo) + 1) % Perches.Length];
                    flyStart = t;
                    if (startled && !ViewerFar(25f)) Sfx.Play(Sound.Caw, transform.position, 0.6f, Random.Range(0.9f, 1.15f));
                    flyTime = Mathf.Clamp(Vector3.Distance(flyFrom, flyTo) / 7f, 1.2f, 6f);
                }
                return;
            }
            float k = Mathf.Clamp01((t - flyStart) / flyTime);
            var pos = Vector3.Lerp(flyFrom, flyTo, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * Mathf.Max(3f, Vector3.Distance(flyFrom, flyTo) * 0.25f);
            var dir = pos - transform.position; dir.y *= 0.3f;
            transform.position = pos;
            if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), Time.deltaTime * 8f);
            float flap = Mathf.Sin(t * (k < 0.2f ? 26f : 16f)) * 50f;
            wingL.localRotation = Quaternion.Euler(0f, 0f, flap);
            wingR.localRotation = Quaternion.Euler(0f, 0f, -flap);
            if (k >= 1f)
            {
                state = 0;
                stateUntil = t + Random.Range(15f, 60f);
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            }
        }
    }
}
