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
    /// "one more run" moment, and where the Renown it earned is banked for the tailor.
    /// </summary>
    public class RunSummary : MonoBehaviour
    {
        private int renownEarned;

        private void OnGUI()
        {
            UiSkin.Apply();
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
            float pw = Mathf.Min(900f, w - 60f), ph = 236f + PlayerLife.All.Count * 62f;
            var panel = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.95f);

            UiSkin.Shadowed(new Rect(panel.x, panel.y + 14, pw, 50), won ? "THE DEPTHS ARE CLEARED" : "YOUR PARTY HAS FALLEN",
                new GUIStyle(UiSkin.Title) { fontSize = 38, normal = { textColor = won ? UiSkin.Gold : new Color(0.85f, 0.25f, 0.2f) } });
            GUI.Label(new Rect(panel.x, panel.y + 66, pw, 24),
                (won ? "Every floor conquered" : "Fell on floor " + floor + " of " + DungeonDirector.FloorsPerRun) + "   ·   Run time " + time,
                new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter });

            // Table.
            string[] headers = { "PLAYER", "CLASS", "KILLS", "DAMAGE", "COMBOS", "GOLD", "RELICS", "REVIVES", "DOWNED" };
            float[] cols = { 0.03f, 0.19f, 0.32f, 0.42f, 0.54f, 0.64f, 0.73f, 0.82f, 0.91f };
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
                UiSkin.Rect(new Rect(panel.x + 12, ty - 4, pw - 24, 54), new Color(np.SlotColor.r, np.SlotColor.g, np.SlotColor.b, 0.18f));
                string[] values =
                {
                    p.name + (np == NetworkPlayer.Local ? " (you)" : ""),
                    prog.Class.Name,
                    prog.Kills.Value + (prog.Kills.Value == topKills && topKills > 0 ? "  ★" : ""),
                    prog.DamageDealt.Value.ToString("N0"),
                    prog.CombosTriggered.Value.ToString(),
                    prog.GoldEarned.Value.ToString(),
                    prog.RelicCount.ToString(),
                    prog.Revives.Value.ToString(),
                    prog.Downs.Value.ToString(),
                };
                for (int c = 0; c < values.Length; c++)
                    GUI.Label(new Rect(panel.x + pw * cols[c], ty + 4, 160, 24), values[c], new GUIStyle(UiSkin.Body) { fontStyle = c == 0 ? FontStyle.Bold : FontStyle.Normal });
                GUI.Label(new Rect(panel.x + pw * cols[0], ty + 28, pw - 48, 20), BuildLine(prog), new GUIStyle(UiSkin.Small) { fontSize = 12 });
                ty += 62;
            }

            // Renown: this machine's share of the run, banked once, spent at the camp tailor.
            var me = NetworkPlayer.Local != null ? NetworkPlayer.Local.GetComponent<PlayerProgress>() : null;
            if (me != null)
            {
                int bosses = won ? DungeonDirector.FloorsPerRun : Mathf.Max(0, d.Floor.Value.y);
                int got = Cosmetics.AwardRun(d.RunStartedAt.Value, bosses, me.Kills.Value, me.CombosTriggered.Value, won, d.DifficultyIndex.Value, DungeonDirector.OmenRenown(d.RunOmen));
                if (got >= 0) renownEarned = got;
                GUI.Label(new Rect(panel.x, panel.yMax - 72, pw, 26),
                    "RENOWN  +" + renownEarned + "      (" + Cosmetics.Renown + " to spend at the tailor in camp)",
                    new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = UiSkin.Gold } });
            }

            GUI.Label(new Rect(panel.x, panel.yMax - 40, pw, 24), "Returning to camp in " + left + "...",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });
        }

        /// <summary>"Longsword +3 (Cleave) · Ossuary Plate · Hunter's Tooth · Frost Rune · Q2 E1"</summary>
        private static string BuildLine(PlayerProgress prog)
        {
            var id = prog.ClassId.Value;
            var parts = new System.Collections.Generic.List<string>();
            string weapon = Classes.ClassDefinition.WeaponName(id) + " +" + prog.WeaponLevel.Value;
            if (prog.WeaponPath.Value != 0) weapon += " (" + Classes.WeaponPaths.Name(id, prog.WeaponPath.Value) + ")";
            parts.Add(weapon);
            foreach (var slot in new[] { Loot.GearSlot.Armor, Loot.GearSlot.Charm })
                if (!prog.Equipped(slot).IsEmpty) parts.Add(Loot.Gear.BaseName(prog.Equipped(slot).Kind));
            for (int i = 0; i < 2; i++)
                if (prog.Socketed(i) != Loot.Rune.None) parts.Add(Loot.Runes.Name(prog.Socketed(i)));
            parts.Add("Q" + prog.RankQ.Value + " E" + prog.RankE.Value);
            return string.Join("   ·   ", parts);
        }
    }
}
