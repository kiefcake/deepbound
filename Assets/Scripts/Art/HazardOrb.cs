using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// An Orbweaver's orb, as drawn on every peer: a burning amber core in two turning rings, with a
    /// small light, following <see cref="Enemies.OrbweaverBrain.OrbPosition"/> on the server clock.
    /// The rings show which pattern it fires (spiral: one tilted ring; rings: two flat; aimed: an eye).
    /// </summary>
    public class HazardOrb : MonoBehaviour
    {
        private static readonly Dictionary<(Object, int), HazardOrb> live = new Dictionary<(Object, int), HazardOrb>();
        private Vector3 from, to;
        private double start;
        private float life, born;
        private Transform ringA, ringB, core;
        private Light glow;
        private static readonly Color Amber = new Color(1f, 0.62f, 0.2f);

        private static double Now => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        public static void Spawn(Object owner, int id, Vector3 from, Vector3 to, double start, float life, byte pattern)
        {
            var go = new GameObject("HazardOrb");
            var o = go.AddComponent<HazardOrb>();
            o.from = from; o.to = to; o.start = start; o.life = life; o.born = Time.time;
            o.core = MeshKit.Part(go.transform, "Core", MeshKit.Ball(0.3f, 12, 8), Mat.Glow, Vector3.zero).transform;
            ArtLibrary.SetGlow(o.core.GetComponent<Renderer>(), Amber * 1.4f);
            var halo = MeshKit.Part(go.transform, "Halo", MeshKit.Ball(0.48f, 12, 8), Mat.Smoke, Vector3.zero);
            ArtLibrary.Tint(halo.GetComponent<Renderer>(), new Color(1f, 0.55f, 0.2f, 0.18f));
            var ringMesh = MeshKit.Lathe("orbRing", new[] { new Vector2(0.55f, -0.02f), new Vector2(0.6f, 0f), new Vector2(0.55f, 0.02f) }, 24, false);
            o.ringA = MeshKit.Part(go.transform, "RingA", ringMesh, Mat.Glow, Vector3.zero).transform;
            ArtLibrary.SetGlow(o.ringA.GetComponent<Renderer>(), Amber);
            if (pattern != 0)
            {
                o.ringB = MeshKit.Part(go.transform, "RingB", ringMesh, Mat.Glow, Vector3.zero, Vector3.zero, Vector3.one * (pattern == 2 ? 0.6f : 1.25f)).transform;
                ArtLibrary.SetGlow(o.ringB.GetComponent<Renderer>(), Amber * 0.8f);
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            o.glow = go.AddComponent<Light>();
            o.glow.type = LightType.Point;
            o.glow.color = Amber;
            o.glow.range = 6f;
            o.glow.intensity = 2.2f;
            o.glow.shadows = LightShadows.None;
            live[(owner, id)] = o;
            o.Update();
        }

        public static void Pop(Object owner, int id)
        {
            if (live.TryGetValue((owner, id), out var o) && o != null) Destroy(o.gameObject);
            live.Remove((owner, id));
        }

        private void Update()
        {
            double now = Now;
            if (now - start > life + 0.5) { Destroy(gameObject); return; }
            transform.position = Enemies.OrbweaverBrain.OrbPosition(from, to, start, now);
            float t = Time.time - born;
            float grow = Mathf.Clamp01(t / 0.4f);
            transform.localScale = Vector3.one * grow;
            ringA.localRotation = Quaternion.Euler(60f, t * 140f, 0f);
            if (ringB != null) ringB.localRotation = Quaternion.Euler(0f, -t * 90f, 90f);
            float pulse = 1f + 0.15f * Mathf.Sin(t * 9f);
            core.localScale = Vector3.one * pulse;
            glow.intensity = 1.8f + 0.6f * pulse;
        }
    }
}
