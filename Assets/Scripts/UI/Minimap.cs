using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    /// <summary>
    /// The minimap: north-up, centred on you, drawn from the floor's layout data (so it
    /// costs no second camera).
    ///
    /// <para>
    /// Rooms appear only once someone in the party has stood in them — the map is a record
    /// of where you have been, which keeps exploration a decision rather than a solved
    /// maze. The boss room is marked from the start, faintly, so the party always knows
    /// which way the floor ends. Enemies show only within radar range; teammates always.
    /// </para>
    /// M toggles a large overview.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        private const float RadarRange = 22f;

        private NetworkPlayer player;
        private readonly HashSet<int> explored = new HashSet<int>();
        private int arenaSeen = -1;
        private bool large;

        /// <summary>Large overview on or off (M; the screenshot run also sets it).</summary>
        public bool Large { get => large; set => large = value; }

        public void Init(NetworkPlayer owner) => player = owner;

        private void Update()
        {
            var layout = DungeonDirector.Layout;
            if (layout == null) return;

            if (arenaSeen != DungeonDirector.ArenaId)
            {
                arenaSeen = DungeonDirector.ArenaId;
                explored.Clear();
                explored.Add(layout.StartRoom.Id);
            }

            // Any party member standing in a room reveals it for everyone's map.
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var room = layout.RoomAt(p.transform.position, 1f);
                if (room != null) explored.Add(room.Id);
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame) large = !large;
        }

        private void OnGUI()
        {
            UiSkin.Apply();
            var layout = DungeonDirector.Layout;
            if (layout == null || player == null || CharacterSelect.Open) return;

            float w = Screen.width, h = Screen.height;
            float size = large ? Mathf.Min(w, h) * 0.8f : 210f;
            var frame = large ? new Rect(w / 2f - size / 2f, h / 2f - size / 2f, size, size) : new Rect(w - size - 18f, 18f, size, size);
            // Metres covered by the map's width.
            float span = large ? MapSpan(layout) : 60f;
            float scale = size / span;
            Vector3 centre = large ? MapCentre(layout) : player.transform.position;

            UiSkin.Panel(frame, large ? 0.92f : 0.75f);
            GUI.BeginGroup(frame);
            Vector2 ToMap(Vector3 world) => new Vector2(size / 2f + (world.x - centre.x) * scale, size / 2f - (world.z - centre.z) * scale);

            // Corridors first, then rooms over them.
            foreach (var link in layout.Links)
            {
                var a = layout.Rooms[link.A];
                var b = layout.Rooms[link.B];
                if (!explored.Contains(a.Id) && !explored.Contains(b.Id)) continue;
                var pa = ToMap(a.Center);
                var pb = ToMap(b.Center);
                float thick = Mathf.Max(3f, DungeonLayout.CorridorWidth * scale);
                var min = Vector2.Min(pa, pb);
                var max = Vector2.Max(pa, pb);
                var r = Mathf.Abs(pa.x - pb.x) > Mathf.Abs(pa.y - pb.y)
                    ? new Rect(min.x, pa.y - thick / 2f, max.x - min.x, thick)
                    : new Rect(pa.x - thick / 2f, min.y, thick, max.y - min.y);
                UiSkin.Rect(r, new Color(0.35f, 0.32f, 0.28f, 0.9f));
            }

            foreach (var room in layout.Rooms)
            {
                bool seen = explored.Contains(room.Id);
                bool boss = room.Kind == RoomKind.Boss;
                if (!seen && !boss) continue;

                var c = ToMap(room.Center);
                var r = new Rect(c.x - room.Size.x * scale / 2f, c.y - room.Size.y * scale / 2f, room.Size.x * scale, room.Size.y * scale);
                Color fill = boss ? new Color(0.45f, 0.08f, 0.08f, seen ? 0.95f : 0.4f)
                    : room.Kind == RoomKind.Start ? new Color(0.25f, 0.3f, 0.2f, 0.95f)
                    : room.Kind == RoomKind.Side ? new Color(0.3f, 0.26f, 0.15f, 0.95f)
                    : new Color(0.22f, 0.21f, 0.2f, 0.95f);
                UiSkin.Rect(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(0.6f, 0.55f, 0.45f, seen ? 0.9f : 0.3f));
                UiSkin.Rect(r, fill);

                if (boss) Icon(c, "BOSS", new Color(1f, 0.35f, 0.3f), large ? 16 : 11);
            }


            // Places worth remembering, once you have been in their room.
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 1f;
                chests = FindObjectsByType<Chest>(FindObjectsSortMode.None);
                fountains = FindObjectsByType<Fountain>(FindObjectsSortMode.None);
                pages = FindObjectsByType<LorePage>(FindObjectsSortMode.None);
                reliquaries = FindObjectsByType<Reliquary>(FindObjectsSortMode.None);
            }
            bool Known(Vector3 p)
            {
                foreach (var room in layout.Rooms) if (room.Contains(p, 0.5f)) return explored.Contains(room.Id);
                return false;
            }
            int fs = large ? 13 : 10;
            if (chests != null) foreach (var ch in chests) if (ch != null && !ch.Opened.Value && Known(ch.transform.position)) Square(ToMap(ch.transform.position), new Color(1f, 0.78f, 0.3f), large ? 8f : 6f);
            if (fountains != null) foreach (var f in fountains) if (f != null && !f.Spent.Value && Known(f.transform.position)) Icon(ToMap(f.transform.position), "+", new Color(0.45f, 0.75f, 1f), fs + 2);
            if (pages != null) foreach (var pg in pages) if (pg != null && Known(pg.transform.position)) Icon(ToMap(pg.transform.position), "J", new Color(0.92f, 0.85f, 0.65f), fs);
            if (reliquaries != null) foreach (var rq in reliquaries) if (rq != null) Icon(ToMap(rq.transform.position), "R", new Color(1f, 0.85f, 0.4f), fs);
            if (layout.PeddlerSpot.HasValue && Known(layout.PeddlerSpot.Value)) Icon(ToMap(layout.PeddlerSpot.Value), "$", new Color(1f, 0.82f, 0.35f), fs + 1);
            if (layout.Rooms.Count > 0) Icon(ToMap(layout.WaystonePosition), "W", new Color(0.55f, 0.85f, 1f), fs);
            var dirx = DungeonDirector.Instance;
            if (dirx != null && dirx.Phase.Value == RunPhase.FloorCleared) Icon(ToMap(DungeonDirector.ExitPosition), "EXIT", new Color(0.75f, 0.55f, 1f), fs + 1);

            // Loot worth walking to: relics, sigils, runes, and your own gear.
            foreach (var drop in LootDrop.All)
            {
                var k = drop.Kind.Value;
                if (k == LootKind.Relic) Dot(ToMap(drop.transform.position), Relics.GemColor((Relic)drop.Amount.Value), 7f);
                else if (k == LootKind.Sigil) Dot(ToMap(drop.transform.position), LootInfo.SigilColor, 6f);
                else if (k == LootKind.Rune) Dot(ToMap(drop.transform.position), Runes.Color((Rune)drop.Amount.Value), 6f);
            }
            var me = player.GetComponent<Progression.PlayerProgress>();
            foreach (var i in World.Interactable.All)
                if (i is GearDrop g && g.For.Value == me.OwnerClientId && !g.Item.Value.IsEmpty)
                    Dot(ToMap(g.transform.position), Gear.QualityColor(g.Item.Value.Quality), 6f);

            // Enemies within radar range of you.
            foreach (var hp in Health.All)
            {
                if (hp == null || hp.Team != Team.Enemies || !hp.IsAlive) continue;
                if (hp.TryGetComponent<Enemies.GargoyleBrain>(out var stone) && !stone.Woken.Value) continue;
                if (Vector3.Distance(hp.transform.position, player.transform.position) > RadarRange) continue;
                bool boss = hp.GetComponent<Enemies.BossBrain>() != null;
                Dot(ToMap(hp.transform.position), boss ? new Color(1f, 0.2f, 0.15f) : new Color(0.85f, 0.25f, 0.2f), boss ? 9f : 5f);
            }

            // Teammates, then you on top as an arrow.
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.gameObject == player.gameObject) continue;
                var np = p.GetComponent<NetworkPlayer>();
                Dot(ToMap(p.transform.position), p.Downed.Value ? Color.gray : np.SlotColor, 8f);
            }
            Arrow(ToMap(player.transform.position), player.transform.eulerAngles.y, player.SlotColor);

            GUI.EndGroup();

            if (!large) GUI.Label(new Rect(frame.x, frame.yMax + 2, frame.width, 18), "M  map", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.UpperRight });
        }

        private static float MapSpan(DungeonLayout layout)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var r in layout.Rooms)
            {
                minX = Mathf.Min(minX, r.Center.x - r.Size.x); maxX = Mathf.Max(maxX, r.Center.x + r.Size.x);
                minZ = Mathf.Min(minZ, r.Center.z - r.Size.y); maxZ = Mathf.Max(maxZ, r.Center.z + r.Size.y);
            }
            return Mathf.Max(maxX - minX, maxZ - minZ) + 10f;
        }

        private static Vector3 MapCentre(DungeonLayout layout)
        {
            var sum = Vector3.zero;
            foreach (var r in layout.Rooms) sum += r.Center;
            return sum / layout.Rooms.Count;
        }

        private float nextScan;
        private Chest[] chests;
        private Fountain[] fountains;
        private LorePage[] pages;
        private Reliquary[] reliquaries;

        private static void Square(Vector2 at, Color color, float size)
        {
            UiSkin.Rect(new Rect(at.x - size / 2f - 1f, at.y - size / 2f - 1f, size + 2f, size + 2f), new Color(0f, 0f, 0f, 0.7f));
            UiSkin.Rect(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), color);
        }

        private static void Dot(Vector2 at, Color color, float size)
        {
            UiSkin.Rect(new Rect(at.x - size / 2f - 1, at.y - size / 2f - 1, size + 2, size + 2), new Color(0f, 0f, 0f, 0.8f));
            UiSkin.Rect(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), color);
        }

        private static void Icon(Vector2 at, string glyph, Color color, int fontSize)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(at.x - 29, at.y - 19, 60, 40), glyph, style);
            style.normal.textColor = color;
            GUI.Label(new Rect(at.x - 30, at.y - 20, 60, 40), glyph, style);
        }

        /// <summary>Your marker: a small chevron pointing where you face.</summary>
        private static void Arrow(Vector2 at, float yaw, Color color)
        {
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(yaw, at);
            Dot(at, color, 9f);
            UiSkin.Rect(new Rect(at.x - 2f, at.y - 11f, 4f, 7f), Color.white);
            GUI.matrix = old;
        }
    }
}
