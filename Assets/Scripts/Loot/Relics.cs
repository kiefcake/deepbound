using System.Collections.Generic;
using System.Linq;
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
        ExecutionersCoin = 12,
        WraithChain = 13,
        WarlordsHorn = 14,
        WintersTooth = 15,
        IronPsalter = 16,
        LongShadow = 17,
        PilgrimsPurse = 18,
        RootbreakerCharm = 19,
        TallymansLedger = 20,
        LamplightersOil = 21,
        ChoristersBell = 22,
        // 2026-10-23: the party relics and a crit relic.
        BannerOfTheFallen = 23,
        SharedVow = 24,
        RallyingBell = 25,
        WhettedFang = 26,
    }

    /// <summary>
    /// Relics: the run's "fun items". Where potions are steady numeric growth, each relic
    /// changes how you play — a reason to take risks (Blood Pact), to stick together
    /// (Briar Crown on the tank), or to build around a combo (Frost Shard on anyone
    /// partied with a Mage). One of each per player; a duplicate stays on the floor for a
    /// teammate. Lost when the run ends, like everything else.
    /// </summary>
    public static class Relics
    {
        public static readonly int Count = System.Enum.GetValues(typeof(Relic)).Length;

        /// <summary>
        /// The relic tray (2026-10-23): at most <see cref="MaxCarried"/>, each up to tier III. A relic you
        /// already carry, or any relic once the tray is full, empowers one instead (+35% to its numbers per
        /// tier). A full run used to end with 15+ relics and every build the same.
        /// </summary>
        public const int MaxCarried = 8, MaxTier = 2;
        public static float Power(int tier) => 1f + TierStep * Mathf.Clamp(tier, 0, MaxTier);
        /// <summary>+35% a tier (it was 50%: the autoplay run's floors 8-9 went by with two close calls).</summary>
        public const float TierStep = 0.35f;
        public static int TierPercent(int tier) => Mathf.RoundToInt(TierStep * 100f * Mathf.Clamp(tier, 0, MaxTier));
        public static string TierName(int tier) => tier >= 2 ? " III" : tier == 1 ? " II" : "";

        /// <summary>Relics with nothing to scale cannot be empowered.</summary>
        public static bool Scales(Relic r) => r != Relic.LamplightersOil;

        /// <summary>Only worth having with someone to share it: never rolled in a solo run.</summary>
        public static bool PartyOnly(Relic r) => r == Relic.SharedVow || r == Relic.RallyingBell;

        public static string Name(Relic r)
        {
            switch (r)
            {
                case Relic.VampiricFang: return "Vampiric Fang";
                case Relic.ThunderSigil: return "Thunder Idol"; // renamed 2026-10-21: "Sigil" is the ability currency
                case Relic.BerserkersBrand: return "Berserker's Brand";
                case Relic.BoneWard: return "Bone Ward";
                case Relic.PhoenixFeather: return "Phoenix Feather";
                case Relic.GildedSkull: return "Gilded Skull";
                case Relic.FrostCharm: return "Frost Shard";
                case Relic.EchoStone: return "Echo Stone";
                case Relic.GhostLantern: return "Ghost Lantern";
                case Relic.ThornedMail: return "Briar Crown";
                case Relic.HeartOfTheDeep: return "Heart of the Deep";
                case Relic.BloodPact: return "Blood Pact";
                case Relic.ExecutionersCoin: return "Executioner's Coin";
                case Relic.WraithChain: return "Wraith Chain";
                case Relic.WarlordsHorn: return "Warlord's Horn";
                case Relic.WintersTooth: return "Winter's Tooth";
                case Relic.IronPsalter: return "Iron Psalter";
                case Relic.LongShadow: return "Long Shadow";
                case Relic.PilgrimsPurse: return "Pilgrim's Purse";
                case Relic.RootbreakerCharm: return "Rootbreaker Knot";
                case Relic.TallymansLedger: return "Tallyman's Ledger";
                case Relic.LamplightersOil: return "Lamplighter's Oil";
                case Relic.ChoristersBell: return "Chorister's Bell";
                case Relic.BannerOfTheFallen: return "Banner of the Fallen";
                case Relic.SharedVow: return "Shared Vow";
                case Relic.RallyingBell: return "Rallying Bell";
                case Relic.WhettedFang: return "Whetted Fang";
                default: return r.ToString();
            }
        }

        public static string Describe(Relic r)
        {
            switch (r)
            {
                case Relic.VampiricFang: return "Heal for 6% of all damage you deal.";
                case Relic.ThunderSigil: return "Every 6th hit (one sooner per tier) calls lightning that chains to 2 more enemies.";
                case Relic.BerserkersBrand: return "+40% damage while below half health.";
                case Relic.BoneWard: return "Blocks one hit completely every 12 seconds (sooner per tier).";
                case Relic.PhoenixFeather: return "Once per floor, rise again the moment you go down.";
                case Relic.GildedSkull: return "+25% gold from everything you pick up (+10% more per tier). Only the Bone Hoarder carries one.";
                case Relic.FrostCharm: return "+5% critical chance, and your critical hits chill.";
                case Relic.EchoStone: return "Ability cooldowns 20% shorter, and your ultimate charges 20% faster.";
                case Relic.GhostLantern: return "+15% move speed, and dodge recovers twice as fast.";
                case Relic.ThornedMail: return "Melee attackers take 40% of their damage back.";
                case Relic.HeartOfTheDeep: return "+25% maximum health.";
                case Relic.BloodPact: return "+30% damage, but -25% maximum health.";
                case Relic.ExecutionersCoin: return "+50% damage to enemies below 30% health.";
                case Relic.WraithChain: return "Your kills burst in a ghostly nova that hurts enemies within 4 m.";
                case Relic.WarlordsHorn: return "Your ultimate charges 50% faster, and casting it heals allies within 12 m by 25%.";
                case Relic.WintersTooth: return "+25% damage to chilled enemies.";
                case Relic.IronPsalter: return "You cannot be maddened, curses last half as long, and blows from 5 m or more hurt 15% less.";
                case Relic.LongShadow: return "Shots fly 15% further and keep their full bite to the end; melee reaches a little further too.";
                case Relic.PilgrimsPurse: return "Camp prices 15% lower, and every 10 gold you spend there comes back as an ember.";
                case Relic.RootbreakerCharm: return "+50% damage while rooted or held; frost roots let go twice as fast and an Entling loses its grip after 3 s.";
                case Relic.TallymansLedger: return "+20% gold (+10% per tier), and the far bell and the Tallyman come three minutes later for your party.";
                case Relic.LamplightersOil: return "You can fight while carrying anything: the hand torch, a reliquary or a soul skull.";
                case Relic.ChoristersBell: return "Each room cleared mends you 12%.";
                case Relic.BannerOfTheFallen: return "You and allies within 8 m deal 8% more damage.";
                case Relic.SharedVow: return "Your Health Potions also mend the nearest ally within 12 m for half as much.";
                case Relic.RallyingBell: return "Whenever someone near you gets back up, everyone within 15 m gains +20% speed and +15% damage for 5 s.";
                case Relic.WhettedFang: return "+10% critical chance, and your critical hits deal x2 instead of x1.75.";
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
                case Relic.ExecutionersCoin: return new Color(0.75f, 0.7f, 0.6f);
                case Relic.WraithChain: return new Color(0.55f, 0.75f, 1f);
                case Relic.WarlordsHorn: return new Color(0.85f, 0.45f, 0.2f);
                case Relic.WintersTooth: return new Color(0.75f, 0.95f, 1f);
                case Relic.IronPsalter: return new Color(0.7f, 0.7f, 0.75f);
                case Relic.LongShadow: return new Color(0.35f, 0.3f, 0.5f);
                case Relic.PilgrimsPurse: return new Color(0.75f, 0.55f, 0.3f);
                case Relic.RootbreakerCharm: return new Color(0.5f, 0.75f, 0.3f);
                case Relic.TallymansLedger: return new Color(0.62f, 1f, 0.7f);
                case Relic.LamplightersOil: return new Color(1f, 0.7f, 0.35f);
                case Relic.ChoristersBell: return new Color(1f, 0.85f, 0.45f);
                case Relic.BannerOfTheFallen: return new Color(0.85f, 0.25f, 0.2f);
                case Relic.SharedVow: return new Color(0.7f, 0.9f, 1f);
                case Relic.RallyingBell: return new Color(0.85f, 0.65f, 0.35f);
                case Relic.WhettedFang: return new Color(0.88f, 0.9f, 0.95f);
                default: return Color.white;
            }
        }

        /// <summary>Never in a random roll: the Gilded Skull comes only from the Bone Hoarder (2026-11-13, the user: "put else where").</summary>
        public static bool NotRandom(Relic r) => r == Relic.GildedSkull;
        public const float HoarderSkullChance = 0.35f;

        /// <summary>A random relic; in a solo run never a party one.</summary>
        public static Relic Random()
        {
            bool solo = Player.PlayerLife.All.Count <= 1;
            for (int i = 0; i < 20; i++)
            {
                var r = (Relic)UnityEngine.Random.Range(0, Count);
                if ((!solo || !PartyOnly(r)) && !NotRandom(r)) return r;
            }
            return Relic.HeartOfTheDeep;
        }

        /// <summary>The same, from a seeded generator (the shrine's stock).</summary>
        public static Relic FromSeed(System.Random rng)
        {
            bool solo = Player.PlayerLife.All.Count <= 1;
            for (int i = 0; i < 20; i++)
            {
                var r = (Relic)rng.Next(Count);
                if ((!solo || !PartyOnly(r)) && !NotRandom(r)) return r;
            }
            return Relic.HeartOfTheDeep;
        }

        // ------------------------------------------------------------------
        // Synergies: two relics that belong together awaken something more
        // ------------------------------------------------------------------

        public enum Synergy { DeepWinter, Bloodbound, StormCaller, Reaper, Undying, WarBand, ColdSteel }

        public static readonly (Synergy kind, Relic a, Relic b, string name, string text)[] Synergies =
        {
            (Synergy.DeepWinter, Relic.FrostCharm, Relic.WintersTooth, "Deep Winter", "chilled enemies take a further 20% from you"),
            (Synergy.Bloodbound, Relic.VampiricFang, Relic.BloodPact, "Bloodbound", "your lifesteal rises to 12%"),
            (Synergy.StormCaller, Relic.ThunderSigil, Relic.EchoStone, "Storm Caller", "lightning strikes every 3rd hit"),
            (Synergy.Reaper, Relic.ExecutionersCoin, Relic.BerserkersBrand, "Reaper", "the execution threshold rises to 45%"),
            (Synergy.Undying, Relic.PhoenixFeather, Relic.HeartOfTheDeep, "Undying", "the Phoenix raises you at full health"),
            (Synergy.WarBand, Relic.BannerOfTheFallen, Relic.RallyingBell, "War Band", "your banner reaches 12 m and lifts everyone 12%"),
            (Synergy.ColdSteel, Relic.FrostCharm, Relic.WhettedFang, "Cold Steel", "a crit on a chilled enemy shatters the chill for x1.4 more"),
        };

        public static bool Has(PlayerProgress p, Synergy s)
        {
            foreach (var x in Synergies) if (x.kind == s) return p.HasRelic(x.a) && p.HasRelic(x.b);
            return false;
        }

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

        /// <summary>Hit counts start again each run.</summary>
        public static void ResetRun() => HitCounters.Clear();

        private static void OnServerAnyDamaged(Health target, float dealt, ulong attackerId)
        {
            if (target.Team == Team.Players && target.TryGetComponent<PlayerProgress>(out var victim)) victim.ServerCreditTaken(dealt);
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

            if (p.HasRelic(Relic.VampiricFang)) obj.GetComponent<Health>().ServerHeal(dealt * (Has(p, Synergy.Bloodbound) ? 0.12f : 0.06f) * p.RelicPower(Relic.VampiricFang));

            if (!target.IsAlive && p.HasRelic(Relic.WraithChain))
            {
                resolvingProc = true;
                try
                {
                    var at = target.transform.position;
                    foreach (var foe in CombatQuery.Circle(at, 4f, Team.Enemies).ToArray())
                        foe.Tag("Relics").ServerDamage(22f * p.RelicPower(Relic.WraithChain) * p.AttackMultiplier, at, attackerId);
                    p.WraithNovaFxRpc(at);
                }
                finally { resolvingProc = false; }
            }

            if (p.HasRelic(Relic.ThunderSigil))
            {
                HitCounters.TryGetValue(attackerId, out int hits);
                hits++;
                if (hits >= Mathf.Max(3, (Has(p, Synergy.StormCaller) ? 3 : 6) - p.RelicTier(Relic.ThunderSigil))) // never more often than every 3rd hit (Storm Caller on the Cleric's beam ticks was the strongest pair seen)
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
                struck[i].Tag("Relics").ServerDamage(i == 0 ? dmg : dmg * 0.6f, struck[i].Center + Vector3.up, attackerId);
            }
            caster.LightningFxRpc(points);
        }
    }
}
