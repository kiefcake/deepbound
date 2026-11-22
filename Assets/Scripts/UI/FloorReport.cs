using System.Collections.Generic;
using Deepbound.Player;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// FLOOR REPORT (2026-11-22, "keep grinding"): when a floor's guardian falls, a small panel on the right lists what
    /// each player did on that floor alone - damage dealt, kills, combos, damage taken and downs - with a star on the
    /// floor's top damage. Built from the run statistics on <see cref="PlayerProgress"/> (synced to everyone), snapshotted
    /// as the floor begins, so every peer shows the same table. Stays up for <see cref="ShowSeconds"/>.
    /// </summary>
    public static class FloorReport
    {
        public const float ShowSeconds = 14f;
        private struct Snap { public int Kills, Damage, Combos, Taken, Downs; }
        public struct Row { public string Name; public string Class; public Color Color; public int Kills, Damage, Combos, Taken, Downs; public bool You; }

        private static readonly Dictionary<ulong, Snap> start = new Dictionary<ulong, Snap>();
        public static readonly List<Row> Rows = new List<Row>();
        private static float shownAt = -99f;
        private static int floorNumber;

        private static Snap Read(PlayerProgress p) => new Snap { Kills = p.Kills.Value, Damage = p.DamageDealt.Value, Combos = p.CombosTriggered.Value, Taken = p.DamageTaken.Value, Downs = p.Downs.Value };

        /// <summary>A floor began: remember where everyone's tallies stood.</summary>
        public static void Begin()
        {
            start.Clear();
            foreach (var l in PlayerLife.All)
                if (l != null && l.TryGetComponent<PlayerProgress>(out var p)) start[p.NetworkObjectId] = Read(p);
        }

        /// <summary>The guardian fell: build the table and show it.</summary>
        public static void Finish(int floor)
        {
            Rows.Clear();
            foreach (var l in PlayerLife.All)
            {
                if (l == null || !l.TryGetComponent<PlayerProgress>(out var p)) continue;
                var now = Read(p);
                start.TryGetValue(p.NetworkObjectId, out var was); // a player who joined mid-floor counts from zero
                var np = l.GetComponent<NetworkPlayer>();
                Rows.Add(new Row
                {
                    Name = np != null ? np.name : l.name, Class = p.Class.Name, Color = np != null ? np.SlotColor : Color.white, You = np == NetworkPlayer.Local,
                    Kills = now.Kills - was.Kills, Damage = now.Damage - was.Damage, Combos = now.Combos - was.Combos, Taken = now.Taken - was.Taken, Downs = now.Downs - was.Downs,
                });
            }
            Rows.Sort((a, b) => b.Damage.CompareTo(a.Damage));
            floorNumber = floor;
            shownAt = Time.time;
        }

        public static bool Showing => Rows.Count > 0 && Time.time - shownAt < ShowSeconds;

        public static void Draw(float w, float h)
        {
            if (!Showing) return;
            float k = Time.time - shownAt;
            float a = Mathf.Clamp01(k * 3f) * Mathf.Clamp01((ShowSeconds - k) * 1.5f);
            float pw = 430f, rowH = 26f, ph = 64f + Rows.Count * rowH;
            var r = new Rect(w - pw - 24f + (1f - Mathf.Clamp01(k * 4f)) * 40f, h * 0.3f, pw, ph);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            UiSkin.Panel(r, 0.9f);
            UiSkin.Rect(new Rect(r.x, r.y, r.width, 3), UiSkin.Gold);
            GUI.Label(new Rect(r.x + 14, r.y + 8, pw - 28, 24), "FLOOR " + floorNumber + " REPORT", new GUIStyle(UiSkin.Heading) { fontSize = 17 });
            var head = new GUIStyle(UiSkin.Small) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1f, 1f, 1f, 0.55f) } };
            float[] cols = { 190f, 260f, 310f, 360f, 410f };
            string[] heads = { "DAMAGE", "KILLS", "COMBOS", "TAKEN", "DOWNS" };
            for (int c = 0; c < cols.Length; c++) GUI.Label(new Rect(r.x + cols[c] - 70f, r.y + 34, 70f, 18), heads[c], head);
            float y = r.y + 54;
            var cell = new GUIStyle(UiSkin.Small) { fontSize = 13, alignment = TextAnchor.MiddleRight };
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                UiSkin.Rect(new Rect(r.x + 8, y, pw - 16, rowH - 3), new Color(row.Color.r, row.Color.g, row.Color.b, row.You ? 0.22f : 0.1f));
                var nameSt = new GUIStyle(UiSkin.Small) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = false };
                string name = (i == 0 && Rows.Count > 1 && row.Damage > 0 ? "★ " : "") + row.Name;
                GUI.Label(new Rect(r.x + 14, y + 2, 104, 20), UiSkin.Fit(name, nameSt, 104), nameSt);
                int[] vals = { row.Damage, row.Kills, row.Combos, row.Taken, row.Downs };
                for (int c = 0; c < cols.Length; c++)
                {
                    var st = new GUIStyle(cell) { normal = { textColor = c == 4 && vals[c] > 0 ? new Color(1f, 0.5f, 0.45f) : Color.white } };
                    GUI.Label(new Rect(r.x + cols[c] - 70f, y + 2, 70f, 20), vals[c].ToString("N0"), st);
                }
                y += rowH;
            }
            GUI.color = old;
        }
    }
}
