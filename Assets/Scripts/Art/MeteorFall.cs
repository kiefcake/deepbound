using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The meteor itself: a burning, cracked boulder that falls from high and to one side,
    /// trailing fire and smoke, its light swelling over the target until it lands exactly
    /// when the server resolves the hit. Purely visual; every peer spawns its own.
    /// </summary>
    public class MeteorFall : MonoBehaviour
    {
        private static readonly Color Fire = new Color(1f, 0.5f, 0.15f);

        private Vector3 from, to;
        private float duration, age, nextPuff;
        private Transform rock;
        private Light glow;

        private static Mesh puffMesh;
        private Vector3? ceiling;
        private bool brokeThrough;
        private float wait;

        /// <param name="startAfter">Seconds before it appears (a second meteor following the first).</param>
        public static void Spawn(Vector3 target, float delay, int seed, float startAfter = 0f)
        {
            var rng = new System.Random(seed);
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            var go = new GameObject("MeteorFall");
            var m = go.AddComponent<MeteorFall>();
            m.to = target;
            // Underground, it comes through the ceiling: start just below it and break the stone.
            float height = 24f;
            if (Physics.Raycast(target + Vector3.up * 0.5f, Vector3.up, out var roof, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                height = Mathf.Max(4f, roof.point.y - target.y - 0.3f);
                m.ceiling = target + Vector3.up * (height + 0.2f);
            }
            m.from = target + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * height * 0.35f + Vector3.up * height;
            m.duration = delay;
            m.wait = startAfter;
            go.transform.position = m.from;
            m.Build();
            if (startAfter > 0f) foreach (Transform c in go.transform) c.gameObject.SetActive(false);
        }

        private void Build()
        {
            rock = MeshKit.Pivot(transform, "Rock", Vector3.zero);
            // A lumpy stone: a core ball and a few knuckles, with molten seams between.
            MeshKit.Part(rock, "Core", MeshKit.Ball(0.75f, 8, 6), Mat.Rubble, Vector3.zero);
            for (int i = 0; i < 4; i++)
            {
                var d = Quaternion.Euler(i * 70f, i * 95f, 0f) * Vector3.forward;
                MeshKit.Part(rock, "Knuckle" + i, MeshKit.Ball(0.45f, 6, 5), Mat.Rubble, d * 0.5f);
            }
            var molten = MeshKit.Part(rock, "Molten", MeshKit.Ball(0.82f, 8, 6), Mat.Glow, Vector3.zero, Vector3.zero, new Vector3(0.9f, 1.05f, 0.9f));
            ArtLibrary.SetGlow(molten.GetComponent<Renderer>(), Fire * 1.6f);


            glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.type = LightType.Point;
            glow.color = Fire;
            glow.range = 10f;
            glow.intensity = 2f;
            glow.shadows = LightShadows.None;
        }

        private void Update()
        {
            if (wait > 0f)
            {
                wait -= Time.deltaTime;
                if (wait <= 0f) foreach (Transform c in transform) c.gameObject.SetActive(true);
                return;
            }
            age += Time.deltaTime;
            float k = Mathf.Clamp01(age / duration);
            if (!brokeThrough && ceiling.HasValue)
            {
                brokeThrough = true;
                var hole = new Vector3(from.x, ceiling.Value.y, from.z);
                BoneBurst.Spawn(hole + Vector3.down * 0.8f, 1.2f, false, Mat.Rubble);
                Vfx.Smoke(hole + Vector3.down * 0.5f, 3, 0.6f, 1.5f);
                Sfx.Play(Sound.BoneBreak, hole, 0.8f, 0.5f);
            }
            // Accelerates as it comes down.
            transform.position = Vector3.Lerp(from, to, k * k);
            rock.Rotate(new Vector3(260f, 140f, 90f) * Time.deltaTime);
            glow.intensity = Mathf.Lerp(2f, 9f, k);
            glow.range = Mathf.Lerp(8f, 16f, k);

            if (Time.time >= nextPuff)
            {
                // A comet's tail of fire: hot puffs shed behind, shrinking and dimming.
                nextPuff = Time.time + 0.012f;
                var back = (from - to).normalized;
                var hot = Random.value < 0.4f ? new Color(1f, 0.85f, 0.4f) : Fire;
                Vfx.Spawn(puffMesh ??= MeshKit.Ball(0.5f, 6, 4), Mat.Glow, transform.position + back * 0.6f + Random.insideUnitSphere * 0.35f,
                    back * 1.5f + Random.insideUnitSphere * 0.6f, Random.Range(0.25f, 0.45f), Random.Range(0.5f, 0.95f), 0.05f, hot * 1.7f);
                if (Random.value < 0.3f) Vfx.Embers(transform.position, 1, Fire, 3f, 0.07f, 0.6f);
            }
            if (age >= duration) Destroy(gameObject);
        }
    }
}
