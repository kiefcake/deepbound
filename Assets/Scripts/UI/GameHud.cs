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

        private bool shopOpen, forgeOpen;

        /// <summary>Screenshots only: stage the forge panel open.</summary>
        public void DebugOpenForge() => forgeOpen = true;

        /// <summary>Screenshots only: stage the merchant's panel open.</summary>
        public void DebugOpenShop() { shopOpen = true; forgeOpen = false; }

        /// <summary>Screenshots only: hold the stat sheet open.</summary>
        public static bool DebugShowStats;
        private bool statsHeld;

        private struct FeedLine { public string Text; public Color Color; public float Time; }
        private static readonly List<FeedLine> FeedLines = new List<FeedLine>();

        private static string cardTitle, cardSub;
        private static float cardUntil;

        /// <summary>A big centred announcement: a boss's name as its arena seals, an ambush.</summary>
        public static void ShowTitleCard(string title, string subtitle)
        {
            cardTitle = title;
            cardSub = subtitle;
            cardUntil = UnityEngine.Time.time + 3.5f;
        }

        private void DrawTitleCard(float w, float h)
        {
            if (cardTitle == null || Time.time > cardUntil) return;
            float a = Mathf.Clamp01((cardUntil - Time.time) / 1f) * Mathf.Clamp01((3.5f - (cardUntil - Time.time)) * 4f);
            Rect(0, h * 0.3f - 12, w, 96, new Color(0f, 0f, 0f, 0.55f * a));
            UiSkin.Shadowed(new Rect(0, h * 0.3f - 4, w, 54), cardTitle, new GUIStyle(big) { fontSize = 44, normal = { textColor = new Color(0.9f, 0.3f, 0.2f) } }, a);
            GUI.Label(new Rect(0, h * 0.3f + 50, w, 26), cardSub, new GUIStyle(center) { fontSize = 17, normal = { textColor = new Color(1f, 1f, 1f, a) } });
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

        private static readonly string[] FloorNames = { "The Ossuary", "The Drowned Vaults", "The Hollow Throne", "The Black Cathedral" };

        public void Init(NetworkPlayer owner)
        {
            player = owner;
            health = owner.GetComponent<Health>();
            life = owner.GetComponent<PlayerLife>();
            progress = owner.GetComponent<PlayerProgress>();
            health.Hurt += amount => hurtFlash = Mathf.Min(1f, hurtFlash + amount / 30f);
            spawnTime = Time.time;
            Health.AnyHurt += OnAnyHurt;
            Health.AnyHealed += OnAnyHealed;
            DungeonDirector.ArenaBuilt += OnArenaBuilt;
            if (DungeonDirector.ArenaId > 0) OnArenaBuilt(DungeonDirector.ArenaId);
        }

        private void LateUpdate()
        {
            // Esc closes the shop or class screen first; only a second Esc pauses.
            PauseMenu.OtherMenuOpenLastFrame = shopOpen || forgeOpen || CharacterSelect.Open || Wardrobe.Open;
        }

        private void OnDestroy()
        {
            MenuOpen = false;
            Health.AnyHurt -= OnAnyHurt;
            Health.AnyHealed -= OnAnyHealed;
            DungeonDirector.ArenaBuilt -= OnArenaBuilt;
        }

        // ---- Arena transitions: fade through black, then a title banner ----

        private float fade;
        private string bannerTitle, bannerSub;

        private void OnArenaBuilt(int _)
        {
            fade = 1f;
            bannerUntil = Time.time + 5f;
            if (DungeonDirector.InLobby || DungeonDirector.Layout == null)
            {
                bannerTitle = "THE LAST LANTERN";
                bannerSub = "Gather at the carriage when your party is ready";
            }
            else
            {
                int depth = DungeonDirector.Layout.Depth;
                bannerTitle = "FLOOR " + (depth + 1);
                bannerSub = depth < FloorNames.Length ? FloorNames[depth] : "The Deep";
            }
        }

        // ---- Floating damage numbers ----

        private struct DamageNumber { public Vector3 World; public float Amount; public float Time; public bool Player; public bool Heal; }

        private void OnAnyHealed(Health target, float amount)
        {
            // Only meaningful heals (not regen ticks), and only on the party.
            if (target == null || amount < 4f || target.Team != Team.Players) return;
            var jitter = new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));
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
            numbers.Add(new DamageNumber { World = target.Center + Vector3.up * (target.CenterHeight + 0.2f) + jitter, Amount = amount, Time = Time.time, Player = target.Team == Team.Players });
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
                GUI.Label(new Rect(0, h / 2f + 64, w, 24), "TRAINING:  " + Mathf.RoundToInt(sum / span) + " damage per second",
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

                var screen = cam.WorldToScreenPoint(n.World + Vector3.up * age * 0.8f);
                if (screen.z <= 0f || screen.z > 30f) continue;

                bool heavy = n.Amount >= 35f;
                float a = 1f - age * age;
                var color = n.Heal ? new Color(0.45f, 1f, 0.5f, a) : n.Player ? new Color(1f, 0.35f, 0.3f, a) : heavy ? new Color(1f, 0.82f, 0.35f, a) : new Color(1f, 1f, 1f, a);
                var style = new GUIStyle(big) { fontSize = heavy ? 24 : 18, normal = { textColor = color } };
                var shadow = new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, a * 0.8f) } };
                var rect = new Rect(screen.x - 50, Screen.height - screen.y - 15, 100, 30);
                string text = (n.Heal ? "+" : "") + Mathf.RoundToInt(n.Amount);
                GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, shadow);
                GUI.Label(rect, text, style);
            }
        }

        /// <summary>
        /// Camp banner, top of the screen and out of the way: where you are, what is next,
        /// your class and the difficulty (both only changeable before a run), and boarding.
        /// </summary>
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
                : cls + " (B to change)   ·   " + diff.Name + (host ? " difficulty ( [ / ] )" : " difficulty");
            GUI.Label(new Rect(x, y + 24, pw, 18), line2, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(x, y + 41, pw, 18), "Gather in the golden circle at the gate   (" + aboard + " / " + total + " aboard)",
                new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = aboard > 0 ? Gold : new Color(0.85f, 0.82f, 0.78f) } });
            if (counting)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(director.PhaseEndsAt.Value - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time)));
                GUI.Label(new Rect(x, y + 58, pw, 24), "The carriage departs in " + seconds + "...", new GUIStyle(center) { fontSize = 17, normal = { textColor = Gold } });
            }
        }

        private void DrawFade(float w, float h)
        {
            fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime * 0.8f);
            if (fade <= 0f) return;
            Rect(0, 0, w, h, new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, fade)));
        }

        private bool NearMerchant()
        {
            var merchant = DungeonDirector.MerchantPosition;
            return merchant != null && !life.Downed.Value &&
                   Vector3.Distance(player.transform.position, merchant.Value) <= Shop.MerchantRange;
        }

        private void Update()
        {
            hurtFlash = Mathf.MoveTowards(hurtFlash, 0f, Time.deltaTime * 1.5f);

            var keyboard = Keyboard.current;
            bool near = NearMerchant();
            var anvil = NearAnvil();
            // G uses whichever station is closer: the merchant or the anvil beside him.
            bool anvilFirst = anvil != null && (!near || Vector3.Distance(player.transform.position, anvil.transform.position) <
                Vector3.Distance(player.transform.position, DungeonDirector.MerchantPosition.Value));
            if (keyboard != null && keyboard.gKey.wasPressedThisFrame)
            {
                if (anvilFirst) { forgeOpen = !forgeOpen; shopOpen = false; }
                else if (near) { shopOpen = !shopOpen; forgeOpen = false; }
            }
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) shopOpen = forgeOpen = false;
            if (keyboard != null && keyboard.xKey.wasPressedThisFrame && !MenuOpen &&
                Interactable.Nearest(player.AimOrigin, player.AimForward, progress) is GearDrop scrap)
                scrap.ScrapRpc(player.NetworkObject);
            if (!near) shopOpen = false;
            if (anvil == null) forgeOpen = false;
            MenuOpen = shopOpen || forgeOpen || CharacterSelect.Open || PauseMenu.Open || Wardrobe.Open;
            statsHeld = DebugShowStats || (keyboard != null && keyboard.tabKey.isPressed);

            // The host sets difficulty at camp.
            var dir = DungeonDirector.Instance;
            if (keyboard != null && dir != null && DungeonDirector.InLobby && Unity.Netcode.NetworkManager.Singleton.IsHost)
            {
                int cur = dir.DifficultyIndex.Value;
                if (keyboard.rightBracketKey.wasPressedThisFrame) dir.SetDifficultyRpc((byte)Mathf.Min(cur + 1, Enemies.Difficulty.Settings.Length - 1));
                if (keyboard.leftBracketKey.wasPressedThisFrame) dir.SetDifficultyRpc((byte)Mathf.Max(cur - 1, 0));
            }

            // Class choice is a camp decision: number keys pick one, only in the lobby.
            if (keyboard != null && DungeonDirector.InLobby)
            {
                for (int i = 0; i < ClassDefinition.ById.Length && i < 9; i++)
                {
                    var key = keyboard[(Key)((int)Key.Digit1 + i)];
                    if (key.wasPressedThisFrame && progress.ClassId.Value != (ClassId)i)
                    {
                        progress.ClassId.Value = (ClassId)i;
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

            DrawEnemyBars();
            DrawDamageNumbers();
            DrawCombos();
            DrawCampReadouts(w, h);
            DrawHidden(w, h);
            DrawPings(w, h);
            DrawVignette(w, h);
            DrawCrosshair(w, h);
            DrawVitals(w, h);
            DrawAbilities(w, h);
            DrawBoss(w);
            DrawRevive(w, h);
            DrawPhase(w, h);
            DrawBanner(w, h);
            DrawTitleCard(w, h);
            if (!statsHeld) DrawControlsHint(w, h);
            DrawFeed(h);
            DrawForge(w, h);
                DrawMerchant(w, h);
            DrawLobby(w, h);
            if (statsHeld) DrawStatSheet(w, h);
            DrawFade(w, h);
        }

        // ------------------------------------------------------------------

        private void DrawCrosshair(float w, float h)
        {
            if (life.Downed.Value) return;
            var c = new Vector2(w / 2f, h / 2f);

            // Charge meter (bow draw) under the crosshair; gold when full.
            float charge = player.ActiveKit.Charge;
            if (charge > 0f)
            {
                Rect(c.x - 31, c.y + 17, 62, 6, new Color(0f, 0f, 0f, 0.7f));
                Rect(c.x - 30, c.y + 18, 60 * charge, 4, charge >= 1f ? Gold : new Color(0.8f, 0.85f, 0.7f));
            }
            Rect(c.x - 1.5f, c.y - 1.5f, 3, 3, new Color(1f, 1f, 1f, 0.9f));
            var tick = new Color(1f, 1f, 1f, 0.45f);
            Rect(c.x - 11, c.y - 0.5f, 6, 1, tick);
            Rect(c.x + 5, c.y - 0.5f, 6, 1, tick);
            Rect(c.x - 0.5f, c.y + 5, 1, 6, tick);
        }

        private void DrawVitals(float w, float h)
        {
            float x = 24, y = h - 110, bw = 300;
            DrawRelicRow(x, y - 44);
            if (progress.ActiveBlessing.Value != 0)
            {
                var b = (Blessing)progress.ActiveBlessing.Value;
                GUI.Label(new Rect(x, y - 66, bw, 20), Blessings.Name(b) + "  -  " + Blessings.Describe(b), new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Blessings.Color(b) } });
            }
            Rect(x - 8, y - 10, bw + 16, 96, Panel);

            float hp = health.Current.Value, max = Mathf.Max(1f, health.Max.Value);
            Bar(x, y, bw, 22, hp / max, HealthRed);
            // Aegis shield, drawn as a gold band over the health bar.
            if (health.Shield.Value > 0f)
            {
                Rect(x, y, bw * Mathf.Clamp01(health.Shield.Value / max), 7, new Color(1f, 0.85f, 0.4f, 0.95f));
            }
            GUI.Label(new Rect(x + 8, y + 1, bw, 22), Mathf.CeilToInt(hp) + " / " + Mathf.CeilToInt(max), label);

            var kit = player.ActiveKit;
            Bar(x, y + 32, bw, 12, kit.ResourceMax > 0f ? kit.Resource / kit.ResourceMax : 0f, kit.ResourceColor);
            string cls = progress.Class.Name.ToUpperInvariant();
            string weapon = ClassDefinition.WeaponName(progress.ClassId.Value) + " +" + progress.WeaponLevel.Value;
            GUI.Label(new Rect(x, y + 46, bw, 20), (DungeonDirector.InLobby ? cls + "  ·  At camp" : cls + "  ·  Depth " + (CurrentDepth() + 1) + " / " + DungeonDirector.FloorsPerRun) + "  ·  " + weapon, small);
            GUI.Label(new Rect(x, y + 64, bw, 20), progress.Embers.Value + " embers", new GUIStyle(small) { normal = { textColor = LootInfo.EmberColor } });
            if (progress.Sigils.Value > 0)
                GUI.Label(new Rect(x + 90, y + 64, bw, 20), progress.Sigils.Value + (progress.Sigils.Value == 1 ? " sigil" : " sigils"), new GUIStyle(small) { normal = { textColor = LootInfo.SigilColor } });
            GUI.Label(new Rect(x, y + 64, bw, 20), progress.Gold.Value + " gold", new GUIStyle(small) { alignment = TextAnchor.UpperRight, normal = { textColor = Gold } });
        }

        private void DrawAbilities(float w, float h)
        {
            float size = 58, gap = 10;
            float total = size * 5 + gap * 4;
            float x = w / 2f - total / 2f, y = h - size - 26;

            abilities.Clear();
            player.ActiveKit.FillAbilities(abilities);
            abilities.Add(new AbilityView { Key = "CTRL", Name = "Dodge", Remaining = life.DodgeReadyAt - Time.time, Cooldown = life.DodgeCooldownNow });
            for (int i = 0; i < abilities.Count; i++)
            {
                var a = abilities[i];
                Ability(x + (size + gap) * i, y, size, a.Key, a.Name, a.Remaining, a.Cooldown, a.Active, a.Unaffordable);
            }
        }

        private void Ability(float x, float y, float size, string key, string name, float remaining, float cooldown, bool active = false, bool unaffordable = false)
        {
            UiSkin.Panel(new Rect(x, y, size, size), 0.9f);
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
                GUI.Label(new Rect(x, y, size, size), Mathf.CeilToInt(remaining).ToString(), new GUIStyle(big) { fontSize = 22 });
            }
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
            Bar(x, y + 24, bw, 14, boss.Health.Fraction, new Color(0.55f, 0.08f, 0.1f));
            string tags = StatusTags(boss.Health, boss);
            if (boss.Health.Fraction < 0.25f) tags = "<color=#E0403A>ENRAGED</color>" + (tags.Length > 0 ? "   " + tags : "");
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
                GUI.Label(new Rect(x + 10, ry + 52, cw - 20, 18), "X   scrap for " + Gear.ScrapValue(item) + " embers", new GUIStyle(small) { normal = { textColor = LootInfo.EmberColor } });
            }
        }

        private void DrawRevive(float w, float h)
        {
            if (life.Downed.Value)
            {
                Rect(0, 0, w, h, new Color(0.35f, 0f, 0f, 0.25f));
                GUI.Label(new Rect(0, h / 2f - 70, w, 40), "YOU ARE DOWN", big);
                GUI.Label(new Rect(0, h / 2f - 30, w, 24), "A teammate must hold F next to you to revive you", center);
                ProgressBar(w, h / 2f + 4, life.ReviveProgress.Value / PlayerLife.ReviveSeconds, new Color(0.6f, 0.9f, 1f));
                return;
            }

            var target = PlayerLife.DownedNear(player.transform.position, life);
            if (target == null)
            {
                var use = Interactable.Nearest(player.AimOrigin, player.AimForward, progress);
                if (use != null && !statsHeld) GUI.Label(new Rect(0, h / 2f + 40, w, 24), "F   " + use.Prompt(progress), center);
                if (use is GearDrop gear && !statsHeld) DrawGearCard(gear.Item.Value, w / 2f - 180, h / 2f + 70, true);
                return;
            }

            GUI.Label(new Rect(0, h / 2f + 40, w, 24), "Hold F to revive " + target.name, center);
            ProgressBar(w, h / 2f + 68, target.ReviveProgress.Value / PlayerLife.ReviveSeconds, new Color(0.6f, 0.9f, 1f));
        }

        private void DrawPhase(float w, float h)
        {
            var director = DungeonDirector.Instance;
            // Wipes and victories get the full run summary instead.
            if (director == null || director.Phase.Value != RunPhase.FloorCleared) return;

            int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(director.PhaseEndsAt.Value - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time)));
            string title, sub;
            switch (director.Phase.Value)
            {
                case RunPhase.FloorCleared: title = "THE FLOOR IS CLEARED"; sub = "Back to the castle in " + seconds + "... rest, trade, then ride deeper."; break;
                case RunPhase.Wiped: title = "YOUR PARTY HAS FALLEN"; sub = "The run is over. Returning to camp in " + seconds + "..."; break;
                default: title = "THE DEPTHS ARE CLEARED"; sub = "Run complete! Returning to camp in " + seconds + "..."; break;
            }

            Rect(0, h * 0.28f - 10, w, 90, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(0, h * 0.28f, w, 44), title, new GUIStyle(big) { normal = { textColor = Gold } });
            GUI.Label(new Rect(0, h * 0.28f + 46, w, 24), sub, center);
        }

        private void DrawBanner(float w, float h)
        {
            if (Time.time > bannerUntil || bannerTitle == null) return;
            float a = Mathf.Clamp01((bannerUntil - Time.time) / 1.5f) * Mathf.Clamp01(1f - fade * 1.5f);
            var gold = new Color(Gold.r, Gold.g, Gold.b, a);
            GUI.Label(new Rect(0, h * 0.18f, w, 44), bannerTitle, new GUIStyle(big) { normal = { textColor = gold } });
            GUI.Label(new Rect(0, h * 0.18f + 44, w, 26), bannerSub, new GUIStyle(center) { fontSize = 18, normal = { textColor = new Color(1f, 1f, 1f, a) } });
        }

        private void DrawControlsHint(float w, float h)
        {
            float age = Time.time - spawnTime;
            if (age > 25f) return;
            float a = Mathf.Clamp01((25f - age) / 3f);
            var style = new GUIStyle(small) { alignment = TextAnchor.UpperRight, normal = { textColor = new Color(1f, 1f, 1f, a * 0.85f) } };
            GUI.Label(new Rect(w - 330, 262, 310, 150),
                "WASD move  ·  Shift sprint  ·  Space jump\n" +
                (progress.ClassId.Value == ClassId.Ranger
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
                "F  revive a downed ally  ·  G  trade  ·  Tab  stats\n" +
                "CTRL dodge  ·  M map  ·  Z / middle mouse: ping\n" +
                "Hit enemies from behind for +35%  ·  Esc free mouse", style);
        }

        private void DrawFeed(float h)
        {
            float y = h * 0.55f;
            for (int i = FeedLines.Count - 1; i >= 0; i--)
            {
                var line = FeedLines[i];
                float age = Time.time - line.Time;
                if (age > 6f) continue;
                float a = Mathf.Clamp01((6f - age) / 1f);
                GUI.Label(new Rect(24, y, 500, 22), line.Text, new GUIStyle(label) { normal = { textColor = new Color(line.Color.r, line.Color.g, line.Color.b, a) } });
                y -= 22;
            }
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
                if (!NearMerchant() || Vector3.Distance(player.transform.position, NearAnvil().transform.position) <
                    Vector3.Distance(player.transform.position, DungeonDirector.MerchantPosition.Value))
                    GUI.Label(new Rect(0, h / 2f + 40, w, 24), "Press G to use the anvil", center);
                return;
            }

            var id = progress.ClassId.Value;
            int level = progress.WeaponLevel.Value, path = progress.WeaponPath.Value;
            string weapon = ClassDefinition.WeaponName(id);
            const float leftW = 620f;
            float pw = leftW + 340, ph = 470;
            float x = w / 2f - pw / 2f, y = h / 2f - ph / 2f;
            var ember = LootInfo.EmberColor;
            Rect(x - 3, y - 3, pw + 6, ph + 6, new Color(ember.r, ember.g, ember.b, 0.5f));
            Rect(x, y, pw, ph, new Color(0.05f, 0.03f, 0.02f, 0.95f));
            GUI.Label(new Rect(x + 16, y + 12, pw, 24), "THE ANVIL", new GUIStyle(label) { normal = { textColor = ember } });
            GUI.Label(new Rect(x + pw - 300, y + 12, 284, 24), progress.Sigils.Value + " sigils    " + progress.Embers.Value + " embers", new GUIStyle(label) { alignment = TextAnchor.UpperRight, normal = { textColor = ember } });

            // Level pips and the temper button.
            GUI.Label(new Rect(x + 16, y + 44, 200, 24), weapon + " +" + level, new GUIStyle(label) { fontSize = 20 });
            for (int i = 0; i < Anvil.MaxLevel; i++)
                Rect(x + 180 + i * 26, y + 50, 20, 14, i < level ? ember : new Color(ember.r, ember.g, ember.b, 0.18f));
            string reason = progress.CannotTemperReason();
            string next = level >= Anvil.MaxLevel ? "Fully tempered"
                : "Temper to +" + (level + 1) + ":  +" + ClassDefinition.WeaponAttPerLevel + " ATT, +" + ClassDefinition.WeaponDexPerLevel + " DEX" +
                  (level + 1 == WeaponPaths.MasteryLevel && path != 0 ? ", path mastery" : "");
            GUI.Label(new Rect(x + 16, y + 76, leftW - 200, 20), next, small);
            GUI.enabled = reason == null;
            if (GUI.Button(new Rect(x + leftW - 186, y + 46, 170, 30), reason ?? ("Temper  (" + Anvil.Cost(level) + " embers)"))) progress.TemperRpc();
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

            // Right column: abilities (Sigils), then runes.
            float rx = x + leftW + 4, rw = pw - leftW - 20;
            Rect(rx - 10, y + 40, 1, ph - 56, new Color(1f, 1f, 1f, 0.08f));
            float ay = y + 44;
            GUI.Label(new Rect(rx, ay, rw, 20), "ABILITIES  -  a Sigil empowers one rank", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = LootInfo.SigilColor } });
            foreach (var slot in new[] { AbilitySlot.Q, AbilitySlot.E })
            {
                float ry = ay + 22 + (int)slot * 88;
                int rank = progress.Rank(slot);
                GUI.Label(new Rect(rx, ry, rw - 80, 22), slot + "  " + ClassDefinition.AbilityName(id, slot), label);
                for (int r = 0; r < AbilityRanks.MaxRank; r++)
                    Rect(rx + rw - 70 + r * 22, ry + 6, 16, 12, r < rank ? LootInfo.SigilColor : new Color(LootInfo.SigilColor.r, LootInfo.SigilColor.g, LootInfo.SigilColor.b, 0.2f));
                string nextText = rank >= AbilityRanks.MaxRank ? "Rank 3: " + AbilityRanks.Twist(id, slot)
                    : rank == 1 ? "Rank 2: " + AbilityRanks.Rank2(id, slot) : "Rank 3: 25% shorter cooldown. " + AbilityRanks.Twist(id, slot);
                GUI.Label(new Rect(rx, ry + 21, rw, 34), nextText, new GUIStyle(small) { wordWrap = true });
                GUI.enabled = rank < AbilityRanks.MaxRank && progress.Sigils.Value > 0;
                if (GUI.Button(new Rect(rx, ry + 56, rw, 24), rank >= AbilityRanks.MaxRank ? "Max rank" : progress.Sigils.Value > 0 ? "Empower  (1 sigil)" : "Needs a Sigil"))
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
            GUI.Label(new Rect(rx, uy, rw, 20), "RUNES  -  sockets open at +1 and +3", new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            for (int sIdx = 0; sIdx < 2; sIdx++)
            {
                float sy = uy + 22 + sIdx * 38;
                var r = progress.Socketed(sIdx);
                bool open = sIdx < sockets;
                Rect(rx, sy + 3, 10, 10, open ? (r == Rune.None ? new Color(1f, 1f, 1f, 0.25f) : Runes.Color(r)) : new Color(1f, 1f, 1f, 0.06f));
                GUI.Label(new Rect(rx + 16, sy - 2, rw - 16, 18), open ? Runes.Name(r) : "Locked  (weapon +" + (sIdx == 0 ? 1 : 3) + ")",
                    new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = open && r != Rune.None ? Runes.Color(r) : new Color(1f, 1f, 1f, 0.5f) } });
                if (open && r != Rune.None) GUI.Label(new Rect(rx + 16, sy + 14, rw - 16, 18), Runes.Describe(r), small);
            }
            float by = uy + 22 + 2 * 38 + 2;
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
            if (!string.IsNullOrEmpty(GUI.tooltip)) GUI.Label(new Rect(x + 16, y + ph - 26, pw - 32, 20), GUI.tooltip, new GUIStyle(small) { normal = { textColor = Gold } });
        }

        private void DrawMerchant(float w, float h)
        {
            if (!NearMerchant() || forgeOpen) return;
            var nearAnvil = NearAnvil();
            if (!shopOpen && nearAnvil != null && Vector3.Distance(player.transform.position, nearAnvil.transform.position) <
                Vector3.Distance(player.transform.position, DungeonDirector.MerchantPosition.Value)) return;

            if (!shopOpen)
            {
                GUI.Label(new Rect(0, h / 2f + 40, w, 24), "Press G to trade with the merchant", center);
                return;
            }

            float pw = 600, rowH = 38;
            var stock = Shop.For(progress.OwnerClientId);
            float ph = 70 + stock.Length * rowH;
            float x = w / 2f - pw / 2f, y = h / 2f - ph / 2f;
            Rect(x - 3, y - 3, pw + 6, ph + 6, new Color(Gold.r, Gold.g, Gold.b, 0.5f));
            Rect(x, y, pw, ph, new Color(0.04f, 0.035f, 0.03f, 0.94f));
            GUI.Label(new Rect(x + 14, y + 10, pw, 24), "THE MERCHANT", new GUIStyle(label) { normal = { textColor = Gold } });
            GUI.Label(new Rect(x + 14, y + 30, pw - 28, 20), "\"Coin for courage, friend. It all spends the same down here.\"", small);
            GUI.Label(new Rect(x + pw - 120, y + 10, 106, 24), progress.Gold.Value + " gold", new GUIStyle(label) { alignment = TextAnchor.UpperRight, normal = { textColor = Gold } });

            for (int i = 0; i < stock.Length; i++)
            {
                var item = stock[i];
                float ry = y + 60 + i * rowH;
                string reason = progress.CannotBuyReason(i);

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
                    progress.BuyRpc(i);
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

            float pw = 300, rowH = 26, ph = 50 + Stats.Count * rowH + 70 + 88;
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
            for (int sIdx = 0; sIdx < 2; sIdx++)
            {
                var r = progress.Socketed(sIdx);
                bool open = sIdx < Runes.SocketsAt(progress.WeaponLevel.Value);
                GUI.Label(new Rect(x + 12, gy, 70, 20), "RUNE " + (sIdx + 1), small);
                GUI.Label(new Rect(x + 80, gy, pw - 90, 20), !open ? "locked" : r == Rune.None ? "-" : Runes.Name(r),
                    new GUIStyle(small) { normal = { textColor = open && r != Rune.None ? Runes.Color(r) : new Color(1f, 1f, 1f, 0.4f) } });
                gy += 22;
            }
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

        private void DrawEnemyBars()
        {
            var cam = player.Camera;
            if (cam == null) return;

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
            if (brain != null && brain.AffixKind != EliteAffix.None) Tag(EliteAffixes.Tag(brain.AffixKind), EliteAffixes.Hex(brain.AffixKind));
            if (brain is WraithBrain wr && !wr.Pinned) Tag("ETHEREAL", "8FA8D0");
            if (hp.Chilled.Value) Tag("CHILL", "8CD9FF");
            if (hp.Marked.Value) Tag("MARK", "FF5A4D");
            if (brain != null && (brain.Status.Value & 1) != 0) Tag("STUN", "FFD94D");
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

        private void DrawVignette(float w, float h)
        {
            if (hurtFlash <= 0f) return;
            var c = new Color(0.6f, 0f, 0f, hurtFlash * 0.45f);
            float t = Mathf.Min(w, h) * 0.12f;
            Rect(0, 0, w, t, c); Rect(0, h - t, w, t, c);
            Rect(0, t, t, h - 2 * t, c); Rect(w - t, t, t, h - 2 * t, c);
        }

        // ------------------------------------------------------------------

        private void ProgressBar(float w, float y, float k, Color color)
        {
            float bw = 260;
            Rect(w / 2f - bw / 2f - 2, y - 2, bw + 4, 14, Panel);
            Rect(w / 2f - bw / 2f, y, bw * Mathf.Clamp01(k), 10, color);
        }

        private static void Bar(float x, float y, float width, float height, float k, Color color)
        {
            UiSkin.Bar(new Rect(x, y, width, height), k, color, height >= 18f ? 10 : 0);
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
