using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Loot
{
    /// <summary>APPEND ONLY: synced as a byte in loot drops and player relic lists.</summary>
    public enum Relic : byte
    {
        VampiricFang = 0,
        ThunderSigil = 1,
        BerserkersBrand = 2,
        BoneWard = 3,
        PhoenixFeather = 4,
        GildedSkull = 5,
        FrostCharm = 6,
        EchoStone = 7,
        GhostLantern = 8,
        ThornedMail = 9,
        HeartOfTheDeep = 10,
        BloodPact = 11,
    }

    /// <summary>
    /// Relics: the run's "fun items". Where potions are steady numeric growth, each relic
    /// changes how you play — a reason to take risks (Blood Pact), to stick together
    /// (Thorned Mail on the tank), or to build around a combo (Frost Charm on anyone
    /// partied with a Mage). One of each per player; a duplicate stays on the floor for a
    /// teammate. Lost when the run ends, like everything else.
    /// </summary>
    public static class Relics
    {
        public static readonly int Count = System.Enum.GetValues(typeof(Relic)).Length;

        public static string Name(Relic r)
        {
            switch (r)
            {
                case Relic.VampiricFang: return "Vampiric Fang";
                case Relic.ThunderSigil: return "Thunder Sigil";
                case Relic.BerserkersBrand: return "Berserker's Brand";
                case Relic.BoneWard: return "Bone Ward";
                case Relic.PhoenixFeather: return "Phoenix Feather";
                case Relic.GildedSkull: return "Gilded Skull";
                case Relic.FrostCharm: return "Frost Charm";
                case Relic.EchoStone: return "Echo Stone";
                case Relic.GhostLantern: return "Ghost Lantern";
                case Relic.ThornedMail: return "Thorned Mail";
                case Relic.HeartOfTheDeep: return "Heart of the Deep";
                case Relic.BloodPact: return "Blood Pact";
                default: return r.ToString();
            }
        }

        public static string Describe(Relic r)
        {
            switch (r)
            {
                case Relic.VampiricFang: return "Heal for 6% of all damage you deal.";
                case Relic.ThunderSigil: return "Every 6th hit calls lightning that chains to 2 more enemies.";
                case Relic.BerserkersBrand: return "+40% damage while below half health.";
                case Relic.BoneWard: return "Blocks one hit completely every 12 seconds.";
                case Relic.PhoenixFeather: return "Once per floor, rise again the moment you go down.";
                case Relic.GildedSkull: return "+60% gold from everything you pick up.";
                case Relic.FrostCharm: return "Your hits have a 20% chance to chill.";
                case Relic.EchoStone: return "Ability cooldowns are 30% shorter.";
                case Relic.GhostLantern: return "+15% move speed, and dodge recovers twice as fast.";
                case Relic.ThornedMail: return "Melee attackers take 40% of their damage back.";
                case Relic.HeartOfTheDeep: return "+70 maximum health.";
                case Relic.BloodPact: return "+30% damage, but -25% maximum health.";
                default: return "";
            }
        }

        public static Color GemColor(Relic r)
        {
            switch (r)
            {
                case Relic.VampiricFang: return new Color(0.85f, 0.1f, 0.15f);
                case Relic.ThunderSigil: return new Color(0.6f, 0.75f, 1f);
                case Relic.BerserkersBrand: return new Color(1f, 0.35f, 0.1f);
                case Relic.BoneWard: return new Color(0.92f, 0.88f, 0.75f);
                case Relic.PhoenixFeather: return new Color(1f, 0.6f, 0.15f);
                case Relic.GildedSkull: return new Color(1f, 0.82f, 0.3f);
                case Relic.FrostCharm: return new Color(0.5f, 0.9f, 1f);
                case Relic.EchoStone: return new Color(0.65f, 0.4f, 1f);
                case Relic.GhostLantern: return new Color(0.5f, 1f, 0.8f);
                case Relic.ThornedMail: return new Color(0.4f, 0.65f, 0.3f);
                case Relic.HeartOfTheDeep: return new Color(0.2f, 0.5f, 0.9f);
                case Relic.BloodPact: return new Color(0.5f, 0f, 0.1f);
                default: return Color.white;
            }
        }

        public static Relic Random() => (Relic)UnityEngine.Random.Range(0, Count);

        // ------------------------------------------------------------------
        // Server-side effects that hook into damage dealt
        // ------------------------------------------------------------------

        private static bool hooked;
        private static readonly Dictionary<ulong, int> HitCounters = new Dictionary<ulong, int>();

        /// <summary>Server: subscribe once to every damage event in the game.</summary>
        public static void EnsureServerHooks()
        {
            if (hooked) return;
            hooked = true;
            Health.ServerAnyDamaged += OnServerAnyDamaged;
        }

        private static bool resolvingProc;

        private static void OnServerAnyDamaged(Health target, float dealt, ulong attackerId)
        {
            if (target.Team != Team.Enemies) return;
            // Relic procs deal damage too: they count for stats, but must not trigger more procs.
            if (resolvingProc)
            {
                var n = NetworkManager.Singleton;
                if (n != null && n.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var o) && o.TryGetComponent<PlayerProgress>(out var pp))
                    pp.ServerCreditDamage(dealt, !target.IsAlive);
                return;
            }
            var net = NetworkManager.Singleton;
            if (net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj)) return;
            if (!obj.TryGetComponent<PlayerProgress>(out var p)) return;
            p.ServerCreditDamage(dealt, !target.IsAlive);

            if (p.HasRelic(Relic.VampiricFang)) obj.GetComponent<Health>().ServerHeal(dealt * 0.06f);

            if (p.HasRelic(Relic.FrostCharm) && UnityEngine.Random.value < 0.2f) target.ServerChill(3f);

            if (p.HasRelic(Relic.ThunderSigil))
            {
                HitCounters.TryGetValue(attackerId, out int hits);
                hits++;
                if (hits >= 6)
                {
                    hits = 0;
                    resolvingProc = true;
                    try { Lightning(target, p, attackerId); }
                    finally { resolvingProc = false; }
                }
                HitCounters[attackerId] = hits;
            }
        }

        private static void Lightning(Health first, PlayerProgress caster, ulong attackerId)
        {
            float dmg = 30f * caster.AttackMultiplier;
            var struck = new List<Health> { first };
            var from = first;
            for (int i = 0; i < 2; i++)
            {
                Health next = null;
                float best = 7f;
                foreach (var h in Health.All)
                {
                    if (h == null || h.Team != Team.Enemies || !h.IsAlive || struck.Contains(h)) continue;
                    float d = Vector3.Distance(h.Center, from.Center);
                    if (d < best) { best = d; next = h; }
                }
                if (next == null) break;
                struck.Add(next);
                from = next;
            }

            var points = new Vector3[struck.Count];
            for (int i = 0; i < struck.Count; i++)
            {
                points[i] = struck[i].Center;
                struck[i].ServerDamage(i == 0 ? dmg : dmg * 0.6f, struck[i].Center + Vector3.up, attackerId);
            }
            caster.LightningFxRpc(points);
        }
    }
}
