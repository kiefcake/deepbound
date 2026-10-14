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
            float m = Classes.Combos.ServerMultiplier(target, p);
            if (p.Wears(GearId.HuntersTooth) && target.TryGetComponent<EnemyBrain>(out var brain) && (brain.Elite.Value || brain is BossBrain)) m *= 1.25f;
            if (p.ServerConsumeDodgeStrike()) m *= 1.6f;
            return m;
        }

        private static void OnServerAnyDamaged(Health target, float dealt, ulong attackerId)
        {
            if (target.Team != Team.Enemies || target.IsAlive) return;
            var p = Attacker(attackerId);
            if (p == null) return;
            if (p.Wears(GearId.Bloodstone)) p.GetComponent<Health>().ServerHeal(p.GetComponent<Health>().Max.Value * 0.04f);
            if (p.Wears(GearId.EmberLocket) && Random.value < 0.3f && World.DungeonDirector.Instance != null)
                World.DungeonDirector.Instance.ServerDropEmbers(target.transform.position, 2);
        }
    }
}
