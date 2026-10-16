using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The way out after a boss: a standing ring of pale light over a glowing floor circle,
    /// with motes drifting up through it. Local only (every peer builds its own); it lives
    /// under the level root, so it goes when the floor does.
    /// </summary>
    public class ExitPortal : MonoBehaviour
    {
        private Transform ring;
        private Light glow;
        private float nextMote;
        private static readonly Color Pale = new Color(0.55f, 0.85f, 1f);

        public static void Spawn(Vector3 at, Transform levelRoot)
        {
            var go = new GameObject("ExitPortal");
            go.transform.SetParent(levelRoot, true);
            go.transform.position = at;
            go.AddComponent<ExitPortal>().Build();
            Sfx.Play(Sound.Revive, at + Vector3.up, 1f, 0.7f);
        }

        private void Build()
        {
            float r = World.DungeonDirector.ExitRadius;
            var floor = MeshKit.Part(transform, "Circle", MeshKit.Cylinder(r, r, 0.02f, 32), Mat.Glow, Vector3.up * 0.03f);
            ArtLibrary.SetGlow(floor.GetComponent<Renderer>(), Pale * 0.35f);
            var rim = MeshKit.Part(transform, "Rim", MeshKit.Lathe("portalRim", new[] { new Vector2(r - 0.1f, 0f), new Vector2(r + 0.05f, 0.04f), new Vector2(r - 0.1f, 0.08f) }, 32, false), Mat.Glow, Vector3.up * 0.04f);
            ArtLibrary.SetGlow(rim.GetComponent<Renderer>(), Pale * 1.4f);
            ring = MeshKit.Pivot(transform, "Ring", Vector3.up * 2.2f);
            var hoop = MeshKit.Part(ring, "Hoop", MeshKit.Lathe("portalHoop", new[] { new Vector2(1.5f, -0.06f), new Vector2(1.62f, 0f), new Vector2(1.5f, 0.06f) }, 40, false), Mat.Glow, Vector3.zero, new Vector3(90f, 0f, 0f));
            ArtLibrary.SetGlow(hoop.GetComponent<Renderer>(), Pale * 1.8f);
            var veil = MeshKit.Part(ring, "Veil", MeshKit.Cylinder(1.5f, 1.5f, 0.02f, 32), Mat.Telegraph, Vector3.zero, new Vector3(90f, 0f, 0f));
            ArtLibrary.Tint(veil.GetComponent<Renderer>(), new Color(0.6f, 0.9f, 1f, 0.22f));
            foreach (var rr in GetComponentsInChildren<Renderer>()) rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow = new GameObject("PortalLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * 2.2f;
            glow.color = Pale;
            glow.range = 10f;
            glow.intensity = 2.5f;
            glow.shadows = LightShadows.None;
        }

        private void Update()
        {
            if (ring != null) ring.localRotation = Quaternion.Euler(0f, Time.time * 25f, 0f);
            if (glow != null) glow.intensity = 2.3f + Mathf.Sin(Time.time * 2f) * 0.4f;
            if (Time.time >= nextMote)
            {
                nextMote = Time.time + 0.12f;
                var p = transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0.2f, World.DungeonDirector.ExitRadius);
                Vfx.Embers(p + Vector3.up * 0.1f, 1, Pale, 1.4f, 0.05f, 1.2f);
            }
        }
    }
}
