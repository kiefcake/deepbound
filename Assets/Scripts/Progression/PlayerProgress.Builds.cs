using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Core;
using Deepbound.Loot;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Progression
{
    public partial class PlayerProgress
    {
        public readonly NetworkVariable<byte> Picks = new NetworkVariable<byte>();
        public const int PicksPerSet = 3;

        public readonly NetworkVariable<bool> Lantern = new NetworkVariable<bool>();

        public readonly NetworkVariable<float> ShieldCharge = new NetworkVariable<float>();

        public readonly NetworkVariable<byte> BloodOffered = new NetworkVariable<byte>();

        public void ServerOfferBlood()
        {
            if (!IsServer) return;
            BloodOffered.Value++;
            ApplyToHealth(false);
        }

        public readonly NetworkVariable<byte> SurvivalCut = new NetworkVariable<byte>();

        public bool CanDisarm => ClassId.Value == Classes.ClassId.Rogue || Picks.Value > 0;

        public struct GearSet { public string Name, Bonus; public GearId Armour, Charm; }
        public static readonly GearSet[] Sets =
        {
            new GearSet { Name = "The Unbroken Wall", Bonus = "12% less damage taken", Armour = GearId.BulwarkPlate, Charm = GearId.StoneheartAmulet },
            new GearSet { Name = "The Red Hunger", Bonus = "+10% damage", Armour = GearId.BerserkersHarness, Charm = GearId.GlassHeart },
            new GearSet { Name = "The Long Road", Bonus = "+8% speed", Armour = GearId.PilgrimsMail, Charm = GearId.DuelistsSash },
            new GearSet { Name = "The Bone Choir", Bonus = "potions mend 25% more", Armour = GearId.OssuaryPlate, Charm = GearId.RosaryOfAsh },
            new GearSet { Name = "The Grave Trade", Bonus = "8% shorter cooldowns", Armour = GearId.GravediggersCoat, Charm = GearId.WitchboneFetish },
        };
        public const float SetWallTaken = 0.88f, SetHungerDamage = 1.1f, SetRoadSpeed = 1.08f, SetChoirPotion = 1.25f, SetGraveCooldown = 0.92f;

        public int WornSet
        {
            get
            {
                for (int i = 0; i < Sets.Length; i++) if (Wears(Sets[i].Armour) && Wears(Sets[i].Charm)) return i;
                return -1;
            }
        }

        public static string SetLine(GearId id)
        {
            foreach (var s in Sets)
                if (s.Armour == id || s.Charm == id)
                    return "SET " + s.Name + " (" + Gear.BaseName(s.Armour) + " + " + Gear.BaseName(s.Charm) + "): " + s.Bonus;
            return null;
        }

        public const int MasteryDrills = 6;
        public bool Mastered(StatType s) => DrillCount(s) >= MasteryDrills;

        public static string MasteryText(StatType s) => s switch
        {
            StatType.HP => "HP MASTERY: +5% max health",
            StatType.VIT => "VIT MASTERY: health mends 60% faster",
            StatType.DEF => "DEF MASTERY: blocking costs 30% less stamina, 5% less damage taken",
            StatType.ATT => "ATT MASTERY: +5% damage",
            StatType.DEX => "DEX MASTERY: +4% crit chance",
            StatType.MP => "MP MASTERY: mana returns 25% faster",
            StatType.WIS => "WIS MASTERY: 6% shorter cooldowns",
            _ => null,
        };

        public enum Bargain : byte { BloodForSteel = 0, GlassStride = 1, MisersOath = 2, HollowVigil = 3, BitterRoot = 4, EmberTithe = 5, GravePact = 6, ThornCrown = 7 }
        public const int BargainCount = 8;

        public const float GravePactHeal = 0.03f;

        public const float ThornCrownBack = 0.5f, ThornCrownReach = 6f, ThornCrownMendCut = 0.3f;
        public readonly NetworkVariable<ushort> Bargains = new NetworkVariable<ushort>();
        public bool HasBargain(Bargain b) => (Bargains.Value & (1 << (int)b)) != 0;

        public float CurseScale => ClassId.Value == Classes.ClassId.Cleric ? 0.5f : 1f;

        public static string BargainName(Bargain b) => b switch
        {
            Bargain.BloodForSteel => "Blood for Steel",
            Bargain.GlassStride => "The Glass Stride",
            Bargain.MisersOath => "The Miser's Oath",
            Bargain.HollowVigil => "The Hollow Vigil",
            Bargain.BitterRoot => "The Bitter Root",
            Bargain.GravePact => "The Gravedigger's Pact",
            Bargain.ThornCrown => "The Thorned Crown",
            _ => "The Ember Tithe",
        };

        public static string BargainText(Bargain b) => b switch
        {
            Bargain.BloodForSteel => "+15% damage for the run, -15% max health",
            Bargain.GlassStride => "+12% speed for the run, +15% damage taken",
            Bargain.MisersOath => "+40% gold for the run, potions mend 25% less",
            Bargain.HollowVigil => "+8% crit chance for the run, -6 DEF",
            Bargain.BitterRoot => "15% shorter cooldowns for the run, -10% max health",
            Bargain.GravePact => "every kill mends 3% of your health for the run; health potions mend nothing",
            Bargain.ThornCrown => "half of every blow on you goes back into the nearest foe; potions and VIT mend 30% less",
            _ => "+50% embers for the run, -10% damage",
        };

        public bool ServerStrikeBargain(Bargain b)
        {
            if (!IsServer || HasBargain(b)) return false;
            Bargains.Value |= (ushort)(1 << (int)b);
            if (b == Bargain.HollowVigil) ServerAddBonus(StatType.DEF, -6f * CurseScale);
            ApplyToHealth(false);
            return true;
        }

        public static string ClassGift(ClassId c) => c switch
        {
            Classes.ClassId.Knight => "SHIELD-BORN: the shield up halves spike and flame traps; a raised shield stops the Gravewarden's charge",
            Classes.ClassId.Mage => "ARCANE SENSE: secret walls within 12 m show on your map",
            Classes.ClassId.Cleric => "FAITH: a bargain's price is halved",
            Classes.ClassId.Rogue => "LIGHT FINGERS: kneel on a spike plate to jam it (no tool), and hidden plates glint near you",
            Classes.ClassId.Ranger => "TRACKER: your map shows the dead 60% further out",
            Classes.ClassId.Flagellant => "SCARRED HIDE: traps bite 30% less",
            _ => "APOTHECARY: potions mend 25% more",
        };

        public float BuildAttackScale
        {
            get
            {
                float m = 1f;
                if (WornSet == 1) m *= SetHungerDamage;
                if (Mastered(StatType.ATT)) m *= 1.05f;
                if (HasBargain(Bargain.BloodForSteel)) m *= 1.15f;
                if (HasBargain(Bargain.EmberTithe)) m *= 1f - 0.1f * CurseScale;
                m *= 1f + World.BloodAltar.OfferDamage * BloodOffered.Value;
                return m;
            }
        }

        public float BuildSpeedScale => (WornSet == 2 ? SetRoadSpeed : 1f) * (HasBargain(Bargain.GlassStride) ? 1.12f : 1f);

        public float BuildCooldownCut => (WornSet == 4 ? 1f - SetGraveCooldown : 0f) + (Mastered(StatType.WIS) ? 0.06f : 0f);

        public float BuildCooldownScale => HasBargain(Bargain.BitterRoot) ? 0.85f : 1f;

        public float ManaRegenScale => Mastered(StatType.MP) ? 1.25f : 1f;

        public float BuildGoldBonus => HasBargain(Bargain.MisersOath) ? 0.4f : 0f;
        public float BuildEmberScale => HasBargain(Bargain.EmberTithe) ? 1.5f : 1f;

        public float BuildHealthScale => (1f - World.BloodAltar.OfferFraction * BloodOffered.Value) * (Mastered(StatType.HP) ? 1.05f : 1f) * (HasBargain(Bargain.BloodForSteel) ? 1f - 0.15f * CurseScale : 1f)
            * (HasBargain(Bargain.BitterRoot) ? 1f - 0.1f * CurseScale : 1f);

        public float BuildIncomingScale => (WornSet == 0 ? SetWallTaken : 1f) * (Mastered(StatType.DEF) ? 0.95f : 1f)
            * (HasBargain(Bargain.GlassStride) ? 1f + 0.15f * CurseScale : 1f);

        public float BuildCritBonus => (Mastered(StatType.DEX) ? 0.04f : 0f) + (HasBargain(Bargain.HollowVigil) ? 0.08f : 0f);

        public float BuildPotionScale => (WornSet == 3 ? SetChoirPotion : 1f) * (ClassId.Value == Classes.ClassId.PlagueDoctor ? 1.25f : 1f)
            * (HasBargain(Bargain.MisersOath) ? 1f - 0.25f * CurseScale : 1f)
            * (HasBargain(Bargain.GravePact) ? (ClassId.Value == Classes.ClassId.Cleric ? 0.5f : 0f) : 1f)
            * (HasBargain(Bargain.ThornCrown) ? 1f - ThornCrownMendCut * CurseScale : 1f);

        public float BuildRegenScale => (Mastered(StatType.VIT) ? 1.6f : 1f) * (HasBargain(Bargain.ThornCrown) ? 1f - ThornCrownMendCut * CurseScale : 1f);

        private static bool bargainHooked;

        public static void EnsureBargainHook()
        {
            if (bargainHooked) return;
            bargainHooked = true;
            Health.ServerAnyDamaged += (target, lost, attackerId) =>
            {
                if (target == null || target.Team != Team.Enemies || target.IsAlive || lost <= 0f) return;
                var nm = Unity.Netcode.NetworkManager.Singleton;
                if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj) || !obj.TryGetComponent<PlayerProgress>(out var p)) return;
                if (!p.HasBargain(Bargain.GravePact)) return;
                var h = p.GetComponent<Health>();
                if (h != null && h.IsAlive) h.ServerHeal(h.Max.Value * GravePactHeal);
            };
        }

        public float ServerThornCrown(float damage, Vector3 attackerPosition)
        {
            if (!HasBargain(Bargain.ThornCrown) || damage <= 0f) return 0f;
            Health nearest = null;
            float best = ThornCrownReach;
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                float d = Vector3.Distance(h.transform.position, attackerPosition);
                if (d < best) { best = d; nearest = h; }
            }
            if (nearest == null) return 0f;
            float back = damage * ThornCrownBack;
            nearest.Tag("ThornCrown").ServerDamage(back, transform.position, NetworkObjectId);
            return back;
        }
        public float BuildBlockCostScale => Mastered(StatType.DEF) ? 0.7f : 1f;

        public float TrapScale
        {
            get
            {
                if (ClassId.Value == Classes.ClassId.Flagellant) return 0.7f;
                if (ClassId.Value == Classes.ClassId.Knight && TryGetComponent<Player.KnightKit>(out var k) && k.ShieldRaised) return 0.5f;
                return 1f;
            }
        }

        private void ResetBuildRun()
        {
            Picks.Value = 0;
            Lantern.Value = false;
            Bargains.Value = 0;
            BloodOffered.Value = 0;
            ApplyToHealth(false);
        }

        private void CheckMastery(StatType s)
        {
            if (DrillCount(s) != MasteryDrills) return;
            var text = MasteryText(s);
            if (text != null) MasteryRpc(text);
            ApplyToHealth(false);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MasteryRpc(string text)
        {
            UI.GameHud.ShowTitleCard("BRANN'S MASTERY", text, new Color(1f, 0.85f, 0.45f));
            UI.GameHud.Feed(name + " earns " + text, new Color(1f, 0.85f, 0.45f));
        }
    }
}
