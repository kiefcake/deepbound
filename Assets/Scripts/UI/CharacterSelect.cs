using Deepbound.Art;
using Deepbound.Classes;
using Deepbound.Core;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Deepbound.UI
{
    /// <summary>
    /// The character select screen: one card per class with a live 3D model turning on a
    /// plinth, the class's role and difficulty, its base stats, and its full kit.
    ///
    /// <para>
    /// Opens by itself the first time you arrive at camp, and again with B while at camp.
    /// The models are real class rigs on a small lit stage far below the world, each
    /// rendered by its own camera into a texture the card draws — the same art the
    /// game uses, not a separate portrait.
    /// </para>
    /// </summary>
    public class CharacterSelect : MonoBehaviour
    {
        public static bool Open { get; private set; }

        private NetworkPlayer player;
        private PlayerProgress progress;
        private bool shownOnce, toldLate;

        private static readonly Vector3 StageOrigin = new Vector3(0f, -900f, 0f);
        private GameObject stage;
        private RenderTexture[] previews;
        private Transform[] turntables;

        public void Init(NetworkPlayer owner)
        {
            player = owner;
            progress = owner.GetComponent<PlayerProgress>();
        }

        private void OnDestroy()
        {
            Open = false;
            if (stage != null) Destroy(stage);
            if (previews != null) foreach (var rt in previews) if (rt != null) rt.Release();
        }

        private void Update()
        {
            bool inLobby = DungeonDirector.InLobby;
            bool canChange = progress != null ? progress.CanChangeClass : Progression.PlayerProgress.ClassChangeAllowed;
            if (progress != null && progress.LateJoiner.Value && !inLobby && !toldLate)
            {
                toldLate = true;
                GameHud.Feed("You joined a run in progress. You can choose your class back at camp.", new Color(1f, 0.8f, 0.45f));
            }
            if (inLobby && !shownOnce && DungeonDirector.ArenaId > 0 && canChange)
            {
                shownOnce = true;
                SetOpen(true);
            }

            var keyboard = Keyboard.current;
            // Class is chosen for a whole run: only at camp, and not between floors.
            if (keyboard != null && Binds.Down(Bind.Class) && inLobby && canChange) SetOpen(!Open);
            if (Open && (!inLobby || !canChange || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))) SetOpen(false);

            if (Open && turntables != null)
            {
                // Sways around the front: a full turn showed the class's back half the time on the screen where you choose it.
                foreach (var t in turntables) t.rotation = Quaternion.Euler(0f, 200f + 35f * Mathf.Sin(Time.unscaledTime * 0.6f), 0f);
            }
        }

        /// <summary>Close the screen (the smoke test uses this after capturing it).</summary>
        public void Close() => SetOpen(false);

        private void SetOpen(bool open)
        {
            Open = open;
            if (open && stage == null) BuildStage();
            if (stage != null) stage.SetActive(open);
            // The previews render to textures; at a render scale below 1 (Low/Medium graphics) URP's
            // Forward+ light tiles no longer line up with them and the portraits come out in dark
            // blocks. Full scale while the screen is open, the preset back when it closes.
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                if (open) urp.renderScale = 1f;
                else Settings.ApplyGraphics();
            }
        }

        // ------------------------------------------------------------------
        // Preview stage
        // ------------------------------------------------------------------

        private void BuildStage()
        {
            int n = ClassDefinition.ById.Length;
            stage = new GameObject("CharacterSelectStage");
            previews = new RenderTexture[n];
            previewCams = new Camera[n];
            turntables = new Transform[n];

            for (int i = 0; i < n; i++)
            {
                var spot = StageOrigin + new Vector3(i * 20f, 0f, 0f);

                var b = new MeshKit.Batcher();
                b.AddBox(spot + Vector3.down * 0.2f, new Vector3(8f, 0.4f, 8f), Mat.Flagstone);
                b.Add(MeshKit.Cylinder(1.1f, 1.2f, 0.25f, 12), Mat.StoneWall, Matrix4x4.Translate(spot));
                b.AddBox(spot + new Vector3(0f, 3f, 2.5f), new Vector3(8f, 6f, 0.5f), Mat.StoneWall);
                b.Build(stage.transform, "Plinth" + i, collide: false);

                var table = new GameObject("Turntable" + i).transform;
                table.SetParent(stage.transform, false);
                table.position = spot + Vector3.up * 0.25f;
                table.rotation = Quaternion.Euler(0f, 200f, 0f);
                var color = player.SlotColor;
                var rig = (ClassId)i == ClassId.Mage ? CharacterArt.BuildMage(table, color)
                    : (ClassId)i == ClassId.Cleric ? CharacterArt.BuildCleric(table, color)
                    : (ClassId)i == ClassId.Rogue ? CharacterArt.BuildRogue(table, color)
                    : (ClassId)i == ClassId.Ranger ? CharacterArt.BuildRanger(table, color)
                    : (ClassId)i == ClassId.Flagellant ? CharacterArt.BuildFlagellant(table, color)
                    : (ClassId)i == ClassId.PlagueDoctor ? CharacterArt.BuildPlagueDoctor(table, color)
                    : CharacterArt.BuildKnight(table, color);
                rig.LowGuard = (ClassId)i == ClassId.Knight; // was Blocking: the raised shield hid him (the user, 2026-11-07)
                turntables[i] = table;

                // Warm key light and a cool rim, like the dungeon itself.
                AddLight(spot + new Vector3(-2f, 3f, -2.5f), new Color(1f, 0.7f, 0.45f), 9f);
                AddLight(spot + new Vector3(2.2f, 2.5f, 1.5f), new Color(0.45f, 0.6f, 1f), 6f);
                AddLight(spot + new Vector3(1.5f, 1.4f, -2.2f), new Color(1f, 0.85f, 0.7f), 3f);

                previews[i] = new RenderTexture(480, 600, 24) { name = "ClassPreview" + i };
                var cam = new GameObject("PreviewCam" + i).AddComponent<Camera>();
                cam.transform.SetParent(stage.transform, false);
                cam.transform.SetPositionAndRotation(spot + new Vector3(0f, 1.15f, -4.1f), Quaternion.Euler(2f, 0f, 0f));
                cam.fieldOfView = 34f;
                cam.targetTexture = previews[i];
                previewCams[i] = cam;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.03f, 0.03f, 0.04f);
                cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            }
        }

        private void AddLight(Vector3 at, Color color, float intensity)
        {
            var l = new GameObject("StageLight").AddComponent<Light>();
            l.transform.SetParent(stage.transform, false);
            l.transform.position = at;
            l.type = LightType.Point;
            l.color = color;
            l.range = 8f;
            l.intensity = intensity;
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        private int viewing = -1;

        /// <summary>Screenshots only: show one class, turned to a given yaw (200 = front, 20 = back).</summary>
        /// <summary>Shots: every preview figure holds its guard up (the Knight's shield, HumanoidRig.GuardShield).</summary>
        public void DebugBlock(bool up)
        {
            if (turntables == null) return;
            foreach (var t in turntables)
                if (t != null) foreach (var r in t.GetComponentsInChildren<Art.HumanoidRig>()) r.Blocking = up;
        }

        public void DebugView(int i, float yaw)
        {
            viewing = i;
            if (turntables != null && i < turntables.Length) turntables[i].rotation = Quaternion.Euler(0f, yaw, 0f);
        }
        private Camera[] previewCams;

        private void OnGUI()
        {
            useGUILayout = false; // no GUILayout here: skip the layout pass (perf)
            UiSkin.Apply();
            if (!Open || progress == null) return;
            float w = UiScale.W, h = UiScale.H;
            GUI.depth = -10;
            int n = ClassDefinition.ById.Length;
            if (viewing < 0 || viewing >= n) viewing = (int)progress.ClassId.Value;

            // Only the class being looked at renders its preview.
            if (previewCams != null)
                for (int i = 0; i < previewCams.Length; i++) previewCams[i].enabled = i == viewing;

            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.82f));
            UiSkin.Shadowed(new Rect(0, h * 0.03f, w, 50), "CHOOSE YOUR CLASS", new GUIStyle(UiSkin.Title) { fontSize = 40 });
            GUI.Label(new Rect(0, h * 0.03f + 48, w, 24), "Your choice lasts the run. Change it at camp with B before the next one.",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });

            float top = h * 0.14f, bottom = h * 0.95f;
            float listW = Mathf.Min(300f, w * 0.26f);
            float total = Mathf.Min(1180f, w - 60f);
            float left = w / 2f - total / 2f;

            // --- Class list (scales to any number of classes) ---
            float rowH = Mathf.Min(64f, (bottom - top - (n - 1) * 8f) / n);
            for (int i = 0; i < n; i++)
            {
                var id = (ClassId)i;
                var def = ClassDefinition.For(id);
                var r = new Rect(left, top + i * (rowH + 8f), listW, rowH);
                bool selected = progress.ClassId.Value == id;
                bool view = viewing == i;
                if (selected) UiSkin.Rect(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, 0.9f));
                UiSkin.Rect(r, view ? new Color(0.22f, 0.17f, 0.1f, 0.95f) : new Color(0.06f, 0.055f, 0.05f, 0.92f));
                GUI.Label(new Rect(r.x + 14, r.y + 6, r.width - 20, 28), def.Name.ToUpperInvariant(), new GUIStyle(UiSkin.Heading) { fontSize = 20 });
                GUI.Label(new Rect(r.x + 14, r.y + 32, r.width - 20, 20), def.Role + (selected ? "   — chosen" : ""), UiSkin.Small);
                if (r.Contains(Event.current.mousePosition)) viewing = i;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Choose(id);
            }

            // --- Detail panel for the class being viewed ---
            var panel = new Rect(left + listW + 24f, top, total - listW - 24f, bottom - top);
            DrawDetail(panel, (ClassId)viewing);
        }

        private void Choose(ClassId id)
        {
            if (progress.ClassId.Value == id)
            {
                SetOpen(false);
                return;
            }
            if (progress != null ? !progress.CanChangeClass : !Progression.PlayerProgress.ClassChangeAllowed) { SetOpen(false); return; }
            progress.ChooseClassRpc(id);
            Sfx.Play2D(Sound.Unsheathe, 0.7f);
        }

        private void DrawDetail(Rect r, ClassId id)
        {
            var def = ClassDefinition.For(id);
            bool selected = progress.ClassId.Value == id;
            UiSkin.Panel(r, 0.95f);

            // Preview on the left of the panel, details on the right.
            float previewW = r.width * 0.42f;
            var previewRect = new Rect(r.x + 14, r.y + 14, previewW, r.height - 28 - 60);
            if (previews != null && previews[(int)id] != null) GUI.DrawTexture(previewRect, previews[(int)id], ScaleMode.ScaleAndCrop);

            float x = previewRect.xMax + 22, cw = r.xMax - x - 18, y = r.y + 16;
            GUI.Label(new Rect(x, y, cw, 40), def.Name.ToUpperInvariant(), new GUIStyle(UiSkin.Heading) { fontSize = 34 });
            GUI.Label(new Rect(x, y + 42, cw, 20), def.Role, UiSkin.Body);
            GUI.Label(new Rect(x, y + 64, cw, 20), ClassDefinition.Difficulty(id), new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Italic });
            GUI.Label(new Rect(x, y + 86, cw, 40), ClassDefinition.Pitch(id), UiSkin.Small);
            y += 130;

            for (int s = 0; s < Stats.Count; s++)
            {
                var stat = (StatType)s;
                float v = def.BaseStats[s];
                float max = stat == StatType.HP ? 300f : stat == StatType.MP ? 150f : 60f;
                float col = x + (s % 2) * cw / 2f;
                float row = y + (s / 2) * 22;
                float bw = cw / 2f - 90;
                GUI.Label(new Rect(col, row, 40, 20), Stats.ShortName(stat), UiSkin.Small);
                UiSkin.Rect(new Rect(col + 40, row + 6, bw, 9), new Color(0.15f, 0.13f, 0.12f));
                UiSkin.Rect(new Rect(col + 40, row + 6, bw * Mathf.Clamp01(v / max), 9), Loot.LootInfo.StatColor(stat));
                GUI.Label(new Rect(col + 46 + bw, row, 40, 20), Mathf.RoundToInt(v).ToString(), UiSkin.Small);
            }
            y += 4 * 22 + 14;

            var kit = new System.Collections.Generic.List<(string, string, string)>(ClassDefinition.Kit(id)) { ("R", Ultimates.Name(id), Ultimates.Describe(id)), ("COMBOS", "", ClassDefinition.ComboLine(id)) };
            float kitRow = Mathf.Min(52f, (r.yMax - 76 - y) / kit.Count);
            foreach (var (key, name, desc) in kit)
            {
                GUI.Label(new Rect(x, y, 70, 20), key, new GUIStyle(UiSkin.Small) { normal = { textColor = UiSkin.Gold }, fontStyle = FontStyle.Bold, fontSize = 13 });
                GUI.Label(new Rect(x + 74, y, cw - 74, kitRow), (name.Length > 0 ? "<b>" + name + "</b>  " : "") + desc, new GUIStyle(UiSkin.Body) { fontSize = 14, wordWrap = true });
                y += kitRow;
            }

            var button = new Rect(r.x + 14, r.yMax - 60, r.width - 28, 46);
            if (GUI.Button(button, selected ? "READY" : "PLAY " + def.Name.ToUpperInvariant(), UiSkin.ButtonBig)) Choose(id);
        }
    }
}
