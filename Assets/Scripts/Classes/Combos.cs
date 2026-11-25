using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.Classes
{
    public enum ComboKind : byte { Shatter, Exposed, Distracted, AegisBurst, MarkSpread, Blessed, Hallowed, WardBroken, ThermalShock, Pincer, Punish, Purge, PlagueCrash, Convergence }

    public static class Combos
    {
        public const float PunishMultiplier = 1.2f;
        public const float ShatterMultiplier = 1.5f, ExposedMultiplier = 1.25f, DistractedMultiplier = 1.15f, BlessedMultiplier = 1.2f, InspiredMultiplier = 1.15f;
        public const float AegisBurstDamage = 35f, AegisBurstRadius = 3.5f;
        public const float PurgeMultiplier = 1.25f, PlagueCrashSeconds = 4f;

        public static float PurgeFor(Classes.ClassId attacker, Health target) =>
            attacker == Classes.ClassId.Cleric && target != null && target.Blighted.Value ? PurgeMultiplier : 1f;

        public static int ServerPlagueCrash(System.Collections.Generic.IEnumerable<Health> struck, Vector3 at, float radius, PlayerProgress by = null)
        {
            Health carrier = null;
            foreach (var h in struck) if (h != null && h.IsAlive && h.Blighted.Value) { carrier = h; break; }
            if (carrier == null) return 0;
            int caught = 0;
            foreach (var h in Combat.CombatQuery.Circle(at, radius, Team.Enemies))
            {
                if (h == null || !h.IsAlive || h.Blighted.Value) continue;
                h.ServerBlight(PlagueCrashSeconds, Player.PlagueDoctorKit.BlightDps, carrier.BlightSource);
                caught++;
            }
            if (caught > 0) Popup(carrier, ComboKind.PlagueCrash, by);
            return caught;
        }

        public static string Label(ComboKind kind)
        {
            switch (kind)
            {
                case ComboKind.Shatter: return "SHATTER";
                case ComboKind.Exposed: return "EXPOSED";
                case ComboKind.Distracted: return "DISTRACTED";
                case ComboKind.AegisBurst: return "AEGIS BURST";
                case ComboKind.MarkSpread: return "MARK SPREADS";
                case ComboKind.Hallowed: return "HALLOWED FIRE";
                case ComboKind.WardBroken: return "WARD BROKEN";
                case ComboKind.ThermalShock: return "THERMAL SHOCK";
                case ComboKind.Pincer: return "PINCER";
                case ComboKind.Punish: return "PUNISH";
                case ComboKind.Purge: return "PURGE";
                case ComboKind.PlagueCrash: return "PLAGUE CRASH";
                case ComboKind.Convergence: return "CONVERGENCE";
                default: return "BLESSED";
            }
        }

        public static Color Color(ComboKind kind)
        {
            switch (kind)
            {
                case ComboKind.Shatter: return new Color(0.55f, 0.85f, 1f);
                case ComboKind.Exposed: return new Color(1f, 0.85f, 0.3f);
                case ComboKind.Distracted: return new Color(1f, 0.55f, 0.35f);
                case ComboKind.AegisBurst: return new Color(1f, 0.9f, 0.55f);
                case ComboKind.MarkSpread: return new Color(1f, 0.35f, 0.3f);
                case ComboKind.Hallowed: return new Color(1f, 0.75f, 0.35f);
                case ComboKind.WardBroken: return new Color(0.73f, 0.66f, 1f);
                case ComboKind.ThermalShock: return new Color(1f, 0.55f, 0.85f);
                case ComboKind.Pincer: return new Color(0.6f, 0.95f, 0.75f);
                case ComboKind.Punish: return new Color(1f, 0.82f, 0.35f);
                case ComboKind.Purge: return new Color(0.85f, 1f, 0.55f);
                case ComboKind.PlagueCrash: return new Color(0.6f, 0.9f, 0.25f);
                case ComboKind.Convergence: return new Color(1f, 0.95f, 0.8f);
                default: return new Color(1f, 0.95f, 0.6f);
            }
        }

        public static string Explain(ComboKind kind)
        {
            switch (kind)
            {
                case ComboKind.Shatter: return "Shatter: heavy hits on a chilled enemy consume the chill for bonus damage.";
                case ComboKind.Exposed: return "Exposed: stunned enemies take 25% more damage from all sources.";
                case ComboKind.Distracted: return "Distracted: taunted enemies take 15% more damage from everyone except the taunter.";
                case ComboKind.AegisBurst: return "Aegis Burst: a broken Aegis shield explodes, damaging nearby enemies.";
                case ComboKind.MarkSpread: return "Mark Spread: when a Marked enemy dies, the Mark transfers to the nearest enemy.";
                case ComboKind.Hallowed: return "Hallowed Fire: a meteor landing on consecrated ground heals allies standing on it.";
                case ComboKind.WardBroken: return "Ward Broken: stunning or chilling a Warded elite shatters its ward.";
                case ComboKind.ThermalShock: return "Thermal Shock: frost on a scorched enemy, or fire on a chilled one, deals a burst of damage.";
                case ComboKind.Pincer: return "Pincer: striking an enemy opposite an ally who just hit it deals 15% more damage.";
                case ComboKind.Punish: return "Punish: enemies recovering after an attack (slumped, eyes dim) take 20% more damage.";
                case ComboKind.Purge: return "Purge: the Cleric deals 25% more damage to Blighted enemies.";
                case ComboKind.PlagueCrash: return "Plague Crash: the Flagellant's Crash on a Blighted enemy spreads Blight to all nearby enemies.";
                case ComboKind.Convergence: return "Convergence: two ultimates cast within 4 s of each other unleash a burst that stuns nearby enemies and grants the party Bloodlust.";
                default: return "Blessed: allies standing in consecrated ground deal 20% more damage.";
            }
        }

        private static readonly Dictionary<(ulong, ComboKind), float> lastPopup = new Dictionary<(ulong, ComboKind), float>();
        private static readonly HashSet<ComboKind> seenThisRun = new HashSet<ComboKind>();

        public static void ResetRun() => seenThisRun.Clear();

        public static bool Popup(Health at, ComboKind kind, PlayerProgress by = null)
        {
            var key = (at.NetworkObjectId, kind);
            if (lastPopup.TryGetValue(key, out float t) && Time.time - t < 1f) return false;
            lastPopup[key] = Time.time;
            if (lastPopup.Count > 256) lastPopup.Clear();
            if (by != null) by.ServerCreditCombo();
            bool first = seenThisRun.Add(kind);
            World.DungeonDirector.Instance?.ComboFxRpc(at.Center, (byte)kind, first);
            return true;
        }

        public static float TryShatter(Health target, PlayerProgress by = null)
        {
            if (!target.ServerConsumeChill()) return 1f;
            Popup(target, ComboKind.Shatter, by);
            return ShatterMultiplier;
        }

        public static float ServerMultiplier(Health target, PlayerProgress attacker)
        {
            float m = 1f;
            if (target.TryGetComponent<EnemyBrain>(out var brain))
            {
                if (brain.IsStunned)
                {
                    m *= ExposedMultiplier;
                    Popup(target, ComboKind.Exposed, attacker);
                }
                else if (brain.Recovering)
                {
                    m *= PunishMultiplier;
                    Popup(target, ComboKind.Punish, attacker);
                }
                var taunter = brain.Taunter;
                if (taunter != null && taunter.gameObject != attacker.gameObject)
                {
                    m *= DistractedMultiplier;
                    Popup(target, ComboKind.Distracted, attacker);
                }
            }
            m *= Player.Teamwork.ServerPincer(target, attacker);

            if (target.Blighted.Value && attacker.HasRelic(Loot.Relic.PlagueCenser) && attacker.TryGetComponent<Health>(out var censerHp) && censerHp.IsAlive)
                censerHp.ServerHeal(censerHp.Max.Value * 0.006f * attacker.RelicPower(Loot.Relic.PlagueCenser));
            float purge = PurgeFor(attacker.ClassId.Value, target);
            if (purge > 1f) { m *= purge; Popup(target, ComboKind.Purge, attacker); }
            if (Player.ClericKit.InConsecration(attacker.transform.position)) m *= BlessedMultiplier;
            if (Player.ClericKit.IsInspired(attacker.GetComponent<Health>())) m *= InspiredMultiplier;
            return m;
        }

        private static bool hooked;

        public static void EnsureServerHooks()
        {
            if (hooked) return;
            hooked = true;
            Health.ServerShieldBroken += OnShieldBroken;
            Health.ServerAnyDamaged += OnAnyDamaged;
            Health.ThermalShock = OnThermalShock;
        }

        public const float ShockFlat = 18f, ShockFraction = 0.05f, ShockBossFraction = 0.01f;

        private static void OnThermalShock(Health target, ulong by)
        {
            PlayerProgress p = null;
            var net = Unity.Netcode.NetworkManager.Singleton;
            if (net != null && by != ulong.MaxValue && net.SpawnManager.SpawnedObjects.TryGetValue(by, out var o)) o.TryGetComponent(out p);

            bool boss = target.TryGetComponent<BossBrain>(out var bossBrain);
            float dmg = ShockFlat * (p != null ? p.AttackMultiplier : 1f) + (boss ? target.Max.Value / bossBrain.PartyHealthScale * ShockBossFraction : target.Max.Value * ShockFraction);
            Popup(target, ComboKind.ThermalShock, p);
            World.DungeonDirector.Instance?.ThermalShockFxRpc(target.Center);
            target.Tag("Combos").ServerDamage(dmg, target.Center, by);
        }

        private static void OnShieldBroken(Health shielded)
        {
            if (shielded.Team != Team.Players) return;
            foreach (var foe in CombatQuery.Circle(shielded.transform.position, AegisBurstRadius, Team.Enemies).ToArrayList())
                foe.Tag("Combos").ServerDamage(AegisBurstDamage, shielded.transform.position, shielded.NetworkObjectId);
            Popup(shielded, ComboKind.AegisBurst, shielded.GetComponent<PlayerProgress>());
            World.DungeonDirector.Instance?.AegisBurstFxRpc(shielded.transform.position);
        }

        private static void OnAnyDamaged(Health target, float dealt, ulong attackerId)
        {
            if (target.Team != Team.Enemies || target.IsAlive || !target.Marked.Value) return;
            Health next = null;
            float best = 7f;
            foreach (var h in Health.All)
            {
                if (h == null || h == target || h.Team != Team.Enemies || !h.IsAlive) continue;
                float d = Vector3.Distance(h.transform.position, target.transform.position);
                if (d < best) { best = d; next = h; }
            }
            if (next == null) return;
            next.ServerMark(5f);
            var net = Unity.Netcode.NetworkManager.Singleton;
            PlayerProgress killer = null;
            if (net != null && net.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var o)) o.TryGetComponent(out killer);
            Popup(next, ComboKind.MarkSpread, killer);
        }

        private static List<Health> ToArrayList(this IEnumerable<Health> e) => new List<Health>(e);
    }
}
