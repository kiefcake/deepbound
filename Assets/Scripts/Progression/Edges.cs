using Deepbound.Combat;
using Deepbound.Enemies;
using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>APPEND ONLY: synced packed in <see cref="PlayerProgress.EdgeBits"/>. 0 = none.</summary>
    public enum Edge : byte
    {
        None = 0, Momentum = 1, Cleaver = 2, Duelist = 3, Opportunist = 4,
        Stalwart = 5, Quickening = 6, EmberTongue = 7, SecondWind = 8,
    }

    /// <summary>
    /// Edges (2026-10-23, "improve the progression"): at +5, +7 and +9 the anvil asks you to forge
    /// one of three edges into the weapon. Tempering used to be the obvious buy at every level, with
    /// the only choice at +2; now the weapon of each run grows a shape of its own. Edges are about HOW
    /// you fight (crowds or duels, set-ups, pace), so they sit beside relics (spikes) and runes (procs)
    /// rather than repeat them. The three on offer are seeded per player and run, so a client draws
    /// the same cards the server checks against.
    /// </summary>
    public static class Edges
    {
        public const int Count = 8, Slots = 3, OfferSize = 3;
        public static readonly int[] Levels = { 5, 7, 9 };

        public const float MomentumDamage = 1.2f, MomentumSeconds = 4f;
        public const float CleaverPerFoe = 0.05f, CleaverMax = 0.2f, CleaverRadius = 6f;
        public const float DuelistDamage = 1.18f, DuelistRadius = 10f;
        public const float OpportunistDamage = 1.2f;
        public const float StalwartTaken = 0.88f;
        public const float QuickeningCooldown = 0.88f;
        public const float EmberTongueEmbers = 1.35f;
        public const float SecondWindHeal = 1.5f;

        public static string Name(Edge e)
        {
            switch (e)
            {
                case Edge.Momentum: return "Momentum";
                case Edge.Cleaver: return "Cleaver";
                case Edge.Duelist: return "Duelist";
                case Edge.Opportunist: return "Opportunist";
                case Edge.Stalwart: return "Stalwart";
                case Edge.Quickening: return "Quickening";
                case Edge.EmberTongue: return "Ember Tongue";
                case Edge.SecondWind: return "Second Wind";
                default: return "";
            }
        }

        public static string Describe(Edge e)
        {
            switch (e)
            {
                case Edge.Momentum: return "A kill gives +20% damage for 4 s (each kill renews it).";
                case Edge.Cleaver: return "+5% damage for each other enemy within 6 m of you (up to +20%).";
                case Edge.Duelist: return "+18% damage with no other enemy within 10 m.";
                case Edge.Opportunist: return "+20% damage to stunned, chilled, marked or taunted enemies.";
                case Edge.Stalwart: return "Take 12% less damage from everything.";
                case Edge.Quickening: return "Your abilities recharge 12% faster.";
                case Edge.EmberTongue: return "Embers you pick up are worth 35% more.";
                case Edge.SecondWind: return "Health Potions mend 50% more.";
                default: return "";
            }
        }

        public static Color Color(Edge e)
        {
            switch (e)
            {
                case Edge.Momentum: case Edge.Cleaver: case Edge.Duelist: case Edge.Opportunist: return new Color(1f, 0.55f, 0.3f);
                case Edge.Stalwart: case Edge.SecondWind: return new Color(0.7f, 0.85f, 1f);
                default: return new Color(1f, 0.85f, 0.4f);
            }
        }

        /// <summary>The slot that opens at weapon level <paramref name="level"/>, or -1.</summary>
        public static int SlotAt(int level) => System.Array.IndexOf(Levels, level);

        /// <summary>The three edges offered for <paramref name="slot"/>: seeded by run and player, never one already forged.</summary>
        public static Edge[] Offer(int runSeed, ulong clientId, int slot, int takenBits)
        {
            var pool = new System.Collections.Generic.List<Edge>();
            for (int i = 1; i <= Count; i++)
            {
                bool taken = false;
                for (int s = 0; s < Slots; s++) if (((takenBits >> (8 * s)) & 0xFF) == i && s != slot) taken = true;
                if (!taken) pool.Add((Edge)i);
            }
            var rng = new System.Random(runSeed * 31 + (int)clientId * 977 + slot * 7919);
            var pick = new Edge[OfferSize];
            for (int i = 0; i < OfferSize && pool.Count > 0; i++)
            {
                int k = rng.Next(pool.Count);
                pick[i] = pool[k];
                pool.RemoveAt(k);
            }
            return pick;
        }

        /// <summary>Server: what the attacker's edges add to one hit on <paramref name="target"/>.</summary>
        public static float OutgoingMultiplier(Health target, PlayerProgress p)
        {
            if (p.EdgeBits.Value == 0) return 1f;
            float m = 1f;
            if (p.HasEdge(Edge.Momentum) && Time.time < p.ServerMomentumUntil) m *= MomentumDamage;
            if (p.HasEdge(Edge.Opportunist) && (target.Chilled.Value || target.Marked.Value
                || target.TryGetComponent<EnemyBrain>(out var b) && (b.IsStunned || b.Taunter != null))) m *= OpportunistDamage;
            if (p.HasEdge(Edge.Cleaver) || p.HasEdge(Edge.Duelist))
            {
                var me = p.transform.position;
                int near = 0, close = 0;
                foreach (var h in Health.All)
                {
                    if (h == null || h == target || h.Team != Team.Enemies || !h.IsAlive) continue;
                    float d = (h.transform.position - me).sqrMagnitude;
                    if (d < DuelistRadius * DuelistRadius) close++;
                    if (d < CleaverRadius * CleaverRadius) near++;
                }
                if (p.HasEdge(Edge.Cleaver)) m *= 1f + Mathf.Min(CleaverMax, CleaverPerFoe * near);
                if (p.HasEdge(Edge.Duelist) && close == 0) m *= DuelistDamage;
            }
            return m;
        }
    }
}
