using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// The end-of-run screen, shown to the whole party after a wipe or a victory, while the
    /// carriage waits to take them back to camp: how far you got, how long it took, and
    /// each player's kills, damage, gold, relics, revives and times downed. It is the
    /// "one more run" moment — and, later, where cosmetic unlocks will be announced.
    /// </summary>
    public class RunSummary : MonoBehaviour
    {
        private void OnGUI()
        {
            var d = DungeonDirector.Instance;
            if (d == null || (d.Phase.Value != RunPhase.Wiped && d.Phase.Value != RunPhase.RunComplete)) return;
            float w = Screen.width, h = Screen.height;
            GUI.depth = -5;

            bool won = d.Phase.Value == RunPhase.RunComplete;
            int floor = Mathf.Max(0, d.Floor.Value.y) + 1;
            double seconds = d.RunEndedAt.Value - d.RunStartedAt.Value;
            string time = seconds > 0 ? ((int)(seconds / 60)).ToString() + "m " + ((int)(seconds % 60)).ToString("00") + "s" : "-";
            int left = Mathf.Max(0, Mathf.CeilToInt((float)(d.PhaseEndsAt.Value - NetworkManager.Singleton.ServerTime.Time)));

            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.6f));
            float pw = Mathf.Min(900f, w - 60f), ph = 200f + PlayerLife.All.Count * 46f;
            var panel = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.95f);

            UiSkin.Shadowed(new Rect(panel.x, panel.y + 14, pw, 50), won ? "THE DEPTHS ARE CLEARED" : "YOUR PARTY HAS FALLEN",
                new GUIStyle(UiSkin.Title) { fontSize = 38, normal = { textColor = won ? UiSkin.Gold : new Color(0.85f, 0.25f, 0.2f) } });
            GUI.Label(new Rect(panel.x, panel.y + 66, pw, 24),
                (won ? "Every floor conquered" : "Fell on floor " + floor + " of " + DungeonDirector.FloorsPerRun) + "   ·   Run time " + time,
                new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter });

            // Table.
            string[] headers = { "PLAYER", "CLASS", "KILLS", "DAMAGE", "GOLD", "RELICS", "REVIVES", "DOWNED" };
            float[] cols = { 0.03f, 0.2f, 0.36f, 0.47f, 0.6f, 0.7f, 0.8f, 0.9f };
            float ty = panel.y + 110;
            for (int c = 0; c < headers.Length; c++)
                GUI.Label(new Rect(panel.x + pw * cols[c], ty, 120, 20), headers[c], new GUIStyle(UiSkin.Small) { normal = { textColor = UiSkin.Gold }, fontStyle = FontStyle.Bold });
            ty += 26;

            // Highlight the party's best killer.
            int topKills = 0;
            foreach (var p in PlayerLife.All) if (p != null) topKills = Mathf.Max(topKills, p.GetComponent<PlayerProgress>().Kills.Value);

            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var prog = p.GetComponent<PlayerProgress>();
                var np = p.GetComponent<NetworkPlayer>();
                UiSkin.Rect(new Rect(panel.x + 12, ty - 4, pw - 24, 38), new Color(np.SlotColor.r, np.SlotColor.g, np.SlotColor.b, 0.18f));
                string[] values =
                {
                    p.name + (np == NetworkPlayer.Local ? " (you)" : ""),
                    prog.Class.Name,
                    prog.Kills.Value + (prog.Kills.Value == topKills && topKills > 0 ? "  ★" : ""),
                    prog.DamageDealt.Value.ToString("N0"),
                    prog.GoldEarned.Value.ToString(),
                    prog.RelicCount.ToString(),
                    prog.Revives.Value.ToString(),
                    prog.Downs.Value.ToString(),
                };
                for (int c = 0; c < values.Length; c++)
                    GUI.Label(new Rect(panel.x + pw * cols[c], ty + 4, 160, 24), values[c], new GUIStyle(UiSkin.Body) { fontStyle = c == 0 ? FontStyle.Bold : FontStyle.Normal });
                ty += 46;
            }

            GUI.Label(new Rect(panel.x, panel.yMax - 40, pw, 24), "Returning to camp in " + left + "...",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });
        }
    }
}
