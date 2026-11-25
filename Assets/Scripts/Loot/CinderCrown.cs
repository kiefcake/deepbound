using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.Loot
{
    public static class CinderCrown
    {
        public const float Radius = 1.6f, Seconds = 4f, PerTick = 6f, RebornHeal = 0.015f;
        private struct Patch { public Vector3 At; public float Until; public ulong By; public float Hit; }
        private static readonly List<Patch> patches = new List<Patch>();
        private static float nextTick;

        public static bool Ticking { get; private set; }

        public static int Live => patches.Count;

        public static void ServerDrop(PlayerProgress p, Vector3 at)
        {
            float hit = PerTick * p.AttackMultiplier * p.RelicPower(Relic.CinderCrown);
            patches.Add(new Patch { At = at, Until = Time.time + Seconds, By = p.NetworkObjectId, Hit = hit });
            p.CinderPatchRpc(at);
        }

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

        public static void Clear() => patches.Clear();
    }
}
