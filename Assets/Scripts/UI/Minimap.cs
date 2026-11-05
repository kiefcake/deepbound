using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.World;
using Deepbound.Progression;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    /// <summary>
    /// The map, north-up, drawn from the floor's layout (no second camera).
    ///
    /// <para>
    /// Rooms appear once someone in the party has stood in them; their neighbours show as faint
    /// "?" outlines, so you can see where the unexplored doors lead without seeing what is behind
    /// them. Rooms are drawn as rooms: stone walls with doorway gaps, the floor in the floor's
    /// colour, pits, water and sunken courts marked. Pixel icons mark what matters (chests you have
    /// seen - not ones still hidden behind cracked walls - fountains, the journal, the peddler, the
    /// waystone, the reliquary, the exit, the boss); your arrow carries a view cone; allies are
    /// arrows in their colours; pings pulse; the dead show within radar range. Objectives off the
    /// edge of the small map are pinned to its border. M toggles the large map with a legend.
    /// </para>
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        private const float RadarRange = 22f;

        private NetworkPlayer player;
        private readonly HashSet<int> explored = new HashSet<int>();
        private int arenaSeen = -1;
        private bool large;

        /// <summary>The corner map's side at the current setting (virtual pixels).</summary>
        public static float SmallSize => 220f * Settings.MinimapSize;
        /// <summary>Where the space under the corner map begins: what sits beneath it starts here.</summary>
        public static float SmallBottom => Settings.ShowMinimap ? 18f + SmallSize + 16f : 18f;

        /// <summary>Large overview on or off (M; the screenshot run also sets it).</summary>
        public bool Large { get => large; set => large = value; }

        public void Init(NetworkPlayer owner) => player = owner;

        private float nextScan;
        private Chest[] chests;
        private Fountain[] fountains;
        private LorePage[] pages;
        private Reliquary[] reliquaries;
        private CrackedWall[] crackedWalls;
        private Lever[] levers;

        // Per-floor floor colours; the wall stone.
        private static readonly Color[] FloorCol = { new Color(0.27f, 0.25f, 0.22f), new Color(0.17f, 0.25f, 0.24f), new Color(0.23f, 0.22f, 0.29f), new Color(0.3f, 0.17f, 0.16f), new Color(0.2f, 0.14f, 0.27f) };
        private static readonly Color Wall = new Color(0.62f, 0.56f, 0.46f), WallDim = new Color(0.4f, 0.37f, 0.32f);

        private void Update()
        {
            var layout = DungeonDirector.Layout;
            if (layout == null) return;

            if (arenaSeen != DungeonDirector.ArenaId)
            {
                arenaSeen = DungeonDirector.ArenaId;
                explored.Clear();
                explored.Add(layout.StartRoom.Id);
                nextScan = 0f;
            }

            // Corwin's map: every room charted from the start.
            var me = NetworkPlayer.Local;
            if (me != null && me.TryGetComponent<PlayerProgress>(out var mine) && mine.MapRevealed.Value && explored.Count < layout.Rooms.Count)
                foreach (var r in layout.Rooms) explored.Add(r.Id);

            // Any party member standing in a room reveals it for everyone's map.
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var room = layout.RoomAt(p.transform.position, 1f);
                if (room != null) explored.Add(room.Id);
            }

            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 1f;
                chests = FindObjectsByType<Chest>(FindObjectsSortMode.None);
                fountains = FindObjectsByType<Fountain>(FindObjectsSortMode.None);
                pages = FindObjectsByType<LorePage>(FindObjectsSortMode.None);
                reliquaries = FindObjectsByType<Reliquary>(FindObjectsSortMode.None);
                crackedWalls = FindObjectsByType<CrackedWall>(FindObjectsSortMode.None);
                levers = FindObjectsByType<Lever>(FindObjectsSortMode.None);
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && Binds.Down(Bind.Map)) large = !large;
        }

        private bool Neighbour(DungeonLayout layout, DungeonLayout.Room room)
        {
            foreach (var l in layout.Links)
                if ((l.A == room.Id && explored.Contains(l.B)) || (l.B == room.Id && explored.Contains(l.A))) return true;
            return false;
        }

        /// <summary>Seen, and not still sealed behind a cracked wall.</summary>
        private bool Visible(DungeonLayout layout, Vector3 p)
        {
            var room = layout.RoomAt(p, 0f);
            if (room == null)
            {
                // Outside every room's floor: in a niche. Shown once its wall is down and the room beside it is known.
                if (crackedWalls != null)
                    foreach (var w in crackedWalls)
                        if (w != null && (w.transform.position - p).sqrMagnitude < 5f * 5f) return false;
                room = layout.RoomAt(p, 4f);
            }
            return room != null && explored.Contains(room.Id);
        }

                private void OnGUI()
        {
            UiTimer.Begin();
            OnGUIBody();
            UiTimer.Mark("Minimap");
        }

        private void OnGUIBody()
        {
            useGUILayout = false; // no GUILayout here: skip the layout pass (perf)
            AfflictionOverlay.PaintOnce();
            if (GameHud.Cinematic) return;
            UiSkin.Apply();
            var layout = DungeonDirector.Layout;
            if (layout == null || player == null || CharacterSelect.Open || PauseMenu.Open) return;

            if (!large && !Settings.ShowMinimap) return;
            float w = UiScale.W, h = UiScale.H;
            float size = large ? Mathf.Min(w * 0.6f, h * 0.84f) : SmallSize;
            var frame = large ? new Rect(w / 2f - size / 2f - 120f, h / 2f - size / 2f, size, size) : new Rect(w - size - 18f, 18f, size, size);
            float span = large ? MapSpan(layout) : 62f;
            float scale = size / span;
            Vector3 centre = large ? MapCentre(layout) : player.transform.position;
            int depth = Mathf.Clamp(layout.Depth, 0, FloorCol.Length - 1);
            var floor = FloorCol[depth];
            float px = large ? 2.4f : 1.8f;

            if (large) UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.55f));
            UiSkin.Panel(new Rect(frame.x - 6f, frame.y - 6f, frame.width + 12f, frame.height + 12f), large ? 0.96f : 0.85f);
            UiSkin.Rect(frame, new Color(0.035f, 0.03f, 0.028f, 0.95f));
            GUI.BeginGroup(frame);
            Vector2 ToMap(Vector3 world) => new Vector2(size / 2f + (world.x - centre.x) * scale, size / 2f - (world.z - centre.z) * scale);
            Rect WorldRect(float cx, float cz, float sx, float sz)
            {
                var c = ToMap(new Vector3(cx, 0f, cz));
                return new Rect(c.x - sx * scale / 2f, c.y - sz * scale / 2f, sx * scale, sz * scale);
            }
            float wallPx = Mathf.Max(2f, 0.9f * scale);
            var here = layout.RoomAt(player.transform.position, 1f);

            // ---- Corridors (under the rooms): stone edges, a floor down the middle ----
            foreach (var link in layout.Links)
            {
                var a = layout.Rooms[link.A];
                var b = layout.Rooms[link.B];
                bool seenA = explored.Contains(a.Id), seenB = explored.Contains(b.Id);
                if (!seenA && !seenB) continue;
                var pa = ToMap(a.Center);
                var pb = ToMap(b.Center);
                float thick = Mathf.Max(4f, DungeonLayout.CorridorWidth * scale);
                bool horiz = Mathf.Abs(pa.x - pb.x) > Mathf.Abs(pa.y - pb.y);
                var min = Vector2.Min(pa, pb);
                var max = Vector2.Max(pa, pb);
                var r = horiz ? new Rect(min.x, pa.y - thick / 2f, max.x - min.x, thick) : new Rect(pa.x - thick / 2f, min.y, thick, max.y - min.y);
                var edge = seenA && seenB ? Wall : WallDim;
                UiSkin.Rect(new Rect(r.x - (horiz ? 0 : wallPx), r.y - (horiz ? wallPx : 0), r.width + (horiz ? 0 : wallPx * 2f), r.height + (horiz ? wallPx * 2f : 0)), new Color(edge.r, edge.g, edge.b, 0.9f));
                UiSkin.Rect(r, floor * (seenA && seenB ? 0.9f : 0.6f));
            }

            // ---- Rooms ----
            foreach (var room in layout.Rooms)
            {
                bool seen = explored.Contains(room.Id);
                bool boss = room.Kind == RoomKind.Boss;
                bool hint = !seen && Neighbour(layout, room);
                if (!seen && !boss && !hint) continue;
                var r = WorldRect(room.Center.x, room.Center.z, room.Size.x, room.Size.y);
                var outer = new Rect(r.x - wallPx, r.y - wallPx, r.width + wallPx * 2f, r.height + wallPx * 2f);

                if (!seen)
                {
                    // Known only by its doorway: a faint dashed outline and a question mark (the boss room, a skull).
                    DashedRect(outer, boss ? new Color(0.85f, 0.3f, 0.25f, 0.7f) : new Color(Wall.r, Wall.g, Wall.b, 0.45f), wallPx);
                    if (boss) MapIcons.Draw(MapIcons.Id.Skull, r.center, new Color(0.9f, 0.3f, 0.25f, 0.85f), px);
                    else if (r.width > 18f) GUI.Label(new Rect(r.center.x - 15f, r.center.y - 12f, 30f, 24f), "?", new GUIStyle(UiSkin.Small) { fontSize = large ? 18 : 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.75f, 0.65f, 0.5f) } });
                    continue;
                }

                var wallCol = room == here ? new Color(1f, 0.86f, 0.55f) : Wall;
                UiSkin.Rect(outer, wallCol);
                var fill = boss ? Color.Lerp(floor, new Color(0.45f, 0.1f, 0.08f), 0.55f)
                    : room.Kind == RoomKind.Start ? Color.Lerp(floor, new Color(0.25f, 0.36f, 0.22f), 0.4f)
                    : room.Feature == RoomFeature.Vault ? Color.Lerp(floor, new Color(0.45f, 0.36f, 0.15f), 0.4f)
                    : room.Feature == RoomFeature.Treasury ? Color.Lerp(floor, new Color(0.55f, 0.45f, 0.15f), 0.45f)
                    : room.Feature == RoomFeature.Mirror ? Color.Lerp(floor, new Color(0.35f, 0.15f, 0.45f), 0.45f)
                    : room.Feature == RoomFeature.Oath ? Color.Lerp(floor, new Color(0.2f, 0.3f, 0.5f), 0.45f)
                    : room.Feature == RoomFeature.Lightless ? Color.Lerp(floor, Color.black, 0.6f)
                    : room.Feature == RoomFeature.Proving ? Color.Lerp(floor, new Color(0.5f, 0.25f, 0.1f), 0.45f)
                    : room.Feature == RoomFeature.Beggar || room.Feature == RoomFeature.Armoury ? Color.Lerp(floor, new Color(0.35f, 0.3f, 0.2f), 0.4f)
                    : floor;
                UiSkin.Rect(r, fill);
                // Flagstone hint: a faint grid every 4 m.
                if (scale * 4f > 7f)
                {
                    var grid = new Color(0f, 0f, 0f, 0.13f);
                    for (float gx = r.x + scale * 4f; gx < r.xMax - 1f; gx += scale * 4f) UiSkin.Rect(new Rect(gx, r.y, 1f, r.height), grid);
                    for (float gy = r.y + scale * 4f; gy < r.yMax - 1f; gy += scale * 4f) UiSkin.Rect(new Rect(r.x, gy, r.width, 1f), grid);
                }
                // The special rooms say what they are (on the big map, or a big enough mini-map).
                if (room.Special && (large || r.width > 26f))
                {
                    string label = room.Feature == RoomFeature.Treasury ? "TREASURY" : room.Feature == RoomFeature.Beggar ? "BEGGAR"
                        : room.Feature == RoomFeature.Proving ? "PROVING" : room.Feature == RoomFeature.Armoury ? "ARMOURY" : room.Feature == RoomFeature.Oath ? "OATH" : "MIRROR";
                    GUI.Label(new Rect(r.x - 20f, r.center.y - 9f, r.width + 40f, 18f), label, new GUIStyle(UiSkin.Small) { fontSize = large ? 12 : 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.88f, 0.55f, 0.9f) } });
                }
                // Raised ground: lighter blocks.
                foreach (var (rr, top) in room.Raised) UiSkin.Rect(WorldRect(rr.center.x, rr.center.y, rr.width, rr.height), new Color(1f, 1f, 1f, 0.06f + 0.03f * top));
                // Features: the chasm's pit, a sunken court or flooded cistern.
                if (room.Feature == RoomFeature.Chasm && room.Pit.width > 0f)
                    UiSkin.Rect(WorldRect(room.Pit.center.x, room.Pit.center.y, room.Pit.width, room.Pit.height), new Color(0f, 0f, 0f, 0.92f));
                if ((room.Feature == RoomFeature.SunkenCourt || room.Feature == RoomFeature.Cistern) && room.Low.width > 0f)
                {
                    var low = WorldRect(room.Low.center.x, room.Low.center.y, room.Low.width, room.Low.height);
                    UiSkin.Rect(low, room.Feature == RoomFeature.Cistern ? new Color(0.12f, 0.28f, 0.33f, 0.95f) : fill * 0.65f);
                }
                if (depth == 1 && DungeonDirector.Thickness > 0.05f) UiSkin.Rect(r, new Color(0.25f, 0.3f, 0.12f, 0.35f * DungeonDirector.Thickness));
                // Doorway gaps: the corridor's floor cut through the wall.
                for (int side = 0; side < 4; side++)
                {
                    if (!room.Doors[side]) continue;
                    float door = DungeonLayout.CorridorWidth * scale;
                    var c = r.center;
                    Rect gap = side == 0 ? new Rect(c.x - door / 2f, outer.y, door, wallPx)
                        : side == 2 ? new Rect(c.x - door / 2f, r.yMax, door, wallPx)
                        : side == 1 ? new Rect(r.xMax, c.y - door / 2f, wallPx, door)
                        : new Rect(outer.x, c.y - door / 2f, wallPx, door);
                    UiSkin.Rect(gap, floor * 0.9f);
                }
                if (boss) MapIcons.Draw(MapIcons.Id.Skull, r.center, new Color(1f, 0.35f, 0.3f), px);
            }

            // ---- Places ----
            if (chests != null) foreach (var ch in chests) if (ch != null && !ch.Opened.Value && Visible(layout, ch.transform.position)) MapIcons.Draw(MapIcons.Id.Chest, ToMap(ch.transform.position), new Color(1f, 0.8f, 0.35f), px);
            if (fountains != null) foreach (var f in fountains) if (f != null && !f.Spent.Value && Visible(layout, f.transform.position)) MapIcons.Draw(MapIcons.Id.Fountain, ToMap(f.transform.position), new Color(0.45f, 0.78f, 1f), px);
            if (pages != null) foreach (var pg in pages) if (pg != null && Visible(layout, pg.transform.position)) MapIcons.Draw(MapIcons.Id.Book, ToMap(pg.transform.position), new Color(0.93f, 0.86f, 0.66f), px);
            if (levers != null) foreach (var lv in levers) if (lv != null && !lv.Pulled.Value && Visible(layout, lv.transform.position)) MapIcons.Draw(MapIcons.Id.Lever, ToMap(lv.transform.position), new Color(1f, 0.7f, 0.35f), px);
            if (layout.PeddlerSpot.HasValue && Visible(layout, layout.PeddlerSpot.Value)) MapIcons.Draw(MapIcons.Id.Coin, ToMap(layout.PeddlerSpot.Value), new Color(1f, 0.82f, 0.35f), px);
            MapIcons.Draw(MapIcons.Id.Waystone, ToMap(layout.WaystonePosition), new Color(0.55f, 0.85f, 1f), px);

            // Loot worth walking to: relics, sigils, runes, rare draughts and tomes, and your own gear.
            foreach (var drop in LootDrop.All)
            {
                if (drop == null) continue;
                var k = drop.Kind.Value;
                var c = k == LootKind.Relic ? Relics.GemColor((Relic)drop.Amount.Value) : k == LootKind.Sigil ? LootInfo.SigilColor : k == LootKind.Rune ? Runes.Color((Rune)drop.Amount.Value)
                    : k == LootKind.RareItem ? Progression.RareItems.Color((Progression.RareItem)drop.Amount.Value) : Color.clear;
                if (c.a > 0f && Visible(layout, drop.transform.position)) MapIcons.Draw(MapIcons.Id.Gem, ToMap(drop.transform.position), c, px * 0.85f);
            }
            var me = player.GetComponent<Progression.PlayerProgress>();
            foreach (var i in Interactable.All)
                if (i is GearDrop g && (g.For.Value == me.OwnerClientId || g.Shared) && !g.Item.Value.IsEmpty)
                    MapIcons.Draw(MapIcons.Id.Gear, ToMap(g.transform.position), Gear.QualityColor(g.Item.Value.Quality), px * 0.85f);

            // The dead within radar range: soft red blobs, gold if elite.
            foreach (var hp in Health.All)
            {
                if (hp == null || hp.Team != Team.Enemies || !hp.IsAlive) continue;
                if (Vector3.Distance(hp.transform.position, player.transform.position) > RadarRange) continue;
                if (hp.TryGetComponent<Enemies.GargoyleBrain>(out var stone) && !stone.Woken.Value) continue;
                if (hp.GetComponent<Enemies.BossBrain>() != null) continue;
                var at = ToMap(hp.transform.position);
                bool elite = hp.TryGetComponent<Enemies.EnemyBrain>(out var brain) && brain.Elite.Value;
                float s = elite ? 11f : 8f;
                var old = GUI.color;
                GUI.color = elite ? new Color(1f, 0.75f, 0.25f) : new Color(0.95f, 0.25f, 0.2f);
                GUI.DrawTexture(new Rect(at.x - s / 2f, at.y - s / 2f, s, s), MapIcons.Soft);
                GUI.color = old;
            }
            var bossNow = Enemies.BossBrain.Active;
            if (bossNow != null && !bossNow.IsDead && Vector3.Distance(bossNow.transform.position, player.transform.position) < RadarRange * 1.5f)
                MapIcons.Draw(MapIcons.Id.Skull, ToMap(bossNow.transform.position), new Color(1f, 0.25f, 0.2f), px * 1.15f);

            // Pings: a pulsing ring in the ping's colour.
            foreach (var kv in PlayerPings.Active)
            {
                var pg = kv.Value;
                if (Time.time > pg.Until) continue;
                var at = ToMap(pg.Follow != null ? pg.Follow.position : pg.Position);
                float k = Mathf.Repeat(Time.time * 1.5f, 1f);
                float pulse = 10f + k * 12f;
                var old = GUI.color;
                GUI.color = new Color(pg.Color.r, pg.Color.g, pg.Color.b, 1f - k);
                GUI.DrawTexture(new Rect(at.x - pulse / 2f, at.y - pulse / 2f, pulse, pulse), MapIcons.Soft);
                GUI.color = old;
                MapIcons.Draw(MapIcons.Id.Ping, at, pg.Color, px);
            }

            // Objectives: the reliquary and, once the floor is cleared, the way out (pinned to the edge if off the map).
            if (reliquaries != null) foreach (var rq in reliquaries) if (rq != null && Visible(layout, rq.transform.position)) Pinned(ToMap(rq.transform.position), size, MapIcons.Id.Cross, new Color(1f, 0.86f, 0.45f), px);
            var dir = DungeonDirector.Instance;
            if (dir != null && dir.Phase.Value == RunPhase.FloorCleared) Pinned(ToMap(DungeonDirector.ExitPosition), size, MapIcons.Id.Exit, new Color(0.78f, 0.58f, 1f), px);

            // Allies: arrows in their colours (pinned to the edge on the small map), grey when down.
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.gameObject == player.gameObject) continue;
                var np = p.GetComponent<NetworkPlayer>();
                var at = ToMap(p.transform.position);
                var col = p.Downed.Value ? new Color(0.65f, 0.65f, 0.65f) : np.SlotColor;
                if (!large && !new Rect(6f, 6f, size - 12f, size - 12f).Contains(at)) { EdgeChevron(at, size, col); continue; }
                ArrowAt(at, p.transform.eulerAngles.y, col, 16f);
            }

            // You: a view cone, then your arrow.
            var mine = ToMap(player.transform.position);
            float yaw = player.transform.eulerAngles.y;
            var m = GUI.matrix;
            UiScale.Rotate(yaw, mine);
            var oc = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.8f, 0.28f);
            float coneLen = large ? 70f : 60f;
            GUI.DrawTexture(new Rect(mine.x - coneLen, mine.y - coneLen, coneLen * 2f, coneLen), MapIcons.Cone);
            GUI.color = oc;
            GUI.matrix = m;
            ArrowAt(mine, yaw, Color.Lerp(player.SlotColor, Color.white, 0.35f), 18f);

            GUI.EndGroup();

            // Compass and caption.
            GUI.Label(new Rect(frame.center.x - 15f, frame.y - 4f, 30f, 18f), "N", new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UiSkin.Gold } });
            if (!large)
            {
                string place = "FLOOR " + (layout.Depth + 1) + (layout.Depth >= DungeonDirector.AbyssDepth ? " ABYSS" : "") + "  ·  " + DungeonTheme.FloorName(layout.Depth);
                // A dark strip under the frame, so the caption reads over bright rooms.
                UiSkin.Rect(new Rect(frame.x, frame.yMax + 4f, frame.width, 20f), new Color(0f, 0f, 0f, 0.55f));
                // The key hint on the right; the place name gets what is left (clipped, never under the hint).
                var hintStyle = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.UpperRight, wordWrap = false };
                string hint = Binds.Live(Bind.Map) + " map";
                float hintW = hintStyle.CalcSize(new GUIContent(hint)).x;
                var placeStyle = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip, wordWrap = false, normal = { textColor = UiSkin.Gold } };
                GUI.Label(new Rect(frame.x + 6f, frame.yMax + 5f, frame.width - 20f - hintW, 18), UiSkin.Fit(place, placeStyle, frame.width - 20f - hintW), placeStyle);
                GUI.Label(new Rect(frame.x, frame.yMax + 5f, frame.width - 6f, 18), hint, hintStyle);
            }
            else DrawLegend(layout, frame);
        }

        /// <summary>The large map's side panel: where you are, how much is charted, and what the marks mean.</summary>
        private void DrawLegend(DungeonLayout layout, Rect frame)
        {
            var panel = new Rect(frame.xMax + 18f, frame.y, 240f, frame.height);
            UiSkin.Panel(panel, 0.95f);
            float y = panel.y + 14f;
            var theme = DungeonTheme.ForDepth(layout.Depth);
            string title = "FLOOR " + (layout.Depth + 1);
            GUI.Label(new Rect(panel.x + 14f, y, panel.width - 28f, 24f), title, new GUIStyle(UiSkin.Heading) { fontSize = 20 });
            y += 26f;
            GUI.Label(new Rect(panel.x + 14f, y, panel.width - 28f, 20f), theme.Name + ": " + DungeonTheme.FloorName(layout.Depth), new GUIStyle(UiSkin.Body) { fontStyle = FontStyle.Italic, wordWrap = true });
            y += 26f;
            int seen = 0;
            foreach (var r in layout.Rooms) if (explored.Contains(r.Id)) seen++;
            GUI.Label(new Rect(panel.x + 14f, y, panel.width - 28f, 20f), "Rooms charted  " + seen + " / " + layout.Rooms.Count, UiSkin.Small);
            y += 28f;
            var entries = new (MapIcons.Id icon, Color col, string text)[]
            {
                (MapIcons.Id.Skull, new Color(1f, 0.35f, 0.3f), "The guardian"),
                (MapIcons.Id.Waystone, new Color(0.55f, 0.85f, 1f), "Waystone (souls, the reliquary)"),
                (MapIcons.Id.Chest, new Color(1f, 0.8f, 0.35f), "Unopened chest"),
                (MapIcons.Id.Fountain, new Color(0.45f, 0.78f, 1f), "Fountain"),
                (MapIcons.Id.Book, new Color(0.93f, 0.86f, 0.66f), "Journal page"),
                (MapIcons.Id.Lever, new Color(1f, 0.7f, 0.35f), "Vault lever"),
                (MapIcons.Id.Coin, new Color(1f, 0.82f, 0.35f), "Peddler"),
                (MapIcons.Id.Cross, new Color(1f, 0.86f, 0.45f), "Reliquary"),
                (MapIcons.Id.Exit, new Color(0.78f, 0.58f, 1f), "The way out"),
                (MapIcons.Id.Gem, new Color(0.75f, 0.55f, 1f), "Relic, sigil or rune"),
                (MapIcons.Id.Gear, new Color(0.55f, 0.85f, 0.55f), "Gear for you"),
                (MapIcons.Id.Ping, new Color(0.35f, 0.7f, 1f), "A ping"),
            };
            foreach (var (icon, col, text) in entries)
            {
                MapIcons.Draw(icon, new Vector2(panel.x + 26f, y + 9f), col, 2f);
                GUI.Label(new Rect(panel.x + 44f, y, panel.width - 54f, 20f), text, UiSkin.Small);
                y += 23f;
            }
            y += 6f;
            GUI.Label(new Rect(panel.x + 14f, y, panel.width - 28f, 40f), "Dashed rooms with a ? lie beyond doors you have not taken.", new GUIStyle(UiSkin.Small) { wordWrap = true, fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.75f, 0.72f, 0.65f) } });
        }

        private static void ArrowAt(Vector2 at, float yaw, Color color, float size)
        {
            var m = GUI.matrix;
            UiScale.Rotate(yaw, at);
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), MapIcons.Arrow);
            GUI.color = old;
            GUI.matrix = m;
        }

        /// <summary>An icon at its place, or pinned to the edge (with a little arrow) when it is off the map.</summary>
        private static void Pinned(Vector2 at, float size, MapIcons.Id icon, Color col, float px)
        {
            var inner = new Rect(12f, 12f, size - 24f, size - 24f);
            if (inner.Contains(at)) { MapIcons.Draw(icon, at, col, px); return; }
            MapIcons.Draw(icon, EdgePoint(at, size, 14f), col, px);
            EdgeChevron(at, size, col);
        }

        private static Vector2 EdgePoint(Vector2 at, float size, float inset)
        {
            var c = new Vector2(size / 2f, size / 2f);
            var d = at - c;
            float half = size / 2f - inset;
            float k = Mathf.Min(half / Mathf.Max(0.001f, Mathf.Abs(d.x)), half / Mathf.Max(0.001f, Mathf.Abs(d.y)));
            return c + d * Mathf.Min(1f, k);
        }

        private static void EdgeChevron(Vector2 at, float size, Color col)
        {
            var d = at - new Vector2(size / 2f, size / 2f);
            float yaw = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
            ArrowAt(EdgePoint(at, size, 5f), yaw, col, 12f);
        }

        private static void DashedRect(Rect r, Color c, float t)
        {
            const float dash = 6f, gapLen = 5f;
            for (float x = r.x; x < r.xMax; x += dash + gapLen)
            {
                float len = Mathf.Min(dash, r.xMax - x);
                UiSkin.Rect(new Rect(x, r.y, len, t), c);
                UiSkin.Rect(new Rect(x, r.yMax - t, len, t), c);
            }
            for (float y = r.y; y < r.yMax; y += dash + gapLen)
            {
                float len = Mathf.Min(dash, r.yMax - y);
                UiSkin.Rect(new Rect(r.x, y, t, len), c);
                UiSkin.Rect(new Rect(r.xMax - t, y, t, len), c);
            }
        }

        private static float MapSpan(DungeonLayout layout)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var r in layout.Rooms)
            {
                minX = Mathf.Min(minX, r.Center.x - r.Size.x / 2f); maxX = Mathf.Max(maxX, r.Center.x + r.Size.x / 2f);
                minZ = Mathf.Min(minZ, r.Center.z - r.Size.y / 2f); maxZ = Mathf.Max(maxZ, r.Center.z + r.Size.y / 2f);
            }
            return Mathf.Max(maxX - minX, maxZ - minZ) + 16f;
        }

        private static Vector3 MapCentre(DungeonLayout layout)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var r in layout.Rooms)
            {
                minX = Mathf.Min(minX, r.Center.x - r.Size.x / 2f); maxX = Mathf.Max(maxX, r.Center.x + r.Size.x / 2f);
                minZ = Mathf.Min(minZ, r.Center.z - r.Size.y / 2f); maxZ = Mathf.Max(maxZ, r.Center.z + r.Size.y / 2f);
            }
            return new Vector3((minX + maxX) / 2f, 0f, (minZ + maxZ) / 2f);
        }
    }
}
