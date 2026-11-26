using System.Collections.Generic;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.Art
{
    public class TripWireArt : MonoBehaviour
    {
        private static readonly List<TripWireArt> All = new List<TripWireArt>();

        private int index;
        private Transform wire;

        public static void Build(Transform parent, DungeonLayout.TripWire t, int index)
        {
            var go = new GameObject("TripWire" + index);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(t.At, Quaternion.LookRotation(t.Across));
            var art = go.AddComponent<TripWireArt>();
            art.index = index;
            float half = DungeonLayout.CorridorWidth / 2f, y = t.High ? DungeonLayout.TripHighHeight : DungeonLayout.TripLowHeight;
            art.wire = MeshKit.Pivot(go.transform, "Wire", Vector3.up * y);
            var line = MeshKit.Part(art.wire, "Line", MeshKit.Box(new Vector3(0.025f, 0.025f, half * 2f)), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(line.GetComponent<Renderer>(), new Color(0.75f, 0.7f, 0.6f) * 0.35f);
            line.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (float s in new[] { -1f, 1f })
                MeshKit.Part(go.transform, "Peg", MeshKit.Cylinder(0.03f, 0.05f, 0.22f, 6), Mat.Steel, new Vector3(0f, y, s * (half - 0.05f)), new Vector3(90f * s, 0f, 0f));
            var keg = MeshKit.Pivot(go.transform, "Keg", new Vector3(0.45f, 0f, half - 0.45f));
            MeshKit.Part(keg, "Barrel", MeshKit.Cylinder(0.28f, 0.28f, 0.6f, 10), Mat.Wood, Vector3.up * 0.3f);
            foreach (float band in new[] { 0.1f, 0.5f })
                MeshKit.Part(keg, "Hoop", MeshKit.Cylinder(0.295f, 0.295f, 0.05f, 10), Mat.Steel, Vector3.up * band);
            MeshKit.Part(keg, "Fuse", MeshKit.Box(new Vector3(0.02f, 0.02f, 0.5f)), Mat.Cloth, new Vector3(-0.2f, 0.62f, -0.2f), new Vector3(-20f, -45f, 0f));
            All.Add(art);
        }

        public static void Sprung(ulong mask)
        {
            foreach (var a in All)
                if (a != null && a.index < 64 && (mask & (1UL << a.index)) != 0) a.gameObject.SetActive(false);
        }

        private void OnDestroy() => All.Remove(this);
    }
}
