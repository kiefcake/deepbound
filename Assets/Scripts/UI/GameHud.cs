using System.Collections.Generic;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Core;
using Deepbound.Loot;
using Deepbound.Progression;
using Deepbound.Enemies;
using Deepbound.Player;
using Deepbound.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    /// <summary>
    /// The in-run HUD, drawn with IMGUI.
    ///
    /// <para>
    /// IMGUI is an M1 expedient: zero assets, zero scene setup, and it redraws from live
    /// state every frame so it cannot drift out of sync. It will be replaced with UI
    /// Toolkit at M2 when the HUD needs real art and animation. Until then the layout,
    /// information and colour language here are the spec for that replacement.
    /// </para>
    ///
    /// Colour language: red = your health and danger, amber = stamina, gold = abilities and
    /// boss, cyan-white = friendly actions (revive).
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        private NetworkPlayer player;
        private Health health;
        private PlayerLife life;
        private readonly List<AbilityView> abilities = new List<AbilityView>();
        private PlayerProgress progress;

        /// <summary>True while a menu wants the cursor (the merchant's shop). The motor reads this.</summary>
        public static bool MenuOpen { get; private set; }

        private bool shopOpen, forgeOpen, invOpen, runeOpen;

        /// <summary>Tests and screenshots: open the inventory panel.</summary>
        public void DebugOpenInventory(bool open) => invOpen = open;
        public static bool InventoryOpen { get; private set; }

        /// <summary>Screenshots only: stage the forge panel open.</summary>
        public void DebugOpenForge() => forgeOpen = true;

        /// <summary>Screenshots only: stage the merchant's panel open.</summary>
        public void DebugCloseMenus() { shopOpen = forgeOpen = invOpen = runeOpen = false; }
        public void DebugOpenRunewright() { runeOpen = true; shopOpen = forgeOpen = false; }
        public void DebugOpenShop(Shop.Vendor vendor = Shop.Vendor.Merchant) { shopOpen = true; forgeOpen = false; shopVendor = vendor; }
        /// <summary>Which vendor's stall the open shop panel belongs to.</summary>
        private Shop.Vendor shopVendor;

        /// <summary>Screenshots only: hold the stat sheet open.</summary>
        public static bool DebugShowStats;
        private bool statsHeld;

        private struct FeedLine { public string Text; public Color Color; public float Time; }
        private static readonly List<FeedLine> FeedLines = new List<FeedLine>();

        private static string cardTitle, cardSub;
        private static float cardUntil;

        /// <summary>A big centred announcement: a boss's name as its arena seals, an ambush.</summary>
        private static Color cardColor = new Color(0.9f, 0.3f, 0.2f);

        private static readonly Queue<(string title, string sub, Color color)> cardQueue = new Queue<(string, string, Color)>();

        /// <summary>
        /// Shows now, or queues behind the card on screen (at most three wait), so two announcements
        /// never print over each other. Cards also wait for a boss intro to finish.
        /// </summary>
        private static float clearedAt = -10f;
        private static bool clearedReward;

        /// <summary>A quick CLEARED stamp under the crosshair when the last of a room's dead falls.</summary>
        public static void RoomCleared(bool reward) { clearedAt = Time.time; clearedReward = reward; }

        private void DrawRoomCleared(float w, float h)
        {
            float t = Time.time - clearedAt;
            if (t > 1.8f || BossIntroShowing) return;
            float a = Mathf.Clamp01(t * 6f) * Mathf.Clamp01((1.8f - t) / 0.5f);
            float s = 1f + Mathf.Max(0f, 0.25f - t) * 1.2f;
            var st = new GUIStyle(center) { fontSize = Mathf.RoundToInt(22 * s), fontStyle = FontStyle.Bold, normal = { textColor = new Color(Gold.r, Gold.g, Gold.b, a) } };
            float y = h * 0.3f;
            Rect(w / 2f - 110, y - 2, 220, 1, new Color(Gold.r, Gold.g, Gold.b, a * 0.6f));
            GUI.Label(new Rect(0, y, w, 30), "ROOM CLEARED", st);
            if (clearedReward) GUI.Label(new Rect(0, y + 28, w, 20), "something was left behind", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.9f, 0.7f, a * 0.85f) } });
        }

        public static void ShowTitleCard(string title, string subtitle, Color? color = null)
        {
            var c = color ?? new Color(0.9f, 0.3f, 0.2f);
            bool busy = (cardTitle != null && UnityEngine.Time.time < cardUntil - 0.6f) || BossIntroShowing;
            if (busy)
            {
                if (cardQueue.Count < 3) cardQueue.Enqueue((title, subtitle, c));
                return;
            }
            cardColor = c;
            cardTitle = title;
            cardSub = subtitle;
            cardUntil = UnityEngine.Time.time + 3.5f;
        }

        private static void NextTitleCard()
        {
            if (cardQueue.Count == 0 || BossIntroShowing) return;
            if (cardTitle != null && UnityEngine.Time.time < cardUntil - 0.6f) return;
            var (t, s, c) = cardQueue.Dequeue();
            cardColor = c;
            cardTitle = t;
            cardSub = s;
            cardUntil = UnityEngine.Time.time + 3.5f;
        }

        // ---- A journal page, read at its lectern ----
        private static int pageShown = -1;
        private static bool pageNew;
        private static float pageAt;
        private static Vector3 pageWhere;
        private static bool pageAnchored;

        public static void ShowPage(int index, bool isNew)
        {
            pageShown = index;
            pageNew = isNew;
            pageAt = UnityEngine.Time.time;
            pageAnchored = false;
        }

        private void DrawPage(float w, float h)
        {
            if (pageShown < 0 || pageShown >= Journal.Entries.Length) return;
            var kb = Keyboard.current;
            // F, Esc, or walking off closes it (it closes itself after a while too).
            if (Time.time - pageAt > 0.3f && kb != null && kb.escapeKey.wasPressedThisFrame) { pageShown = -1; return; }
            if (!pageAnchored) { pageWhere = player.transform.position; pageAnchored = true; }
            if ((player.transform.position - pageWhere).sqrMagnitude > 16f) { pageShown = -1; return; }
            if (Time.time - pageAt > 25f || player.GetComponent<FirstPersonMotor>().MoveInput.sqrMagnitude > 0.1f && Time.time - pageAt > 1f) { pageShown = -1; return; }
            var e = Journal.Entries[pageShown];
            float pw = Mathf.Min(560f, w - 80f), ph = 330f, px = w / 2f - pw / 2f, py = h / 2f - ph / 2f - 20f;
            Rect(px - 6, py - 6, pw + 12, ph + 12, new Color(0.25f, 0.18f, 0.1f, 0.95f));
            Rect(px, py, pw, ph, new Color(0.9f, 0.84f, 0.68f, 0.98f));
            var ink = new Color(0.22f, 0.14f, 0.08f);
            string[] floors = { "The Ossuary", "The Drowned Vaults", "The Hollow Throne", "The Black Cathedral", "Eulogies" };
            GUI.Label(new Rect(px + 24, py + 16, pw - 48, 20), floors[Mathf.Clamp(e.Floor, 0, 4)].ToUpperInvariant() + (pageNew ? "   -   NEW PAGE  (" + Journal.Count + " / " + Journal.Entries.Length + ")" : ""),
                new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.45f, 0.25f, 0.12f) } });
            GUI.Label(new Rect(px + 24, py + 38, pw - 48, 34), e.Title, new GUIStyle(big) { fontSize = 26, alignment = TextAnchor.MiddleLeft, normal = { textColor = ink } });
            GUI.Label(new Rect(px + 24, py + 82, pw - 48, ph - 120), e.Text, new GUIStyle(label) { wordWrap = true, fontSize = 16, fontStyle = FontStyle.Italic, normal = { textColor = ink } });
            GUI.Label(new Rect(px, py + ph - 28, pw, 20), "Esc or walk away to close   ·   kept in your Journal (Esc > Guide)", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.4f, 0.3f, 0.2f) } });
        }

        private static string tauntWho, tauntLine;
        private static float tauntAt = -99f;

        /// <summary>A boss speaks (phase changes): a line under the boss bar, typed out.</summary>
        public static void ShowBossTaunt(string who, string line)
        {
            tauntWho = who;
            tauntLine = line;
            tauntAt = UnityEngine.Time.time;
        }

        private void DrawTaunt(float w, float h)
        {
            float t = Time.time - tauntAt;
            if (tauntLine == null || t > 5f) return;
            float a = Mathf.Clamp01(Mathf.Min(t / 0.25f, (5f - t) / 0.6f));
            int shown = Mathf.Clamp(Mathf.RoundToInt(t * 45f), 0, tauntLine.Length);
            var style = new GUIStyle(center) { fontSize = 18, fontStyle = FontStyle.Italic, wordWrap = true, normal = { textColor = new Color(1f, 0.85f, 0.7f, a) } };
            // Subtitles: low and centred, clear of the tips and the boss bar.
            float y = h - 196f;
            Rect(w * 0.18f, y - 18, w * 0.64f, 54, new Color(0f, 0f, 0f, 0.5f * a));
            GUI.Label(new Rect(w * 0.18f, y - 16, w * 0.64f, 18), tauntWho.ToUpperInvariant(),
                new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(Gold.r, Gold.g, Gold.b, a) } });
            GUI.Label(new Rect(w * 0.18f, y + 2, w * 0.64f, 34), "\u201C" + tauntLine.Substring(0, shown) + (shown < tauntLine.Length ? "" : "\u201D"), style);
        }

        private static string introName, introEpithet, introHint;
        private static float introAt = -99f;
        private const float IntroLength = 5.5f;

        /// <summary>A boss's entrance: letterbox bars, its name and epithet, and how to fight it.</summary>
        public static void ShowBossIntro(string name, string epithet, string hint)
        {
            introName = name;
            introEpithet = epithet;
            introHint = hint;
            introAt = UnityEngine.Time.time;
            cardUntil = 0f;
        }

        /// <summary>For tests and screenshots: is the intro card up?</summary>
        public static bool BossIntroShowing => UnityEngine.Time.time - introAt < IntroLength;

        private void DrawBossIntro(float w, float h)
        {
            float t = Time.time - introAt;
            if (introName == null || t > IntroLength) return;
            // Bars slide in, hold, slide out; the text fades in after them.
            float bars = Mathf.SmoothStep(0f, 1f, Mathf.Min(t / 0.5f, (IntroLength - t) / 0.6f));
            float text = Mathf.Clamp01(Mathf.Min((t - 0.35f) / 0.5f, (IntroLength - t) / 0.8f));
            float bar = h * 0.11f * bars;
            Rect(0, 0, w, bar, Color.black);
            Rect(0, h - bar, w, bar, Color.black);
            if (text <= 0f) return;
            float y = h * 0.24f;
            // The name tracks in: wide letter-spacing that settles.
            float spread = Mathf.Lerp(1.12f, 1f, Mathf.Clamp01((t - 0.35f) / 1.2f));
            int nameSize = Mathf.RoundToInt(Mathf.Clamp(w / 22f, 34f, 60f));
            {
                var probe = new GUIStyle(big) { fontSize = nameSize };
                while (nameSize > 26 && probe.CalcSize(new GUIContent(introName)).x * spread > w * 0.6f) { nameSize -= 2; probe.fontSize = nameSize; }
            }
            UiSkin.Shadowed(new Rect(-w * (spread - 1f) / 2f, y, w * spread, nameSize + 12), introName,
                new GUIStyle(big) { fontSize = nameSize, normal = { textColor = new Color(0.92f, 0.32f, 0.22f) } }, text);
            float line = Mathf.Min(w * 0.36f, 460f) * Mathf.Clamp01((t - 0.5f) / 0.6f);
            Rect(w / 2f - line / 2f, y + nameSize + 16, line, 2, new Color(Gold.r, Gold.g, Gold.b, 0.8f * text));
            GUI.Label(new Rect(0, y + nameSize + 22, w, 28), introEpithet.ToUpperInvariant(),
                new GUIStyle(center) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(Gold.r, Gold.g, Gold.b, text) } });
            float hintA = Mathf.Clamp01(Mathf.Min((t - 1.2f) / 0.6f, (IntroLength - t) / 0.8f));
            if (hintA > 0f)
            {
                Rect(w * 0.18f, y + nameSize + 52, w * 0.64f, 48, new Color(0f, 0f, 0f, 0.5f * hintA));
                GUI.Label(new Rect(w * 0.22f, y + nameSize + 54, w * 0.56f, 44), introHint,
                    new GUIStyle(center) { fontSize = 15, wordWrap = true, fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.85f, 0.82f, 0.75f, hintA) } });
            }
        }

        private void DrawTitleCard(float w, float h)
        {
            NextTitleCard();
            // The floor banner owns the upper screen while it shows: cards drop below it.
            if (cardTitle == null || Time.time > cardUntil) return;
            float a = Mathf.Clamp01((cardUntil - Time.time) / 1f) * Mathf.Clamp01((3.5f - (cardUntil - Time.time)) * 4f);
            float cy = Time.time < bannerUntil && bannerTitle != null ? h * 0.42f : h * 0.3f;
            // Shrink a long title to fit the middle of the screen (clear of the minimap).
            int size = 44;
            var st = new GUIStyle(big) { fontSize = size, normal = { textColor = cardColor } };
            while (size > 24 && st.CalcSize(new GUIContent(cardTitle)).x > w * 0.62f) { size -= 2; st.fontSize = size; }
            Rect(0, cy - 12, w, 96, new Color(0f, 0f, 0f, 0.55f * a));
            Rect(w * 0.3f, cy - 12, w * 0.4f, 1, new Color(Gold.r, Gold.g, Gold.b, 0.6f * a));
            Rect(w * 0.3f, cy + 83, w * 0.4f, 1, new Color(Gold.r, Gold.g, Gold.b, 0.6f * a));
            UiSkin.Shadowed(new Rect(0, cy - 4, w, 54), cardTitle, st, a);
            GUI.Label(new Rect(w * 0.15f, cy + 50, w * 0.7f, 26), cardSub, new GUIStyle(center) { fontSize = 17, wordWrap = true, normal = { textColor = new Color(1f, 1f, 1f, a) } });
        }

        /// <summary>Adds a line to the event feed (potions drunk, and so on). Any peer.</summary>
        public static void Feed(string text, Color color)
        {
            FeedLines.Add(new FeedLine { Text = text, Color = color, Time = UnityEngine.Time.time });
            if (FeedLines.Count > 6) FeedLines.RemoveAt(0);
        }

        private static Texture2D white;
        private GUIStyle label, big, small, center;
        private float hurtFlash;
        private float spawnTime;
        private float bannerUntil;

        private static readonly Color Panel = new Color(0.03f, 0.03f, 0.04f, 0.72f);
        private static readonly Color Gold = new Color(0.92f, 0.74f, 0.36f);
        private static readonly Color HealthRed = new Color(0.78f, 0.12f, 0.1f);

        private static readonly string[] FloorNames = { "The Ossuary", "The Drowned Vaults", "The Hollow Throne", "The Black Cathedral", "The Abyss" };

        public void Init(NetworkPlayer owner)
        {
            player = owner;
            health = owner.GetComponent<Health>();
            life = owner.GetComponent<PlayerLife>();
            progress = owner.GetComponent<PlayerProgress>();
            health.Hurt += amount => hurtFlash = Mathf.Min(1f, hurtFlash + amount / 30f);
            PlayerLife.DownedRecap += (killer, lines) => { recapKiller = killer; recapLines = lines; };
            health.HurtFrom += from => { hitMarks.Add((from, Time.time)); if (hitMarks.Count > 6) hitMarks.RemoveAt(0); };
            spawnTime = Time.time;
            Health.AnyHurt += OnAnyHurt;
            Health.AnyHealed += OnAnyHealed;
            DungeonDirector.ArenaBuilt += OnArenaBuilt;
            if (DungeonDirector.ArenaId > 0) OnArenaBuilt(DungeonDirector.ArenaId);
        }

        private void LateUpdate()
        {
            // Esc closes the shop or class screen first; only a second Esc pauses.
            PauseMenu.OtherMenuOpenLastFrame = shopOpen || forgeOpen || invOpen || runeOpen || pageShown >= 0 || CharacterSelect.Open || Wardrobe.Open;
        }

        private void OnDestroy()
        {
            if (stateVolume != null) Destroy(stateVolume.gameObject);
            MenuOpen = false;
            Health.AnyHurt -= OnAnyHurt;
            Health.AnyHealed -= OnAnyHealed;
            DungeonDirector.ArenaBuilt -= OnArenaBuilt;
        }

        // ---- Arena transitions: fade through black, then a title banner ----

        private float fade;
        private string bannerTitle, bannerSub, bannerHint;

        // The descent cutscene: white rises as the beam takes you; you arrive out of white, letterboxed.
        private static float departStart = -1f, departLength = 1f;
        private static bool arrivingFromBeam;
        private float whiteFade, letterboxUntil;

        private static float freezeUntil;
        private static float fadeOutStart = -1f, fadeOutLength = 1f;

        /// <summary>The grave lift's climb: the screen goes to black over <paramref name="seconds"/>.</summary>
        public static void BeginFadeOut(float seconds)
        {
            fadeOutStart = UnityEngine.Time.time;
            fadeOutLength = Mathf.Max(0.1f, seconds);
        }

        /// <summary>The descent cutscene holds the player still: while the beam takes them, and a moment after arriving.</summary>
        public static bool CutsceneFreeze => departStart >= 0f || UnityEngine.Time.time < freezeUntil;
        /// <summary>The carriage ride out: no HUD, no hands, just the road.</summary>
        public static bool Cinematic => departStart >= 0f;

        /// <summary>Lines of the Cardinal's hymn, one shown on each descent.</summary>
        private static readonly string[] Hymn =
        {
            "Sleep, you who were carried down. Sleep, you who carried.",
            "The morning is coming, and it is not yet.",
            "Lie close, lie still, lie near to the Light.",
            "Keep the lantern lit. Keep the way home.",
            "What the dark takes, the stone gives back.",
            "I will sing until it is. I will sing until it is.",
            "Down, and down, and nearer to Thee.",
        };
        private static string HymnLine = Hymn[0];

        public static void BeginDeparture(float seconds)
        {
            HymnLine = Hymn[UnityEngine.Random.Range(0, Hymn.Length)];
            departStart = UnityEngine.Time.time;
            departLength = seconds;
            arrivingFromBeam = true;
        }

        private void OnArenaBuilt(int _)
        {
            fade = 1f;
            if (arrivingFromBeam && !DungeonDirector.InLobby)
            {
                arrivingFromBeam = false;
                departStart = -1f;
                fade = 0f;
                whiteFade = 1f;
                letterboxUntil = Time.time + 3.2f;
                foreach (var p in PlayerLife.All) if (p != null) Art.TeleportBeam.On(p.transform, true, 2.2f);
                Art.Sfx.Play2D(Art.Sound.Revive, 0.7f);
                freezeUntil = Time.time + 1.2f;
            }
            else if (DungeonDirector.InLobby) { departStart = -1f; arrivingFromBeam = false; }
            bannerUntil = Time.time + 5f;
            if (DungeonDirector.InLobby || DungeonDirector.Layout == null)
            {
                bannerTitle = "THE LAST LANTERN";
                bannerSub = "Speak to Corwin, then sit on the back of the carriage when your party is ready";
                bannerHint = "";
            }
            else
            {
                int depth = DungeonDirector.Layout.Depth;
                bannerTitle = "FLOOR " + (depth + 1) + "  -  " + DungeonTheme.FloorName(depth).ToUpperInvariant();
                int act = DungeonDirector.Act(depth);
                bannerSub = (act < FloorNames.Length ? FloorNames[act] : "The Deep") + (DungeonDirector.IsWardenFloor(depth) ? "  ·  a Warden bars the way down" : "  ·  the act's master waits below");
                bannerHint = DungeonDirector.GimmickHint(depth);
            }
        }

        // ---- Floating damage numbers ----

        private struct DamageNumber { public Vector3 World; public float Amount; public float Time; public bool Player; public bool Heal; public float Drift; }

        private void OnAnyHealed(Health target, float amount)
        {
            // Only meaningful heals (not regen ticks), and only on the party.
            if (target == null || amount < 4f || target.Team != Team.Players) return;
            var jitter = new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));
            if (!Settings.DamageNumbers) return;
            numbers.Add(new DamageNumber { World = target.Center + Vector3.up * (target.CenterHeight + 0.3f) + jitter, Amount = amount, Time = Time.time, Heal = true });
            if (numbers.Count > 40) numbers.RemoveAt(0);
        }
        private readonly List<DamageNumber> numbers = new List<DamageNumber>();

        private void OnAnyHurt(Health target, float amount)
        {
            if (target == null || amount < 0.5f) return;
            if (target.GetComponent<TrainingDummy>() != null) dummyHits.Add((Time.time, amount));
            bool mine = target == health;
            // Your own damage shows on your vitals; teammates' and enemies' float.
            if (mine) return;
            var jitter = new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));
            if (!Settings.DamageNumbers) return;
            numbers.Add(new DamageNumber { World = target.Center + Vector3.up * (target.CenterHeight + 0.2f) + jitter, Amount = amount, Time = Time.time, Player = target.Team == Team.Players, Drift = Random.Range(-1f, 1f) });
            if (numbers.Count > 40) numbers.RemoveAt(0);
        }

        // Damage landed on the training dummies, for the yard's DPS readout.
        private readonly List<(float time, float amount)> dummyHits = new List<(float, float)>();

        /// <summary>At camp: DPS on the dummies, and the Test of Might's record when you are near the gong.</summary>
        private void DrawCampReadouts(float w, float h)
        {
            if (!DungeonDirector.InLobby) return;
            dummyHits.RemoveAll(x => Time.time - x.time > 4f);
            if (dummyHits.Count > 0)
            {
                float sum = 0f, first = Time.time;
                foreach (var (t, a) in dummyHits) { sum += a; first = Mathf.Min(first, t); }
                float span = Mathf.Max(1f, Time.time - first);
                GUI.Label(new Rect(0, h / 2f + 104, w, 24), "TRAINING:  " + Mathf.RoundToInt(sum / span) + " damage per second",
                    new GUIStyle(center) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            }
            // The range's tally, when you are near the butts.
            var gongNear = Gong.Instance;
            float dRange = Vector3.Distance(player.transform.position, Lobby.RangeCenter);
            float dGong = gongNear != null ? Vector3.Distance(player.transform.position, gongNear.transform.position) : 999f;
            if (dRange < 11f && dRange < dGong)
            {
                var parts = new List<string>();
                foreach (var kv in RangeTarget.Tally) parts.Add(kv.Key + " " + kv.Value);
                Rect(w / 2f - 210, 100, 420, 46, Panel);
                GUI.Label(new Rect(0, 104, w, 22), "THE BUTTS", new GUIStyle(center) { normal = { textColor = Gold } });
                GUI.Label(new Rect(0, 124, w, 20), parts.Count > 0 ? "Targets hit:   " + string.Join("   ·   ", parts) : "Hit the moving targets with anything you have.",
                    new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            }
            var gong = Gong.Instance;
            if (gong != null && dGong < 9f && dGong <= dRange)
            {
                string rec = gong.Record.Value > 0f ? Mathf.RoundToInt(gong.Record.Value) + "  by " + gong.RecordHolder.Value : "nobody yet";
                Rect(w / 2f - 190, 100, 380, 46, Panel);
                GUI.Label(new Rect(0, 104, w, 22), "THE TEST OF MIGHT", new GUIStyle(center) { normal = { textColor = Gold } });
                GUI.Label(new Rect(0, 124, w, 20), "Strike the gong as hard as you can.   Record: " + rec, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            }
        }

        private struct ComboText { public Vector3 World; public string Text; public Color Color; public float Time; }
        private static readonly List<ComboText> combos = new List<ComboText>();

        /// <summary>Float a combo's name over a point (any peer).</summary>
        public static void ShowCombo(Vector3 at, string text, Color color) =>
            combos.Add(new ComboText { World = at, Text = text, Color = color, Time = UnityEngine.Time.time });

        private void DrawCombos()
        {
            var cam = player.Camera;
            for (int i = combos.Count - 1; i >= 0; i--)
            {
                var c = combos[i];
                float age = Time.time - c.Time;
                if (age > 1.3f) { combos.RemoveAt(i); continue; }
                var screen = cam.WorldToScreenPoint(c.World + Vector3.up * (0.4f + age * 0.5f));
                if (screen.z <= 0f || screen.z > 35f) continue;
                float a = Mathf.Clamp01(1.3f - age) * Mathf.Clamp01(age * 8f);
                float scale = 1f + Mathf.Max(0f, 0.15f - age) * 3f;
                var style = new GUIStyle(big) { fontSize = Mathf.RoundToInt(20 * scale), fontStyle = FontStyle.Bold, normal = { textColor = new Color(c.Color.r, c.Color.g, c.Color.b, a) } };
                var shadow = new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, a * 0.85f) } };
                var rect = new Rect(screen.x - 120, Screen.height - screen.y - 16, 240, 32);
                GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), c.Text, shadow);
                GUI.Label(rect, c.Text, style);
            }

            // Standing in consecrated ground: say so, under the crosshair.
            var buffs = new List<string>();
            if (ClericKit.InConsecration(player.transform.position)) buffs.Add("BLESSED +20%");
            if (ClericKit.IsInspired(health)) buffs.Add("INSPIRED +15%");
            var emotes = player.GetComponent<Emotes>();
            if (emotes != null && emotes.Brothers) buffs.Add("BROTHERS IN ARMS +10%");
            DrawPouchFull();
            if (Time.time < life.VeiledUntil || (life.IsServer == false && InRogueSmoke())) buffs.Add("SMOKE SCREEN");
            if (buffs.Count > 0)
                GUI.Label(new Rect(0, Screen.height / 2f + 18, Screen.width, 20), string.Join("   ", buffs),
                    new GUIStyle(small) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, normal = { textColor = Combos.Color(ComboKind.Blessed) } });
        }

        /// <summary>Clients do not run the veil; approximate it from any hidden Rogue nearby.</summary>
        private bool InRogueSmoke()
        {
            foreach (var p in PlayerLife.All)
                if (p != null && p != life && p.Hidden.Value && p.GetComponent<PlayerProgress>().ClassId.Value == ClassId.Rogue &&
                    Vector3.Distance(p.transform.position, player.transform.position) < RogueKit.SmokeCloudRadius) return true;
            return false;
        }

        private void DrawDamageNumbers()
        {
            var cam = player.Camera;
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                var n = numbers[i];
                float age = Time.time - n.Time;
                if (age > 1f) { numbers.RemoveAt(i); continue; }

                // Hits pop out and arc away to one side under a little gravity; heals rise straight.
                var lift = n.Heal ? Vector3.up * age * 0.8f : Vector3.up * (age * 1.6f - age * age * 1.1f) + cam.transform.right * n.Drift * age * 0.6f;
                var screen = cam.WorldToScreenPoint(n.World + lift);
                if (screen.z <= 0f || screen.z > 30f) continue;

                bool heavy = n.Amount >= 35f;
                float a = 1f - age * age;
                float pop = age < 0.12f ? Mathf.Lerp(1.6f, 1f, age / 0.12f) : 1f;
                var color = n.Heal ? new Color(0.45f, 1f, 0.5f, a) : n.Player ? new Color(1f, 0.35f, 0.3f, a) : heavy ? new Color(1f, 0.82f, 0.35f, a) : new Color(1f, 1f, 1f, a);
                var style = new GUIStyle(big) { fontSize = Mathf.RoundToInt((heavy ? 26 : 18) * pop), normal = { textColor = color } };
                var shadow = new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, a * 0.8f) } };
                var rect = new Rect(screen.x - 60, Screen.height - screen.y - 20, 120, 40);
                string text = (n.Heal ? "+" : "") + Mathf.RoundToInt(n.Amount);
                GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, shadow);
                GUI.Label(rect, text, style);
            }
        }

        /// <summary>
        /// Camp banner, top of the screen and out of the way: where you are, what is next,
        /// your class and the difficulty (both only changeable before a run), and boarding.
        /// </summary>
        /// <summary>The party's contract, top left: what it asks, how far along, and how it ended.</summary>
        private void DrawContract()
        {
            var d = DungeonDirector.Instance;
            if (d == null || d.ActiveContract.Value == 0) return;
            var c = (Contract)d.ActiveContract.Value;
            var state = (DungeonDirector.ContractStatus)d.ContractState.Value;
            string line = state == DungeonDirector.ContractStatus.Taken ? "Next floor: " + DungeonDirector.ContractTask(c, d.NextDepth.Value)
                : state == DungeonDirector.ContractStatus.Active ? (c == Contract.Slayer ? d.ContractProgress.Value + " / " + d.ContractGoal.Value + " slain" : DungeonDirector.ContractTask(c, DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0).Replace("the next", "this").Replace("next ", ""))
                : state == DungeonDirector.ContractStatus.Fulfilled ? "FULFILLED" : state == DungeonDirector.ContractStatus.Failed ? "FAILED" : "";
            var col = state == DungeonDirector.ContractStatus.Fulfilled ? new Color(0.5f, 1f, 0.55f) : state == DungeonDirector.ContractStatus.Failed ? new Color(0.9f, 0.4f, 0.35f) : new Color(0.9f, 0.88f, 0.84f);
            Rect(14, 14, 330, 46, Panel);
            GUI.Label(new Rect(22, 16, 320, 20), "CONTRACT  ·  " + DungeonDirector.ContractName(c).ToUpperInvariant(), new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            GUI.Label(new Rect(22, 35, 320, 20), line, new GUIStyle(small) { normal = { textColor = col } });
        }

        // ---- Where hits come from: a red arc at the screen's edge, toward the attacker. ----
        private readonly List<(Vector3 from, float time)> hitMarks = new List<(Vector3, float)>();

        private void DrawHitDirections(float w, float h)
        {
            var cam = player.Camera;
            for (int i = hitMarks.Count - 1; i >= 0; i--)
            {
                float age = Time.time - hitMarks[i].time;
                if (age > 1.1f) { hitMarks.RemoveAt(i); continue; }
                var to = hitMarks[i].from - player.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < 0.01f) continue;
                // Angle relative to where you look: 0 = ahead, 90 = right.
                float ang = Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up), to, Vector3.up) * Mathf.Deg2Rad;
                float a = (1f - age / 1.1f) * 0.85f;
                var center = new Vector2(w / 2f, h / 2f);
                float radius = Mathf.Min(w, h) * 0.32f;
                // A short arc of blocks, thickest in the middle.
                for (int k = -4; k <= 4; k++)
                {
                    float t = ang + k * 0.06f;
                    var p = center + new Vector2(Mathf.Sin(t), -Mathf.Cos(t)) * radius;
                    float size = 10f - Mathf.Abs(k) * 1.4f;
                    Rect(p.x - size / 2f, p.y - size / 2f, size, size, new Color(0.95f, 0.15f, 0.1f, a));
                }
            }
        }

        // ---- Inventory (I): what you wear, your pack, and everything else the run has given you. ----
        private void DrawInventory(float w, float h)
        {
            Rect(0, 0, w, h, new Color(0f, 0f, 0f, 0.55f));
            float pw = Mathf.Min(980f, w - 40f), ph = Mathf.Min(560f, h - 60f);
            float px = w / 2f - pw / 2f, py = h / 2f - ph / 2f;
            UiSkin.Panel(new Rect(px - 12, py - 12, pw + 24, ph + 24), 0.96f);
            GUI.Label(new Rect(px, py, pw, 30), "INVENTORY", new GUIStyle(UiSkin.Heading) { alignment = TextAnchor.MiddleLeft, fontSize = 26 });
            GUI.Label(new Rect(px, py + 4, pw, 24), progress.Gold.Value + " gold     " + progress.Embers.Value + " embers     " + progress.Sigils.Value + " sigil" + (progress.Sigils.Value == 1 ? "" : "s"),
                new GUIStyle(label) { alignment = TextAnchor.MiddleRight, normal = { textColor = Gold } });

            var ev = Event.current;
            var mouse = ev.mousePosition;
            GearItem hovered = default;
            bool hoveredWorn = false;
            bool click = ev.type == EventType.MouseDown;
            int button = ev.button;

            // ---- Equipped ----
            float cx = px, cy = py + 44;
            GUI.Label(new Rect(cx, cy, 300, 22), "EQUIPPED", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            cy += 24;
            string weapon = ClassDefinition.WeaponName(progress.ClassId.Value) + " +" + progress.WeaponLevel.Value;
            Rect(cx, cy, 300, 46, new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(cx + 10, cy + 4, 280, 20), weapon, new GUIStyle(label) { fontStyle = FontStyle.Bold });
            string runes = "";
            for (int s = 0; s < Runes.SocketsAt(progress.WeaponLevel.Value); s++) runes += (s > 0 ? ",  " : "") + Runes.Name(progress.Socketed(s));
            GUI.Label(new Rect(cx + 10, cy + 24, 280, 18), runes.Length > 0 ? runes : "No rune sockets yet (anvil +1)", small);
            cy += 54;
            foreach (var slot in new[] { GearSlot.Armor, GearSlot.Charm })
            {
                var item = progress.Equipped(slot);
                var r = new Rect(cx, cy, 300, 52);
                bool over = r.Contains(mouse);
                var qc = item.IsEmpty ? new Color(0.4f, 0.4f, 0.4f) : Gear.QualityColor(item.Quality);
                Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2, new Color(qc.r, qc.g, qc.b, over ? 0.8f : 0.35f));
                Rect(r.x, r.y, r.width, r.height, new Color(0.05f, 0.045f, 0.04f, 0.95f));
                GUI.Label(new Rect(r.x + 10, r.y + 4, 280, 18), slot.ToString().ToUpperInvariant(), small);
                GUI.Label(new Rect(r.x + 10, r.y + 22, 280, 22), item.IsEmpty ? "(empty)" : Gear.Name(item), new GUIStyle(label) { normal = { textColor = qc } });
                if (over && !item.IsEmpty) { hovered = item; hoveredWorn = true; }
                if (over && click && !item.IsEmpty && button == 1) { progress.UnequipRpc((byte)slot); ev.Use(); }
                cy += 60;
            }

            // ---- Flasks and relics ----
            var fl = player.GetComponent<Flasks>();
            if (fl != null)
            {
                GUI.Label(new Rect(cx, cy + 4, 320, 20), "POUCH   Health " + fl.Health.Value + "   Mana " + fl.Mana.Value + "   Frost " + fl.Frost.Value + "   Holy " + fl.Holy.Value + "   (5 each)", small);
                cy += 28;
            }
            GUI.Label(new Rect(cx, cy, 300, 22), "RELICS (" + progress.RelicCount + ")", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            cy += 22;
            string hoverText = null;
            bool party = PlayerLife.All.Count > 1;
            for (int i = 0; i < progress.RelicCount && cy < py + ph - (party ? 150 : 20); i++)
            {
                var rel = progress.RelicAt(i);
                var rr = new Rect(cx, cy, 300, 18);
                GUI.Label(rr, Relics.Name(rel), new GUIStyle(small) { normal = { textColor = rr.Contains(mouse) ? Color.white : new Color(0.85f, 0.8f, 0.7f) } });
                if (rr.Contains(mouse)) hoverText = Relics.Name(rel) + ": " + Relics.Describe(rel);
                cy += 18;
            }

            // ---- The party purse: give gold, or even it out ----
            if (party)
            {
                float qy = py + ph - 144;
                GUI.Label(new Rect(cx, qy, 300, 22), "PARTY PURSE" + (DungeonDirector.ShareLoot ? "  (loot is shared)" : "  (finders keepers)"), new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
                qy += 22;
                int shown = 0;
                foreach (var ally in PlayerLife.All)
                {
                    if (ally == null || ally.gameObject == player.gameObject || shown >= 3) continue;
                    var ap = ally.GetComponent<PlayerProgress>();
                    GUI.Label(new Rect(cx, qy, 190, 20), ally.name + "   " + ap.Gold.Value + "g", small);
                    GUI.enabled = progress.Gold.Value >= 25;
                    if (GUI.Button(new Rect(cx + 196, qy, 104, 20), "Give 25 gold")) progress.GiveGoldRpc(ally.NetworkObjectId, 25);
                    GUI.enabled = true;
                    qy += 23;
                    shown++;
                }
                if (GUI.Button(new Rect(cx, qy + 2, 300, 22), "Even out gold  (share what you have over the average)")) progress.EvenOutGoldRpc();
            }

            // ---- The pack: a 4 x 2 grid ----
            float gx = px + 330, gy = py + 44;
            GUI.Label(new Rect(gx, gy, 400, 22), "PACK  " + progress.BagCount + " / " + PlayerProgress.BagSize, new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            gy += 24;
            const float cell = 122f, gap = 8f;
            for (int i = 0; i < PlayerProgress.BagSize; i++)
            {
                var r = new Rect(gx + (i % 4) * (cell + gap), gy + (i / 4) * (cell * 0.8f + gap), cell, cell * 0.8f);
                var item = progress.BagItem(i);
                bool over = r.Contains(mouse);
                var qc = item.IsEmpty ? new Color(0.3f, 0.3f, 0.3f) : Gear.QualityColor(item.Quality);
                Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2, new Color(qc.r, qc.g, qc.b, over && !item.IsEmpty ? 0.9f : 0.35f));
                Rect(r.x, r.y, r.width, r.height, new Color(0.05f, 0.045f, 0.04f, 0.95f));
                if (item.IsEmpty) continue;
                GUI.Label(new Rect(r.x + 6, r.y + 4, r.width - 12, 16), Gear.SlotOf(item.Kind) == GearSlot.Armor ? "ARMOR" : "CHARM", small);
                GUI.Label(new Rect(r.x + 6, r.y + 20, r.width - 12, r.height - 22), Gear.Name(item),
                    new GUIStyle(small) { wordWrap = true, fontStyle = FontStyle.Bold, normal = { textColor = qc } });
                if (over)
                {
                    hovered = item;
                    if (click && button == 0 && ev.control && party) { progress.BagGiftRpc(i); ev.Use(); }
                    else if (click && button == 0 && ev.shift) { progress.BagDropRpc(i); ev.Use(); }
                    else if (click && button == 0) { progress.BagEquipRpc(i); Art.Sfx.Play2D(Art.Sound.Block, 0.4f); ev.Use(); }
                    else if (click && button == 1)
                    {
                        if (NearAnvil() != null) progress.BagScrapRpc(i);
                        else Feed("Only Hobb scraps gear: bring it to his anvil at camp.", new Color(1f, 0.75f, 0.45f));
                        ev.Use();
                    }
                }
            }

            // ---- Runes waiting for a socket ----
            float ry = gy + 2 * (cell * 0.8f + gap) + 8;
            GUI.Label(new Rect(gx, ry, 400, 22), "RUNES IN THE BAG (" + progress.RuneBagCount + ")  -  set or meld them at Ysolde's stones", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            ry += 22;
            for (int i = 0; i < progress.RuneBagCount && i < 6; i++)
            {
                var rune = progress.RuneInBag(i);
                var rr = new Rect(gx + (i % 3) * 132, ry + (i / 3) * 20, 128, 18);
                GUI.Label(rr, Runes.Name(rune), new GUIStyle(small) { normal = { textColor = Runes.Color(rune) } });
                if (rr.Contains(mouse)) hoverText = Runes.Name(rune) + ": " + Runes.Describe(rune);
            }

            // ---- What the mouse is over ----
            // Below the pack and runes, clear of the grid.
            float dx = gx, dy = Mathf.Min(ry + 52f, py + ph - 214f);
            if (!hovered.IsEmpty) DrawGearCard(hovered, dx, dy, !hoveredWorn);
            else if (hoverText != null)
            {
                Rect(dx, dy, 360, 70, new Color(0.04f, 0.035f, 0.03f, 0.93f));
                GUI.Label(new Rect(dx + 10, dy + 8, 340, 60), hoverText, new GUIStyle(small) { wordWrap = true });
            }

            GUI.Label(new Rect(px, py + ph - 22, pw, 20),
                "Pack: click to wear   right-click to scrap (at Hobb's anvil)   shift-click to drop" + (party ? "   ctrl-click to give to the nearest ally" : "") + "      Worn: right-click to take off      I / Esc close",
                new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
        }

        // ---- Your microphone: lit while you transmit; PTT hint. ----
        private void DrawMicLight(float w, float h)
        {
            var v = PlayerVoice.LocalVoice;
            if (v == null || !PlayerVoice.MicAvailable || Settings.VoiceMode == 2) return;
            bool on = v.Speaking;
            // Beside the vitals panel, clear of the status badges above it.
            float x = 270f, y = h - 142f;
            Rect(x, y, 58, 18, new Color(0f, 0f, 0f, on ? 0.6f : 0.35f));
            Rect(x + 4, y + 5, 8, 8, on ? new Color(0.4f, 1f, 0.5f) : new Color(0.4f, 0.4f, 0.4f));
            GUI.Label(new Rect(x + 16, y, 44, 18), Settings.VoiceMode == 1 ? Binds.Label(Bind.PushToTalk) + " talk" : "MIC", small);
        }

        // ---- Death recap: what put you down (newest hit's source, then damage by source). ----
        private string recapKiller, recapLines;

        private void DrawRecap(float w, float h)
        {
            if (string.IsNullOrEmpty(recapKiller)) return;
            var lines = (recapLines ?? "").Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            float pw = 300f, ph = 50f + lines.Length * 20f;
            float x = w - pw - 24f, y = h * 0.5f - ph / 2f;
            Rect(x, y, pw, ph, Panel);
            GUI.Label(new Rect(x + 12, y + 8, pw - 24, 20), "DOWNED BY", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.75f, 0.7f, 0.65f) } });
            GUI.Label(new Rect(x + 12, y + 24, pw - 24, 22), recapKiller, new GUIStyle(label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.4f, 0.3f) } });
            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\t');
                float ly = y + 48 + i * 20;
                GUI.Label(new Rect(x + 12, ly, pw - 80, 20), parts[0], small);
                if (parts.Length > 1) GUI.Label(new Rect(x + pw - 72, ly, 60, 20), parts[1], new GUIStyle(small) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1f, 0.55f, 0.45f) } });
            }
        }

        // ---- Soul skulls: a violet marker on each, through walls; the waystone while you carry one. ----
        private void DrawSouls(float w, float h)
        {
            var cam = player.Camera;
            bool carrying = false;
            var rel = Reliquary.Current;
            if (rel != null && rel.Carrier.Value != player.NetworkObjectId)
            {
                var rs = cam.WorldToScreenPoint(rel.transform.position + Vector3.up * 1.1f);
                float rd = Vector3.Distance(cam.transform.position, rel.transform.position);
                if (rs.z > 0f && (rd < 30f || rel.Carried))
                    GUI.Label(new Rect(rs.x - 120, h - rs.y - 10, 240, 20), (rel.Carried ? "RELIQUARY (carried)  " : "RELIQUARY  ") + Mathf.RoundToInt(rel.Integrity.Value) + "%" + (rd > 8f ? "  " + Mathf.RoundToInt(rd) + "m" : ""),
                        new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = rel.MarkerColor } });
            }
            foreach (var cy in Carryable.All) if (cy != null && cy.Carrier.Value == player.NetworkObjectId) carrying = true;
            foreach (var sk in SoulSkull.Skulls)
            {
                if (sk == null) continue;
                if (sk.Carrier.Value == player.NetworkObjectId) continue;
                var screen = cam.WorldToScreenPoint(sk.transform.position + Vector3.up * 0.9f);
                if (screen.z <= 0f) continue;
                float dist = Vector3.Distance(cam.transform.position, sk.transform.position);
                string text = (sk.Carried ? "SOUL (carried): " : "SOUL: ") + sk.SoulName + (dist > 8f ? "  " + Mathf.RoundToInt(dist) + "m" : "");
                GUI.Label(new Rect(screen.x - 120, h - screen.y - 10, 240, 20), text,
                    new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.78f, 0.6f, 1f) } });
            }
            if (!carrying || DungeonDirector.Layout == null) return;
            var way = DungeonDirector.Layout.WaystonePosition + Vector3.up * 2.9f;
            var ws = cam.WorldToScreenPoint(way);
            float wd = Vector3.Distance(player.transform.position, DungeonDirector.Layout.WaystonePosition);
            if (ws.z > 0f)
                GUI.Label(new Rect(ws.x - 120, h - ws.y - 10, 240, 20), "WAYSTONE  " + Mathf.RoundToInt(wd) + "m",
                    new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.6f, 0.85f, 1f) } });
        }

        // ---- Tells: a red "!" over anything about to strike, bigger when it is coming for you. ----
        private void DrawThreats(float w, float h)
        {
            var cam = player.Camera;
            foreach (var hp in Health.All)
            {
                if (hp == null || hp.Team != Team.Enemies || !hp.IsAlive) continue;
                var brain = hp.GetComponent<EnemyBrain>();
                if (brain == null || (brain.Status.Value & 4) == 0) continue;
                var world = hp.transform.position + Vector3.up * (hp.CenterHeight * 2f + 0.7f);
                var screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f || screen.z > 22f) continue;
                bool atMe = Vector3.Distance(player.transform.position, hp.transform.position) < 4f;
                byte st = brain.Status.Value;
                // Heavy "!!" (sidestep it), sweep "<!>" (back off), lunge ">>" (dodge sideways), plain "!".
                string glyph = (st & 8) != 0 ? "!!" : (st & 16) != 0 ? "<!>" : (st & 32) != 0 ? ">>" : "!";
                if ((st & 32) != 0) atMe = atMe || Vector3.Distance(player.transform.position, hp.transform.position) < 8f;
                float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 18f);
                int size = Mathf.RoundToInt((atMe ? 34 : 24) * pulse);
                var c = atMe ? new Color(1f, 0.25f, 0.15f) : new Color(1f, 0.6f, 0.2f);
                var style = new GUIStyle(big) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };
                var shadow = new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, 0.8f) } };
                var r = new Rect(screen.x - 30, h - screen.y - 26, 60, 40);
                r = new Rect(screen.x - 50, h - screen.y - 26, 100, 40);
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), glyph, shadow);
                GUI.Label(r, glyph, style);
            }
        }

        // ---- Allies: name and health over their heads, through walls; DOWNED pulses. ----
        private void DrawAllies(float w, float h)
        {
            var cam = player.Camera;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p == life) continue;
                var np = p.GetComponent<NetworkPlayer>();
                var world = p.transform.position + Vector3.up * 2.25f;
                var screen = cam.WorldToScreenPoint(world);
                float dist = Vector3.Distance(cam.transform.position, world);
                if (screen.z <= 0f || dist > 60f) continue;
                float y = h - screen.y;
                bool downed = p.Downed.Value;
                var slot = np != null ? np.SlotColor : Color.white;
                float a = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(10f, 60f, dist));
                string label = p.name + (dist > 12f ? "  " + Mathf.RoundToInt(dist) + "m" : "");
                var style = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(slot.r * 0.5f + 0.5f, slot.g * 0.5f + 0.5f, slot.b * 0.5f + 0.5f, a) } };
                GUI.Label(new Rect(screen.x - 80, y - 18, 160, 16), label, style);
                var voice = p.GetComponent<PlayerVoice>();
                if (voice != null && voice.Speaking)
                {
                    // Three little bars that bounce with their voice.
                    float lv = Mathf.Clamp01(voice.Level / 0.12f);
                    float bx = screen.x - 7;
                    for (int k = 0; k < 3; k++)
                    {
                        float bh = 3f + lv * (6f + 6f * Mathf.Abs(Mathf.Sin(Time.time * 14f + k * 1.7f)));
                        Rect(bx + k * 5, y - 22 - bh, 3, bh, new Color(0.45f, 1f, 0.55f, a));
                    }
                }
                if (downed)
                {
                    float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 6f);
                    string downText = p.Soul.Value ? "BLED OUT - carry the soul skull" : "DOWNED " + Mathf.CeilToInt(p.BleedLeft) + "s - hold F";
                    GUI.Label(new Rect(screen.x - 120, y - 2, 240, 18), downText,
                        new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.3f, 0.25f, pulse) } });
                    continue;
                }
                float bw = 54f;
                Rect(screen.x - bw / 2f - 1, y, bw + 2, 6, new Color(0f, 0f, 0f, 0.7f * a));
                Rect(screen.x - bw / 2f, y + 1, bw * p.Health.Fraction, 4, new Color(0.3f, 0.85f, 0.35f, a));
            }
        }

        private float fpsSmoothed = 60f;

        /// <summary>Optional frame-rate readout under the minimap (Options).</summary>
        private void DrawFps(float w)
        {
            fpsSmoothed = Mathf.Lerp(fpsSmoothed, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            if (!Settings.ShowFps) return;
            var c = fpsSmoothed >= 55f ? new Color(0.6f, 1f, 0.6f) : fpsSmoothed >= 30f ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.4f, 0.35f);
            GUI.Label(new Rect(w - 220, Screen.height - 20, 206, 18), Mathf.RoundToInt(fpsSmoothed) + " fps  (" + (1000f / fpsSmoothed).ToString("F1") + " ms)",
                new GUIStyle(small) { alignment = TextAnchor.UpperRight, normal = { textColor = c } });
        }

        private void DrawLobby(float w, float h)
        {
            var director = DungeonDirector.Instance;
            if (director == null || !DungeonDirector.InLobby) return;

            int total = PlayerLife.All.Count, aboard = DungeonDirector.PlayersAboard();
            bool counting = director.PhaseEndsAt.Value > 0;
            bool midRun = director.RunInProgress;
            float pw = 560, x = w / 2f - pw / 2f, y = 10;
            Rect(x, y, pw, counting ? 84 : 62, Panel);

            string title = midRun ? "CASTLE RAVENHOLD   ·   FLOOR " + director.NextDepth.Value + " CLEARED" : "CASTLE RAVENHOLD";
            GUI.Label(new Rect(x, y + 4, pw, 22), title, new GUIStyle(center) { normal = { textColor = Gold } });

            var diff = Enemies.Difficulty.Level;
            bool host = Unity.Netcode.NetworkManager.Singleton.IsHost;
            string cls = ClassDefinition.For(progress.ClassId.Value).Name;
            string line2 = midRun
                ? "Rest, trade and gamble.   The carriage goes down to floor " + (director.NextDepth.Value + 1) + "."
                : cls + " (B to change)   ·   " + diff.Name + " difficulty (" + (host ? "set it with Corwin" : "the host sets it with Corwin") + ")" +
                  (director.RunOmen != Omen.None ? "   ·   Omen: " + DungeonDirector.OmenName(director.RunOmen) : "");
            if (Unity.Netcode.NetworkManager.Singleton.ConnectedClientsIds.Count > 1 || PlayerLife.All.Count > 1)
                line2 += "   ·   Loot: " + (director.FindersKeepers.Value ? "finders keepers" : "shared") + (host ? " (L)" : "");
            GUI.Label(new Rect(x, y + 24, pw, 18), line2, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(x, y + 41, pw, 18), "Talk to Corwin at the carriage, then take a seat on its back   (" + aboard + " / " + total + " seated)",
                new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = aboard > 0 ? Gold : new Color(0.85f, 0.82f, 0.78f) } });
            if (counting)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(director.PhaseEndsAt.Value - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time)));
                GUI.Label(new Rect(x, y + 58, pw, 24), "The carriage departs in " + seconds + "...", new GUIStyle(center) { fontSize = 17, normal = { textColor = Gold } });
            }
        }

        private void DrawFade(float w, float h)
        {
            if (fadeOutStart >= 0f)
            {
                float k = Mathf.Clamp01((Time.time - fadeOutStart) / fadeOutLength);
                if (Time.time - fadeOutStart > fadeOutLength + 3f) fadeOutStart = -1f;
                else Rect(0, 0, w, h, new Color(0f, 0f, 0f, k * k));
            }
            // Departing: bars close in and the light takes the screen.
            if (departStart >= 0f)
            {
                float k = Mathf.Clamp01((Time.time - departStart) / departLength);
                float bar = h * 0.12f * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 3f));
                Rect(0, 0, w, bar, Color.black);
                Rect(0, h - bar, w, bar, Color.black);
                Rect(0, 0, w, h, new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 0.97f, k))));
                if (k > 0.45f && k < 0.98f)
                {
                    float ta = Mathf.Sin((k - 0.45f) / 0.53f * Mathf.PI);
                    UiSkin.Shadowed(new Rect(0, h * 0.78f - 20, w, 40), "INTO THE DEEP", new GUIStyle(big) { normal = { textColor = new Color(0.95f, 0.85f, 0.6f, ta) } });
                    GUI.Label(new Rect(0, h * 0.78f + 26, w, 26), "\u201C" + HymnLine + "\u201D", new GUIStyle(center) { fontSize = 17, fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.85f, 0.78f, 0.62f, ta) } });
                }
                if (k >= 1f && !arrivingFromBeam) departStart = -1f;
                return;
            }
            // Arriving: out of white, letterboxed for a moment.
            if (Time.time < letterboxUntil)
            {
                float k = Mathf.Clamp01((letterboxUntil - Time.time) / 0.8f);
                float bar = h * 0.12f * k;
                Rect(0, 0, w, bar, Color.black);
                Rect(0, h - bar, w, bar, Color.black);
            }
            if (whiteFade > 0f)
            {
                whiteFade = Mathf.MoveTowards(whiteFade, 0f, Time.deltaTime * 0.55f);
                Rect(0, 0, w, h, new Color(1f, 0.97f, 0.88f, Mathf.SmoothStep(0f, 1f, whiteFade)));
            }
            fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime * 0.8f);
            if (fade <= 0f) return;
            Rect(0, 0, w, h, new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, fade)));
        }

        /// <summary>At the back of the carriage: F to sit (or stand).</summary>
        private void DrawSeatPrompt(float w, float h)
        {
            var ride = CarriageRide.Instance;
            if (ride == null || !DungeonDirector.InLobby || ride.Riding || shopOpen || forgeOpen || life.Downed.Value) return;
            string text = null;
            if (life.Seat.Value >= 0) text = "Seated on the carriage   -   F  stand up";
            else if (life.NearestFreeSeat(ride) >= 0) text = "F  Sit on the back of the carriage (ready to go)";
            if (text != null) GUI.Label(new Rect(0, h / 2f + 92, w, 24), text, center);
        }

        private bool NearMerchant() => NearVendor(Shop.Vendor.Merchant);

        private bool NearVendor(Shop.Vendor v) => !life.Downed.Value && Shop.InReach(v, player.transform.position);

        /// <summary>The vendor whose stall you stand at (merchant or shrine; the smith trades through the forge), or null.</summary>
        public Shop.Vendor? DebugStallHere() => StallHere();

        private Shop.Vendor? StallHere()
        {
            if (NearVendor(Shop.Vendor.Merchant)) return Shop.Vendor.Merchant;
            if (NearVendor(Shop.Vendor.Shrine)) return Shop.Vendor.Shrine;
            if (NearVendor(Shop.Vendor.Yard)) return Shop.Vendor.Yard;
            if (NearVendor(Shop.Vendor.Carriage)) return Shop.Vendor.Carriage;
            if (NearVendor(Shop.Vendor.Runewright)) return Shop.Vendor.Runewright;
            return null;
        }

        private RunPhase lastPhase;

        /// <summary>The floor's guardian just fell: how long the floor took, and a record if it was quick.</summary>
        private void WatchFloorTime()
        {
            var dd = DungeonDirector.Instance;
            if (dd == null || Unity.Netcode.NetworkManager.Singleton == null) return;
            var phase = dd.Phase.Value;
            if (lastPhase == RunPhase.Exploring && phase == RunPhase.FloorCleared)
            {
                float took = (float)(Unity.Netcode.NetworkManager.Singleton.ServerTime.Time - dd.FloorBegan.Value);
                bool best = Records.SubmitFloorTime(took);
                Feed((best ? "NEW RECORD - fastest floor: " : "Floor cleared in ") + Records.Clock(took), best ? new Color(1f, 0.85f, 0.4f) : new Color(0.85f, 0.8f, 0.7f));
            }
            lastPhase = phase;
        }

        /// <summary>The Hall of Records slab at camp, read when you stand at it.</summary>
        private void DrawRecords(float w, float h)
        {
            if (!DungeonDirector.InLobby) return;
            // Only when you stand at it and look at it: it is by the road, and people walk past.
            var toSlab = Lobby.RecordsSpot - player.transform.position;
            toSlab.y = 0f;
            if (toSlab.magnitude > 2.6f || Vector3.Dot(player.transform.forward, toSlab.normalized) < 0.55f) return;
            var lines = Records.Lines;
            float pw = 400f, ph = 46f + lines.Length * 22f;
            var r = new Rect(w / 2f - pw / 2f, 96f, pw, ph);
            UiSkin.Panel(r, 0.94f);
            GUI.Label(new Rect(r.x, r.y + 8, pw, 24), "THE HALL OF RECORDS", new GUIStyle(center) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            var st = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontSize = 14, normal = { textColor = new Color(0.92f, 0.88f, 0.8f) } };
            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split(new[] { "  " }, System.StringSplitOptions.RemoveEmptyEntries);
                GUI.Label(new Rect(r.x + 26, r.y + 38 + i * 22, pw - 52, 20), parts[0].Trim(), st);
                GUI.Label(new Rect(r.x + 26, r.y + 38 + i * 22, pw - 52, 20), parts.Length > 1 ? parts[parts.Length - 1].Trim() : "", new GUIStyle(st) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            }
        }

        private void Update()
        {
            WatchFloorTime();
            hurtFlash = Mathf.MoveTowards(hurtFlash, 0f, Time.deltaTime * 1.5f);
            Heartbeat();
            StateGrade();
            Tips.Check(player);

            var keyboard = Keyboard.current;
            var stall = StallHere();
            bool near = stall != null;
            var anvil = NearAnvil();
            // G uses whichever station is closer: a stall or the anvil.
            bool anvilFirst = anvil != null && (!near || Vector3.Distance(player.transform.position, anvil.transform.position) <
                Vector3.Distance(player.transform.position, Shop.Position(stall.Value).Value));
            bool catFirst = keyboard != null && Binds.Down(Bind.Interact) && !MenuOpen &&
                Interactable.Nearest(player.AimOrigin, player.AimForward, progress) is CampCat;
            if (catFirst) ((CampCat)Interactable.Nearest(player.AimOrigin, player.AimForward, progress)).PickUpRpc(player.NetworkObject);
            else if (keyboard != null && Binds.Down(Bind.Interact))
            {
                if (shopOpen && shopVendor == Shop.Vendor.Smith) { shopOpen = false; forgeOpen = false; }
                else if (anvilFirst) { forgeOpen = !forgeOpen; shopOpen = false; }
                else if (near && stall.Value == Shop.Vendor.Runewright) { runeOpen = !runeOpen; shopOpen = false; forgeOpen = false; }
                else if (near) { shopOpen = !shopOpen; forgeOpen = false; runeOpen = false; shopVendor = stall.Value; }
            }
            if (keyboard != null && Binds.Down(Bind.Inventory) && !shopOpen && !forgeOpen && !runeOpen && !CharacterSelect.Open && !PauseMenu.Open && !Wardrobe.Open) invOpen = !invOpen;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) shopOpen = forgeOpen = invOpen = runeOpen = false;
            if (runeOpen && !NearVendor(Shop.Vendor.Runewright)) runeOpen = false;
            // G (when no station is near) stows the gear you are looking at.
            if (keyboard != null && Binds.Down(Bind.Interact) && !near && anvil == null && !MenuOpen &&
                PlayerLife.DownedNear(player.transform.position, life) == null && !PlayerLife.IsDragging(player.NetworkObjectId) &&
                Interactable.Nearest(player.AimOrigin, player.AimForward, progress) is GearDrop stow && !progress.BagFull)
                stow.StowRpc(player.NetworkObject);
            if (keyboard != null && Binds.Down(Bind.Scrap) && !MenuOpen &&
                Interactable.Nearest(player.AimOrigin, player.AimForward, progress) is GearDrop scrap)
            {
                if (anvil != null) scrap.ScrapRpc(player.NetworkObject);
                else Feed("Only Hobb scraps gear: carry it to his anvil at camp (or take it into your pack with F).", new Color(1f, 0.75f, 0.45f));
            }
            if (shopOpen && !NearVendor(shopVendor)) shopOpen = false;
            if (anvil == null) forgeOpen = false;
            bool inspect = keyboard != null && keyboard.leftAltKey.isPressed && !life.Downed.Value;
            MenuOpen = shopOpen || forgeOpen || invOpen || runeOpen || DevMenu.Open || inspect || BlackjackTable.LocalOpen || CharacterSelect.Open || PauseMenu.Open || Wardrobe.Open;
            InventoryOpen = invOpen;
            statsHeld = DebugShowStats || (keyboard != null && Binds.Held(Bind.Stats) && !Wardrobe.Open);

            // The host sets difficulty at camp.
            var dir = DungeonDirector.Instance;
            if (keyboard != null && dir != null && DungeonDirector.InLobby && Unity.Netcode.NetworkManager.Singleton.IsHost)
            {
                // Difficulty and the loot rule are set by talking to Corwin at the carriage (G).
            }

            // Class choice is a camp decision: number keys pick one, only in the lobby.
            // Only before a run: once the first floor is done the class is locked for the run.
            if (keyboard != null && PlayerProgress.ClassChangeAllowed)
            {
                for (int i = 0; i < ClassDefinition.ById.Length && i < 9; i++)
                {
                    var key = keyboard[(Key)((int)Key.Digit1 + i)];
                    if (key.wasPressedThisFrame && progress.ClassId.Value != (ClassId)i)
                    {
                        progress.ChooseClassRpc((ClassId)i);
                        Art.Sfx.Play2D(Art.Sound.Purchase, 0.6f);
                    }
                }
            }

        }

        private void EnsureStyles()
        {
            if (white != null && label != null) return;
            white = Texture2D.whiteTexture;
            UiSkin.Apply();
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            label.normal.textColor = new Color(0.94f, 0.9f, 0.84f);
            small = new GUIStyle(label) { fontSize = 13, fontStyle = FontStyle.Normal };
            big = new GUIStyle(label) { fontSize = 36, alignment = TextAnchor.MiddleCenter, font = UiSkin.TitleFont, fontStyle = FontStyle.Normal };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        }

        private void OnGUI()
        {
            if (player == null) return;
            UiSkin.Apply();
            EnsureStyles();
            if (CharacterSelect.Open) return;

            float w = Screen.width, h = Screen.height;
            if (Cinematic) { DrawFade(w, h); return; }

            DrawEnemyBars();
            DrawRelicTags();
            DrawDamageNumbers();
            DrawCombos();
            DrawCampReadouts(w, h);
            DrawRecords(w, h);
            DrawHidden(w, h);
            DrawPings(w, h);
            DrawVignette(w, h);
            DrawAfflictions(w, h);
            DrawHitDirections(w, h);
            DrawThreats(w, h);
            DrawMicLight(w, h);
            DrawSouls(w, h);
            DrawAllies(w, h);
            DrawCrosshair(w, h);
            DrawVitals(w, h);
            DrawAbilities(w, h);
            DrawBoss(w);
            DrawTally(w, h);
            DrawRoomCleared(w, h);
            DrawRage(w);
            DrawRevive(w, h);
            DrawPhase(w, h);
            DrawBanner(w, h);
            DrawTitleCard(w, h);
            DrawBossIntro(w, h);
            DrawTaunt(w, h);
            DrawPage(w, h);
            // A panel open (forge, stones, shop, journal, sheet, pack): the camp banner, tips, contract and
            // feed step aside, so nothing is drawn across the panel you are reading (UI pass 2026-10-20).
            bool panel = shopOpen || forgeOpen || runeOpen || invOpen || statsHeld || pageShown >= 0;
            if (!statsHeld && !invOpen) DrawControlsHint(w, h);
            if (!panel)
            {
                Tips.Draw(w, h);
                DrawContract();
                DrawLobby(w, h);
                DrawFeed(h);
            }
            DrawForge(w, h);
                DrawMerchant(w, h);
            DrawRunewright(w, h);
            DrawFps(w);
            if (!panel) DrawToasts(w, h);
            if (statsHeld) DrawStatSheet(w, h);
            // Last, so nothing (feed, tips, banners) draws over it.
            if (invOpen) DrawInventory(w, h);
            DrawFade(w, h);
        }

        // ------------------------------------------------------------------

        /// <summary>Relics on the ground carry their name in their own colour, readable across a room.</summary>
        private void DrawRelicTags()
        {
            var cam = Art.ViewCamera.Current;
            if (cam == null) return;
            foreach (var d in LootDrop.All)
            {
                if (d == null || d.Kind.Value != LootKind.Relic) continue;
                var at = d.transform.position + Vector3.up * 1.4f;
                float dist = Vector3.Distance(cam.transform.position, at);
                if (dist > 30f) continue;
                var sp = cam.WorldToScreenPoint(at);
                if (sp.z <= 0f) continue;
                // Only what you can actually see: a wall (or anything solid) between hides the tag.
                if (Physics.Linecast(cam.transform.position, at, out var block, ~0, QueryTriggerInteraction.Ignore) && block.collider.GetComponentInParent<LootDrop>() == null && block.collider.GetComponentInParent<Player.NetworkPlayer>() == null) continue;
                var relic = (Relic)d.Amount.Value;
                var c = Relics.GemColor(relic);
                float a = Mathf.Clamp01((30f - dist) / 8f);
                var r = new Rect(sp.x - 160f, Screen.height - sp.y - 30f, 320f, 22f);
                UiSkin.Shadowed(r, "RELIC:  " + Relics.Name(relic), new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(Mathf.Lerp(c.r, 1f, 0.3f), Mathf.Lerp(c.g, 1f, 0.3f), Mathf.Lerp(c.b, 1f, 0.3f), a) } }, a);
                if (dist < 9f) GUI.Label(new Rect(r.x, r.y + 20f, r.width, 18f), Relics.Describe(relic), new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.8f * a) } });
            }
        }

        /// <summary>Standing on a potion you cannot carry: say why, so it does not look like a bug.</summary>
        private void DrawPouchFull()
        {
            var pouch = player.GetComponent<Flasks>();
            if (pouch == null) return;
            foreach (var d in LootDrop.All)
            {
                if (d == null) continue;
                var k = d.Kind.Value;
                if (k != LootKind.PotionHP && k != LootKind.PotionMP) continue;
                var off = d.transform.position - player.transform.position; off.y *= 0.5f;
                if (off.magnitude > 1.8f) continue;
                var kind = k == LootKind.PotionHP ? Flasks.Kind.Health : Flasks.Kind.Mana;
                if (!pouch.IsFull(kind)) continue;
                GUI.Label(new Rect(0, Screen.height / 2f + 44f, Screen.width, 22f), "POUCH FULL  -  " + (kind == Flasks.Kind.Health ? "Health" : "Mana") + " Potions " + Flasks.Max + "/" + Flasks.Max + "  (drink one to make room)",
                    new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Flasks.ColorOf(kind) } });
                return;
            }
        }

        private float rangeCheckAt;
        private bool outOfRange;

        /// <summary>Aiming at an enemy your attack cannot reach: say so under the crosshair.</summary>
        private void DrawOutOfRange(Vector2 c)
        {
            if (Time.time >= rangeCheckAt)
            {
                rangeCheckAt = Time.time + 0.1f;
                outOfRange = false;
                float reach = player.ActiveKit.AttackRange;
                if (reach > 0f && Physics.Raycast(player.AimOrigin, player.AimForward, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var h = hit.collider.GetComponentInParent<Health>();
                    // Melee reach is measured from your body, ranged from your eye; either way past it, it is out.
                    if (h != null && h.Team == Team.Enemies && h.IsAlive && hit.distance > reach + 0.6f) outOfRange = true;
                }
            }
            if (!outOfRange) return;
            GUI.Label(new Rect(c.x - 100f, c.y + 24f, 200f, 18f), "OUT OF RANGE  (" + player.ActiveKit.AttackRange.ToString(player.ActiveKit.AttackRange < 10f ? "F1" : "F0") + " m)",
                new GUIStyle(small) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.55f, 0.4f, 0.9f) } });
        }

        private static float hitMarkAt = -10f, killMarkAt = -10f;
        private static bool hitMarkHeavy;

        /// <summary>Owner: your hit landed. The crosshair flashes an X (red and larger for a kill).</summary>
        public static void HitMark(bool kill, bool heavy)
        {
            hitMarkAt = Time.time;
            hitMarkHeavy = heavy;
            if (kill)
            {
                killMarkAt = Time.time;
                Art.Sfx.Play2D(Art.Sound.KillTick, 0.35f);
            }
        }

        private void DrawHitMark(Vector2 c)
        {
            float ka = 1f - (Time.time - killMarkAt) / 0.35f, ha = 1f - (Time.time - hitMarkAt) / 0.18f;
            bool kill = ka > 0f;
            float a = kill ? ka : ha;
            if (a <= 0f) return;
            float gap = kill ? 9f : 7f, len = kill ? 9f : hitMarkHeavy ? 7f : 5f;
            var col = kill ? new Color(1f, 0.25f, 0.2f, a) : new Color(1f, 1f, 1f, a * 0.95f);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, c);
            foreach (var d in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) })
            {
                bool horizontal = d.y == 0f;
                float tx = c.x + d.x * gap, ty = c.y + d.y * gap;
                var rr = horizontal ? new Rect(tx + (d.x > 0 ? 0f : -len), ty - 1f, len, 2) : new Rect(tx - 1f, ty + (d.y > 0 ? 0f : -len), 2, len);
                Rect(rr.x - 0.5f, rr.y - 0.5f, rr.width + 1f, rr.height + 1f, new Color(0f, 0f, 0f, a * 0.5f));
                Rect(rr.x, rr.y, rr.width, rr.height, col);
            }
            GUI.matrix = m;
        }

        private UnityEngine.Rendering.Volume stateVolume;
        private float stateWeight;

        /// <summary>Colour drains from the world as your health does, and nearly all of it while you are down.</summary>
        private void StateGrade()
        {
            if (stateVolume == null)
            {
                var go = new GameObject("StateGrade");
                stateVolume = go.AddComponent<UnityEngine.Rendering.Volume>();
                stateVolume.isGlobal = true;
                stateVolume.priority = 20f;
                var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                var grade = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
                grade.saturation.Override(-80f);
                grade.postExposure.Override(0.25f);
                grade.contrast.Override(28f);
                stateVolume.sharedProfile = profile;
                stateVolume.weight = 0f;
            }
            float want = 0f;
            if (life != null && life.Downed.Value) want = 1f;
            else if (health != null && health.Fraction > 0f) want = Mathf.InverseLerp(0.4f, 0.08f, health.Fraction) * 0.55f;
            stateWeight = Mathf.MoveTowards(stateWeight, want, Time.deltaTime * (want > stateWeight ? 2f : 0.8f));
            stateVolume.weight = stateWeight;
        }

        private float nextBeat;

        /// <summary>Below a third of your health, you hear your heart (in time with the vignette's pulse).</summary>
        private void Heartbeat()
        {
            if (health == null || health.Fraction <= 0f || health.Fraction >= 0.33f || life.Downed.Value) return;
            if (Time.time < nextBeat) return;
            float interval = Mathf.Lerp(0.6f, 1.15f, health.Fraction * 3f);
            nextBeat = Time.time + interval;
            Art.Sfx.Play2D(Art.Sound.Heartbeat, Mathf.Lerp(0.55f, 0.25f, health.Fraction * 3f));
        }

        private void DrawCrosshair(float w, float h)
        {
            if (life.Downed.Value) return;
            var c = new Vector2(w / 2f, h / 2f);
            DrawHitMark(new Vector2(Mathf.Round(c.x), Mathf.Round(c.y)));
            DrawOutOfRange(c);

            // Charge meter (bow draw) under the crosshair; gold when full.
            float charge = player.ActiveKit.Charge;
            if (charge > 0f)
            {
                Rect(c.x - 31, c.y + 17, 62, 6, new Color(0f, 0f, 0f, 0.7f));
                Rect(c.x - 30, c.y + 18, 60 * charge, 4, charge >= 1f ? Gold : new Color(0.8f, 0.85f, 0.7f));
            }
            // A centred dot and four even ticks, with a dark outline so it reads on bright stone too.
            float cx = Mathf.Round(c.x), cy = Mathf.Round(c.y);
            var outline = new Color(0f, 0f, 0f, 0.55f);
            var tick = new Color(1f, 1f, 1f, 0.6f);
            Rect(cx - 2f, cy - 2f, 4, 4, outline);
            Rect(cx - 1f, cy - 1f, 2, 2, new Color(1f, 1f, 1f, 0.95f));
            foreach (var d in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) })
            {
                float tx = cx + d.x * 8f, ty = cy + d.y * 8f;
                bool horizontal = d.y == 0f;
                var rr = horizontal ? new Rect(tx + (d.x > 0 ? 0f : -5f), ty - 0.5f, 5, 1) : new Rect(tx - 0.5f, ty + (d.y > 0 ? 0f : -5f), 1, 5);
                Rect(rr.x - 0.5f, rr.y - 0.5f, rr.width + 1f, rr.height + 1f, outline);
                Rect(rr.x, rr.y, rr.width, rr.height, tick);
            }
        }

        private void DrawVitals(float w, float h)
        {
            float x = 24, y = h - 110, bw = 300;
            DrawRelicRow(x, y - 44);
            // What is affecting you: badges, hover (ALT) to read them.
            StatusEffects.Draw(player, x, progress.RelicCount > 0 ? y - 84 : y - 50, MenuOpen);
            Rect(x - 8, y - 10, bw + 16, 96, Panel);

            float hp = health.Current.Value, max = Mathf.Max(1f, health.Max.Value);
            Bar(x, y, bw, 22, hp / max, HealthRed, Trail(ref hpTrail, ref hpHold, hp / max));
            // Aegis shield, drawn as a gold band over the health bar.
            if (health.Shield.Value > 0f)
            {
                Rect(x, y, bw * Mathf.Clamp01(health.Shield.Value / max), 7, new Color(1f, 0.85f, 0.4f, 0.95f));
            }
            GUI.Label(new Rect(x + 8, y + 1, bw, 22), Mathf.CeilToInt(hp) + " / " + Mathf.CeilToInt(max), label);

            // Low health: the frame pulses red, faster the lower it goes.
            float frac = hp / max;
            if (frac < 0.3f && health.IsAlive)
            {
                float beat = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(9f, 5f, frac / 0.3f));
                var pc = new Color(1f, 0.15f, 0.1f, 0.35f + 0.5f * beat);
                Rect(x - 8, y - 10, bw + 16, 2, pc); Rect(x - 8, y + 84, bw + 16, 2, pc);
                Rect(x - 8, y - 10, 2, 96, pc); Rect(x + bw + 6, y - 10, 2, 96, pc);
            }
            var kit = player.ActiveKit;
            Bar(x, y + 32, bw, 12, kit.ResourceMax > 0f ? kit.Resource / kit.ResourceMax : 0f, kit.ResourceColor);
            GUI.Label(new Rect(x + 6, y + 29, bw - 12, 16), kit.ResourceName.ToUpperInvariant() + "  " + Mathf.FloorToInt(kit.Resource) + " / " + Mathf.RoundToInt(kit.ResourceMax),
                new GUIStyle(small) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1f, 1f, 1f, 0.85f) } });
            string cls = progress.Class.Name.ToUpperInvariant();
            string weapon = ClassDefinition.WeaponName(progress.ClassId.Value) + " +" + progress.WeaponLevel.Value;
            GUI.Label(new Rect(x, y + 46, bw, 20), (DungeonDirector.InLobby ? cls + "  ·  At camp" : cls + "  ·  Floor " + (CurrentDepth() + 1) + " / " + DungeonDirector.FloorsPerRun + (CurrentDepth() >= DungeonDirector.AbyssDepth ? " (ABYSS)" : ""))
                + (DungeonDirector.Instance != null && DungeonDirector.Instance.SealCount > 0 ? "  ·  Seals " + DungeonDirector.Instance.SealCount + "/" + DungeonDirector.SealTotal : "") + "  ·  " + weapon, small);
            GUI.Label(new Rect(x, y + 64, bw, 20), progress.Embers.Value + " embers", new GUIStyle(small) { normal = { textColor = LootInfo.EmberColor } });
            if (progress.Sigils.Value > 0)
                GUI.Label(new Rect(x + 90, y + 64, bw, 20), progress.Sigils.Value + (progress.Sigils.Value == 1 ? " sigil" : " sigils"), new GUIStyle(small) { normal = { textColor = LootInfo.SigilColor } });
            GUI.Label(new Rect(x, y + 64, bw, 20), progress.Gold.Value + " gold", new GUIStyle(small) { alignment = TextAnchor.UpperRight, normal = { textColor = Gold } });
        }

        private void DrawAbilities(float w, float h)
        {
            float size = 58, gap = 10;
            float total = size * 7 + gap * 6;
            float x = w / 2f - total / 2f, y = h - size - 26;

            abilities.Clear();
            player.ActiveKit.FillAbilities(abilities);
            abilities.Add(new AbilityView { Key = "CTRL", Name = "Dodge", Remaining = life.DodgeReadyAt - Time.time, Cooldown = life.DodgeCooldownNow });
            var kick = player.GetComponent<Kick>();
            if (kick != null) abilities.Add(new AbilityView { Key = "V", Name = "Kick", Remaining = kick.ReadyAt - Time.time, Cooldown = Kick.Cooldown });
            for (int i = 0; i < abilities.Count; i++)
            {
                var a = abilities[i];
                Ability(x + (size + gap) * i, y, size, Binds.Show(a.Key), a.Name, a.Remaining, a.Cooldown, a.Active, a.Unaffordable);
            }
            DrawUltimate(x + (size + gap) * abilities.Count, y, size);

            // The potion pouch, just above the bar: four little bottles with their counts.
            var belt = player.GetComponent<Flasks>();
            if (belt != null)
            {
                float bw = 74f, bx = w / 2f - bw * 2f;
                for (int k = 0; k < Flasks.All.Length; k++)
                {
                    var kind = Flasks.All[k];
                    var c = Flasks.ColorOf(kind);
                    int n = belt.Count(kind);
                    float sx = bx + k * bw;
                    Rect(sx + 2, y - 30, bw - 4, 24, new Color(0f, 0f, 0f, 0.5f));
                    // The bottle: a filled body, dim when empty.
                    var bc = n > 0 ? c : c * 0.35f;
                    Rect(sx + 8, y - 25, 10, 14, bc);
                    Rect(sx + 11, y - 29, 4, 4, n > 0 ? new Color(0.6f, 0.45f, 0.3f) : new Color(0.3f, 0.25f, 0.2f));
                    if (kind == Flasks.Kind.Health && belt.Healing) Rect(sx + 2, y - 7, (bw - 4) * Mathf.PingPong(Time.time * 2f, 1f), 2, c);
                    GUI.Label(new Rect(sx + 22, y - 30, bw - 24, 24), (k + 1) + "  " + n + "/" + Flasks.Max,
                        new GUIStyle(small) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = n > 0 ? Color.white : new Color(0.6f, 0.6f, 0.6f) } });
                }
            }
        }

        /// <summary>R: the ultimate, a slot that fills from the bottom as you fight and glows when full.</summary>
        private void DrawUltimate(float x, float y, float size)
        {
            var ult = player.GetComponent<Ultimates>();
            if (ult == null) return;
            var cls = progress.ClassId.Value;
            float k = Mathf.Clamp01(ult.Charge.Value / Ultimates.ChargeMax);
            var c = Ultimates.UltColor(cls);
            UiSkin.Panel(new Rect(x, y, size, size), 0.9f);
            Rect(x + 3, y + 3 + (size - 6) * (1f - k), size - 6, (size - 6) * k, new Color(c.r, c.g, c.b, ult.Ready ? 0.45f + 0.2f * Mathf.Sin(Time.time * 5f) : 0.22f));
            if (ult.Ready)
            {
                Rect(x - 3, y - 3, size + 6, 2, c); Rect(x - 3, y + size + 1, size + 6, 2, c);
                Rect(x - 3, y - 3, 2, size + 6, c); Rect(x + size + 1, y - 3, 2, size + 6, c);
            }
            GUI.Label(new Rect(x, y + 6, size, 20), Binds.Live(Bind.Ultimate), center);
            string label = ult.Ready ? "READY" : Mathf.FloorToInt(k * 100f) + "%";
            GUI.Label(new Rect(x - 10, y + size - 22, size + 20, 18), label, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = ult.Ready ? FontStyle.Bold : FontStyle.Normal });
        }

        private static Font glyphFont;
        private readonly Dictionary<string, float> readyFlash = new Dictionary<string, float>();
        private readonly Dictionary<string, float> lastRemaining = new Dictionary<string, float>();

        /// <summary>A symbol for an ability, picked from its name (OS symbol font; plain text if missing).</summary>
        private static string GlyphFor(string name)
        {
            var n = name.ToLowerInvariant();
            if (n.Contains("frost") || n.Contains("ice") || n.Contains("nova")) return "❄";
            if (n.Contains("meteor") || n.Contains("cataclysm")) return "☄";
            if (n.Contains("fire") || n.Contains("flame")) return "✹";
            if (n.Contains("blink")) return "✧";
            if (n.Contains("shadow") || n.Contains("step")) return "☾";
            if (n.Contains("smoke")) return "☁";
            if (n.Contains("smite")) return "☀";
            if (n.Contains("consecr")) return "✠";
            if (n.Contains("beam") || n.Contains("mend") || n.Contains("heal")) return "✚";
            if (n.Contains("aegis") || n.Contains("block") || n.Contains("bash") || n.Contains("shield") || n.Contains("guard")) return "⛨";
            if (n.Contains("cry") || n.Contains("taunt") || n.Contains("roar")) return "❗";
            if (n.Contains("mark")) return "◎";
            if (n.Contains("trap")) return "❂";
            if (n.Contains("knife") || n.Contains("knives") || n.Contains("fan")) return "✦";
            if (n.Contains("bow") || n.Contains("shot") || n.Contains("arrow") || n.Contains("volley") || n.Contains("draw")) return "➶";
            if (n.Contains("dodge") || n.Contains("roll")) return "↻";
            if (n.Contains("kick")) return "✷";
            if (n.Contains("swing") || n.Contains("dagger") || n.Contains("strike") || n.Contains("slash") || n.Contains("stab")) return "⚔";
            return "✦";
        }

        private void Ability(float x, float y, float size, string key, string name, float remaining, float cooldown, bool active = false, bool unaffordable = false)
        {
            UiSkin.Panel(new Rect(x, y, size, size), 0.9f);
            // The ability's symbol, faint in the middle of the slot.
            if (glyphFont == null) { try { glyphFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Symbol", "Arial Unicode MS", "Arial" }, 26); } catch { } }
            bool cooling = remaining > 0f && cooldown > 0f;
            var glyphCol = unaffordable ? new Color(0.5f, 0.6f, 0.9f, 0.5f) : cooling ? new Color(0.7f, 0.65f, 0.55f, 0.35f) : new Color(0.95f, 0.8f, 0.5f, 0.7f);
            GUI.Label(new Rect(x, y + 13, size, size - 30), GlyphFor(name), new GUIStyle(GUI.skin.label) { font = glyphFont, fontSize = 28, alignment = TextAnchor.MiddleCenter, normal = { textColor = glyphCol } });
            // A flash when it comes off cooldown.
            if (lastRemaining.TryGetValue(name, out var was) && was > 0f && remaining <= 0f && cooldown > 0f) readyFlash[name] = Time.time;
            lastRemaining[name] = remaining;
            if (readyFlash.TryGetValue(name, out var at) && Time.time - at < 0.45f)
            {
                float f = 1f - (Time.time - at) / 0.45f;
                var fc = new Color(1f, 0.9f, 0.6f, 0.8f * f);
                float g = 2f + 5f * (1f - f);
                Rect(x - g, y - g, size + 2 * g, 2, fc); Rect(x - g, y + size + g - 2, size + 2 * g, 2, fc);
                Rect(x - g, y - g, 2, size + 2 * g, fc); Rect(x + size + g - 2, y - g, 2, size + 2 * g, fc);
                Rect(x, y, size, size, new Color(1f, 0.85f, 0.5f, 0.18f * f));
            }
            if (active)
            {
                Rect(x - 3, y - 3, size + 6, 2, Gold); Rect(x - 3, y + size + 1, size + 6, 2, Gold);
                Rect(x - 3, y - 3, 2, size + 6, Gold); Rect(x + size + 1, y - 3, 2, size + 6, Gold);
            }
            if (unaffordable) Rect(x, y, size, size, new Color(0.1f, 0.15f, 0.4f, 0.45f));
            GUI.Label(new Rect(x, y + 6, size, 20), key, center);
            GUI.Label(new Rect(x, y + size - 22, size, 18), name, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });

            if (remaining > 0f && cooldown > 0f)
            {
                float k = Mathf.Clamp01(remaining / cooldown);
                Rect(x, y + size * (1f - k), size, size * k, new Color(0f, 0f, 0f, 0.65f));
                // A thin progress line along the bottom edge, and tenths in the last three seconds.
                Rect(x + 2, y + size - 4, (size - 4) * (1f - k), 2, new Color(1f, 0.82f, 0.45f, 0.85f));
                GUI.Label(new Rect(x, y, size, size), remaining < 3f ? remaining.ToString("0.0") : Mathf.CeilToInt(remaining).ToString(), new GUIStyle(big) { fontSize = remaining < 3f ? 18 : 22 });
            }
            else if (!unaffordable && cooldown > 0f)
            {
                // Ready: a faint warm rim, so a glance tells ready from cooling.
                var rc = new Color(1f, 0.8f, 0.45f, 0.35f);
                Rect(x, y, size, 1, rc); Rect(x, y + size - 1, size, 1, rc);
            }
        }

        private EnemyBrain rageFocus;
        private float rageFocusUntil;

        /// <summary>
        /// The enemy whose rage the meter shows: the boss in a fight; else an elite or champion under your
        /// crosshair (kept a few seconds after you look away), else one that is hunting you.
        /// </summary>
        private EnemyBrain RageFocus()
        {
            var boss = BossBrain.Active;
            if (boss != null && !boss.IsDead && (boss.transform.position - player.transform.position).sqrMagnitude < 40f * 40f && boss.Rage.Value.Count > 0) return boss;
            var cam = player.Camera;
            EnemyBrain best = null;
            float bestAngle = 9f;
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive || !h.TryGetComponent<EnemyBrain>(out var e)) continue;
                if (!(e.Elite.Value || e.IsChampion) || e.Rage.Value.Count == 0) continue;
                var to = h.Center - cam.transform.position;
                if (to.sqrMagnitude > 28f * 28f) continue;
                float ang = Vector3.Angle(cam.transform.forward, to);
                if (e.Rage.Value.Target == player.NetworkObjectId && to.sqrMagnitude < 15f * 15f) ang = Mathf.Min(ang, 8.5f);
                if (ang < bestAngle) { bestAngle = ang; best = e; }
            }
            if (best != null) { rageFocus = best; rageFocusUntil = Time.time + 3f; return best; }
            if (rageFocus != null && !rageFocus.IsDead && Time.time < rageFocusUntil) return rageFocus;
            return null;
        }

        /// <summary>The rage meter: who it hates, by how much, and who it is after.</summary>
        private void DrawRage(float w)
        {
            var e = RageFocus();
            if (e == null) return;
            var v = e.Rage.Value;
            if (v.Count == 0) return;
            bool boss = e is BossBrain;
            float pw = 300f, x = w / 2f - pw / 2f, y = boss ? 116f : 62f; // a boss: below its bar, stance and hint lines
            var rows = new List<(ulong id, float t)>();
            float top = 1f;
            for (int i = 0; i < 4; i++) { v.Get(i, out var id, out var t); if (id != 0) { rows.Add((id, t)); top = Mathf.Max(top, t); } }
            rows.Sort((a, b) => b.t.CompareTo(a.t));
            float ph = 26f + rows.Count * 18f;
            Rect(x, y, pw, ph, new Color(0.04f, 0.035f, 0.03f, 0.82f));
            string who = boss ? ((BossBrain)e).DisplayName : e.IsChampion ? e.Title.Value.ToString() : "Elite " + Health.NameOf(e.NetworkObjectId).Replace("Elite ", "");
            GUI.Label(new Rect(x + 8, y + 3, pw - 16, 18), "RAGE  -  " + who, new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.45f, 0.35f) } });
            var nm = Unity.Netcode.NetworkManager.Singleton;
            float targetThreat = v.Of(v.Target);
            for (int i = 0; i < rows.Count; i++)
            {
                var (id, t) = rows[i];
                float ry = y + 23f + i * 18f;
                string name = "?";
                Color col = Color.gray;
                if (nm != null && nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj) && obj != null)
                {
                    name = obj.name.Replace("(Clone)", "").Trim();
                    if (obj.TryGetComponent<NetworkPlayer>(out var np)) col = np.SlotColor;
                    if (obj.TryGetComponent<PlayerProgress>(out var pp)) name += " (" + pp.Class.Name + ")";
                }
                bool me = id == player.NetworkObjectId, hunted = id == v.Target;
                GUI.Label(new Rect(x + 8, ry, 130, 18), name, new GUIStyle(small) { fontStyle = me ? FontStyle.Bold : FontStyle.Normal, normal = { textColor = me ? Color.white : new Color(0.82f, 0.8f, 0.75f) } });
                var bar = new Rect(x + 140, ry + 4, pw - 220, 9);
                Rect(bar.x - 1, bar.y - 1, bar.width + 2, bar.height + 2, new Color(0f, 0f, 0f, 0.8f));
                Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(t / top), bar.height, new Color(col.r, col.g, col.b, 0.95f));
                // The line a rival must cross to take its rage (a fifth over whoever it is hunting).
                if (!hunted && targetThreat > 0f)
                {
                    float line = Mathf.Clamp01(targetThreat * EnemyBrain.SwitchThreshold / top);
                    Rect(bar.x + bar.width * line - 1f, bar.y - 2f, 2f, bar.height + 4f, new Color(1f, 0.4f, 0.3f, 0.7f));
                }
                if (hunted) GUI.Label(new Rect(x + pw - 76, ry, 72, 18), "HUNTED", new GUIStyle(small) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight, normal = { textColor = new Color(1f, 0.35f, 0.3f) } });
            }
            // A word for you: about to pull it, or already its prey.
            float mine = v.Of(player.NetworkObjectId);
            string warn = null;
            if (v.Target == player.NetworkObjectId && progress.ClassId.Value != Classes.ClassId.Knight && rows.Count > 1) warn = "It is coming for YOU";
            else if (v.Target != player.NetworkObjectId && targetThreat > 0f && mine > targetThreat * EnemyBrain.SwitchThreshold * 0.85f) warn = "Careful: you are about to pull its rage";
            if (warn != null)
                GUI.Label(new Rect(x, y + ph + 2f, pw, 18), warn, new GUIStyle(small) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.6f, 0.3f) } });
        }

        /// <summary>
        /// The Tallyman (2026-10-20): after the bell, a countdown to his coming; once he is here, how far
        /// off he is (red when close) and a green mark where he walks, through walls, within 45 m.
        /// </summary>
        private void DrawTally(float w, float h)
        {
            var dd = DungeonDirector.Instance;
            if (dd == null || DungeonDirector.InLobby || BossIntroShowing || Unity.Netcode.NetworkManager.Singleton == null) return;
            var green = new Color(0.62f, 1f, 0.7f);
            var tm = Tallyman.Active;
            if (dd.TallyStage.Value == 1 && dd.Phase.Value == RunPhase.Exploring && !dd.BossSealed)
            {
                double left = dd.FloorBegan.Value + DungeonDirector.TallyArriveSeconds - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time;
                if (left <= 0) return;
                int sec = Mathf.CeilToInt((float)left);
                var st = new GUIStyle(center) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = new Color(green.r, green.g, green.b, 0.6f + 0.3f * Mathf.Sin(Time.time * 2f)) } };
                GUI.Label(new Rect(0, 8, w, 22), "THE COUNT   " + sec / 60 + ":" + (sec % 60).ToString("00"), st);
                return;
            }
            if (tm == null || tm.Leaving.Value) return;
            float d = Vector3.Distance(player.transform.position, tm.transform.position);
            bool close = d < 12f;
            if (d < 20f) Bestiary.See("Tallyman");
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (close ? 9f : 3f));
            var col = close ? Color.Lerp(new Color(1f, 0.3f, 0.25f), Color.white, pulse * 0.3f) : green;
            var big = new GUIStyle(center) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = col } };
            Rect(w / 2f - 130, 6, 260, 26, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(0, 8, w, 22), "THE TALLYMAN   " + Mathf.RoundToInt(d) + " m", big);
            if (close) Rect(0, 0, w, h, new Color(0.1f, 0.4f, 0.2f, 0.06f + 0.06f * pulse));
            var cam = player.Camera;
            if (cam == null || d > 45f) return;
            var sp = cam.WorldToScreenPoint(tm.transform.position + Vector3.up * 3.1f);
            if (sp.z <= 0f) return;
            float y = h - sp.y;
            Rect(sp.x - 5, y - 5, 10, 10, new Color(col.r, col.g, col.b, 0.85f));
            Rect(sp.x - 3, y - 3, 6, 6, new Color(0f, 0f, 0f, 0.8f));
        }

        private void DrawBoss(float w)
        {
            var boss = BossBrain.Active;
            if (boss == null || boss.IsDead) return;

            var room = DungeonDirector.Layout?.BossRoom;
            bool inRoom = room != null && room.Contains(player.transform.position, 1f);
            bool hurt = boss.Health.Fraction < 0.999f;
            if (!inRoom && !hurt) return;

            float bw = Mathf.Min(620f, w * 0.6f), x = w / 2f - bw / 2f, y = 28;
            GUI.Label(new Rect(x, y - 4, bw, 24), boss.DisplayName, new GUIStyle(center) { normal = { textColor = Gold } });
            Rect(x - 3, y + 21, bw + 6, 20, Panel);
            var stance = boss.CurrentStance;
            var barColor = boss.Immune ? new Color(0.35f, 0.45f, 0.62f) : stance == BossBrain.Stance.Armored ? new Color(0.45f, 0.45f, 0.5f) : new Color(0.55f, 0.08f, 0.1f);
            Bar(x, y + 24, bw, 14, boss.Health.Fraction, barColor, Trail(ref bossTrail, ref bossHold, boss.Health.Fraction));
            // Where its phases turn: a notch at each gate (lit gold once passed).
            foreach (float g in boss.Gates)
            {
                bool passed = boss.Health.Fraction < g;
                Rect(x + bw * g - 1, y + 21, 2, 20, passed ? new Color(1f, 0.82f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.55f));
            }
            // Gold trim top and bottom, the percentage, and the party it is sized for.
            Rect(x - 3, y + 20, bw + 6, 1, new Color(0.8f, 0.62f, 0.3f, 0.8f));
            Rect(x - 3, y + 41, bw + 6, 1, new Color(0.8f, 0.62f, 0.3f, 0.8f));
            GUI.Label(new Rect(x, y + 23, bw - 6, 16), Mathf.CeilToInt(boss.Health.Fraction * 100f) + "%", new GUIStyle(small) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1f, 1f, 1f, 0.9f) } });
            int party = 0;
            foreach (var pl in Player.PlayerLife.All) if (pl != null) party++;
            GUI.Label(new Rect(x + 6, y + 23, bw, 16), party <= 1 ? "SOLO" : "PARTY OF " + party, new GUIStyle(small) { fontSize = 10, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 1f, 1f, 0.6f) } });
            // Phase name on the right of the title row.
            string phase = boss.PhaseName.Value.ToString();
            if (phase.Length > 0)
                GUI.Label(new Rect(x, y - 4, bw, 24), phase, new GUIStyle(small) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.85f, 0.8f, 0.7f) } });
            string tags = StatusTags(boss.Health, boss);
            string stanceTag = null;
            double left = boss.StanceEndsAt.Value > 0.0 && Unity.Netcode.NetworkManager.Singleton != null ? boss.StanceEndsAt.Value - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time : 0.0;
            switch (stance)
            {
                case BossBrain.Stance.Transition: stanceTag = "<color=#9DB8FF>INVULNERABLE</color>"; break;
                case BossBrain.Stance.Invulnerable: stanceTag = "<color=#9DB8FF>WARDED</color>" + (boss.WardsLeft.Value > 0 ? "  (" + boss.WardsLeft.Value + " left)" : ""); break;
                case BossBrain.Stance.Survival: stanceTag = "<color=#FFB070>SURVIVE " + Mathf.CeilToInt((float)System.Math.Max(0.0, left)) + "</color>"; break;
                case BossBrain.Stance.Armored: stanceTag = "<color=#C8C8D0>ARMORED</color>"; break;
            }
            if (stanceTag != null) tags = stanceTag + (tags.Length > 0 ? "   " + tags : "");
            else if (boss.Health.Fraction < 0.25f) tags = "<color=#E0403A>ENRAGED</color>" + (tags.Length > 0 ? "   " + tags : "");
            string hint = boss.PhaseHint.Value.ToString();
            if (hint.Length > 0 && (boss.Immune || stance == BossBrain.Stance.Armored || Time.time - tauntAt < 12f))
                GUI.Label(new Rect(x - 60, y + 58, bw + 120, 20), hint, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.85f, 1f) } });
            if (tags.Length > 0)
                GUI.Label(new Rect(x, y + 40, bw, 20), tags, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, richText = true, fontStyle = FontStyle.Bold });
        }

        /// <summary>
        /// A gear piece: name in its quality colour, each stat with the change against what
        /// you wear now, and its effect. Used for floor drops and the Tab sheet.
        /// </summary>
        private void DrawGearCard(GearItem item, float x, float y, bool compare)
        {
            const float cw = 360;
            var slot = Gear.SlotOf(item.Kind);
            var current = progress.Equipped(slot);
            var stats = new List<StatType>(Gear.StatsOf(item));
            if (compare) foreach (var s in Gear.StatsOf(current)) if (!stats.Contains(s)) stats.Add(s);
            float ch = 66 + stats.Count * 18 + (compare ? 40 : 0);
            var qc = Gear.QualityColor(item.Quality);
            Rect(x - 2, y - 2, cw + 4, ch + 4, new Color(qc.r, qc.g, qc.b, 0.55f));
            Rect(x, y, cw, ch, new Color(0.04f, 0.035f, 0.03f, 0.93f));
            GUI.Label(new Rect(x + 10, y + 6, cw - 20, 22), Gear.Name(item), new GUIStyle(label) { normal = { textColor = qc } });
            GUI.Label(new Rect(x + 10, y + 6, cw - 20, 22), slot.ToString().ToUpperInvariant(), new GUIStyle(small) { alignment = TextAnchor.UpperRight });

            float ry = y + 30;
            foreach (var s in stats)
            {
                float v = Gear.StatValue(item, s), diff = v - Gear.StatValue(current, s);
                GUI.Label(new Rect(x + 10, ry, 120, 18), (v >= 0 ? "+" : "") + v + " " + Stats.ShortName(s), small);
                if (compare && !current.IsEmpty && Mathf.Abs(diff) > 0.01f)
                    GUI.Label(new Rect(x + 130, ry, 80, 18), (diff > 0 ? "(+" : "(-") + Mathf.Abs(diff) + ")", new GUIStyle(small) { normal = { textColor = diff > 0 ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.45f, 0.4f) } });
                ry += 18;
            }
            GUI.Label(new Rect(x + 10, ry + 2, cw - 20, 34), Gear.Effect(item.Kind), new GUIStyle(small) { wordWrap = true, normal = { textColor = Gold } });
            if (compare)
            {
                GUI.Label(new Rect(x + 10, ry + 34, cw - 20, 18), "Wearing: " + (current.IsEmpty ? "nothing" : Gear.Name(current)), small);
                GUI.Label(new Rect(x + 10, ry + 52, cw - 20, 18), "X   scrap for " + Gear.ScrapValue(item) + " embers" + (progress.BagFull ? "" : "      G   stow in pack"), new GUIStyle(small) { normal = { textColor = LootInfo.EmberColor } });
            }
        }

        private void DrawRevive(float w, float h)
        {
            if (!life.Downed.Value) recapKiller = null;
            if (life.Downed.Value)
            {
                if (life.Soul.Value)
                {
                    Rect(0, 0, w, h, new Color(0.12f, 0.04f, 0.22f, 0.45f));
                    GUI.Label(new Rect(0, h / 2f - 70, w, 40), "YOU HAVE BLED OUT", big);
                    var skull = SoulSkull.For(player.NetworkObjectId);
                    string where = skull != null && skull.Carried ? "An ally is carrying your soul skull to the waystone..." : "Your soul skull lies where you fell. An ally must carry it to the waystone in the start room.";
                    GUI.Label(new Rect(0, h / 2f - 30, w, 24), where, center);
                    if (skull != null && skull.Ritual.Value > 0f)
                        ProgressBar(w, h / 2f + 4, skull.Ritual.Value / SoulSkull.RitualSeconds, new Color(0.7f, 0.5f, 1f));
                    DrawRecap(w, h);
                    return;
                }
                Rect(0, 0, w, h, new Color(0.35f, 0f, 0f, 0.25f));
                GUI.Label(new Rect(0, h / 2f - 70, w, 40), "YOU ARE DOWN", big);
                var dragger = life.BeingDragged && Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(life.DraggedBy.Value, out var dob) ? dob.name : null;
                GUI.Label(new Rect(0, h / 2f - 30, w, 24), dragger != null ? dragger + " is dragging you to safety (you bleed slower)" : "A teammate must hold F next to you to revive you, or drag you clear (G)", center);
                ProgressBar(w, h / 2f + 4, life.ReviveProgress.Value / PlayerLife.ReviveSeconds, new Color(0.6f, 0.9f, 1f));
                if (life.Bleed.Value > 0f)
                {
                    float left = life.BleedLeft;
                    var bc = left < 10f ? new Color(1f, 0.3f, 0.25f, 0.6f + 0.4f * Mathf.Sin(Time.time * 8f)) : new Color(1f, 0.6f, 0.5f);
                    GUI.Label(new Rect(0, h / 2f + 22, w, 24), "Bleeding out in " + Mathf.CeilToInt(left) + "s", new GUIStyle(center) { normal = { textColor = bc } });
                }
                DrawRecap(w, h);
                return;
            }

            foreach (var sk in Carryable.All)
            {
                if (sk == null || sk.Carrier.Value != player.NetworkObjectId) continue;
                string msg = sk.Ritual.Value > 0f ? "The waystone stirs... (don't get hit)" : "Carrying " + sk.ItemName + ": bring it to the waystone (F sets it down)";
                if (sk is Reliquary rq) msg += "   Intact " + Mathf.RoundToInt(rq.Integrity.Value) + "%";
                GUI.Label(new Rect(0, h / 2f + 96, w, 24), msg, new GUIStyle(center) { normal = { textColor = sk.MarkerColor } });
                if (sk.Ritual.Value > 0f) ProgressBar(w, h / 2f + 124, sk.Ritual.Value / sk.DeliverSeconds, sk.MarkerColor);
            }

            var hauled = PlayerLife.DraggedByPlayer(player.NetworkObjectId);
            if (hauled != null)
                GUI.Label(new Rect(0, h / 2f + 96, w, 24), "Dragging " + hauled.name + " (" + Binds.Live(Bind.Interact) + " lets go; stop and hold " + Binds.Live(Bind.Use) + " to revive)",
                    new GUIStyle(center) { normal = { textColor = new Color(0.6f, 0.9f, 1f) } });

            var target = PlayerLife.DownedNear(player.transform.position, life);
            if (target == null)
            {
                var use = Interactable.Nearest(player.AimOrigin, player.AimForward, progress);
                if (use != null && !statsHeld) PromptLine(w, h, "F   " + use.Prompt(progress));
                if (use is GearDrop gear && !statsHeld) DrawGearCard(gear.Item.Value, w / 2f - 180, h / 2f + 70, true);
                return;
            }

            PromptLine(w, h, "Hold F to revive " + target.name);
            if (hauled == null && !target.BeingDragged) PromptLine(w, h, "Press G to drag " + target.name + " clear", 100f);
            ProgressBar(w, h / 2f + 68, target.ReviveProgress.Value / PlayerLife.ReviveSeconds, new Color(0.6f, 0.9f, 1f));
        }

        private float clearedSeenAt = -1f;

        private void DrawPhase(float w, float h)
        {
            var director = DungeonDirector.Instance;
            // Wipes and victories get the full run summary instead.
            if (director == null || director.Phase.Value != RunPhase.FloorCleared) { clearedSeenAt = -1f; return; }
            // The boss's own card ("... FALLS") has the screen first.
            if (cardTitle != null && Time.time < cardUntil) return;

            int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(director.PhaseEndsAt.Value - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time)));
            string title, sub;
            switch (director.Phase.Value)
            {
                case RunPhase.FloorCleared:
                    title = director.ExitFinal.Value ? "THE DEPTHS ARE CLEARED" : "THE FLOOR IS CLEARED";
                    int at = DungeonDirector.PlayersAtExit(), all = PlayerLife.All.Count;
                    sub = director.PhaseEndsAt.Value > 0 ? (director.ExitFinal.Value ? "Ending the run in " : "The lift rises in ") + seconds + "..."
                        : "Explore and loot at your leisure. The GRAVE LIFT waits where the guardian stood: everyone into the cage to ride up  (" + at + " / " + all + ")";
                    break;
                case RunPhase.Wiped: title = "YOUR PARTY HAS FALLEN"; sub = "The run is over. Returning to camp in " + seconds + "..."; break;
                default: title = "THE DEPTHS ARE CLEARED"; sub = "Run complete! Returning to camp in " + seconds + "..."; break;
            }

            // Big for the first moments, then a quiet strip at the top so the floor can be explored.
            bool fresh = director.PhaseEndsAt.Value > 0 || Time.time - clearedSeenAt < 6f;
            if (clearedSeenAt < 0f) clearedSeenAt = Time.time;
            if (fresh)
            {
                Rect(0, h * 0.28f - 10, w, 90, new Color(0f, 0f, 0f, 0.55f));
                GUI.Label(new Rect(0, h * 0.28f, w, 44), title, new GUIStyle(big) { normal = { textColor = Gold } });
                GUI.Label(new Rect(0, h * 0.28f + 46, w, 24), sub, center);
            }
            else
            {
                Rect(w * 0.25f, 88, w * 0.5f, 24, new Color(0f, 0f, 0f, 0.5f));
                GUI.Label(new Rect(w * 0.25f, 88, w * 0.5f, 24), sub, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Gold } });
            }
            // A marker on the lift, through walls.
            var cam = player.Camera;
            var ps = cam.WorldToScreenPoint(DungeonDirector.ExitPosition + Vector3.up * 3.2f);
            if (ps.z > 0f)
                GUI.Label(new Rect(ps.x - 100, h - ps.y - 10, 200, 20), "THE LIFT  " + Mathf.RoundToInt(Vector3.Distance(player.transform.position, DungeonDirector.ExitPosition)) + "m",
                    new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.6f, 0.9f, 1f) } });
        }

        private void DrawBanner(float w, float h)
        {
            if (Time.time > bannerUntil || bannerTitle == null || BossIntroShowing) return;
            float a = Mathf.Clamp01((bannerUntil - Time.time) / 1.5f) * Mathf.Clamp01(1f - fade * 1.5f);
            var gold = new Color(Gold.r, Gold.g, Gold.b, a);
            GUI.Label(new Rect(0, h * 0.18f, w, 44), bannerTitle, new GUIStyle(big) { normal = { textColor = gold } });
            GUI.Label(new Rect(0, h * 0.18f + 44, w, 26), bannerSub, new GUIStyle(center) { fontSize = 18, normal = { textColor = new Color(1f, 1f, 1f, a) } });
            if (!string.IsNullOrEmpty(bannerHint))
                GUI.Label(new Rect(0, h * 0.18f + 72, w, 22), bannerHint, new GUIStyle(center) { fontSize = 15, fontStyle = FontStyle.Italic, normal = { textColor = new Color(gold.r, gold.g, gold.b, a * 0.9f) } });
            // This floor's event, read live (it may land a moment after the banner).
            var ev = DungeonDirector.CurrentEvent;
            if (ev != FloorEvent.None)
            {
                var ec = DungeonDirector.EventColor(ev);
                GUI.Label(new Rect(0, h * 0.18f + 98, w, 26), DungeonDirector.EventName(ev).ToUpperInvariant() + "  -  " + DungeonDirector.EventText(ev),
                    new GUIStyle(center) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = new Color(ec.r, ec.g, ec.b, a) } });
            }
        }

        private void DrawControlsHint(float w, float h)
        {
            float age = Time.time - spawnTime;
            if (age > 25f) return;
            float a = Mathf.Clamp01((25f - age) / 3f);
            var style = new GUIStyle(small) { alignment = TextAnchor.UpperRight, normal = { textColor = new Color(1f, 1f, 1f, a * 0.85f) } };
            GUI.Label(new Rect(w - 330, 262, 310, 190), Binds.Rekey(
                "WASD move  ·  Shift sprint  ·  Space jump\n" +
                (progress.ClassId.Value == ClassId.Flagellant
                    ? "LMB Flail (every third swing CRASHES)  ·  RMB Scourge yourself for Zeal\n" +
                      "Lower health = harder hits; every hit heals you\n" +
                      "Q  Chains (drag them in)  ·  E  Penitent Leap\n"
                    : progress.ClassId.Value == ClassId.PlagueDoctor
                    ? "LMB lob Vials (blight)  ·  RMB Tonic (heals allies where it bursts)\n" +
                      "Blighted dead take +10% from all, and spread it\n" +
                      "Q  Miasma cloud  ·  E  Plague Mask (cleanse the party)\n"
                    : progress.ClassId.Value == ClassId.Ranger
                    ? "LMB hold to draw, release to shoot\n" +
                      "3 hits mark a target (+25% for all)  ·  RMB leap back\n" +
                      "Q  Bear Trap  ·  E  Volley at the crosshair\n"
                    : progress.ClassId.Value == ClassId.Rogue
                    ? "LMB Twin Daggers  ·  RMB Fan of Knives\n" +
                      "Hit from BEHIND: +50% more (Ambush)\n" +
                      "Q  Shadowstep  ·  E  Smoke Bomb (vanish)\n"
                    : progress.ClassId.Value == ClassId.Cleric
                    ? "LMB Smite  ·  RMB hold Mending Beam on an ally\n" +
                      "Q  Consecrate (heal allies, burn foes)\n" +
                      "E  Divine Aegis shields everyone nearby\n"
                    : progress.ClassId.Value == ClassId.Mage
                    ? "LMB Firebolt  ·  RMB Frostbolt (chills)\n" +
                      "Fire on a chilled enemy SHATTERS (+75%)\n" +
                      "Q  Blink  ·  E  Meteor at the crosshair\n"
                    : "LMB swing  ·  RMB block (front)\n" +
                      "Q  Shield Bash — stuns, cancels wind-ups\n" +
                      "E  War Cry — taunt + half damage\n") +
                "F  revive a downed ally  ·  G  trade  ·  Tab  stats  ·  I  inventory\n" +
                "CTRL dodge (slide if sprinting)  ·  V kick\n" +
                "R ultimate (when charged)\n" +
                "1 / 2  drink Health / Mana   3 / 4  throw Frost / Holy\n" +
                "M map  ·  Z ping  ·  H high five  ·  Esc free mouse\n" +
                "Hit enemies from behind for +35%"), style);
        }

        private GUIStyle feedStyle;

        private class Toast { public string Key, Suffix; public int Amount; public Color Color; public float At; }
        private static readonly List<Toast> toasts = new List<Toast>();

        /// <summary>
        /// Any peer: something you just picked up, shown under the crosshair at once (UI pass 2026-10-20).
        /// The same kind within a moment adds up ("+12 gold" then "+30 gold"), up to four lines.
        /// </summary>
        public static void Pickup(string key, int amount, string suffix, Color color)
        {
            foreach (var t in toasts)
                if (t.Key == key && UnityEngine.Time.time - t.At < 1.4f) { t.Amount += amount; t.At = UnityEngine.Time.time; return; }
            toasts.Add(new Toast { Key = key, Amount = amount, Suffix = suffix, Color = color, At = UnityEngine.Time.time });
            if (toasts.Count > 4) toasts.RemoveAt(0);
        }

        private void DrawToasts(float w, float h)
        {
            // To the right of the crosshair, clear of gear cards (below) and prompts (under the crosshair).
            float y = h * 0.42f, x = w / 2f + 70f;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                float age = Time.time - t.At;
                if (age > 1.8f) { toasts.RemoveAt(i); continue; }
                float a = Mathf.Clamp01((1.8f - age) / 0.5f);
                float pop = 1f + 0.25f * Mathf.Clamp01(1f - age / 0.15f);
                string text = (t.Amount > 1 || t.Key == "gold" || t.Key == "embers" ? "+" + t.Amount : "+") + t.Suffix;
                var st = new GUIStyle(center) { fontSize = Mathf.RoundToInt(16 * pop), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(t.Color.r, t.Color.g, t.Color.b, a) } };
                UiSkin.Shadowed(new Rect(x, y - age * 12f, w / 2f - 90f, 26), text, st, a);
                y += 24f;
            }
        }

        /// <summary>
        /// The feed (2026-10-20 UI pass): each line on its own dark pill with an accent in its colour,
        /// sliding in from the left; newest at the bottom, at most seven, faded out after six seconds.
        /// Kept to the left third so it never runs under title cards or loot cards in the middle.
        /// </summary>
        private void DrawFeed(float h)
        {
            feedStyle ??= new GUIStyle(label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            float maxW = Mathf.Min(460f, Screen.width * 0.34f);
            float y = h * 0.6f;
            int shown = 0;
            for (int i = FeedLines.Count - 1; i >= 0 && shown < 7; i--)
            {
                var line = FeedLines[i];
                float age = Time.time - line.Time;
                if (age > 6f) continue;
                shown++;
                float a = Mathf.Clamp01((6f - age) / 1f);
                float slide = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / 0.22f), 3f);
                float tw = Mathf.Min(maxW, feedStyle.CalcSize(new GUIContent(line.Text)).x + 22f);
                float x = 18f - (1f - slide) * 60f;
                Rect(x, y, tw, 22, new Color(0.03f, 0.025f, 0.02f, 0.62f * a));
                Rect(x, y, 3, 22, new Color(line.Color.r, line.Color.g, line.Color.b, 0.9f * a));
                var st = new GUIStyle(feedStyle) { normal = { textColor = new Color(line.Color.r, line.Color.g, line.Color.b, a) } };
                GUI.Label(new Rect(x + 10, y, tw - 12, 22), line.Text, st);
                y -= 25;
            }
        }

        /// <summary>
        /// An interaction prompt under the crosshair (UI pass 2026-10-20): the key in a gold badge and
        /// the action on a dark backing, instead of bare text over the world. Understands "F   ...",
        /// "Hold F ..." and "Press G to ..." so every caller keeps its wording.
        /// </summary>
        private void PromptLine(float w, float h, string text, float dy = 40f)
        {
            if (string.IsNullOrEmpty(text)) return;
            string key = null, body = text;
            if (text.StartsWith("F   ")) { key = "F"; body = text.Substring(4); }
            else if (text.StartsWith("Hold F ")) { key = "F"; body = "Hold: " + text.Substring(7); }
            else if (text.StartsWith("Press ") && text.Length > 8 && text[7] == ' ')
            {
                key = text.Substring(6, 1);
                body = text.Substring(8);
                if (body.StartsWith("to ")) body = body.Substring(3);
                if (body.Length > 0) body = char.ToUpperInvariant(body[0]) + body.Substring(1);
            }
            if (key != null) key = Binds.Show(key);
            var st = new GUIStyle(center) { fontSize = 15, wordWrap = false, alignment = TextAnchor.MiddleLeft };
            float tw = Mathf.Min(w * 0.7f, st.CalcSize(new GUIContent(body)).x + 16f);
            float badge = key != null ? 30f : 0f;
            float total = tw + badge + 12f;
            float x = w / 2f - total / 2f, y = h / 2f + dy;
            Rect(x, y - 2, total, 28, new Color(0.02f, 0.02f, 0.02f, 0.62f));
            Rect(x, y + 25, total, 1, new Color(Gold.r, Gold.g, Gold.b, 0.45f));
            if (key != null)
            {
                UiSkin.Panel(new Rect(x + 4, y + 1, 24, 22), 0.95f);
                GUI.Label(new Rect(x + 4, y + 1, 24, 22), key, new GUIStyle(center) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Gold } });
            }
            GUI.Label(new Rect(x + badge + 8f, y, tw, 24), body, st);
        }

        private Anvil NearAnvil() => life.Downed.Value ? null : Anvil.Near(player.transform.position);

        /// <summary>
        /// The forge: temper the class weapon with embers, and at +2 choose its path. Both
        /// paths are always shown, so the choice can be planned from the first floor.
        /// </summary>
        private void DrawForge(float w, float h)
        {
            if (NearAnvil() == null || shopOpen) return;
            if (!forgeOpen)
            {
                var stall = StallHere();
                if (stall == null || Vector3.Distance(player.transform.position, NearAnvil().transform.position) <
                    Vector3.Distance(player.transform.position, Shop.Position(stall.Value).Value))
                    PromptLine(w, h, DungeonDirector.InLobby ? "Press G to use the anvil (Hobb tempers, refines and scraps; sells plate and gear)" : "Press G to use the anvil");
                return;
            }

            var id = progress.ClassId.Value;
            int level = progress.WeaponLevel.Value, path = progress.WeaponPath.Value;
            string weapon = ClassDefinition.WeaponName(id);
            const float leftW = 620f;
            float pw = leftW + 340, ph = 500;
            float x = w / 2f - pw / 2f, y = h / 2f - ph / 2f;
            var ember = LootInfo.EmberColor;
            Rect(x - 3, y - 3, pw + 6, ph + 6, new Color(ember.r, ember.g, ember.b, 0.5f));
            Rect(x, y, pw, ph, new Color(0.05f, 0.03f, 0.02f, 0.95f));
            GUI.Label(new Rect(x + 16, y + 12, pw, 24), "THE ANVIL", new GUIStyle(label) { normal = { textColor = ember } });
            if (Shop.Position(Shop.Vendor.Smith) != null && GUI.Button(new Rect(x + 150, y + 10, 190, 24), "Hobb's wares (buy)"))
            { forgeOpen = false; shopOpen = true; shopVendor = Shop.Vendor.Smith; }
            GUI.Label(new Rect(x + pw - 300, y + 12, 284, 24), progress.Sigils.Value + " sigils    " + progress.Embers.Value + " embers", new GUIStyle(label) { alignment = TextAnchor.UpperRight, normal = { textColor = ember } });

            // Level pips and the temper button.
            GUI.Label(new Rect(x + 16, y + 44, 200, 24), weapon + " +" + level, new GUIStyle(label) { fontSize = 20 });
            for (int i = 0; i < Anvil.MaxLevel; i++)
                Rect(x + 180 + i * 17 + (i >= 5 ? 6 : 0), y + 50, 13, 14, i < level ? (i >= Anvil.AscendedLevel - 1 ? new Color(1f, 0.85f, 0.45f) : ember) : new Color(ember.r, ember.g, ember.b, 0.18f));
            string reason = progress.CannotTemperReason();
            string next = level >= Anvil.MaxLevel ? "Fully tempered"
                : "Temper to +" + (level + 1) + ":  +" + ClassDefinition.WeaponAttPerLevel + " ATT, +" + ClassDefinition.WeaponDexPerLevel + " DEX, +5% range" +
                  (level + 1 == WeaponPaths.MasteryLevel && path != 0 ? ", path mastery" : "") +
                  (level + 1 == Runes.SocketLevels[2] ? ", a 3rd rune socket" : "") +
                  (level + 1 == Anvil.AscendedLevel ? ", ASCENDED (+10% dmg)" : "") +
                  (level + 1 == Anvil.MasterworkLevel ? ", MASTERWORK (+8% crit)" : "");
            GUI.Label(new Rect(x + 16, y + 76, leftW - 230, 20), next, small);
            GUI.enabled = reason == null;
            int goldCost = Anvil.GoldCost(level);
            if (GUI.Button(new Rect(x + leftW - 216, y + 46, 200, 30), reason ?? ("Temper  (" + Anvil.Cost(level) + " embers" + (goldCost > 0 ? ", " + goldCost + "g)" : ")")))) progress.TemperRpc();
            GUI.enabled = true;

            // The two paths.
            string head = path != 0 ? "PATH" : level >= WeaponPaths.PathLevel ? "CHOOSE A PATH (once per run)" : "PATHS  -  choose one at +" + WeaponPaths.PathLevel;
            GUI.Label(new Rect(x + 16, y + 108, pw, 20), head, new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            for (int p = 1; p <= 2; p++)
            {
                float cx = x + 16 + (p - 1) * ((leftW - 44) / 2f + 12), cw = (leftW - 44) / 2f, cy = y + 132, chh = 182;
                bool chosen = path == p, locked = path != 0 && !chosen;
                var border = chosen ? ember : new Color(1f, 1f, 1f, locked ? 0.06f : 0.18f);
                Rect(cx - 2, cy - 2, cw + 4, chh + 4, border);
                Rect(cx, cy, cw, chh, new Color(0.08f, 0.06f, 0.05f, 0.95f));
                float alpha = locked ? 0.4f : 1f;
                GUI.Label(new Rect(cx + 10, cy + 8, cw - 20, 22), WeaponPaths.Name(id, p).ToUpperInvariant(), new GUIStyle(label) { normal = { textColor = new Color(ember.r, ember.g, ember.b, alpha) } });
                var wrap = new GUIStyle(small) { wordWrap = true, normal = { textColor = new Color(0.9f, 0.88f, 0.85f, alpha) } };
                GUI.Label(new Rect(cx + 10, cy + 32, cw - 20, 60), WeaponPaths.Describe(id, p), wrap);
                bool mastered = chosen && level >= WeaponPaths.MasteryLevel;
                GUI.Label(new Rect(cx + 10, cy + 92, cw - 20, 18), mastered ? "MASTERED" : "Mastery at +" + WeaponPaths.MasteryLevel,
                    new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(Gold.r, Gold.g, Gold.b, alpha) } });
                GUI.Label(new Rect(cx + 10, cy + 110, cw - 20, 40), WeaponPaths.Mastery(id, p), wrap);
                if (path == 0)
                {
                    GUI.enabled = level >= WeaponPaths.PathLevel;
                    if (GUI.Button(new Rect(cx + 10, cy + chh - 34, cw - 20, 26), level >= WeaponPaths.PathLevel ? "Choose " + WeaponPaths.Name(id, p) : "Unlocks at +" + WeaponPaths.PathLevel))
                        progress.ChoosePathRpc((byte)p);
                    GUI.enabled = true;
                }
            }

            // Under the paths: refine or attune the gear you are wearing, so a favourite keeps up.
            GUI.Label(new Rect(x + 16, y + 328, leftW - 32, 20), "GEAR  -  refine quality, or attune it to this floor", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            int floorDepth = DungeonDirector.GearDepth;
            foreach (var gslot in new[] { GearSlot.Armor, GearSlot.Charm })
            {
                float gy = y + 350 + (int)gslot * 34;
                var g = progress.Equipped(gslot);
                GUI.Label(new Rect(x + 16, gy + 4, 250, 20), g.IsEmpty ? gslot + ": nothing worn" : Gear.Name(g) + "  (floor " + (g.Depth + 1) + ")",
                    new GUIStyle(small) { normal = { textColor = g.IsEmpty ? new Color(1f, 1f, 1f, 0.4f) : Gear.QualityColor(g.Quality) } });
                if (g.IsEmpty) continue;
                int refine = Gear.RefineCost(g), attune = Gear.AttuneCost(g, floorDepth);
                GUI.enabled = refine > 0 && progress.Embers.Value >= refine;
                if (GUI.Button(new Rect(x + 270, gy, 160, 26), refine > 0 ? "Refine  (" + refine + ")" : "Fully refined")) progress.ReforgeRpc((byte)gslot, 0);
                GUI.enabled = attune > 0 && progress.Embers.Value >= attune;
                if (GUI.Button(new Rect(x + 440, gy, 160, 26), attune > 0 ? "Attune  (" + attune + ")" : "Attuned")) progress.ReforgeRpc((byte)gslot, 1);
                GUI.enabled = true;
            }

            // Right column: Hobb's scrap bench. What you are carrying, broken down for embers.
            float rx = x + leftW + 4, rw = pw - leftW - 20;
            Rect(rx - 10, y + 40, 1, ph - 56, new Color(1f, 1f, 1f, 0.08f));
            float ay = y + 44;
            GUI.Label(new Rect(rx, ay, rw, 20), "SCRAP  -  break down pack gear for embers", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = ember } });
            if (progress.BagCount == 0) GUI.Label(new Rect(rx, ay + 24, rw, 40), "Nothing in your pack. Gear you pick up goes there (I to look).", new GUIStyle(small) { wordWrap = true });
            for (int i = 0; i < progress.BagCount; i++)
            {
                var item = progress.BagItem(i);
                float ry2 = ay + 24 + i * 30;
                GUI.Label(new Rect(rx, ry2 + 4, rw - 112, 20), Gear.Name(item), new GUIStyle(small) { normal = { textColor = Gear.QualityColor(item.Quality) } });
                if (GUI.Button(new Rect(rx + rw - 108, ry2, 108, 24), "Scrap  +" + Gear.ScrapValue(item))) progress.BagScrapRpc(i);
            }
            GUI.Label(new Rect(rx, y + ph - 70, rw, 40), "Runes, sigils and ability ranks are Ysolde's work: find her at the standing stones.", new GUIStyle(small) { wordWrap = true, normal = { textColor = new Color(0.6f, 0.85f, 1f) } });
            if (!string.IsNullOrEmpty(GUI.tooltip)) GUI.Label(new Rect(x + 16, y + ph - 26, pw - 32, 20), GUI.tooltip, new GUIStyle(small) { normal = { textColor = Gold } });
        }

        // ---- Ysolde the runewright (G at the standing stones): ability ranks, runes, melding ----
        private void DrawRunewright(float w, float h)
        {
            if (!runeOpen) return;
            var id = progress.ClassId.Value;
            int level = progress.WeaponLevel.Value;
            float pw = 900, ph = 520;
            float x = w / 2f - pw / 2f, y = h / 2f - ph / 2f;
            var blue = new Color(0.45f, 0.85f, 1f);
            Rect(x - 3, y - 3, pw + 6, ph + 6, new Color(blue.r, blue.g, blue.b, 0.45f));
            Rect(x, y, pw, ph, new Color(0.03f, 0.04f, 0.06f, 0.95f));
            GUI.Label(new Rect(x + 16, y + 12, pw, 24), "YSOLDE'S STONES", new GUIStyle(label) { normal = { textColor = blue } });
            if (GUI.Button(new Rect(x + 230, y + 10, 190, 24), "Buy a Sigil (her wares)")) { runeOpen = false; shopOpen = true; shopVendor = Shop.Vendor.Runewright; }
            GUI.Label(new Rect(x + pw - 300, y + 12, 284, 24), progress.Sigils.Value + " sigils    " + progress.Embers.Value + " embers", new GUIStyle(label) { alignment = TextAnchor.UpperRight, normal = { textColor = blue } });
            const float leftW = 440f;
            // Left: abilities (Sigils). Right: runes in the weapon, the bag, and melding.
            float rx = x + 16, rw = leftW - 32;
            float ay = y + 44;
            GUI.Label(new Rect(rx, ay, rw, 20), "ABILITIES  -  Sigils empower a rank (up to 8)", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = LootInfo.SigilColor } });
            foreach (var slot in new[] { AbilitySlot.Q, AbilitySlot.E })
            {
                float ry = ay + 22 + (int)slot * 88;
                int rank = progress.Rank(slot);
                GUI.Label(new Rect(rx, ry, rw - AbilityRanks.MaxRank * 13 - 8, 22), slot + "  " + ClassDefinition.AbilityName(id, slot), label);
                for (int r = 0; r < AbilityRanks.MaxRank; r++)
                    Rect(rx + rw - AbilityRanks.MaxRank * 13 + r * 13, ry + 6, 10, 12, r < rank ? LootInfo.SigilColor : new Color(LootInfo.SigilColor.r, LootInfo.SigilColor.g, LootInfo.SigilColor.b, 0.2f));
                string nextText = AbilityRanks.NextRank(id, slot, rank);
                GUI.Label(new Rect(rx, ry + 21, rw, 34), nextText, new GUIStyle(small) { wordWrap = true });
                int sigilCost = AbilityRanks.SigilCost(rank);
                GUI.enabled = rank < AbilityRanks.MaxRank && progress.Sigils.Value >= sigilCost;
                if (GUI.Button(new Rect(rx, ry + 56, rw, 24), rank >= AbilityRanks.MaxRank ? "Max rank" : progress.Sigils.Value >= sigilCost ? "Empower  (" + sigilCost + (sigilCost == 1 ? " sigil)" : " sigils)") : "Needs " + sigilCost + (sigilCost == 1 ? " Sigil" : " Sigils")))
                    progress.EmpowerRpc((byte)slot);
                GUI.enabled = true;
            }

            if (progress.Rank(AbilitySlot.Q) >= AbilityRanks.MaxRank && progress.Rank(AbilitySlot.E) >= AbilityRanks.MaxRank && progress.Sigils.Value > 0)
            {
                // Both abilities maxed: a spare Sigil is still worth something.
                if (GUI.Button(new Rect(rx, ay + 22 + 2 * 88 - 6, rw, 22), "Melt a spare Sigil into 30 embers")) progress.MeltSigilRpc();
            }
            float uy = ay + 22 + 2 * 88 + 18;
            int sockets = Runes.SocketsAt(level);
            GUI.Label(new Rect(rx, uy, rw, 20), "RUNES  -  sockets open at +1, +3 and +6", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            for (int sIdx = 0; sIdx < Runes.MaxSockets; sIdx++)
            {
                float sy = uy + 22 + sIdx * 34;
                var r = progress.Socketed(sIdx);
                bool open = sIdx < sockets;
                Rect(rx, sy + 3, 10, 10, open ? (r == Rune.None ? new Color(1f, 1f, 1f, 0.25f) : Runes.Color(r)) : new Color(1f, 1f, 1f, 0.06f));
                GUI.Label(new Rect(rx + 16, sy - 2, rw - 16, 18), open ? Runes.Name(r) : "Locked  (weapon +" + Runes.SocketLevels[sIdx] + ")",
                    new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = open && r != Rune.None ? Runes.Color(r) : new Color(1f, 1f, 1f, 0.5f) } });
                if (open && r != Rune.None) GUI.Label(new Rect(rx + 16, sy + 14, rw - 16, 18), Runes.Describe(r), small);
            }
            float by = uy + 22 + Runes.MaxSockets * 34 + 2;
            int bag = progress.RuneBagCount;
            if (bag == 0) GUI.Label(new Rect(rx, by, rw, 18), "No unset runes. Elites and chests drop them.", small);
            for (int i = 0; i < bag; i++)
            {
                var r = progress.RuneInBag(i);
                var bx = rx + (i % 2) * (rw / 2f + 2);
                var bRect = new Rect(bx, by + (i / 2) * 28, rw / 2f - 2, 24);
                GUI.enabled = sockets > 0;
                if (GUI.Button(bRect, new GUIContent("Set " + Runes.Name(r).Replace(" Rune", ""), Runes.Describe(r)))) progress.SocketRuneRpc((byte)r);
                GUI.enabled = true;
            }

            // Melding: three alike (kind and tier) become one of the next tier.
            float mx = x + leftW + 10, mw = pw - leftW - 26, my = y + 44;
            Rect(mx - 12, y + 40, 1, ph - 56, new Color(1f, 1f, 1f, 0.08f));
            GUI.Label(new Rect(mx, my, mw, 20), "MELD  -  three of a rune become one of the next tier (II, III)", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = blue } });
            var seenRunes = new System.Collections.Generic.HashSet<Rune>();
            int rows = 0;
            for (int i = 0; i < progress.RuneBagCount; i++)
            {
                var r = progress.RuneInBag(i);
                if (!seenRunes.Add(r)) continue;
                int have = progress.RuneCount(r), tier = Runes.Tier(r);
                float ry2 = my + 24 + rows * 44;
                rows++;
                GUI.Label(new Rect(mx, ry2, mw - 150, 20), Runes.Name(r) + "  x" + have, new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Runes.Color(r) } });
                GUI.Label(new Rect(mx, ry2 + 18, mw - 150, 20), tier >= Runes.MaxTier ? "Already as strong as runes get." : "Next: " + Runes.Describe(Runes.Make(r, tier + 1)), new GUIStyle(small) { fontSize = 11, wordWrap = true });
                int cost = Runes.MeldCost(tier);
                GUI.enabled = tier < Runes.MaxTier && have >= 3 && progress.Embers.Value >= cost;
                if (GUI.Button(new Rect(mx + mw - 146, ry2 + 4, 146, 26), tier >= Runes.MaxTier ? "Tier III" : have < 3 ? "Need 3 (" + have + ")" : "Meld  (" + cost + " embers)")) progress.MeldRunesRpc((byte)r);
                GUI.enabled = true;
            }
            if (rows == 0) GUI.Label(new Rect(mx, my + 24, mw, 40), "No runes in your bag. Elites and chests drop them; bring three alike.", new GUIStyle(small) { wordWrap = true });
            if (!string.IsNullOrEmpty(GUI.tooltip)) GUI.Label(new Rect(x + 16, y + ph - 26, pw - 32, 20), GUI.tooltip, new GUIStyle(small) { normal = { textColor = Gold } });
        }

        private void DrawMerchant(float w, float h)
        {
            DrawSeatPrompt(w, h);
            if (forgeOpen || runeOpen) return;
            if (!shopOpen)
            {
                var stall = StallHere();
                if (stall == null) return;
                var nearAnvil = NearAnvil();
                if (nearAnvil != null && Vector3.Distance(player.transform.position, nearAnvil.transform.position) <
                    Vector3.Distance(player.transform.position, Shop.Position(stall.Value).Value)) return;
                string who = stall == Shop.Vendor.Shrine ? "Press G for the shrine's offerings (holy water, relics, Aldric's prayer)"
                    : stall == Shop.Vendor.Yard ? "Press G to talk to Sergeant Brann (drills: a stat for gold)"
                    : stall == Shop.Vendor.Runewright ? "Press G to visit Ysolde (runes, melding, sigils, ability ranks)"
                    : stall == Shop.Vendor.Carriage ? "Press G to talk to Corwin (maps of the way down, smelling salts)"
                    : DungeonDirector.InLobby ? "Press G to trade with Silas (potions and provisions)" : "Press G to trade with the peddler";
                PromptLine(w, h, who, stall == Shop.Vendor.Shrine ? 70f : 40f);
                return;
            }

            var vendor = shopVendor;
            float pw = 600, rowH = 38;
            var stock = Shop.For(progress.OwnerClientId, vendor);
            // Corwin also takes the party's orders: how hard a road, and how the spoils are shared.
            float top = vendor == Shop.Vendor.Carriage ? 70f : 0f;
            float ph = 70 + top + stock.Length * rowH;
            float x = w / 2f - pw / 2f, y = h / 2f - ph / 2f;
            Rect(x - 3, y - 3, pw + 6, ph + 6, new Color(Gold.r, Gold.g, Gold.b, 0.5f));
            Rect(x, y, pw, ph, new Color(0.04f, 0.035f, 0.03f, 0.94f));
            GUI.Label(new Rect(x + 14, y + 10, pw, 24), Shop.VendorTitle(vendor), new GUIStyle(label) { normal = { textColor = Gold } });
            GUI.Label(new Rect(x + 14, y + 30, pw - 28, 20), Shop.VendorLine(vendor), small);
            if (vendor == Shop.Vendor.Smith && GUI.Button(new Rect(x + pw - 300, y + 8, 160, 24), "Back to the anvil")) { shopOpen = false; forgeOpen = true; }
            if (vendor == Shop.Vendor.Runewright && GUI.Button(new Rect(x + pw - 300, y + 8, 160, 24), "Back to the stones")) { shopOpen = false; runeOpen = true; }
            // Struck this vendor? It shows in the prices, and coin can mend it.
            int amends = progress.AmendsCost(vendor);
            if (amends > 0)
            {
                int pct = Mathf.RoundToInt((progress.PriceMultiplier(vendor) - 1f) * 100f);
                GUI.Label(new Rect(x + 14, y + ph + 6, pw - 200, 22), "You struck them: +" + pct + "% on everything here.", new GUIStyle(small) { normal = { textColor = new Color(0.95f, 0.45f, 0.35f) } });
                GUI.enabled = progress.Gold.Value >= amends;
                if (GUI.Button(new Rect(x + pw - 190, y + ph + 4, 190, 26), "Make amends (" + amends + " gold)")) progress.AmendsRpc(vendor);
                GUI.enabled = true;
            }
            GUI.Label(new Rect(x + pw - 120, y + 10, 106, 24), progress.Gold.Value + " gold", new GUIStyle(label) { alignment = TextAnchor.UpperRight, normal = { textColor = Gold } });
            if (top > 0f)
            {
                var dir = DungeonDirector.Instance;
                bool host = Unity.Netcode.NetworkManager.Singleton.IsHost;
                bool locked = dir == null || dir.RunInProgress;
                var diff = Enemies.Difficulty.Level;
                float dy = y + 56;
                Rect(x + 10, dy - 2, pw - 20, top - 6, new Color(1f, 1f, 1f, 0.04f));
                GUI.Label(new Rect(x + 16, dy + 4, 120, 22), "THE ROAD", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
                GUI.enabled = host && !locked && dir.DifficultyIndex.Value > 0;
                if (GUI.Button(new Rect(x + 120, dy + 2, 28, 24), "<")) dir.SetDifficultyRpc((byte)(dir.DifficultyIndex.Value - 1));
                GUI.enabled = true;
                GUI.Label(new Rect(x + 152, dy + 4, 110, 22), diff.Name.ToUpperInvariant(), new GUIStyle(label) { alignment = TextAnchor.UpperCenter });
                GUI.enabled = host && !locked && dir.DifficultyIndex.Value < Enemies.Difficulty.Settings.Length - 1;
                if (GUI.Button(new Rect(x + 266, dy + 2, 28, 24), ">")) dir.SetDifficultyRpc((byte)(dir.DifficultyIndex.Value + 1));
                GUI.enabled = true;
                if (PlayerLife.All.Count > 1 && dir != null)
                {
                    GUI.enabled = host;
                    if (GUI.Button(new Rect(x + pw - 214, dy + 2, 200, 24), "Loot: " + (dir.FindersKeepers.Value ? "finders keepers" : "shared"))) dir.SetLootRuleRpc(!dir.FindersKeepers.Value);
                    GUI.enabled = true;
                }
                string note = locked ? "Set for this run. \"Same road as before, then.\"" : host ? diff.Blurb : diff.Blurb + "  (the host chooses)";
                GUI.Label(new Rect(x + 16, dy + 30, pw - 32, 20), note, small);
            }

            for (int i = 0; i < stock.Length; i++)
            {
                var item = stock[i];
                float ry = y + 60 + top + i * rowH;
                string reason = progress.CannotBuyReason(vendor, i);

                var nameStyle = item.Kind == Shop.ItemKind.Relic ? new GUIStyle(label) { normal = { textColor = Relics.GemColor((Relic)(int)item.Amount) } }
                    : item.Kind == Shop.ItemKind.Gear ? new GUIStyle(label) { normal = { textColor = Gear.QualityColor(item.Gear.Quality) } }
                    : item.Kind == Shop.ItemKind.Sigil ? new GUIStyle(label) { normal = { textColor = LootInfo.SigilColor } }
                    : label;
                string title = item.Kind == Shop.ItemKind.Relic ? "RELIC: " + item.Name : item.Kind == Shop.ItemKind.Gear ? "FOR YOU: " + item.Name : item.Name;
                GUI.Label(new Rect(x + 14, ry, 300, 20), title, nameStyle);
                GUI.Label(new Rect(x + 14, ry + 17, pw - 200, 18), item.Description, small);

                GUI.enabled = reason == null;
                if (GUI.Button(new Rect(x + pw - 174, ry + 3, 160, 28), reason ?? (item.Price + " gold")))
                {
                    progress.BuyRpc(vendor, i);
                }
                GUI.enabled = true;
            }
        }

        /// <summary>Owned relics as gem-coloured chips above the health bar.</summary>
        private void DrawRelicRow(float x, float y)
        {
            for (int i = 0; i < progress.RelicCount; i++)
            {
                var r = progress.RelicAt(i);
                var c = Relics.GemColor(r);
                float cx = x + i * 30;
                Rect(cx - 1, y - 1, 26, 26, new Color(Gold.r, Gold.g, Gold.b, 0.8f));
                Rect(cx, y, 24, 24, new Color(0.04f, 0.035f, 0.03f, 0.9f));
                Rect(cx + 5, y + 5, 14, 14, c);
                GUI.Label(new Rect(cx, y + 3, 24, 20), Relics.Name(r).Substring(0, 1), new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
            }
        }

        private void DrawStatSheet(float w, float h)
        {
            // Worn gear as full cards, bottom-left of the sheet.
            float cardY = h - 150;
            foreach (var slot in new[] { GearSlot.Charm, GearSlot.Armor })
            {
                var g = progress.Equipped(slot);
                if (g.IsEmpty) continue;
                cardY -= 66 + new List<StatType>(Gear.StatsOf(g)).Count * 18 + 12;
                DrawGearCard(g, w - 300 - 24 - 360 - 12, cardY, false);
            }
            // Relics, with what they do, to the left of the stat sheet.
            if (progress.RelicCount > 0)
            {
                float rw = 380, rh = 40 + progress.RelicCount * 34;
                float rx = w - 300 - 24 - rw - 12, ry = h / 2f - rh / 2f;
                Rect(rx, ry, rw, rh, Panel);
                GUI.Label(new Rect(rx + 12, ry + 10, rw, 22), "RELICS", new GUIStyle(label) { normal = { textColor = Gold } });
                for (int i = 0; i < progress.RelicCount; i++)
                {
                    var r = progress.RelicAt(i);
                    GUI.Label(new Rect(rx + 12, ry + 36 + i * 34, rw, 20), Relics.Name(r), new GUIStyle(label) { normal = { textColor = Relics.GemColor(r) } });
                    GUI.Label(new Rect(rx + 12, ry + 53 + i * 34, rw - 20, 18), Relics.Describe(r), small);
                }
            }

            float pw = 300, rowH = 26, ph = 50 + Stats.Count * rowH + 70 + 88 + 22;
            float x = w - pw - 24, y = h / 2f - ph / 2f;
            Rect(x, y, pw, ph, Panel);
            // The two gear slots, at the foot of the sheet; hover-free: full cards sit left of it.
            float gy = y + 46 + Stats.Count * rowH;
            foreach (var slot in new[] { GearSlot.Armor, GearSlot.Charm })
            {
                var g = progress.Equipped(slot);
                var gc = g.IsEmpty ? new Color(1f, 1f, 1f, 0.4f) : Gear.QualityColor(g.Quality);
                GUI.Label(new Rect(x + 12, gy, 70, 20), slot.ToString().ToUpperInvariant(), small);
                GUI.Label(new Rect(x + 80, gy, pw - 90, 20), g.IsEmpty ? "-" : Gear.Name(g), new GUIStyle(small) { normal = { textColor = gc } });
                gy += 22;
            }
            for (int sIdx = 0; sIdx < Runes.MaxSockets; sIdx++)
            {
                var r = progress.Socketed(sIdx);
                bool open = sIdx < Runes.SocketsAt(progress.WeaponLevel.Value);
                GUI.Label(new Rect(x + 12, gy, 70, 20), "RUNE " + (sIdx + 1), small);
                GUI.Label(new Rect(x + 80, gy, pw - 90, 20), !open ? "locked" : r == Rune.None ? "-" : Runes.Name(r),
                    new GUIStyle(small) { normal = { textColor = open && r != Rune.None ? Runes.Color(r) : new Color(1f, 1f, 1f, 0.4f) } });
                gy += 22;
            }
            // Reach: the class's attack range now, and how much the anvil and runes have stretched it.
            float rangeNow = player.ActiveKit.AttackRange;
            bool melee = progress.ClassId.Value == Classes.ClassId.Knight || progress.ClassId.Value == Classes.ClassId.Rogue || progress.ClassId.Value == Classes.ClassId.Flagellant;
            float bonus = (melee ? progress.ReachMultiplier : progress.RangeMultiplier) - 1f;
            GUI.Label(new Rect(x + 12, gy, 70, 20), "RANGE", small);
            GUI.Label(new Rect(x + 80, gy, pw - 90, 20), rangeNow.ToString(melee ? "F1" : "F0") + " m" + (melee ? " reach" : "") + (bonus > 0.001f ? "  (+" + Mathf.RoundToInt(bonus * 100f) + "%)" : "  (anvil, Farsight +)"),
                new GUIStyle(small) { normal = { textColor = bonus > 0.001f ? new Color(0.5f, 0.95f, 0.9f) : new Color(1f, 1f, 1f, 0.6f) } });
            gy += 22;
            foreach (var slot in new[] { AbilitySlot.Q, AbilitySlot.E })
            {
                GUI.Label(new Rect(x + 12, gy, 70, 20), slot.ToString(), small);
                GUI.Label(new Rect(x + 80, gy, pw - 90, 20), ClassDefinition.AbilityName(progress.ClassId.Value, slot) + "  rank " + progress.Rank(slot),
                    new GUIStyle(small) { normal = { textColor = progress.Rank(slot) > 1 ? LootInfo.SigilColor : new Color(1f, 1f, 1f, 0.6f) } });
                gy += 22;
            }
            GUI.Label(new Rect(x + 12, y + 10, pw, 22), progress.Class.Name.ToUpperInvariant() + "  -  " + ClassDefinition.WeaponName(progress.ClassId.Value) + " +" + progress.WeaponLevel.Value,
                new GUIStyle(label) { normal = { textColor = Gold } });

            // Each stat: its value, a bar against a generous ceiling, and what this run has added.
            for (int i = 0; i < Stats.Count; i++)
            {
                var stat = (StatType)i;
                float ry = y + 40 + i * rowH;
                float value = progress.Value(stat), baseValue = progress.Class.BaseStats[i];
                GUI.Label(new Rect(x + 12, ry, 50, 22), Stats.ShortName(stat), label);
                GUI.Label(new Rect(x + 60, ry, 60, 22), Mathf.RoundToInt(value).ToString(), label);

                var c = LootInfo.StatColor(stat);
                float ceiling = stat == StatType.HP ? 500f : stat == StatType.MP ? 250f : stat == StatType.DEF ? 30f : 100f;
                Rect(x + 110, ry + 6, 120, 10, new Color(c.r, c.g, c.b, 0.18f));
                Rect(x + 110, ry + 6, 120 * Mathf.Clamp01(baseValue / ceiling), 10, new Color(c.r, c.g, c.b, 0.6f));
                Rect(x + 110 + 120 * Mathf.Clamp01(baseValue / ceiling), ry + 6, 120 * Mathf.Clamp01((value - baseValue) / ceiling), 10, c);
                float added = value - baseValue;
                if (Mathf.Abs(added) >= 0.5f)
                    GUI.Label(new Rect(x + 238, ry, 60, 22), (added > 0 ? "+" : "") + Mathf.RoundToInt(added), new GUIStyle(small) { normal = { textColor = Gold } });
            }
        }

        /// <summary>
        /// Ping markers: a diamond in the pinger's colour with a label and distance, pinned to
        /// the screen edge when the ping is off-screen or behind you.
        /// </summary>
        private void DrawPings(float w, float h)
        {
            var cam = player.Camera;
            foreach (var p in PlayerPings.Active.Values)
            {
                var world = p.Position + Vector3.up * (p.Kind == PlayerPings.Kind.Enemy ? 2.6f : 1.2f);
                var sp = cam.WorldToScreenPoint(world);
                bool behind = sp.z < 0f;
                var pos = new Vector2(sp.x, h - sp.y);
                if (behind) pos = new Vector2(w - pos.x, h - pos.y);
                bool off = behind || pos.x < 30 || pos.x > w - 30 || pos.y < 30 || pos.y > h - 30;
                if (off)
                {
                    // Push to the screen edge along the direction from centre.
                    var c = new Vector2(w / 2f, h / 2f);
                    var d = pos - c;
                    if (behind && d.sqrMagnitude < 1f) d = Vector2.down;
                    float k = Mathf.Min((w / 2f - 40f) / Mathf.Max(1f, Mathf.Abs(d.x)), (h / 2f - 40f) / Mathf.Max(1f, Mathf.Abs(d.y)));
                    pos = c + d * k;
                }
                float dist = Vector3.Distance(player.transform.position, p.Position);
                float fade = Mathf.Clamp01((p.Until - Time.time) / 1f);
                var col = new Color(p.Color.r, p.Color.g, p.Color.b, fade);

                var old = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, pos);
                Rect(pos.x - 9, pos.y - 9, 18, 18, new Color(0f, 0f, 0f, 0.7f * fade));
                Rect(pos.x - 7, pos.y - 7, 14, 14, col);
                GUI.matrix = old;
                var style = new GUIStyle(small) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 1f, 1f, fade) } };
                GUI.Label(new Rect(pos.x - 80, pos.y + 12, 160, 18), p.Label + "  " + Mathf.RoundToInt(dist) + "m", style);
            }
        }

        private float nextBestiaryCheck;

        private void DrawEnemyBars()
        {
            var cam = player.Camera;
            if (cam == null) return;
            // The Bestiary fills in when a creature is close and on screen (and once more for a boss in its intro).
            if (Event.current.type == EventType.Repaint && Time.time >= nextBestiaryCheck)
            {
                nextBestiaryCheck = Time.time + 0.5f;
                foreach (var e in EnemyBrain.All)
                {
                    if (e == null || e.IsDead) continue;
                    var sp = cam.WorldToViewportPoint(e.transform.position + Vector3.up);
                    if (sp.z > 0f && sp.z < 16f && sp.x > 0.1f && sp.x < 0.9f && sp.y > 0.05f && sp.y < 0.95f) Bestiary.See(e);
                }
            }

            foreach (var hp in Health.All)
            {
                if (hp == null || hp.Team != Team.Enemies || !hp.IsAlive) continue;
                if (hp.GetComponent<BossBrain>() != null) continue;
                var brain = hp.GetComponent<EnemyBrain>();
                string tags = StatusTags(hp, brain);
                bool hurt = Time.time - hp.LastHurtTime <= 5f;
                if (!hurt && tags.Length == 0) continue;

                var world = hp.transform.position + Vector3.up * (hp.CenterHeight * 2f + 0.35f);
                var screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f || screen.z > 25f) continue;

                float bw = 60f, x = screen.x - bw / 2f, y = Screen.height - screen.y;
                if (hurt)
                {
                    Rect(x - 1, y - 1, bw + 2, 7, new Color(0, 0, 0, 0.75f));
                    Rect(x, y, bw * hp.Fraction, 5, new Color(0.85f, 0.2f, 0.15f));
                }
                if (tags.Length > 0)
                {
                    var style = new GUIStyle(small) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, richText = true, fontSize = 11 };
                    GUI.Label(new Rect(screen.x - 90, y - 17, 180, 16), "<color=#000000AA>" + StripColors(tags) + "</color>", new GUIStyle(style) { contentOffset = new Vector2(1, 1) });
                    GUI.Label(new Rect(screen.x - 90, y - 17, 180, 16), tags, style);
                }
            }
        }

        /// <summary>What a combo can cash in on this enemy, as coloured tags ("CHILL MARK STUN").</summary>
        private static string StatusTags(Health hp, EnemyBrain brain)
        {
            var sb = new System.Text.StringBuilder();
            void Tag(string text, string hex) { if (sb.Length > 0) sb.Append(' '); sb.Append("<color=#").Append(hex).Append('>').Append(text).Append("</color>"); }
            if (brain != null && brain.IsChampion) Tag(brain.Title.Value.ToString().ToUpperInvariant(), "FFC14D");
            if (brain != null && brain.AffixKind != EliteAffix.None) Tag(EliteAffixes.Tag(brain.AffixKind), EliteAffixes.Hex(brain.AffixKind));
            if (brain is WraithBrain wr && !wr.Pinned) Tag("ETHEREAL", "8FA8D0");
            if (brain is ShieldbearerBrain shieldB) { if (shieldB.ShieldUp) Tag("SHIELDED", "C8B48C"); else if (shieldB.GuardDown) Tag("OPEN", "FFD98A"); }
            if (brain is GargoyleBrain garg && !garg.Woken.Value) return "";
            if (DungeonDirector.InRitualWard(brain != null ? brain.transform.position : hp.transform.position)) Tag("RITUAL WARD", "B266FF");
            if (hp.Chilled.Value) Tag("CHILL", "8CD9FF");
            if (hp.Marked.Value) Tag("MARK", "FF5A4D");
            if (hp.Blighted.Value) Tag("BLIGHT", "8CFF59");
            if (brain != null && (brain.Status.Value & 1) != 0) Tag("STUN", "FFD94D");
            if (brain is MournerBrain mo && mo.Seen.Value) Tag("WATCHED", "B8C4D6");
            if (brain != null && (brain.Status.Value & 2) != 0) Tag("TAUNT", "FF8C59");
            return sb.ToString();
        }

        private static string StripColors(string tags) => System.Text.RegularExpressions.Regex.Replace(tags, "<[^>]+>", "");

        private float hiddenSince = -1f;

        /// <summary>Hidden in smoke: darkened violet edges and a countdown, so you know you are unseen and for how long.</summary>
        private void DrawHidden(float w, float h)
        {
            if (!life.Hidden.Value) { hiddenSince = -1f; return; }
            if (hiddenSince < 0f) hiddenSince = Time.time;
            var rogue = GetComponent<RogueKit>();
            float total = rogue != null ? rogue.HiddenDuration : 4f;
            float left = Mathf.Clamp01(1f - (Time.time - hiddenSince) / total);
            var edge = new Color(0.12f, 0.05f, 0.2f, 0.55f);
            float t = Mathf.Min(w, h) * 0.16f;
            for (int i = 0; i < 6; i++)
            {
                float k = t * (1f - i / 6f);
                var c = new Color(edge.r, edge.g, edge.b, edge.a * 0.22f);
                Rect(0, 0, w, k, c); Rect(0, h - k, w, k, c); Rect(0, 0, k, h, c); Rect(w - k, 0, k, h, c);
            }
            float bw = 220, bx = w / 2f - bw / 2f, by = h / 2f + 90;
            GUI.Label(new Rect(0, by - 22, w, 20), "HIDDEN  -  your next strike is an Ambush", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.8f, 0.65f, 1f) } });
            Rect(bx - 1, by - 1, bw + 2, 8, new Color(0f, 0f, 0f, 0.7f));
            Rect(bx, by, bw * left, 6, new Color(0.65f, 0.45f, 1f));
        }

        private static Texture2D vignetteTex;

        /// <summary>A soft radial edge (clear centre, dense corners), built once.</summary>
        private static Texture2D VignetteTex
        {
            get
            {
                if (vignetteTex != null) return vignetteTex;
                const int n = 128;
                vignetteTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(u * u * 0.9f + v * v * 1.1f);
                        float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, d));
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                vignetteTex.SetPixels32(px);
                vignetteTex.Apply();
                return vignetteTex;
            }
        }

        private Player.PlayerDebuffs debuffs;
        private bool wasPoison, wasFrost, wasCurse, wasMania, wasRoot;
        private string afflictText;
        private Color afflictColor;
        private float afflictAt = -10f;

        /// <summary>
        /// Afflictions you cannot miss (2026-10-20): each tints the screen's edge in its own colour and
        /// pulses, and the moment one takes hold its name lands big in the middle of the screen with
        /// what to do about it.
        /// </summary>
        private void DrawAfflictions(float w, float h)
        {
            debuffs ??= player.GetComponent<Player.PlayerDebuffs>();
            if (debuffs == null) return;
            bool poison = debuffs.Poisoned, frost = debuffs.Frostbitten, curse = debuffs.Cursed, mania = debuffs.Manic, root = debuffs.Rooted;
            void Alert(bool now, bool was, string text, Color c) { if (now && !was) { afflictText = text; afflictColor = c; afflictAt = Time.time; } }
            Alert(poison, wasPoison, "POISONED  -  drink Holy Water or a potion", new Color(0.55f, 1f, 0.3f));
            Alert(frost, wasFrost, "FROSTBITTEN  -  you are slowed", new Color(0.55f, 0.85f, 1f));
            Alert(curse, wasCurse, "CURSED  -  less damage and healing (kill the Hexer)", new Color(0.75f, 0.45f, 1f));
            Alert(mania, wasMania, "MANIA!  -  you cannot control your feet", new Color(1f, 0.4f, 0.9f));
            Alert(root, wasRoot, debuffs.Entangled ? "ENTANGLED  -  kill the Entling holding you" : "ROOTED  -  your feet are frozen", new Color(0.75f, 0.9f, 0.55f));
            wasPoison = poison; wasFrost = frost; wasCurse = curse; wasMania = mania; wasRoot = root;

            var old = GUI.color;
            void Edge(bool on, Color c, float rate)
            {
                if (!on) return;
                float p = 0.3f + 0.18f * Mathf.Sin(Time.time * rate);
                GUI.color = new Color(c.r, c.g, c.b, p);
                GUI.DrawTexture(new Rect(0, 0, w, h), VignetteTex, ScaleMode.StretchToFill);
            }
            Edge(poison, new Color(0.25f, 0.75f, 0.1f), 3f);
            Edge(frost, new Color(0.35f, 0.7f, 1f), 1.6f);
            Edge(curse, new Color(0.45f, 0.15f, 0.7f), 2.2f);
            Edge(mania, new Color(0.9f, 0.2f, 0.75f), 7f);
            Edge(root, new Color(0.4f, 0.55f, 0.2f), 2f);
            GUI.color = old;

            // Never over a boss's name card: the callout waits for the intro to finish.
            if (BossIntroShowing && afflictText != null && Time.time - afflictAt < 2.4f) afflictAt = Time.time;
            float age = Time.time - afflictAt;
            if (afflictText != null && age < 2.4f && !BossIntroShowing)
            {
                float a = Mathf.Clamp01((2.4f - age) / 0.6f);
                float pop = 1f + 0.35f * Mathf.Clamp01(1f - age / 0.18f);
                UiSkin.Shadowed(new Rect(0, h * 0.34f, w, 40), afflictText, new GUIStyle(big) { fontSize = Mathf.RoundToInt(28 * pop), normal = { textColor = new Color(afflictColor.r, afflictColor.g, afflictColor.b, a) } }, a);
            }
        }

        private void DrawVignette(float w, float h)
        {
            // A hit flashes the edges red; below a third of your health they pulse with your heartbeat.
            float low = 0f;
            if (health != null && health.Fraction > 0f && health.Fraction < 0.33f)
            {
                float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.time * Mathf.Lerp(4.5f, 2.6f, health.Fraction * 3f))), 6f);
                low = (0.25f + 0.35f * beat) * Mathf.InverseLerp(0.33f, 0.05f, health.Fraction) + 0.12f;
            }
            float a = Mathf.Clamp01(hurtFlash * 0.75f + low);
            if (a <= 0.01f) return;
            var old = GUI.color;
            GUI.color = new Color(0.55f, 0.02f, 0.02f, a);
            GUI.DrawTexture(new Rect(0, 0, w, h), VignetteTex, ScaleMode.StretchToFill);
            GUI.color = old;
        }

        // ------------------------------------------------------------------

        private void ProgressBar(float w, float y, float k, Color color)
        {
            float bw = 260;
            Rect(w / 2f - bw / 2f - 2, y - 2, bw + 4, 14, Panel);
            Rect(w / 2f - bw / 2f, y, bw * Mathf.Clamp01(k), 10, color);
        }

        private float hpTrail = 1f, hpHold, bossTrail = 1f, bossHold;

        /// <summary>A trailing value for a bar: holds a moment after a drop, then drains toward the real value.</summary>
        private static float Trail(ref float trail, ref float holdUntil, float now)
        {
            if (now >= trail) { trail = now; return trail; }
            if (Event.current.type != EventType.Repaint) return trail;
            if (holdUntil < Time.time - 2f) holdUntil = Time.time + 0.45f; // a fresh drop
            if (Time.time >= holdUntil) trail = Mathf.MoveTowards(trail, now, Time.unscaledDeltaTime * 0.8f);
            if (trail - now < 0.001f) { trail = now; holdUntil = 0f; }
            return trail;
        }

        private static void Bar(float x, float y, float width, float height, float k, Color color, float trail = -1f)
        {
            UiSkin.Bar(new Rect(x, y, width, height), k, color, height >= 18f ? 10 : 0, trail);
            return;
            Rect(x, y, width * Mathf.Clamp01(k), height * 0.35f, new Color(1f, 1f, 1f, 0.12f));
        }

        private static void Rect(float x, float y, float width, float height, Color color)
        {
            // Every HUD panel goes through here: draw those as the skin's ornate panel.
            if (color == Panel && width > 30f && height > 20f)
            {
                UiSkin.Panel(new Rect(x, y, width, height), 0.86f);
                return;
            }
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static int CurrentDepth()
        {
            var d = DungeonDirector.Instance;
            return d != null ? Mathf.Max(0, d.Floor.Value.y) : 0;
        }
    }
}
