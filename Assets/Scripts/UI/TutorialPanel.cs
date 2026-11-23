using Deepbound.World;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// The Proving Path's lesson line (2026-11-23): one short instruction at the top of the screen, naming the player's own
    /// keys, and "Go through the gate" once the lesson is met. The tutorial is opt-in, so it may say what the dungeon
    /// itself never does. Drawn only on the Proving Path.
    /// </summary>
    public class TutorialPanel : MonoBehaviour
    {
        private static float metAt = -10f, beganAt = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("TutorialPanel");
            DontDestroyOnLoad(go);
            go.AddComponent<TutorialPanel>();
        }

        public static void Met() => metAt = Time.time;
        public static void Began() => beganAt = Time.time;

        private static string K(Bind b) => Binds.Label(b);

        public static string Title(DungeonDirector.Lesson l) => l switch
        {
            DungeonDirector.Lesson.Move => "MOVEMENT",
            DungeonDirector.Lesson.Attack => "ATTACKS",
            DungeonDirector.Lesson.Dodge => "DODGING",
            DungeonDirector.Lesson.Abilities => "ABILITIES",
            DungeonDirector.Lesson.Telegraphs => "WARNINGS",
            DungeonDirector.Lesson.Shields => "SHIELDS",
            DungeonDirector.Lesson.Potions => "AFFLICTIONS AND POTIONS",
            DungeonDirector.Lesson.Loot => "LOOT",
            DungeonDirector.Lesson.Ultimate => "ULTIMATE",
            _ => "THE GUARDIAN",
        };

        public static string Line(DungeonDirector.Lesson l) => l switch
        {
            DungeonDirector.Lesson.Move => K(Bind.MoveForward) + " " + K(Bind.MoveLeft) + " " + K(Bind.MoveBack) + " " + K(Bind.MoveRight) + " to move. Hold "
                + K(Bind.Sprint) + " to sprint and press " + K(Bind.Jump) + " to jump.",
            DungeonDirector.Lesson.Attack => "Left mouse is your attack. Right mouse is your class's second attack. Strike the dummies.",
            DungeonDirector.Lesson.Dodge => "Press " + K(Bind.Dodge) + " to dodge. Nothing can hurt you mid-dodge. Dodge twice and defeat the skeleton.",
            DungeonDirector.Lesson.Abilities => K(Bind.AbilityQ) + " and " + K(Bind.AbilityE) + " are your class abilities. Use them on this group.",
            DungeonDirector.Lesson.Telegraphs => "A red mark on the floor shows where a heavy blow will land. Step out of it.",
            DungeonDirector.Lesson.Shields => "A shield stops blows from the front. Press " + K(Bind.Kick) + " to kick the guard open, or strike from the side.",
            DungeonDirector.Lesson.Potions => "Afflictions show around the edge of your view. Press " + K(Bind.Potion1) + " to drink a health potion.",
            DungeonDirector.Lesson.Loot => "Press " + K(Bind.Use) + " to open the chest. Press " + K(Bind.Inventory) + " to see your pack.",
            DungeonDirector.Lesson.Ultimate => "Your ultimate is charged. Press " + K(Bind.Ultimate) + ". It charges as you fight.",
            _ => "Watch for its tells and keep moving.",
        };

        private void OnGUI()
        {
            UiSkin.Apply();
            if (!DungeonDirector.InTutorial || DungeonDirector.Instance == null) return;
            GUI.depth = 8;
            UiScale.Begin();
            float w = UiScale.W;
            var d = DungeonDirector.Instance;
            var lesson = (DungeonDirector.Lesson)d.TutStep.Value;
            bool done = d.TutDone.Value;
            float since = Time.time - (done ? metAt : beganAt);
            // The guardian's own bar and name take the top of the screen once the fight is on.
            if (lesson == DungeonDirector.Lesson.Guardian && !done && since > 6f) return;
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(since / 0.35f));
            float flash = done ? Mathf.Clamp01(1f - since / 1.2f) : 0f;

            float bw = Mathf.Min(720f, w - 40f), bh = 66f;
            var r = new Rect((w - bw) / 2f, 18f - (1f - slide) * 12f, bw, bh);
            UiSkin.Rect(r, new Color(0.04f, 0.035f, 0.03f, 0.72f * Mathf.Max(slide, 0.4f)));
            UiSkin.Rect(new Rect(r.x, r.yMax - 2f, r.width, 2f), Color.Lerp(new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, 0.45f), Color.white, flash));

            int n = (int)lesson + 1;
            string head = "LESSON " + n + " OF " + DungeonLayout.TutorialRooms + "  ·  " + Title(lesson);
            var hs = UiSkin.Derive(UiSkin.Small, UiSkin.Gold, 12, FontStyle.Bold, TextAnchor.UpperCenter, false);
            GUI.Label(new Rect(r.x + 12, r.y + 8, r.width - 24, 18), head, hs);
            string line = done ? (lesson == DungeonDirector.Lesson.Guardian ? "Proven." : "Done. Go through the gate.") : Line(lesson);
            var ls = UiSkin.Derive(UiSkin.Body, done ? Color.Lerp(new Color(0.72f, 0.95f, 0.6f), Color.white, flash) : new Color(0.93f, 0.9f, 0.85f), 15, FontStyle.Normal, TextAnchor.UpperCenter, true);
            GUI.Label(new Rect(r.x + 16, r.y + 28, r.width - 32, 36), line, ls);
        }
    }
}
