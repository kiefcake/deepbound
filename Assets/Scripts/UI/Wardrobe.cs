using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Deepbound.UI
{
    /// <summary>
    /// The camp tailor (reworked 2026-10-20): cloak dyes, HERALDRY (the colour of your tabard, plume and
    /// sash, on every class) and auras. A live portrait of your own character, turning on a lit stand,
    /// sits beside the list and shows whatever you point at, front and back, so you can see it on you
    /// before you wear it. Entirely local (saved on this machine) except what you wear, which the
    /// party sees through <see cref="NetworkPlayer.CloakDye"/>, <see cref="NetworkPlayer.Heraldry"/>
    /// and <see cref="NetworkPlayer.Aura"/>. The mannequin at the stall still wears it in the world.
    /// </summary>
    public class Wardrobe : MonoBehaviour
    {
        public static bool Open { get; private set; }
        public const float Range = 3.2f;

        private NetworkPlayer player;
        private PlayerProgress progress;
        private PlayerLife life;
        private GameObject mannequin;
        private int mannequinDye = -1, mannequinHeraldry = -1, mannequinPattern = -1, previewIndex = -1;
        private Classes.ClassId mannequinClass;
        private Cosmetics.Kind tab = Cosmetics.Kind.Cloak;
        private Vector2 scroll;
        private string flash;
        private float flashUntil, nextMote, spin, spinTarget = 200f;
        private bool wasOpen;

        // The portrait stage, far below the camp.
        private static readonly Vector3 StageAt = new Vector3(0f, -1300f, 0f);
        private GameObject stage;
        private Transform turntable;
        private Camera stageCam;
        private RenderTexture portrait;
        private int portraitDye = -1, portraitHeraldry = -1, portraitPattern = -1;
        private Classes.ClassId portraitClass = (Classes.ClassId)255;

        public void Init(NetworkPlayer owner)
        {
            player = owner;
            progress = owner.GetComponent<PlayerProgress>();
            life = owner.GetComponent<PlayerLife>();
        }

        private void OnDestroy()
        {
            Open = false;
            if (mannequin != null) Destroy(mannequin);
            if (stage != null) Destroy(stage);
            if (portrait != null) portrait.Release();
        }

        public void DebugOpen(bool open) => Open = open;
        public void DebugTab(Cosmetics.Kind kind) => SwitchTab(kind);

        private bool Near => DungeonDirector.InLobby && life != null && !life.Downed.Value &&
                             Vector3.Distance(player.transform.position, Lobby.TailorSpot) <= Range;

        private int WornNow(Cosmetics.Kind kind) => kind == Cosmetics.Kind.Aura ? player.Aura.Value : kind == Cosmetics.Kind.Heraldry ? player.Heraldry.Value
            : kind == Cosmetics.Kind.Pattern ? player.Pattern.Value : player.CloakDye.Value;

        private static readonly Cosmetics.Kind[] TabOrder = { Cosmetics.Kind.Cloak, Cosmetics.Kind.Pattern, Cosmetics.Kind.Heraldry, Cosmetics.Kind.Aura };

        private void Update()
        {
            if (player == null) return;
            var keyboard = Keyboard.current;
            bool near = Near;
            if (keyboard != null && near && Binds.Down(Bind.Interact) && !CharacterSelect.Open && !PauseMenu.Open) Open = !Open;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Open = false;
            if (keyboard != null && Open && keyboard.tabKey.wasPressedThisFrame) SwitchTab(TabOrder[(System.Array.IndexOf(TabOrder, tab) + 1) % TabOrder.Length]);
            if (!near) Open = false;
            if (!Open) previewIndex = -1;
            if (Open != wasOpen)
            {
                wasOpen = Open;
                // The portrait renders to a texture: full render scale while it shows (see CharacterSelect).
                if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                {
                    if (Open) urp.renderScale = 1f;
                    else Settings.ApplyGraphics();
                }
            }
            UpdateMannequin();
            UpdatePortrait();
        }

        private void SwitchTab(Cosmetics.Kind to)
        {
            tab = to;
            scroll = Vector2.zero;
            previewIndex = -1;
        }

        private (int dye, int heraldry, int aura, int pattern) Shown()
        {
            bool previewing = previewIndex >= 0;
            int dye = previewing && tab == Cosmetics.Kind.Cloak ? previewIndex : player.CloakDye.Value;
            int heraldry = previewing && tab == Cosmetics.Kind.Heraldry ? previewIndex : player.Heraldry.Value;
            int aura = previewing && tab == Cosmetics.Kind.Aura ? previewIndex : player.Aura.Value;
            int pattern = previewing && tab == Cosmetics.Kind.Pattern ? previewIndex : player.Pattern.Value;
            return (dye, heraldry, aura, pattern);
        }

        private Color DressFor(int heraldry) => Cosmetics.HeraldryColor(heraldry) ?? player.SlotColor;

        /// <summary>The dress form at the stall: your class, in the dye, heraldry and aura you are hovering (or wearing).</summary>
        private void UpdateMannequin()
        {
            if (!DungeonDirector.InLobby)
            {
                if (mannequin != null) Destroy(mannequin);
                mannequin = null;
                mannequinDye = -1;
                return;
            }
            if (mannequin != null) mannequin.transform.Rotate(0f, 30f * Time.deltaTime, 0f);
            var (dye, heraldry, aura, pattern) = Shown();
            var auraColor = Cosmetics.AuraColor(aura);
            if (mannequin != null && auraColor.HasValue && Time.time >= nextMote)
            {
                nextMote = Time.time + 0.09f;
                Vfx.Mote(Lobby.MannequinSpot + Vector3.up * 0.1f, auraColor.Value);
                if (turntable != null && Open) Vfx.Mote(turntable.position + Vector3.up * 0.1f, auraColor.Value);
            }

            var cls = progress.ClassId.Value;
            if (mannequin != null && dye == mannequinDye && heraldry == mannequinHeraldry && pattern == mannequinPattern && cls == mannequinClass) return;
            Quaternion? old = mannequin != null ? mannequin.transform.rotation : (Quaternion?)null;
            if (mannequin != null) Destroy(mannequin);
            mannequin = new GameObject("TailorMannequin");
            mannequin.transform.SetPositionAndRotation(Lobby.MannequinSpot, old ?? Quaternion.LookRotation(Lobby.TailorFacing));
            NetworkPlayer.BuildBody(cls, mannequin.transform, DressFor(heraldry), dye, pattern);
            mannequinPattern = pattern;
            mannequinDye = dye;
            mannequinHeraldry = heraldry;
            mannequinClass = cls;
        }

        // ------------------------------------------------------------------
        // The portrait
        // ------------------------------------------------------------------

        private void BuildStage()
        {
            stage = new GameObject("WardrobeStage");
            var b = new MeshKit.Batcher();
            b.AddBox(StageAt + Vector3.down * 0.2f, new Vector3(8f, 0.4f, 8f), Mat.Flagstone);
            b.Add(MeshKit.Cylinder(1.1f, 1.2f, 0.25f, 16), Mat.Wood, Matrix4x4.Translate(StageAt));
            b.AddBox(StageAt + new Vector3(0f, 3f, 2.6f), new Vector3(8f, 6f, 0.5f), Mat.StoneWall);
            b.Build(stage.transform, "WardrobeFloor", collide: false);
            // A hanging curtain behind, in deep red, and a gilt frame.
            var curtain = MeshKit.Part(stage.transform, "Curtain", MeshKit.Box(new Vector3(3.6f, 4f, 0.05f)), Mat.Cloth, StageAt + new Vector3(0f, 2f, 2.3f));
            ArtLibrary.Tint(curtain.GetComponent<Renderer>(), new Color(0.35f, 0.06f, 0.08f));
            turntable = new GameObject("Turntable").transform;
            turntable.SetParent(stage.transform, false);
            turntable.position = StageAt + Vector3.up * 0.25f;
            void Light(Vector3 at, Color c, float i)
            {
                var l = new GameObject("WardrobeLight").AddComponent<Light>();
                l.transform.SetParent(stage.transform, false);
                l.transform.position = at;
                l.type = LightType.Point;
                l.color = c;
                l.range = 8f;
                l.intensity = i;
            }
            Light(StageAt + new Vector3(-2f, 3f, -2.5f), new Color(1f, 0.75f, 0.5f), 9f);
            Light(StageAt + new Vector3(2.2f, 2.5f, 1.5f), new Color(0.5f, 0.6f, 1f), 6f);
            Light(StageAt + new Vector3(1.5f, 1.4f, -2.2f), new Color(1f, 0.88f, 0.75f), 3f);
            portrait = new RenderTexture(420, 560, 24) { name = "WardrobePortrait" };
            stageCam = new GameObject("WardrobeCam").AddComponent<Camera>();
            stageCam.transform.SetParent(stage.transform, false);
            stageCam.transform.SetPositionAndRotation(StageAt + new Vector3(0f, 1.15f, -4.1f), Quaternion.Euler(2f, 0f, 0f));
            stageCam.fieldOfView = 34f;
            stageCam.targetTexture = portrait;
            stageCam.clearFlags = CameraClearFlags.SolidColor;
            stageCam.backgroundColor = new Color(0.03f, 0.03f, 0.04f);
            stageCam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        }

        private void UpdatePortrait()
        {
            if (!Open)
            {
                if (stage != null && stage.activeSelf) stage.SetActive(false);
                return;
            }
            if (stage == null) BuildStage();
            if (!stage.activeSelf) stage.SetActive(true);
            var (dye, heraldry, _, pattern) = Shown();
            var cls = progress.ClassId.Value;
            if (dye != portraitDye || heraldry != portraitHeraldry || pattern != portraitPattern || cls != portraitClass)
            {
                // A new pattern is on the back: swing round to show it.
                if (pattern != portraitPattern && portraitPattern >= 0 && tab == Cosmetics.Kind.Pattern) spinTarget = Mathf.Round((spinTarget - 20f) / 360f) * 360f + 20f;
                portraitPattern = pattern;
                foreach (Transform c in turntable) Destroy(c.gameObject);
                NetworkPlayer.BuildBody(cls, turntable, DressFor(heraldry), dye, pattern);
                portraitDye = dye;
                portraitHeraldry = heraldry;
                portraitClass = cls;
            }
            // It turns slowly; the buttons swing it to the front or the back.
            spinTarget += 22f * Time.deltaTime;
            spin = Mathf.LerpAngle(spin, spinTarget, 1f - Mathf.Exp(-Time.deltaTime * 4f));
            turntable.rotation = Quaternion.Euler(0f, spin, 0f);
        }

        private void OnGUI()
        {
            if (player == null || !DungeonDirector.InLobby) return;
            UiSkin.Apply();
            float w = Screen.width, h = Screen.height;
            if (!Open)
            {
                if (Near && !CharacterSelect.Open)
                    UiSkin.Shadowed(new Rect(0, h / 2f + 40, w, 24), "Press G to visit the tailor   ·   cloaks, patterns, heraldry and auras" + (Cosmetics.AllFree ? "  (all free for now)" : "   ·   Renown " + Cosmetics.Renown),
                        new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter });
                return;
            }

            GUI.depth = -3;
            var items = Cosmetics.Of(tab);
            int longest = Mathf.Max(Mathf.Max(Cosmetics.Dyes.Length, Cosmetics.Patterns.Length), Mathf.Max(Cosmetics.Auras.Length, Cosmetics.Heraldries.Length));
            float pw = 520f, rowH = 46f, ph = Mathf.Min(h - 80f, 186f + longest * rowH);
            float portraitW = Mathf.Min(360f, w - pw - 90f), total = pw + 20f + portraitW;
            var panel = new Rect(w / 2f - total / 2f + portraitW + 20f, h / 2f - ph / 2f, pw, ph);
            var shot = new Rect(w / 2f - total / 2f, panel.y, portraitW, ph);

            // The portrait: you, turning, in what you are pointing at.
            UiSkin.Panel(shot, 0.95f);
            if (portrait != null) GUI.DrawTexture(new Rect(shot.x + 10, shot.y + 10, shot.width - 20, shot.height - 64), portrait, ScaleMode.ScaleAndCrop);
            if (GUI.Button(new Rect(shot.x + 10, shot.yMax - 46, shot.width / 2f - 15, 34), "Front", UiSkin.Button)) spinTarget = Mathf.Round((spinTarget - 200f) / 360f) * 360f + 200f;
            if (GUI.Button(new Rect(shot.x + shot.width / 2f + 5, shot.yMax - 46, shot.width / 2f - 15, 34), "Back", UiSkin.Button)) spinTarget = Mathf.Round((spinTarget - 20f) / 360f) * 360f + 20f;

            UiSkin.Panel(panel, 0.95f);
            UiSkin.Shadowed(new Rect(panel.x, panel.y + 10, pw, 40), "Mistress Vey, Tailor", new GUIStyle(UiSkin.Title) { fontSize = 28 });
            GUI.Label(new Rect(panel.x, panel.y + 50, pw, 22), Cosmetics.AllFree ? "EVERYTHING IS FREE FOR NOW  ·  pick and wear" : "RENOWN  " + Cosmetics.Renown + "      ·      earned at the end of every run",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = UiSkin.Gold } });

            string[] names = { "CLOAKS", "AURAS", "HERALDRY", "PATTERNS" };
            for (int t = 0; t < TabOrder.Length; t++)
            {
                var k = TabOrder[t];
                var r = new Rect(panel.x + pw / 2f - 238 + t * 120, panel.y + 76, 112, 30);
                bool on = tab == k;
                if (GUI.Button(r, names[(int)k], new GUIStyle(UiSkin.Button) { fontStyle = on ? FontStyle.Bold : FontStyle.Normal, normal = { textColor = on ? UiSkin.Gold : new Color(0.7f, 0.67f, 0.62f) } }) && !on) SwitchTab(k);
            }
            string blurb = tab == Cosmetics.Kind.Cloak ? "The cloak on your back, and its trim." : tab == Cosmetics.Kind.Pattern ? "Bones sewn on the back of your cloak, in any dye."
                : tab == Cosmetics.Kind.Heraldry ? "Your colours: tabard, plume, sash, hat band - on every class." : "Motes that drift up around you.";
            GUI.Label(new Rect(panel.x, panel.y + 108, pw, 20), blurb + "   (Tab switches)", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });

            var view = new Rect(panel.x + 16, panel.y + 134, pw - 32, ph - 176);
            var content = new Rect(0, 0, view.width - 20, items.Length * rowH);
            int hovered = -1;
            scroll = GUI.BeginScrollView(view, scroll, content);
            for (int i = 0; i < items.Length; i++)
            {
                var row = new Rect(0, i * rowH, content.width, rowH - 6);
                bool wearing = WornNow(tab) == i;
                if (row.Contains(Event.current.mousePosition)) hovered = i;
                UiSkin.Rect(row, wearing ? new Color(0.92f, 0.74f, 0.36f, 0.16f) : new Color(1f, 1f, 1f, hovered == i ? 0.07f : 0.03f));

                var item = items[i];
                if (tab == Cosmetics.Kind.Cloak)
                {
                    var cloak = i == 0 ? player.SlotColor * 0.6f : item.Cloak;
                    cloak.a = 1f;
                    UiSkin.Rect(new Rect(row.x + 8, row.y + 6, 38, 28), cloak);
                    if (i > 0) UiSkin.Rect(new Rect(row.x + 8, row.y + 28, 38, 6), item.Trim);
                }
                else if (tab == Cosmetics.Kind.Pattern)
                    DrawPatternIcon(new Rect(row.x + 8, row.y + 4, 38, 32), item, i);
                else if (tab == Cosmetics.Kind.Heraldry)
                {
                    // A little shield in the colour, with a gold chevron.
                    var col = i == 0 ? player.SlotColor : item.Cloak;
                    col.a = 1f;
                    UiSkin.Rect(new Rect(row.x + 12, row.y + 5, 30, 24), col);
                    UiSkin.Rect(new Rect(row.x + 18, row.y + 29, 18, 5), col);
                    UiSkin.Rect(new Rect(row.x + 12, row.y + 15, 30, 3), UiSkin.Gold);
                }
                else
                {
                    UiSkin.Rect(new Rect(row.x + 8, row.y + 6, 38, 28), new Color(0.03f, 0.03f, 0.04f));
                    if (i > 0)
                        for (int m = 0; m < 6; m++)
                            UiSkin.Rect(new Rect(row.x + 12 + (m * 11) % 30, row.y + 9 + (m * 7) % 20, 4, 4), item.Cloak);
                }

                GUI.Label(new Rect(row.x + 58, row.y + 4, 260, 22), item.Name, new GUIStyle(UiSkin.Body) { fontStyle = FontStyle.Bold });
                bool owns = Cosmetics.Owns(tab, i), unlocked = Cosmetics.Unlocked(tab, i);
                string sub = i == 0 ? (tab == Cosmetics.Kind.Cloak ? "Each class's own colours" : tab == Cosmetics.Kind.Heraldry ? "Your party colour" : tab == Cosmetics.Kind.Pattern ? "No pattern" : "Nothing about you") : owns ? (Cosmetics.AllFree ? "Free" : "Owned")
                    : !unlocked ? "Fell " + BossName(item.NeedsBoss) + " to unlock" : item.Cost + " Renown";
                GUI.Label(new Rect(row.x + 58, row.y + 22, 300, 18), sub, new GUIStyle(UiSkin.Small) { normal = { textColor = !unlocked ? new Color(0.7f, 0.45f, 0.4f) : UiSkin.Small.normal.textColor } });

                var button = new Rect(row.xMax - 118, row.y + 6, 108, 28);
                if (wearing) GUI.Label(button, "WEARING", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = UiSkin.Gold } });
                else if (owns)
                {
                    if (GUI.Button(button, "Wear", UiSkin.Button)) Wear(tab, i);
                }
                else if (unlocked)
                {
                    GUI.enabled = Cosmetics.Renown >= item.Cost;
                    if (GUI.Button(button, "Buy", UiSkin.Button) && Cosmetics.Buy(tab, i))
                    {
                        Wear(tab, i);
                        Say("Bought " + item.Name);
                    }
                    GUI.enabled = true;
                }
            }
            GUI.EndScrollView();
            if (Event.current.type == EventType.Repaint) previewIndex = hovered;

            if (Time.time < flashUntil)
                GUI.Label(new Rect(panel.x, panel.yMax - 40, pw, 20), flash, new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = UiSkin.Gold } });
            else
                GUI.Label(new Rect(panel.x, panel.yMax - 40, pw, 20), "G or Esc to close", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
        }

        /// <summary>A swatch of the cloak you wear with the pattern's motif drawn small on it.</summary>
        private void DrawPatternIcon(Rect r, Cosmetics.Item item, int i)
        {
            var dyeItem = Cosmetics.Get(player.CloakDye.Value);
            var cloth = dyeItem.HasValue ? dyeItem.Value.Cloak : player.SlotColor * 0.6f;
            cloth.a = 1f;
            UiSkin.Rect(r, cloth);
            if (i == 0) return;
            var c = item.Cloak; c.a = 1f;
            var k = item.Trim; k.a = 1f;
            float cx = r.center.x, cy = r.center.y;
            void Skull(float x, float y, float s)
            {
                UiSkin.Rect(new Rect(x - 7 * s, y - 7 * s, 14 * s, 10 * s), c);
                UiSkin.Rect(new Rect(x - 5 * s, y + 3 * s, 10 * s, 4 * s), c);
                UiSkin.Rect(new Rect(x - 5 * s, y - 4 * s, 4 * s, 4 * s), k);
                UiSkin.Rect(new Rect(x + 1 * s, y - 4 * s, 4 * s, 4 * s), k);
            }
            switch (item.Motif)
            {
                case Cosmetics.Motif.Skull: Skull(cx, cy, 1.4f); break;
                case Cosmetics.Motif.Crossbones:
                    UiSkin.Rect(new Rect(cx - 13, cy + 7, 26, 3), c);
                    UiSkin.Rect(new Rect(cx - 13, cy + 11, 26, 3), c);
                    Skull(cx, cy - 2, 1.1f);
                    break;
                case Cosmetics.Motif.BonePrint:
                    for (int m = 0; m < 6; m++)
                        UiSkin.Rect(new Rect(r.x + 4 + (m % 3) * 11, r.y + 6 + (m / 3) * 13 + (m % 2) * 3, 8, 3), c);
                    break;
                case Cosmetics.Motif.Ribcage:
                    UiSkin.Rect(new Rect(cx - 1.5f, r.y + 4, 3, 24), c);
                    for (int m = 0; m < 4; m++) UiSkin.Rect(new Rect(cx - 12 + m, r.y + 7 + m * 5, 24 - m * 2, 2), c);
                    break;
                case Cosmetics.Motif.SkullHem:
                    for (int m = 0; m < 3; m++) Skull(r.x + 7 + m * 12, r.yMax - 8, 0.6f);
                    break;
            }
        }

        private void Wear(Cosmetics.Kind kind, int i)
        {
            if (!Cosmetics.Wear(kind, i)) return;
            if (kind == Cosmetics.Kind.Aura) player.Aura.Value = (byte)i;
            else if (kind == Cosmetics.Kind.Heraldry) player.Heraldry.Value = (byte)i;
            else if (kind == Cosmetics.Kind.Pattern) player.Pattern.Value = (byte)i;
            else player.CloakDye.Value = (byte)i;
            Sfx.Play(Sound.Purchase, player.transform.position + Vector3.up, 0.6f, 1.2f);
            Say("Now wearing " + Cosmetics.Of(kind)[i].Name);
        }

        private void Say(string text)
        {
            flash = text;
            flashUntil = Time.time + 2.5f;
        }

        private static string BossName(int depth) =>
            depth == 0 ? "the Gravewarden" : depth == 1 ? "the Tide Lich" : depth == 2 ? "the Hollow King" : "the Pale Cardinal";
    }
}
