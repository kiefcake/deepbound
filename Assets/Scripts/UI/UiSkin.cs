using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// Shared IMGUI look: gothic. Serif book type (Palatino, from the OS), blackletter for
    /// titles where Windows has it, and panels framed like old ironwork — dark iron edge, a
    /// gold inner rule, a leather-dark fill and gold studs at the corners. Built from code;
    /// no assets, like everything else.
    /// </summary>
    public static class UiSkin
    {
        private static Font bookFont, titleFont;

        /// <summary>Body text: a clean sans (2026-10-21, the user: away from the gothic look).</summary>
        public static Font BookFont => bookFont ??= Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Tahoma", "Arial" }, 16);

        /// <summary>Titles: the same clean face, semibold.</summary>
        /// A second OS font of the same family ("Segoe UI Semibold") shared the body font's atlas and drew
        /// nothing or the wrong words (the title screen lost DEEPBOUND): titles are the body face in bold.
        public static Font TitleFont => BookFont;

        /// <summary>Call at the top of every OnGUI: styles that do not name a font use the book serif.</summary>
        public static void Apply()
        {
            UiScale.Begin();
            if (GUI.skin.font != BookFont)
            {
                GUI.skin.font = BookFont;
                // Segoe sits lower in its line than the old serif: never cut off descenders (g, p, y).
                GUI.skin.label.clipping = TextClipping.Overflow;
                GUI.skin.button.clipping = TextClipping.Overflow;
            }
        }

        public static readonly Color Gold = new Color(0.92f, 0.74f, 0.36f);
        public static readonly Color Ink = new Color(0.03f, 0.03f, 0.04f, 0.88f);
        public static readonly Color Ember = new Color(0.85f, 0.35f, 0.12f);

        private static GUIStyle button, buttonBig, title, heading, body, small, field;

        public static GUIStyle Button => button ??= MakeButton(18);
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

        /// <summary>A gothic rule under the title: two gold lines that taper toward a diamond, with a bead at each end.</summary>
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

        /// <summary>An ornate panel: iron edge, gold rule, dark leather, gold corner studs.</summary>
        public static void Panel(Rect r, float alpha = 0.88f)
        {
            float a = alpha;
            Rect(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), new Color(0f, 0f, 0f, 0.5f * a));
            Rect(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(Iron.r, Iron.g, Iron.b, 0.95f * a));
            Rect(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(Gold.r * 0.8f, Gold.g * 0.8f, Gold.b * 0.8f, 0.85f * a));
            Rect(r, new Color(Leather.r, Leather.g, Leather.b, 0.94f * a));
            // A faint warm sheen across the top, like lamplight on leather.
            Rect(new Rect(r.x, r.y, r.width, Mathf.Min(18f, r.height * 0.3f)), new Color(0.35f, 0.22f, 0.1f, 0.12f * a));
            if (r.width > 40 && r.height > 24)
            {
                foreach (var c in new[] { new Vector2(r.x - 2, r.y - 2), new Vector2(r.xMax + 2, r.y - 2), new Vector2(r.x - 2, r.yMax + 2), new Vector2(r.xMax + 2, r.yMax + 2) })
                    Stud(c, 5f, a);
            }
        }

        /// <summary>A small gold diamond (rotated square) centred on a point.</summary>
        public static void Stud(Vector2 at, float size, float alpha = 1f)
        {
            var m = GUI.matrix;
            UiScale.Rotate(45f, at);
            Rect(new Rect(at.x - size / 2f - 1, at.y - size / 2f - 1, size + 2, size + 2), new Color(0.1f, 0.07f, 0.03f, alpha));
            Rect(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), new Color(Gold.r, Gold.g, Gold.b, alpha));
            GUI.matrix = m;
        }

        /// <summary>A framed bar: iron frame, dark well, the fill with a highlight, and notches.</summary>
        public static void Bar(Rect r, float k, Color fill, int notches = 0, float trail = -1f)
        {
            Rect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(Iron.r, Iron.g, Iron.b, 0.95f));
            Rect(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(Gold.r * 0.55f, Gold.g * 0.55f, Gold.b * 0.55f, 0.9f));
            Rect(r, new Color(0.05f, 0.03f, 0.03f, 0.95f));
            float fw = r.width * Mathf.Clamp01(k);
            // What was just lost lingers as a pale chunk before it drains away.
            if (trail > k) Rect(new Rect(r.x + fw, r.y, r.width * (Mathf.Clamp01(trail) - Mathf.Clamp01(k)), r.height), new Color(1f, 0.92f, 0.75f, 0.75f));
            Rect(new Rect(r.x, r.y, fw, r.height), fill);
            Rect(new Rect(r.x, r.y, fw, Mathf.Max(1f, r.height * 0.35f)), new Color(1f, 1f, 1f, 0.18f));
            Rect(new Rect(r.x, r.yMax - Mathf.Max(1f, r.height * 0.2f), fw, Mathf.Max(1f, r.height * 0.2f)), new Color(0f, 0f, 0f, 0.25f));
            for (int i = 1; i < notches; i++)
                Rect(new Rect(r.x + r.width * i / notches, r.y, 1f, r.height), new Color(0f, 0f, 0f, 0.35f));
        }

        /// <summary>Text cut to fit a width on one line, ending "..." when it had to be shortened.</summary>
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

        /// <summary>A colour pushed light enough to read as text on the dark panels.</summary>
        public static Color Readable(Color c, float minLum = 0.64f)
        {
            float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            if (lum >= minLum) return c;
            var o = Color.Lerp(c, Color.white, Mathf.Clamp01((minLum - lum) / Mathf.Max(0.01f, 1f - lum)));
            o.a = c.a;
            return o;
        }

        /// <summary>Text with a soft dark drop shadow, for titles over 3D scenes.</summary>
        public static void Shadowed(Rect r, string text, GUIStyle style, float alpha = 1f)
        {
            var shadow = new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, 0.85f * alpha) } };
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, shadow);
            var main = new GUIStyle(style);
            main.normal.textColor = new Color(style.normal.textColor.r, style.normal.textColor.g, style.normal.textColor.b, alpha);
            GUI.Label(r, text, main);
        }

        private static GUIStyle MakeButton(int size)
        {
            var s = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            // Buttons are little framed plaques: a gold-ruled border baked into a 3x3 texture.
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

        /// <summary>A 3x3 texture: a one-pixel border round a fill (stretched with a 1px border).</summary>
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
