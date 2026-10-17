using System.Collections.Generic;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Loot
{
    /// <summary>APPEND ONLY: synced as a byte. 0 = empty socket.</summary>
    public enum Rune : byte { None = 0, Frost = 1, Storm = 2, Hunter = 3, Holy = 4, Ember = 5, Keen = 6, Farsight = 7 }

    /// <summary>
    /// Weapon enchantments. Runes drop as loot; at the anvil you set them into the class
    /// weapon's sockets (one at +1, a second at +3). Each adds an on-hit effect — and most
    /// of those feed a class combo, so a Rogue with a Frost rune sets up Shatter for the
    /// Knight, or a Mage with a Storm rune leaves enemies Exposed for everyone.
    /// </summary>
    public static class Runes
    {
        public const int Count = 7;

        public static int SocketsAt(int weaponLevel) => weaponLevel >= 3 ? 2 : weaponLevel >= 1 ? 1 : 0;

        public static string Name(Rune r)
        {
            switch (r)
            {
                case Rune.Frost: return "Frost Rune";
                case Rune.Storm: return "Storm Rune";
                case Rune.Hunter: return "Hunter's Rune";
                case Rune.Holy: return "Holy Rune";
                case Rune.Ember: return "Ember Rune";
                case Rune.Keen: return "Keen Rune";
                case Rune.Farsight: return "Farsight Rune";
                default: return "Empty socket";
            }
        }

        public static string Describe(Rune r)
        {
            switch (r)
            {
                case Rune.Frost: return "Hits have a 15% chance to chill (sets up Shatter).";
                case Rune.Storm: return "Hits have a 10% chance to stun briefly (Exposed).";
                case Rune.Hunter: return "Every fifth hit on the same enemy marks it.";
                case Rune.Holy: return "Hits heal the most wounded ally nearby for 8% of the damage.";
                case Rune.Ember: return "Kills explode, burning enemies within 2.5 m.";
                case Rune.Keen: return "+15% damage to enemies above 80% health.";
                case Rune.Farsight: return "+25% range on your attacks and abilities (melee reach a little longer).";
                default: return "";
            }
        }

        public static Color Color(Rune r)
        {
            switch (r)
            {
                case Rune.Frost: return new Color(0.55f, 0.85f, 1f);
                case Rune.Storm: return new Color(0.75f, 0.7f, 1f);
                case Rune.Hunter: return new Color(1f, 0.4f, 0.35f);
                case Rune.Holy: return new Color(1f, 0.92f, 0.6f);
                case Rune.Ember: return new Color(1f, 0.5f, 0.15f);
                case Rune.Keen: return new Color(0.75f, 1f, 0.75f);
                case Rune.Farsight: return new Color(0.5f, 0.95f, 0.9f);
                default: return UnityEngine.Color.gray;
            }
        }

        public static Rune Random() => (Rune)UnityEngine.Random.Range(1, Count + 1);

        // ------------------------------------------------------------------
        // Server effects
        // ------------------------------------------------------------------

        private static bool hooked, resolving;
        private static readonly Dictionary<ulong, (ulong target, int hits)> hunterCounts = new Dictionary<ulong, (ulong, int)>();

        public static void EnsureServerHooks()
        {
            if (hooked) return;
            hooked = true;
            Health.ServerAnyDamaged += OnDamaged;
        }

        /// <summary>Keen: applied before damage, through the outgoing multiplier.</summary>
        public static float OutgoingMultiplier(Health target, PlayerProgress p) =>
            p.HasRune(Rune.Keen) && target.Fraction > 0.8f ? 1.15f : 1f;

        private static void OnDamaged(Health target, float dealt, ulong attackerId)
        {
            if (resolving || target.Team != Team.Enemies) return;
            var net = NetworkManager.Singleton;
            if (net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj) || !obj.TryGetComponent<PlayerProgress>(out var p)) return;
            if (p.Socketed(0) == Rune.None && p.Socketed(1) == Rune.None) return;

            resolving = true;
            try
            {
                if (target.IsAlive)
                {
                    if (p.HasRune(Rune.Frost) && UnityEngine.Random.value < 0.15f) target.ServerChill(3f);
                    if (p.HasRune(Rune.Storm) && UnityEngine.Random.value < 0.1f && target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(0.7f);
                    if (p.HasRune(Rune.Hunter))
                    {
                        hunterCounts.TryGetValue(attackerId, out var c);
                        c = c.target == target.NetworkObjectId ? (c.target, c.hits + 1) : (target.NetworkObjectId, 1);
                        if (c.hits >= 5)
                        {
                            c.hits = 0;
                            target.ServerMark(4f);
                        }
                        hunterCounts[attackerId] = c;
                    }
                }
                if (p.HasRune(Rune.Holy))
                {
                    Health worst = null;
                    foreach (var ally in CombatQuery.Circle(p.transform.position, 10f, Team.Players))
                        if (ally.IsAlive && ally.Fraction < 1f && (worst == null || ally.Fraction < worst.Fraction)) worst = ally;
                    if (worst != null) worst.ServerHeal(dealt * 0.08f);
                }
                if (!target.IsAlive && p.HasRune(Rune.Ember))
                {
                    var at = target.transform.position;
                    foreach (var foe in new List<Health>(CombatQuery.Circle(at, 2.5f, Team.Enemies)))
                        foe.ServerDamage(20f * p.AttackMultiplier, at, attackerId);
                    World.DungeonDirector.Instance?.EmberBurstFxRpc(at);
                }
            }
            finally { resolving = false; }
        }
    }
}
