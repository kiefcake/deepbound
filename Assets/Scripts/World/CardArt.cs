using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Playing cards drawn in code (2026-11-07, the 3D blackjack table): one point-filtered atlas of 52 faces and a back,
    /// pixel ranks from a 5x7 font and pixel suits, so they sit with the retro look. <see cref="Mesh"/> gives a flat card
    /// (long side along +Z, the face's top toward +Z) with its cell's UVs.
    /// </summary>
    public static class CardArt
    {
        public const byte Back = 52;
        private const int CW = 40, CH = 56, Cols = 14, Rows = 4;

        private static Texture2D atlas;
        private static readonly Dictionary<byte, Mesh> meshes = new Dictionary<byte, Mesh>();

        private static readonly string[] SpadeG = { "...#...", "..###..", ".#####.", "#######", "#######", "...#...", "..###.." };
        private static readonly string[] HeartG = { ".##.##.", "#######", "#######", "#######", ".#####.", "..###..", "...#..." };
        private static readonly string[] DiamondG = { "...#...", "..###..", ".#####.", "#######", ".#####.", "..###..", "...#..." };
        private static readonly string[] ClubG = { "..###..", "..###..", "##.#.##", "#######", "##.#.##", "...#...", "..###.." };

        private static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            ['6'] = new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." },
            ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." },
            ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "#..#.", "#..#.", ".##.." },
            ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
        };

        public static Texture2D Atlas
        {
            get
            {
                if (atlas == null) Build();
                return atlas;
            }
        }

        private static string RankText(byte card)
        {
            int r = BlackjackTable.Rank(card);
            return r == 1 ? "A" : r == 11 ? "J" : r == 12 ? "Q" : r == 13 ? "K" : r.ToString();
        }

        private static void Build()
        {
            atlas = new Texture2D(CW * Cols, CH * Rows, TextureFormat.RGBA32, false) { name = "CardAtlas", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[CW * Cols * CH * Rows];
            for (byte c = 0; c <= Back; c++) DrawCard(px, c);
            atlas.SetPixels32(px);
            atlas.Apply(false, true);
        }

        private static void Cell(byte card, out int ox, out int oy) { ox = (card % Cols) * CW; oy = (card / Cols) * CH; }

        private static void DrawCard(Color32[] px, byte card)
        {
            Cell(card, out int ox, out int oy);
            var paper = new Color32(242, 234, 214, 255);
            var edge = new Color32(70, 58, 48, 255);
            bool back = card == Back;
            for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    bool border = x == 0 || y == 0 || x == CW - 1 || y == CH - 1;
                    bool corner = (x < 2 || x > CW - 3) && (y < 2 || y > CH - 3);
                    Color32 c = border || corner ? edge : paper;
                    if (back && !border)
                    {
                        // Oxblood with a gold rule and a diamond lattice.
                        bool rule = x == 3 || y == 3 || x == CW - 4 || y == CH - 4;
                        bool lattice = ((x + y) % 6 == 0 || (x - y + 600) % 6 == 0) && x > 4 && y > 4 && x < CW - 5 && y < CH - 5;
                        c = rule ? new Color32(196, 150, 70, 255) : lattice ? new Color32(150, 40, 44, 255) : new Color32(100, 20, 26, 255);
                        if (x < 2 || y < 2 || x > CW - 3 || y > CH - 3) c = paper;
                    }
                    px[(oy + y) * CW * Cols + ox + x] = c;
                }
            if (back) return;
            int suit = BlackjackTable.Suit(card);
            bool red = suit == 1 || suit == 2;
            var ink = red ? new Color32(176, 26, 30, 255) : new Color32(26, 22, 24, 255);
            var glyph = suit == 0 ? SpadeG : suit == 1 ? HeartG : suit == 2 ? DiamondG : ClubG;
            string rank = RankText(card);
            // JUMBO INDEX (2026-11-13, the user could not read the cards from the stool): the rank big across the top (three
            // times the old corner glyph; "10" twice), the suit big beneath it, and a small suit in the top-left corner.
            int rs = rank.Length > 1 ? 2 : 3, gap = rs, rw = rank.Length * 5 * rs + (rank.Length - 1) * gap;
            int tx = (CW - rw) / 2 + 2, ty = CH - 5;
            foreach (char ch in rank) { Stamp(px, ox, oy, Font[ch], tx, ty, rs, ink, false); tx += 5 * rs + gap; }
            Stamp(px, ox, oy, glyph, 3, CH - 3, 1, ink, false);
            int s = 3, w = 7 * s;
            // Picture cards: a gold rule round the big suit.
            if (BlackjackTable.Rank(card) > 10)
            {
                var gold = new Color32(190, 140, 50, 255);
                for (int y = 4; y < 30; y++)
                    for (int x = 7; x < CW - 7; x++)
                        if (x == 7 || x == CW - 8 || y == 4 || y == 29) px[(oy + y) * CW * Cols + ox + x] = gold;
            }
            Stamp(px, ox, oy, glyph, (CW - w) / 2, 6 + w, s, ink, false);
        }

        /// <summary>Draws a glyph with its top-left at (x, y) (y counts up from the bottom), or rotated 180 degrees about that point.</summary>
        private static void Stamp(Color32[] px, int ox, int oy, string[] g, int x, int y, int scale, Color32 ink, bool flipped)
        {
            for (int row = 0; row < g.Length; row++)
                for (int col = 0; col < g[row].Length; col++)
                {
                    if (g[row][col] != '#') continue;
                    for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int px0 = flipped ? x - col * scale - sx : x + col * scale + sx;
                            int py0 = flipped ? y + row * scale + sy : y - row * scale - sy;
                            if (px0 < 1 || py0 < 1 || px0 >= CW - 1 || py0 >= CH - 1) continue;
                            px[(oy + py0) * CW * Cols + ox + px0] = ink;
                        }
                }
        }

        /// <summary>A flat card for <paramref name="card"/> (0..51, or <see cref="Back"/>), centred on its origin.</summary>
        public static Mesh Mesh(byte card)
        {
            if (meshes.TryGetValue(card, out var m) && m != null) return m;
            Cell(card, out int ox, out int oy);
            float u0 = (float)ox / (CW * Cols), u1 = (float)(ox + CW) / (CW * Cols), v0 = (float)oy / (CH * Rows), v1 = (float)(oy + CH) / (CH * Rows);
            float hw = BlackjackView.CardW / 2f, hh = BlackjackView.CardH / 2f;
            m = new Mesh { name = "Card" + card };
            m.vertices = new[] { new Vector3(-hw, 0f, -hh), new Vector3(-hw, 0f, hh), new Vector3(hw, 0f, hh), new Vector3(hw, 0f, -hh) };
            m.uv = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            meshes[card] = m;
            return m;
        }
    }
}
