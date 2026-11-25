using System.Collections.Generic;
using Deepbound.Classes;
using Deepbound.Core;
using Deepbound.Player;
using UnityEngine;

namespace Deepbound.Progression
{
    public static class DamageSheet
    {
        public struct Line
        {
            public string Key, Name, Unit;
            public float Now, Start;
        }

        public static float StartMultiplier(PlayerProgress p) => 0.5f + p.Class.BaseStats[(int)StatType.ATT] / 50f;

        public static List<Line> For(PlayerProgress p)
        {
            var lines = new List<Line>();
            if (p == null || p.Class == null) return lines;
            float att = p.AttackMultiplier, att0 = StartMultiplier(p);
            float q = p.Potency(AbilitySlot.Q), e = p.Potency(AbilitySlot.E);
            void Add(string key, string name, float baseDamage, float potency = 1f, string unit = "")
                => lines.Add(new Line { Key = key, Name = name, Unit = unit, Now = baseDamage * att * potency, Start = baseDamage * att0 });
            var cls = p.ClassId.Value;
            switch (cls)
            {
                case ClassId.Knight:
                    Add("LMB", "Sword Swing", KnightKit.SwingDamage);
                    Add("Q", "Shield Bash", KnightKit.BashDamage, q);
                    Add("", "Riposte", KnightKit.SwingDamage * KnightKit.RiposteDamage);
                    break;
                case ClassId.Mage:
                    Add("LMB", "Firebolt", MageKit.FireboltDamage);
                    Add("RMB", "Frostbolt", MageKit.FrostboltDamage);
                    Add("Q", "Tidal Wave", MageKit.WaveDamage, q);
                    Add("E", "Meteor", MageKit.MeteorDamage, e);
                    Add("R", "Cataclysm", MageKit.MeteorDamage * 0.85f, 1f, " a meteor");
                    break;
                case ClassId.Cleric:
                    Add("LMB", "Smite", ClericKit.SmiteDamage);
                    Add("Q", "Consecrate", ClericKit.ConsecrateBurnPerSec, q, " /s");
                    Add("R", "Dawnbreak", Ultimates.DawnBurn * 2f, 1f, " /s");
                    break;
                case ClassId.Rogue:
                    Add("LMB", "Twin Daggers", RogueKit.StabDamage);
                    Add("", "From behind", RogueKit.StabDamage * RogueKit.AmbushMultiplier);
                    Add("RMB", "Fan of Knives", RogueKit.FanDamage, 1f, " a knife");
                    Add("R", "Death from Shadow", 150f);
                    break;
                case ClassId.Ranger:
                    Add("LMB", "Longbow", RangerKit.ArrowMaxDamage, 1f, " full draw");
                    Add("", "Deadeye", RangerKit.ArrowMaxDamage * RangerKit.DeadeyeDamage);
                    Add("Q", "Snare Shot", RangerKit.SnareDamage, q);
                    Add("E", "Volley", RangerKit.VolleyDamage, e, " an arrow");
                    Add("R", "Twin Wyrms", Ultimates.WyrmBite, 1f, " bite");
                    break;
                case ClassId.Flagellant:
                    Add("LMB", "Flail", FlagellantKit.SwingDamage);
                    Add("Q", "Chains of Penance", FlagellantKit.ChainDamage, q);
                    Add("E", "Penitent Leap", FlagellantKit.LeapDamage, e);
                    Add("R", "The Red Litany", FlagellantKit.SwingDamage * FlagellantKit.LitanyShare, 1f, " a strike");
                    break;
                case ClassId.PlagueDoctor:
                    Add("LMB", "Vial", PlagueDoctorKit.VialDamage);
                    Add("", "Vial, 4 stacks", PlagueDoctorKit.VialDamage * (1f + PlagueDoctorKit.StackBonus * Combat.Health.MaxBlightStacks));
                    Add("RMB", "Expunge", PlagueDoctorKit.ExpungePerStack, 1f, " a stack");
                    Add("Q", "Miasma", PlagueDoctorKit.MiasmaDps, q, " /s");
                    Add("R", "Patient Zero", PlagueDoctorKit.ZeroBurst, 1f, " burst");
                    break;
            }
            return lines;
        }

        public static float Main(PlayerProgress p)
        {
            var l = For(p);
            return l.Count > 0 ? l[0].Now : 0f;
        }
    }
}
