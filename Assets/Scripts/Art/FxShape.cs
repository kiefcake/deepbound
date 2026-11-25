using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    public class FxShape : MonoBehaviour
    {
        public float Life = 1f;

        public float Bright = 1f;

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

        public FxShape Ride(Transform t, Vector3 offset, bool yaw = false)
        {
            Follow = t; Offset = offset; FollowYaw = yaw; followed = t != null;
            Place();
            return this;
        }

        public Transform Light(Mesh mesh, Color c, Vector3 pos, Quaternion rot, Vector3? scale = null, bool soft = true, Transform parent = null)
        {
            var t = NewPart("L", mesh, soft ? SoftMat : SolidMat, pos, rot, scale, parent, out var r);
            parts.Add(new Part { R = r, C = c, Fades = true, M = 1f });
            return t;
        }

        public Transform Shade(Mesh mesh, Color c, Vector3 pos, Quaternion rot, Vector3? scale = null, Transform parent = null)
        {
            var t = NewPart("S", mesh, ArtLibrary.Get(Mat.Smoke), pos, rot, scale, parent, out var r);
            parts.Add(new Part { R = r, C = c, Fades = true, M = 1f });
            return t;
        }

        public Transform Solid(Mesh mesh, string material, Vector3 pos, Quaternion rot, Vector3? scale = null, Transform parent = null, Color? glow = null)
        {
            var t = NewPart("O", mesh, ArtLibrary.Get(material), pos, rot, scale, parent, out var r);
            if (glow.HasValue) ArtLibrary.SetGlow(r, glow.Value * Mathf.Lerp(1f, Gain, 0.6f));
            return t;
        }

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

        public void Recolor(Transform part, Color c)
        {
            var r = part.GetComponent<Renderer>();
            for (int i = 0; i < parts.Count; i++) if (parts[i].R == r) parts[i] = new Part { R = r, C = c, Fades = true, M = parts[i].M };
        }

        public void Dim(Transform part, float m)
        {
            var r = part.GetComponent<Renderer>();
            for (int i = 0; i < parts.Count; i++) if (parts[i].R == r) { var q = parts[i]; q.M = m; parts[i] = q; return; }
        }

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

        private static Material solidMat, softMat;
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

                float a = soft ? Mathf.Pow(Mathf.Sin(v * Mathf.PI), 1.6f) : 1f;
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
