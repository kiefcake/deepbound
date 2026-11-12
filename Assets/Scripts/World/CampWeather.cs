using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// WEATHER AT CAMP (2026-11-08, the user's list: "camp life: weather"). The host rolls it on each return to camp
    /// (<see cref="DungeonDirector.Weather"/>, synced; always clear in tests): a clear night (60%), MIST (25%: thick grey
    /// fog, the gate road lost in it) or RAIN (15%). Rain comes in SHOWERS (2026-11-09, the user: "more lowkey, and not on
    /// all of the time"): on the server clock, so every peer agrees, about a minute of light rain in every two and a half
    /// (<see cref="Shower"/>); while a shower falls there are thin streaks round you that stop at any roof, the rain loop,
    /// no fireflies, and the folk stay home under cover (<see cref="FolkRounds"/>). It changes nothing in play. Local on every peer; added to the camp root by
    /// <see cref="Lobby"/>.
    /// </summary>
    public class CampWeather : MonoBehaviour
    {
        public enum Kind : byte { Clear = 0, Mist = 1, Rain = 2 }

        /// <summary>The weather now (synced).</summary>
        public static Kind Now => DungeonDirector.Instance != null ? (Kind)DungeonDirector.Instance.Weather.Value : Kind.Clear;
        /// <summary>The folk keep indoors (no errands) while it rains.</summary>
        public static bool KeepIn => DungeonDirector.InLobby && Now == Kind.Rain && (DebugShowering || Shower(Clock) > 0.15f);
        /// <summary>Tests: treat it as mid-shower whatever the clock says.</summary>
        public static bool DebugShowering;
        public const double ShowerCycle = 150, ShowerWet = 62;
        public const float ShowerRamp = 12f;
        private static double Clock => Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening ? Unity.Netcode.NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        /// <summary>
        /// How hard a rainy night is raining at server time <paramref name="t"/>, 0..~0.8: a shower of <see cref="ShowerWet"/> s
        /// (easing in and out over <see cref="ShowerRamp"/> s) in every <see cref="ShowerCycle"/> s, never a downpour.
        /// </summary>
        public static float Shower(double t)
        {
            double ph = t % ShowerCycle;
            if (ph < 0) ph += ShowerCycle;
            if (ph >= ShowerWet) return 0f;
            float k = Mathf.Clamp01((float)ph / ShowerRamp) * Mathf.Clamp01((float)(ShowerWet - ph) / ShowerRamp);
            return k * (0.45f + 0.35f * Mathf.PerlinNoise((float)(t * 0.04), 0.7f));
        }
        /// <summary>0..1, eased: how much rain / mist there is this frame (audio and ambience read these).</summary>
        public static float RainK { get; private set; }
        public static float MistK { get; private set; }

        public const int Drops = 140; // was 300: the user found it too much
        public const float Box = 12f, Near = 3.5f, FallSpeed = 13f, Streak = 0.4f, Width = 0.02f; // ~1 retro pixel at 5 m; none within Near (close ones were fat white bars)
        private static readonly Color RainTint = new Color(0.55f, 0.6f, 0.7f);

        private Mesh mesh;
        private MeshRenderer mr;
        private Vector3[] verts;
        private Vector3[] drop;
        private float[] land;
        private float baseFog;
        private Color baseFogColor;

        /// <summary>The host's roll for a new camp visit: clear 60%, mist 25%, rain 15%.</summary>
        public static byte Roll()
        {
            float r = Random.value;
            return (byte)(r < 0.6f ? Kind.Clear : r < 0.85f ? Kind.Mist : Kind.Rain);
        }

        private void Start()
        {
            baseFog = RenderSettings.fogDensity;
            baseFogColor = RenderSettings.fogColor;
            RainK = Now == Kind.Rain ? Shower(Clock) : 0f;
            MistK = Now == Kind.Mist ? 1f : 0f;
            if (Application.isBatchMode) return;
            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            // The vertices are world positions: the mesh's own transform must be the identity whatever the camp root is.
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            mesh = new Mesh { name = "RainStreaks" };
            mesh.MarkDynamic();
            verts = new Vector3[Drops * 4];
            var tris = new int[Drops * 6];
            for (int i = 0; i < Drops; i++)
            {
                int v = i * 4, k = i * 6;
                // Wound so the face points at the camera (side = up x toCam): the other way round culled every streak.
                tris[k] = v; tris[k + 1] = v + 1; tris[k + 2] = v + 2;
                tris[k + 3] = v + 1; tris[k + 4] = v + 3; tris[k + 5] = v + 2;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArtLibrary.Get(Mat.Glow);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            ArtLibrary.Tint(mr, RainTint * 0.18f); // a white base lit by the lanterns made bright white bars
            ArtLibrary.SetGlow(mr, RainTint * 0.09f);
            drop = new Vector3[Drops];
            land = new float[Drops];
            for (int i = 0; i < Drops; i++) land[i] = float.MaxValue; // respawn on the first frame
        }

        private void OnDestroy()
        {
            RainK = 0f;
            MistK = 0f;
            if (mesh != null) Destroy(mesh);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            var w = Now;
            RainK = Mathf.MoveTowards(RainK, w == Kind.Rain ? (DebugShowering ? 0.8f : Shower(Clock)) : 0f, dt / 3f);
            MistK = Mathf.MoveTowards(MistK, w == Kind.Mist ? 1f : 0f, dt / 3f);
            if (!DungeonDirector.InLobby) return;
            // Mist thickens the fog and greys it; rain a little.
            RenderSettings.fogDensity = baseFog + 0.019f * MistK + 0.004f * RainK;
            RenderSettings.fogColor = Color.Lerp(baseFogColor, new Color(0.1f, 0.11f, 0.13f), MistK * 0.8f + RainK * 0.2f);
            var cam = ViewCamera.Current;
            if (cam != null) cam.backgroundColor = RenderSettings.fogColor;
            if (mr == null) return;
            mr.enabled = RainK > 0.01f && cam != null;
            if (!mr.enabled) return;

            var eye = cam.transform.position;
            int live = Mathf.RoundToInt(Drops * RainK);
            var up = Vector3.up * Streak;
            for (int i = 0; i < Drops; i++)
            {
                int v = i * 4;
                if (i >= live) { verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = Vector3.zero; continue; }
                var p = drop[i];
                p.y -= FallSpeed * dt;
                var flat = p - eye; flat.y = 0f;
                if (p.y < land[i] || flat.sqrMagnitude > Box * Box)
                {
                    // A new drop somewhere above and around you; it stops at whatever it would land on (a roof keeps it off you).
                    var c = Random.insideUnitCircle.normalized * Random.Range(Near, Box);
                    p = new Vector3(eye.x + c.x, eye.y + Random.Range(1f, 9f), eye.z + c.y);
                    land[i] = Physics.Raycast(new Vector3(p.x, eye.y + 14f, p.z), Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : eye.y - 3f;
                    if (p.y < land[i]) p.y = land[i] + Random.Range(0.5f, 6f);
                }
                drop[i] = p;
                // A thin streak facing the camera.
                var toCam = eye - p; toCam.y = 0f;
                var side = toCam.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, toCam.normalized) * Width : Vector3.right * Width;
                verts[v] = p - side;
                verts[v + 1] = p + side;
                verts[v + 2] = p - side + up;
                verts[v + 3] = p + side + up;
            }
            mesh.vertices = verts;
        }

        /// <summary>Shots and tests: as if the weather had already settled.</summary>
        public static void DebugSettle() { RainK = Now == Kind.Rain ? (DebugShowering ? 0.8f : Shower(Clock)) : 0f; MistK = Now == Kind.Mist ? 1f : 0f; }
    }
}
