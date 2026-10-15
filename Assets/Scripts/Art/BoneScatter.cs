using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The dead falling apart: once a creature has crumpled, its pieces come loose — bones
    /// clatter outward, bounce on the floor, settle, and sink away. Every peer runs it locally
    /// from the death animation; nothing is synced, nothing collides with gameplay.
    /// </summary>
    public class BoneScatter : MonoBehaviour
    {
        private struct Piece { public Transform T; public Vector3 Velocity, Spin; public float Floor; public bool Resting; }
        private readonly List<Piece> pieces = new List<Piece>();
        private float born;
        private const float Life = 3.2f, SinkFrom = 2.2f;

        /// <summary>Break <paramref name="rig"/> apart. Large creatures scatter less violently.</summary>
        public static void Break(HumanoidRig rig)
        {
            if (rig == null) return;
            var go = new GameObject("Remains");
            go.transform.position = rig.transform.position;
            var s = go.AddComponent<BoneScatter>();
            s.born = Time.time;
            float floor = rig.transform.position.y;
            float scale = Mathf.Max(0.6f, rig.transform.lossyScale.y);
            var renderers = rig.GetComponentsInChildren<MeshRenderer>();
            int n = 0;
            foreach (var r in renderers)
            {
                if (n++ > 48) { r.enabled = false; continue; }
                var t = r.transform;
                t.SetParent(go.transform, true);
                var away = t.position - rig.transform.position;
                away.y = 0f;
                var v = (away.normalized * Random.Range(0.6f, 2.4f) + Vector3.up * Random.Range(1.5f, 3.5f)) / Mathf.Sqrt(scale);
                s.pieces.Add(new Piece { T = t, Velocity = v, Spin = Random.insideUnitSphere * 540f, Floor = floor });
                // Glowing bits (eyes, runes) go dark as they fall.
                if (r.sharedMaterial != null && r.sharedMaterial.name.Contains("Glow")) ArtLibrary.SetGlow(r, Color.black);
            }
            foreach (var l in rig.GetComponentsInChildren<Light>()) l.enabled = false;
            Destroy(go, Life);
        }

        private void Update()
        {
            float age = Time.time - born;
            float dt = Time.deltaTime;
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (p.T == null) continue;
                if (!p.Resting)
                {
                    p.Velocity += Vector3.down * 14f * dt;
                    var pos = p.T.position + p.Velocity * dt;
                    if (pos.y < p.Floor + 0.03f)
                    {
                        pos.y = p.Floor + 0.03f;
                        // Bounce once or twice, losing most of it.
                        p.Velocity = new Vector3(p.Velocity.x * 0.45f, -p.Velocity.y * 0.3f, p.Velocity.z * 0.45f);
                        p.Spin *= 0.4f;
                        if (p.Velocity.sqrMagnitude < 0.3f) p.Resting = true;
                    }
                    p.T.position = pos;
                    p.T.Rotate(p.Spin * dt, Space.World);
                }
                else if (age > SinkFrom)
                {
                    p.T.position += Vector3.down * 0.35f * dt;
                }
                pieces[i] = p;
            }
        }
    }
}
