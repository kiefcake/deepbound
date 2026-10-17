using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The balance pass's measuring stick. For every enemy type on every floor it works out,
    /// from the real stats and scaling: how many Knight sword swings kill it (a reference
    /// player with one anvil level per floor), how long that takes, and how many of its hits
    /// put that Knight down. Anything far outside the norm is flagged so it can be tuned.
    /// Pure arithmetic: logged by the smoke test as <c>[Balance]</c> lines.
    /// </summary>
    public static class BalanceReport
    {
        // The reference player: a Knight carrying what a typical run has gathered by each floor.
        private const float SwingDamage = Player.KnightKit.SwingDamage, SwingCycle = 0.6f;

        /// <summary>What the reference Knight carries into floor <paramref name="depth"/> (0-based).</summary>
        public struct Loadout
        {
            public int WeaponLevel, Relics;
            public Loot.GearItem Armor, Charm;
            public bool Rune;
            public float Att, Def, Hp, DamageMult;

            public static Loadout For(int depth)
            {
                var k = Classes.ClassDefinition.Knight.BaseStats;
                var l = new Loadout
                {
                    // Anvil: nothing before floor 1; embers from each floor buy roughly one and a half levels.
                    WeaponLevel = new[] { 0, 2, 3, 4 }[Mathf.Clamp(depth, 0, 3)],
                    // Gear: a Fine armour piece and charm from the floor before (none on floor 1).
                    Armor = depth >= 1 ? new Loot.GearItem(Loot.GearId.OssuaryPlate, 1, depth - 1) : default,
                    Charm = depth >= 1 ? new Loot.GearItem(Loot.GearId.HuntersTooth, 1, depth - 1) : default,
                    // A rune once the first socket opens at +1.
                    Rune = depth >= 1,
                    // Relics: about one a floor.
                    Relics = depth,
                };
                float gearAtt = Loot.Gear.StatValue(l.Armor, Core.StatType.ATT) + Loot.Gear.StatValue(l.Charm, Core.StatType.ATT);
                float gearDef = Loot.Gear.StatValue(l.Armor, Core.StatType.DEF) + Loot.Gear.StatValue(l.Charm, Core.StatType.DEF);
                float gearHp = Loot.Gear.StatValue(l.Armor, Core.StatType.HP) + Loot.Gear.StatValue(l.Charm, Core.StatType.HP);
                // Gold at the merchant: Hearty Stew and Riveted Plate each floor, to their caps.
                float shopHp = Mathf.Min(90f, 30f * depth), shopDef = Mathf.Min(6f, 2f * depth);
                l.Att = k[(int)Core.StatType.ATT] + 4f * l.WeaponLevel + gearAtt;
                l.Def = k[(int)Core.StatType.DEF] + gearDef + shopDef;
                l.Hp = k[(int)Core.StatType.HP] + gearHp + shopHp;
                // Rune (~+5% on average) and relics (most are not raw damage: ~+6% each).
                l.DamageMult = (l.Rune ? 1.05f : 1f) * (1f + 0.06f * l.Relics);
                return l;
            }

            public override string ToString() => "+" + WeaponLevel + " sword, ATT " + Att.ToString("F0") + ", DEF " + Def.ToString("F0") + ", HP " + Hp.ToString("F0") +
                (Armor.IsEmpty ? "" : ", " + Loot.Gear.Name(Armor) + " + " + Loot.Gear.Name(Charm)) + (Rune ? ", a rune" : "") + ", " + Relics + " relic(s)";
        }

        public struct Row
        {
            public string Name;
            public int Depth;
            public float Hp, Hit, SwingsToKill, SecondsToKill, HitsToDown, EnemyDps;
            public string Flag;
        }

        public static List<Row> Compute(IEnumerable<EnemyBrain> prefabs)
        {
            var rows = new List<Row>();
            foreach (var b in prefabs)
            {
                if (b == null) continue;
                for (int depth = 0; depth <= World.DungeonDirector.AbyssDepth; depth++)
                {
                    // A boss is only ever met on its own floor (the Unsung in the Abyss, below the last).
                    int bossFloor = b is GravewardenBrain ? 0 : b is TideLichBrain ? 1 : b is HollowKingBrain ? 2 : b is CardinalBrain ? 3 : b is UnsungBrain ? World.DungeonDirector.AbyssDepth : -1;
                    if (bossFloor < 0 && depth >= World.DungeonDirector.AbyssDepth) continue; // the rank and file: floors 1-4
                    if (bossFloor >= 0 && depth != bossFloor) continue;
                    bool boss = b is BossBrain;
                    var kit = Loadout.For(depth);
                    float swing = SwingDamage * (0.5f + kit.Att / 50f) * kit.DamageMult * (boss && !kit.Charm.IsEmpty ? 1.25f : 1f); // Hunter's Tooth vs bosses
                    float hp = b.BaseHealth * (boss ? Difficulty.BossHealth(1) : Difficulty.EnemyHealth(1) * Difficulty.RankHealth) * Difficulty.DepthHealth(depth);
                    float perSwing = Mathf.Max(swing - b.BaseDefense * Combat.Health.DefenseWeight, swing * Combat.Health.MinDamageFraction);
                    float hit = b.BaseDamage * Difficulty.Damage(1) * Difficulty.DepthDamage(depth) * (boss ? 1f : Difficulty.RankDamage);
                    float taken = Mathf.Max(hit - kit.Def * Combat.Health.DefenseWeight, hit * Combat.Health.MinDamageFraction);
                    rows.Add(Make(b.name, depth, hp, hit, perSwing, taken, kit.Hp, b.AttackCycle, b));
                    // The first boss, alone: the shield takes most frontal hits; openings after each sweep let some through.
                    if (b is GravewardenBrain)
                    {
                        float open = GravewardenBrain.Opening / GravewardenBrain.SweepCycle;
                        float fraction = open + (1f - open) * GravewardenBrain.SoloFrontal;
                        rows.Add(Make(b.name + " (solo, from the front)", depth, hp, hit, perSwing * fraction, taken, kit.Hp, b.AttackCycle, b));
                    }
                }
            }
            return rows;
        }

        private static Row Make(string name, int depth, float hp, float hit, float perSwing, float taken, float playerHp, float cycle, EnemyBrain b)
        {
            var row = new Row
            {
                Name = name,
                Depth = depth,
                Hp = hp,
                Hit = hit,
                SwingsToKill = hp / perSwing,
                SecondsToKill = hp / perSwing * SwingCycle,
                HitsToDown = taken > 0f ? playerHp / taken : 999f,
                EnemyDps = taken / Mathf.Max(0.5f, cycle),
            };
            // The norms: a rank-and-file enemy dies in 1.6-12 swings and needs 4+ hits to down you;
            // a heavy up to 25 swings; a boss, on its own floor, 50-160.
            bool boss = b is BossBrain;
            bool heavy = b is BruteBrain || b is GargoyleBrain || b is MimicBrain;
            float maxSwings = boss ? 160f : heavy ? 25f : 12f, minSwings = boss ? 50f : 1.6f;
            if (row.SwingsToKill > maxSwings) row.Flag = "TANKY";
            else if (row.SwingsToKill < minSwings) row.Flag = "FRAGILE";
            if (b.BaseDamage > 0f && row.HitsToDown < (boss ? 3f : 4f)) row.Flag = (row.Flag != null ? row.Flag + "+" : "") + "BRUTAL";
            return row;
        }

        public static string Format(List<Row> rows)
        {
            var sb = new StringBuilder();
            for (int d = 0; d < 4; d++) sb.AppendLine("[Balance] loadout floor " + (d + 1) + ": " + Loadout.For(d));
            sb.AppendLine("[Balance] enemy | floor | hp | hit | swings to kill | seconds | hits to down a knight | dps vs knight | flag");
            foreach (var r in rows)
                sb.AppendLine("[Balance] " + r.Name + " | " + (r.Depth + 1) + " | " + r.Hp.ToString("F0") + " | " + r.Hit.ToString("F0") + " | " +
                    r.SwingsToKill.ToString("F1") + " | " + r.SecondsToKill.ToString("F1") + " | " + r.HitsToDown.ToString("F1") + " | " +
                    r.EnemyDps.ToString("F1") + " | " + (r.Flag ?? "ok"));
            return sb.ToString();
        }
    }
}
