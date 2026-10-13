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
        private bool shownOnce;

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
            if (inLobby && !shownOnce && DungeonDirector.ArenaId > 0)
            {
                shownOnce = true;
                SetOpen(true);
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.bKey.wasPressedThisFrame && inLobby) SetOpen(!Open);
            if (Open && (!inLobby || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))) SetOpen(false);

            if (Open && turntables != null)
            {
                foreach (var t in turntables) t.Rotate(0f, 35f * Time.deltaTime, 0f);
            }
        }

        /// <summary>Close the screen (the smoke test uses this after capturing it).</summary>
        public void Close() => SetOpen(false);

        private void SetOpen(bool open)
        {
            Open = open;
            if (open && stage == null) BuildStage();
            if (stage != null) stage.SetActive(open);
        }

        // ------------------------------------------------------------------
        // Preview stage
        // ------------------------------------------------------------------

        private void BuildStage()
        {
            int n = ClassDefinition.ById.Length;
            stage = new GameObject("CharacterSelectStage");
            previews = new RenderTexture[n];
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
                var rig = (ClassId)i == ClassId.Mage ? CharacterArt.BuildMage(table, color) : CharacterArt.BuildKnight(table, color);
                rig.Blocking = (ClassId)i == ClassId.Knight;
                turntables[i] = table;

                // Warm key light and a cool rim, like the dungeon itself.
                AddLight(spot + new Vector3(-2f, 3f, -2.5f), new Color(1f, 0.7f, 0.45f), 6f);
                AddLight(spot + new Vector3(2.2f, 2.5f, 1.5f), new Color(0.45f, 0.6f, 1f), 4f);

                previews[i] = new RenderTexture(1024, 400, 24) { name = "ClassPreview" + i };
                var cam = new GameObject("PreviewCam" + i).AddComponent<Camera>();
                cam.transform.SetParent(stage.transform, false);
                cam.transform.SetPositionAndRotation(spot + new Vector3(0f, 1.1f, -3.3f), Quaternion.Euler(3f, 0f, 0f));
                cam.fieldOfView = 36f;
                cam.targetTexture = previews[i];
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

        private void OnGUI()
        {
            if (!Open || progress == null) return;
            float w = Screen.width, h = Screen.height;
            GUI.depth = -10;

            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.82f));
            UiSkin.Shadowed(new Rect(0, h * 0.04f, w, 50), "CHOOSE YOUR CLASS", new GUIStyle(UiSkin.Title) { fontSize = 40 });
            GUI.Label(new Rect(0, h * 0.04f + 48, w, 24), "Your choice lasts the run. You can change it at camp with B.",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });

            int n = ClassDefinition.ById.Length;
            float cardW = Mathf.Min(460f, (w - 80f - (n - 1) * 30f) / n), cardH = Mathf.Min(h * 0.78f, 720f);
            float total = n * cardW + (n - 1) * 30f;
            float x0 = w / 2f - total / 2f, y0 = h * 0.15f;

            for (int i = 0; i < n; i++) DrawCard(new Rect(x0 + i * (cardW + 30f), y0, cardW, cardH), (ClassId)i);
        }

        private void DrawCard(Rect r, ClassId id)
        {
            var def = ClassDefinition.For(id);
            bool selected = progress.ClassId.Value == id;

            if (selected) UiSkin.Rect(new Rect(r.x - 5, r.y - 5, r.width + 10, r.height + 10), new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, 0.85f));
            UiSkin.Panel(r, 0.95f);

            // 3D preview.
            float previewH = r.height * 0.3f;
            var previewRect = new Rect(r.x + 12, r.y + 12, r.width - 24, previewH);
            if (previews != null && previews[(int)id] != null) GUI.DrawTexture(previewRect, previews[(int)id], ScaleMode.ScaleToFit);

            float y = previewRect.yMax + 8;
            GUI.Label(new Rect(r.x + 16, y, r.width - 32, 30), def.Name.ToUpperInvariant(), new GUIStyle(UiSkin.Heading) { fontSize = 26 });
            GUI.Label(new Rect(r.x + 16, y + 30, r.width - 32, 20), def.Role + "   ·   " + ClassDefinition.Difficulty(id), UiSkin.Small);
            y += 56;

            // Base stats as bars, on a common scale so classes compare at a glance.
            for (int s = 0; s < Stats.Count; s++)
            {
                var stat = (StatType)s;
                float v = def.BaseStats[s];
                float max = stat == StatType.HP ? 300f : stat == StatType.MP ? 150f : 60f;
                float col = r.x + 16 + (s % 2) * (r.width - 32) / 2f;
                float row = y + (s / 2) * 18;
                GUI.Label(new Rect(col, row, 40, 18), Stats.ShortName(stat), UiSkin.Small);
                float bw = (r.width - 32) / 2f - 90;
                UiSkin.Rect(new Rect(col + 38, row + 5, bw, 8), new Color(0.15f, 0.13f, 0.12f));
                UiSkin.Rect(new Rect(col + 38, row + 5, bw * Mathf.Clamp01(v / max), 8), Loot.LootInfo.PotionColor(stat));
                GUI.Label(new Rect(col + 42 + bw, row, 40, 18), Mathf.RoundToInt(v).ToString(), UiSkin.Small);
            }
            y += 4 * 18 + 8;

            // Two lines per ability: the name and key, then what it does (wrapped).
            float room = (r.yMax - 66) - y;
            float rowH = Mathf.Min(40f, room / 5f);
            foreach (var (key, name, desc) in ClassDefinition.Kit(id))
            {
                GUI.Label(new Rect(r.x + 16, y, 64, 18), key, new GUIStyle(UiSkin.Small) { normal = { textColor = UiSkin.Gold }, fontStyle = FontStyle.Bold });
                GUI.Label(new Rect(r.x + 78, y, r.width - 94, rowH), "<b>" + name + "</b>  " + desc, new GUIStyle(UiSkin.Small) { fontSize = 11 });
                y += rowH;
            }

            var button = new Rect(r.x + 16, r.yMax - 58, r.width - 32, 44);
            if (selected)
            {
                if (GUI.Button(button, "READY", UiSkin.ButtonBig)) SetOpen(false);
            }
            else if (GUI.Button(button, "PLAY " + def.Name.ToUpperInvariant(), UiSkin.ButtonBig))
            {
                progress.ClassId.Value = id;
                Sfx.Play2D(Sound.Purchase, 0.6f);
            }
        }
    }
}
