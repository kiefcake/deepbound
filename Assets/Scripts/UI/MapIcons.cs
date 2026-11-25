using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.UI
{
    public static class MapIcons
    {
        public enum Id { Chest, Skull, Fountain, Book, Coin, Waystone, Exit, Cross, Ping, Gear, Gem, Lever, Altar, Armor, Charm, Sword, Flask, Rune, Lair }

        private static readonly Dictionary<Id, string[]> Masks = new Dictionary<Id, string[]>
        {
            [Id.Chest] = new[] { ".#######.", "#########", "#########", "####.####", "#########", "#########", ".#######." },
            [Id.Skull] = new[] { "..#####..", ".#######.", "#########", "##..#..##", "##..#..##", "#########", ".##.#.##.", "..#.#.#.." },
            [Id.Fountain] = new[] { "....#....", "...###...", "..#####..", ".#######.", ".#######.", ".#######.", "..#####..", "...###..." },
            [Id.Book] = new[] { "#########", "#...#...#", "#.#.#.#.#", "#...#...#", "#.#.#.#.#", "#...#...#", "#########" },
            [Id.Coin] = new[] { "..#####..", ".##...##.", "##.###.##", "##.#...##", "##.###.##", "##...#.##", "##.###.##", ".##...##.", "..#####.." },
            [Id.Waystone] = new[] { "....#....", "...###...", "..##.##..", ".##...##.", "##.....##", ".##...##.", "..##.##..", "...###...", "....#...." },
            [Id.Exit] = new[] { "..#####..", ".#.....#.", "#..###..#", "#.#...#.#", "#.#...#.#", "#.#...#.#", "#..###..#", ".#.....#.", "..#####.." },
            [Id.Cross] = new[] { "...###...", "...###...", "#########", "#########", "...###...", "...###...", "...###...", "...###..." },
            [Id.Ping] = new[] { "..##..", "..##..", "..##..", "..##..", "..##..", "......", "..##..", "..##.." },
            [Id.Gear] = new[] { "#######", "#######", "#######", "#######", ".#####.", "..###..", "...#..." },
            [Id.Gem] = new[] { "...#...", "..###..", ".#####.", "#######", ".#####.", "..###..", "...#..." },
            [Id.Lever] = new[] { "......##", ".....##.", "....##..", "...##...", "..##....", "#######.", "#######." },
            [Id.Altar] = new[] { "...#...", "..###..", "...#...", "#######", ".#####.", ".#...#.", ".#...#." },

            [Id.Armor] = new[] { "##.....##", "###...###", "#########", ".#######.", ".###.###.", ".#######.", ".###.###.", ".#######.", "..#####.." },
            [Id.Charm] = new[] { "#.......#", ".#.....#.", "..#...#..", "...#.#...", "...###...", "..#####..", "..##.##..", "..#####..", "...###..." },
            [Id.Sword] = new[] { "........#", ".......##", "......##.", ".....##..", "#...##...", ".####....", "..##.....", ".#.##....", "#..#....." },
            [Id.Flask] = new[] { "...##...", "...##...", "..####..", ".##..##.", "########", "########", "########", ".######." },
            [Id.Rune] = new[] { ".#####.", "##.#.##", "#..#..#", "#.###.#", "#..#..#", "##.#.##", ".#####." },
            [Id.Lair] = new[] { "#.......#", "##.....##", ".##...##.", ".#######.", "##.###.##", "#########", ".#.#.#.#.", "..#####.." },
        };

        private static readonly Dictionary<Id, Texture2D> cache = new Dictionary<Id, Texture2D>();
        private static Texture2D cone, arrow, soft;

        public static Texture2D Get(Id id)
        {
            if (cache.TryGetValue(id, out var t) && t != null) return t;
            var m = Masks[id];
            int h = m.Length, w = m[0].Length;
            t = new Texture2D(w + 2, h + 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            bool Ink(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && m[h - 1 - y][x] == '#';
            for (int y = -1; y <= h; y++)
                for (int x = -1; x <= w; x++)
                {
                    Color c = Color.clear;
                    if (Ink(x, y)) c = Color.white;
                    else
                        for (int dy = -1; dy <= 1 && c.a == 0f; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                                if (Ink(x + dx, y + dy)) { c = new Color(0f, 0f, 0f, 0.85f); break; }
                    t.SetPixel(x + 1, y + 1, c);
                }
            t.Apply();
            cache[id] = t;
            return t;
        }

        public static void Draw(Id id, Vector2 at, Color color, float px = 2f)
        {
            var t = Get(id);
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(at.x - t.width * px / 2f, at.y - t.height * px / 2f, t.width * px, t.height * px), t);
            GUI.color = old;
        }

        public static Texture2D Cone
        {
            get
            {
                if (cone != null) return cone;
                const int n = 64;
                cone = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var c = new Vector2(n / 2f, 0f);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var d = new Vector2(x + 0.5f, y + 0.5f) - c;
                        float ang = Mathf.Abs(Vector2.SignedAngle(Vector2.up, d));
                        float a = d.magnitude > n ? 0f : Mathf.Clamp01((40f - ang) / 6f) * Mathf.Clamp01(1f - d.magnitude / n);
                        cone.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                cone.Apply();
                return cone;
            }
        }

        public static Texture2D Arrow
        {
            get
            {
                if (arrow != null) return arrow;
                const int n = 24;
                arrow = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n;
                        float half = (1f - v) * 0.45f;
                        float notch = 0.18f - Mathf.Abs(u) * 0.6f;
                        bool inside = Mathf.Abs(u) < half && v > notch && v < 0.98f;
                        bool rim = !inside && Mathf.Abs(u) < half + 0.07f && v > notch - 0.07f && v < 1f;

                        arrow.SetPixel(x, y, inside ? Color.white : rim ? new Color(0f, 0f, 0f, 0.85f) : Color.clear);
                    }
                arrow.Apply();
                return arrow;
            }
        }

        public static Texture2D Soft
        {
            get
            {
                if (soft != null) return soft;
                const int n = 16;
                soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = Mathf.Clamp01((1f - d) * 3f);
                        soft.SetPixel(x, y, d < 0.62f ? new Color(1f, 1f, 1f, a) : new Color(0f, 0f, 0f, a * 0.9f));
                    }
                soft.Apply();
                return soft;
            }
        }
    }
}
