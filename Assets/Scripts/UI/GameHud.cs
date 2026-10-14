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

        private static readonly string[] FloorNames = { "The Ossuary", "The Drowned Vaults", "The Hollow Throne" };

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
            PauseMenu.OtherMenuOpenLastFrame = shopOpen || forgeOpen || CharacterSelect.Open;
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
            bool mine = target == health;
            // Your own damage shows on your vitals; teammates' and enemies' float.
            if (mine) return;
            var jitter = new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));
            numbers.Add(new DamageNumber { World = target.Center + Vector3.up * (target.CenterHeight + 0.2f) + jitter, Amount = amount, Time = Time.time, Player = target.Team == Team.Players });
            if (numbers.Count > 40) numbers.RemoveAt(0);
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

        private void DrawLobby(float w, float h)
        {
            var director = DungeonDirector.Instance;
            if (director == null || !DungeonDirector.InLobby) return;

            int total = PlayerLife.All.Count, aboard = DungeonDirector.PlayersAboard();
            bool counting = director.PhaseEndsAt.Value > 0;
            float y = h - 170;

            Rect(w / 2f - 230, y - 6, 460, counting ? 62 : 44, Panel);
            GUI.Label(new Rect(0, y, w, 24), "Stand in the golden circle by the carriage to descend   (" + aboard + " / " + total + " aboard)", center);

            // Current class and how to change it (the class screen scales to any roster;
            // number keys still work as a shortcut).
            float cy = y - 64;
            Rect(w / 2f - 230, cy - 6, 460, 52, Panel);
            var current = progress.ClassId.Value;
            GUI.Label(new Rect(0, cy, w, 22), "You are the " + ClassDefinition.For(current).Name.ToUpperInvariant() + "   ·   press B to change class",
                new GUIStyle(center) { normal = { textColor = Gold } });
            GUI.Label(new Rect(0, cy + 22, w, 20), ClassDefinition.Pitch(current), new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });

            // Difficulty: the host chooses with [ and ]; everyone sees it.
            var diff = Enemies.Difficulty.Level;
            float dy = cy - 50;
            Rect(w / 2f - 230, dy - 6, 460, 40, Panel);
            bool host = Unity.Netcode.NetworkManager.Singleton.IsHost;
            GUI.Label(new Rect(0, dy - 2, w, 20), "DIFFICULTY:  " + diff.Name.ToUpperInvariant() + (host ? "      ( [  /  ]  to change )" : ""),
                new GUIStyle(center) { normal = { textColor = diff.Name == "Normal" ? Color.white : new Color(1f, 0.45f, 0.3f) } });
            GUI.Label(new Rect(0, dy + 16, w, 18), diff.Blurb, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            if (counting)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(director.PhaseEndsAt.Value - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time)));
                GUI.Label(new Rect(0, y + 24, w, 26), "The carriage departs in " + seconds + "...", new GUIStyle(center) { fontSize = 18, normal = { textColor = Gold } });
            }
            else
            {
                GUI.Label(new Rect(0, y + 20, w, 20), "Test your kit on the training dummies to the west", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
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
            var layout = DungeonDirector.Layout;
            return layout != null && !life.Downed.Value &&
                   Vector3.Distance(player.transform.position, layout.MerchantPosition) <= Shop.MerchantRange;
        }

        private void Update()
        {
            hurtFlash = Mathf.MoveTowards(hurtFlash, 0f, Time.deltaTime * 1.5f);

            var keyboard = Keyboard.current;
            bool near = NearMerchant();
            var anvil = NearAnvil();
            // G uses whichever station is closer: the merchant or the anvil beside him.
            bool anvilFirst = anvil != null && (!near || Vector3.Distance(player.transform.position, anvil.transform.position) <
                Vector3.Distance(player.transform.position, DungeonDirector.Layout.MerchantPosition));
            if (keyboard != null && keyboard.gKey.wasPressedThisFrame)
            {
                if (anvilFirst) { forgeOpen = !forgeOpen; shopOpen = false; }
                else if (near) { shopOpen = !shopOpen; forgeOpen = false; }
            }
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) shopOpen = forgeOpen = false;
            if (!near) shopOpen = false;
            if (anvil == null) forgeOpen = false;
            MenuOpen = shopOpen || forgeOpen || CharacterSelect.Open || PauseMenu.Open;
            statsHeld = keyboard != null && keyboard.tabKey.isPressed;

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
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = 12, fontStyle = FontStyle.Normal };
            big = new GUIStyle(label) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        }

        private void OnGUI()
        {
            if (player == null) return;
            EnsureStyles();
            if (CharacterSelect.Open) return;

            float w = Screen.width, h = Screen.height;

            DrawEnemyBars();
            DrawDamageNumbers();
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
            DrawControlsHint(w, h);
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
            Rect(x - 2, y - 2, size + 4, size + 4, active ? Gold : new Color(0.35f, 0.3f, 0.22f, 0.9f));
            Rect(x, y, size, size, Panel);
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
            if (boss.Health.Fraction < 0.25f)
            {
                GUI.Label(new Rect(x, y + 40, bw, 20), "ENRAGED", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = HealthRed } });
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
                if (use != null) GUI.Label(new Rect(0, h / 2f + 40, w, 24), "F   " + use.Prompt(progress), center);
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
                case RunPhase.FloorCleared: title = "THE KING FALLS"; sub = "Descending deeper in " + seconds + "..."; break;
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
                    Vector3.Distance(player.transform.position, DungeonDirector.Layout.MerchantPosition))
                    GUI.Label(new Rect(0, h / 2f + 40, w, 24), "Press G to use the anvil", center);
                return;
            }

            var id = progress.ClassId.Value;
            int level = progress.WeaponLevel.Value, path = progress.WeaponPath.Value;
            string weapon = ClassDefinition.WeaponName(id);
            float pw = 620, ph = 330;
            float x = w / 2f - pw / 2f, y = h / 2f - ph / 2f;
            var ember = LootInfo.EmberColor;
            Rect(x - 3, y - 3, pw + 6, ph + 6, new Color(ember.r, ember.g, ember.b, 0.5f));
            Rect(x, y, pw, ph, new Color(0.05f, 0.03f, 0.02f, 0.95f));
            GUI.Label(new Rect(x + 16, y + 12, pw, 24), "THE ANVIL", new GUIStyle(label) { normal = { textColor = ember } });
            GUI.Label(new Rect(x + pw - 160, y + 12, 144, 24), progress.Embers.Value + " embers", new GUIStyle(label) { alignment = TextAnchor.UpperRight, normal = { textColor = ember } });

            // Level pips and the temper button.
            GUI.Label(new Rect(x + 16, y + 44, 200, 24), weapon + " +" + level, new GUIStyle(label) { fontSize = 20 });
            for (int i = 0; i < Anvil.MaxLevel; i++)
                Rect(x + 180 + i * 26, y + 50, 20, 14, i < level ? ember : new Color(ember.r, ember.g, ember.b, 0.18f));
            string reason = progress.CannotTemperReason();
            string next = level >= Anvil.MaxLevel ? "Fully tempered"
                : "Temper to +" + (level + 1) + ":  +" + ClassDefinition.WeaponAttPerLevel + " ATT, +" + ClassDefinition.WeaponDexPerLevel + " DEX" +
                  (level + 1 == WeaponPaths.MasteryLevel && path != 0 ? ", path mastery" : "");
            GUI.Label(new Rect(x + 16, y + 76, pw - 200, 20), next, small);
            GUI.enabled = reason == null;
            if (GUI.Button(new Rect(x + pw - 186, y + 46, 170, 30), reason ?? ("Temper  (" + Anvil.Cost(level) + " embers)"))) progress.TemperRpc();
            GUI.enabled = true;

            // The two paths.
            string head = path != 0 ? "PATH" : level >= WeaponPaths.PathLevel ? "CHOOSE A PATH (once per run)" : "PATHS  -  choose one at +" + WeaponPaths.PathLevel;
            GUI.Label(new Rect(x + 16, y + 108, pw, 20), head, new GUIStyle(small) { fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            for (int p = 1; p <= 2; p++)
            {
                float cx = x + 16 + (p - 1) * ((pw - 44) / 2f + 12), cw = (pw - 44) / 2f, cy = y + 132, chh = 182;
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
        }

        private void DrawMerchant(float w, float h)
        {
            if (!NearMerchant() || forgeOpen) return;
            var nearAnvil = NearAnvil();
            if (!shopOpen && nearAnvil != null && Vector3.Distance(player.transform.position, nearAnvil.transform.position) <
                Vector3.Distance(player.transform.position, DungeonDirector.Layout.MerchantPosition)) return;

            if (!shopOpen)
            {
                GUI.Label(new Rect(0, h / 2f + 40, w, 24), "Press G to trade with the merchant", center);
                return;
            }

            float pw = 440, rowH = 38;
            var stock = Shop.Current;
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

                var nameStyle = item.Kind == Shop.ItemKind.Relic
                    ? new GUIStyle(label) { normal = { textColor = Relics.GemColor((Relic)(int)item.Amount) } }
                    : label;
                GUI.Label(new Rect(x + 14, ry, 260, 20), item.Kind == Shop.ItemKind.Relic ? "RELIC: " + item.Name : item.Name, nameStyle);
                GUI.Label(new Rect(x + 14, ry + 17, 300, 18), item.Description, small);

                GUI.enabled = reason == null;
                if (GUI.Button(new Rect(x + pw - 124, ry + 3, 110, 28), reason ?? (item.Price + " gold")))
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

            float pw = 300, rowH = 26, ph = 50 + Stats.Count * rowH;
            float x = w - pw - 24, y = h / 2f - ph / 2f;
            Rect(x, y, pw, ph, Panel);
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
                if (Time.time - hp.LastHurtTime > 5f || hp.GetComponent<BossBrain>() != null) continue;

                var world = hp.transform.position + Vector3.up * (hp.CenterHeight * 2f + 0.35f);
                var screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f || screen.z > 25f) continue;

                float bw = 60f, x = screen.x - bw / 2f, y = Screen.height - screen.y;
                Rect(x - 1, y - 1, bw + 2, 7, new Color(0, 0, 0, 0.75f));
                Rect(x, y, bw * hp.Fraction, 5, new Color(0.85f, 0.2f, 0.15f));
            }
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
            Rect(x, y, width, height, new Color(0.12f, 0.1f, 0.1f, 0.9f));
            Rect(x, y, width * Mathf.Clamp01(k), height, color);
            Rect(x, y, width * Mathf.Clamp01(k), height * 0.35f, new Color(1f, 1f, 1f, 0.12f));
        }

        private static void Rect(float x, float y, float width, float height, Color color)
        {
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
