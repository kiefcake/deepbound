using Deepbound.World;
using UnityEngine;

namespace Deepbound.UI
{
    public class LoadingScreen : MonoBehaviour
    {
        public enum Kind { Descent, Camp, Joining, Hosting }

        public static bool AllowInTests;

        private static LoadingScreen instance;
        private static Kind kind;
        private static float beginAt = -1f, visibleAt, builtAt = -1f, endAt = -1f;
        private static string line;
        private static int lineIndex = -1;
        private Texture2D ringOuter, ringInner, glow;

        public const float MinShow = 2.4f, FadeIn = 0.35f, FadeOut = 0.7f, GiveUp = 25f;

        public static void Begin(Kind k, float delay)
        {
            if (Networking.NetworkLauncher.TestRun && !AllowInTests) return;
            if (instance == null) instance = new GameObject("LoadingScreen").AddComponent<LoadingScreen>();
            kind = k;
            beginAt = Time.time;
            visibleAt = Time.time + Mathf.Max(0f, delay);
            builtAt = -1f;
            endAt = -1f;
            lineIndex = (lineIndex + 1 + Random.Range(0, Lines.Length - 1)) % Lines.Length;
            line = Lines[lineIndex];
        }

        public static void Cancel() => beginAt = -1f;

        public static bool Active => beginAt >= 0f;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            DungeonDirector.ArenaBuilt += OnBuilt;
        }

        private void OnDestroy() => DungeonDirector.ArenaBuilt -= OnBuilt;

        private void OnBuilt(int _)
        {
            if (beginAt < 0f || builtAt >= 0f) return;
            builtAt = Time.time;

            endAt = Mathf.Max(builtAt + 0.6f, visibleAt + (kind == Kind.Hosting ? 1.3f : MinShow));
            if (!Networking.NetworkLauncher.TestRun) GameHud.HoldStill(endAt - Time.time + 0.3f);
        }

        private void Update()
        {
            if (beginAt < 0f) return;
            bool connected = Unity.Netcode.NetworkManager.Singleton != null && (Unity.Netcode.NetworkManager.Singleton.IsClient || Unity.Netcode.NetworkManager.Singleton.IsServer);

            if (((kind == Kind.Joining || kind == Kind.Hosting) && !connected && Time.time - beginAt > 1f) || Time.time - beginAt > GiveUp) { endAt = Mathf.Min(endAt < 0f ? Time.time : endAt, Time.time); if (builtAt < 0f) builtAt = Time.time; }
            if (endAt >= 0f && Time.time >= endAt + FadeOut) beginAt = -1f;
        }

        private static readonly string[] ActNames = { "THE OSSUARY", "THE DROWNED VAULTS", "THE HOLLOW THRONE", "THE BLACK CATHEDRAL", "THE ABYSS" };
        private static readonly Color[] ActColors =
        {
            new Color(0.95f, 0.85f, 0.62f), new Color(0.4f, 0.85f, 0.9f), new Color(1f, 0.78f, 0.35f), new Color(0.72f, 0.48f, 1f), new Color(0.62f, 0.4f, 1f),
        };
        private static readonly Color CampColor = new Color(1f, 0.62f, 0.3f);

        private static readonly string[] Lines =
        {
            "“Sleep, you who were carried down. Sleep, you who carried.”  - the Cardinal's hymn",
            "“Keep the lantern lit. Keep the way home.”",
            "The Ossuary's bones do not stay down: a skeleton knocked apart drags itself back together unless the pile is ground under a heel.",
            "The river found the Drowned Vaults a hundred years after the singing stopped. The water rises and falls like a tide that has forgotten the moon.",
            "The soul lanterns of the Hollow Throne still burn with the trapped light of Ossric's courtiers.",
            "The Cardinal crowned two kings, one in secret, so that Cathedra would have a king whichever way the war went.",
            "The Black Cathedral's bell still tolls on the hour, and the toll still knocks the living off their feet.",
            "The hymn was never for the dead. It was sung so they would sleep quietly enough not to wake what the seals held.",
            "Corwin keeps his eyes shut on the way down. Forty years on the seat, and he has never seen the bottom.",
            "The Tallyman keeps a ledger of the living. Do not be on a page when he arrives.",
            "The dead hear you run. Walk, and strike first.",
            "A Warden blocks the way down on the first floor of every act; the act's master waits on the second.",
            "Rooms ring and walls muffle: fight behind a door and the next hall may not hear it.",
            "Break a pack's line of sight and they lose you. They will come looking.",
            "Embers temper your weapon at Hobb's anvil. Sigils rank your abilities at Ysolde's stones.",
            "The cleared floor is safe. Loot it at your leisure; the lift waits.",
        };

        private static string Title(out string sub, out Color tint)
        {
            var d = DungeonDirector.Instance;
            switch (kind)
            {
                case Kind.Camp:
                    sub = "THE LAST LANTERN";
                    tint = CampColor;
                    return "BACK TO CAMP";
                case Kind.Joining:
                    sub = "THE LAST LANTERN";
                    tint = CampColor;
                    return "JOINING THE PARTY";
                case Kind.Hosting:
                    sub = "THE CAMP ABOVE THE SHAFT";
                    tint = CampColor;
                    return "THE LAST LANTERN";
                default:
                {
                    int depth = d != null ? (d.RunInProgress ? d.NextDepth.Value : 0) : 0;
                    int act = DungeonDirector.Act(depth);
                    tint = ActColors[Mathf.Clamp(act, 0, ActColors.Length - 1)];
                    sub = ActNames[Mathf.Clamp(act, 0, ActNames.Length - 1)] + (DungeonDirector.IsWardenFloor(depth) ? "  ·  a Warden bars the way" : "  ·  the act's master waits")
                        + (DungeonDirector.Endless ? "  ·  ENDLESS" : "");
                    return "FLOOR " + (depth + 1) + "  -  " + DungeonTheme.FloorName(depth).ToUpperInvariant();
                }
            }
        }

        private static readonly string[] Stages = { "Carving the halls", "Laying the bones", "Hanging the torches", "Waking the dead", "Hiding the gold" };

        private void OnGUI()
        {
            if (beginAt < 0f || Time.time < visibleAt) return;
            UiSkin.Apply();
            useGUILayout = false;
            GUI.depth = -1000;
            float w = UiScale.W, h = UiScale.H;
            float a = Mathf.Clamp01((Time.time - visibleAt) / FadeIn);
            if (endAt >= 0f && Time.time > endAt) a *= 1f - Mathf.Clamp01((Time.time - endAt) / FadeOut);
            if (a <= 0f) return;
            string title = Title(out string sub, out Color tint);
            float t = Time.time - visibleAt;

            UiSkin.Rect(new Rect(0, 0, w, h), new Color(0.015f, 0.012f, 0.018f, a));
            if (glow == null) glow = MakeGlow();
            float breathe = 0.85f + 0.15f * Mathf.Sin(t * 1.3f);
            var gc = new Color(tint.r, tint.g, tint.b, 0.16f * a * breathe);
            GUI.color = gc;
            float gs = Mathf.Max(w, h) * 1.1f;
            GUI.DrawTexture(new Rect(w / 2f - gs / 2f, h * 0.42f - gs / 2f, gs, gs), glow);
            GUI.color = Color.white;

            for (int i = 0; i < 46; i++)
            {
                float seed = i * 12.9898f;
                float speed = 0.05f + Frac(seed * 0.71f) * 0.09f;
                float k = Frac(t * speed + Frac(seed));
                float x = w * Frac(seed * 3.17f) + Mathf.Sin(t * (0.6f + Frac(seed) * 0.8f) + i) * 18f;
                float y = h * (1.05f - k * 1.1f);
                float s = 2f + Frac(seed * 5.3f) * 3f;
                float ea = Mathf.Sin(k * Mathf.PI) * a * (0.35f + 0.5f * Frac(seed * 7.1f));
                UiSkin.Rect(new Rect(x, y, s, s), new Color(Mathf.Lerp(tint.r, 1f, 0.4f), Mathf.Lerp(tint.g, 0.6f, 0.3f), tint.b * 0.5f, ea));
            }

            if (ringOuter == null) { ringOuter = MakeRing(256, 0.46f, 0.5f, 24, 0.42f); ringInner = MakeRing(256, 0.3f, 0.34f, 12, 0.24f); }
            var c = new Vector2(w / 2f, h * 0.4f);
            float rs = Mathf.Min(w, h) * 0.3f;
            var m = GUI.matrix;
            GUI.color = new Color(tint.r, tint.g, tint.b, 0.7f * a);
            UiScale.Rotate(t * 14f, c);
            GUI.DrawTexture(new Rect(c.x - rs / 2f, c.y - rs / 2f, rs, rs), ringOuter);
            GUI.matrix = m;
            UiScale.Rotate(-t * 26f, c);
            GUI.color = new Color(Mathf.Lerp(tint.r, 1f, 0.3f), Mathf.Lerp(tint.g, 1f, 0.3f), Mathf.Lerp(tint.b, 1f, 0.3f), 0.8f * a);
            GUI.DrawTexture(new Rect(c.x - rs / 2f, c.y - rs / 2f, rs, rs), ringInner);
            GUI.matrix = m;
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * 3.2f);
            float cs = rs * 0.32f * pulse;
            GUI.color = new Color(1f, Mathf.Lerp(tint.g, 1f, 0.5f), Mathf.Lerp(tint.b, 1f, 0.4f), 0.85f * a);
            GUI.DrawTexture(new Rect(c.x - cs / 2f, c.y - cs / 2f, cs, cs), glow);
            GUI.color = Color.white;

            var titleStyle = new GUIStyle(UiSkin.Title) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
            UiSkin.Shadowed(new Rect(0, h * 0.6f, w, 52), title, titleStyle, a);
            UiSkin.Ornament(w / 2f - 210f, h * 0.6f + 56f, 420f, a);
            GUI.Label(new Rect(0, h * 0.6f + 64, w, 26), sub, UiSkin.Derive(UiSkin.Heading, new Color(tint.r, tint.g, tint.b, a), fontSize: 17, align: TextAnchor.MiddleCenter));

            GUI.Label(new Rect(w / 2f - 380f, h * 0.6f + 104, 760f, 60), line,
                UiSkin.Derive(UiSkin.Body, new Color(0.86f, 0.82f, 0.74f, a), fontSize: 16, fontStyle: FontStyle.Italic, align: TextAnchor.UpperCenter, wrap: true));

            float p = builtAt >= 0f ? Mathf.Clamp01(0.9f + (Time.time - builtAt) / 0.45f * 0.1f) : 0.9f * (1f - Mathf.Exp(-(Time.time - beginAt) / 1.6f));
            float bw = Mathf.Min(520f, w * 0.5f), bx = w / 2f - bw / 2f, by = h - 74f;
            UiSkin.Rect(new Rect(bx - 1, by - 1, bw + 2, 8), new Color(0f, 0f, 0f, 0.8f * a));
            UiSkin.Rect(new Rect(bx, by, bw, 6), new Color(1f, 1f, 1f, 0.06f * a));
            UiSkin.Rect(new Rect(bx, by, bw * p, 6), new Color(tint.r, tint.g, tint.b, 0.9f * a));
            float sheen = Frac(t * 0.6f);
            UiSkin.Rect(new Rect(bx + bw * p * sheen, by, 18f, 6), new Color(1f, 1f, 1f, 0.35f * a * (p < 1f ? 1f : 0f)));
            string stage = builtAt >= 0f ? "Ready" : Stages[Mathf.Min(Stages.Length - 1, (int)(p / 0.9f * Stages.Length))] + new string('.', 1 + (int)(t * 2.5f) % 3);
            GUI.Label(new Rect(bx, by + 10, bw, 20), stage, UiSkin.Derive(UiSkin.Small, new Color(0.75f, 0.7f, 0.62f, a), align: TextAnchor.UpperCenter));
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        private static Texture2D MakeGlow()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float v = Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color(1f, 1f, 1f, v * v * v);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D MakeRing(int n, float r0, float r1, int runes, float markInner)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 0.5f;
                    float alpha = 0f;

                    float edge = 1.2f / n;
                    alpha = Mathf.Max(alpha, Mathf.Clamp01(1f - Mathf.Abs(d - r1) / edge) , Mathf.Clamp01(1f - Mathf.Abs(d - r0) / edge) * 0.8f);
                    if (d > r0 && d < r1) alpha = Mathf.Max(alpha, 0.18f);

                    float slot = ang * runes;
                    float f = slot - Mathf.Floor(slot);
                    int which = (int)slot;
                    if (d > markInner && d < r0 - 0.01f)
                    {
                        if (Mathf.Abs(f - 0.5f) < 0.06f) alpha = Mathf.Max(alpha, 0.9f);
                        if (which % 3 == 0 && Mathf.Abs(d - (markInner + r0) / 2f) < 0.008f && Mathf.Abs(f - 0.5f) < 0.22f) alpha = Mathf.Max(alpha, 0.9f);
                    }
                    if (which % 2 == 1 && Mathf.Abs(f - 0.5f) < 0.08f && Mathf.Abs(d - (r0 + r1) / 2f) < 0.01f) alpha = Mathf.Max(alpha, 1f);
                    px[y * n + x] = new Color(1f, 1f, 1f, alpha);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
