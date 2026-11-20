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
        /// <summary>The Wardweaver: a shard at most every <see cref="ShardEvery"/> s, each soaking up to <see cref="ShardSoak"/> of max health.</summary>
        public const float ShardEvery = 1.5f, ShardSoak = 0.15f, ShardBurst = 3f;
        public const int Shards = 3, ShardsMastered = 5;
        private static readonly Dictionary<ulong, float> nextShard = new Dictionary<ulong, float>();

        /// <summary>Server: the Wardweaver's shards take what they can of a blow.</summary>
        public static float Absorb(PlayerProgress p, float damage)
        {
            if (p == null || damage <= 0f || !p.OnPath(Path) || p.ClassId.Value != ClassId.Mage || p.WardShards.Value == 0) return damage;
            var h = p.GetComponent<Health>();
            if (h == null) return damage;
            p.WardShards.Value--;
            float soak = h.Max.Value * ShardSoak;
            if (p.Mastered(Path)) AbilityKit.Blast(p.transform.position, ShardBurst, 20f * p.AttackMultiplier, p.NetworkObjectId, "Ward Shard", e => AbilityKit.Stun(e, 0.3f));
            p.AbilityBurstRpc(p.transform.position + Vector3.up * 1.1f, p.Mastered(Path) ? ShardBurst : 0.6f, PlayerProgress.FxKind.Frost);
            return Mathf.Max(0f, damage - soak);
        }

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
            ClassId.Mage => "SURVIVAL. Spells that land spin WARD SHARDS round you (up to 3); each one swallows a blow (up to 15% of your health) and is gone.",
            ClassId.Cleric => "SURVIVAL. Stand still for a second and you PLANT: you take 30% less and your smites fly in pairs until you move.",
            ClassId.Rogue => "SURVIVAL. For a second after each hit you land you take 25% less, and every kill shrouds you (unseen for 1.5 s).",
            ClassId.Ranger => "SURVIVAL. The dead within 6 m hit you 20% softer, and a shot at one that close is a POINT-BLANK shot: it hurls it back and stuns it.",
            ClassId.Flagellant => "SURVIVAL. A blow that takes you below half health makes you ERUPT: you heal 10% and stun everything within 4 m (every 20 s). Below half you take 25% less.",
            ClassId.PlagueDoctor => "SURVIVAL. Whatever strikes you in melee is blighted, and each blighted foe within 8 m makes you take 4% less (up to 20%).",
            _ => "SURVIVAL. Blocks build SHIELD CHARGE; when full, your next block looses a shockwave (4 m, damage and a stun).",
        };

        public static string Mastery(ClassId id) => id switch
        {
            ClassId.Mage => "Up to five shards, and each one bursts on the enemies round you as it breaks.",
            ClassId.Cleric => "Planted, you also mend allies within 5 m (2% a second).",
            ClassId.Rogue => "35% less instead.",
            ClassId.Ranger => "30% softer instead.",
            ClassId.Flagellant => "40% less instead, and the eruption comes every 12 s.",
            ClassId.PlagueDoctor => "Up to 32%.",
            _ => "The shockwave also shields you (20% of max health).",
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
                    int cap = p.Mastered(Path) ? ShardsMastered : Shards;
                    if (p.WardShards.Value >= cap || (nextShard.TryGetValue(p.NetworkObjectId, out var ns) && Time.time < ns)) break;
                    nextShard[p.NetworkObjectId] = Time.time + ShardEvery;
                    p.WardShards.Value++;
                    break;
                }
                case ClassId.Rogue:
                    shadowUntil[p.NetworkObjectId] = Time.time + ShadowSeconds;
                    break;
                case ClassId.Ranger:
                    // Point-blank: a shot at something this close hurls it back and stuns it (once a second each).
                    if ((target.transform.position - p.transform.position).sqrMagnitude < PathwardenReach * PathwardenReach && !AbilityKit.IsBoss(target)
                        && (!mendReady.TryGetValue(target.NetworkObjectId, out var r) || Time.time >= r))
                    {
                        mendReady[target.NetworkObjectId] = Time.time + 1f;
                        var away = target.transform.position - p.transform.position; away.y = 0f;
                        AbilityKit.Push(target, away.normalized * 11f);
                        AbilityKit.Stun(target, 0.6f);
                        p.AbilityTextRpc(target.Center + Vector3.up, "POINT BLANK", PlayerProgress.FxKind.Earth);
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
                case ClassId.Mage: return 1f;
                case ClassId.Cleric: return p.TryGetComponent<Player.ClericKit>(out var ck) && ck.Planted ? Player.ClericKit.PlantedTaken : 1f;
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

        /// <summary>Server: the cut a blow would get right now, for the HUD (the Pathwarden's counts when the dead are within reach).</summary>
        public static float Readout(PlayerProgress p)
        {
            if (p == null || !p.OnPath(Path)) return 1f;
            var at = p.transform.position + Vector3.forward * 999f;
            if (p.ClassId.Value == ClassId.Ranger)
                foreach (var e in Health.All)
                    if (e != null && e.Team == Team.Enemies && e.IsAlive && (e.transform.position - p.transform.position).sqrMagnitude < PathwardenReach * PathwardenReach) { at = e.transform.position; break; }
            return Incoming(p, at);
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
