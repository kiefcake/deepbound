using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Client-side: roots coiled round a held player's legs (an Entling has them). Gnarled, twisting
    /// tendrils rise from the floor around the feet, tighten in, and shudder; gone when it lets go.
    /// </summary>
    public class RootCage : MonoBehaviour
    {
        private static readonly Dictionary<Transform, RootCage> On = new Dictionary<Transform, RootCage>();
        private Transform player;
        private Object source;
        private float born;
        private readonly List<Transform> tendrils = new List<Transform>();

        public static void Attach(Transform player, Object source)
        {
            Detach(player);
            var go = new GameObject("RootCage");
            var c = go.AddComponent<RootCage>();
            c.player = player;
            c.source = source;
            c.born = Time.time;
            var bark = new Color(0.32f, 0.22f, 0.14f);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 360f / 7f + Random.Range(-12f, 12f);
                var pivot = MeshKit.Pivot(go.transform, "Tendril" + i, Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.42f);
                pivot.localRotation = Quaternion.Euler(-28f, a + 180f, Random.Range(-15f, 15f));
                // Three segments, each a little thinner and bent further in.
                var parent = pivot;
                float r = 0.07f;
                for (int sgm = 0; sgm < 3; sgm++)
                {
                    var seg = MeshKit.Part(parent, "Seg", MeshKit.Cylinder(r, r * 0.75f, 0.42f, 6), Mat.Wood, Vector3.zero, Vector3.zero);
                    ArtLibrary.Tint(seg.GetComponent<Renderer>(), bark * Random.Range(0.85f, 1.1f));
                    var next = MeshKit.Pivot(parent, "Joint", new Vector3(0f, 0.4f, 0f));
                    next.localRotation = Quaternion.Euler(-22f, 0f, Random.Range(-10f, 10f));
                    parent = next;
                    r *= 0.72f;
                }
                if (i % 2 == 0)
                {
                    var leaf = MeshKit.Part(parent, "Leaf", MeshKit.Box(new Vector3(0.08f, 0.01f, 0.12f)), Mat.Cloth, Vector3.zero, new Vector3(20f, 0f, 30f));
                    ArtLibrary.Tint(leaf.GetComponent<Renderer>(), new Color(0.2f, 0.32f, 0.12f));
                }
                c.tendrils.Add(pivot);
            }
            Vfx.Smoke(player.position, 2, 0.4f, 0.8f);
            On[player] = c;
        }

        public static void Detach(Transform player)
        {
            if (player != null && On.TryGetValue(player, out var c) && c != null)
            {
                Vfx.Smoke(c.transform.position, 2, 0.4f, 0.8f);
                Destroy(c.gameObject);
            }
            if (player != null) On.Remove(player);
        }

        private void Update()
        {
            if (player == null || source == null) { if (player != null) On.Remove(player); Destroy(gameObject); return; }
            transform.position = player.position;
            // Grow up out of the floor, then keep squeezing.
            float grow = Mathf.Clamp01((Time.time - born) / 0.35f);
            float squeeze = 1f + Mathf.Sin(Time.time * 6f) * 0.04f;
            transform.localScale = new Vector3(squeeze, grow, squeeze);
        }
    }
}
