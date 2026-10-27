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
        private static double recordedRun = -1;

        private void OnGUI()
        {
            useGUILayout = false; // no GUILayout here: skip the layout pass (perf)
            UiSkin.Apply();
            if (DebugEpilogueAt >= 0f) { GUI.depth = -6; DrawEpilogue(Screen.width, Screen.height, DebugEpilogueAt); return; }
            var d = DungeonDirector.Instance;
            if (d == null || (d.Phase.Value != RunPhase.Wiped && d.Phase.Value != RunPhase.RunComplete)) return;
            float w = Screen.width, h = Screen.height;
            GUI.depth = -5;

            bool won = d.Phase.Value == RunPhase.RunComplete;
            // A victory opens on the epilogue: the Last Morning, over black, before the table.
            if (won && DrawEpilogue(Screen.width, Screen.height, (float)(NetworkManager.Singleton.ServerTime.Time - d.RunEndedAt.Value))) return;
            int floor = Mathf.Max(0, d.Floor.Value.y) + 1;
            double seconds = d.RunEndedAt.Value - d.RunStartedAt.Value;
            string time = seconds > 0 ? ((int)(seconds / 60)).ToString() + "m " + ((int)(seconds % 60)).ToString("00") + "s" : "-";
            int left = Mathf.Max(0, Mathf.CeilToInt((float)(d.PhaseEndsAt.Value - NetworkManager.Singleton.ServerTime.Time)));

            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.6f));
            float pw = Mathf.Min(900f, w - 60f), ph = 290f + PlayerLife.All.Count * 62f;
            // Remember how it ended (once a run): the camp will mention it.
            if (recordedRun != d.RunStartedAt.Value)
            {
                recordedRun = d.RunStartedAt.Value;
                if (won) Deeds.AddWin(); else Deeds.AddWipe();
            }
            var panel = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f, pw, ph);
            UiSkin.Panel(panel, 0.95f);

            UiSkin.Shadowed(new Rect(panel.x, panel.y + 14, pw, 50), won ? "THE DEPTHS ARE CLEARED" : "YOUR PARTY HAS FALLEN",
                new GUIStyle(UiSkin.Title) { fontSize = 38, normal = { textColor = won ? UiSkin.Gold : new Color(0.85f, 0.25f, 0.2f) } });
            GUI.Label(new Rect(panel.x, panel.y + 66, pw, 24),
                (won ? "All " + DungeonDirector.FloorsPerRun + " floors conquered, the Unsung silenced"
                    : "Fell on floor " + floor + " of " + DungeonDirector.FloorsPerRun + (floor - 1 >= DungeonDirector.AbyssDepth ? ", in the Abyss" : "")) + "   ·   Run time " + time,
                new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter });

            // How long each floor took (as this machine saw the guardians fall).
            if (GameHud.FloorTimes.Count > 0)
            {
                var parts = new System.Collections.Generic.List<string>();
                for (int i = 0; i < GameHud.FloorTimes.Count; i++) parts.Add((i + 1) + "  " + Records.Clock(GameHud.FloorTimes[i]));
                GUI.Label(new Rect(panel.x, panel.y + 88, pw, 20), "Floors:   " + string.Join("   ·   ", parts),
                    new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.76f, 0.68f) } });
            }

            // Table.
            string[] headers = { "PLAYER", "CLASS", "KILLS", "DAMAGE", "COMBOS", "GOLD", "RELICS", "REVIVES", "DOWNED" };
            float[] cols = { 0.03f, 0.19f, 0.32f, 0.42f, 0.54f, 0.64f, 0.73f, 0.82f, 0.91f };
            float ty = panel.y + 128;
            for (int c = 0; c < headers.Length; c++)
                GUI.Label(new Rect(panel.x + pw * cols[c], ty, 120, 20), headers[c], new GUIStyle(UiSkin.Small) { normal = { textColor = UiSkin.Gold }, fontStyle = FontStyle.Bold });
            ty += 26;

            // Highlight the party's best killer.
            int topKills = 0;
            foreach (var p in PlayerLife.All) if (p != null) topKills = Mathf.Max(topKills, p.GetComponent<PlayerProgress>().Kills.Value);

            var titles = Titles();
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var prog = p.GetComponent<PlayerProgress>();
                var np = p.GetComponent<NetworkPlayer>();
                UiSkin.Rect(new Rect(panel.x + 12, ty - 4, pw - 24, 54), new Color(np.SlotColor.r, np.SlotColor.g, np.SlotColor.b, 0.18f));
                string[] values =
                {
                    (np != null ? np.NameWithTitle : p.name) + (np == NetworkPlayer.Local ? " (you)" : ""),
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
                string title = titles.TryGetValue(prog, out var t) ? t : "";
                GUI.Label(new Rect(panel.x + pw * cols[0], ty + 28, pw - 48, 20), "<color=#E8BC5C><b>" + title + "</b></color>     " + BuildLine(prog),
                    new GUIStyle(UiSkin.Small) { fontSize = 12, richText = true });
                ty += 62;
            }

            // Renown: this machine's share of the run, banked once, spent at the camp tailor.
            var me = NetworkPlayer.Local != null ? NetworkPlayer.Local.GetComponent<PlayerProgress>() : null;
            if (me != null)
            {
                // Renown counts the act bosses (a Warden is half of one): the run is twice as long as it was.
                int bosses = ((won ? DungeonDirector.FloorsPerRun : Mathf.Max(0, d.Floor.Value.y)) + 1) / 2;
                int got = Cosmetics.AwardRun(d.RunStartedAt.Value, bosses, me.Kills.Value, me.CombosTriggered.Value, won, d.DifficultyIndex.Value, DungeonDirector.OmenRenown(d.RunOmen), me.Gold.Value);
                if (got >= 0) renownEarned = got;
                // The Hall of Records: anything this run beat is carved at camp (once per run).
                foreach (var line in Records.SubmitRun(d.RunStartedAt.Value, floor, me.Kills.Value, me.GoldEarned.Value, won, d.DailyRun.Value ? d.DailyDate.Value : 0))
                    GameHud.Feed("NEW RECORD - " + line, new Color(1f, 0.85f, 0.4f));
                GUI.Label(new Rect(panel.x + 30, panel.yMax - 104, pw - 60, 28), Epitaph(me, won, floor),
                    new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, wordWrap = true, normal = { textColor = new Color(0.85f, 0.8f, 0.72f) } });
                GUI.Label(new Rect(panel.x, panel.yMax - 72, pw, 26),
                    "RENOWN  +" + renownEarned + (Cosmetics.TitheFor(me.Gold.Value) > 0 ? "  (+" + Cosmetics.TitheFor(me.Gold.Value) + " tithed from " + me.Gold.Value + " gold left)" : "") + "      (" + Cosmetics.Renown + " to spend at the tailor)",
                    new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = UiSkin.Gold } });
            }

            GUI.Label(new Rect(panel.x, panel.yMax - 40, pw, 24), "Returning to camp in " + left + "...",
                new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });
        }

        /// <summary>The camp's verdict on your run, in one line, from the most notable thing you did.</summary>
        /// <summary>The ending (2026-10-20): the lore's open thread answered. The party brought the Last Morning.</summary>
        public static readonly string[] EpilogueLines =
        {
            "The Unsung falls, and for the first time in four hundred years there is no song beneath Cathedra.",
            "In the Ossuary the restless bones lie still. In the drowned vaults the water runs clear.",
            "On the Empty Throne the Hollow King lowers his head, and does not lift it again.",
            "Up the shaft, past the grave lift's chains, a grey light is climbing the walls of Ravenhold.",
            "It is morning. The Last Morning. You brought it.",
        };

        public const float EpilogueSeconds = 14f;

        /// <summary>For the screenshot run: draw the epilogue at this time (seconds in), -1 off.</summary>
        public static float DebugEpilogueAt = -1f;

        /// <summary>True while the epilogue holds the screen.</summary>
        private static bool DrawEpilogue(float w, float h, float t)
        {
            if (t < 0f || t > EpilogueSeconds) return false;
            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, Mathf.Clamp01(t * 1.5f)));
            // Dawn: a pale band rising at the bottom as the lines go by.
            float dawn = Mathf.Clamp01((t - 7f) / 6f);
            // Soft, in strips that fade upward, warmest at the horizon.
            const int strips = 24;
            float band = h * 0.45f * dawn;
            for (int i = 0; i < strips; i++)
            {
                float k = (i + 1f) / strips;
                UiSkin.Rect(new Rect(0, h - band * k, w, band / strips + 1f), new Color(0.62f, 0.52f, 0.4f, 0.22f * dawn * (1f - k) * (1f - k)));
            }
            var style = new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 20, fontStyle = FontStyle.Italic };
            float y = h * 0.28f;
            for (int i = 0; i < EpilogueLines.Length; i++)
            {
                float a = Mathf.Clamp01((t - 0.8f - i * 1.9f) / 1.2f);
                if (a <= 0f) break;
                bool last = i == EpilogueLines.Length - 1;
                var c = last ? new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, a) : new Color(0.88f, 0.84f, 0.76f, a);
                GUI.Label(new Rect(w * 0.15f, y + i * 44f, w * 0.7f, 40), EpilogueLines[i], new GUIStyle(style) { normal = { textColor = c }, fontStyle = last ? FontStyle.BoldAndItalic : FontStyle.Italic });
            }
            float title = Mathf.Clamp01((t - 11f) / 1.5f);
            if (title > 0f)
            {
                UiSkin.Shadowed(new Rect(0, h * 0.72f, w, 60), "DEEPBOUND", new GUIStyle(UiSkin.Title) { fontSize = 46, normal = { textColor = new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, title) } });
                GUI.Label(new Rect(0, h * 0.72f + 62, w, 24), "Thank you for playing.", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 15, normal = { textColor = new Color(0.85f, 0.8f, 0.72f, title) } });
            }
            return true;
        }

        private static string Epitaph(PlayerProgress me, bool won, int floor)
        {
            string name = me.name;
            var d = DungeonDirector.Instance;
            if (won && me.Downs.Value == 0) return "Here walks " + name + ", who went to the bottom of Cathedra and never once knelt.";
            if (won && d != null && d.SealCount >= DungeonDirector.SealTotal) return "Here walks " + name + ", who broke every Seal and sang the Unsung to its end.";
            if (won && d != null && d.Floor.Value.y >= DungeonDirector.AbyssDepth) return "Here walks " + name + ", who went below the hymn and sang the Unsung to its end.";
            if (won) return "Here walks " + name + ", who brought the Last Lantern home from the bottom of the world.";
            if (me.CatPeed.Value) return "Here lies " + name + ", who picked up the cat without asking. The smell outlived them.";
            if (me.AbyssKicks.Value >= 3) return "Here lies " + name + ", who kicked " + me.AbyssKicks.Value + " of the dead into the dark. The dark kept count.";
            if (me.Revives.Value >= 3) return "Here lies " + name + ", who lifted their friends " + me.Revives.Value + " times, and had no one left to lift them.";
            if (me.Kills.Value >= 60) return "Here lies " + name + ", who made the Ossuary " + me.Kills.Value + " bones fuller.";
            if (me.Downs.Value >= 4) return "Here lies " + name + ", who knelt " + me.Downs.Value + " times and got up " + (me.Downs.Value - 1) + ".";
            if (me.GoldEarned.Value >= 500) return "Here lies " + name + ", with " + me.GoldEarned.Value + " gold in their pockets. The dead are richer for it.";
            if (floor <= 1) return "Here lies " + name + ", who did not get far, but got further than the Baron let most.";
            return "Here lies " + name + ", who went down to floor " + floor + " and is still there, in a way.";
        }

        /// <summary>
        /// One title each, for what they did most: the party's butcher, its guardian angel,
        /// its pincushion. A player who tops nothing still gets an honest one.
        /// </summary>
        private static System.Collections.Generic.Dictionary<PlayerProgress, string> Titles()
        {
            var players = new System.Collections.Generic.List<PlayerProgress>();
            foreach (var p in PlayerLife.All) if (p != null) players.Add(p.GetComponent<PlayerProgress>());
            var titles = new System.Collections.Generic.Dictionary<PlayerProgress, string>();
            var categories = new (string title, System.Func<PlayerProgress, int> stat)[]
            {
                ("THE BUTCHER", p => p.Kills.Value),
                ("CHASM ENTHUSIAST", p => p.AbyssKicks.Value),
                ("GUARDIAN ANGEL", p => p.Revives.Value),
                ("BONEBREAKER", p => p.DamageDealt.Value),
                ("MAESTRO OF COMBOS", p => p.CombosTriggered.Value),
                ("LUCKY DEVIL", p => p.Crits.Value),
                ("PINCUSHION", p => p.DamageTaken.Value),
                ("PROFESSIONAL CORPSE", p => p.Downs.Value),
            };
            foreach (var (title, stat) in categories)
            {
                PlayerProgress best = null;
                int bestValue = 0;
                foreach (var p in players)
                {
                    if (titles.ContainsKey(p)) continue;
                    int v = stat(p);
                    if (v > bestValue) { bestValue = v; best = p; }
                }
                if (best != null) titles[best] = title;
            }
            foreach (var p in players) if (!titles.ContainsKey(p)) titles[p] = "THE SURVIVOR";
            return titles;
        }

        /// <summary>"Longsword +3 (Cleave) · Ossuary Plate · Hunter's Tooth · Frost Rune · Q2 E1"</summary>
        private static string BuildLine(PlayerProgress prog)
        {
            var id = prog.ClassId.Value;
            var parts = new System.Collections.Generic.List<string>();
            string weapon = Classes.ClassDefinition.WeaponName(id) + " +" + prog.WeaponLevel.Value;
            if (prog.WeaponPath.Value != 0) weapon += " (" + Classes.WeaponPaths.Name(id, prog.WeaponPath.Value) + ")";
            parts.Add(weapon);
            for (int e = 0; e < Edges.Slots; e++)
                if (prog.EdgeAt(e) != Edge.None) parts.Add(Edges.Name(prog.EdgeAt(e)));
            foreach (var slot in new[] { Loot.GearSlot.Armor, Loot.GearSlot.Charm })
                if (!prog.Equipped(slot).IsEmpty) parts.Add(Loot.Gear.BaseName(prog.Equipped(slot).Kind));
            for (int i = 0; i < Loot.Runes.MaxSockets; i++)
                if (prog.Socketed(i) != Loot.Rune.None) parts.Add(Loot.Runes.Name(prog.Socketed(i)));
            parts.Add("Q" + prog.RankQ.Value + " E" + prog.RankE.Value);
            return string.Join("   ·   ", parts);
        }
    }
}
