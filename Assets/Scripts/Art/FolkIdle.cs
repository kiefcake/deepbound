using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Brings a standing camp figure to life: it breathes, its head turns to follow you when you are
    /// near (and drifts idly when you are not), with a small nod now and then. The head parts are moved
    /// under a neck pivot by <see cref="Attach"/>. Local only.
    /// </summary>
    public class FolkIdle : MonoBehaviour
    {
        private Transform neck;
        private float seed, yaw, pitch, nextGlance, glanceYaw;
        private Vector3 baseScale;
        private float jolt;
        private Vector3 joltAway;
        private Quaternion baseRot;
        public static readonly System.Collections.Generic.List<FolkIdle> All = new System.Collections.Generic.List<FolkIdle>();

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>The figure standing nearest <paramref name="at"/> (within 3 m), or null.</summary>
        public static FolkIdle Near(Vector3 at)
        {
            FolkIdle best = null; float bd = 9f;
            foreach (var f in All)
            {
                if (f == null) continue;
                var d = f.transform.position - at; d.y = 0f;
                if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = f; }
            }
            return best;
        }

        /// <summary>Struck (or startled): rock back away from <paramref name="from"/>.</summary>
        public void Flinch(Vector3 from, float strength = 1f)
        {
            var away = transform.position - from; away.y = 0f;
            joltAway = away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward;
            jolt = Mathf.Max(jolt, strength);
        }

        /// <summary>Gather the named head parts under a neck pivot at <paramref name="neckY"/> and animate them.</summary>
        public static FolkIdle Attach(Transform root, float neckY, params string[] headParts)
        {
            var neck = new GameObject("Neck").transform;
            neck.SetParent(root, false);
            neck.localPosition = new Vector3(0f, neckY, 0f);
            var move = new System.Collections.Generic.List<Transform>();
            foreach (Transform c in root)
                foreach (var n in headParts)
                    if (c != neck && c.name == n) { move.Add(c); break; }
            foreach (var c in move) c.SetParent(neck, true);
            var idle = root.gameObject.AddComponent<FolkIdle>();
            idle.neck = neck;
            return idle;
        }

        private void Start()
        {
            seed = Random.value * 20f;
            baseScale = transform.localScale;
            baseRot = transform.localRotation;
            nextGlance = Time.time + Random.Range(1f, 4f);
        }

        private void Update()
        {
            float t = Time.time + seed;
            // Breathing: a slow rise and fall.
            transform.localScale = new Vector3(baseScale.x * (1f + Mathf.Sin(t * 1.4f) * 0.006f), baseScale.y * (1f + Mathf.Sin(t * 1.4f) * 0.009f), baseScale.z);
            // A flinch: the whole figure rocks back and settles.
            jolt = Mathf.MoveTowards(jolt, 0f, Time.deltaTime * 2.5f);
            var axis = Vector3.Cross(Vector3.up, joltAway);
            var lean = jolt > 0f ? Quaternion.AngleAxis(Mathf.Sin(jolt * Mathf.PI) * 14f * jolt, transform.parent != null ? transform.parent.InverseTransformDirection(axis) : axis) : Quaternion.identity;
            transform.localRotation = lean * baseRot;
            if (neck == null) return;

            float wantYaw = glanceYaw, wantPitch = 0f;
            var cam = ViewCamera.Current;
            bool watching = false;
            if (cam != null)
            {
                var to = cam.transform.position - neck.position;
                if (to.sqrMagnitude < 9f * 9f)
                {
                    var local = transform.InverseTransformDirection(to);
                    float y = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    if (Mathf.Abs(y) < 100f)
                    {
                        watching = true;
                        wantYaw = Mathf.Clamp(y, -60f, 60f);
                        wantPitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -20f, 20f);
                    }
                }
            }
            if (!watching && Time.time >= nextGlance)
            {
                nextGlance = Time.time + Random.Range(2.5f, 6f);
                glanceYaw = Random.Range(-35f, 35f);
            }
            float nod = Mathf.Max(0f, Mathf.Sin(t * 0.7f) - 0.93f) * 60f;
            yaw = Mathf.Lerp(yaw, wantYaw, 1f - Mathf.Exp(-Time.deltaTime * 3f));
            pitch = Mathf.Lerp(pitch, wantPitch, 1f - Mathf.Exp(-Time.deltaTime * 3f));
            neck.localRotation = Quaternion.Euler(pitch + nod, yaw, Mathf.Sin(t * 0.5f) * 2f);
        }
    }
}
