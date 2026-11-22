using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A shaped, animated effect (2026-11-21, the user: "every ability should be discernable in battle, and look
    /// distinct"). Where <see cref="Vfx"/> puffs are specks and rings, an FxShape is a SILHOUETTE: a kite shield of
    /// light, a cross, a crescent, a whip-lash, a ghost of the body, a flask. Parts are additive light that fades to
    /// nothing (never to a black shape), soft-edged when built with <see cref="SoftMat"/>; or lit solids (steel, rubble)
    /// that shrink away. Each shape animates through a <see cref="Tick"/> callback given k = 0..1 over its life.
    /// Client only, local art: call from FX RPCs. Not pooled: abilities fire a few times a second at most.
    /// </summary>
    public class FxShape : MonoBehaviour
    {
        public float Life = 1f;
        /// <summary>Multiplies every additive part's brightness this frame (Tick sets it: flashes, fades).</summary>
        public float Bright = 1f;
        /// <summary>Rides this transform (the caster, a target) at <see cref="Offset"/>; gone with it.</summary>
        public Transform Follow;
        public Vector3 Offset;
        public bool FollowYaw;
        public System.Action<FxShape, float> Tick;
        private bool followed;
        private float born;
        public float Age => Time.time - born;

        private struct Part { public Renderer R; public Color C; public bool Fades; public float M; }
        private readonly List<Part> parts = new List<Part>();
        private static MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static FxShape Make(string name, Vector3 at, Quaternion rot, float life)
        {
            var go = new GameObject("FxShape_" + name);
            go.transform.SetPositionAndRotation(at, rot);
            var s = go.AddComponent<FxShape>();
            s.Life = life;
            s.born = Time.time;
            return s;
        }

        /// <summary>Rides <paramref name="t"/> (position, and its yaw if asked).</summary>
        public FxShape Ride(Transform t, Vector3 offset, bool yaw = false)
        {
            Follow = t; Offset = offset; FollowYaw = yaw; followed = t != null;
            Place();
            return this;
        }

        // ------------------------------------------------------------------
        // Parts
        // ------------------------------------------------------------------

        /// <summary>Additive light in <paramref name="c"/> (HDR above 1 blooms). Soft: its edges fade (meshes from <see cref="Shapes"/> carry the V gradient).</summary>
        public Transform Light(Mesh mesh, Color c, Vector3 pos, Quaternion rot, Vector3? scale = null, bool soft = true, Transform parent = null)
        {
            var t = NewPart("L", mesh, soft ? SoftMat : SolidMat, pos, rot, scale, parent, out var r);
            parts.Add(new Part { R = r, C = c, Fades = true, M = 1f });
            return t;
        }

        /// <summary>Translucent dark (alpha blended, Mat.Smoke): shadows, afterimages, soot.</summary>
        public Transform Shade(Mesh mesh, Color c, Vector3 pos, Quaternion rot, Vector3? scale = null, Transform parent = null)
        {
            var t = NewPart("S", mesh, ArtLibrary.Get(Mat.Smoke), pos, rot, scale, parent, out var r);
            parts.Add(new Part { R = r, C = c, Fades = true, M = 1f });
            return t;
        }

        /// <summary>A lit solid (steel, rubble, glass): not faded, so Tick shrinks it away.</summary>
        public Transform Solid(Mesh mesh, string material, Vector3 pos, Quaternion rot, Vector3? scale = null, Transform parent = null, Color? glow = null)
        {
            var t = NewPart("O", mesh, ArtLibrary.Get(material), pos, rot, scale, parent, out var r);
            if (glow.HasValue) ArtLibrary.SetGlow(r, glow.Value * Mathf.Lerp(1f, Gain, 0.6f));
            return t;
        }

        /// <summary>An empty pivot to hang parts from.</summary>
        public Transform Pivot(Vector3 pos, Quaternion rot, Transform parent = null)
        {
            var go = new GameObject("Pivot");
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            return go.transform;
        }

        private Transform NewPart(string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3? scale, Transform parent, out Renderer r)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            r = mr;
            return go.transform;
        }

        /// <summary>Recolours one additive part (by its renderer) from now on.</summary>
        public void Recolor(Transform part, Color c)
        {
            var r = part.GetComponent<Renderer>();
            for (int i = 0; i < parts.Count; i++) if (parts[i].R == r) parts[i] = new Part { R = r, C = c, Fades = true, M = parts[i].M };
        }

        /// <summary>One part's own brightness (0..1+) on top of <see cref="Bright"/>: a lash drawn along its length, veins creeping.</summary>
        public void Dim(Transform part, float m)
        {
            var r = part.GetComponent<Renderer>();
            for (int i = 0; i < parts.Count; i++) if (parts[i].R == r) { var q = parts[i]; q.M = m; parts[i] = q; return; }
        }

        // ------------------------------------------------------------------
        // Life
        // ------------------------------------------------------------------

        private void Place()
        {
            if (Follow == null) return;
            if (FollowYaw)
            {
                var yaw = Quaternion.Euler(0f, Follow.eulerAngles.y, 0f);
                transform.SetPositionAndRotation(Follow.position + yaw * Offset, yaw);
            }
            else transform.position = Follow.position + Offset;
        }

        /// <summary>Every ability shape's brightness (2026-11-22, the user: "tone the light levels down on ability effects").
        /// Scales the HDR colour, so bloom glare drops with it; the shapes keep their silhouettes.</summary>
        public static float Gain = 0.6f;

        private void LateUpdate()
        {
            if (followed && Follow == null) { Destroy(gameObject); return; }
            Place();
            float age = Age;
            float k = Mathf.Clamp01(age / Life);
            Bright = 1f;
            Tick?.Invoke(this, k);
            block ??= new MaterialPropertyBlock();
            foreach (var p in parts)
            {
                if (p.R == null || !p.Fades) continue;
                p.R.GetPropertyBlock(block);
                block.SetColor(BaseColorId, new Color(p.C.r * Gain, p.C.g * Gain, p.C.b * Gain, Mathf.Clamp01(p.C.a * Bright * p.M)));
                p.R.SetPropertyBlock(block);
            }
            if (age >= Life) Destroy(gameObject);
        }

        // ------------------------------------------------------------------
        // Materials: additive copies of a Kenney sprite material (its shader variant already ships), given our own
        // textures. Solid: flat white. Soft: white fading to nothing toward both V edges (ribbons, walls, blades).
        // ------------------------------------------------------------------

        private static Material solidMat, softMat, glowDotMat;
        public static Material SolidMat => solidMat != null ? solidMat : solidMat = Additive("FxSolid", Gradient(false));
        public static Material SoftMat => softMat != null ? softMat : softMat = Additive("FxSoft", Gradient(true));

        private static Material Additive(string name, Texture2D tex)
        {
            var m = new Material(ArtLibrary.Get("Fx_flare_01")) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.SetColor("_BaseColor", Color.white);
            return m;
        }

        private static Texture2D Gradient(bool soft)
        {
            const int n = 32;
            var tex = new Texture2D(4, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = soft ? "FxSoftTex" : "FxSolidTex" };
            var px = new Color[4 * n];
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) / n;
                // Soft: a bright core that falls away to the edges (a blade of light, a band of sound).
                float a = soft ? Mathf.Pow(Mathf.Sin(v * Mathf.PI), 1.6f) : 1f;
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
