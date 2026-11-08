using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.UI;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// THE BOARD OF THE FALLEN on the tavern's south wall (2026-11-08, the activities plan: "the names of the dead go on a
    /// memorial wall after a wipe"). Local to each machine, like the Hall of Records it reads from
    /// (<see cref="Records.Fallen"/>, the last <see cref="Records.FallenKept"/> parties that wiped): one brass plate and
    /// one lit candle per lost party; walk up to it and the names show over it. Nothing is networked or written here.
    /// The transform stands on the wall face, forward = into the room.
    /// </summary>
    public class MemorialBoard : MonoBehaviour
    {
        public const float ReadRange = 3.2f;
        /// <summary>Screenshots only: names to show instead of this machine's records (nothing is saved).</summary>
        public static string[] DebugLines;

        private readonly Renderer[] plates = new Renderer[Records.FallenKept];
        private readonly Renderer[] flames = new Renderer[Records.FallenKept];
        private Light glow;
        private string[] lines = new string[0];
        private float nextRead;
        private static GUIStyle titleStyle, lineStyle;

        private static readonly Color Brass = new Color(0.78f, 0.6f, 0.3f);
        private static readonly Color Blank = new Color(0.2f, 0.16f, 0.12f);

        private void Start()
        {
            var plank = new Color(0.2f, 0.13f, 0.09f);
            var timber = new Color(0.12f, 0.08f, 0.05f);
            Tint(MeshKit.Part(transform, "Backing", MeshKit.Box(new Vector3(1.15f, 0.82f, 0.05f)), Mat.Wood, new Vector3(0f, 0f, 0.025f)).transform, plank);
            // A frame, a carved lintel with a little gable, and the shelf of candles below.
            Tint(MeshKit.Part(transform, "FrameTop", MeshKit.Box(new Vector3(1.27f, 0.07f, 0.08f)), Mat.Wood, new Vector3(0f, 0.44f, 0.04f)).transform, timber);
            Tint(MeshKit.Part(transform, "FrameLow", MeshKit.Box(new Vector3(1.27f, 0.07f, 0.08f)), Mat.Wood, new Vector3(0f, -0.44f, 0.04f)).transform, timber);
            foreach (float s in new[] { -1f, 1f })
            {
                Tint(MeshKit.Part(transform, "FrameSide", MeshKit.Box(new Vector3(0.07f, 0.95f, 0.08f)), Mat.Wood, new Vector3(s * 0.6f, 0f, 0.04f)).transform, timber);
                Tint(MeshKit.Part(transform, "Gable", MeshKit.Box(new Vector3(0.42f, 0.06f, 0.07f)), Mat.Wood, new Vector3(s * 0.19f, 0.54f, 0.04f), new Vector3(0f, 0f, -s * 18f)).transform, timber);
            }
            MeshKit.Part(transform, "Skull", MeshKit.Ball(0.05f, 8, 6), Mat.Bone, new Vector3(0f, 0.53f, 0.07f), default, new Vector3(1f, 1.1f, 0.8f));
            Tint(MeshKit.Part(transform, "Shelf", MeshKit.Box(new Vector3(1.2f, 0.05f, 0.2f)), Mat.Wood, new Vector3(0f, -0.56f, 0.1f)).transform, timber);
            for (int i = 0; i < Records.FallenKept; i++)
            {
                int col = i % 2, row = i / 2;
                var plate = MeshKit.Part(transform, "Plate", MeshKit.Box(new Vector3(0.46f, 0.11f, 0.012f)), Mat.Gold, new Vector3(col == 0 ? -0.26f : 0.26f, 0.24f - row * 0.24f, 0.056f));
                plates[i] = plate.GetComponent<Renderer>();
                // Candle stubs of uneven height along the shelf; the flame only burns for a lost party.
                float x = -0.5f + i * 0.2f, hgt = 0.06f + (i * 37 % 5) * 0.012f;
                MeshKit.Part(transform, "Candle", MeshKit.Cylinder(0.022f, 0.022f, hgt, 6), Mat.Bone, new Vector3(x, -0.535f, 0.12f));
                var f = MeshKit.Part(transform, "Flame", MeshKit.Ball(0.018f, 6, 4), Mat.Glow, new Vector3(x, -0.535f + hgt + 0.025f, 0.12f), default, new Vector3(1f, 1.7f, 1f));
                f.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ArtLibrary.SetGlow(f.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.25f) * 1.8f);
                flames[i] = f.GetComponent<Renderer>();
            }
            glow = new GameObject("MemorialGlow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0f, -0.35f, 0.35f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.62f, 0.32f);
            glow.range = 2.4f;
            glow.shadows = LightShadows.None;
            Refresh();
        }

        private static void Tint(Transform t, Color c) { if (t != null) ArtLibrary.Tint(t.GetComponent<Renderer>(), c); }

        private void Refresh()
        {
            lines = DebugLines ?? Records.Fallen ?? new string[0];
            for (int i = 0; i < Records.FallenKept; i++)
            {
                bool lost = i < lines.Length;
                if (plates[i] != null) ArtLibrary.Tint(plates[i], lost ? Brass : Blank);
                if (flames[i] != null) flames[i].enabled = lost;
            }
            if (glow != null) { glow.enabled = lines.Length > 0; glow.intensity = 0.35f + 0.12f * lines.Length; }
        }

        private void Update()
        {
            // A wipe is written on the run summary, by which time this camp may already stand: look again now and then.
            if (Time.time >= nextRead) { nextRead = Time.time + 4f; Refresh(); }
            if (lines.Length == 0) return;
            float t = Time.time;
            for (int i = 0; i < lines.Length && i < flames.Length; i++)
                if (flames[i] != null) flames[i].transform.localScale = new Vector3(1f, 1.5f + Mathf.Sin(t * 9f + i * 1.7f) * 0.25f, 1f);
            if (glow != null) glow.intensity = (0.35f + 0.12f * lines.Length) * (0.9f + Mathf.PerlinNoise(t * 3f, 0.3f) * 0.2f);
        }

        /// <summary>Close and facing it: the names over the board.</summary>
        private void OnGUI()
        {
            useGUILayout = false;
            var cam = ViewCamera.Current;
            if (cam == null || NetworkPlayer.Local == null || PauseMenu.Open || GameHud.PanelShowing || !DungeonDirector.InLobby) return;
            var to = transform.position - cam.transform.position;
            if (to.magnitude > ReadRange || Vector3.Dot(cam.transform.forward, to.normalized) < 0.55f || Vector3.Dot(transform.forward, -to) < 0f) return;
            UiSkin.Apply();
            var sp = UiScale.World(cam, transform.position + Vector3.up * 0.75f);
            if (sp.z <= 0f) return;
            titleStyle ??= new GUIStyle(UiSkin.Small) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            lineStyle ??= new GUIStyle(UiSkin.Small) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            const float w = 420f;
            int n = Mathf.Max(1, lines.Length);
            float hgt = 46f + n * 22f;
            var r = new Rect(sp.x - w / 2f, UiScale.H - sp.y - hgt, w, hgt);
            UiSkin.Rect(r, new Color(0.04f, 0.03f, 0.025f, 0.82f));
            titleStyle.normal.textColor = UiSkin.Gold;
            GUI.Label(new Rect(r.x, r.y + 6f, w, 22f), "THE FALLEN  -  remembered at the Gutted Lantern", titleStyle);
            lineStyle.normal.textColor = new Color(0.9f, 0.86f, 0.78f);
            if (lines.Length == 0) GUI.Label(new Rect(r.x + 10f, r.y + 32f, w - 20f, 22f), "No names yet. Marta would like to keep it that way.", lineStyle);
            for (int i = 0; i < lines.Length; i++) GUI.Label(new Rect(r.x + 10f, r.y + 32f + i * 22f, w - 20f, 22f), lines[i], lineStyle);
        }
    }
}
