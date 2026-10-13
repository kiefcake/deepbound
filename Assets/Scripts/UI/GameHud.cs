using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Deepbound.World;
using UnityEngine;

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
        private KnightKit kit;

        private static Texture2D white;
        private GUIStyle label, big, small, center;
        private float hurtFlash;
        private float spawnTime;
        private int bannerFloor = -1;
        private float bannerUntil;

        private static readonly Color Panel = new Color(0.03f, 0.03f, 0.04f, 0.72f);
        private static readonly Color Gold = new Color(0.92f, 0.74f, 0.36f);
        private static readonly Color HealthRed = new Color(0.78f, 0.12f, 0.1f);
        private static readonly Color StaminaAmber = new Color(0.9f, 0.68f, 0.18f);

        private static readonly string[] FloorNames = { "The Ossuary", "The Drowned Vaults", "The Hollow Throne" };

        public void Init(NetworkPlayer owner)
        {
            player = owner;
            health = owner.GetComponent<Health>();
            life = owner.GetComponent<PlayerLife>();
            kit = owner.GetComponent<KnightKit>();
            health.Hurt += amount => hurtFlash = Mathf.Min(1f, hurtFlash + amount / 30f);
            spawnTime = Time.time;
        }

        private void Update()
        {
            hurtFlash = Mathf.MoveTowards(hurtFlash, 0f, Time.deltaTime * 1.5f);

            var director = DungeonDirector.Instance;
            if (director != null && director.Floor.Value.y != bannerFloor && DungeonDirector.Layout != null)
            {
                bannerFloor = director.Floor.Value.y;
                bannerUntil = Time.time + 5f;
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

            float w = Screen.width, h = Screen.height;

            DrawEnemyBars();
            DrawVignette(w, h);
            DrawCrosshair(w, h);
            DrawVitals(w, h);
            DrawAbilities(w, h);
            DrawBoss(w);
            DrawRevive(w, h);
            DrawPhase(w, h);
            DrawBanner(w, h);
            DrawControlsHint(w, h);
        }

        // ------------------------------------------------------------------

        private void DrawCrosshair(float w, float h)
        {
            if (life.Downed.Value) return;
            var c = new Vector2(w / 2f, h / 2f);
            Rect(c.x - 1.5f, c.y - 1.5f, 3, 3, new Color(1f, 1f, 1f, 0.9f));
            var tick = new Color(1f, 1f, 1f, 0.45f);
            Rect(c.x - 11, c.y - 0.5f, 6, 1, tick);
            Rect(c.x + 5, c.y - 0.5f, 6, 1, tick);
            Rect(c.x - 0.5f, c.y + 5, 1, 6, tick);
        }

        private void DrawVitals(float w, float h)
        {
            float x = 24, y = h - 92, bw = 300;
            Rect(x - 8, y - 10, bw + 16, 78, Panel);

            float hp = health.Current.Value, max = Mathf.Max(1f, health.Max.Value);
            Bar(x, y, bw, 22, hp / max, HealthRed);
            GUI.Label(new Rect(x + 8, y + 1, bw, 22), Mathf.CeilToInt(hp) + " / " + Mathf.CeilToInt(max), label);

            Bar(x, y + 32, bw, 12, kit.Stamina / KnightKit.StaminaMax, StaminaAmber);
            GUI.Label(new Rect(x, y + 46, bw, 20), "KNIGHT  ·  Depth " + (CurrentDepth() + 1) + " / " + DungeonDirector.FloorsPerRun, small);
        }

        private void DrawAbilities(float w, float h)
        {
            float size = 58, gap = 10;
            float total = size * 4 + gap * 3;
            float x = w / 2f - total / 2f, y = h - size - 26;

            Ability(x, y, size, "LMB", "Swing", 0f, 0f);
            Ability(x + (size + gap), y, size, "RMB", "Block", 0f, 0f, kit.Blocking.Value);
            Ability(x + (size + gap) * 2, y, size, "Q", "Bash", kit.BashReadyAt - Time.time, KnightKit.BashCooldown);

            float warCryActive = (float)(kit.WarCryEnds.Value - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time);
            Ability(x + (size + gap) * 3, y, size, "E", "War Cry", kit.WarCryReadyAt - Time.time, KnightKit.WarCryCooldown, warCryActive > 0f);
        }

        private void Ability(float x, float y, float size, string key, string name, float remaining, float cooldown, bool active = false)
        {
            Rect(x - 2, y - 2, size + 4, size + 4, active ? Gold : new Color(0.35f, 0.3f, 0.22f, 0.9f));
            Rect(x, y, size, size, Panel);
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
            if (target == null) return;

            GUI.Label(new Rect(0, h / 2f + 40, w, 24), "Hold F to revive " + target.name, center);
            ProgressBar(w, h / 2f + 68, target.ReviveProgress.Value / PlayerLife.ReviveSeconds, new Color(0.6f, 0.9f, 1f));
        }

        private void DrawPhase(float w, float h)
        {
            var director = DungeonDirector.Instance;
            if (director == null || director.Phase.Value == RunPhase.Exploring) return;

            int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(director.PhaseEndsAt.Value - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time)));
            string title, sub;
            switch (director.Phase.Value)
            {
                case RunPhase.FloorCleared: title = "THE KING FALLS"; sub = "Descending deeper in " + seconds + "..."; break;
                case RunPhase.Wiped: title = "YOUR PARTY HAS FALLEN"; sub = "The run is over. A new descent begins in " + seconds + "..."; break;
                default: title = "THE DEPTHS ARE CLEARED"; sub = "Run complete. A new descent begins in " + seconds + "..."; break;
            }

            Rect(0, h * 0.28f - 10, w, 90, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(0, h * 0.28f, w, 44), title, new GUIStyle(big) { normal = { textColor = Gold } });
            GUI.Label(new Rect(0, h * 0.28f + 46, w, 24), sub, center);
        }

        private void DrawBanner(float w, float h)
        {
            if (Time.time > bannerUntil || bannerFloor < 0) return;
            float a = Mathf.Clamp01((bannerUntil - Time.time) / 1.5f);
            var gold = new Color(Gold.r, Gold.g, Gold.b, a);
            GUI.Label(new Rect(0, h * 0.18f, w, 44), "FLOOR " + (bannerFloor + 1), new GUIStyle(big) { normal = { textColor = gold } });
            string name = bannerFloor < FloorNames.Length ? FloorNames[bannerFloor] : "The Deep";
            GUI.Label(new Rect(0, h * 0.18f + 44, w, 26), name, new GUIStyle(center) { fontSize = 18, normal = { textColor = new Color(1f, 1f, 1f, a) } });
        }

        private void DrawControlsHint(float w, float h)
        {
            float age = Time.time - spawnTime;
            if (age > 25f) return;
            float a = Mathf.Clamp01((25f - age) / 3f);
            var style = new GUIStyle(small) { alignment = TextAnchor.UpperRight, normal = { textColor = new Color(1f, 1f, 1f, a * 0.85f) } };
            GUI.Label(new Rect(w - 330, 16, 310, 140),
                "WASD move  ·  Shift sprint  ·  Space jump\n" +
                "LMB swing  ·  RMB block (front)\n" +
                "Q  Shield Bash — stuns, cancels wind-ups\n" +
                "E  War Cry — taunt + half damage\n" +
                "F  revive a downed ally  ·  Esc free mouse", style);
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
