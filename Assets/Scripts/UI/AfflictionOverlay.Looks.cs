using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// STATUS LOOKS (2026-11-23, the user: "each one should look different on the fov and not a copy paste different colour").
    /// Before this, fifteen states drew the same corner smoke in a new tint. Each now has its own picture AND its own motion:
    /// poison drips and bubbles, a turning rune ring for the curse, eyelids for the hex, a spiral for mania, grey bars and a
    /// broken ring for silence, rising motes for a mending potion, crackling arcs for a draught surge, a thumping ring for
    /// Bloodfury, cracked glass for a last stand, lash marks for zeal, a thorn band for martyrdom, a hexagon lattice for a ward,
    /// a glass rim for a shield, bark plates for Ironbark, stone cracks for Grave Stillness, light rays for Inspired, rising gold
    /// for consecrated ground, embers for Brothers in Arms and a column of light for a sanctuary. All painted in code at first use.
    /// </summary>
    public partial class AfflictionOverlay
    {
        private static Texture2D[] runes;
        private static Texture2D blob, lid, spiral, ring, cracks, rays, lattice, bark, thorns;

        private static void EnsureLooks()
        {
            if (blob != null) return;
            blob = PaintBlob();
            runes = new Texture2D[6];
            for (int g = 0; g < runes.Length; g++) runes[g] = PaintRune(g);
            lid = PaintLid();
            spiral = PaintSpiral();
            ring = PaintRing();
            cracks = PaintCracks();
            rays = PaintRays();
            lattice = PaintLattice();
            bark = PaintBark();
            thorns = PaintThorns();
        }

        // ------------------------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------------------------

        private static void Full(Texture2D tex, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(0, 0, UiScale.W, UiScale.H), tex, ScaleMode.StretchToFill);
        }

        private static void Dot(Vector2 at, float size, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), blob);
        }

        /// <summary>Poison: drips sliding down from the top edge with a trail, and bubbles rising from the bottom.</summary>
        private static void DrawPoison(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            for (int i = 0; i < 14; i++)
            {
                float lane = Hash(i, 3, 91);
                float x = (lane < 0.5f ? lane * 0.36f : 0.64f + (lane - 0.5f) * 0.72f) * w;
                float speed = 0.06f + 0.1f * Hash(i, 5, 91);
                float phase = (t * speed + Hash(i, 7, 91)) % 1f;
                float y = phase * h * 0.42f;
                float a = k * (1f - phase) * 0.7f * calm;
                GUI.color = new Color(0.4f, 0.78f, 0.18f, a * 0.5f);
                GUI.DrawTexture(new Rect(x - 2f, 0, 4f, y), Texture2D.whiteTexture);
                Dot(new Vector2(x, y), h * 0.018f, new Color(0.55f, 0.95f, 0.25f, a));
            }
            for (int i = 0; i < 10; i++)
            {
                float x = (0.05f + 0.9f * Hash(i, 11, 92)) * w;
                float phase = (t * (0.12f + 0.1f * Hash(i, 13, 92)) + Hash(i, 17, 92)) % 1f;
                float y = h - phase * h * 0.3f;
                float wob = Mathf.Sin(t * 4f + i) * 6f;
                Dot(new Vector2(x + wob, y), h * (0.012f + 0.01f * Hash(i, 19, 92)), new Color(0.5f, 0.9f, 0.3f, k * (1f - phase) * 0.6f * calm));
            }
        }

        /// <summary>Curse (Hexer): violet runes in a slow ring round the view, flickering one by one.</summary>
        private static void DrawCurseRunes(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            var c = new Vector2(w / 2f, h / 2f);
            int n = 12;
            float rx = w * 0.42f, ry = h * 0.4f, s = h * 0.07f;
            for (int i = 0; i < n; i++)
            {
                float a = t * 0.15f + i * Mathf.PI * 2f / n;
                var p = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                float flick = 0.55f + 0.45f * Mathf.Sin(t * 3f + i * 1.7f);
                GUI.color = new Color(0.65f, 0.3f, 1f, k * flick * 0.75f * calm);
                GUI.DrawTexture(new Rect(p.x - s / 2f, p.y - s / 2f, s, s), runes[i % runes.Length]);
            }
        }

        /// <summary>Hex (the hag's curse): dark eyelids close in from top and bottom on a slow throb, a green tinge at their edge.</summary>
        private static void DrawHexLids(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            float close = k * (0.2f + 0.05f * Mathf.Sin(t * 2.4f));
            GUI.color = new Color(0.08f, 0.12f, 0.02f, 0.9f * Mathf.Clamp01(k * 1.5f) * calm);
            GUI.DrawTexture(new Rect(-w * 0.1f, -h * (0.5f - close), w * 1.2f, h * 0.5f), lid);
            GUI.DrawTextureWithTexCoords(new Rect(-w * 0.1f, h * (1f - close), w * 1.2f, h * 0.5f), lid, new Rect(0, 1, 1, -1));
        }

        /// <summary>Mania: a magenta spiral turning fast over the whole view, breathing in and out.</summary>
        private static void DrawManiaSpiral(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            float s = Mathf.Max(w, h) * (1.5f + 0.1f * Mathf.Sin(t * 3f));
            var pivot = new Vector2(w / 2f, h / 2f);
            var keep = GUI.matrix;
            UiScale.Rotate(t * 160f, pivot);
            GUI.color = new Color(0.95f, 0.25f, 0.8f, k * 0.2f * calm);
            GUI.DrawTexture(new Rect(pivot.x - s / 2f, pivot.y - s / 2f, s, s), spiral);
            GUI.matrix = keep;
        }

        /// <summary>Silence: grey bars press in from top and bottom, and a broken ring sits round the crosshair (the sound cut off).</summary>
        private static void DrawSilence(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            float bar = h * 0.09f * Ease(k);
            GUI.color = new Color(0.42f, 0.42f, 0.5f, 0.55f * calm);
            GUI.DrawTexture(new Rect(0, 0, w, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, h - bar, w, bar), Texture2D.whiteTexture);
            float r = h * 0.07f;
            var c = new Vector2(w / 2f, h / 2f);
            for (int i = 0; i < 10; i++)
            {
                if (i % 3 == 2) continue; // the gaps
                float a = i * Mathf.PI * 2f / 10f + t * 0.3f;
                Dot(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, h * 0.012f, new Color(0.75f, 0.75f, 0.88f, k * 0.6f * calm));
            }
        }

        /// <summary>A mending potion: warm red-gold motes rising up the view, thick at the bottom.</summary>
        private static void DrawRising(float k, float t, float calm, Color c, int seed, int count, float height)
        {
            float w = UiScale.W, h = UiScale.H;
            for (int i = 0; i < count; i++)
            {
                float x = (0.03f + 0.94f * Hash(i, 1, seed)) * w;
                float phase = (t * (0.15f + 0.2f * Hash(i, 2, seed)) + Hash(i, 3, seed)) % 1f;
                float y = h - phase * h * height;
                float sway = Mathf.Sin(t * 2f + i * 1.3f) * 8f;
                Dot(new Vector2(x + sway, y), h * (0.008f + 0.012f * Hash(i, 4, seed)), new Color(c.r, c.g, c.b, k * (1f - phase) * 0.8f * calm));
            }
        }

        /// <summary>A draught surge: short crackling arcs along both side edges, re-struck many times a second.</summary>
        private static void DrawSurge(float k, float t, float calm, Color c)
        {
            float w = UiScale.W, h = UiScale.H;
            int strike = Mathf.FloorToInt(t * 12f);
            for (int side = 0; side < 2; side++)
                for (int arc = 0; arc < 3; arc++)
                {
                    float y = (0.15f + 0.7f * Hash(strike, arc + side * 7, 31)) * h;
                    float x = side == 0 ? w * 0.02f : w * 0.98f;
                    var p = new Vector2(x, y);
                    GUI.color = new Color(c.r, c.g, c.b, k * 0.85f * calm);
                    for (int seg = 0; seg < 6; seg++)
                    {
                        var q = p + new Vector2((side == 0 ? 1f : -1f) * (6f + 14f * Hash(strike, seg + arc * 9, 32 + side)), (Hash(strike, seg, 33 + arc) - 0.5f) * 40f);
                        Line(p, q, 2.5f);
                        p = q;
                    }
                }
        }

        private static void Line(Vector2 a, Vector2 b, float width)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var keep = GUI.matrix;
            UiScale.Rotate(ang, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, len, width), Texture2D.whiteTexture);
            GUI.matrix = keep;
        }

        /// <summary>Bloodfury: a red ring round the view that thumps inward on every heartbeat.</summary>
        private static void DrawFuryBeat(float k, float t, float calm)
        {
            float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(t * 3.4f)), 8f);
            float w = UiScale.W, h = UiScale.H, grow = 1f + 0.08f * (1f - beat);
            GUI.color = new Color(0.85f, 0.04f, 0.04f, k * (0.35f + 0.45f * beat) * calm);
            GUI.DrawTexture(new Rect(w / 2f - w * grow / 2f, h / 2f - h * grow / 2f, w * grow, h * grow), ring);
        }

        /// <summary>Last stand: cracked glass at the edges in deep red, and the view darkening slowly round it.</summary>
        private static void DrawLastStand(float k, float t, float calm)
        {
            float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(t * 2.2f)), 4f);
            Full(ring, new Color(0.08f, 0f, 0f, k * (0.5f + 0.2f * beat) * calm));
            Full(cracks, new Color(0.8f, 0.12f, 0.08f, k * (0.45f + 0.25f * beat) * calm));
        }

        /// <summary>Zeal: three red lash marks raked down each side, flaring on the beat.</summary>
        private static void DrawLashes(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            float flare = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(t * 7f));
            GUI.color = new Color(0.9f, 0.1f, 0.06f, k * (0.35f + 0.35f * flare) * calm);
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < 3; i++)
                {
                    float x0 = side == 0 ? w * (0.02f + 0.03f * i) : w * (0.98f - 0.03f * i);
                    float dx = side == 0 ? w * 0.06f : -w * 0.06f;
                    Line(new Vector2(x0, h * (0.25f + 0.04f * i)), new Vector2(x0 + dx, h * (0.6f + 0.04f * i)), 5f - i);
                }
        }

        /// <summary>Shield: a glassy rim round the whole view, with a glint that runs round it.</summary>
        private static void DrawShieldRim(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            Full(ring, new Color(1f, 0.94f, 0.78f, k * 0.28f * calm));
            float a = t * 1.4f;
            var c = new Vector2(w / 2f, h / 2f);
            var p = c + new Vector2(Mathf.Cos(a) * w * 0.47f, Mathf.Sin(a) * h * 0.46f);
            Dot(p, h * 0.08f, new Color(1f, 1f, 0.92f, k * 0.5f * calm));
        }

        /// <summary>Inspired: light rays leaning down from the top, sweeping slowly.</summary>
        private static void DrawRays(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            float sweep = Mathf.Sin(t * 0.6f) * w * 0.04f;
            GUI.color = new Color(1f, 0.92f, 0.62f, k * (0.3f + 0.08f * Mathf.Sin(t * 5f)) * calm);
            GUI.DrawTexture(new Rect(sweep - w * 0.05f, 0, w * 1.1f, h * 0.75f), rays);
        }

        /// <summary>A sanctuary: a soft column of gold light from straight above.</summary>
        private static void DrawColumn(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            GUI.color = new Color(1f, 0.85f, 0.45f, k * 0.22f * calm);
            GUI.DrawTexture(new Rect(w * 0.2f, -h * 0.2f, w * 0.6f, h * 1.2f), rays);
        }

        /// <summary>Plague Mask ward: a faint hexagon lattice over the edges, shimmering.</summary>
        private static void DrawWard(float k, float t, float calm)
        {
            Full(lattice, new Color(0.45f, 0.95f, 0.55f, k * (0.22f + 0.08f * Mathf.Sin(t * 4f)) * calm));
        }

        /// <summary>Martyrdom: a band of thorns across the top of the view, tightening on the pulse.</summary>
        private static void DrawThorns(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            float pulse = Mathf.Abs(Mathf.Sin(t * 5f));
            float bandH = h * (0.16f + 0.03f * pulse) * Ease(k);
            GUI.color = new Color(0.75f, 0.22f, 0.2f, k * 0.85f * calm);
            GUI.DrawTexture(new Rect(0, 0, w, bandH), thorns, ScaleMode.StretchToFill);
        }

        /// <summary>
        /// THE TALLYMAN NEAR: the view closes in black from its edges, and his tally marks - strokes of four and a slash -
        /// scratch themselves in round it, more the closer he is, twitching. Sickly lantern green.
        /// </summary>
        private static void DrawDread(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H;
            float throb = 0.85f + 0.15f * Mathf.Sin(t * Mathf.Lerp(4f, 11f, k));
            float grow = 1f - 0.18f * k;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(k * 1.15f) * 0.9f * throb);
            GUI.DrawTexture(new Rect(w / 2f - w * grow / 2f, h / 2f - h * grow / 2f, w * grow, h * grow), ring);
            GUI.DrawTexture(new Rect(w / 2f - w * grow / 2f, h / 2f - h * grow / 2f, w * grow, h * grow), ring);
            int groups = Mathf.FloorToInt(k * 14f);
            float jump = Mathf.Floor(t * 7f);
            for (int g = 0; g < groups; g++)
            {
                // Round the edge of the view, never over the middle.
                float a = Hash(g, 1, 61) * Mathf.PI * 2f;
                float r = 0.36f + 0.1f * Hash(g, 2, 61);
                var c = new Vector2(w / 2f + Mathf.Cos(a) * w * r, h / 2f + Mathf.Sin(a) * h * r * 1.1f);
                c += new Vector2(Hash(g, (int)jump, 62) - 0.5f, Hash(g, (int)jump, 63) - 0.5f) * 3f;
                float s = h * (0.045f + 0.02f * Hash(g, 3, 61));
                float alpha = Mathf.Clamp01(k * 14f - g) * (0.55f + 0.35f * Hash(g, (int)jump, 64)) * calm;
                GUI.color = new Color(0.55f, 0.95f, 0.6f, alpha);
                for (int i = 0; i < 4; i++)
                {
                    float x = c.x - s * 0.4f + i * s * 0.25f;
                    Line(new Vector2(x, c.y - s / 2f), new Vector2(x + s * 0.06f, c.y + s / 2f), 2.2f);
                }
                Line(new Vector2(c.x - s * 0.55f, c.y + s * 0.3f), new Vector2(c.x + s * 0.55f, c.y - s * 0.3f), 2.2f);
            }
        }

        /// <summary>Ironbark: bark plates growing up both sides of the view.</summary>
        private static void DrawBark(float k, float t, float calm)
        {
            float w = UiScale.W, h = UiScale.H, bw = w * 0.16f * Ease(k);
            GUI.color = new Color(0.55f, 0.38f, 0.22f, 0.95f * calm);
            GUI.DrawTexture(new Rect(0, 0, bw, h), bark, ScaleMode.StretchToFill);
            GUI.DrawTextureWithTexCoords(new Rect(w - bw, 0, bw, h), bark, new Rect(1, 0, -1, 1));
        }

        // ------------------------------------------------------------------------------------------------------------
        // Painting (once)
        // ------------------------------------------------------------------------------------------------------------

        private static Texture2D PaintBlob()
        {
            var cv = new Canvas(64);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                    float a = Mathf.Clamp01(1f - d);
                    cv.Px[y * 64 + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
                }
            return cv.Bake();
        }

        /// <summary>One angular rune: a stave with seeded branches, and a soft glow round it.</summary>
        private static Texture2D PaintRune(int g)
        {
            var cv = new Canvas(64);
            var glow = new Color(1f, 1f, 1f, 0.18f);
            var top = new Vector2(32f, 56f);
            var bottom = new Vector2(32f, 8f);
            cv.Stroke(top, bottom, 6f, glow);
            var pts = new Vector2[4];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new Vector2(12f + 40f * Hash(g, i, 41), 10f + 44f * Hash(g, i + 9, 41));
            for (int i = 0; i < pts.Length - 1; i++) cv.Stroke(pts[i], pts[i + 1], 6f, glow);
            cv.Stroke(top, bottom, 2.4f, Color.white);
            for (int i = 0; i < pts.Length - 1; i++) cv.Stroke(pts[i], pts[i + 1], 2.4f, Color.white);
            return cv.Bake();
        }

        /// <summary>An eyelid: dark at the top edge, a curved soft lower edge.</summary>
        private static Texture2D PaintLid()
        {
            var cv = new Canvas(256);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float u = x / 255f * 2f - 1f;
                    float edge = 0.15f + 0.55f * (1f - u * u); // lower edge curves down in the middle
                    float v = 1f - y / 255f;                   // 0 at the top of the image (GUI draws y down: texture row 255 is top)
                    float a = Mathf.Clamp01((edge - v) * 6f + 0.5f);
                    cv.Px[y * 256 + x] = new Color(1f, 1f, 1f, a);
                }
            return cv.Bake();
        }

        private static Texture2D PaintSpiral()
        {
            var cv = new Canvas(Size);
            float c = Size / 2f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy) / c;
                    float ang = Mathf.Atan2(dy, dx);
                    float arm = Mathf.Sin(ang * 3f + r * 14f);
                    float a = Mathf.Clamp01(arm * 1.5f - 0.2f) * Mathf.Clamp01(r * 2.5f - 0.35f) * Mathf.Clamp01((1f - r) * 3f);
                    cv.Px[y * Size + x] = new Color(1f, 1f, 1f, a);
                }
            return cv.Bake();
        }

        /// <summary>A soft ring hugging the edges of the view (clear in the middle).</summary>
        private static Texture2D PaintRing()
        {
            var cv = new Canvas(256);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float u = x / 255f * 2f - 1f, v = y / 255f * 2f - 1f;
                    float d = Mathf.Sqrt(u * u * 0.9f + v * v * 0.9f);
                    float a = Mathf.Clamp01((d - 0.72f) * 3.2f);
                    cv.Px[y * 256 + x] = new Color(1f, 1f, 1f, a * a);
                }
            return cv.Bake();
        }

        /// <summary>Glass cracks spreading in from the corners and edges.</summary>
        private static Texture2D PaintCracks()
        {
            var cv = new Canvas(Size);
            var starts = new[] { new Vector2(0, 0), new Vector2(Size, 0), new Vector2(0, Size), new Vector2(Size, Size), new Vector2(Size * 0.5f, 0), new Vector2(0, Size * 0.5f), new Vector2(Size, Size * 0.55f) };
            for (int s = 0; s < starts.Length; s++)
                for (int branch = 0; branch < 4; branch++)
                {
                    var p = starts[s];
                    var toC = (new Vector2(Size / 2f, Size / 2f) - p).normalized;
                    float ang = Mathf.Atan2(toC.y, toC.x) + (Hash(s, branch, 51) - 0.5f) * 1.4f;
                    for (int seg = 0; seg < 7; seg++)
                    {
                        ang += (Hash(s * 10 + branch, seg, 52) - 0.5f) * 0.9f;
                        var q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (18f + 20f * Hash(s, seg, 53));
                        cv.Stroke(p, q, 1.6f - seg * 0.15f, new Color(1f, 1f, 1f, 1f - seg / 8f));
                        p = q;
                        if ((p - new Vector2(Size / 2f, Size / 2f)).magnitude < Size * 0.28f) break;
                    }
                }
            return cv.Bake();
        }

        /// <summary>Light rays fanning down from above (bright at the top, fading out below).</summary>
        private static Texture2D PaintRays()
        {
            var cv = new Canvas(256);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float fromTop = 1f - y / 255f;                // 0 at the top
                    float u = (x / 255f - 0.5f) / Mathf.Max(0.15f, 0.25f + fromTop);
                    float beams = 0.5f + 0.5f * Mathf.Sin(u * 40f + Noise(u * 6f, 0f, 61) * 4f);
                    float a = Mathf.Pow(beams, 3f) * Mathf.Clamp01(1f - fromTop * 1.1f) * Mathf.Clamp01(1f - Mathf.Abs(x / 255f - 0.5f) * 1.6f);
                    cv.Px[y * 256 + x] = new Color(1f, 1f, 1f, a);
                }
            return cv.Bake();
        }

        /// <summary>A hexagon lattice, strong at the edges, gone in the middle.</summary>
        private static Texture2D PaintLattice()
        {
            var cv = new Canvas(Size);
            float r = 22f;
            float hw = r * Mathf.Sqrt(3f);
            for (int row = -1; row < Size / (r * 1.5f) + 2; row++)
                for (int col = -1; col < Size / hw + 2; col++)
                {
                    var c = new Vector2(col * hw + (row % 2 == 0 ? 0f : hw / 2f), row * r * 1.5f);
                    float edge = Mathf.Max(Mathf.Abs(c.x / Size - 0.5f), Mathf.Abs(c.y / Size - 0.5f)) * 2f;
                    float a = Mathf.Clamp01((edge - 0.55f) * 2.5f);
                    if (a <= 0f) continue;
                    for (int i = 0; i < 6; i++)
                    {
                        float a0 = Mathf.PI / 6f + i * Mathf.PI / 3f, a1 = a0 + Mathf.PI / 3f;
                        cv.Stroke(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, 1.3f, new Color(1f, 1f, 1f, a));
                    }
                }
            return cv.Bake();
        }

        /// <summary>Bark: long fibrous plates running up and down, darker in the furrows.</summary>
        private static Texture2D PaintBark()
        {
            var cv = new Canvas(256);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float u = x / 255f;
                    float fibre = Fbm(x / 9f, y / 60f, 71);
                    float furrow = Mathf.Abs(Mathf.Sin(x / 255f * 18f + Fbm(x / 30f, y / 40f, 72) * 4f));
                    float shade = 0.55f + 0.45f * fibre * furrow;
                    float a = Mathf.Clamp01((1f - u) * 1.6f) * Mathf.Clamp01(0.4f + furrow);
                    cv.Px[y * 256 + x] = new Color(shade, shade * 0.85f, shade * 0.7f, a);
                }
            return cv.Bake();
        }

        /// <summary>A twisted band of thorny stems along the top.</summary>
        private static Texture2D PaintThorns()
        {
            int wTex = Size, hTex = Size;
            var cv = new Canvas(Size);
            for (int strand = 0; strand < 3; strand++)
            {
                Vector2 prev = new Vector2(0, hTex * (0.42f + 0.08f * strand));
                for (int x = 8; x <= wTex; x += 8)
                {
                    var p = new Vector2(x, hTex * (0.4f + 0.08f * strand) + Mathf.Sin(x * 0.03f + strand * 2f) * 40f);
                    cv.Stroke(prev, p, 7f, new Color(0.35f, 0.2f, 0.15f, 1f));
                    if (Hash(x, strand, 81) > 0.6f)
                    {
                        var dir = new Vector2(Hash(x, 1, 82) - 0.5f, Hash(x, 2, 82) > 0.5f ? 1f : -1f).normalized;
                        cv.Stroke(p, p + dir * 36f, 3f, new Color(0.55f, 0.35f, 0.25f, 1f));
                    }
                    prev = p;
                }
            }
            return cv.Bake();
        }
    }
}
