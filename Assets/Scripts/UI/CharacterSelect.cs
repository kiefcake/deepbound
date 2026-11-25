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

            if (keyboard != null && Binds.Down(Bind.Class) && inLobby && canChange) SetOpen(!Open);
            if (Open && (!inLobby || !canChange || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))) SetOpen(false);

            if (Open && turntables != null)
            {
                foreach (var t in turntables) t.rotation = Quaternion.Euler(0f, 200f + 35f * Mathf.Sin(Time.unscaledTime * 0.6f), 0f);
            }
        }

        public void Close() => SetOpen(false);

        private void SetOpen(bool open)
        {
            Open = open;
            if (open && stage == null) BuildStage();
            if (stage != null) stage.SetActive(open);

            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                if (open) urp.renderScale = 1f;
                else Settings.ApplyGraphics();
            }
        }

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
                rig.LowGuard = (ClassId)i == ClassId.Knight;
                turntables[i] = table;

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

        private int viewing = -1;

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
            useGUILayout = false;
            UiSkin.Apply();
            if (!Open || progress == null) return;
            float w = UiScale.W, h = UiScale.H;
            GUI.depth = -10;
            int n = ClassDefinition.ById.Length;
            if (viewing < 0 || viewing >= n) viewing = (int)progress.ClassId.Value;

            if (previewCams != null)
                for (int i = 0; i < previewCams.Length; i++) previewCams[i].enabled = i == viewing;

            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.82f));
            UiSkin.Shadowed(new Rect(0, h * 0.03f, w, 50), "CHOOSE YOUR CLASS", UiSkin.Derive(UiSkin.Title, UiSkin.Title.normal.textColor, fontSize: 40));
            GUI.Label(new Rect(0, h * 0.03f + 48, w, 24), "Your choice lasts the run. Change it at camp with B before the next one.",
                UiSkin.Derive(UiSkin.Small, UiSkin.Small.normal.textColor, fontSize: 14, align: TextAnchor.MiddleCenter));

            float top = h * 0.14f, bottom = h * 0.95f;
            float listW = Mathf.Min(300f, w * 0.26f);
            float total = Mathf.Min(1180f, w - 60f);
            float left = w / 2f - total / 2f;

            float rowH = Mathf.Min(72f, (bottom - top - (n - 1) * 8f) / n);
            for (int i = 0; i < n; i++)
            {
                var id = (ClassId)i;
                var def = ClassDefinition.For(id);
                var acc = Accent(id);
                bool selected = progress.ClassId.Value == id;
                bool view = viewing == i;
                var r = new Rect(left + (view ? 10f : 0f), top + i * (rowH + 8f), listW, rowH);
                if (view) UiSkin.Rect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(acc.r, acc.g, acc.b, 0.75f));
                UiSkin.Rect(r, view ? new Color(0.13f, 0.11f, 0.09f, 0.98f) : new Color(0.06f, 0.055f, 0.05f, 0.92f));
                UiSkin.Rect(new Rect(r.x, r.y, 6, r.height), new Color(acc.r, acc.g, acc.b, view ? 1f : 0.65f));

                var disc = new Rect(r.x + 16, r.y + r.height / 2f - 20, 40, 40);
                UiSkin.Rect(disc, new Color(acc.r * 0.35f, acc.g * 0.35f, acc.b * 0.35f, 0.95f));
                UiSkin.Rect(new Rect(disc.x, disc.yMax - 3, disc.width, 3), acc);
                GUI.Label(disc, def.Name.Substring(0, 1), UiSkin.Derive(UiSkin.Heading, Color.Lerp(acc, Color.white, 0.4f), fontSize: 22, align: TextAnchor.MiddleCenter));
                float tx = disc.xMax + 12, tw = r.xMax - tx - 10;
                GUI.Label(new Rect(tx, r.y + 7, tw, 26), def.Name.ToUpperInvariant(), UiSkin.Derive(UiSkin.Heading, view ? Color.white : new Color(0.88f, 0.85f, 0.8f), fontSize: 19));
                GUI.Label(new Rect(tx, r.y + 33, tw - 60, 18), def.Role, UiSkin.Derive(UiSkin.Small, UiSkin.Small.normal.textColor, fontSize: 12, wrap: false));
                Pips(new Rect(tx, r.yMax - 15, 60, 8), DifficultyLevel(id), acc);
                if (selected)
                {
                    var badge = new Rect(r.xMax - 74, r.y + 8, 64, 18);
                    UiSkin.Rect(badge, new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, 0.9f));
                    GUI.Label(badge, "CHOSEN", UiSkin.Derive(UiSkin.Small, new Color(0.1f, 0.08f, 0.05f), fontSize: 11, fontStyle: FontStyle.Bold, align: TextAnchor.MiddleCenter));
                }
                if (r.Contains(Event.current.mousePosition)) viewing = i;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Choose(id);
            }

            var panel = new Rect(left + listW + 34f, top, total - listW - 34f, bottom - top);
            DrawDetail(panel, (ClassId)viewing);
        }

        public static Color Accent(ClassId id) => id switch
        {
            ClassId.Knight => new Color(0.72f, 0.78f, 0.88f),
            ClassId.Cleric => new Color(1f, 0.82f, 0.4f),
            ClassId.Flagellant => new Color(0.9f, 0.25f, 0.2f),
            ClassId.Rogue => new Color(0.68f, 0.45f, 1f),
            ClassId.PlagueDoctor => new Color(0.55f, 0.85f, 0.3f),
            ClassId.Mage => new Color(1f, 0.55f, 0.2f),
            ClassId.Ranger => new Color(0.45f, 0.85f, 0.45f),
            _ => UiSkin.Gold,
        };

        private static int DifficultyLevel(ClassId id)
        {
            var d = ClassDefinition.Difficulty(id);
            return d.StartsWith("Easy") ? 1 : d.StartsWith("Hard") ? 3 : 2;
        }

        private static void Pips(Rect r, int level, Color c)
        {
            for (int k = 0; k < 3; k++)
                UiSkin.Rect(new Rect(r.x + k * 16, r.y, 12, r.height), k < level ? c : new Color(c.r, c.g, c.b, 0.18f));
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
            var acc = Accent(id);
            UiSkin.Panel(r, 0.95f);
            UiSkin.Rect(new Rect(r.x, r.y, r.width, 4), acc);

            float previewW = r.width * 0.4f;
            var previewRect = new Rect(r.x + 14, r.y + 18, previewW, r.height - 32 - 60);
            UiSkin.Rect(new Rect(previewRect.x - 2, previewRect.y - 2, previewRect.width + 4, previewRect.height + 4), new Color(acc.r, acc.g, acc.b, 0.5f));
            if (previews != null && previews[(int)id] != null) GUI.DrawTexture(previewRect, previews[(int)id], ScaleMode.ScaleAndCrop);
            for (int k = 0; k < 8; k++)
                UiSkin.Rect(new Rect(previewRect.x, previewRect.yMax - 70 + k * 8.75f, previewRect.width, 8.75f), new Color(0f, 0f, 0f, 0.08f + k * 0.07f));
            GUI.Label(new Rect(previewRect.x + 14, previewRect.yMax - 44, previewRect.width - 28, 20), def.Role.ToUpperInvariant(), UiSkin.Derive(UiSkin.Small, acc, fontSize: 13, fontStyle: FontStyle.Bold));
            Pips(new Rect(previewRect.x + 14, previewRect.yMax - 20, 60, 8), DifficultyLevel(id), acc);

            float x = previewRect.xMax + 22, cw = r.xMax - x - 18, y = r.y + 16;
            GUI.Label(new Rect(x, y, cw, 40), def.Name.ToUpperInvariant(), UiSkin.Derive(UiSkin.Heading, Color.Lerp(acc, Color.white, 0.55f), fontSize: 34));
            GUI.Label(new Rect(x, y + 42, cw, 20), ClassDefinition.Difficulty(id), UiSkin.Derive(UiSkin.Small, UiSkin.Small.normal.textColor, fontSize: 13, fontStyle: FontStyle.Italic));
            var pitchSt = new GUIStyle(UiSkin.Body) { fontSize = 14, wordWrap = true };
            float pitchH = pitchSt.CalcHeight(new GUIContent(ClassDefinition.Pitch(id)), cw);
            GUI.Label(new Rect(x, y + 66, cw, pitchH), ClassDefinition.Pitch(id), pitchSt);
            y += 66 + pitchH + 12;

            for (int s = 0; s < Stats.Count; s++)
            {
                var stat = (StatType)s;
                float v = def.BaseStats[s];
                float max = stat == StatType.HP ? 300f : stat == StatType.MP ? 150f : 60f;
                float col = x + (s % 2) * cw / 2f;
                float row = y + (s / 2) * 20;
                float bw = cw / 2f - 90;
                GUI.Label(new Rect(col, row, 40, 20), Stats.ShortName(stat), UiSkin.Small);
                UiSkin.Rect(new Rect(col + 40, row + 6, bw, 8), new Color(0.15f, 0.13f, 0.12f));
                UiSkin.Rect(new Rect(col + 40, row + 6, bw * Mathf.Clamp01(v / max), 8), Loot.LootInfo.StatColor(stat));
                GUI.Label(new Rect(col + 46 + bw, row, 40, 20), Mathf.RoundToInt(v).ToString(), UiSkin.Small);
            }
            y += 4 * 20 + 12;

            var kit = new System.Collections.Generic.List<(string, string, string)>(ClassDefinition.Kit(id)) { ("R", Ultimates.Name(id), Ultimates.Describe(id)), ("WITH", "Plays well with", ClassDefinition.ComboLine(id)) };
            float avail = r.yMax - 76 - y, keyW = 62f;
            int size = 14;
            var st = new GUIStyle(UiSkin.Body) { fontSize = size, wordWrap = true, richText = true };
            float Need()
            {
                float t = 0f;
                foreach (var (_, name, desc) in kit) t += Mathf.Max(24f, st.CalcHeight(new GUIContent((name.Length > 0 ? "<b>" + name + "</b>  " : "") + desc), cw - keyW)) + 6f;
                return t;
            }
            while (size > 11 && Need() > avail) { size--; st.fontSize = size; }
            foreach (var (key, name, desc) in kit)
            {
                string text = (name.Length > 0 ? "<b>" + name + "</b>  " : "") + desc;
                float rh = Mathf.Max(24f, st.CalcHeight(new GUIContent(text), cw - keyW));
                bool ult = key == "R", with = key == "WITH";
                var kc = ult ? acc : with ? new Color(0.6f, 0.8f, 1f) : UiSkin.Gold;
                var cap = new Rect(x, y + 1, keyW - 10, 20);
                UiSkin.Rect(cap, new Color(kc.r, kc.g, kc.b, 0.16f));
                UiSkin.Rect(new Rect(cap.x, cap.yMax - 2, cap.width, 2), new Color(kc.r, kc.g, kc.b, 0.8f));
                GUI.Label(cap, key, UiSkin.Derive(UiSkin.Small, kc, fontSize: key.Length > 3 ? 10 : 12, fontStyle: FontStyle.Bold, align: TextAnchor.MiddleCenter));
                GUI.Label(new Rect(x + keyW, y, cw - keyW, rh), text, st);
                y += rh + 6f;
            }

            var button = new Rect(r.x + 14, r.yMax - 60, r.width - 28, 46);
            if (GUI.Button(button, selected ? "READY" : "PLAY " + def.Name.ToUpperInvariant(), UiSkin.ButtonBig)) Choose(id);
        }
    }
}
