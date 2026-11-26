using Deepbound.Art;
using System;
using System.Linq;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Core;
using Deepbound.Loot;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Progression
{
    [RequireComponent(typeof(Health))]
    public partial class PlayerProgress : NetworkBehaviour
    {
        public readonly NetworkVariable<int> Gold = new NetworkVariable<int>();

        public readonly NetworkVariable<int> Embers = new NetworkVariable<int>();

        public readonly NetworkVariable<int> WeaponLevel = new NetworkVariable<int>();

        public readonly NetworkVariable<int> Sigils = new NetworkVariable<int>();

        public readonly NetworkVariable<bool> CatPeed = new NetworkVariable<bool>();

        public readonly NetworkVariable<bool> CatWhisker = new NetworkVariable<bool>();

        public readonly NetworkVariable<int> Grudges = new NetworkVariable<int>();

        private System.Collections.IEnumerator AddPrayerLast()
        {
            yield return null;
            yield return null;
            if (health != null) health.IncomingModifiers.Add(PrayerModifier);
        }

        public readonly NetworkVariable<bool> PrayedFor = new NetworkVariable<bool>();

        public readonly NetworkVariable<ulong> Drills = new NetworkVariable<ulong>();
        public int DrillCount(StatType s) => (int)((Drills.Value >> (4 * (int)s)) & 0xF);

        public readonly NetworkVariable<bool> ScoutMap = new NetworkVariable<bool>();
        public readonly NetworkVariable<bool> MapRevealed = new NetworkVariable<bool>();

        public readonly NetworkVariable<bool> Salts = new NetworkVariable<bool>();

        public readonly NetworkVariable<bool> Rope = new NetworkVariable<bool>();

        public const float RopeRange = 3.2f;

        public bool CanUseRope => Rope.Value && World.DungeonDirector.Instance != null && World.DungeonDirector.Instance.CanRetreat && World.DungeonDirector.Layout != null
            && Vector3.Distance(transform.position, World.DungeonDirector.Layout.WaystonePosition) <= RopeRange;

        [Rpc(SendTo.Server)]
        public void UseRopeRpc()
        {
            RunLog.Note("action", "UseRope", this);
            if (!CanUseRope) return;
            Rope.Value = false;
            World.DungeonDirector.Instance.ServerRetreat(name);
        }

        public readonly NetworkVariable<byte> StockRerolls = new NetworkVariable<byte>();

        private float PrayerModifier(float dealt, Vector3 from)
        {
            if (!PrayedFor.Value || health == null || dealt - health.Shield.Value < health.Current.Value) return dealt;
            PrayedFor.Value = false;
            PrayerHeldRpc(name);
            return Mathf.Max(0f, health.Current.Value - 1f + health.Shield.Value);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PrayerHeldRpc(string who)
        {
            var at = transform.position + Vector3.up * 1.4f;
            Art.Vfx.Flash(at, new Color(1f, 0.88f, 0.55f), 6f, 6f, 0.5f);
            Art.Vfx.Ring(transform.position + Vector3.up * 0.1f, 2.2f, new Color(1f, 0.88f, 0.55f), 0.6f);
            Art.Vfx.Embers(at, 20, new Color(1f, 0.85f, 0.5f), 3f, 0.05f, 1.2f);
            Art.Sfx.Play(Art.Sound.Relic, at, 1f, 1.2f);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.5f, "THE PRAYER HOLDS", new Color(1f, 0.88f, 0.55f));
            UI.GameHud.Feed("Brother Aldric's prayer holds: " + who + " is still standing (at 1 health). Money well spent.", new Color(1f, 0.85f, 0.55f));
        }
        public const int MaxGrudge = 4;

        public int Grudge(World.FolkId who) => (Grudges.Value >> ((int)who * 3)) & 7;

        private void SetGrudge(World.FolkId who, int level)
        {
            int shift = (int)who * 3;
            Grudges.Value = (Grudges.Value & ~(7 << shift)) | (Mathf.Clamp(level, 0, MaxGrudge) << shift);
        }

        public int ServerAddGrudge(World.FolkId who)
        {
            int level = Mathf.Min(MaxGrudge, Grudge(who) + 1);
            SetGrudge(who, level);
            return level;
        }

        public void ServerEaseGrudges()
        {
            for (int i = 0; i <= (int)World.FolkId.Peddler; i++) SetGrudge((World.FolkId)i, Grudge((World.FolkId)i) - 1);
        }

        public float PriceMultiplier(Shop.Vendor vendor) => (1f + 0.25f * Grudge(VendorFolk(vendor))) * World.DungeonDirector.OmenPrices
            * (World.DungeonDirector.InLobby ? World.DungeonDirector.FavourPriceScale(VendorFolk(vendor)) : 1f)
            * (HasRelic(Relic.PilgrimsPurse) && World.DungeonDirector.InLobby ? Mathf.Max(0.55f, 1f - 0.15f * RelicPower(Relic.PilgrimsPurse)) : 1f);

        public static World.FolkId VendorFolk(Shop.Vendor vendor) => vendor == Shop.Vendor.Smith ? World.FolkId.Hobb : vendor == Shop.Vendor.Shrine ? World.FolkId.Aldric
            : vendor == Shop.Vendor.Carriage ? World.FolkId.Corwin : vendor == Shop.Vendor.Yard ? World.FolkId.Brann : vendor == Shop.Vendor.Runewright ? World.FolkId.Ysolde
            : World.DungeonDirector.InLobby ? World.FolkId.Silas : World.FolkId.Peddler;

        [Rpc(SendTo.Server)]
        public void TalkHoldRpc(byte who, bool on)
        {
            var d = World.DungeonDirector.Instance;
            if (d == null) return;
            if (on && (!World.DungeonDirector.InLobby || !World.FolkPlaces.InReach((World.FolkId)who, transform.position, 2f))) return;
            d.ServerHoldFolk((World.FolkId)who, OwnerClientId, on);
        }

        [Rpc(SendTo.Server)]
        public void TakeQuestRpc(byte who)
        {
            RunLog.Note("action", "TakeQuest" + " " + who, this);
            var d = World.DungeonDirector.Instance;
            if (d == null || !World.DungeonDirector.InLobby || !World.FolkPlaces.InReach((World.FolkId)who, transform.position, 2f)) return;
            if (Grudge((World.FolkId)who) >= 3) return;
            d.ServerTakeQuest((World.FolkId)who, name);
        }

        public int AmendsCost(Shop.Vendor vendor)
        {
            return Grudge(VendorFolk(vendor)) * 30;
        }

        [Rpc(SendTo.Server)]
        public void AmendsRpc(Shop.Vendor vendor)
        {
            RunLog.Note("action", "Amends" + " " + vendor, this);
            int cost = AmendsCost(vendor);
            if (cost <= 0 || Gold.Value < cost || !Shop.InReach(vendor, transform.position, 1f)) return;
            Gold.Value -= cost;
            var who = VendorFolk(vendor);
            SetGrudge(who, 0);
            AmendsMadeRpc((byte)who);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AmendsMadeRpc(byte who) => UI.CampChatter.Forgiven((World.FolkId)who, name);

        public static PlayerProgress ForClient(ulong clientId)
        {
            foreach (var life in Player.PlayerLife.All)
                if (life != null && life.OwnerClientId == clientId && life.TryGetComponent<PlayerProgress>(out var p)) return p;
            return null;
        }
        public const float CatPeeHealth = 0.95f, WhiskerEvade = 0.01f;

        public void ServerCatVerdict(bool petted)
        {
            if (petted && !CatWhisker.Value) CatWhisker.Value = true;
        }
        public readonly NetworkVariable<byte> RankQ = new NetworkVariable<byte>(1);
        public readonly NetworkVariable<byte> RankE = new NetworkVariable<byte>(1);

        public readonly NetworkVariable<byte> RankChoice = new NetworkVariable<byte>();

        public int Pick(AbilitySlot slot, int tier) => tier < 1 || Rank(slot) < tier + 1 ? 0 : ((RankChoice.Value >> ((int)slot * 4 + tier - 1)) & 1) + 1;

        public bool A(AbilitySlot slot, int tier) => Pick(slot, tier) == 1;

        public bool B(AbilitySlot slot, int tier) => Pick(slot, tier) == 2;

        public int Rank(AbilitySlot slot) => Mathf.Min(AbilityRanks.MaxRank, (slot == AbilitySlot.Q ? RankQ.Value : RankE.Value) + (Wears(GearId.HollowCrown) ? 1 : 0));

        public int BoughtRank(AbilitySlot slot) => slot == AbilitySlot.Q ? RankQ.Value : RankE.Value;

        public float Potency(AbilitySlot slot) => 1f + AbilityRanks.PotencyPerRank * (Rank(slot) - 1);

        public float CooldownFor(AbilitySlot slot) => CooldownMultiplier * (1f - AbilityRanks.CooldownPerRank * (Rank(slot) - 1));

        public void ServerAddSigil()
        {
            if (!IsServer) return;
            Sigils.Value++;
            SigilRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SigilRpc() => UI.GameHud.Feed(name + " found a Sigil  -  rank an ability at Ysolde's stones", LootInfo.SigilColor);

        [Rpc(SendTo.Server)]
        public void EmpowerRpc(byte slot, bool optionB) => ServerEmpowerAbility(slot, optionB);

        public bool ServerEmpowerAbility(byte slot, bool optionB)
        {
            if (!AtRunewright || slot > 1) return false;
            var rank = slot == 0 ? RankQ : RankE;
            if (rank.Value >= AbilityRanks.MaxRank || Rank((AbilitySlot)slot) >= AbilityRanks.MaxRank) return false;
            int cost = AbilityRanks.SigilCost(Rank((AbilitySlot)slot));
            if (Sigils.Value < cost) return false;
            Sigils.Value -= cost;
            rank.Value++;
            int tier = Rank((AbilitySlot)slot) - 1;
            int bit = 1 << (slot * 4 + tier - 1);
            RankChoice.Value = (byte)(optionB ? RankChoice.Value | bit : RankChoice.Value & ~bit);
            MeldedFxRpc();
            EmpoweredRpc(slot, rank.Value, (byte)tier, optionB);
            return true;
        }

        [Rpc(SendTo.Server)]
        public void MeltSigilRpc()
        {
            RunLog.Note("action", "MeltSigil", this);
            if (!AtRunewright || Sigils.Value < 1 || Rank(AbilitySlot.Q) < AbilityRanks.MaxRank || Rank(AbilitySlot.E) < AbilityRanks.MaxRank) return;
            Sigils.Value--;
            Embers.Value += AbilityRanks.MeltEmbers;
            MeldedFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EmpoweredRpc(byte slot, byte rank, byte tier, bool optionB)
        {
            string ability = ClassDefinition.AbilityName(ClassId.Value, (AbilitySlot)slot);
            var opt = AbilityRanks.Get(ClassId.Value, (AbilitySlot)slot, tier, optionB);
            UI.GameHud.Feed(name + "'s " + ability + " reaches rank " + rank + ": " + opt.Name.ToUpperInvariant(), LootInfo.SigilColor);
        }

        public readonly NetworkVariable<byte> WeaponPath = new NetworkVariable<byte>();

        public bool OnPath(int path) => WeaponPath.Value == path;

        public readonly NetworkVariable<int> EdgeBits = new NetworkVariable<int>();

        public readonly NetworkVariable<byte> Hones = new NetworkVariable<byte>();

        public float ServerMomentumUntil;

        public Edge EdgeAt(int slot) => (Edge)((EdgeBits.Value >> (8 * slot)) & 0xFF);

        public bool HasEdge(Edge e)
        {
            for (int s = 0; s < Edges.Slots; s++) if (EdgeAt(s) == e) return true;
            return false;
        }

        public int OpenEdgeSlot
        {
            get
            {
                for (int s = 0; s < Edges.Slots; s++)
                    if (WeaponLevel.Value >= Edges.Levels[s] && EdgeAt(s) == Edge.None) return s;
                return -1;
            }
        }

        private int RunSeed => World.DungeonDirector.Instance != null ? (int)(World.DungeonDirector.Instance.RunStartedAt.Value * 10.0) : 0;

        public Edge[] EdgeOffer(int slot) => Edges.Offer(RunSeed, OwnerClientId, slot, EdgeBits.Value);

        [Rpc(SendTo.Server)]
        public void ChooseEdgeRpc(byte edge)
        {
            RunLog.Note("action", "ChooseEdge" + " " + edge, this);
            if (World.Anvil.Near(transform.position) == null) return;
            ServerChooseEdge((Edge)edge);
        }

        public bool ServerChooseEdge(Edge e)
        {
            int slot = OpenEdgeSlot;
            if (!IsServer || slot < 0 || System.Array.IndexOf(EdgeOffer(slot), e) < 0) return false;
            EdgeBits.Value |= (int)e << (8 * slot);

            if (e == Edge.Bastion) ServerAddBonus(StatType.HP, Edges.BastionHP);
            if (e == Edge.Ironhide) ServerAddBonus(StatType.DEF, Edges.IronhideDEF);
            EdgeChosenRpc((byte)e);
            return true;
        }

        public readonly NetworkVariable<bool> EdgeRespecced = new NetworkVariable<bool>();
        public int RespecCost => 150 + 50 * World.DungeonDirector.Act(Mathf.Max(0, World.DungeonDirector.Instance != null ? World.DungeonDirector.Instance.NextDepth.Value : 0));

        public string CannotRespecReason(int slot)
        {
            if (EdgeRespecced.Value) return "Once a run";
            if (slot < 0 || slot >= Edges.Slots || EdgeAt(slot) == Edge.None) return "No edge there";
            if (Gold.Value < RespecCost) return "Not enough gold";
            return null;
        }

        [Rpc(SendTo.Server)]
        public void RespecEdgeRpc(byte slot)
        {
            RunLog.Note("action", "RespecEdge" + " " + slot, this);
            if (World.Anvil.Near(transform.position) == null) return;
            ServerRespecEdge(slot);
        }

        public bool ServerRespecEdge(int slot)
        {
            if (!IsServer || CannotRespecReason(slot) != null) return false;
            var e = EdgeAt(slot);
            ServerAddGold(-RespecCost);
            if (e == Edge.Bastion) ServerAddBonus(StatType.HP, -Edges.BastionHP);
            if (e == Edge.Ironhide) ServerAddBonus(StatType.DEF, -Edges.IronhideDEF);
            EdgeBits.Value &= ~(0xFF << (8 * slot));
            EdgeRespecced.Value = true;
            RespecRpc((byte)e);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RespecRpc(byte e)
        {
            Art.Sfx.Play(Art.Sound.Shatter, transform.position + Vector3.up, 0.6f, 1.2f);
            UI.GameHud.Feed(name + " has Hobb grind the " + Edges.Name((Edge)e).ToUpperInvariant() + " edge out: choose again", new Color(1f, 0.7f, 0.4f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EdgeChosenRpc(byte e)
        {
            Art.Sfx.Play(Art.Sound.Relic, transform.position + Vector3.up, 0.7f, 1.05f);
            UI.GameHud.Feed(name + " forges the " + Edges.Name((Edge)e).ToUpperInvariant() + " edge: " + Edges.Describe((Edge)e), Edges.Color((Edge)e));
            if (IsOwner && EdgeAt(Edges.Slots - 1) != Edge.None) Deeds.Earn(Deeds.Deed.ThreeEdges, "Three edges forged");
        }

        public string CannotHoneReason()
        {
            if (WeaponLevel.Value < World.Anvil.MaxLevel) return "Temper to +" + World.Anvil.MaxLevel + " first";
            if (Hones.Value >= World.Anvil.MaxHones) return "Honed to the limit";
            if (OpenEdgeSlot >= 0) return "Choose an edge first";
            if (Embers.Value < World.Anvil.HoneEmbers(Hones.Value)) return "Not enough embers";
            if (Gold.Value < World.Anvil.HoneGold(Hones.Value)) return "Not enough gold";
            return null;
        }

        [Rpc(SendTo.Server)]
        public void HoneRpc()
        {
            RunLog.Note("action", "Hone", this);
            var anvil = World.Anvil.Near(transform.position);
            if (anvil != null && ServerHone()) anvil.WorkFxRpc(World.Anvil.Work.Grindstone, 1f);
        }

        public bool ServerHone()
        {
            if (!IsServer || CannotHoneReason() != null) return false;
            Embers.Value -= World.Anvil.HoneEmbers(Hones.Value);
            Gold.Value -= World.Anvil.HoneGold(Hones.Value);
            Hones.Value++;
            HonedRpc(Hones.Value);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HonedRpc(byte hones)
        {
            if (IsOwner && hones >= World.Anvil.MaxHones) Deeds.Earn(Deeds.Deed.FullyHoned, "Honed to the limit");
            UI.GameHud.Feed(name + " hones their " + ClassDefinition.WeaponName(ClassId.Value) + " (" + hones + "/" + World.Anvil.MaxHones + "): +" + Mathf.RoundToInt(hones * World.Anvil.HoneDamage * 100f) + "% damage", LootInfo.EmberColor);
        }

        public readonly NetworkVariable<byte> ActiveBlessing = new NetworkVariable<byte>();
        public bool Blessed(Blessing b) => ActiveBlessing.Value == (byte)b;

        public readonly NetworkVariable<byte> ShrineRerolls = new NetworkVariable<byte>();

        public void ServerBless(Blessing b)
        {
            if (!IsServer) return;
            ActiveBlessing.Value = (byte)b;

            if (b == Blessing.Fortune) ServerAddSigil();
            OnStatsChangedServer(StatType.HP);
        }

        public void ServerClearBlessing()
        {
            if (!IsServer || ActiveBlessing.Value == 0) return;
            ActiveBlessing.Value = 0;
            ApplyToHealth(refill: false);
        }

        public readonly NetworkVariable<GearItem> Armor = new NetworkVariable<GearItem>();
        public readonly NetworkVariable<GearItem> Charm = new NetworkVariable<GearItem>();

        public GearItem Equipped(GearSlot slot) => slot == GearSlot.Armor ? Armor.Value : Charm.Value;

        public const int BagSize = 8;
        private NetworkList<GearItem> bag;
        public int BagCount => bag.Count;
        public GearItem BagItem(int i) => i >= 0 && i < bag.Count ? bag[i] : default;
        public bool BagFull => bag.Count >= BagSize;

        public bool ServerStow(GearItem item)
        {
            if (!IsServer || item.IsEmpty || bag.Count >= BagSize) return false;
            bag.Add(item);
            return true;
        }

        private bool CanManagePack(int i) => IsServer && i >= 0 && i < bag.Count && !GetComponent<Player.PlayerLife>().Downed.Value;

        private bool AtAnvil => World.Anvil.Near(transform.position) != null;

        [Rpc(SendTo.Server)]
        public void BagEquipRpc(int i)
        {
            RunLog.Note("action", "BagEquip" + " " + i, this);
            if (!CanManagePack(i)) return;
            var item = bag[i];
            bag.RemoveAt(i);
            var old = ServerEquip(item);
            if (!old.IsEmpty) bag.Insert(Mathf.Min(i, bag.Count), old);
        }

        [Rpc(SendTo.Server)]
        public void BagScrapRpc(int i)
        {
            RunLog.Note("action", "BagScrap" + " " + i, this);
            if (!CanManagePack(i) || !AtAnvil) return;
            var item = bag[i];
            bag.RemoveAt(i);
            ServerAddEmbers(Gear.ScrapValue(item));
            ServerAddPart(Part.Iron, 1 + item.Quality);
            PackFxRpc(0);
            var smithy = World.Anvil.Near(transform.position);
            if (smithy != null) smithy.WorkFxRpc(World.Anvil.Work.Hearth, 1f);
        }

        [Rpc(SendTo.Server)]
        public void BagDropRpc(int i)
        {
            RunLog.Note("action", "BagDrop" + " " + i, this);
            if (!CanManagePack(i)) return;
            var item = bag[i];
            bag.RemoveAt(i);

            GearDrop.ServerSpawn(item, GearDrop.Anyone, transform.position + transform.forward * 1.2f + Vector3.up * 0.05f);
        }

        [Rpc(SendTo.Server)]
        public void UnequipRpc(byte slot)
        {
            RunLog.Note("action", "Unequip" + " " + slot, this);
            if (!IsServer || slot > 1 || GetComponent<Player.PlayerLife>().Downed.Value || bag.Count >= BagSize) return;
            var item = Equipped((GearSlot)slot);
            if (item.IsEmpty) return;
            ServerUnequip((GearSlot)slot);
            bag.Add(item);
        }

        public void ServerUnequip(GearSlot which)
        {
            var slot = which == GearSlot.Armor ? Armor : Charm;
            var old = slot.Value;
            foreach (var s in Gear.StatsOf(old)) Stats.AddEquipmentBonus(s, -Gear.StatValue(old, s));
            slot.Value = default;
            health.HealMultiplier = (Wears(GearId.TidewovenRobe) ? 1.3f : 1f) * (Wears(GearId.MartyrsShell) ? MartyrHeal : 1f);
            OnStatsChangedServer(null);
        }

        [Rpc(SendTo.Owner)]
        private void PackFxRpc(byte kind) => Sfx.Play2D(kind == 0 ? Sound.Shatter : Sound.Purchase, 0.5f);
        public bool Wears(GearId id) => Armor.Value.Kind == id || Charm.Value.Kind == id;

        private float dodgeStrikeUntil, shroudReadyAt, bulwarkReadyAt;

        public GearItem ServerEquip(GearItem item)
        {
            if (!IsServer || item.IsEmpty) return default;
            var slot = Gear.SlotOf(item.Kind) == GearSlot.Armor ? Armor : Charm;
            var old = slot.Value;
            foreach (var s in Gear.StatsOf(old)) Stats.AddEquipmentBonus(s, -Gear.StatValue(old, s));
            slot.Value = item;
            foreach (var s in Gear.StatsOf(item)) Stats.AddEquipmentBonus(s, Gear.StatValue(item, s));
            health.HealMultiplier = (Wears(GearId.TidewovenRobe) ? 1.3f : 1f) * (Wears(GearId.MartyrsShell) ? MartyrHeal : 1f);
            OnStatsChangedServer(null);
            EquippedRpc(item);
            return old;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EquippedRpc(GearItem item) =>
            UI.GameHud.Feed(name + " equipped " + Gear.Name(item), Gear.QualityColor(item.Quality));

        [Rpc(SendTo.Server)]
        public void ReforgeRpc(byte slot, byte mode)
        {
            RunLog.Note("action", "Reforge" + " " + slot + " " + mode, this);
            var anvil = World.Anvil.Near(transform.position);
            if (anvil == null || slot > 1) return;
            var item = Equipped((GearSlot)slot);
            int depth = World.DungeonDirector.GearDepth;
            int cost = mode == 0 ? Gear.RefineCost(item) : Gear.AttuneCost(item, depth);
            if (cost <= 0 || Embers.Value < cost) return;
            Embers.Value -= cost;
            var better = mode == 0 ? new GearItem(item.Kind, item.Quality + 1, item.Depth) : new GearItem(item.Kind, item.Quality, depth);
            ServerEquip(better);

            anvil.WorkFxRpc(mode == 0 ? World.Anvil.Work.Grindstone : World.Anvil.Work.Hearth, 0.7f);
        }

        public const float MartyrDamage = 0.45f, MartyrHeal = 0.85f, WraithstepSpeed = 1.4f, WraithstepSeconds = 1.5f;

        public float WraithstepUntil { get; set; } = -1f;
        public bool Wraithstepping => Time.time < WraithstepUntil;

        public void ServerOnDodge()
        {
            World.DungeonDirector.Instance?.TutorialOnDodge();
            if (Wears(GearId.GraveclothMantle)) dodgeStrikeUntil = Time.time + 2f;
        }

        public const float PerfectStrikeSeconds = 2f, PerfectStrikeMultiplier = 1.3f;

        public float PerfectStrikeUntil;

        public bool ServerConsumePerfectStrike()
        {
            if (Time.time > PerfectStrikeUntil) return false;
            PerfectStrikeUntil = 0f;
            return true;
        }

        public bool ServerConsumeDodgeStrike()
        {
            if (Time.time > dodgeStrikeUntil) return false;
            dodgeStrikeUntil = 0f;
            return true;
        }

        public bool DesperateRegen => Wears(GearId.ThornhideJerkin) && health.Fraction < 0.4f;
        public bool Mastered(int path) => WeaponPath.Value == path && WeaponLevel.Value >= WeaponPaths.MasteryLevel;

        public readonly NetworkVariable<int> Kills = new NetworkVariable<int>();
        public readonly NetworkVariable<int> DamageDealt = new NetworkVariable<int>();
        public readonly NetworkVariable<int> GoldEarned = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Downs = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Revives = new NetworkVariable<int>();
        public readonly NetworkVariable<int> CombosTriggered = new NetworkVariable<int>();

        public readonly NetworkVariable<int> DamageTaken = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Crits = new NetworkVariable<int>();
        public readonly NetworkVariable<int> AbyssKicks = new NetworkVariable<int>();
        private float takenCarry;

        public void ServerCreditTaken(float dealt)
        {
            takenCarry += dealt;
            int whole = Mathf.FloorToInt(takenCarry);
            if (whole > 0) { takenCarry -= whole; DamageTaken.Value += whole; }
        }

        public void ServerCreditCombo() => CombosTriggered.Value++;
        private float damageCarry;

        private float lastKillAt = -10f;
        private int killChain;

        [Rpc(SendTo.ClientsAndHost)]
        private void MultiKillRpc(byte n)
        {
            string text = n == 2 ? "DOUBLE KILL" : n == 3 ? "TRIPLE KILL" : n == 4 ? "QUAD KILL" : "MASSACRE";
            var color = n >= 4 ? new Color(1f, 0.3f, 0.2f) : n == 3 ? new Color(1f, 0.6f, 0.2f) : new Color(1f, 0.85f, 0.4f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.4f, text, color);
            if (n >= 3) UI.GameHud.Feed(name + ": " + text, color);
            if (IsOwner) Art.Sfx.Play2D(Art.Sound.Relic, 0.4f + 0.1f * n);
        }

        public void ServerCreditDamage(float dealt, bool killed)
        {
            damageCarry += dealt;
            int whole = Mathf.FloorToInt(damageCarry);
            if (whole > 0)
            {
                damageCarry -= whole;
                DamageDealt.Value += whole;
            }
            if (killed)
            {
                Kills.Value++;
                World.DungeonDirector.ServerOnPlayerKill(GetComponent<Combat.Health>());

                if (Time.time - lastKillAt > 1.6f) killChain = 0;
                lastKillAt = Time.time;
                killChain++;
                if (killChain >= 2) MultiKillRpc((byte)Mathf.Min(killChain, 6));
            }
        }

        public readonly NetworkVariable<ClassId> ClassId = new NetworkVariable<ClassId>(Classes.ClassId.Knight,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public static bool ClassChangeAllowed => World.DungeonDirector.InLobby && (World.DungeonDirector.Instance == null || !World.DungeonDirector.Instance.RunInProgress);

        public readonly NetworkVariable<bool> LateJoiner = new NetworkVariable<bool>();

        public bool CanChangeClass => ClassChangeAllowed || (World.DungeonDirector.InLobby && LateJoiner.Value);

        [Rpc(SendTo.Server)]
        public void ChooseClassRpc(ClassId id)
        {
            RunLog.Note("action", "ChooseClass" + " " + id, this);
            if (!CanChangeClass || (int)id < 0 || (int)id >= System.Enum.GetValues(typeof(ClassId)).Length) return;
            ClassId.Value = id;
        }
        private NetworkList<float> bonuses;
        private NetworkList<byte> runeBag;
        public readonly NetworkVariable<byte> Socket0 = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Socket1 = new NetworkVariable<byte>();

        public readonly NetworkVariable<byte> Socket2 = new NetworkVariable<byte>();

        public Rune Socketed(int socket) => (Rune)(socket == 0 ? Socket0.Value : socket == 1 ? Socket1.Value : Socket2.Value);

        public bool HasRune(Rune r) => RuneTier(r) >= 0;

        public int RuneTier(Rune kind)
        {
            int best = -1;
            kind = Runes.Kind(kind);
            for (int s = 0; s < Runes.MaxSockets; s++)
            {
                var r = Socketed(s);
                if (r != Rune.None && Runes.Kind(r) == kind) best = Mathf.Max(best, s == Runes.MajorSocket ? Runes.Tier(r) * 2 + 2 : Runes.Tier(r));
            }
            return best;
        }

        public int RuneCount(Rune r)
        {
            int n = 0;
            foreach (var b in runeBag) if (b == (byte)r) n++;
            return n;
        }

        public bool AtRunewright => Shop.InReach(Shop.Vendor.Runewright, transform.position, 1f);

        [Rpc(SendTo.Server)]
        public void MeldRunesRpc(byte rune)
        {
            RunLog.Note("action", "MeldRunes" + " " + rune, this);
            var r = (Rune)rune;
            int tier = Runes.Tier(r);
            if (!AtRunewright || r == Rune.None || tier >= Runes.MaxTier || RuneCount(r) < 3) return;
            int cost = Runes.MeldCost(tier);
            if (Embers.Value < cost) return;
            Embers.Value -= cost;
            for (int k = 0; k < 3; k++) runeBag.RemoveAt(runeBag.IndexOf(rune));
            var made = Runes.Make(r, tier + 1);
            runeBag.Add((byte)made);
            MeldedRpc((byte)made);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MeldedFxRpc()
        {
            Art.Sfx.Play(Art.Sound.Relic, World.Lobby.RunewrightSpot + Vector3.up, 0.6f, 1.6f);
            Art.Vfx.Embers(World.Lobby.RunewrightSpot + new Vector3(0.9f, 1f, 0.25f), 10, new Color(0.45f, 0.85f, 1f), 2f, 0.04f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MeldedRpc(byte made)
        {
            var r = (Rune)made;
            UI.GameHud.Feed(name + " melded three runes into a " + Runes.Name(r), Runes.Color(r));
            Art.Sfx.Play(Art.Sound.Relic, World.Lobby.RunewrightSpot + Vector3.up, 0.9f, 1.3f);
            Art.Vfx.Ring(World.Lobby.RunewrightSpot + new Vector3(0.8f, 0.05f, 0.2f), 2.4f, Runes.Color(r), 0.6f);
            UI.CampChatter.Say(World.FolkId.Ysolde, "Three into one. Feel how heavy it is? That's the good kind of heavy.", 5f);
        }
        public int RuneBagCount => runeBag.Count;
        public Rune RuneInBag(int i) => (Rune)runeBag[i];

        public void ServerAddRune(Rune r)
        {
            if (!IsServer || r == Rune.None) return;
            runeBag.Add((byte)r);
            RuneFoundRpc((byte)r);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RuneFoundRpc(byte r) => UI.GameHud.Feed(name + " found " + ("AEIOU".IndexOf(char.ToUpperInvariant(Runes.Name((Rune)r)[0])) >= 0 ? "an " : "a ") + Runes.Name((Rune)r) + "  -  set it at Ysolde's stones", Runes.Color((Rune)r));

        [Rpc(SendTo.Server)]
        public void SocketRuneRpc(byte rune, sbyte socketIndex = -1)
        {
            RunLog.Note("action", "SocketRune" + " " + rune + " " + socketIndex, this);
            int sockets = Runes.SocketsAt(WeaponLevel.Value);
            int bagIndex = runeBag.IndexOf(rune);
            if (!AtRunewright || sockets == 0 || bagIndex < 0) return;

            int target = 0;
            if (socketIndex >= 0 && socketIndex < sockets) target = socketIndex;
            else for (int s = 0; s < sockets; s++) if (Socketed(s) == Rune.None) { target = s; break; }
            var socket = target == 0 ? Socket0 : target == 1 ? Socket1 : Socket2;
            runeBag.RemoveAt(bagIndex);
            if (socket.Value != 0) runeBag.Add(socket.Value);
            socket.Value = rune;
            MeldedFxRpc();
        }
        private NetworkList<byte> relics;

        private float boneWardReadyAt;

        public void ServerDebugSpendBoneWard() => boneWardReadyAt = Time.time + 60f;
        private bool phoenixSpent;

        public StatBlock Stats { get; private set; }

        public ClassDefinition Class => ClassDefinition.For(ClassId.Value);

        public event Action StatsChanged;

        private Health health;

        private void Awake()
        {
            health = GetComponent<Health>();

            bonuses = new NetworkList<float>();
            relics = new NetworkList<byte>();
            runeBag = new NetworkList<byte>();
            bag = new NetworkList<GearItem>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) { StartCoroutine(AddPrayerLast()); PathTriads.EnsureHook(); EnsureBargainHook(); }
            if (IsServer && World.DungeonDirector.Instance != null && World.DungeonDirector.Instance.RunInProgress) LateJoiner.Value = true;
            WeaponLevel.OnValueChanged += (_, __) => StatsChanged?.Invoke();
            bonuses.OnListChanged += _ => StatsChanged?.Invoke();
            relics.OnListChanged += _ => StatsChanged?.Invoke();
            Armor.OnValueChanged += (_, __) => StatsChanged?.Invoke();
            Charm.OnValueChanged += (_, __) => StatsChanged?.Invoke();
            ClassId.OnValueChanged += (_, __) =>
            {
                if (IsServer && Stats != null)
                {
                    Stats = Class.Build();
                    ServerResetRun();
                }
                StatsChanged?.Invoke();
            };

            if (!IsServer) return;

            Relics.EnsureServerHooks();
            GearEffects.EnsureServerHooks();
            Runes.EnsureServerHooks();
            Combos.EnsureServerHooks();
            health.IncomingModifiers.Add(RelicDefense);
            health.IncomingModifiers.Add(GearDefense);
            health.IncomingModifiers.Add(EdgeDefense);

            Stats = Class.Build();
            for (int i = 0; i < Core.Stats.Count; i++) bonuses.Add(0f);
            ApplyToHealth(refill: true);
        }

        public float Value(StatType stat)
        {
            int i = (int)stat;
            float bonus = i < bonuses.Count ? bonuses[i] : 0f;
            return Mathf.Max(0f, Class.BaseStats[i] + bonus + Gear.StatValue(Armor.Value, stat) + Gear.StatValue(Charm.Value, stat));
        }

        public float RangeMultiplier => 1f + 0.05f * WeaponLevel.Value + (HasRune(Loot.Rune.Farsight) ? 0.25f * Runes.Power(RuneTier(Loot.Rune.Farsight)) : 0f) + (HasRelic(Relic.LongShadow) ? RelicPower(Relic.LongShadow) * 0.15f : 0f);

        public float ReachMultiplier => 1f + (RangeMultiplier - 1f) * 0.5f;

        public readonly NetworkVariable<double> BloodlustEnds = new NetworkVariable<double>();
        public bool Bloodlust => NetworkManager != null && BloodlustEnds.Value > NetworkManager.ServerTime.Time;
        public const float BloodlustDamage = 1.15f;

        public float AttackMultiplier
        {
            get
            {
                float m = 0.5f + Value(StatType.ATT) / 50f;
                if (Bloodlust) m *= BloodlustDamage;
                if (WeaponLevel.Value >= World.Anvil.AscendedLevel) m *= World.Anvil.AscendedDamage;
                m *= 1f + World.Anvil.HoneDamage * Hones.Value;
                if (HasRelic(Relic.BerserkersBrand) && health.Fraction < 0.5f) m *= 1f + 0.4f * RelicPower(Relic.BerserkersBrand);
                if (HasRelic(Relic.BloodPact)) m *= 1f + 0.3f * RelicPower(Relic.BloodPact);
                if (Rallied) m *= 1f + 0.15f * RallyPower.Value;
                if (Pouch != null && Pouch.Bloodfury) m *= RareItems.BloodfuryDamage;
                if (Wears(GearId.MartyrsShell)) m *= 1f + MartyrDamage * (1f - health.Fraction);
                m *= 1f + Crafting.EdgeDamage * Whetted;
                m *= BuildAttackScale;
                return m;
            }
        }

        public float SpeedMultiplier => (Player.Ultimates.InBannerLocal(transform.position) ? Player.Ultimates.BannerSpeed : 1f) * (1f + (Value(StatType.SPD) - 50f) / 100f) * (HasRelic(Relic.GhostLantern) ? 1f + 0.15f * RelicPower(Relic.GhostLantern) : 1f) * (Blessed(Blessing.Swiftness) ? 1.12f : 1f)
            * (Rallied ? 1f + 0.2f * RallyPower.Value : 1f)
            * (1f + Crafting.BootsSpeed * Boots)
            * (Pouch != null && Pouch.InStasis ? 0f : 1f)
            * World.DungeonDirector.WaterSlow(transform.position, true)
            * (TryGetComponent<Player.PlayerDebuffs>(out var deb) ? deb.SpeedMultiplier : 1f)
            * World.Carryable.SpeedFor(NetworkObjectId)
            * World.DungeonDirector.EventSpeed
            * (Wraithstepping ? WraithstepSpeed : 1f)
            * (Player.PlayerLife.IsDragging(NetworkObjectId) ? Player.PlayerLife.DragSpeed : 1f)
            * BuildSpeedScale;

        public float CooldownMultiplier => (1f - CooldownCut)
            * World.DungeonDirector.OmenCooldown * BuildCooldownScale;

        public const float MaxCooldownCut = 0.8f;

        public float CooldownCut => Mathf.Min(MaxCooldownCut,
            (HasRelic(Relic.EchoStone) ? 0.2f * RelicPower(Relic.EchoStone) : 0f) + (Blessed(Blessing.Swiftness) ? 0.15f : 0f) + (HasEdge(Edge.Quickening) ? Edges.QuickeningCut : 0f) + BuildCooldownCut);

        public readonly NetworkVariable<double> RallyUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<float> RallyPower = new NetworkVariable<float>(1f);
        public bool Rallied => NetworkManager != null && RallyUntil.Value > NetworkManager.ServerTime.Time;

        public void ServerRally(float seconds, float power)
        {
            if (!IsServer) return;
            RallyPower.Value = Mathf.Max(Rallied ? RallyPower.Value : 0f, power);
            RallyUntil.Value = NetworkManager.ServerTime.Time + seconds;
        }

        public readonly System.Collections.Generic.HashSet<ulong> ToothMarked = new System.Collections.Generic.HashSet<ulong>();

        [Rpc(SendTo.Owner)]
        public void ShaveCooldownsRpc(float seconds)
        {
            var kit = GetComponent<Player.NetworkPlayer>()?.ActiveKit;
            kit?.ShaveCooldowns(seconds);
        }

        public float GoldMultiplier => 1f + (HasRelic(Relic.GildedSkull) ? 0.25f + 0.1f * RelicTier(Relic.GildedSkull) : 0f) + (Blessed(Blessing.Fortune) ? 0.5f : 0f)
            + (HasRelic(Relic.TallymansLedger) ? 0.2f + 0.1f * RelicTier(Relic.TallymansLedger) : 0f) + BuildGoldBonus;

        private static Relic IdOf(byte b) => (Relic)(b & 31);
        private static int TierOf(byte b) => (b >> 5) & 3;

        public bool HasRelic(Relic r)
        {
            foreach (var b in relics) if (IdOf(b) == r) return true;
            return false;
        }

        public int RelicCount => relics.Count;

        public int RelicWealth { get { int w = relics.Count; for (int i = 0; i < relics.Count; i++) w += RelicTierAt(i); return w; } }

        public int SigilWealth => RankQ.Value + RankE.Value + Sigils.Value;
        public Relic RelicAt(int i) => IdOf(relics[i]);
        public int RelicTierAt(int i) => TierOf(relics[i]);

        public int RelicTier(Relic r)
        {
            foreach (var b in relics) if (IdOf(b) == r) return TierOf(b);
            return 0;
        }

        public float RelicPower(Relic r) => HasRelic(r) ? Relics.Power(RelicTier(r)) : 0f;

        public string RelicLabel(int i) => Relics.Name(RelicAt(i)) + Relics.TierName(RelicTierAt(i));

        public bool CanTakeRelic(Relic r)
        {
            if (HasRelic(r)) return Relics.Scales(r) && RelicTier(r) < Relics.MaxTier;
            if (relics.Count < Relics.MaxCarried) return true;
            for (int i = 0; i < relics.Count; i++) if (Relics.Scales(RelicAt(i)) && RelicTierAt(i) < Relics.MaxTier) return true;
            return false;
        }

        private void ServerEmpower(int index, Relic fedBy)
        {
            var r = RelicAt(index);
            int tier = Mathf.Min(Relics.MaxTier, RelicTierAt(index) + 1);
            relics[index] = (byte)((byte)r | (tier << 5));
            if (r == Relic.HeartOfTheDeep) OnStatsChangedServer(StatType.HP);
            RelicEmpoweredRpc((byte)r, (byte)fedBy, (byte)tier);
        }

        public void ServerRemoveRelic(Relic r)
        {
            if (!IsServer) return;
            for (int i = relics.Count - 1; i >= 0; i--) if (RelicAt(i) == r) relics.RemoveAt(i);
        }

        [Rpc(SendTo.Server)]
        public void SetDownRelicRpc(byte index) => ServerSetDownRelic(index);

        public LootDrop ServerSetDownRelic(int index)
        {
            if (!IsServer || index < 0 || index >= relics.Count || World.DungeonDirector.Instance == null) return null;
            byte b = relics[index];
            var r = RelicAt(index);
            relics.RemoveAt(index);
            if (r == Relic.HeartOfTheDeep || r == Relic.BloodPact) OnStatsChangedServer(StatType.HP);
            var at = transform.position + transform.forward * 1.1f + Vector3.up * 0.1f;
            var drop = World.DungeonDirector.Instance.ServerDropLootGet(at, LootKind.Relic, b, transform.position, settle: true, fixedSum: true);
            if (drop != null) drop.SetDownBy = NetworkObjectId;
            RelicSetDownRpc(b);
            return drop;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RelicSetDownRpc(byte b)
        {
            var r = (Relic)(b & 31);
            int t = (b >> 5) & 3;
            Art.Sfx.Play(Art.Sound.Relic, transform.position + Vector3.up, 0.5f, 0.8f);
            UI.GameHud.Feed(name + " sets down " + Relics.Name(r) + (t > 0 ? (t == 1 ? " II" : " III") : "") + " (it keeps its power)", Relics.GemColor(r));
        }

        public bool ServerMeltRelic(out Relic gone, out Relic got, out int gotTier)
        {
            gone = got = default; gotTier = 0;
            if (!IsServer || relics.Count == 0) return false;
            int idx = 0;
            for (int i = 1; i < relics.Count; i++) if (RelicTierAt(i) < RelicTierAt(idx)) idx = i;
            gone = RelicAt(idx);
            int tier = RelicTierAt(idx);
            relics.RemoveAt(idx);
            if (gone == Relic.HeartOfTheDeep || gone == Relic.BloodPact) OnStatsChangedServer(StatType.HP);
            got = gone;
            for (int tries = 0; tries < 60; tries++)
            {
                var r = Relics.Random();
                if (r == gone || HasRelic(r)) continue;
                got = r;
                break;
            }
            gotTier = Relics.Scales(got) ? Mathf.Min(tier + 1, Relics.MaxTier) : 0;
            ServerAddRelic(got, gotTier);
            return true;
        }

        public bool ServerAddRelic(Relic r) => ServerAddRelic(r, 0);

        public bool ServerAddRelic(Relic r, int tier)
        {
            if (!IsServer || !CanTakeRelic(r)) return false;
            RunLog.Note("relic", Relics.Name(r) + (tier > 0 ? " +" + tier : ""), this);
            if (HasRelic(r))
            {
                for (int i = 0; i < relics.Count; i++) if (RelicAt(i) == r) { ServerEmpower(i, r); break; }
                return true;
            }
            if (relics.Count >= Relics.MaxCarried)
            {
                int lowest = 99;
                var pick = new System.Collections.Generic.List<int>();
                for (int i = 0; i < relics.Count; i++)
                {
                    if (!Relics.Scales(RelicAt(i)) || RelicTierAt(i) >= Relics.MaxTier) continue;
                    int t = RelicTierAt(i);
                    if (t < lowest) { lowest = t; pick.Clear(); }
                    if (t == lowest) pick.Add(i);
                }
                ServerEmpower(pick[UnityEngine.Random.Range(0, pick.Count)], r);
                return true;
            }
            relics.Add((byte)((byte)r | (Mathf.Clamp(tier, 0, Relics.MaxTier) << 5)));
            if (r == Relic.HeartOfTheDeep || r == Relic.BloodPact) OnStatsChangedServer(StatType.HP);
            RelicGainedRpc((byte)r);

            for (int i = 0; i < Relics.Synergies.Length; i++)
            {
                var syn = Relics.Synergies[i];
                if ((syn.a == r || syn.b == r) && HasRelic(syn.a) && HasRelic(syn.b)) SynergyRpc(i);
            }
            return true;
        }

        public void ServerDebugRemoveRelic(Relic r)
        {
            if (!IsServer) return;
            for (int i = relics.Count - 1; i >= 0; i--) if (IdOf(relics[i]) == r) relics.RemoveAt(i);
        }

        public void ServerNewFloor()
        {
            phoenixSpent = false;
            boneWardReadyAt = 0f;

            MapRevealed.Value = ScoutMap.Value;
            ScoutMap.Value = false;
        }

        public float PhoenixFraction => Relics.Has(this, Relics.Synergy.Undying) ? 1f : 0.5f + 0.25f * RelicTier(Relic.PhoenixFeather);

        [Rpc(SendTo.ClientsAndHost)]
        private void SynergyRpc(int index)
        {
            var syn = Relics.Synergies[index];
            var c = Relics.GemColor(syn.a);
            UI.GameHud.Feed(name + " awakens a SYNERGY: " + syn.name + " - " + syn.text, c);
            if (IsOwner)
            {
                UI.GameHud.ShowTitleCard("SYNERGY: " + syn.name.ToUpperInvariant(), syn.text);
                Art.Sfx.Play2D(Art.Sound.Relic, 1f);
            }
            Art.Vfx.Ring(transform.position, 2.5f, c, 0.5f);
            Art.Vfx.Embers(transform.position + Vector3.up, 20, c, 4f, 0.05f, 0.9f);
        }

        public bool ServerTryPhoenix()
        {
            if (!HasRelic(Relic.PhoenixFeather) || phoenixSpent) return false;
            phoenixSpent = true;
            PhoenixFxRpc();
            return true;
        }

        private float RelicDefense(float damage, Vector3 attackerPosition)
        {
            if (HasRelic(Relic.BoneWard) && Time.time >= boneWardReadyAt)
            {
                boneWardReadyAt = Time.time + 12f / RelicPower(Relic.BoneWard);
                BoneWardFxRpc();
                return 0f;
            }

            if (HasRelic(Relic.ThornedMail) && Vector3.Distance(attackerPosition, transform.position) < 4f)
            {
                Health nearest = null;
                float best = 2.5f;
                foreach (var h in Health.All)
                {
                    if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                    float d = Vector3.Distance(h.transform.position, attackerPosition);
                    if (d < best) { best = d; nearest = h; }
                }
                if (nearest != null) nearest.Tag("PlayerProgress").ServerDamage(damage * 0.4f * RelicPower(Relic.ThornedMail), transform.position, NetworkObjectId);
            }
            return damage;
        }

        private float EdgeDefense(float damage, Vector3 attackerPosition)
        {
            if (HasEdge(Edge.Stalwart)) damage *= Edges.StalwartTaken;
            damage *= BuildIncomingScale * PathTriads.Incoming(this, attackerPosition);
            damage = PathTriads.Absorb(this, damage);
            ServerThornCrown(damage, attackerPosition);

            if (HasRelic(Relic.IronPsalter) && Vector3.Distance(attackerPosition, transform.position) >= 5f) damage *= 1f - 0.15f * RelicPower(Relic.IronPsalter);
            return damage;
        }

        private float GearDefense(float damage, Vector3 attackerPosition)
        {
            if (damage <= 0f) return damage;
            if (Wears(GearId.LuckyKnucklebone) && UnityEngine.Random.value < 0.1f)
            {
                MissedRpc();
                return 0f;
            }
            if (Wears(GearId.OssuaryPlate) && health.Fraction > 0.5f) damage *= 0.85f;
            if (Wears(GearId.GravediggersCoat) && StandingStill()) damage *= 0.75f;
            if (Wears(GearId.BellringersHauberk) && Time.time >= bellReadyAt)
            {
                bellReadyAt = Time.time + 10f;
                foreach (var foe in CombatQuery.Circle(transform.position, 4f, Team.Enemies))
                {
                    if (!foe.TryGetComponent<ICrowdControl>(out var cc)) continue;
                    var push = foe.transform.position - transform.position;
                    push.y = 0f;
                    cc.ServerKnockback(push.normalized * 9f);
                }
                BellFxRpc();
            }
            if (Blessed(Blessing.Bulwark)) damage *= 0.85f;
            if (Wears(GearId.WardensBulwark) && Time.time >= bulwarkReadyAt)
            {
                bulwarkReadyAt = Time.time + 8f;
                damage *= 0.5f;
                Enemies.EnemyBrain nearest = null;
                float best = 4f;
                foreach (var h in Health.All)
                {
                    if (h == null || h.Team != Team.Enemies || !h.IsAlive || !h.TryGetComponent<Enemies.EnemyBrain>(out var b)) continue;
                    float d = Vector3.Distance(h.transform.position, attackerPosition);
                    if (d < best) { best = d; nearest = b; }
                }
                if (nearest != null) nearest.ServerStun(1.5f);
                BulwarkFxRpc();
            }

            if (Wears(GearId.AldousOath))
            {
                var to = attackerPosition - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, to) < 60f) damage *= 0.8f;
            }

            if (HasEdge(Edge.Retribution) && damage > 0f && (attackerPosition - transform.position).sqrMagnitude < Edges.RetributionReach * Edges.RetributionReach)
            {
                Health striker = null;
                float best = 2.5f;
                foreach (var h in Health.All)
                {
                    if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                    float d = Vector3.Distance(h.transform.position, attackerPosition);
                    if (d < best) { best = d; striker = h; }
                }
                if (striker != null) striker.Tag("Retribution").ServerDamage(damage * Edges.RetributionShare, transform.position, NetworkObjectId);
            }
            if ((Wears(GearId.DrownedKnightsMail) || Wears(GearId.GorrsHide)) && (attackerPosition - transform.position).sqrMagnitude < 16f)
            {
                Health striker = null;
                float best = 2.5f;
                foreach (var h in Health.All)
                {
                    if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                    float d = Vector3.Distance(h.transform.position, attackerPosition);
                    if (d < best) { best = d; striker = h; }
                }
                if (striker != null)
                {
                    if (Wears(GearId.DrownedKnightsMail)) striker.ServerChill(2.5f);
                    if (Wears(GearId.GorrsHide)) { striker.Tag("PlayerProgress").ServerDamage(15f * AttackMultiplier, transform.position, NetworkObjectId); RotFxRpc(striker.Center); }
                }
            }
            if (Wears(GearId.AshenShroud) && Time.time >= shroudReadyAt && (health.Current.Value - damage) / Mathf.Max(1f, health.Max.Value) < 0.3f)
            {
                shroudReadyAt = Time.time + 15f;
                foreach (var foe in CombatQuery.Circle(transform.position, 4.5f, Team.Enemies))
                {
                    foe.Tag("PlayerProgress").ServerDamage(40f * AttackMultiplier, transform.position, NetworkObjectId);
                    foe.ServerScorch(5f, NetworkObjectId);
                }
                ShroudFxRpc();
            }
            return damage;
        }

        private float judgementReadyAt, fetishReadyAt, bellReadyAt, stillSince;
        private Vector3 stillAt;

        public bool ServerConsumeFetish()
        {
            if (Time.time < fetishReadyAt) return false;
            fetishReadyAt = Time.time + 8f;
            return true;
        }

        private bool StandingStill()
        {
            if ((transform.position - stillAt).sqrMagnitude > 0.04f)
            {
                stillAt = transform.position;
                stillSince = Time.time;
            }
            return Time.time - stillSince >= 1f;
        }

        private void LateUpdate()
        {
            TickWardShardFx();
            if (IsServer && Wears(GearId.GravediggersCoat)) StandingStill();

            if (IsServer && (Time.frameCount + (int)NetworkObjectId) % 8 == 0)
            {
                byte cut = OnPath(Classes.PathTriads.Path) ? (byte)Mathf.RoundToInt((1f - Classes.PathTriads.Readout(this)) * 100f) : (byte)0;
                if (SurvivalCut.Value != cut) SurvivalCut.Value = cut;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void ZoneHitFxRpc(Vector3 at, byte zone)
        {
            bool burst = zone == 3, weak = zone == (byte)HitZone.Weak || burst;
            var c = weak ? new Color(1f, 0.5f, 0.2f) : new Color(1f, 0.92f, 0.55f);
            if (burst)
            {
                UI.GameHud.ShowCombo(at + Vector3.up * 0.25f, "WEAK POINT!", c);
                Art.Vfx.Fireball(at, 0.9f, c);
                Art.Vfx.Flash(at, c, 4f, 5f, 0.15f);
                Art.Vfx.Embers(at, 16, c, 5f, 0.05f, 0.5f);
                Art.Vfx.Ring(Art.Vfx.Floor(at) + Vector3.up * 0.05f, Combat.Arrows.BurstRadius, c, 0.35f);
                Art.Sfx.Play(Art.Sound.Fire, at, 0.7f, 1.6f);
            }
            else if (weak) Art.Vfx.Embers(at, 5, c * 0.8f, 2.5f, 0.035f, 0.3f);
            else UI.GameHud.ShowCombo(at + Vector3.up * 0.25f, "HEADSHOT", c);
            if (!weak) Art.Vfx.Embers(at, 6, c, 3f, 0.04f, 0.35f);
            if (IsOwner)
            {
                Art.Sfx.Play(Art.Sound.Shatter, at, 0.5f, weak ? 1.1f : 1.6f);
                Player.NetworkPlayer.Local?.ConfirmHit(burst ? 1.7f : weak ? 1.2f : 1.1f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void CritFxRpc(Vector3 at)
        {
            if (IsServer) Crits.Value++;
            UI.GameHud.ShowCombo(at, "CRIT", new Color(1f, 0.55f, 0.15f));
            if (IsOwner) Art.Sfx.Play(Art.Sound.Hit, at, 0.9f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void WraithNovaFxRpc(Vector3 at)
        {
            var ghost = new Color(0.55f, 0.75f, 1f);
            Art.Sfx.Play(Art.Sound.Whisper, at + Vector3.up, 0.7f, 0.7f);
            Art.Vfx.Ring(at, 4f, ghost, 0.4f);
            Art.Vfx.Embers(at + Vector3.up * 0.6f, 10, ghost, 3.5f, 0.05f, 0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BellFxRpc()
        {
            Art.Sfx.Play(Art.Sound.Slam, transform.position + Vector3.up, 0.8f, 1.4f);
            Art.Vfx.Ring(transform.position, 4f, new Color(1f, 0.8f, 0.4f), 0.4f);
        }

        public void ServerJudgement(Vector3 at)
        {
            if (Time.time < judgementReadyAt) return;
            judgementReadyAt = Time.time + 1f;
            JudgementWarnRpc(at);
            StartCoroutine(Judgement(at));
        }

        private System.Collections.IEnumerator Judgement(Vector3 at)
        {
            yield return new WaitForSeconds(0.7f);
            foreach (var foe in CombatQuery.Circle(at, 2.6f, Team.Enemies))
            {
                foe.Tag("PlayerProgress").ServerDamage(30f * AttackMultiplier, at, NetworkObjectId);
                foe.ServerScorch(4f, NetworkObjectId);
            }
            JudgementFxRpc(at);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void JudgementWarnRpc(Vector3 at) => Telegraph.Show(at, 2.6f, 0.7f, null, new Color(1f, 0.8f, 0.35f, 0.3f));

        [Rpc(SendTo.ClientsAndHost)]
        private void JudgementFxRpc(Vector3 at)
        {
            var holy = new Color(1f, 0.75f, 0.35f);
            Art.Sfx.Play(Art.Sound.Fire, at + Vector3.up, 0.6f, 1.3f);
            for (int i = 0; i < 4; i++) Art.Vfx.Fireball(at + Vector3.up * (0.4f + i * 0.6f), 1f - i * 0.12f, holy);
            Art.Vfx.Embers(at, 10, holy, 4f, 0.05f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void CinderPatchRpc(Vector3 at)
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("CinderPatch");
            go.transform.position = Art.Vfx.Floor(at) + Vector3.up * 0.05f;
            go.AddComponent<Enemies.EmberPatchFx>().Init(Loot.CinderCrown.Radius, Loot.CinderCrown.Seconds);
            Art.Sfx.Play(Art.Sound.Fire, at, 0.5f, 1.2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void BoneBurstFxRpc(Vector3 at)
        {
            Art.Sfx.Play(Art.Sound.BoneBreak, at + Vector3.up, 0.9f, 0.9f);
            Art.Vfx.Ring(Art.Vfx.Floor(at) + Vector3.up * 0.05f, 3f, new Color(0.9f, 0.85f, 0.7f), 0.35f);
            Art.Vfx.Embers(at + Vector3.up, 14, new Color(0.95f, 0.9f, 0.78f), 6f, 0.06f, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void BatonFxRpc()
        {
            Art.Sfx.Play(Art.Sound.Toll, transform.position + Vector3.up * 2f, 0.4f, 1.6f);
            Art.Vfx.Ring(Art.Vfx.Floor(transform.position) + Vector3.up * 0.05f, 8f, new Color(0.55f, 1f, 0.6f), 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RotFxRpc(Vector3 at) => Art.Vfx.Embers(at, 8, new Color(0.55f, 0.9f, 0.25f), 3f, 0.05f, 0.5f);

        [Rpc(SendTo.ClientsAndHost)]
        private void BulwarkFxRpc() => Art.Sfx.Play(Art.Sound.Block, transform.position + Vector3.up, 1f, 0.7f);

        [Rpc(SendTo.ClientsAndHost)]
        private void MissedRpc() => Art.Sfx.Play(Art.Sound.Dodge, transform.position + Vector3.up, 0.5f, 1.4f);

        [Rpc(SendTo.ClientsAndHost)]
        private void ShroudFxRpc()
        {
            Art.Sfx.Play(Art.Sound.Fire, transform.position + Vector3.up, 1f, 0.8f);
            Telegraph.Show(transform.position, 4.5f, 0.5f, null, new Color(1f, 0.4f, 0.1f, 0.45f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RelicEmpoweredRpc(byte r, byte fedBy, byte tier)
        {
            var relic = (Relic)r;
            if (IsOwner && tier >= Relics.MaxTier) Deeds.Earn(Deeds.Deed.RelicThree, "A relic at its third tier");
            Art.Sfx.Play(Art.Sound.Relic, transform.position + Vector3.up, 0.9f, 1.15f);
            UI.GameHud.Feed(fedBy == r
                ? name + "'s " + Relics.Name(relic) + " grows stronger: " + Relics.Name(relic) + Relics.TierName(tier) + " (+" + Relics.TierPercent(tier) + "%)"
                : Relics.Name((Relic)fedBy) + " feeds " + name + "'s " + Relics.Name(relic) + ": now " + Relics.Name(relic) + Relics.TierName(tier) + " (the tray holds " + Relics.MaxCarried + ")",
                Relics.GemColor(relic));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RelicGainedRpc(byte r)
        {
            var relic = (Relic)r;
            Art.Sfx.Play(Art.Sound.Relic, transform.position + Vector3.up, 0.9f);
            UI.GameHud.Feed(name + " found " + Relics.Name(relic) + ": " + Relics.Describe(relic), Relics.GemColor(relic));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BoneWardFxRpc() => Art.Sfx.Play(Art.Sound.Block, transform.position + Vector3.up, 0.8f, 0.6f);

        [Rpc(SendTo.ClientsAndHost)]
        private void PhoenixFxRpc()
        {
            Art.Sfx.Play(Art.Sound.Revive, transform.position + Vector3.up, 1f, 0.8f);
            Telegraph.Show(transform.position, 2.5f, 0.6f, null, new Color(1f, 0.6f, 0.15f, 0.4f));
            UI.GameHud.Feed(name + " rises from the ashes!", Relics.GemColor(Relic.PhoenixFeather));
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void LightningFxRpc(Vector3[] points)
        {
            Art.LightningBolt.Spawn(transform.position + Vector3.up * 6f, points);
            Art.Sfx.Play(Art.Sound.Shatter, points[0], 1f, 0.6f);
        }

        public float AttackTimeMultiplier => Mathf.Max(0.5f, 1.5f - Value(StatType.DEX) / 50f * 0.5f) * (Pouch != null && Pouch.Bloodfury ? RareItems.BloodfuryAttackTime : 1f)
            * (Player.Ultimates.InBannerLocal(transform.position) ? Player.Ultimates.BannerAttackTime : 1f);

        private Player.Flasks Pouch => pouchCache != null ? pouchCache : (pouchCache = GetComponent<Player.Flasks>());
        private Player.Flasks pouchCache;

        public void ServerAddEmbers(int amount)
        {
            if (!IsServer) return;
            if (amount > 0) amount = Mathf.RoundToInt(amount * BuildEmberScale);
            Embers.Value = Mathf.Max(0, Embers.Value + amount);
        }

        public string CannotTemperReason()
        {
            if (WeaponLevel.Value >= World.Anvil.MaxLevel) return "Fully tempered";
            if (WeaponLevel.Value >= WeaponPaths.PathLevel && WeaponPath.Value == 0) return "Choose a path first";
            if (OpenEdgeSlot >= 0) return "Choose an edge first";
            if (Embers.Value < World.Anvil.Cost(WeaponLevel.Value)) return "Not enough embers";
            if (Gold.Value < World.Anvil.GoldCost(WeaponLevel.Value)) return "Not enough gold";
            return null;
        }

        [Rpc(SendTo.Server)]
        public void TemperRpc()
        {
            RunLog.Note("action", "Temper", this);
            var anvil = World.Anvil.Near(transform.position);
            if (anvil != null && ServerUpgradeWeapon()) anvil.ForgeFxRpc(WeaponLevel.Value);
        }

        [Rpc(SendTo.Server)]
        public void ChoosePathRpc(byte path)
        {
            RunLog.Note("action", "ChoosePath" + " " + path, this);
            if (World.Anvil.Near(transform.position) == null) return;
            if (WeaponPath.Value != 0 || WeaponLevel.Value < WeaponPaths.PathLevel || path < 1 || path > WeaponPaths.Count) return;
            WeaponPath.Value = path;
            PathChosenRpc(path);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PathChosenRpc(byte path)
        {
            Art.Sfx.Play(Art.Sound.Relic, transform.position + Vector3.up, 0.7f, 1.2f);
            UI.GameHud.Feed(name + "'s " + ClassDefinition.WeaponName(ClassId.Value) + " takes the path of the " + WeaponPaths.Name(ClassId.Value, path), LootInfo.EmberColor);
        }

        public bool ServerFreeTemper()
        {
            if (!IsServer || CannotTemperReason() != null && WeaponLevel.Value >= World.Anvil.MaxLevel) return false;
            if (WeaponLevel.Value >= World.Anvil.MaxLevel || (WeaponLevel.Value >= WeaponPaths.PathLevel && WeaponPath.Value == 0) || OpenEdgeSlot >= 0) return false;
            WeaponLevel.Value++;
            ServerAddBonus(StatType.ATT, ClassDefinition.WeaponAttPerLevel);
            ServerAddBonus(StatType.DEX, ClassDefinition.WeaponDexPerLevel);
            WeaponUpgradedRpc(WeaponLevel.Value);
            return true;
        }

        public readonly NetworkVariable<bool> MirrorCursed = new NetworkVariable<bool>();
        public const float MirrorHealth = 0.8f;

        public void ServerSetMirrorCursed(bool on)
        {
            if (!IsServer || MirrorCursed.Value == on) return;
            MirrorCursed.Value = on;
            ApplyToHealth(refill: false);
        }

        public bool ServerUpgradeWeapon()
        {
            if (!IsServer || CannotTemperReason() != null) return false;
            int cost = World.Anvil.Cost(WeaponLevel.Value), gold = World.Anvil.GoldCost(WeaponLevel.Value);
            if (Embers.Value < cost || Gold.Value < gold) return false;
            Embers.Value -= cost;
            Gold.Value -= gold;
            if (gold > 0 && HasRelic(Relic.PilgrimsPurse)) Embers.Value += gold / 10;
            WeaponLevel.Value++;
            ServerAddBonus(StatType.ATT, ClassDefinition.WeaponAttPerLevel);
            ServerAddBonus(StatType.DEX, ClassDefinition.WeaponDexPerLevel);
            WeaponUpgradedRpc(WeaponLevel.Value);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WeaponUpgradedRpc(int level)
        {
            string weapon = ClassDefinition.WeaponName(ClassId.Value);
            UI.GameHud.Feed(name + " tempered their " + weapon + " to +" + level, LootInfo.EmberColor);
            if (level == WeaponPaths.MasteryLevel && WeaponPath.Value != 0)
                UI.GameHud.Feed(WeaponPaths.Name(ClassId.Value, WeaponPath.Value) + " mastery: " + WeaponPaths.Mastery(ClassId.Value, WeaponPath.Value), LootInfo.EmberColor);
            if (Edges.SlotAt(level) >= 0 && IsOwner) UI.GameHud.Feed("An EDGE opens in your " + weapon + " at +" + level + ": forge one of three at the anvil", Edges.Color(Edge.Momentum));
            if (level == Runes.SocketLevels[2]) UI.GameHud.Feed("A third rune socket opens in the " + weapon, LootInfo.EmberColor);
            if (level == World.Anvil.AscendedLevel) UI.GameHud.Feed(name + "'s " + weapon + " is ASCENDED: +10% damage", new Color(1f, 0.85f, 0.45f));
            if (level == World.Anvil.MasterworkLevel) UI.GameHud.Feed(name + "'s " + weapon + " is a MASTERWORK: +8% critical chance", new Color(1f, 0.92f, 0.7f));
        }

        public void ServerAddBonus(StatType stat, float amount)
        {
            if (!IsServer) return;
            Stats.AddEquipmentBonus(stat, amount);
            bonuses[(int)stat] += amount;
            OnStatsChangedServer(stat);
        }

        private static System.Collections.Generic.List<PlayerProgress> Party()
        {
            var list = new System.Collections.Generic.List<PlayerProgress>();
            foreach (var l in Player.PlayerLife.All)
                if (l != null && l.TryGetComponent<PlayerProgress>(out var p)) list.Add(p);
            return list;
        }

        public static void ServerShareGold(PlayerProgress finder, int total)
        {
            var party = Party();
            if (!World.DungeonDirector.ShareLoot || party.Count <= 1) { finder.ServerAddGold(total); return; }
            int each = total / party.Count, rest = total - each * party.Count;
            foreach (var p in party) p.ServerAddGold(each + (p == finder ? rest : 0));
        }

        public static void ServerShareEmbers(PlayerProgress finder, int total)
        {
            var party = Party();
            if (!World.DungeonDirector.ShareLoot || party.Count <= 1) { finder.ServerAddEmbers(total); return; }
            total = Mathf.Max(1, Mathf.RoundToInt(total * Enemies.Difficulty.PartyEmbers));
            int each = total / party.Count, rest = total - each * party.Count;
            foreach (var p in party) p.ServerAddEmbers(each + (p == finder ? rest : 0));
        }

        [Rpc(SendTo.Server)]
        public void GiveGoldRpc(ulong toPlayer, int amount)
        {
            RunLog.Note("action", "GiveGold" + " " + toPlayer + " " + amount, this);
            amount = Mathf.Min(amount, Gold.Value);
            if (amount <= 0 || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(toPlayer, out var obj) || !obj.TryGetComponent<PlayerProgress>(out var to) || to == this) return;
            Gold.Value -= amount;
            to.Gold.Value += amount;
            GiftRpc(name, to.name, amount + " gold");
        }

        [Rpc(SendTo.Server)]
        public void EvenOutGoldRpc()
        {
            RunLog.Note("action", "EvenOutGold", this);
            var party = Party();
            if (party.Count <= 1) return;
            int total = 0;
            foreach (var p in party) total += p.Gold.Value;
            int average = total / party.Count, spare = Gold.Value - average, given = 0;
            foreach (var p in party)
            {
                if (spare <= 0) break;
                if (p == this || p.Gold.Value >= average) continue;
                int give = Mathf.Min(spare, average - p.Gold.Value);
                p.Gold.Value += give;
                Gold.Value -= give;
                spare -= give;
                given += give;
            }
            if (given > 0) GiftRpc(name, "the party", given + " gold, evening out the purse");
        }

        [Rpc(SendTo.Server)]
        public void BagGiftRpc(int i)
        {
            RunLog.Note("action", "BagGift" + " " + i, this);
            if (!CanManagePack(i)) return;
            PlayerProgress to = null;
            float best = float.MaxValue;
            foreach (var p in Party())
            {
                if (p == this) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; to = p; }
            }
            if (to == null) return;
            var item = bag[i];
            bag.RemoveAt(i);
            if (to.bag.Count < BagSize) to.bag.Add(item);
            else GearDrop.ServerSpawn(item, to.OwnerClientId, to.transform.position + to.transform.forward * 1.2f + Vector3.up * 0.05f);
            GiftRpc(name, to.name, Gear.Name(item));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GiftRpc(string from, string to, string what)
        {
            UI.GameHud.Feed(from + " gave " + to + " " + what, new Color(1f, 0.85f, 0.45f));
            Art.Sfx.Play2D(Art.Sound.Coins, 0.5f);
        }

        public void ServerAddGold(int amount)
        {
            if (!IsServer) return;
            Gold.Value = Mathf.Max(0, Gold.Value + amount);
            if (amount > 0) GoldEarned.Value += amount;
        }

        public bool ServerSpendGold(int amount)
        {
            if (!IsServer || Gold.Value < amount) return false;
            Gold.Value -= amount;
            return true;
        }

        public float Bonus(StatType stat) => (int)stat < bonuses.Count ? bonuses[(int)stat] : 0f;

        public string CannotBuyReason(Shop.Vendor vendor, int index)
        {
            var items = Shop.For(OwnerClientId, vendor);
            if (index < 0 || index >= items.Length) return "Unknown item";
            var item = items[index];
            if (Gold.Value < item.Price) return "Not enough gold";
            switch (item.Kind)
            {
                case Shop.ItemKind.Heal: return health.Current.Value >= health.Max.Value ? "Already at full health" : null;
                case Shop.ItemKind.Embers: return null;
                case Shop.ItemKind.Sigil: return null;
                case Shop.ItemKind.Flasks: return "Sold out";
                case Shop.ItemKind.Potion: return GetComponent<Player.Flasks>() is var fl && fl != null && fl.IsFull((Player.Flasks.Kind)(int)item.Amount) ? "Pouch full (5)" : null;
                case Shop.ItemKind.Gear: return gearBoughtSeed == World.DungeonDirector.Instance?.Floor.Value.x ? "Sold" : null;
                case Shop.ItemKind.Relic: return !CanTakeRelic((Relic)(int)item.Amount) ? (HasRelic((Relic)(int)item.Amount) ? "At full power" : "Tray full") : null;
                case Shop.ItemKind.Blessing: return ActiveBlessing.Value != 0 ? "Already blessed" : null;
                case Shop.ItemKind.ShrineRestock: return null;
                case Shop.ItemKind.Rare: return GetComponent<Player.Flasks>() is var rf && rf != null && rf.Rare.Value != 0 ? "Slot 5 is full" : null;
                case Shop.ItemKind.Prayer: return PrayedFor.Value ? "Already prayed for" : null;
                case Shop.ItemKind.Drill: return DrillCount(item.Stat) >= (int)item.Cap ? "Mastered" : null;
                case Shop.ItemKind.Map: return ScoutMap.Value ? "Already have it" : null;
                case Shop.ItemKind.Salts: return Salts.Value ? "Already carrying some" : null;
                case Shop.ItemKind.Rope: return Rope.Value ? "Already carrying one" : null;
                case Shop.ItemKind.Picks: return Picks.Value >= PicksPerSet ? "Your roll is full" : null;
                case Shop.ItemKind.Lantern: return Lantern.Value ? "Already carrying one" : null;
                case Shop.ItemKind.Restock: return null;
                default: return Bonus(item.Stat) + item.Amount > item.Cap + 0.01f ? "Sold out" : null;
            }
        }

        [Rpc(SendTo.Server)]
        public void BuyRpc(Shop.Vendor vendor, int index)
        {
            if (!Shop.InReach(vendor, transform.position, 1f)) return;
            if (CannotBuyReason(vendor, index) != null) return;

            var item = Shop.For(OwnerClientId, vendor)[index];
            bool bought;
            switch (item.Kind)
            {
                case Shop.ItemKind.Heal:
                    bought = true;
                    health.ServerHeal(health.Max.Value * item.Amount);
                    break;
                case Shop.ItemKind.Embers:
                    ServerAddEmbers((int)item.Amount);
                    bought = true;
                    break;
                case Shop.ItemKind.Sigil:
                    ServerAddSigil();
                    bought = true;
                    break;
                case Shop.ItemKind.Potion:
                    var pouch = GetComponent<Player.Flasks>();
                    bought = pouch != null && pouch.ServerAdd((Player.Flasks.Kind)(int)item.Amount, 1) > 0;
                    break;
                case Shop.ItemKind.Gear:

                    GearDrop.ServerSpawn(item.Gear, OwnerClientId, transform.position + transform.forward * 1.2f + Vector3.up * 0.05f);
                    gearBoughtSeed = World.DungeonDirector.Instance.Floor.Value.x;
                    GearBoughtRpc(gearBoughtSeed);
                    bought = true;
                    break;
                case Shop.ItemKind.Relic:
                    bought = ServerAddRelic((Relic)(int)item.Amount);
                    break;
                case Shop.ItemKind.Prayer:
                    PrayedFor.Value = true;
                    PrayerBoughtRpc(name);
                    bought = true;
                    break;
                case Shop.ItemKind.Drill:
                    ServerAddBonus(item.Stat, item.Amount);
                    Drills.Value += 1UL << (4 * (int)item.Stat);
                    CheckMastery(item.Stat);
                    bought = true;
                    break;
                case Shop.ItemKind.Map:
                    ScoutMap.Value = true;
                    bought = true;
                    break;
                case Shop.ItemKind.Salts:
                    Salts.Value = true;
                    bought = true;
                    break;
                case Shop.ItemKind.Rope:
                    Rope.Value = true;
                    bought = true;
                    break;
                case Shop.ItemKind.Picks:
                    Picks.Value = (byte)PicksPerSet;
                    bought = true;
                    break;
                case Shop.ItemKind.Lantern:
                    Lantern.Value = true;
                    bought = true;
                    break;
                case Shop.ItemKind.Blessing:
                    ServerBless((Blessing)(int)item.Amount);
                    BlessedRpc((byte)(int)item.Amount);
                    bought = true;
                    break;
                case Shop.ItemKind.Rare:
                    bought = GetComponent<Player.Flasks>() is var rp && rp != null && rp.ServerGiveRare((RareItem)(int)item.Amount);
                    break;
                case Shop.ItemKind.ShrineRestock:
                    ShrineRerolls.Value++;
                    bought = true;
                    break;
                case Shop.ItemKind.Restock:
                    StockRerolls.Value++;
                    gearBoughtSeed = 0;
                    GearBoughtRpc(0);
                    bought = true;
                    break;
                default:
                    ServerAddBonus(item.Stat, item.Amount);
                    bought = true;
                    break;
            }

            if (bought)
            {
                RunLog.Note("buy", vendor + ": " + item.Name + " (" + item.Price + "g)", this);
                Gold.Value -= item.Price;
                if (HasRelic(Relic.PilgrimsPurse) && World.DungeonDirector.InLobby) ServerAddEmbers(item.Price / 10);
                PurchasedRpc();
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BlessedRpc(byte b)
        {
            var c = Blessings.Color((Blessing)b);
            Art.Sfx.Play(Art.Sound.Revive, transform.position + Vector3.up, 0.9f, 1.1f);
            Art.Vfx.Ring(transform.position, 2f, c, 0.5f);
            UI.GameHud.Feed(name + " receives the " + Blessings.Name((Blessing)b).Replace("Blessing of ", "blessing of ") + " for the next floor", c);
        }

        private int gearBoughtSeed = int.MinValue;

        [Rpc(SendTo.Owner)]
        private void GearBoughtRpc(int seed) => gearBoughtSeed = seed;

        private float grazeStreakUntil;
        private int grazeStreak;

        public int Grazes { get; private set; }

        public void ServerGraze(Vector3 at)
        {
            var life = GetComponent<Player.PlayerLife>();
            if (life != null && (life.Downed.Value || !life.Targetable)) return;
            Grazes++;
            grazeStreak = Time.time < grazeStreakUntil ? grazeStreak + 1 : 1;
            grazeStreakUntil = Time.time + 2.2f;

            GrazeRpc(at, (byte)Mathf.Min(255, grazeStreak));
        }

        [Rpc(SendTo.Owner, Delivery = RpcDelivery.Unreliable)]
        private void GrazeRpc(Vector3 at, byte streak) => UI.GameHud.Graze(at, streak);

        [Rpc(SendTo.Owner, Delivery = RpcDelivery.Unreliable)]
        public void HitMarkRpc(bool kill, bool heavy) => UI.GameHud.HitMark(kill, heavy);

        [Rpc(SendTo.ClientsAndHost)]
        private void PrayerBoughtRpc(string who)
        {
            string[] lines =
            {
                "*Brother Aldric pockets the coin, then prays at the top of his voice.* Lord, keep " + who + " from harm! Paid in full!",
                "A generous soul! The gods will catch you once, " + who + ". Twice costs double.",
                "*He bites the coin first. Then he prays.* There. You are spoken for, " + who + ".",
            };
            UI.CampChatter.Say(World.FolkId.Aldric, lines[UnityEngine.Random.Range(0, lines.Length)], 6f);
        }

        [Rpc(SendTo.Owner)]
        private void PurchasedRpc() => Art.Sfx.Play2D(Art.Sound.Purchase, 0.8f);

        public void ServerResetRun()
        {
            if (!IsServer) return;
            Stats.ResetRunProgress();

            var flasks = GetComponent<Player.Flasks>();
            if (flasks != null) { flasks.ServerReset(); flasks.ServerStartingPouch(); }
            for (int i = 0; i < Core.Stats.Count; i++) bonuses[i] = 0f;
            Gold.Value = 0;
            Embers.Value = 0;
            WeaponLevel.Value = 0;
            WeaponPath.Value = 0;
            EdgeBits.Value = 0;
            EdgeRespecced.Value = false;
            Hones.Value = 0;
            ServerMomentumUntil = 0f;
            ActiveBlessing.Value = 0;
            Sigils.Value = 0;
            RankQ.Value = RankE.Value = 1;
            RankChoice.Value = 0;
            Armor.Value = default;
            runeBag.Clear();
            bag.Clear();
            CatPeed.Value = false;
            CatWhisker.Value = false;
            Grudges.Value = 0;
            PrayedFor.Value = false;
            Drills.Value = 0;
            Parts.Value = 0;
            ScoutMap.Value = MapRevealed.Value = Salts.Value = Rope.Value = false;
            ResetBuildRun();
            StockRerolls.Value = 0;
            ShrineRerolls.Value = 0;
            Socket0.Value = Socket1.Value = Socket2.Value = 0;
            Charm.Value = default;
            health.HealMultiplier = 1f;
            Kills.Value = DamageDealt.Value = GoldEarned.Value = Downs.Value = Revives.Value = CombosTriggered.Value = 0;
            DamageTaken.Value = Crits.Value = AbyssKicks.Value = 0;
            damageCarry = 0f;
            relics.Clear();
            ToothMarked.Clear();
            RallyUntil.Value = 0;
            ServerNewFloor();
            ApplyToHealth(refill: true);
        }

        private float bannerVigorUntil, bannerVigor;
        private Coroutine bannerVigorEnd;

        public void ServerBannerVigor(float amount, float seconds)
        {
            if (!IsServer) return;
            bannerVigor = amount;
            bannerVigorUntil = Time.time + seconds;
            OnStatsChangedServer(StatType.HP);
            if (bannerVigorEnd != null) StopCoroutine(bannerVigorEnd);
            bannerVigorEnd = StartCoroutine(EndBannerVigor(seconds));
        }

        private System.Collections.IEnumerator EndBannerVigor(float seconds)
        {
            yield return new WaitForSeconds(seconds + 0.05f);
            bannerVigorEnd = null;
            ApplyToHealth(refill: false);
        }

        public bool BannerVigorOn => Time.time < bannerVigorUntil;

        private void OnStatsChangedServer(StatType? stat)
        {
            float before = health.Max.Value;
            ApplyToHealth(refill: false);
            if (stat == null || stat == StatType.HP) health.ServerHeal(health.Max.Value - before);
        }

        private void ApplyToHealth(bool refill)
        {
            float hp = Stats.GetValue(StatType.HP);
            if (HasRelic(Relic.HeartOfTheDeep)) hp *= 1f + 0.25f * RelicPower(Relic.HeartOfTheDeep);
            if (HasRelic(Relic.BloodPact)) hp *= 0.75f;
            if (Blessed(Blessing.Vigor)) hp *= 1.25f;
            if (CatPeed.Value) hp *= CatPeeHealth;
            if (MirrorCursed.Value) hp *= MirrorHealth;
            hp *= BuildHealthScale;
            if (Time.time < bannerVigorUntil) hp += bannerVigor;
            health.ServerSetMax(hp, refill);
            health.Defense = Stats.GetValue(StatType.DEF) + Lining * Crafting.LiningDefense;
        }
    }
}
