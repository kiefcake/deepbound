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
            float m = Classes.Combos.ServerMultiplier(target, p) * Runes.OutgoingMultiplier(target, p);
            if (p.Wears(GearId.HuntersTooth) && target.TryGetComponent<EnemyBrain>(out var brain) && (brain.Elite.Value || brain is BossBrain)) m *= 1.25f;
            if (p.ServerConsumeDodgeStrike()) m *= 1.6f;
            if (p.Blessed(Blessing.Wrath)) m *= 1.15f;
            if (p.HasRelic(Relic.ExecutionersCoin) && target.Fraction < (Relics.Has(p, Relics.Synergy.Reaper) ? 0.45f : 0.3f)) m *= 1.5f;
            if (p.HasRelic(Relic.WintersTooth) && target.Chilled.Value) m *= Relics.Has(p, Relics.Synergy.DeepWinter) ? 1.5f : 1.25f;
            m *= Player.Ultimates.OutgoingMultiplier(p);
            if (p.TryGetComponent<Player.Emotes>(out var em) && em.Brothers) m *= 1.1f;
            if (p.TryGetComponent<Player.PlayerDebuffs>(out var curse)) m *= curse.DamageMultiplier;
            // Critical hits: a little luck, more with DEX.
            if (!World.DungeonDirector.TestMode && Random.value < 0.05f + p.Value(Core.StatType.DEX) / 650f)
            {
                m *= 1.75f;
                p.CritFxRpc(target.Center + Vector3.up * 0.4f);
            }
            if (World.DungeonDirector.InRitualWard(target.transform.position)) m *= 0.5f;
            return m;
        }

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
                // Lich's Phylactery: every fourth hit chills.
                if (p.Wears(GearId.LichPhylactery))
                {
                    phylacteryHits.TryGetValue(attackerId, out int n);
                    if (++n >= 4) { n = 0; target.ServerChill(3f); }
                    phylacteryHits[attackerId] = n;
                }
                return;
            }
            if (p.Wears(GearId.Bloodstone)) p.GetComponent<Health>().ServerHeal(p.GetComponent<Health>().Max.Value * 0.04f);
            if (p.Wears(GearId.CardinalVestments)) p.ServerJudgement(target.transform.position);
            if (p.Wears(GearId.EmberLocket) && Random.value < 0.3f && World.DungeonDirector.Instance != null)
                World.DungeonDirector.Instance.ServerDropEmbers(target.transform.position, 2);
        }
    }
}
