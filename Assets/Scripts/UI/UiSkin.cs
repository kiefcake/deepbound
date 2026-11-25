using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.UI
{
    public static class UiSkin
    {
        private static Font bookFont;

        public static Font BookFont => bookFont ??= Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Tahoma", "Arial" }, 16);

        public static Font TitleFont => BookFont;

        public static void Apply()
        {
            UiScale.Begin();
            if (GUI.skin.font != BookFont)
            {
                GUI.skin.font = BookFont;

                GUI.skin.label.clipping = TextClipping.Overflow;
                GUI.skin.button.clipping = TextClipping.Overflow;
            }
        }

        public static readonly Color Gold = new Color(0.92f, 0.74f, 0.36f);
        public static readonly Color Ink = new Color(0.03f, 0.03f, 0.04f, 0.88f);
        public static readonly Color Ember = new Color(0.85f, 0.35f, 0.12f);

        private static GUIStyle button, buttonBig, title, heading, body, small, field;

        public static GUIStyle Button => button ??= MakeButton(18);

        private static int hoverKey, hoverFrame = -1;

        public static bool Btn(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            bool hit = GUILayout.Button(text, style, options);
            Feedback(GUILayoutUtility.GetLastRect(), text, hit);
            return hit;
        }

        public static bool Btn(Rect rect, string text, GUIStyle style)
        {
            bool hit = GUI.Button(rect, text, style);
            Feedback(rect, text, hit);
            return hit;
        }

        private static void Feedback(Rect r, string text, bool hit)
        {
            if (hit) Art.Sfx.Play2D(Art.Sound.UiClick, 0.75f);
            var e = Event.current;
            if (e == null || e.type != EventType.Repaint || !GUI.enabled) return;
            int key = (text ?? "").GetHashCode() ^ ((int)r.y * 397) ^ (int)r.x;
            if (r.Contains(e.mousePosition))
            {
                if (key != hoverKey || Time.frameCount - hoverFrame > 2) { if (key != hoverKey) Art.Sfx.Play2D(Art.Sound.UiClick, 0.18f); hoverKey = key; }
                hoverFrame = Time.frameCount;
            }
            else if (key == hoverKey && Time.frameCount - hoverFrame > 2) hoverKey = 0;
        }
        public static GUIStyle ButtonBig => buttonBig ??= MakeButton(24);
        public static GUIStyle Title => title ??= MakeTitle();

        private static GUIStyle MakeTitle()
        {
            var s = MakeLabel(72, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
            s.font = TitleFont;
            return s;
        }
        public static GUIStyle Heading => heading ??= MakeLabel(22, TextAnchor.MiddleLeft, Gold, FontStyle.Bold);
        public static GUIStyle Body => body ??= MakeLabel(15, TextAnchor.UpperLeft, new Color(0.9f, 0.88f, 0.84f), FontStyle.Normal, wrap: true);
        public static GUIStyle Small => small ??= MakeLabel(12, TextAnchor.UpperLeft, new Color(0.75f, 0.72f, 0.68f), FontStyle.Normal, wrap: true);

        public static GUIStyle Field
        {
            get
            {
                if (field != null) return field;
                field = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
                field.normal.background = Tex(new Color(0.08f, 0.07f, 0.06f, 0.95f));
                field.focused.background = Tex(new Color(0.14f, 0.12f, 0.09f, 0.95f));
                field.normal.textColor = field.focused.textColor = Color.white;
                return field;
            }
        }

        public static void Ornament(float x, float y, float width, float fade)
        {
            var gold = new Color(Gold.r, Gold.g, Gold.b, 0.85f * fade);
            var dim = new Color(Gold.r, Gold.g, Gold.b, 0.4f * fade);
            float mid = x + width / 2f;
            Rect(new Rect(x + 8f, y, width / 2f - 22f, 1f), gold);
            Rect(new Rect(mid + 14f, y, width / 2f - 22f, 1f), gold);
            Rect(new Rect(x + 40f, y + 4f, width / 2f - 54f, 1f), dim);
            Rect(new Rect(mid + 14f, y + 4f, width / 2f - 54f, 1f), dim);
            Rect(new Rect(x, y - 1f, 3f, 3f), gold);
            Rect(new Rect(x + width - 3f, y - 1f, 3f, 3f), gold);
            var m = GUI.matrix;
            UiScale.Rotate(45f, new Vector2(mid, y + 0.5f));
            Rect(new Rect(mid - 5f, y - 4.5f, 10f, 10f), gold);
            Rect(new Rect(mid - 2.5f, y - 2f, 5f, 5f), new Color(0.05f, 0.03f, 0.02f, 0.9f * fade));
            GUI.matrix = m;
        }

        public static void Rect(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static readonly Color Iron = new Color(0.16f, 0.15f, 0.15f);
        public static readonly Color Leather = new Color(0.07f, 0.05f, 0.045f);

        public static void Panel(Rect r, float alpha = 0.88f)
        {
            float a = alpha;
            Rect(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), new Color(0f, 0f, 0f, 0.5f * a));
            Rect(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(Iron.r, Iron.g, Iron.b, 0.95f * a));
            Rect(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(Gold.r * 0.8f, Gold.g * 0.8f, Gold.b * 0.8f, 0.85f * a));
            Rect(r, new Color(Leather.r, Leather.g, Leather.b, 0.94f * a));

            Rect(new Rect(r.x, r.y, r.width, Mathf.Min(18f, r.height * 0.3f)), new Color(0.35f, 0.22f, 0.1f, 0.12f * a));
            if (r.width > 40 && r.height > 24)
            {
                foreach (var c in new[] { new Vector2(r.x - 2, r.y - 2), new Vector2(r.xMax + 2, r.y - 2), new Vector2(r.x - 2, r.yMax + 2), new Vector2(r.xMax + 2, r.yMax + 2) })
                    Stud(c, 5f, a);
            }
        }

        public static void Stud(Vector2 at, float size, float alpha = 1f)
        {
            var m = GUI.matrix;
            UiScale.Rotate(45f, at);
            Rect(new Rect(at.x - size / 2f - 1, at.y - size / 2f - 1, size + 2, size + 2), new Color(0.1f, 0.07f, 0.03f, alpha));
            Rect(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), new Color(Gold.r, Gold.g, Gold.b, alpha));
            GUI.matrix = m;
        }

        public static void Bar(Rect r, float k, Color fill, int notches = 0, float trail = -1f)
        {
            Rect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(Iron.r, Iron.g, Iron.b, 0.95f));
            Rect(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(Gold.r * 0.55f, Gold.g * 0.55f, Gold.b * 0.55f, 0.9f));
            Rect(r, new Color(0.05f, 0.03f, 0.03f, 0.95f));
            float fw = r.width * Mathf.Clamp01(k);

            if (trail > k) Rect(new Rect(r.x + fw, r.y, r.width * (Mathf.Clamp01(trail) - Mathf.Clamp01(k)), r.height), new Color(1f, 0.92f, 0.75f, 0.75f));
            Rect(new Rect(r.x, r.y, fw, r.height), fill);
            Rect(new Rect(r.x, r.y, fw, Mathf.Max(1f, r.height * 0.35f)), new Color(1f, 1f, 1f, 0.18f));
            Rect(new Rect(r.x, r.yMax - Mathf.Max(1f, r.height * 0.2f), fw, Mathf.Max(1f, r.height * 0.2f)), new Color(0f, 0f, 0f, 0.25f));
            for (int i = 1; i < notches; i++)
                Rect(new Rect(r.x + r.width * i / notches, r.y, 1f, r.height), new Color(0f, 0f, 0f, 0.35f));
        }

        public static GUIStyle Derive(GUIStyle from, Color color, int fontSize = 0, FontStyle? fontStyle = null, TextAnchor? align = null, bool? wrap = null, Font font = null)
        {
            var key = (from, fontSize, fontStyle.HasValue ? (int)fontStyle.Value : -1, align.HasValue ? (int)align.Value : -1, wrap.HasValue ? (wrap.Value ? 1 : 0) : -1, font);
            if (!derived.TryGetValue(key, out var s))
            {
                if (derived.Count > 400) derived.Clear();
                s = new GUIStyle(from);
                if (fontSize > 0) s.fontSize = fontSize;
                if (fontStyle.HasValue) s.fontStyle = fontStyle.Value;
                if (align.HasValue) s.alignment = align.Value;
                if (wrap.HasValue) s.wordWrap = wrap.Value;
                if (font != null) s.font = font;
                derived[key] = s;
            }
            s.normal.textColor = color;
            return s;
        }
        private static readonly Dictionary<(GUIStyle, int, int, int, int, Font), GUIStyle> derived = new Dictionary<(GUIStyle, int, int, int, int, Font), GUIStyle>();

        public static string FitCached(ref string lastText, ref string lastFit, string text, GUIStyle style, float width)
        {
            string key = text + "" + Mathf.RoundToInt(width);
            if (key != lastText) { lastText = key; lastFit = Fit(text, style, width); }
            return lastFit;
        }

        public static string Fit(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || style.CalcSize(new GUIContent(text)).x <= width) return text;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (style.CalcSize(new GUIContent(text.Substring(0, mid) + "...")).x <= width) lo = mid; else hi = mid - 1;
            }
            return text.Substring(0, lo).TrimEnd(' ', ',', '.', ':') + "...";
        }

        public static Color Readable(Color c, float minLum = 0.64f)
        {
            float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            if (lum >= minLum) return c;
            var o = Color.Lerp(c, Color.white, Mathf.Clamp01((minLum - lum) / Mathf.Max(0.01f, 1f - lum)));
            o.a = c.a;
            return o;
        }

        public static void Shadowed(Rect r, string text, GUIStyle style, float alpha = 1f)
        {
            var c = style.normal.textColor;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, Derive(style, new Color(0f, 0f, 0f, 0.85f * alpha)));
            GUI.Label(r, text, Derive(style, new Color(c.r, c.g, c.b, alpha)));
        }

        private static GUIStyle MakeButton(int size)
        {
            var s = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };

            s.normal.background = Framed(new Color(0.09f, 0.065f, 0.05f, 0.95f), new Color(0.55f, 0.42f, 0.2f, 0.9f));
            s.hover.background = Framed(new Color(0.28f, 0.17f, 0.08f, 0.97f), new Color(0.95f, 0.75f, 0.36f, 1f));
            s.active.background = Framed(new Color(0.5f, 0.3f, 0.12f, 1f), new Color(1f, 0.85f, 0.45f, 1f));
            s.onNormal.background = s.normal.background;
            s.normal.textColor = new Color(0.9f, 0.85f, 0.75f);
            s.hover.textColor = s.active.textColor = Color.white;
            s.border = new RectOffset(1, 1, 1, 1);
            return s;
        }

        private static GUIStyle MakeLabel(int size, TextAnchor anchor, Color color, FontStyle fs, bool wrap = false)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, fontStyle = fs, wordWrap = wrap, richText = true, clipping = TextClipping.Overflow };
            s.normal.textColor = color;
            return s;
        }

        private static Texture2D Framed(Color fill, Color edge)
        {
            var t = new Texture2D(3, 3) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    t.SetPixel(x, y, x == 1 && y == 1 ? fill : edge);
            t.Apply();
            return t;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
