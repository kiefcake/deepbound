using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    /// <summary>
    /// The camp tailor: spend Renown on cloak dyes and wear them. Entirely local — the
    /// wardrobe is saved on this machine — except the dye you wear, which the party sees
    /// through <see cref="NetworkPlayer.CloakDye"/>. A mannequin at the stall wears your
    /// class with whatever dye you are looking at, since you never see your own back.
    /// </summary>
    public class Wardrobe : MonoBehaviour
    {
        public static bool Open { get; private set; }
        public const float Range = 3.2f;

        private NetworkPlayer player;
        private PlayerProgress progress;
        private PlayerLife life;
        private GameObject mannequin;
        private int mannequinDye = -1, previewDye = -1;
        private Classes.ClassId mannequinClass;
        private Vector2 scroll;
        private string flash;
        private float flashUntil;

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

        private void Update()
        {
            if (player == null) return;
            var keyboard = Keyboard.current;
            bool near = Near;
            if (keyboard != null && near && keyboard.gKey.wasPressedThisFrame && !CharacterSelect.Open && !PauseMenu.Open) Open = !Open;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Open = false;
            if (!near) Open = false;
            if (!Open) previewDye = -1;
            UpdateMannequin();
        }

        /// <summary>The dress form at the stall: your class, in the dye you are hovering (or wearing).</summary>
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
            int dye = previewDye >= 0 ? previewDye : player.CloakDye.Value;
            var cls = progress.ClassId.Value;
            if (mannequin != null && dye == mannequinDye && cls == mannequinClass) return;
            Quaternion? mannequinOld = mannequin != null ? mannequin.transform.rotation : (Quaternion?)null;
            if (mannequin != null) Destroy(mannequin);
            mannequin = new GameObject("TailorMannequin");
            var facing = Quaternion.LookRotation(Lobby.TailorFacing);
            if (mannequinOld.HasValue) facing = mannequinOld.Value;
            mannequin.transform.SetPositionAndRotation(Lobby.MannequinSpot, facing);
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
            var dyes = Cosmetics.Dyes;
            float pw = 560f, rowH = 46f, ph = Mathf.Min(h - 80f, 150f + dyes.Length * rowH);
            var panel = new Rect(w - pw - 30f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.95f);
            UiSkin.Shadowed(new Rect(panel.x, panel.y + 10, pw, 40), "The Tailor", new GUIStyle(UiSkin.Title) { fontSize = 30 });
            GUI.Label(new Rect(panel.x, panel.y + 50, pw, 22), "RENOWN  " + Cosmetics.Renown + "      ·      earned at the end of every run",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = UiSkin.Gold } });
            GUI.Label(new Rect(panel.x, panel.y + 70, pw, 20), "Your party sees your cloak. The mannequin wears what you point at.",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });

            var view = new Rect(panel.x + 16, panel.y + 98, pw - 32, ph - 140);
            var content = new Rect(0, 0, view.width - 20, dyes.Length * rowH);
            int hovered = -1;
            scroll = GUI.BeginScrollView(view, scroll, content);
            for (int i = 0; i < dyes.Length; i++)
            {
                var row = new Rect(0, i * rowH, content.width, rowH - 6);
                bool wearing = player.CloakDye.Value == i;
                if (row.Contains(Event.current.mousePosition)) hovered = i;
                UiSkin.Rect(row, wearing ? new Color(0.92f, 0.74f, 0.36f, 0.16f) : new Color(1f, 1f, 1f, hovered == i ? 0.07f : 0.03f));

                // Swatch: the cloak, a trim stripe along its hem.
                var dye = dyes[i];
                var cloak = i == 0 ? player.SlotColor * 0.6f : dye.Cloak;
                cloak.a = 1f;
                UiSkin.Rect(new Rect(row.x + 8, row.y + 6, 38, 28), cloak);
                if (i > 0) UiSkin.Rect(new Rect(row.x + 8, row.y + 28, 38, 6), dye.Trim);

                GUI.Label(new Rect(row.x + 58, row.y + 4, 260, 22), dye.Name, new GUIStyle(UiSkin.Body) { fontStyle = FontStyle.Bold });
                bool owns = Cosmetics.Owns(i), unlocked = Cosmetics.Unlocked(i);
                string sub = i == 0 ? "Each class's own colours" : owns ? "Owned" : !unlocked ? "Fell " + BossName(dye.NeedsBoss) + " to unlock" : dye.Cost + " Renown";
                GUI.Label(new Rect(row.x + 58, row.y + 22, 300, 18), sub, new GUIStyle(UiSkin.Small) { normal = { textColor = !unlocked ? new Color(0.7f, 0.45f, 0.4f) : UiSkin.Small.normal.textColor } });

                var button = new Rect(row.xMax - 118, row.y + 6, 108, 28);
                if (wearing) GUI.Label(button, "WEARING", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = UiSkin.Gold } });
                else if (owns)
                {
                    if (GUI.Button(button, "Wear", UiSkin.Button)) Wear(i);
                }
                else if (unlocked)
                {
                    GUI.enabled = Cosmetics.Renown >= dye.Cost;
                    if (GUI.Button(button, "Buy", UiSkin.Button))
                    {
                        if (Cosmetics.Buy(i)) { Wear(i); Say("Bought " + dye.Name); }
                    }
                    GUI.enabled = true;
                }
            }
            GUI.EndScrollView();
            if (Event.current.type == EventType.Repaint) previewDye = hovered;

            if (Time.time < flashUntil)
                GUI.Label(new Rect(panel.x, panel.yMax - 40, pw, 20), flash, new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = UiSkin.Gold } });
            else
                GUI.Label(new Rect(panel.x, panel.yMax - 40, pw, 20), "G or Esc to close", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
        }

        private void Wear(int dye)
        {
            if (!Cosmetics.Equip(dye)) return;
            player.CloakDye.Value = (byte)dye;
            Sfx.Play(Sound.Purchase, player.transform.position + Vector3.up, 0.6f, 1.2f);
            Say("Now wearing " + Cosmetics.Dyes[dye].Name);
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
