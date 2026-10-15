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
        // The reference player: a Knight, weapon tempered once per floor, no gear, no relics.
        private const float SwingDamage = 34f, SwingCycle = 0.6f, PlayerHp = 240f, PlayerDef = 6f;

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
                for (int depth = 0; depth < 4; depth++)
                {
                    // A boss is only ever met on its own floor.
                    int bossFloor = b is GravewardenBrain ? 0 : b is TideLichBrain ? 1 : b is HollowKingBrain ? 2 : b is CardinalBrain ? 3 : -1;
                    if (bossFloor >= 0 && depth != bossFloor) continue;
                    float att = 20f + 4f * depth;
                    float swing = SwingDamage * (0.5f + att / 50f);
                    float hp = b.BaseHealth * (b is BossBrain ? Difficulty.BossHealth(1) : Difficulty.EnemyHealth(1)) * Difficulty.DepthHealth(depth);
                    float perSwing = Mathf.Max(swing - b.BaseDefense, swing * 0.15f);
                    float hit = b.BaseDamage * Difficulty.Damage(1) * Difficulty.DepthDamage(depth);
                    float taken = Mathf.Max(hit - PlayerDef, hit * 0.15f);
                    var row = new Row
                    {
                        Name = b.name,
                        Depth = depth,
                        Hp = hp,
                        Hit = hit,
                        SwingsToKill = hp / perSwing,
                        SecondsToKill = hp / perSwing * SwingCycle,
                        HitsToDown = taken > 0f ? PlayerHp * (1f + 0.25f * depth) / taken : 999f,
                        EnemyDps = taken / Mathf.Max(0.5f, b.AttackCycle),
                    };
                    // The norms: a rank-and-file enemy dies in 2-12 swings and needs 4+ hits to down you;
                    // a heavy (Brute, champion-worthy) up to 25 swings; a boss, on its own floor, 50-160.
                    bool boss = b is BossBrain;
                    bool heavy = b is BruteBrain || b is GargoyleBrain || b is MimicBrain;
                    float maxSwings = boss ? 160f : heavy ? 25f : 12f, minSwings = boss ? 50f : 1.6f;
                    if (row.SwingsToKill > maxSwings) row.Flag = "TANKY";
                    else if (row.SwingsToKill < minSwings) row.Flag = "FRAGILE";
                    if (b.BaseDamage > 0f && row.HitsToDown < (boss ? 3f : 4f)) row.Flag = (row.Flag != null ? row.Flag + "+" : "") + "BRUTAL";
                    rows.Add(row);
                }
            }
            return rows;
        }

        public static string Format(List<Row> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Balance] enemy | floor | hp | hit | swings to kill | seconds | hits to down a knight | dps vs knight | flag");
            foreach (var r in rows)
                sb.AppendLine("[Balance] " + r.Name + " | " + (r.Depth + 1) + " | " + r.Hp.ToString("F0") + " | " + r.Hit.ToString("F0") + " | " +
                    r.SwingsToKill.ToString("F1") + " | " + r.SecondsToKill.ToString("F1") + " | " + r.HitsToDown.ToString("F1") + " | " +
                    r.EnemyDps.ToString("F1") + " | " + (r.Flag ?? "ok"));
            return sb.ToString();
        }
    }
}
