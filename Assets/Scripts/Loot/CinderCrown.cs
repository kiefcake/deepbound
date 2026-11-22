using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.Loot
{
    /// <summary>
    /// CINDER CROWN (2026-11-22, relic 29): the Emberbound's fire turned on the dead. Every kill by its bearer leaves a burning
    /// patch (<see cref="Radius"/> m, <see cref="Seconds"/> s) that scorches and burns enemies standing in it every half second.
    /// Server side; the look is the Emberbound patch (<see cref="Enemies.EmberPatchFx"/>), sent by <see cref="PlayerProgress.CinderPatchRpc"/>.
    /// </summary>
    public static class CinderCrown
    {
        public const float Radius = 1.6f, Seconds = 4f, PerTick = 6f, RebornHeal = 0.015f;
        private struct Patch { public Vector3 At; public float Until; public ulong By; public float Hit; }
        private static readonly List<Patch> patches = new List<Patch>();
        private static float nextTick;
        /// <summary>True while patches burn: a kill by a patch leaves no new patch (no chain fires).</summary>
        public static bool Ticking { get; private set; }

        public static int Live => patches.Count;

        /// <summary>Server: the bearer killed something at <paramref name="at"/>.</summary>
        public static void ServerDrop(PlayerProgress p, Vector3 at)
        {
            float hit = PerTick * p.AttackMultiplier * p.RelicPower(Relic.CinderCrown);
            patches.Add(new Patch { At = at, Until = Time.time + Seconds, By = p.NetworkObjectId, Hit = hit });
            p.CinderPatchRpc(at);
        }

        /// <summary>Server, every frame (DungeonDirector).</summary>
        public static void ServerTick()
        {
            if (patches.Count == 0 || Time.time < nextTick) return;
            nextTick = Time.time + 0.5f;
            Ticking = true;
            try
            {
            for (int i = patches.Count - 1; i >= 0; i--)
            {
                var pt = patches[i];
                if (Time.time > pt.Until) { patches.RemoveAt(i); continue; }
                // Reborn in Flame (Cinder Crown + Phoenix Feather): the bearer mends standing in their own fire.
                var nm = Unity.Netcode.NetworkManager.Singleton;
                if (nm != null && nm.SpawnManager.SpawnedObjects.TryGetValue(pt.By, out var owner) && owner.TryGetComponent<PlayerProgress>(out var pp)
                    && Relics.Has(pp, Relics.Synergy.RebornInFlame) && owner.TryGetComponent<Health>(out var oh) && oh.IsAlive)
                {
                    var d = owner.transform.position - pt.At; d.y = 0f;
                    if (d.magnitude <= Radius) oh.ServerHeal(oh.Max.Value * RebornHeal);
                }
                foreach (var foe in CombatQuery.Circle(pt.At, Radius, Team.Enemies))
                {
                    if (foe == null || !foe.IsAlive) continue;
                    foe.ServerScorch(2f, pt.By);
                    foe.Tag("Cinder Crown").ServerDamage(pt.Hit, pt.At + Vector3.up * 0.2f, pt.By);
                }
            }
            }
            finally { Ticking = false; }
        }

        /// <summary>A floor change drops every patch.</summary>
        public static void Clear() => patches.Clear();
    }
}
