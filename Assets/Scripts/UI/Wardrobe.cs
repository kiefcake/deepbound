using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    /// <summary>
    /// The camp tailor: spend Renown on cloak dyes and auras, and wear them. Entirely local
    /// — the wardrobe is saved on this machine — except what you wear, which the party sees
    /// through <see cref="NetworkPlayer.CloakDye"/> and <see cref="NetworkPlayer.Aura"/>. A
    /// mannequin at the stall wears your class in whatever you are pointing at, since you
    /// never see your own back.
    /// </summary>
    public class Wardrobe : MonoBehaviour
    {
        public static bool Open { get; private set; }
        public const float Range = 3.2f;

        private NetworkPlayer player;
        private PlayerProgress progress;
        private PlayerLife life;
        private GameObject mannequin;
        private int mannequinDye = -1, previewIndex = -1;
        private Classes.ClassId mannequinClass;
        private Cosmetics.Kind tab = Cosmetics.Kind.Cloak;
        private Vector2 scroll;
        private string flash;
        private float flashUntil, nextMote;

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
        }

        public void DebugOpen(bool open) => Open = open;

        private bool Near => DungeonDirector.InLobby && life != null && !life.Downed.Value &&
                             Vector3.Distance(player.transform.position, Lobby.TailorSpot) <= Range;

        private int WornNow(Cosmetics.Kind kind) => kind == Cosmetics.Kind.Aura ? player.Aura.Value : player.CloakDye.Value;

        private void Update()
        {
            if (player == null) return;
            var keyboard = Keyboard.current;
            bool near = Near;
            if (keyboard != null && near && keyboard.gKey.wasPressedThisFrame && !CharacterSelect.Open && !PauseMenu.Open) Open = !Open;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Open = false;
            if (keyboard != null && Open && keyboard.tabKey.wasPressedThisFrame) SwitchTab();
            if (!near) Open = false;
            if (!Open) previewIndex = -1;
            UpdateMannequin();
        }

        private void SwitchTab()
        {
            tab = tab == Cosmetics.Kind.Cloak ? Cosmetics.Kind.Aura : Cosmetics.Kind.Cloak;
            scroll = Vector2.zero;
            previewIndex = -1;
        }

        /// <summary>The dress form at the stall: your class, in the dye and aura you are hovering (or wearing).</summary>
        private void UpdateMannequin()
        {
            if (!DungeonDirector.InLobby)
            {
                if (mannequin != null) Destroy(mannequin);
                mannequin = null;
                mannequinDye = -1;
                return;
            }
            // It turns slowly on its stand, so the cloak (always on the back) gets seen.
            if (mannequin != null) mannequin.transform.Rotate(0f, 30f * Time.deltaTime, 0f);
            bool previewing = previewIndex >= 0;
            int dye = previewing && tab == Cosmetics.Kind.Cloak ? previewIndex : player.CloakDye.Value;
            int aura = previewing && tab == Cosmetics.Kind.Aura ? previewIndex : player.Aura.Value;
            var auraColor = Cosmetics.AuraColor(aura);
            if (mannequin != null && auraColor.HasValue && Time.time >= nextMote)
            {
                nextMote = Time.time + 0.09f;
                Vfx.Mote(Lobby.MannequinSpot + Vector3.up * 0.1f, auraColor.Value);
            }

            var cls = progress.ClassId.Value;
            if (mannequin != null && dye == mannequinDye && cls == mannequinClass) return;
            Quaternion? old = mannequin != null ? mannequin.transform.rotation : (Quaternion?)null;
            if (mannequin != null) Destroy(mannequin);
            mannequin = new GameObject("TailorMannequin");
            mannequin.transform.SetPositionAndRotation(Lobby.MannequinSpot, old ?? Quaternion.LookRotation(Lobby.TailorFacing));
            NetworkPlayer.BuildBody(cls, mannequin.transform, player.SlotColor, dye);
            mannequinDye = dye;
            mannequinClass = cls;
        }

        private void OnGUI()
        {
            if (player == null || !DungeonDirector.InLobby) return;
            UiSkin.Apply();
            float w = Screen.width, h = Screen.height;
            if (!Open)
            {
                if (Near && !CharacterSelect.Open)
                    UiSkin.Shadowed(new Rect(0, h / 2f + 40, w, 24), "Press G to visit the tailor   ·   Renown " + Cosmetics.Renown,
                        new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter });
                return;
            }

            GUI.depth = -3;
            var items = Cosmetics.Of(tab);
            int longest = Mathf.Max(Cosmetics.Dyes.Length, Cosmetics.Auras.Length);
            float pw = 560f, rowH = 46f, ph = Mathf.Min(h - 80f, 186f + longest * rowH);
            var panel = new Rect(w - pw - 30f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.95f);
            UiSkin.Shadowed(new Rect(panel.x, panel.y + 10, pw, 40), "The Tailor", new GUIStyle(UiSkin.Title) { fontSize = 30 });
            GUI.Label(new Rect(panel.x, panel.y + 50, pw, 22), "RENOWN  " + Cosmetics.Renown + "      ·      earned at the end of every run",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = UiSkin.Gold } });

            // Tabs.
            string[] names = { "CLOAKS", "AURAS" };
            for (int t = 0; t < 2; t++)
            {
                var r = new Rect(panel.x + pw / 2f - 170 + t * 175, panel.y + 76, 165, 30);
                bool on = (int)tab == t;
                if (GUI.Button(r, names[t], new GUIStyle(UiSkin.Button) { fontStyle = on ? FontStyle.Bold : FontStyle.Normal, normal = { textColor = on ? UiSkin.Gold : new Color(0.7f, 0.67f, 0.62f) } }) && !on) SwitchTab();
            }
            GUI.Label(new Rect(panel.x, panel.y + 108, pw, 20), "Your party sees what you wear. The mannequin shows what you point at.   (Tab switches)",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });

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
                    // Swatch: the cloak, a trim stripe along its hem.
                    var cloak = i == 0 ? player.SlotColor * 0.6f : item.Cloak;
                    cloak.a = 1f;
                    UiSkin.Rect(new Rect(row.x + 8, row.y + 6, 38, 28), cloak);
                    if (i > 0) UiSkin.Rect(new Rect(row.x + 8, row.y + 28, 38, 6), item.Trim);
                }
                else
                {
                    // Swatch: a scatter of motes on black.
                    UiSkin.Rect(new Rect(row.x + 8, row.y + 6, 38, 28), new Color(0.03f, 0.03f, 0.04f));
                    if (i > 0)
                        for (int m = 0; m < 6; m++)
                            UiSkin.Rect(new Rect(row.x + 12 + (m * 11) % 30, row.y + 9 + (m * 7) % 20, 4, 4), item.Cloak);
                }

                GUI.Label(new Rect(row.x + 58, row.y + 4, 260, 22), item.Name, new GUIStyle(UiSkin.Body) { fontStyle = FontStyle.Bold });
                bool owns = Cosmetics.Owns(tab, i), unlocked = Cosmetics.Unlocked(tab, i);
                string sub = i == 0 ? (tab == Cosmetics.Kind.Cloak ? "Each class's own colours" : "Nothing about you") : owns ? "Owned"
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

        private void Wear(Cosmetics.Kind kind, int i)
        {
            if (!Cosmetics.Wear(kind, i)) return;
            if (kind == Cosmetics.Kind.Aura) player.Aura.Value = (byte)i;
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
