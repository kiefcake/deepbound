using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Deepbound.Art
{
    /// <summary>
    /// A real mirror (2026-11-08, the user: "make a large mirror when reworking the tailor so i and players can see emotes").
    /// A second camera stands at the viewer's reflection behind the glass, looks out through it with an off-axis frustum
    /// cut exactly to the glass (near plane = the mirror plane, so nothing behind it shows), and renders into a texture
    /// on the glass. Your own body (shadows-only in first person) is shown to that camera alone; the view-model layer
    /// is left out. It renders only while you are within <see cref="Range"/> m, in front of it, and it is on screen.
    /// Local to each peer; built at camp by <c>Lobby</c>.
    /// </summary>
    public class Mirror : MonoBehaviour
    {
        public const float Range = 14f;
        public Vector2 Size = new Vector2(1.3f, 2.2f);

        private Camera cam;
        private RenderTexture rt;
        private Renderer glass;
        private readonly List<Renderer> shown = new List<Renderer>();

        public static Mirror Build(Transform parent, Vector3 centre, Vector3 facing, Vector2 size)
        {
            var go = new GameObject("Mirror");
            go.transform.SetParent(parent, false);
            facing.y = 0f;
            go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(facing.normalized));
            var m = go.AddComponent<Mirror>();
            m.Size = size;
            m.MakeGlass();
            return m;
        }

        private void MakeGlass()
        {
            float w = Size.x / 2f, h = Size.y / 2f;
            var mesh = new Mesh { name = "MirrorGlass" };
            // In front of the frame by a hair, facing out (+Z). Clockwise as seen from the front.
            mesh.vertices = new[] { new Vector3(-w, -h, 0.012f), new Vector3(-w, h, 0.012f), new Vector3(w, h, 0.012f), new Vector3(w, -h, 0.012f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var g = new GameObject("Glass");
            g.transform.SetParent(transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            glass = g.AddComponent<MeshRenderer>();
            glass.shadowCastingMode = ShadowCastingMode.Off;
            var baseMat = Resources.Load<Material>("Materials/Cards");
            if (baseMat == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { glass.enabled = false; return; }
            var mat = new Material(baseMat) { name = "MirrorGlass" };
            rt = new RenderTexture(Mathf.RoundToInt(360 * Size.x / Size.y * 1.6f), 576, 24) { name = "MirrorRT", filterMode = FilterMode.Point };
            mat.mainTexture = rt;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", rt);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.86f, 0.88f, 0.9f));
            glass.sharedMaterial = mat;

            cam = new GameObject("MirrorCamera").AddComponent<Camera>();
            cam.transform.SetParent(transform, false);
            cam.targetTexture = rt;
            cam.cullingMask = ~(1 << Player.NetworkPlayer.ViewModelLayerIndex);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            cam.depth = -10f;
            cam.enabled = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginContextRendering += Place;
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= Place;
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            Restore();
        }

        private void OnDestroy()
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
        }

        /// <summary>Once a frame, before any camera: stand the mirror camera at the viewer's reflection.</summary>
        private void Place(ScriptableRenderContext ctx, List<Camera> cams)
        {
            if (cam == null) return;
            var viewer = ViewCamera.Current;
            if (viewer == null || viewer == cam) { cam.enabled = false; return; }
            var n = transform.forward;
            var c = transform.position;
            var e = viewer.transform.position;
            float dist = Vector3.Dot(e - c, n);
            bool show = dist > 0.05f && (e - c).sqrMagnitude < Range * Range && glass.isVisible;
            if (!show) { cam.enabled = false; return; }
            var mirrored = e - 2f * dist * n;
            cam.transform.SetPositionAndRotation(mirrored, Quaternion.LookRotation(n, transform.up));
            // Off-axis frustum cut to the glass (camera axes = the mirror's own right/up/forward).
            var toC = c - mirrored;
            float d = Vector3.Dot(toC, n);
            float xc = Vector3.Dot(toC, transform.right), yc = Vector3.Dot(toC, transform.up);
            float l = xc - Size.x / 2f, r = xc + Size.x / 2f, b = yc - Size.y / 2f, t = yc + Size.y / 2f;
            cam.projectionMatrix = Matrix4x4.Frustum(l, r, b, t, d, d + 30f);
            cam.enabled = true;
        }

        /// <summary>Your own first-person body is shadows-only: let the mirror camera see it.</summary>
        private void Begin(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam) return;
            shown.Clear();
            var me = Player.NetworkPlayer.Local;
            if (me == null) return;
            foreach (var r in me.GetComponentsInChildren<Renderer>())
            {
                if (r.shadowCastingMode != ShadowCastingMode.ShadowsOnly || r.gameObject.layer == Player.NetworkPlayer.ViewModelLayerIndex) continue;
                r.shadowCastingMode = ShadowCastingMode.On;
                shown.Add(r);
            }
        }

        private void End(ScriptableRenderContext ctx, Camera c)
        {
            if (c == cam) Restore();
        }

        private void Restore()
        {
            foreach (var r in shown) if (r != null) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            shown.Clear();
        }
    }
}
