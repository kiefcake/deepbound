using System.Collections.Generic;
using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Painted shop signs for the camp (2026-11-07, the user: stations should show what they are from a distance, "Both":
    /// signs and names). Each sign is a pixel picture drawn in code: a dark board with a gold rule, the trade's icon on the
    /// left and its word in a 5x7 pixel capital on the right, point-filtered to sit with the retro look. <see cref="Hang"/>
    /// builds a wooden board on two chains; <see cref="Flat"/> a board fixed to a wall or a beam.
    /// </summary>
    public static class SignArt
    {
        public enum Icon { Potion, Anvil, Needle, Sword, Rune, Candle, Mug, Dice, Scroll, Bell, Target }

        private const int W = 112, H = 30;

        private static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
            ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
            ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "#..#.", "#..#.", ".##.." },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
            ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
            ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
            ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" },
            ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
            ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
            ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
            ['&'] = new[] { ".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#" },
            ['\''] = new[] { "..#..", "..#..", ".....", ".....", ".....", ".....", "....." },
        };

        // 12x12 icons, top row first. Letters pick colours: o outline, a gold, b body, c accent, d dark accent.
        private static readonly Dictionary<Icon, string[]> Icons = new Dictionary<Icon, string[]>
        {
            [Icon.Potion] = new[] { "....oooo....", "....oaao....", ".....oo.....", ".....oo.....", "...oobboo...", "..obbbbbbo..", ".obbcbbbbbo.", ".obcbbbbbbo.", ".obbbbbbbbo.", ".obbbbbbbbo.", "..obbbbbbo..", "...oooooo..." },
            [Icon.Anvil] = new[] { "............", "............", "oooooooooo..", "obbbbbbbbbo.", ".obbbbbbbbbo", "..oobbbbooo.", "....obbo....", "....obbo....", "...obbbbo...", "..obbbbbbo..", "..oooooooo..", "............" },
            [Icon.Needle] = new[] { "..........oo", ".........oao", "........oao.", ".......oao..", "..ooo.oao...", ".obbbooao...", "obcccbao....", "obcccbo.....", "obcccbo.....", ".obbbo......", "..ooo.......", "............" },
            [Icon.Sword] = new[] { "..........oo", ".........oco", "........oco.", ".......oco..", "......oco...", ".....oco....", "..o.oco.....", "..oaoo......", "...oao......", "..oaooa.....", ".oao..o.....", ".oo........." },
            [Icon.Rune] = new[] { "...oooooo...", "..obbbbbbo..", ".obbbccbbbo.", ".obbcbbcbbo.", ".obbbccbbbo.", ".obbbbcbbbo.", ".obbbbcbbbo.", ".obbbccbbbo.", ".obbcbbcbbo.", ".obbbbbbbbo.", ".oooooooooo.", "............" },
            [Icon.Candle] = new[] { ".....oo.....", ".....oco....", "....occo....", ".....oo.....", "....obbo....", "....obbo....", "....obbo....", "....obbo....", "....obbo....", "..oooooooo..", ".oaaaaaaaao.", "..oooooooo.." },
            [Icon.Mug] = new[] { "..........  ", ".ocococo....", ".occcccco...", ".obbbbbboo..", ".obdbdbbo.o.", ".obdbdbbo.o.", ".obdbdbbo.o.", ".obdbdbboo..", ".obbbbbbo...", ".obbbbbbo...", "..oooooo....", "............" },
            [Icon.Dice] = new[] { "............", ".oooooo.....", ".obbbbbo....", ".obdbbbo....", ".obbdbbo....", ".obbbdbooooo", ".oooooo.obbo", "......obdbbo", "......obbbbo", "......obbdbo", "......oooooo", "............" },
            [Icon.Scroll] = new[] { "..oooooooo..", ".oaobbbbbbo.", ".oaobdddbbo.", "..oobbbbbbo.", "...obdddbbo.", "...obbbbbbo.", "...obddbbbo.", "...obbbbbbo.", "...obdddbbo.", "..oobbbbbbo.", ".oaoooooooo.", "..oo........" },
            [Icon.Bell] = new[] { ".....oo.....", "....oaao....", "...oaaaao...", "..oaaaaaao..", "..oaaaaaao..", "..oaaaaaao..", ".oaaaaaaaao.", ".oaaaaaaaao.", "oaaaaaaaaaao", "oooooooooooo", ".....oo.....", "............" },
            [Icon.Target] = new[] { "....oooo....", "..oobbbboo..", ".obbccccbbo.", ".obccbbccbo.", "obccbaabccbo", "obcbaddabcbo", "obcbaddabcbo", "obccbaabccbo", ".obccbbccbo.", ".obbccccbbo.", "..oobbbboo..", "....oooo...." },
        };

        private static readonly Dictionary<Icon, Color32[]> IconColours = new Dictionary<Icon, Color32[]>
        {
            // b body, c accent, d dark accent
            [Icon.Potion] = new[] { C(190, 40, 46), C(255, 190, 180), C(120, 20, 26) },
            [Icon.Anvil] = new[] { C(120, 124, 132), C(200, 200, 210), C(70, 70, 80) },
            [Icon.Needle] = new[] { C(120, 80, 50), C(160, 60, 140), C(90, 40, 80) },
            [Icon.Sword] = new[] { C(160, 120, 70), C(215, 220, 230), C(90, 60, 30) },
            [Icon.Rune] = new[] { C(110, 115, 125), C(110, 210, 255), C(60, 60, 70) },
            [Icon.Candle] = new[] { C(235, 225, 200), C(255, 180, 60), C(150, 140, 120) },
            [Icon.Mug] = new[] { C(150, 100, 55), C(250, 245, 230), C(100, 65, 35) },
            [Icon.Dice] = new[] { C(240, 235, 220), C(200, 40, 40), C(30, 25, 25) },
            [Icon.Scroll] = new[] { C(230, 210, 165), C(150, 40, 30), C(110, 90, 60) },
            [Icon.Bell] = new[] { C(200, 150, 60), C(255, 220, 120), C(120, 80, 30) },
            [Icon.Target] = new[] { C(235, 230, 215), C(40, 70, 160), C(200, 40, 40) },
        };

        private static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        /// <summary>The painted face of a sign: icon left, word right.</summary>
        public static Texture2D Paint(string word, Icon icon)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "Sign_" + word, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            var board = C(44, 28, 20); var grain = C(54, 35, 24); var rule = C(196, 150, 70); var ink = C(236, 214, 160); var edge = C(24, 15, 10);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool border = x == 0 || y == 0 || x == W - 1 || y == H - 1;
                    bool gold = x == 2 || y == 2 || x == W - 3 || y == H - 3;
                    px[y * W + x] = border ? edge : gold ? rule : (y % 5 == 0 || (x * 7 + y * 3) % 23 == 0) ? grain : board;
                }
            // The icon, 12x12 at double size, centred on the left.
            var cols = IconColours[icon];
            var rows = Icons[icon];
            int ix = 5, iy = (H + 24) / 2 - 1;
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    char ch = rows[r][c];
                    if (ch == '.' || ch == ' ') continue;
                    var col = ch == 'o' ? edge : ch == 'a' ? rule : ch == 'b' ? cols[0] : ch == 'c' ? cols[1] : cols[2];
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            int x = ix + c * 2 + sx, y = iy - r * 2 - sy;
                            if (x > 2 && y > 2 && x < W - 3 && y < H - 3) px[y * W + x] = col;
                        }
                }
            // The word, 5x7 capitals at double size if it fits, else single.
            word = word.ToUpperInvariant();
            int scale = 2;
            int textW = Measure(word, scale);
            int room = W - 34 - 6;
            if (textW > room) { scale = 1; textW = Measure(word, scale); }
            int tx = 34 + (room - textW) / 2, ty = H / 2 + (7 * scale) / 2;
            foreach (char ch in word)
            {
                if (ch == ' ') { tx += 3 * scale; continue; }
                if (!Font.TryGetValue(ch, out var g)) continue;
                for (int r = 0; r < 7; r++)
                    for (int c = 0; c < 5; c++)
                    {
                        if (g[r][c] != '#') continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int x = tx + c * scale + sx, y = ty - r * scale - sy;
                                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                                px[y * W + x] = ink;
                                if (y - 1 >= 0 && px[(y - 1) * W + x].r != ink.r) px[(y - 1) * W + x] = edge; // a drop shadow
                            }
                    }
                tx += 6 * scale;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private static int Measure(string word, int scale)
        {
            int w = 0;
            foreach (char ch in word) w += ch == ' ' ? 3 * scale : 6 * scale;
            return w - scale;
        }

        private static Mesh quad, quadBack;

        /// <summary>A quad in the XY plane facing +Z (the back one faces -Z with its picture the right way round).</summary>
        private static Mesh Quad(bool back)
        {
            if (back ? quadBack != null : quad != null) return back ? quadBack : quad;
            var m = new Mesh { name = back ? "SignBack" : "SignFace" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
            // Facing +Z: seen from +Z looking -Z, the viewer's right is -X, so the picture's left goes to +X.
            m.uv = back
                ? new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) }
                : new[] { new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f) };
            m.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            m.triangles = back ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
            if (back) m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.RecalculateBounds();
            if (back) quadBack = m; else quad = m;
            return m;
        }

        private static GameObject Face(Transform parent, Texture2D tex, Vector3 at, Quaternion rot, Vector2 size, bool back)
        {
            var go = new GameObject(back ? "SignBack" : "SignFace");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, rot);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Quad(back);
            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(ArtLibrary.Get("Cards")) { name = tex.name };
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", new Color(0.82f, 0.78f, 0.72f));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A board hung on two short chains under <paramref name="top"/>, readable from both sides, facing <paramref name="facing"/>.</summary>
        public static void Hang(Transform parent, MeshKit.Batcher d, Vector3 top, Vector3 facing, string word, Icon icon, float width = 1.15f)
        {
            facing.y = 0f; facing.Normalize();
            var rot = Quaternion.LookRotation(facing);
            var right = rot * Vector3.right;
            float h = width * H / W;
            var centre = top + Vector3.down * (0.28f + h / 2f);
            d.AddBox(centre, new Vector3(width + 0.06f, h + 0.06f, 0.05f), Mat.Wood, new Color(0.3f, 0.2f, 0.13f), rot);
            foreach (float s in new[] { -0.4f, 0.4f })
                d.AddBox(top + right * (s * width) + Vector3.down * 0.14f, new Vector3(0.02f, 0.28f, 0.02f), Mat.Steel);
            var tex = Paint(word, icon);
            Face(parent, tex, centre + facing * 0.027f, rot, new Vector2(width, h), false);
            Face(parent, tex, centre - facing * 0.027f, rot, new Vector2(width, h), true);
        }

        /// <summary>A board fixed flat (on a wall, a beam, a post), its face toward <paramref name="facing"/>.</summary>
        public static void Flat(Transform parent, MeshKit.Batcher d, Vector3 centre, Vector3 facing, string word, Icon icon, float width = 1.15f)
        {
            facing.y = 0f; facing.Normalize();
            var rot = Quaternion.LookRotation(facing);
            float h = width * H / W;
            d.AddBox(centre, new Vector3(width + 0.06f, h + 0.06f, 0.05f), Mat.Wood, new Color(0.3f, 0.2f, 0.13f), rot);
            Face(parent, Paint(word, icon), centre + facing * 0.027f, rot, new Vector2(width, h), false);
        }

        /// <summary>A post with an arm and a hanging sign off it, for stations with no stall to hang one from.</summary>
        public static void Post(Transform parent, MeshKit.Batcher d, Vector3 foot, Vector3 arm, Vector3 facing, string word, Icon icon)
        {
            arm.y = 0f; arm.Normalize();
            d.AddBox(foot + Vector3.up * 0.1f, new Vector3(0.36f, 0.2f, 0.36f), Mat.StoneWall);
            d.AddBox(foot + Vector3.up * 1.5f, new Vector3(0.12f, 3f, 0.12f), Mat.Wood);
            d.AddBox(foot + arm * 0.6f + Vector3.up * 2.95f, new Vector3(0.08f, 0.08f, 1.3f), Mat.Wood, Quaternion.LookRotation(arm));
            Hang(parent, d, foot + arm * 0.75f + Vector3.up * 2.92f, facing, word, icon, 1.0f);
        }
    }
}
