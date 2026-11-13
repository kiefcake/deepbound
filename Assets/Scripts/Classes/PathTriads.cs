using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.Classes
{
    /// <summary>
    /// PATH TRIADS (2026-11-13, the user: "Give each class a third anvil path built for survival, e.g. a Knight 'Bulwark' path
    /// where blocking builds a shield charge you can spend as a shockwave. Every class gets a clear tank/damage/utility fork").
    /// Path 3 of every class keeps you standing. Server-side: <see cref="Incoming"/> is folded into the delver's incoming damage,
    /// <see cref="OnDamaged"/> hears every hit a delver lands, and <see cref="OnKnightBlock"/> is the Knight's shield charge.
    /// </summary>
    public static class PathTriads
    {
        public const int Path = 3;
        public const float BulwarkCharge = 100f, BulwarkRadius = 4f, BulwarkStun = 0.8f, BulwarkShield = 0.2f;
        public const float WardPerHit = 0.04f, WardCap = 0.25f, WardCapMastered = 0.4f, WardSeconds = 6f;
        public const float AnchoriteHeal = 0.3f, ShadowSeconds = 1f, PathwardenReach = 6f, PathwardenHeal = 0.02f, MithridateReach = 8f;

        public static string Name(ClassId id) => id switch
        {
            ClassId.Mage => "Wardweaver",
            ClassId.Cleric => "Anchorite",
            ClassId.Rogue => "Shadowskin",
            ClassId.Ranger => "Pathwarden",
            ClassId.Flagellant => "Scarred",
            ClassId.PlagueDoctor => "Mithridate",
            _ => "Bulwark",
        };

        public static string Describe(ClassId id) => id switch
        {
            ClassId.Mage => "SURVIVAL. Every spell that lands wards you for 4% of your max health (up to 25%).",
            ClassId.Cleric => "SURVIVAL. Your hits heal you for 30% of the damage, and you take 10% less.",
            ClassId.Rogue => "SURVIVAL. For a second after each hit you land, you take 25% less.",
            ClassId.Ranger => "SURVIVAL. The dead within 6 m hit you 20% softer; your hits on them mend you 2% (once a second).",
            ClassId.Flagellant => "SURVIVAL. Below half health you take 25% less.",
            ClassId.PlagueDoctor => "SURVIVAL. Each blighted foe within 8 m makes you take 4% less (up to 20%).",
            _ => "SURVIVAL. Blocked blows build SHIELD CHARGE; full, your next block looses a shockwave (4 m: damage, a stun).",
        };

        public static string Mastery(ClassId id) => id switch
        {
            ClassId.Mage => "The ward holds up to 40%, and while it holds you take 15% less.",
            ClassId.Cleric => "You take 15% less instead.",
            ClassId.Rogue => "35% less instead.",
            ClassId.Ranger => "30% softer instead.",
            ClassId.Flagellant => "40% less instead.",
            ClassId.PlagueDoctor => "Up to 32%.",
            _ => "The shockwave also shields you for 20% of your max health.",
        };

        private static bool hooked;
        private static readonly Dictionary<ulong, float> shadowUntil = new Dictionary<ulong, float>(), mendReady = new Dictionary<ulong, float>(), charge = new Dictionary<ulong, float>();

        /// <summary>Server: listen for the hits delvers land (once).</summary>
        public static void EnsureHook()
        {
            if (hooked) return;
            hooked = true;
            Health.ServerAnyDamaged += OnDamaged;
        }

        private static PlayerProgress Delver(ulong id)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj)) return null;
            return obj.GetComponent<PlayerProgress>();
        }

        private static void OnDamaged(Health target, float lost, ulong attackerId)
        {
            if (target == null || target.Team != Team.Enemies || lost <= 0f) return;
            var p = Delver(attackerId);
            if (p == null || !p.OnPath(Path)) return;
            var me = p.GetComponent<Health>();
            if (me == null || !me.IsAlive) return;
            switch (p.ClassId.Value)
            {
                case ClassId.Mage:
                {
                    float cap = (p.Mastered(Path) ? WardCapMastered : WardCap) * me.Max.Value;
                    me.ServerShield(Mathf.Min(cap, me.Shield.Value + WardPerHit * me.Max.Value), WardSeconds);
                    break;
                }
                case ClassId.Cleric:
                    me.ServerHeal(lost * AnchoriteHeal);
                    break;
                case ClassId.Rogue:
                    shadowUntil[p.NetworkObjectId] = Time.time + ShadowSeconds;
                    break;
                case ClassId.Ranger:
                    if ((target.transform.position - p.transform.position).sqrMagnitude < PathwardenReach * PathwardenReach
                        && (!mendReady.TryGetValue(p.NetworkObjectId, out var r) || Time.time >= r))
                    {
                        mendReady[p.NetworkObjectId] = Time.time + 1f;
                        me.ServerHeal(me.Max.Value * PathwardenHeal);
                    }
                    break;
            }
        }

        /// <summary>Server: the path's share of a blow on <paramref name="p"/> (a multiplier).</summary>
        public static float Incoming(PlayerProgress p, Vector3 attackerPosition)
        {
            if (p == null || !p.OnPath(Path)) return 1f;
            bool m = p.Mastered(Path);
            var h = p.GetComponent<Health>();
            switch (p.ClassId.Value)
            {
                case ClassId.Mage: return m && h != null && h.Shield.Value > 0f ? 0.85f : 1f;
                case ClassId.Cleric: return m ? 0.85f : 0.9f;
                case ClassId.Rogue: return shadowUntil.TryGetValue(p.NetworkObjectId, out var u) && Time.time < u ? (m ? 0.65f : 0.75f) : 1f;
                case ClassId.Ranger: return (attackerPosition - p.transform.position).sqrMagnitude < PathwardenReach * PathwardenReach ? (m ? 0.7f : 0.8f) : 1f;
                case ClassId.Flagellant: return h != null && h.Fraction < 0.5f ? (m ? 0.6f : 0.75f) : 1f;
                case ClassId.PlagueDoctor:
                {
                    int n = 0;
                    foreach (var e in Health.All)
                        if (e != null && e.Team == Team.Enemies && e.IsAlive && e.Blighted.Value && (e.transform.position - p.transform.position).sqrMagnitude < MithridateReach * MithridateReach) n++;
                    return 1f - Mathf.Min(m ? 0.32f : 0.2f, 0.04f * n);
                }
            }
            return 1f;
        }

        /// <summary>Server: a Knight on the Bulwark path turned <paramref name="blocked"/> damage on the shield. True when it looses the shockwave.</summary>
        public static bool OnKnightBlock(PlayerProgress p, float blocked, float swingDamage)
        {
            if (p == null || !p.OnPath(Path) || p.ClassId.Value != ClassId.Knight) return false;
            charge.TryGetValue(p.NetworkObjectId, out var c);
            if (c < BulwarkCharge) { charge[p.NetworkObjectId] = c + blocked; p.ShieldCharge.Value = Mathf.Clamp01((c + blocked) / BulwarkCharge); return false; }
            charge[p.NetworkObjectId] = 0f;
            p.ShieldCharge.Value = 0f;
            foreach (var e in CombatQuery.Circle(p.transform.position + Vector3.up * 0.9f, BulwarkRadius, Team.Enemies))
            {
                e.Tag("Bulwark").ServerDamage(swingDamage * 2.5f * p.AttackMultiplier, p.transform.position, p.NetworkObjectId);
                if (e.TryGetComponent<Enemies.EnemyBrain>(out var b)) b.ServerStun(BulwarkStun);
            }
            if (p.Mastered(Path))
            {
                var h = p.GetComponent<Health>();
                if (h != null) h.ServerShield(h.Max.Value * BulwarkShield, 6f);
            }
            return true;
        }

        /// <summary>Any peer: how full the Knight's shield charge is (server only; clients see 0).</summary>
        public static float ChargeOf(ulong id) => charge.TryGetValue(id, out var c) ? Mathf.Clamp01(c / BulwarkCharge) : 0f;

        /// <summary>Server, tests: set a Knight's charge.</summary>
        public static void DebugCharge(ulong id, float value) => charge[id] = value;
    }
}
