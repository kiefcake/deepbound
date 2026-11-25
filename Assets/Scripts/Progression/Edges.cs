using Deepbound.Combat;
using Deepbound.Enemies;
using UnityEngine;

namespace Deepbound.Progression
{
    public enum Edge : byte
    {
        None = 0, Momentum = 1, Cleaver = 2, LoneWolf = 3, Opportunist = 4,
        Stalwart = 5, Quickening = 6, Desperation = 7, SecondWind = 8,

        Bastion = 9, Ironhide = 10, Retribution = 11, Lifeblood = 12,
    }

    public static class Edges
    {
        public const int Count = 12, Slots = 3, OfferSize = 3;
        public const float BastionHP = 60f, IronhideDEF = 8f, RetributionShare = 0.3f, RetributionReach = 4f, LifebloodHeal = 0.03f;

        public static bool IsBlade(Edge e) => e == Edge.Momentum || e == Edge.Cleaver || e == Edge.LoneWolf || e == Edge.Opportunist || e == Edge.Desperation;
        public static bool IsGuard(Edge e) => e == Edge.Stalwart || e == Edge.SecondWind || e == Edge.Bastion || e == Edge.Ironhide || e == Edge.Retribution || e == Edge.Lifeblood;
        public static string Kind(Edge e) => IsBlade(e) ? "BLADE" : IsGuard(e) ? "GUARD" : "CRAFT";
        public static readonly int[] Levels = { 5, 7, 9 };

        public const float MomentumDamage = 1.2f, MomentumSeconds = 4f;
        public const float CleaverPerFoe = 0.05f, CleaverMax = 0.2f, CleaverRadius = 6f;
        public const float LoneWolfDamage = 1.18f, LoneWolfRadius = 10f;
        public const float OpportunistDamage = 1.2f;
        public const float StalwartTaken = 0.9f;

        public const float QuickeningCut = 0.1f;

        public const float DesperationMax = 0.25f;
        public const float SecondWindHeal = 1.5f;

        public static string Name(Edge e)
        {
            switch (e)
            {
                case Edge.Momentum: return "Momentum";
                case Edge.Cleaver: return "Cleaver";
                case Edge.LoneWolf: return "Lone Wolf";
                case Edge.Opportunist: return "Opportunist";
                case Edge.Stalwart: return "Stalwart";
                case Edge.Quickening: return "Quickening";
                case Edge.Desperation: return "Desperation";
                case Edge.SecondWind: return "Second Wind";
                case Edge.Bastion: return "Bastion";
                case Edge.Ironhide: return "Ironhide";
                case Edge.Retribution: return "Retribution";
                case Edge.Lifeblood: return "Lifeblood";
                default: return "";
            }
        }

        public static string Describe(Edge e)
        {
            switch (e)
            {
                case Edge.Momentum: return "A kill gives +20% damage for 4 s (each kill renews it).";
                case Edge.Cleaver: return "+5% damage for each other enemy within 6 m of you (up to +20%).";
                case Edge.LoneWolf: return "+18% damage with no other enemy within 10 m.";
                case Edge.Opportunist: return "+20% damage to stunned, chilled, marked or taunted enemies.";
                case Edge.Stalwart: return "Take 10% less damage from everything.";
                case Edge.Quickening: return "Your abilities recharge 10% faster.";
                case Edge.Desperation: return "+1% damage for every 3% of health you are missing (up to +25%).";
                case Edge.SecondWind: return "Health Potions mend 50% more.";
                case Edge.Bastion: return "+" + BastionHP + " max HP for the run.";
                case Edge.Ironhide: return "+" + IronhideDEF + " DEF for the run.";
                case Edge.Retribution: return "The dead that strike you up close take " + Mathf.RoundToInt(RetributionShare * 100f) + "% of the blow back.";
                case Edge.Lifeblood: return "Each kill mends " + Mathf.RoundToInt(LifebloodHeal * 100f) + "% of your max health.";
                default: return "";
            }
        }

        public static Color Color(Edge e)
        {
            switch (e)
            {
                case Edge.Momentum: case Edge.Cleaver: case Edge.LoneWolf: case Edge.Opportunist: case Edge.Desperation: return new Color(1f, 0.55f, 0.3f);
                case Edge.Stalwart: case Edge.SecondWind: case Edge.Bastion: case Edge.Ironhide: case Edge.Retribution: case Edge.Lifeblood: return new Color(0.7f, 0.85f, 1f);
                default: return new Color(1f, 0.85f, 0.4f);
            }
        }

        public static int SlotAt(int level) => System.Array.IndexOf(Levels, level);

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

            System.Func<Edge, bool>[] wants = { IsBlade, IsGuard, _ => true };
            for (int i = 0; i < OfferSize && pool.Count > 0; i++)
            {
                var fits = pool.FindAll(e => wants[i](e));
                if (fits.Count == 0) fits = pool;
                var e = fits[rng.Next(fits.Count)];
                pick[i] = e;
                pool.Remove(e);
            }
            return pick;
        }

        public static float OutgoingMultiplier(Health target, PlayerProgress p)
        {
            if (p.EdgeBits.Value == 0) return 1f;
            float m = 1f;
            if (p.HasEdge(Edge.Momentum) && Time.time < p.ServerMomentumUntil) m *= MomentumDamage;
            if (p.HasEdge(Edge.Opportunist) && (target.Chilled.Value || target.Marked.Value
                || target.TryGetComponent<EnemyBrain>(out var b) && (b.IsStunned || b.Taunter != null))) m *= OpportunistDamage;
            if (p.HasEdge(Edge.Desperation) && p.TryGetComponent<Health>(out var mine))
                m *= 1f + Mathf.Min(DesperationMax, (1f - mine.Fraction) / 3f);
            if (p.HasEdge(Edge.Cleaver) || p.HasEdge(Edge.LoneWolf))
            {
                var me = p.transform.position;
                int near = 0, close = 0;
                foreach (var h in Health.All)
                {
                    if (h == null || h == target || h.Team != Team.Enemies || !h.IsAlive) continue;
                    float d = (h.transform.position - me).sqrMagnitude;
                    if (d < LoneWolfRadius * LoneWolfRadius) close++;
                    if (d < CleaverRadius * CleaverRadius) near++;
                }
                if (p.HasEdge(Edge.Cleaver)) m *= 1f + Mathf.Min(CleaverMax, CleaverPerFoe * near);
                if (p.HasEdge(Edge.LoneWolf) && close == 0) m *= LoneWolfDamage;
            }
            return m;
        }
    }
}
