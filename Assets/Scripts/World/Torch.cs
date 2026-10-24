using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Flickers a point light and its flame mesh.
    ///
    /// Each torch gets its own noise offset so a corridor of them does not pulse in
    /// unison — synchronised flicker is the fastest way to make firelight look fake.
    /// </summary>
    public class Torch : MonoBehaviour
    {
        public Light Light;
        public Transform Flame;
        public float BaseIntensity = 6f;
        /// <summary>Scales the light (a boss's death gutters every fire for a moment).</summary>
        public float Dim = 1f;
        private static Mesh emberMesh;
        private static Mesh EmberMesh => emberMesh ??= Art.MeshKit.Box(Vector3.one);

        private float offset;
        private Vector3 flameScale;
        private Vector3 lightRest;
        private Quaternion flameRest;
        private float nextEmber;
        private Transform[] tongues;

        // Crackle (2026-10-24): the few fires nearest the listener crackle, each from its own looped voice.
        private static readonly System.Collections.Generic.List<Torch> all = new System.Collections.Generic.List<Torch>();
        private static AudioSource[] crackle;
        private static Torch[] crackleOn;
        private static AudioClip crackleClip;
        private static float nextCrackle;
        private const int CrackleVoices = 3;
        private const float CrackleRange = 9f;

        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);

        private static void TickCrackle()
        {
            if (Time.time < nextCrackle) return;
            nextCrackle = Time.time + 0.25f;
            if (crackle == null)
            {
                crackleClip = Resources.Load<AudioClip>("Audio/Ambience/fire_crackle");
                if (crackleClip == null) { nextCrackle = float.MaxValue; return; }
                var root = new GameObject("Torch Crackle");
                DontDestroyOnLoad(root);
                crackle = new AudioSource[CrackleVoices];
                crackleOn = new Torch[CrackleVoices];
                for (int i = 0; i < CrackleVoices; i++)
                {
                    var s = new GameObject("Crackle" + i).AddComponent<AudioSource>();
                    s.transform.SetParent(root.transform, false);
                    s.clip = crackleClip; s.loop = true; s.playOnAwake = false;
                    s.spatialBlend = 1f; s.minDistance = 1.2f; s.maxDistance = CrackleRange;
                    s.rolloffMode = AudioRolloffMode.Linear; s.dopplerLevel = 0f; s.volume = 0f;
                    crackle[i] = s;
                }
            }
            var cam = Art.ViewCamera.Current;
            var ear = cam != null ? cam.transform.position : Vector3.zero;
            // The nearest few lit fires in range.
            var pick = new Torch[CrackleVoices];
            var dist = new float[CrackleVoices];
            for (int i = 0; i < CrackleVoices; i++) dist[i] = CrackleRange * CrackleRange;
            if (cam != null)
                foreach (var t in all)
                {
                    if (t == null || t.Flame == null || t.Dim < 0.3f) continue;
                    float d = (t.Flame.position - ear).sqrMagnitude;
                    for (int i = 0; i < CrackleVoices; i++)
                        if (d < dist[i])
                        {
                            for (int j = CrackleVoices - 1; j > i; j--) { dist[j] = dist[j - 1]; pick[j] = pick[j - 1]; }
                            dist[i] = d; pick[i] = t;
                            break;
                        }
                }
            // Keep a voice on a fire it already serves; hand free voices to the new ones.
            for (int v = 0; v < CrackleVoices; v++)
                if (crackleOn[v] != null && System.Array.IndexOf(pick, crackleOn[v]) < 0) crackleOn[v] = null;
            foreach (var t in pick)
            {
                if (t == null || System.Array.IndexOf(crackleOn, t) >= 0) continue;
                int free = System.Array.IndexOf(crackleOn, null);
                if (free < 0) break;
                crackleOn[free] = t;
                var s = crackle[free];
                s.transform.position = t.Flame.position;
                s.pitch = 0.85f + (t.offset % 1f) * 0.3f;
                if (!s.isPlaying) { s.time = Random.value * (crackleClip.length - 0.1f); s.Play(); }
            }
            for (int v = 0; v < CrackleVoices; v++)
            {
                var t = crackleOn[v];
                float target = t != null ? 0.35f * Mathf.Clamp01(t.flameScale.y * 3f + 0.4f) : 0f;
                crackle[v].volume = Mathf.MoveTowards(crackle[v].volume, target, 0.25f);
            }
        }

        private void Start()
        {
            offset = Random.value * 100f;
            if (Flame != null)
            {
                flameScale = Flame.localScale; flameRest = Flame.localRotation;
                var list = new System.Collections.Generic.List<Transform>();
                for (int i = 0; i < 3; i++) { var tg = Flame.Find("Tongue" + i); if (tg != null) list.Add(tg); }
                tongues = list.ToArray();
            }
            if (Light != null) lightRest = Light.transform.localPosition;
            nextEmber = Time.time + Random.value * 2f;
        }

        private void Update()
        {
            TickCrackle();
            float t = Time.time * 7f + offset;
            float n = Mathf.PerlinNoise(t, offset) * 0.7f + Mathf.PerlinNoise(t * 2.7f, offset + 5f) * 0.3f;

            if (Light != null)
            {
                Light.intensity = BaseIntensity * (0.75f + n * 0.45f) * Dim;
                // The flame wanders, so its shadows breathe a little.
                Light.transform.localPosition = lightRest + new Vector3(Mathf.PerlinNoise(t * 0.6f, offset + 9f) - 0.5f, 0f, Mathf.PerlinNoise(offset + 3f, t * 0.6f) - 0.5f) * 0.06f;
            }
            if (Flame != null)
            {
                Flame.localScale = new Vector3(flameScale.x * (0.9f + n * 0.2f), flameScale.y * (0.8f + n * 0.45f), flameScale.z * (0.9f + n * 0.2f));
                Flame.localRotation = flameRest * Quaternion.Euler((Mathf.PerlinNoise(t * 0.8f, offset + 1f) - 0.5f) * 14f, 0f, (Mathf.PerlinNoise(offset + 2f, t * 0.8f) - 0.5f) * 14f);
                // Each tongue leaps and sinks on its own rhythm.
                if (tongues != null)
                    for (int i = 0; i < tongues.Length; i++)
                    {
                        float k = Mathf.PerlinNoise(t * 1.3f + i * 3.1f, offset + i);
                        tongues[i].localScale = new Vector3(1f, 0.5f + k * 0.9f, 1f);
                    }
                // Now and then a spark lifts off (only near the viewer: cheap).
                if (Time.time >= nextEmber)
                {
                    nextEmber = Time.time + Random.Range(0.35f, 1.2f);
                    var cam = Art.ViewCamera.Current;
                    if (cam != null && (cam.transform.position - Flame.position).sqrMagnitude < 14f * 14f)
                        Art.Vfx.Spawn(EmberMesh, Art.Mat.Glow, Flame.position + Random.insideUnitSphere * 0.05f, Vector3.up * Random.Range(0.6f, 1.2f) + Random.insideUnitSphere * 0.25f,
                            Random.Range(0.8f, 1.6f), 0.025f * Mathf.Max(1f, flameScale.x * 1.6f), 0f, (Light != null ? Light.color : new Color(1f, 0.6f, 0.3f)) * 2.5f, -0.4f, 0.8f);
                }
            }
        }
    }
}
