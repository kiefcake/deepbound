using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Loot
{
    /// <summary>
    /// Server-side effects of gear that touch damage dealt: bonus damage (Hunter's Tooth,
    /// the Mantle's post-dodge strike) and on-kill effects (Bloodstone, Ember Locket).
    /// Defensive effects live in <see cref="PlayerProgress"/>, beside the relic ones.
    /// </summary>
    public static class GearEffects
    {
        private static bool hooked;

        public static void EnsureServerHooks()
        {
            if (hooked) return;
            hooked = true;
            Health.ServerOutgoingMultiplier = OutgoingMultiplier;
            Health.ServerAnyDamaged += OnServerAnyDamaged;
        }

        private static PlayerProgress Attacker(ulong attackerId)
        {
            var net = NetworkManager.Singleton;
            if (net == null || attackerId == ulong.MaxValue || !net.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj)) return null;
            return obj.TryGetComponent<PlayerProgress>(out var p) ? p : null;
        }

        private static float OutgoingMultiplier(Health target, ulong attackerId)
        {
            if (target.Team != Team.Enemies) return 1f;
            var p = Attacker(attackerId);
            if (p == null) return 1f;
            float m = Classes.Combos.ServerMultiplier(target, p) * Runes.OutgoingMultiplier(target, p) * Edges.OutgoingMultiplier(target, p);
            if (p.Wears(GearId.HuntersTooth) && target.TryGetComponent<EnemyBrain>(out var brain) && (brain.Elite.Value || brain is BossBrain || brain.IsChampion))
            {
                m *= 1.15f;
                // The tooth knows its quarry: the first hit on each one Marks it for everyone.
                if (p.ToothMarked.Add(target.NetworkObjectId)) target.ServerMark(4f);
            }
            // Banner of the Fallen: the strongest banner within 8 m lifts everyone near it (its bearer too).
            float banner = 0f;
            foreach (var pl in Player.PlayerLife.All)
            {
                if (pl == null || !pl.TryGetComponent<PlayerProgress>(out var bp) || !bp.HasRelic(Relic.BannerOfTheFallen)) continue;
                bool band = Relics.Has(bp, Relics.Synergy.WarBand);
                float reach = band ? 12f : 8f;
                if ((pl.transform.position - p.transform.position).sqrMagnitude < reach * reach)
                    banner = Mathf.Max(banner, bp.RelicPower(Relic.BannerOfTheFallen) * (band ? 1.5f : 1f));
            }
            if (banner > 0f) m *= 1f + 0.08f * banner;
            if (p.ServerConsumeDodgeStrike()) m *= 1.6f;
            if (p.Blessed(Blessing.Wrath)) m *= 1.15f;
            if (p.HasRelic(Relic.ExecutionersCoin) && target.Fraction < (Relics.Has(p, Relics.Synergy.Reaper) ? 0.45f : 0.3f)) m *= 1f + 0.5f * p.RelicPower(Relic.ExecutionersCoin);
            if (p.HasRelic(Relic.WintersTooth) && target.Chilled.Value) m *= 1f + 0.25f * p.RelicPower(Relic.WintersTooth) + (Relics.Has(p, Relics.Synergy.DeepWinter) ? 0.25f : 0f);
            m *= Player.Ultimates.OutgoingMultiplier(p);
            if (p.TryGetComponent<Player.Emotes>(out var em) && em.Brothers) m *= 1.1f;
            if (Player.Teamwork.InLastStand(p.GetComponent<Player.PlayerLife>())) m *= Player.Teamwork.LastStandDamage;
            if (p.TryGetComponent<Player.PlayerDebuffs>(out var curse))
            {
                m *= curse.DamageMultiplier;
                if (curse.Rooted && p.HasRelic(Relic.RootbreakerCharm)) m *= 1f + 0.5f * p.RelicPower(Relic.RootbreakerCharm);
            }
            // Critical hits: a little luck, more with DEX.
            if (!World.DungeonDirector.TestMode && Random.value < CritChance(p))
            {
                m *= p.HasRelic(Relic.WhettedFang) ? 2f + 0.25f * p.RelicTier(Relic.WhettedFang) : 1.75f;
                // Frost Shard: a crit chills (it used to be a coin flip on every hit, the fifth chill source).
                if (Relics.Has(p, Relics.Synergy.ColdSteel) && target.ServerConsumeChill())
                {
                    m *= 1.4f;
                    Classes.Combos.Popup(target, Classes.ComboKind.Shatter, p);
                }
                else if (p.HasRelic(Relic.FrostCharm)) target.ServerChill(3f);
                p.CritFxRpc(target.Center + Vector3.up * 0.4f);
            }
            if (World.DungeonDirector.InRitualWard(target.transform.position)) m *= 0.5f;
            return m;
        }

        /// <summary>5% base, DEX, the Masterwork weapon, Whetted Fang and Frost Shard. Any peer (the Tab sheet shows it).</summary>
        public static float CritChance(PlayerProgress p) => 0.05f + p.Value(Core.StatType.DEX) / 650f
            + (p.WeaponLevel.Value >= World.Anvil.MasterworkLevel ? World.Anvil.MasterworkCrit : 0f)
            + 0.1f * p.RelicPower(Relic.WhettedFang) + 0.05f * p.RelicPower(Relic.FrostCharm);

        private static readonly System.Collections.Generic.Dictionary<ulong, int> batonHits = new System.Collections.Generic.Dictionary<ulong, int>();
        private static bool boneBursting;
        private static readonly System.Collections.Generic.Dictionary<ulong, int> phylacteryHits = new System.Collections.Generic.Dictionary<ulong, int>();

        private static void OnServerAnyDamaged(Health target, float dealt, ulong attackerId)
        {
            if (target.Team != Team.Enemies) return;
            var p = Attacker(attackerId);
            if (p == null) return;
            if (target.IsAlive)
            {
                // Witchbone Fetish: every 8 s, a hit Marks.
                if (p.Wears(GearId.WitchboneFetish) && p.ServerConsumeFetish()) target.ServerMark(4f);
                // Choirmaster's Baton: every tenth hit mends the bearer and everyone near them.
                if (p.Wears(GearId.ChoirmastersBaton))
                {
                    batonHits.TryGetValue(attackerId, out int b);
                    if (++b >= 10)
                    {
                        b = 0;
                        foreach (var pl in Player.PlayerLife.All)
                            if (pl != null && !pl.Downed.Value && (pl.transform.position - p.transform.position).sqrMagnitude < 64f)
                                pl.Health.ServerHeal(pl.Health.Max.Value * 0.05f);
                        p.BatonFxRpc();
                    }
                    batonHits[attackerId] = b;
                }
                // Lich's Phylactery: every fourth hit chills.
                if (p.Wears(GearId.LichPhylactery))
                {
                    phylacteryHits.TryGetValue(attackerId, out int n);
                    if (++n >= 4) { n = 0; target.ServerChill(3f); }
                    phylacteryHits[attackerId] = n;
                }
                return;
            }
            if (p.HasEdge(Edge.Momentum)) p.ServerMomentumUntil = Time.time + Edges.MomentumSeconds;
            if (p.Wears(GearId.CrackedHourglass)) p.ShaveCooldownsRpc(1f);
            if (p.Wears(GearId.Bloodstone)) p.GetComponent<Health>().ServerHeal(p.GetComponent<Health>().Max.Value * 0.04f);
            if (p.Wears(GearId.CardinalVestments)) p.ServerJudgement(target.transform.position);
            // Marrowgut's Rib: the kill bursts into bone shards (never a chain: shards that kill do not burst again).
            if (p.Wears(GearId.MarrowgutsRib) && !boneBursting)
            {
                boneBursting = true;
                var at = target.transform.position;
                foreach (var foe in CombatQuery.Circle(at, 3f, Team.Enemies).ToArray())
                    if (foe != target) foe.ServerDamage(22f * p.AttackMultiplier, at, p.NetworkObjectId);
                boneBursting = false;
                p.BoneBurstFxRpc(at);
            }
            if (p.Wears(GearId.EmberLocket) && Random.value < 0.3f && World.DungeonDirector.Instance != null)
                World.DungeonDirector.Instance.ServerDropEmbers(target.transform.position, 2);
        }
    }
}
