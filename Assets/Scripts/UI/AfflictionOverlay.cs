using Deepbound.Art;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// Afflictions you see rather than read (2026-10-28, the user: "instead of 'you have been rooted' wrap vines
    /// around the player's vision"). Each one creeps in from the edges of the screen as its own picture, grows in
    /// as it takes hold and withers away as it lets go:
    /// <list type="bullet">
    /// <item>Entangled - thorned vines climb up from the bottom of your view.</item>
    /// <item>Rooted (ice) and Frostbitten - frost rime spreads in from the corners.</item>
    /// <item>Poisoned - green veins and sickly haze.</item>
    /// <item>Cursed - violet smoke curls at the edges; Mania - magenta smoke that churns fast.</item>
    /// <item>Silenced - grey-violet haze, and the world goes muffled (a low-pass on the listener).</item>
    /// <item>Dazed - little stars circle over the crosshair and your ears ring.</item>
    /// </list>
    /// Potions too (2026-10-28, "do potion effects also"): a health potion warms the edges red-gold with sparks rising
    /// while it mends; the class draught surges in its bar's colour; Holy Water's cleanse washes the view white-gold
    /// and wipes every affliction picture at once; Ironbark grows bark plates at the edges, Bloodfury beats blood-red,
    /// Grave Stillness cracks the view to stone, a Sanctuary circle glows gold.
    /// Every texture is generated here at startup (no art files). Drawn behind the HUD; faded under Reduce Flashing.
    /// </summary>
    public class AfflictionOverlay : MonoBehaviour
    {
        private const int Size = 512;
        private static Texture2D vines, frost, veins, smoke, star, ink;
        private float vineK, frostK, poisonK, curseK, maniaK, silenceK, dazeK, blindK, hexK;
        private float mendK, surgeK, barkK, furyK, stoneK, sanctK, lastStandK;
        private static float gulpAt = -10f, cleanseAt = -10f;
        private static Color gulpColor = Color.white;
        private Color surgeColor = Color.white;
        private float nextSpark;
        private struct Spark { public Vector2 P, V; public float Born, Life, Size; public Color C; }
        private readonly System.Collections.Generic.List<Spark> sparks = new System.Collections.Generic.List<Spark>();
        private static AfflictionOverlay instance;

        /// <summary>Owner, a potion drunk: 0 health, 1 the class draught.</summary>
        public static void Drank(int kind, Color c) { gulpAt = Time.time; gulpColor = c; }

        /// <summary>Owner: Holy Water (or anything) cleansed you: a white-gold wash, and the pictures go at once.</summary>
        public static void Cleansed()
        {
            cleanseAt = Time.time;
            if (instance == null) return;
            instance.vineK = instance.frostK = instance.poisonK = instance.curseK = instance.maniaK = instance.silenceK = instance.dazeK = instance.blindK = instance.hexK = 0f;
        }

        private void Awake() => instance = this;
        private bool wasDazed;
        private AudioLowPassFilter muffle;
        private float nextListenerCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("AfflictionOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<AfflictionOverlay>();
        }

        /// <summary>For tests: how far each picture has grown in (0..1).</summary>
        public static float VineLevel, FrostLevel, SmokeLevel, DazeLevel;

        private void Update()
        {
            var me = Player.NetworkPlayer.Local;
            var deb = me != null ? me.GetComponent<Player.PlayerDebuffs>() : null;
            bool on = deb != null && !Application.isBatchMode;
            float dt = Time.deltaTime;
            Approach(ref vineK, on && deb.Entangled, dt, 2.6f, 1.6f);
            Approach(ref frostK, on && (deb.Frostbitten || (deb.Rooted && !deb.Entangled)), dt, 2.2f, 1.2f);
            Approach(ref poisonK, on && deb.Poisoned, dt, 2.4f, 1.4f);
            Approach(ref curseK, on && deb.Cursed, dt, 2f, 1.2f);
            Approach(ref maniaK, on && deb.Manic, dt, 4f, 1.6f);
            Approach(ref silenceK, on && deb.Silenced, dt, 3f, 1.5f);
            Approach(ref dazeK, on && deb.Dazed, dt, 5f, 1.8f);
            Approach(ref blindK, on && deb.Blinded, dt, 5f, 1.4f);
            Approach(ref hexK, on && deb.Accursed, dt, 2.5f, 1.2f);
            var pouch = me != null ? me.GetComponent<Player.Flasks>() : null;
            bool pon = pouch != null && !Application.isBatchMode;
            Approach(ref mendK, pon && pouch.Healing, dt, 4f, 1.5f);
            Approach(ref surgeK, pon && pouch.Surging, dt, 5f, 2f);
            Approach(ref barkK, pon && pouch.Ironbark, dt, 3f, 1.5f);
            Approach(ref furyK, pon && pouch.Bloodfury, dt, 4f, 1.5f);
            Approach(ref stoneK, pon && pouch.InStasis, dt, 6f, 3f);
            Approach(ref sanctK, pon && Player.Flasks.InSanctuary(me.transform.position), dt, 3f, 2f);
            Approach(ref lastStandK, me != null && !Application.isBatchMode && Player.Teamwork.LocalLastStand, dt, 2f, 1.5f);
            if (pon && pouch.Surging) surgeColor = Player.Flasks.DraughtColor(Player.Flasks.LocalClass);
            // Sparks rising up the edges while a potion works.
            if ((mendK > 0.3f || surgeK > 0.3f) && Time.time >= nextSpark)
            {
                nextSpark = Time.time + 0.05f;
                float h = UiScale.H, w = UiScale.W;
                bool left = Random.value < 0.5f;
                var col = surgeK > mendK ? surgeColor : new Color(1f, Random.Range(0.45f, 0.8f), 0.3f);
                sparks.Add(new Spark { P = new Vector2(left ? Random.Range(0f, w * 0.18f) : w - Random.Range(0f, w * 0.18f), h + 4f), V = new Vector2(Random.Range(-20f, 20f), -Random.Range(h * 0.25f, h * 0.45f)), Born = Time.time, Life = Random.Range(0.8f, 1.4f), Size = h * Random.Range(0.006f, 0.013f), C = col });
            }
            for (int i = sparks.Count - 1; i >= 0; i--) if (Time.time - sparks[i].Born > sparks[i].Life) sparks.RemoveAt(i);
            VineLevel = vineK; FrostLevel = frostK; SmokeLevel = Mathf.Max(curseK, maniaK, silenceK, poisonK); DazeLevel = dazeK;

            // Dazed: a ringing in the ears as it lands.
            bool dazed = on && deb.Dazed;
            if (dazed && !wasDazed && ViewCamera.Current != null) Sfx.Play(Sound.Toll, ViewCamera.Current.transform.position, 0.22f, 2.7f);
            wasDazed = dazed;

            // Silenced (and, less, dazed): the world goes muffled.
            if (Time.time >= nextListenerCheck)
            {
                nextListenerCheck = Time.time + 1f;
                var listener = FindFirstObjectByType<AudioListener>();
                if (listener != null && (muffle == null || muffle.gameObject != listener.gameObject))
                    muffle = listener.GetComponent<AudioLowPassFilter>() ?? listener.gameObject.AddComponent<AudioLowPassFilter>();
            }
            if (muffle != null)
            {
                float cut = Mathf.Lerp(22000f, 1100f, silenceK);
                cut = Mathf.Min(cut, Mathf.Lerp(22000f, 3500f, dazeK));
                muffle.cutoffFrequency = cut;
                muffle.enabled = cut < 21500f;
            }
        }

        private static void Approach(ref float k, bool on, float dt, float up, float down) =>
            k = Mathf.MoveTowards(k, on ? 1f : 0f, dt * (on ? up : down));

        private static int paintedFrame = -1;

        /// <summary>
        /// Called at the top of the HUD's and the minimap's OnGUI: the first caller each frame paints the overlay, so it
        /// lies under both (they skip the layout pass, and then IMGUI keeps no depth order between scripts).
        /// </summary>
        public static void PaintOnce()
        {
            if (instance == null || Event.current.type != EventType.Repaint || paintedFrame == Time.frameCount) return;
            paintedFrame = Time.frameCount;
            UiSkin.Apply();
            var keep = GUI.color;
            var keepMatrix = GUI.matrix;
            instance.Paint();
            GUI.color = keep;
            GUI.matrix = keepMatrix;
        }

        private void Paint()
        {
            bool flashes = Time.time - gulpAt < 0.9f || Time.time - cleanseAt < 0.8f;
            if (vineK + frostK + poisonK + curseK + maniaK + silenceK + dazeK + blindK + hexK + mendK + surgeK + barkK + furyK + stoneK + sanctK + lastStandK < 0.001f && !flashes && sparks.Count == 0) return;
            var me = Player.NetworkPlayer.Local;
            if (me == null || PauseMenu.Open || GameHud.Cinematic) return;
            if (me.TryGetComponent<Player.PlayerLife>(out var life) && life.Downed.Value) return;
            EnsureTextures();
            float w = UiScale.W, h = UiScale.H, t = Time.time;
            float calm = Settings.ReduceFlashing ? 0.6f : 1f;
            var old = GUI.color;

            // The potions' own glows, under everything else.
            if (sanctK > 0f) Corners(smoke, h * 0.7f, sanctK, new Color(1f, 0.85f, 0.45f, 0.35f * calm), t * 0.5f, 1f);
            if (mendK > 0f) Corners(smoke, h * 0.6f, mendK, new Color(1f, 0.42f, 0.28f, (0.32f + 0.12f * Mathf.Sin(t * 6f)) * calm), t * 0.7f, 1f);
            if (surgeK > 0f) Corners(smoke, h * 0.65f, surgeK, new Color(surgeColor.r, surgeColor.g, surgeColor.b, (0.4f + 0.15f * Mathf.Sin(t * 10f)) * calm), t * 2f, 2f);
            if (furyK > 0f)
            {
                float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(t * 3.4f)), 6f);
                Corners(smoke, h * (0.7f + 0.08f * beat), furyK, new Color(0.85f, 0.05f, 0.05f, (0.38f + 0.3f * beat) * calm), t, 1.5f);
            }
            if (lastStandK > 0f)
            {
                // Last stand: a slow, heavy red pulse at the edges.
                float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(t * 2.2f)), 4f);
                Corners(smoke, h * (0.6f + 0.06f * beat), lastStandK, new Color(0.7f, 0.08f, 0.06f, (0.3f + 0.22f * beat) * calm), t * 0.4f, 1f);
            }
            float gulp = Mathf.Clamp01(1f - (t - gulpAt) / 0.9f);
            if (gulp > 0f) Corners(smoke, h * (0.45f + 0.35f * (1f - gulp)), gulp, new Color(gulpColor.r, gulpColor.g, gulpColor.b, 0.6f * gulp * calm), 0f, 0f);

            // Smoke and haze first (the soft layers), then the sharp pictures over them.
            if (poisonK > 0f) Corners(smoke, h * 0.62f, poisonK, new Color(0.35f, 0.75f, 0.15f, 0.55f * calm), t * 0.6f, 1.5f);
            if (curseK > 0f) Corners(smoke, h * 0.7f, curseK, new Color(0.45f, 0.15f, 0.7f, 0.7f * calm), t * 0.8f, 2f);
            // The hag's curse: a sickly green-black rot creeping in, on a slow throb.
            if (hexK > 0f) Corners(smoke, h * (0.66f + 0.04f * Mathf.Sin(t * 2.4f)), hexK, new Color(0.22f, 0.32f, 0.06f, 0.75f * calm), t * 0.5f, 1.5f);
            if (maniaK > 0f) Corners(smoke, h * 0.75f, maniaK, new Color(0.95f, 0.25f, 0.8f, (0.55f + 0.2f * Mathf.Sin(t * 9f)) * calm), t * 4f, 5f);
            if (silenceK > 0f) Corners(smoke, h * 0.75f, silenceK, new Color(0.62f, 0.62f, 0.78f, 0.55f * calm), t * 0.4f, 1f, topOnly: false);
            if (poisonK > 0f) Corners(veins, h * 0.45f, poisonK, new Color(0.42f, 0.72f, 0.2f, (0.5f + 0.12f * Mathf.Sin(t * 3f)) * calm), t, 0.6f);
            if (frostK > 0f) Corners(frost, h * 0.5f, frostK, new Color(0.8f, 0.9f, 1f, 0.6f * calm), 0f, 0f);
            if (vineK > 0f)
            {
                // The vines climb from the bottom corners and creep along the floor of your view.
                float s = h * 0.62f * Ease(vineK);
                float sway = Mathf.Sin(t * 1.3f) * 1.6f;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(vineK * 1.6f) * 0.92f);
                DrawCorner(vines, new Rect(0, h - s, s, s), new Rect(0, 0, 1, 1), sway, new Vector2(0, h));
                DrawCorner(vines, new Rect(w - s, h - s, s, s), new Rect(1, 0, -1, 1), -sway, new Vector2(w, h));
            }
            if (barkK > 0f) Corners(vines, h * 0.42f, barkK, new Color(0.55f, 0.38f, 0.22f, 0.9f * calm), 0f, 0f);
            if (stoneK > 0f)
            {
                GUI.color = new Color(0.5f, 0.5f, 0.55f, 0.25f * stoneK);
                GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
                Corners(frost, h * 0.75f, stoneK, new Color(0.35f, 0.35f, 0.38f, 0.95f * calm), 0f, 0f);
            }
            foreach (var sp in sparks)
            {
                float age = t - sp.Born, k = 1f - age / sp.Life;
                var p = sp.P + sp.V * age;
                GUI.color = new Color(sp.C.r, sp.C.g, sp.C.b, k * 0.9f * calm);
                GUI.DrawTexture(new Rect(p.x - sp.Size, p.y - sp.Size, sp.Size * 2f, sp.Size * 2f), star);
            }
            PaintBlind(w, h, t);
            float wash = Mathf.Clamp01(1f - (t - cleanseAt) / 0.8f);
            if (wash > 0f)
            {
                GUI.color = new Color(1f, 0.95f, 0.75f, 0.4f * wash * wash * calm);
                GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            }
            if (dazeK > 0f)
            {
                // Stars circling where your head rings, just over the crosshair.
                GUI.color = new Color(1f, 0.92f, 0.6f, dazeK * calm);
                var c = new Vector2(w * 0.5f, h * 0.5f - h * 0.17f);
                for (int i = 0; i < 5; i++)
                {
                    float a = t * 2.6f + i * Mathf.PI * 2f / 5f;
                    var p = c + new Vector2(Mathf.Cos(a) * h * 0.09f, Mathf.Sin(a) * h * 0.025f);
                    float ss = h * (0.022f + 0.008f * Mathf.Sin(a)) * (0.6f + 0.4f * dazeK);
                    var pivot = p;
                    var keep = GUI.matrix;
                    UiScale.Rotate(t * 140f + i * 50f, pivot);
                    GUI.DrawTexture(new Rect(p.x - ss / 2f, p.y - ss / 2f, ss, ss), star);
                    GUI.matrix = keep;
                }
            }
            GUI.color = old;
        }

        private static float Ease(float k) => 1f - (1f - k) * (1f - k) * (1f - k);

        /// <summary>One picture, mirrored into all four corners, growing out of each as <paramref name="k"/> rises.</summary>
        private static void Corners(Texture2D tex, float size, float k, Color tint, float spin, float swayDeg, bool topOnly = false)
        {
            float w = UiScale.W, h = UiScale.H;
            float s = size * Ease(k);
            GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * Mathf.Clamp01(k * 1.5f));
            float sway = Mathf.Sin(spin) * swayDeg;
            DrawCorner(tex, new Rect(0, h - s, s, s), new Rect(0, 0, 1, 1), sway, new Vector2(0, h));
            DrawCorner(tex, new Rect(w - s, h - s, s, s), new Rect(1, 0, -1, 1), -sway, new Vector2(w, h));
            DrawCorner(tex, new Rect(0, 0, s, s), new Rect(0, 1, 1, -1), -sway, new Vector2(0, 0));
            DrawCorner(tex, new Rect(w - s, 0, s, s), new Rect(1, 1, -1, -1), sway, new Vector2(w, 0));
        }

        private static void DrawCorner(Texture2D tex, Rect r, Rect uv, float degrees, Vector2 pivot)
        {
            var keep = GUI.matrix;
            if (Mathf.Abs(degrees) > 0.01f) UiScale.Rotate(degrees, pivot);
            GUI.DrawTextureWithTexCoords(r, tex, uv);
            GUI.matrix = keep;
        }

        // ------------------------------------------------------------------
        // The pictures, painted once in code
        // ------------------------------------------------------------------

        /// <summary>
        /// BLINDED (the Ink Lamprey, 2026-11-13): ink over the eyes to a small clear hole round the crosshair. Drawn over
        /// the other pictures; it closes in as it lands and drifts a little.
        /// </summary>
        private void PaintBlind(float w, float h, float t)
        {
            if (blindK <= 0f) return;
            ink ??= PaintInk();
            float k = Ease(blindK);
            float s = Mathf.Max(w, h) * Mathf.Lerp(4.5f, 1.25f, k);
            var c = new Vector2(w * 0.5f + Mathf.Sin(t * 0.7f) * h * 0.015f, h * 0.5f + Mathf.Cos(t * 0.53f) * h * 0.012f);
            GUI.color = new Color(0.04f, 0.02f, 0.07f, 0.97f * Mathf.Clamp01(blindK * 1.4f));
            var keep = GUI.matrix;
            UiScale.Rotate(t * 6f, c);
            GUI.DrawTexture(new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s), ink);
            GUI.matrix = keep;
        }

        private static Texture2D PaintInk()
        {
            var cv = new Canvas(Size);
            float half = Size * 0.5f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x - half) / Size, dy = (y - half) / Size;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = Fbm(x / 40f, y / 40f, 37);
                    // Ink runs in tendrils toward the middle: the edge of the hole is ragged.
                    // (First seen 2026-11-13: a hole of 0.15-0.35 left most of the screen clear. Now clear to ~0.08, black by ~0.2.)
                    float edge = 0.075f + (n - 0.5f) * 0.06f;
                    float a = Mathf.Clamp01((d - edge) / 0.12f);
                    cv.Px[y * Size + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
                }
            return cv.Bake();
        }

        private static void EnsureTextures()
        {
            if (vines != null) return;
            vines = PaintVines();
            frost = PaintFrost();
            veins = PaintVeins();
            smoke = PaintSmoke();
            star = PaintStar();
        }

        /// <summary>A premultiplied-free RGBA float canvas with soft strokes and blobs ("over" blending).</summary>
        private class Canvas
        {
            public readonly int N;
            public readonly Color[] Px;
            public Canvas(int n) { N = n; Px = new Color[n * n]; }

            public void Over(int x, int y, Color c)
            {
                if (x < 0 || y < 0 || x >= N || y >= N || c.a <= 0.001f) return;
                int i = y * N + x;
                var d = Px[i];
                float a = c.a + d.a * (1f - c.a);
                if (a <= 0f) return;
                Px[i] = new Color((c.r * c.a + d.r * d.a * (1f - c.a)) / a, (c.g * c.a + d.g * d.a * (1f - c.a)) / a, (c.b * c.a + d.b * d.a * (1f - c.a)) / a, a);
            }

            /// <summary>A round-capped line, soft at its edge.</summary>
            public void Stroke(Vector2 a, Vector2 b, float r, Color c)
            {
                int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - r - 2), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + r + 2);
                int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - r - 2), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + r + 2);
                var ab = b - a;
                float len2 = Mathf.Max(1e-4f, ab.sqrMagnitude);
                for (int y = Mathf.Max(0, y0); y <= Mathf.Min(N - 1, y1); y++)
                    for (int x = Mathf.Max(0, x0); x <= Mathf.Min(N - 1, x1); x++)
                    {
                        var p = new Vector2(x, y);
                        float tt = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                        float d = Vector2.Distance(p, a + ab * tt);
                        float k = Mathf.Clamp01(r + 0.75f - d);
                        if (k > 0f) Over(x, y, new Color(c.r, c.g, c.b, c.a * k));
                    }
            }

            /// <summary>A leaf: an ellipse along <paramref name="dir"/>.</summary>
            public void Leaf(Vector2 at, Vector2 dir, float len, float wid, Color c)
            {
                dir.Normalize();
                var side = new Vector2(-dir.y, dir.x);
                int R = Mathf.CeilToInt(len + 2);
                for (int y = -R; y <= R; y++)
                    for (int x = -R; x <= R; x++)
                    {
                        var p = new Vector2(x, y);
                        float u = Vector2.Dot(p, dir) / len, v = Vector2.Dot(p, side) / wid;
                        float e = u * u + v * v;
                        if (e > 1f) continue;
                        float shade = 0.75f + 0.25f * Mathf.Clamp01(v + 0.3f);
                        Over(Mathf.RoundToInt(at.x + x), Mathf.RoundToInt(at.y + y), new Color(c.r * shade, c.g * shade, c.b * shade, c.a * Mathf.Clamp01((1f - e) * 4f)));
                    }
            }

            public Texture2D Bake()
            {
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                tex.SetPixels(Px);
                tex.Apply(false, true);
                return tex;
            }
        }

        private static float Hash(int x, int y, int s) { unchecked { int h = x * 374761393 + y * 668265263 + s * 982451653; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; } }

        private static float Noise(float x, float y, int s)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Mathf.Lerp(Hash(xi, yi, s), Hash(xi + 1, yi, s), fx), b = Mathf.Lerp(Hash(xi, yi + 1, s), Hash(xi + 1, yi + 1, s), fx);
            return Mathf.Lerp(a, b, fy);
        }

        private static float Fbm(float x, float y, int s) => Noise(x, y, s) * 0.55f + Noise(x * 2.1f, y * 2.1f, s + 7) * 0.3f + Noise(x * 4.3f, y * 4.3f, s + 13) * 0.15f;

        private static Texture2D PaintVines()
        {
            var cv = new Canvas(Size);
            var rng = new System.Random(4207);
            float R() => (float)rng.NextDouble();
            var bark = new Color(0.2f, 0.27f, 0.1f, 1f);
            var dark = new Color(0.05f, 0.07f, 0.02f, 0.95f);
            var leaf = new Color(0.24f, 0.42f, 0.13f, 1f);
            var thorn = new Color(0.55f, 0.5f, 0.32f, 1f);

            // Stems are gathered first and drawn in passes (all outlines, then all bark), so a stem reads as one
            // smooth cord rather than a string of beads; thorns and leaves go on top.
            var stems = new System.Collections.Generic.List<(Vector2 a, Vector2 b, float w)>();
            var thorns = new System.Collections.Generic.List<(Vector2 a, Vector2 b)>();
            var leaves = new System.Collections.Generic.List<(Vector2 at, Vector2 dir, float len, float wid)>();
            void Grow(Vector2 at, float angle, float width, int steps, int depth)
            {
                float phase = R() * 6.28f;
                for (int i = 0; i < steps && width > 0.8f; i++)
                {
                    angle += Mathf.Sin(phase + i * 0.35f) * 0.16f + (R() - 0.5f) * 0.12f;
                    float step = 7f + R() * 3f;
                    var next = at + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * step;
                    stems.Add((at, next, width));
                    var dir = next - at;
                    var side = new Vector2(-dir.y, dir.x).normalized;
                    if (i % 4 == 1)
                    {
                        float sgn = (i / 4) % 2 == 0 ? 1f : -1f;
                        thorns.Add((next + side * sgn * width * 0.8f, next + side * sgn * (width + 5f) + dir.normalized * 3f));
                    }
                    if (i % 6 == 3 && width > 2f)
                    {
                        float sgn = R() < 0.5f ? 1f : -1f;
                        var ld = (side * sgn + dir.normalized * 0.6f).normalized;
                        leaves.Add((next + ld * (width + 8f), ld, 11f + R() * 6f, 5f + R() * 2f));
                    }
                    if (depth < 2 && R() < 0.07f) Grow(next, angle + (R() < 0.5f ? 0.8f : -0.8f), width * 0.6f, steps / 2, depth + 1);
                    at = next;
                    width *= 0.975f;
                }
            }

            // Up from the bottom edge and in from the side, leaning toward the middle of the view.
            for (int k = 0; k < 5; k++) Grow(new Vector2(R() * Size * 0.85f, -4f), Mathf.Deg2Rad * (55f + R() * 35f), 11f + R() * 4f, 34 + rng.Next(18), 0);
            for (int k = 0; k < 3; k++) Grow(new Vector2(-4f, R() * Size * 0.6f), Mathf.Deg2Rad * (10f + R() * 40f), 9f + R() * 3f, 26 + rng.Next(14), 0);
            foreach (var st in stems) cv.Stroke(st.a, st.b, st.w + 1.8f, dark);
            foreach (var st in stems) cv.Stroke(st.a, st.b, st.w, bark);
            // A lighter ridge down each stem, so they look round.
            foreach (var st in stems) if (st.w > 3f) cv.Stroke(st.a, st.b, st.w * 0.3f, new Color(0.34f, 0.42f, 0.18f, 0.7f));
            foreach (var th in thorns) cv.Stroke(th.a, th.b, 1.2f, thorn);
            foreach (var lf in leaves) cv.Leaf(lf.at, lf.dir, lf.len, lf.wid, leaf);
            return cv.Bake();
        }

        private static Texture2D PaintFrost()
        {
            var cv = new Canvas(Size);
            var rng = new System.Random(911);
            float R() => (float)rng.NextDouble();
            // A pale rime fading out from the corner, broken up by noise.
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = Mathf.Sqrt(x * x + y * y) / Size;
                    float n = Fbm(x / 40f, y / 40f, 3);
                    float a = Mathf.Clamp01((0.75f - d) * 1.6f) * (0.35f + 0.5f * n);
                    cv.Px[y * Size + x] = new Color(0.82f, 0.92f, 1f, a * 0.55f);
                }
            var glow = new Color(0.7f, 0.88f, 1f, 0.22f);
            var core = new Color(0.95f, 0.99f, 1f, 0.9f);
            void Dendrite(Vector2 at, float angle, float len, int depth)
            {
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var end = at + dir * len;
                cv.Stroke(at, end, 3f - depth * 0.6f, glow);
                cv.Stroke(at, end, Mathf.Max(0.5f, 1.1f - depth * 0.3f), core);
                if (depth >= 2) return;
                for (float s = 14f; s < len; s += 12f + R() * 10f)
                {
                    var p = at + dir * s;
                    float sub = (len - s) * (0.32f + R() * 0.15f);
                    Dendrite(p, angle + 1.05f, sub, depth + 1);
                    Dendrite(p, angle - 1.05f, sub, depth + 1);
                }
            }
            for (int k = 0; k < 5; k++) Dendrite(new Vector2(-2f, -2f), Mathf.Deg2Rad * (8f + k * 18f + R() * 8f), 130f + R() * 150f, 0);
            return cv.Bake();
        }

        private static Texture2D PaintVeins()
        {
            var cv = new Canvas(Size);
            var rng = new System.Random(77);
            float R() => (float)rng.NextDouble();
            var col = new Color(0.55f, 1f, 0.35f, 0.9f);
            var halo = new Color(0.3f, 0.7f, 0.1f, 0.25f);
            void Vein(Vector2 at, float angle, float width, int steps, int depth)
            {
                for (int i = 0; i < steps; i++)
                {
                    angle += (R() - 0.5f) * 0.5f;
                    var next = at + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (6f + R() * 4f);
                    cv.Stroke(at, next, width + 3f, halo);
                    cv.Stroke(at, next, width, col);
                    if (depth < 3 && R() < 0.12f) Vein(next, angle + (R() - 0.5f) * 1.6f, width * 0.65f, steps / 2, depth + 1);
                    at = next;
                    width = Mathf.Max(0.6f, width * 0.96f);
                }
            }
            for (int k = 0; k < 6; k++) Vein(new Vector2(-2f, R() * Size * 0.3f), Mathf.Deg2Rad * (R() * 80f), 3f, 30 + rng.Next(15), 0);
            // Drips gathering on the edge.
            for (int k = 0; k < 10; k++)
            {
                var at = new Vector2(R() * Size * 0.6f, 4f);
                float len = 20f + R() * 60f;
                cv.Stroke(at, at + Vector2.up * len, 2.2f + R() * 1.5f, new Color(0.45f, 0.85f, 0.2f, 0.75f));
                cv.Leaf(at + Vector2.up * len, Vector2.up, 6f, 4.5f, new Color(0.5f, 0.95f, 0.25f, 0.85f));
            }
            return cv.Bake();
        }

        private static Texture2D PaintSmoke()
        {
            var cv = new Canvas(Size);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = Mathf.Sqrt(x * x + y * y) / Size;
                    float n = Fbm(x / 55f, y / 55f, 21);
                    float wisps = Mathf.Clamp01(Fbm(x / 22f + n * 3f, y / 22f, 5) * 1.6f - 0.4f);
                    float a = Mathf.Clamp01((0.95f - d) * 1.3f) * Mathf.Clamp01(n * 1.3f - 0.15f + wisps * 0.4f);
                    cv.Px[y * Size + x] = new Color(1f, 1f, 1f, a * a);
                }
            return cv.Bake();
        }

        private static Texture2D PaintStar()
        {
            var cv = new Canvas(64);
            var c = new Vector2(32f, 32f);
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI * 2f / 5f;
                cv.Stroke(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 27f, 4.5f, new Color(1f, 0.95f, 0.75f, 1f));
            }
            cv.Stroke(c, c, 8f, Color.white);
            return cv.Bake();
        }
    }
}
