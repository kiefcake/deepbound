using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The Ranger's ultimate, Twin Wyrms (2026-11-20, local art on every peer): a spirit arrow that splits into two
    /// pale serpents coiling round each other as they run straight ahead, through stone, for <c>range</c> metres.
    /// Each wyrm is a chain of glowing beads on a helix round the flight line, a horned head at the front.
    /// The damage is the server's (<see cref="Player.Ultimates"/>); this only draws it.
    /// </summary>
    public class SpiritWyrms : MonoBehaviour
    {
        private const int Beads = 22;
        private const float Coil = 1.15f, Twist = 1.3f;
        private Vector3 origin, dir, side, up;
        private float born, windup, speed, range, length;
        private Transform[][] wyrms;
        private Light glow;
        private Color color;
        private float nextEmber;

        public static void Spawn(Vector3 origin, Vector3 dir, float windup, float speed, float range, float length, Color color)
        {
            var go = new GameObject("SpiritWyrms");
            var w = go.AddComponent<SpiritWyrms>();
            w.origin = origin;
            w.dir = dir.normalized;
            w.side = Vector3.Cross(Vector3.up, w.dir).normalized;
            w.up = Vector3.Cross(w.dir, w.side).normalized;
            w.windup = windup;
            w.speed = speed;
            w.range = range;
            w.length = length;
            w.color = color;
            w.born = Time.time;
            w.Build();
            w.Update();
        }

        private void Build()
        {
            wyrms = new Transform[2][];
            for (int k = 0; k < 2; k++)
            {
                var beads = new Transform[Beads];
                for (int i = 0; i < Beads; i++)
                {
                    float taper = 1f - i / (float)Beads;
                    var bead = MeshKit.Part(transform, "Bead", MeshKit.Ball(0.16f + 0.24f * taper, 8, 6), Mat.Glow, Vector3.zero);
                    var r = bead.GetComponent<Renderer>();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    ArtLibrary.SetGlow(r, color * (0.7f + 0.9f * taper));
                    beads[i] = bead.transform;
                }
                // The head: a long snout, a jaw and two swept horns, all on the first bead.
                var head = beads[0];
                foreach (var (pos, scale) in new[] { (new Vector3(0f, 0f, 0.28f), new Vector3(0.22f, 0.16f, 0.5f)), (new Vector3(0f, -0.12f, 0.22f), new Vector3(0.16f, 0.06f, 0.4f)) })
                {
                    var part = MeshKit.Part(head, "Snout", MeshKit.Box(Vector3.one), Mat.Glow, pos, Vector3.zero, scale);
                    part.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    ArtLibrary.SetGlow(part.GetComponent<Renderer>(), color * 1.6f);
                }
                foreach (float sx in new[] { -1f, 1f })
                {
                    var horn = MeshKit.Part(head, "Horn", MeshKit.Lathe("wyrmHorn", new[] { new Vector2(0.05f, 0f), new Vector2(0f, 0.45f) }, 5), Mat.Glow, new Vector3(sx * 0.1f, 0.1f, -0.05f), new Vector3(-60f, 0f, -sx * 25f));
                    horn.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    ArtLibrary.SetGlow(horn.GetComponent<Renderer>(), color * 1.3f);
                }
                foreach (float sx in new[] { -1f, 1f })
                {
                    var eye = MeshKit.Part(head, "Eye", MeshKit.Ball(0.045f, 6, 4), Mat.Glow, new Vector3(sx * 0.12f, 0.06f, 0.3f));
                    ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), Color.white * 2f);
                }
                wyrms[k] = beads;
            }
            glow = new GameObject("WyrmLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.type = LightType.Point;
            glow.range = 9f;
            glow.intensity = 2.4f;
            glow.color = color;
            glow.shadows = LightShadows.None;
        }

        private void Update()
        {
            float age = Time.time - born;
            float travelled = Mathf.Max(0f, age - windup) * speed;
            float total = range + length;
            if (travelled > total + 0.5f) { Destroy(gameObject); return; }
            // Before the split: a single bright arrow streaking out of the bow.
            float head = Mathf.Min(travelled, range);
            float fade = travelled > range ? 1f - Mathf.Clamp01((travelled - range) / length) : 1f;
            float spacing = length / Beads;
            for (int k = 0; k < 2; k++)
            {
                float phase = k * Mathf.PI;
                for (int i = 0; i < Beads; i++)
                {
                    var bead = wyrms[k][i];
                    // Beads past the end of the run fold away as the tail catches up.
                    float along = travelled - i * spacing;
                    bool show = age >= windup && along > 0f && along <= range;
                    if (bead.gameObject.activeSelf != show) bead.gameObject.SetActive(show);
                    if (!show) continue;
                    float s = Mathf.Min(along, range);
                    float a = s * Twist + phase + age * 2.2f;
                    // The coil opens out after the first few metres.
                    float coil = Coil * Mathf.Clamp01(s / 3f);
                    var p = origin + dir * s + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * coil;
                    bead.position = p;
                    float sz = (1f - 0.5f * (1f - fade));
                    bead.localScale = Vector3.one * sz * (i == 0 ? 1.6f : 1f);
                    // Face along the helix (the head leads with its snout).
                    float a2 = (s + 0.2f) * Twist + phase + age * 2.2f;
                    var p2 = origin + dir * (s + 0.2f) + (side * Mathf.Cos(a2) + up * Mathf.Sin(a2)) * coil;
                    var fwd = p2 - p;
                    if (fwd.sqrMagnitude > 0.0001f) bead.rotation = Quaternion.LookRotation(fwd, up);
                }
            }
            glow.transform.position = origin + dir * head;
            glow.intensity = 2.4f * fade * (age < windup ? age / windup : 1f);
            if (Time.time >= nextEmber && age >= windup && travelled < range)
            {
                nextEmber = Time.time + 0.06f;
                Vfx.Embers(origin + dir * head, 3, color, 2.5f, 0.05f, 0.5f);
            }
        }
    }
}
