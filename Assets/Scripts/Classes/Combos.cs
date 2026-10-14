using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.Classes
{
    /// <summary>APPEND ONLY: synced as a byte for the popup.</summary>
    public enum ComboKind : byte { Shatter, Exposed, Distracted, AegisBurst, MarkSpread, Blessed }

    /// <summary>
    /// How the classes play off each other. Each class leaves a state on enemies or allies
    /// that someone else cashes in:
    /// <list type="bullet">
    /// <item><b>Chill</b> (Mage): any class's heavy hit Shatters it for x1.5 (the Mage's own fire x1.75).</item>
    /// <item><b>Stun</b> (Knight bash, Rogue opener, Ranger trap): the enemy is Exposed, +25% damage from everyone.</item>
    /// <item><b>Taunt</b> (Knight War Cry): the enemy is Distracted, +15% damage from everyone but the Knight.</item>
    /// <item><b>Mark</b> (Ranger): +25% from everyone, and killing a marked enemy spreads the mark.</item>
    /// <item><b>Consecration</b> (Cleric): allies standing in it are Blessed, +20% damage.</item>
    /// <item><b>Aegis</b> (Cleric): when a shield is broken it bursts, hurting enemies nearby.</item>
    /// </list>
    /// Server-side only; popups go out through <see cref="World.DungeonDirector.ComboFxRpc"/>.
    /// </summary>
    public static class Combos
    {
        public const float ShatterMultiplier = 1.5f, ExposedMultiplier = 1.25f, DistractedMultiplier = 1.15f, BlessedMultiplier = 1.2f;
        public const float AegisBurstDamage = 35f, AegisBurstRadius = 3.5f;

        public static string Label(ComboKind kind)
        {
            switch (kind)
            {
                case ComboKind.Shatter: return "SHATTER";
                case ComboKind.Exposed: return "EXPOSED";
                case ComboKind.Distracted: return "DISTRACTED";
                case ComboKind.AegisBurst: return "AEGIS BURST";
                case ComboKind.MarkSpread: return "MARK SPREADS";
                default: return "BLESSED";
            }
        }

        public static Color Color(ComboKind kind)
        {
            switch (kind)
            {
                case ComboKind.Shatter: return new Color(0.55f, 0.85f, 1f);
                case ComboKind.Exposed: return new Color(1f, 0.85f, 0.3f);
                case ComboKind.Distracted: return new Color(1f, 0.55f, 0.35f);
                case ComboKind.AegisBurst: return new Color(1f, 0.9f, 0.55f);
                case ComboKind.MarkSpread: return new Color(1f, 0.35f, 0.3f);
                default: return new Color(1f, 0.95f, 0.6f);
            }
        }

        // One popup per target per kind per second, so a flurry of stabs is not a word cloud.
        private static readonly Dictionary<(ulong, ComboKind), float> lastPopup = new Dictionary<(ulong, ComboKind), float>();

        public static void Popup(Health at, ComboKind kind)
        {
            var key = (at.NetworkObjectId, kind);
            if (lastPopup.TryGetValue(key, out float t) && Time.time - t < 1f) return;
            lastPopup[key] = Time.time;
            if (lastPopup.Count > 256) lastPopup.Clear();
            World.DungeonDirector.Instance?.ComboFxRpc(at.Center, (byte)kind);
        }

        /// <summary>
        /// Server: a heavy hit (Knight bash, full-draw arrow, Rogue ambush) on a chilled
        /// enemy consumes the chill. Returns the damage multiplier to apply.
        /// </summary>
        public static float TryShatter(Health target)
        {
            if (!target.ServerConsumeChill()) return 1f;
            Popup(target, ComboKind.Shatter);
            return ShatterMultiplier;
        }

        /// <summary>Server: every player hit on an enemy passes through here.</summary>
        public static float ServerMultiplier(Health target, PlayerProgress attacker)
        {
            float m = 1f;
            if (target.TryGetComponent<EnemyBrain>(out var brain))
            {
                if (brain.IsStunned)
                {
                    m *= ExposedMultiplier;
                    Popup(target, ComboKind.Exposed);
                }
                var taunter = brain.Taunter;
                if (taunter != null && taunter.gameObject != attacker.gameObject)
                {
                    m *= DistractedMultiplier;
                    Popup(target, ComboKind.Distracted);
                }
            }
            if (Player.ClericKit.InConsecration(attacker.transform.position)) m *= BlessedMultiplier;
            return m;
        }

        private static bool hooked;

        public static void EnsureServerHooks()
        {
            if (hooked) return;
            hooked = true;
            Health.ServerShieldBroken += OnShieldBroken;
            Health.ServerAnyDamaged += OnAnyDamaged;
        }

        private static void OnShieldBroken(Health shielded)
        {
            if (shielded.Team != Team.Players) return;
            foreach (var foe in CombatQuery.Circle(shielded.transform.position, AegisBurstRadius, Team.Enemies).ToArrayList())
                foe.ServerDamage(AegisBurstDamage, shielded.transform.position, shielded.NetworkObjectId);
            Popup(shielded, ComboKind.AegisBurst);
            World.DungeonDirector.Instance?.AegisBurstFxRpc(shielded.transform.position);
        }

        private static void OnAnyDamaged(Health target, float dealt, ulong attackerId)
        {
            // A marked enemy's death passes the mark to the nearest enemy close by.
            if (target.Team != Team.Enemies || target.IsAlive || !target.Marked.Value) return;
            Health next = null;
            float best = 7f;
            foreach (var h in Health.All)
            {
                if (h == null || h == target || h.Team != Team.Enemies || !h.IsAlive) continue;
                float d = Vector3.Distance(h.transform.position, target.transform.position);
                if (d < best) { best = d; next = h; }
            }
            if (next == null) return;
            next.ServerMark(5f);
            Popup(next, ComboKind.MarkSpread);
        }

        private static List<Health> ToArrayList(this IEnumerable<Health> e) => new List<Health>(e);
    }
}
