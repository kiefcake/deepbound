using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The camp breathing (2026-11-07, the user: "ambient effects"): fireflies drifting round the trees and planters,
    /// smoke from the tavern and forge chimneys, leaves falling from the trees, and a low fog rolling in at the gate.
    /// Local on every peer, built with the camp and gone with it; nothing here is networked or touches play.
    /// </summary>
    public class CampAmbience : MonoBehaviour
    {
        public readonly List<Vector3> Glades = new List<Vector3>();   // fireflies hover round these
        public readonly List<Vector3> Crowns = new List<Vector3>();   // leaves fall from these
        public readonly List<Vector3> Chimneys = new List<Vector3>();
        public Vector3 Gate;
        public float Ground;

        private class Fly { public Transform T; public Renderer R; public Vector3 Home; public float Phase, Speed; }
        private class Leaf { public Transform T; public Vector3 Vel; public float Born, Life, Spin; }
        private readonly List<Fly> flies = new List<Fly>();
        private readonly List<Leaf> leaves = new List<Leaf>();
        private float nextSmoke, nextLeaf, nextFog;
        private static readonly Color FlyGlow = new Color(0.85f, 1f, 0.45f);

        private void Start()
        {
            if (Application.isBatchMode) { enabled = false; return; }
            foreach (var g in Glades)
                for (int i = 0; i < 2; i++)
                {
                    var m = MeshKit.Part(transform, "Firefly", MeshKit.Ball(0.035f, 5, 4), Mat.Glow, g);
                    var r = m.GetComponent<Renderer>();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    ArtLibrary.SetGlow(r, FlyGlow * 1.4f);
                    flies.Add(new Fly { T = m.transform, R = r, Home = g, Phase = Random.value * 50f, Speed = Random.Range(0.35f, 0.7f) });
                }
        }

        private void Update()
        {
            float t = Time.time;
            // Fireflies: slow looping drift round their glade, blinking on and off.
            foreach (var f in flies)
            {
                float p = f.Phase + t * f.Speed;
                f.T.position = f.Home + new Vector3(Mathf.Sin(p * 1.3f) * 2.2f, 0.9f + Mathf.Sin(p * 0.7f) * 0.6f, Mathf.Cos(p * 0.9f) * 2.2f);
                bool on = Mathf.Sin(p * 3.1f + f.Phase) > -0.2f && World.CampWeather.RainK < 0.5f; // no fireflies in the rain
                if (f.R.enabled != on) f.R.enabled = on;
            }
            // Chimney smoke.
            if (t >= nextSmoke && Chimneys.Count > 0)
            {
                nextSmoke = t + 0.9f;
                foreach (var c in Chimneys)
                    FxSprite.Billboard(FxSprite.AnySmoke, c + Random.insideUnitSphere * 0.15f, new Vector3(0.25f, Random.Range(0.6f, 0.9f), 0.1f), Random.Range(4f, 5.5f), 0.6f, 2.4f, new Color(0.35f, 0.33f, 0.32f, 0.35f), Random.Range(-20f, 20f), 0.6f);
            }
            // Gate fog: big pale drifts low across the road.
            if (t >= nextFog)
            {
                nextFog = t + 2.2f;
                var at = Gate + new Vector3(Random.Range(-6f, 6f), 0.3f, Random.Range(-2f, 3f));
                FxSprite.Billboard(FxSprite.AnySmoke, at, new Vector3(Random.Range(-0.25f, 0.25f), 0.03f, -0.2f), Random.Range(6f, 8f), 2.5f, 5f, new Color(0.55f, 0.58f, 0.66f, 0.12f), Random.Range(-8f, 8f), 0.4f);
            }
            // Leaves: one every so often from a random crown, tumbling down and lying a while.
            if (t >= nextLeaf && Crowns.Count > 0 && leaves.Count < 28)
            {
                nextLeaf = t + Random.Range(0.4f, 1.2f);
                var crown = Crowns[Random.Range(0, Crowns.Count)] + Random.insideUnitSphere * 1.2f;
                var leaf = MeshKit.Part(transform, "Leaf", MeshKit.Box(new Vector3(0.09f, 0.008f, 0.06f)), Mat.Cloth, crown);
                ArtLibrary.Tint(leaf.GetComponent<Renderer>(), Random.value < 0.5f ? new Color(0.45f, 0.25f, 0.08f) : new Color(0.3f, 0.32f, 0.1f));
                leaf.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                leaves.Add(new Leaf { T = leaf.transform, Vel = new Vector3(Random.Range(-0.3f, 0.3f), -0.45f, Random.Range(-0.3f, 0.3f)), Born = t, Life = 14f, Spin = Random.Range(90f, 240f) });
            }
            for (int i = leaves.Count - 1; i >= 0; i--)
            {
                var l = leaves[i];
                if (l.T == null) { leaves.RemoveAt(i); continue; }
                if (t - l.Born > l.Life) { Destroy(l.T.gameObject); leaves.RemoveAt(i); continue; }
                if (l.T.position.y > Ground + 0.03f)
                {
                    float sway = Mathf.Sin((t - l.Born) * 2.6f + l.Spin);
                    l.T.position += (l.Vel + new Vector3(sway * 0.5f, 0f, Mathf.Cos((t - l.Born) * 2.1f) * 0.35f)) * Time.deltaTime;
                    l.T.rotation = Quaternion.Euler(sway * 50f, (t - l.Born) * l.Spin, Mathf.Cos(t * 1.7f) * 40f);
                }
            }
        }
    }
}
