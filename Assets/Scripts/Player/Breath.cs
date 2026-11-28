using Deepbound.UI;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.Player
{
    public class Breath : MonoBehaviour
    {
        public const float HoldSeconds = 14f, RefillSeconds = 2.5f, EyeHeight = 1.55f, DrownEvery = 1f;

        private NetworkPlayer player;
        private PlayerLife life;
        private float nextDrown;
        private Color empty = new Color(0.85f, 0.25f, 0.2f);
        private Color full = new Color(0.55f, 0.85f, 1f);

        public float Left { get; private set; } = 1f;
        public bool Under { get; private set; }

        public static Breath Local { get; private set; }

        public void Init(NetworkPlayer p)
        {
            player = p;
            life = p.GetComponent<PlayerLife>();
            if (p.IsOwner) Local = this;
        }

        public static bool HeadUnder(Vector3 feet, float eyeHeight)
        {
            var surface = DungeonDirector.WaterSurface(feet);
            if (!surface.HasValue) return false;
            if (DungeonDirector.Layout != null && DungeonDirector.Layout.InCellar(feet)) return false;
            return feet.y + eyeHeight < surface.Value - 0.05f;
        }

        private void Update()
        {
            if (player == null || !player.IsOwner || life == null) return;
            bool down = life.Downed.Value;
            Under = !down && HeadUnder(transform.position, life.Crouched.Value ? EyeHeight * 0.6f : EyeHeight);
            if (Under) Tips.OfferNow("breath", "UNDER WATER: watch your breath under the crosshair. When it runs out you drown, fast. Surface to breathe.");
            Left = Under ? Mathf.Max(0f, Left - Time.deltaTime / HoldSeconds) : Mathf.Min(1f, Left + Time.deltaTime / RefillSeconds);
            if (!Under || Left > 0f || Time.time < nextDrown) return;
            nextDrown = Time.time + DrownEvery;
            life.DrownRpc();
        }

        private void OnGUI()
        {
            useGUILayout = false;
            if (player == null || !player.IsOwner || (Left >= 1f && !Under) || GameHud.MenuOpen) return;
            UiSkin.Apply();
            float w = UiScale.W, h = UiScale.H;
            const float bw = 220f, bh = 9f;
            float x = w / 2f - bw / 2f, y = h / 2f + 64f;
            var tint = Color.Lerp(empty, full, Mathf.Clamp01(Left * 1.6f));
            float pulse = Left <= 0.25f ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 1f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, bw + 4f, bh + 4f), Texture2D.whiteTexture);
            GUI.color = new Color(tint.r, tint.g, tint.b, pulse);
            GUI.DrawTexture(new Rect(x, y, bw * Left, bh), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var label = UiSkin.Derive(UiSkin.Small, new Color(tint.r, tint.g, tint.b, 0.9f * pulse), fontSize: 12, align: TextAnchor.MiddleCenter, wrap: false);
            GUI.Label(new Rect(x, y + bh + 2f, bw, 18f), Left <= 0f ? "DROWNING" : "BREATH", label);
        }
    }
}
