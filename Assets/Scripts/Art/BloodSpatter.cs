using System.Collections.Generic;
using Deepbound.Combat;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Blood on the flagstones (2026-11-01, the retro gothic pass): a player who takes a real blow (3%+ of their health)
    /// leaves a dark splat or two at their feet, sized by the blow, fading over <see cref="Life"/> seconds, so a hard
    /// fight leaves its trail. Players only (the dead here are bone). Every peer, near the camera only; rate-limited.
    /// </summary>
    public static class BloodSpatter
    {
        public const float Life = 25f, MinFraction = 0.03f, Earshot = 30f;
        private static readonly string[] Splats = { "dirt_01", "dirt_02", "dirt_03" };
        private static readonly Color Blood = new Color(0.46f, 0.04f, 0.03f, 0.92f);
        private static readonly Dictionary<Health, float> lastAt = new Dictionary<Health, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            Health.AnyHurt += OnHurt;
        }

        private static void OnHurt(Health who, float amount)
        {
            if (who == null || who.Team != Team.Players || who.Max.Value <= 0f) return;
            float k = amount / who.Max.Value;
            if (k < MinFraction) return;
            var cam = ViewCamera.Current;
            if (cam == null || (cam.transform.position - who.transform.position).sqrMagnitude > Earshot * Earshot) return;
            if (lastAt.TryGetValue(who, out float t) && Time.time - t < 0.3f) return;
            lastAt[who] = Time.time;
            Splash(who.transform.position, k);
        }

        /// <summary>The splat itself (screenshots stage it in front of the camera).</summary>
        public static void Splash(Vector3 at, float k)
        {
            int drops = k > 0.12f ? 2 : 1;
            for (int i = 0; i < drops; i++)
            {
                var off = Random.insideUnitCircle * (0.25f + 0.35f * i);
                float size = Mathf.Lerp(0.9f, 1.7f, Mathf.InverseLerp(MinFraction, 0.25f, k)) * (i == 0 ? 1f : 0.65f);
                var splat = FxSprite.Decal(Splats[Random.Range(0, Splats.Length)], at + new Vector3(off.x, 0.2f, off.y), size, size * 1.08f, Life, Blood);
                if (splat != null) splat.FadeIn = false;
            }
        }
    }
}
