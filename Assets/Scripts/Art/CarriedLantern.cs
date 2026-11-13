using Deepbound.Player;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The HOODED LANTERN (2026-11-13, the user: "an item or pick up that acts like a small light source around the player at
    /// all times"): every peer hangs a small lantern at the belt of each delver whose <see cref="PlayerProgress.Lantern"/> is
    /// set, with a warm shadowless light (<see cref="Range"/>). Booted on load; nothing in batchmode.
    /// </summary>
    public class CarriedLantern : MonoBehaviour
    {
        public const float Range = 7.5f, Intensity = 1.5f;
        private static readonly Color Warm = new Color(1f, 0.72f, 0.4f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("CarriedLanterns");
            DontDestroyOnLoad(go);
            go.AddComponent<CarriedLantern>();
        }

        private float nextScan;

        private void Update()
        {
            if (Time.time < nextScan) return;
            nextScan = Time.time + 0.5f;
            foreach (var life in PlayerLife.All)
            {
                if (life == null) continue;
                var prog = life.GetComponent<PlayerProgress>();
                if (prog == null) continue;
                var have = life.transform.Find("HoodedLantern");
                if (prog.Lantern.Value && have == null) Build(life.transform);
                else if (!prog.Lantern.Value && have != null) Destroy(have.gameObject);
            }
        }

        private static void Build(Transform player)
        {
            var root = new GameObject("HoodedLantern").transform;
            root.SetParent(player, false);
            root.localPosition = new Vector3(0.28f, 0.95f, 0.12f);
            var cage = MeshKit.Part(root, "Cage", MeshKit.Cylinder(0.07f, 0.06f, 0.16f, 6), Mat.Steel, Vector3.zero);
            cage.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var flame = MeshKit.Part(root, "Flame", MeshKit.Cylinder(0.035f, 0.02f, 0.08f, 6), Mat.Glow, Vector3.up * 0.04f);
            var fr = flame.GetComponent<Renderer>();
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.SetGlow(fr, Warm * 1.8f);
            MeshKit.Part(root, "Hood", MeshKit.Cylinder(0.02f, 0.085f, 0.06f, 6), Mat.Dark, Vector3.up * 0.19f);
            // The light sits a little above and ahead of the lantern so it lights the way, not your own hands.
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(-0.2f, 0.5f, 0.6f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = Range;
            l.intensity = Intensity;
            l.color = Warm;
            l.shadows = LightShadows.None;
            lightGo.AddComponent<LanternFlicker>();
        }
    }
}
