using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.Classes
{
    /// <summary>APPEND ONLY: synced as a byte for the popup.</summary>
    public enum ComboKind : byte { Shatter, Exposed, Distracted, AegisBurst, MarkSpread, Blessed, Hallowed, WardBroken }

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
                case ComboKind.Hallowed: return "HALLOWED FIRE";
                case ComboKind.WardBroken: return "WARD BROKEN";
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
                case ComboKind.Hallowed: return new Color(1f, 0.75f, 0.35f);
                case ComboKind.WardBroken: return new Color(0.73f, 0.66f, 1f);
                default: return new Color(1f, 0.95f, 0.6f);
            }
        }

        /// <summary>One line, shown in the feed the first time a combo fires each run.</summary>
        public static string Explain(ComboKind kind)
        {
            switch (kind)
            {
                case ComboKind.Shatter: return "Shatter: heavy hits on a chilled enemy break the chill for bonus damage.";
                case ComboKind.Exposed: return "Exposed: stunned enemies take +25% damage from everyone.";
                case ComboKind.Distracted: return "Distracted: a taunted enemy takes +15% damage from everyone but its taunter.";
                case ComboKind.AegisBurst: return "Aegis Burst: a broken shield erupts, hurting enemies around its wearer.";
                case ComboKind.MarkSpread: return "Mark Spreads: kill a marked enemy and the mark jumps to the nearest foe.";
                case ComboKind.Hallowed: return "Hallowed Fire: a meteor landing on consecrated ground heals the allies on it.";
                case ComboKind.WardBroken: return "Ward Broken: stun or chill a Warded elite and its ward shatters.";
                default: return "Blessed: allies standing in consecrated ground deal +20% damage.";
            }
        }

        // One popup per target per kind per second, so a flurry of stabs is not a word cloud.
        private static readonly Dictionary<(ulong, ComboKind), float> lastPopup = new Dictionary<(ulong, ComboKind), float>();
        private static readonly HashSet<ComboKind> seenThisRun = new HashSet<ComboKind>();

        /// <summary>Server: a new run — combos explain themselves again.</summary>
        public static void ResetRun() => seenThisRun.Clear();

        /// <summary>
        /// Server: show a combo over a target, credited to <paramref name="by"/> (may be null).
        /// False if throttled (so callers do not double-count).
        /// </summary>
        public static bool Popup(Health at, ComboKind kind, PlayerProgress by = null)
        {
            var key = (at.NetworkObjectId, kind);
            if (lastPopup.TryGetValue(key, out float t) && Time.time - t < 1f) return false;
            lastPopup[key] = Time.time;
            if (lastPopup.Count > 256) lastPopup.Clear();
            if (by != null) by.ServerCreditCombo();
            bool first = seenThisRun.Add(kind);
            World.DungeonDirector.Instance?.ComboFxRpc(at.Center, (byte)kind, first);
            return true;
        }

        /// <summary>
        /// Server: a heavy hit (Knight bash, full-draw arrow, Rogue ambush) on a chilled
        /// enemy consumes the chill. Returns the damage multiplier to apply.
        /// </summary>
        public static float TryShatter(Health target, PlayerProgress by = null)
        {
            if (!target.ServerConsumeChill()) return 1f;
            Popup(target, ComboKind.Shatter, by);
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
                    Popup(target, ComboKind.Exposed, attacker);
                }
                var taunter = brain.Taunter;
                if (taunter != null && taunter.gameObject != attacker.gameObject)
                {
                    m *= DistractedMultiplier;
                    Popup(target, ComboKind.Distracted, attacker);
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
            Popup(shielded, ComboKind.AegisBurst, shielded.GetComponent<PlayerProgress>());
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
            var net = Unity.Netcode.NetworkManager.Singleton;
            PlayerProgress killer = null;
            if (net != null && net.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var o)) o.TryGetComponent(out killer);
            Popup(next, ComboKind.MarkSpread, killer);
        }

        private static List<Health> ToArrayList(this IEnumerable<Health> e) => new List<Health>(e);
    }
}
