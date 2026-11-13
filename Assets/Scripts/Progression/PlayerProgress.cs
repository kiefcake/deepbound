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
    /// <summary>
    /// A player's run-scoped power: the stat block, the class weapon's level, bonuses, relics,
    /// gold and embers. Everything here is wiped when the run ends — the game's central rule.
    ///
    /// <para>
    /// The authoritative <see cref="StatBlock"/> lives on the server. Bonuses are mirrored
    /// into a network list so every peer can compute the same stat values: the owner needs
    /// SPD for movement and DEX for swing speed without asking the server, and the HUD's
    /// stat panel needs all eight.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Health))]
    public partial class PlayerProgress : NetworkBehaviour
    {
        public readonly NetworkVariable<int> Gold = new NetworkVariable<int>();

        /// <summary>Weapon upgrade material, spent at the anvil.</summary>
        public readonly NetworkVariable<int> Embers = new NetworkVariable<int>();

        /// <summary>The class weapon's anvil level, 0 to <see cref="World.Anvil.MaxLevel"/>.</summary>
        public readonly NetworkVariable<int> WeaponLevel = new NetworkVariable<int>();

        /// <summary>Ability rank-ups, spent at the anvil.</summary>
        public readonly NetworkVariable<int> Sigils = new NetworkVariable<int>();

        // ---- The castle cat's verdict, kept until the run ends ----
        /// <summary>Picked the cat up without petting it first: it peed on you. -5% max health this run.</summary>
        public readonly NetworkVariable<bool> CatPeed = new NetworkVariable<bool>();
        /// <summary>Petted, then picked up: it gave you a whisker. 1% chance to evade any hit this run (one per run).</summary>
        public readonly NetworkVariable<bool> CatWhisker = new NetworkVariable<bool>();

        /// <summary>
        /// How much each camp character resents you (3 bits per <see cref="World.FolkId"/>, 0..4), from
        /// hitting them. Server-written; eases by one per return to camp, cleared with the run.
        /// </summary>
        public readonly NetworkVariable<int> Grudges = new NetworkVariable<int>();

        private System.Collections.IEnumerator AddPrayerLast()
        {
            yield return null;
            yield return null;
            if (health != null) health.IncomingModifiers.Add(PrayerModifier);
        }

        /// <summary>Bought Brother Aldric's prayer: the next blow that would down you leaves you at 1 health instead.</summary>
        public readonly NetworkVariable<bool> PrayedFor = new NetworkVariable<bool>();

        /// <summary>Training-yard drills taken this run: four bits per stat (see Shop.Drills).</summary>
        public readonly NetworkVariable<ulong> Drills = new NetworkVariable<ulong>();
        public int DrillCount(StatType s) => (int)((Drills.Value >> (4 * (int)s)) & 0xF);

        /// <summary>Bought Corwin's map: the next floor is charted from the start. MapRevealed: this floor is.</summary>
        public readonly NetworkVariable<bool> ScoutMap = new NetworkVariable<bool>();
        public readonly NetworkVariable<bool> MapRevealed = new NetworkVariable<bool>();
        /// <summary>Smelling salts: going down, you come round on your own (once).</summary>
        public readonly NetworkVariable<bool> Salts = new NetworkVariable<bool>();
        /// <summary>A climbing rope from Corwin: one retreat to camp from a floor's waystone.</summary>
        public readonly NetworkVariable<bool> Rope = new NetworkVariable<bool>();

        public const float RopeRange = 3.2f;

        /// <summary>Owner: can I climb out here, now?</summary>
        public bool CanUseRope => Rope.Value && World.DungeonDirector.Instance != null && World.DungeonDirector.Instance.CanRetreat && World.DungeonDirector.Layout != null
            && Vector3.Distance(transform.position, World.DungeonDirector.Layout.WaystonePosition) <= RopeRange;

        /// <summary>Owner → server: climb the rope (the whole party goes up).</summary>
        [Rpc(SendTo.Server)]
        public void UseRopeRpc()
        {
            if (!CanUseRope) return;
            Rope.Value = false;
            World.DungeonDirector.Instance.ServerRetreat(name);
        }
        /// <summary>How many times Hobb has re-forged your piece this run (it reseeds his offer).</summary>
        public readonly NetworkVariable<byte> StockRerolls = new NetworkVariable<byte>();

        /// <summary>Server, last in the incoming chain: a paid prayer catches a killing blow, once.</summary>
        private float PrayerModifier(float dealt, Vector3 from)
        {
            // A shield soaks what comes after this; only a blow that would really down you spends the prayer.
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

        /// <summary>Server: one more offence against <paramref name="who"/>. Returns the new level.</summary>
        public int ServerAddGrudge(World.FolkId who)
        {
            int level = Mathf.Min(MaxGrudge, Grudge(who) + 1);
            SetGrudge(who, level);
            return level;
        }

        /// <summary>Server: time heals - every grudge eases by one (called on each return to camp).</summary>
        public void ServerEaseGrudges()
        {
            for (int i = 0; i <= (int)World.FolkId.Peddler; i++) SetGrudge((World.FolkId)i, Grudge((World.FolkId)i) - 1);
        }

        /// <summary>A vendor's mark-up for you: +25% per grudge level.</summary>
        public float PriceMultiplier(Shop.Vendor vendor) => (1f + 0.25f * Grudge(VendorFolk(vendor))) * World.DungeonDirector.OmenPrices
            * (World.DungeonDirector.InLobby ? World.DungeonDirector.FavourPriceScale(VendorFolk(vendor)) : 1f)
            * (HasRelic(Relic.PilgrimsPurse) && World.DungeonDirector.InLobby ? Mathf.Max(0.55f, 1f - 0.15f * RelicPower(Relic.PilgrimsPurse)) : 1f);

        /// <summary>Who stands behind a vendor's counter right now (the peddler, off camp).</summary>
        public static World.FolkId VendorFolk(Shop.Vendor vendor) => vendor == Shop.Vendor.Smith ? World.FolkId.Hobb : vendor == Shop.Vendor.Shrine ? World.FolkId.Aldric
            : vendor == Shop.Vendor.Carriage ? World.FolkId.Corwin : vendor == Shop.Vendor.Yard ? World.FolkId.Brann : vendor == Shop.Vendor.Runewright ? World.FolkId.Ysolde
            : World.DungeonDirector.InLobby ? World.FolkId.Silas : World.FolkId.Peddler;

        /// <summary>Owner -> server: you start or stop talking/trading with <paramref name="who"/> (they hold still meanwhile).</summary>
        [Rpc(SendTo.Server)]
        public void TalkHoldRpc(byte who, bool on)
        {
            var d = World.DungeonDirector.Instance;
            if (d == null) return;
            if (on && (!World.DungeonDirector.InLobby || !World.FolkPlaces.InReach((World.FolkId)who, transform.position, 2f))) return;
            d.ServerHoldFolk((World.FolkId)who, OwnerClientId, on);
        }

        /// <summary>Owner -> server: take <paramref name="who"/>'s quest while talking to them (party-wide).</summary>
        [Rpc(SendTo.Server)]
        public void TakeQuestRpc(byte who)
        {
            var d = World.DungeonDirector.Instance;
            if (d == null || !World.DungeonDirector.InLobby || !World.FolkPlaces.InReach((World.FolkId)who, transform.position, 2f)) return;
            if (Grudge((World.FolkId)who) >= 3) return;
            d.ServerTakeQuest((World.FolkId)who, name);
        }

        /// <summary>What it costs to make amends with a vendor (0: nothing to mend).</summary>
        public int AmendsCost(Shop.Vendor vendor)
        {
            return Grudge(VendorFolk(vendor)) * 30;
        }

        /// <summary>Owner → server: pay a vendor to forget it (at their stall).</summary>
        [Rpc(SendTo.Server)]
        public void AmendsRpc(Shop.Vendor vendor)
        {
            int cost = AmendsCost(vendor);
            if (cost <= 0 || Gold.Value < cost || !Shop.InReach(vendor, transform.position, 1f)) return;
            Gold.Value -= cost;
            var who = VendorFolk(vendor);
            SetGrudge(who, 0);
            AmendsMadeRpc((byte)who);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AmendsMadeRpc(byte who) => UI.CampChatter.Forgiven((World.FolkId)who, name);

        /// <summary>The player object whose owner is <paramref name="clientId"/>, on any peer.</summary>
        public static PlayerProgress ForClient(ulong clientId)
        {
            foreach (var life in Player.PlayerLife.All)
                if (life != null && life.OwnerClientId == clientId && life.TryGetComponent<PlayerProgress>(out var p)) return p;
            return null;
        }
        public const float CatPeeHealth = 0.95f, WhiskerEvade = 0.01f;

        /// <summary>Server: the cat has judged you.</summary>
        public void ServerCatVerdict(bool petted)
        {
            // Not petted first: it squirms free (2026-11-07, the user: "remove the pee"). No penalty any more.
            if (petted && !CatWhisker.Value) CatWhisker.Value = true;
        }
        public readonly NetworkVariable<byte> RankQ = new NetworkVariable<byte>(1);
        public readonly NetworkVariable<byte> RankE = new NetworkVariable<byte>(1);

        /// <summary>Rank 5's choice per ability: bit 0 = Q Hastened, bit 1 = E Hastened (else Overcharged).</summary>
        public readonly NetworkVariable<byte> RankChoice = new NetworkVariable<byte>();
        public bool Hastened(AbilitySlot slot) => (RankChoice.Value & (1 << (int)slot)) != 0;

        /// <summary>Effective rank: bought ranks, plus one for the Hollow Crown (max 5).</summary>
        public int Rank(AbilitySlot slot) => Mathf.Min(AbilityRanks.MaxRank, (slot == AbilitySlot.Q ? RankQ.Value : RankE.Value) + (Wears(GearId.HollowCrown) ? 1 : 0));

        /// <summary>Ranks actually bought with Sigils (what the anvil can still raise).</summary>
        public int BoughtRank(AbilitySlot slot) => slot == AbilitySlot.Q ? RankQ.Value : RankE.Value;

        /// <summary>Rank 2 and up: 30% stronger.</summary>
        public float Potency(AbilitySlot slot) => (Rank(slot) >= 2 ? AbilityRanks.Rank2Potency : 1f) * (Rank(slot) >= 4 ? AbilityRanks.Rank4Potency : 1f)
            * (Rank(slot) >= 5 && !Hastened(slot) ? AbilityRanks.OverchargedPotency : 1f);

        /// <summary>Rank 3: the twist.</summary>
        public bool Twist(AbilitySlot slot) => Rank(slot) >= 3;

        /// <summary>Q/E cooldown scale: relics and gear, and a quarter off at rank 3.</summary>
        public float CooldownFor(AbilitySlot slot) => CooldownMultiplier * (Twist(slot) ? AbilityRanks.Rank3Cooldown : 1f)
            * (Rank(slot) >= 5 && Hastened(slot) ? AbilityRanks.HastenedCooldown : 1f);

        public void ServerAddSigil()
        {
            if (!IsServer) return;
            Sigils.Value++;
            SigilRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SigilRpc() => UI.GameHud.Feed(name + " found a Sigil  -  rank an ability at Ysolde's stones", LootInfo.SigilColor);

        /// <summary>Owner → server: spend a Sigil on Q or E, at the anvil.</summary>
        [Rpc(SendTo.Server)]
        public void EmpowerRpc(byte slot) => ServerEmpowerAbility(slot, false);

        /// <summary>Owner -> server: the final rank, taken as HASTENED (EmpowerRpc takes it Overcharged).</summary>
        [Rpc(SendTo.Server)]
        public void EmpowerHastenedRpc(byte slot) => ServerEmpowerAbility(slot, true);

        private void ServerEmpowerAbility(byte slot, bool hastened)
        {
            if (!AtRunewright || slot > 1) return;
            var rank = slot == 0 ? RankQ : RankE;
            if (rank.Value >= AbilityRanks.MaxRank || Rank((AbilitySlot)slot) >= AbilityRanks.MaxRank) return;
            int cost = AbilityRanks.SigilCost(Rank((AbilitySlot)slot));
            if (Sigils.Value < cost) return;
            Sigils.Value -= cost;
            rank.Value++;
            if (rank.Value >= AbilityRanks.MaxRank)
                RankChoice.Value = (byte)(hastened ? RankChoice.Value | (1 << slot) : RankChoice.Value & ~(1 << slot));
            MeldedFxRpc();
            EmpoweredRpc(slot, rank.Value);
        }

        /// <summary>Owner → server: with both abilities at their top rank, a spare Sigil melts into embers.</summary>
        [Rpc(SendTo.Server)]
        public void MeltSigilRpc()
        {
            if (!AtRunewright || Sigils.Value < 1 || Rank(AbilitySlot.Q) < AbilityRanks.MaxRank || Rank(AbilitySlot.E) < AbilityRanks.MaxRank) return;
            Sigils.Value--;
            Embers.Value += AbilityRanks.MeltEmbers;
            MeldedFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EmpoweredRpc(byte slot, byte rank)
        {
            string ability = ClassDefinition.AbilityName(ClassId.Value, (AbilitySlot)slot);
            UI.GameHud.Feed(name + "'s " + ability + " reaches rank " + rank, LootInfo.SigilColor);
        }

        /// <summary>The weapon's chosen path: 0 = not yet, 1 or 2. See <see cref="WeaponPaths"/>.</summary>
        public readonly NetworkVariable<byte> WeaponPath = new NetworkVariable<byte>();

        public bool OnPath(int path) => WeaponPath.Value == path;

        /// <summary>Forged edges (see <see cref="Edges"/>), one byte per slot: slot s in bits 8s..8s+7.</summary>
        public readonly NetworkVariable<int> EdgeBits = new NetworkVariable<int>();

        /// <summary>Hones past +10 (see <see cref="World.Anvil.MaxHones"/>).</summary>
        public readonly NetworkVariable<byte> Hones = new NetworkVariable<byte>();

        /// <summary>Server: when Momentum's bonus runs out (Time.time).</summary>
        public float ServerMomentumUntil;

        public Edge EdgeAt(int slot) => (Edge)((EdgeBits.Value >> (8 * slot)) & 0xFF);

        public bool HasEdge(Edge e)
        {
            for (int s = 0; s < Edges.Slots; s++) if (EdgeAt(s) == e) return true;
            return false;
        }

        /// <summary>The first edge slot the weapon has reached but not filled, or -1.</summary>
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

        /// <summary>The three edges on offer for <paramref name="slot"/> (any peer: the same seed on both sides).</summary>
        public Edge[] EdgeOffer(int slot) => Edges.Offer(RunSeed, OwnerClientId, slot, EdgeBits.Value);

        /// <summary>Owner -> server: forge one of the offered edges (at the anvil).</summary>
        [Rpc(SendTo.Server)]
        public void ChooseEdgeRpc(byte edge)
        {
            if (World.Anvil.Near(transform.position) == null) return;
            ServerChooseEdge((Edge)edge);
        }

        /// <summary>Server: fill the open edge slot with <paramref name="e"/> if it is on offer.</summary>
        public bool ServerChooseEdge(Edge e)
        {
            int slot = OpenEdgeSlot;
            if (!IsServer || slot < 0 || System.Array.IndexOf(EdgeOffer(slot), e) < 0) return false;
            EdgeBits.Value |= (int)e << (8 * slot);
            // The guard edges that are simply more of you (2026-11-12).
            if (e == Edge.Bastion) ServerAddBonus(StatType.HP, Edges.BastionHP);
            if (e == Edge.Ironhide) ServerAddBonus(StatType.DEF, Edges.IronhideDEF);
            EdgeChosenRpc((byte)e);
            return true;
        }

        /// <summary>
        /// RESPEC (2026-11-13, the user: "A paid option at Ysolde or Hobb to swap one edge per run, so a bad early pick isn't
        /// fatal"): at Hobb's anvil, once a run, for <see cref="RespecCost"/> gold, one forged edge comes out and its slot opens
        /// again on the same offer.
        /// </summary>
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
            if (World.Anvil.Near(transform.position) == null) return;
            ServerRespecEdge(slot);
        }

        /// <summary>Server: take the edge in <paramref name="slot"/> back out (tests call this).</summary>
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

        /// <summary>Why the weapon cannot be honed now, or null.</summary>
        public string CannotHoneReason()
        {
            if (WeaponLevel.Value < World.Anvil.MaxLevel) return "Temper to +" + World.Anvil.MaxLevel + " first";
            if (Hones.Value >= World.Anvil.MaxHones) return "Honed to the limit";
            if (OpenEdgeSlot >= 0) return "Choose an edge first";
            if (Embers.Value < World.Anvil.HoneEmbers(Hones.Value)) return "Not enough embers";
            if (Gold.Value < World.Anvil.HoneGold(Hones.Value)) return "Not enough gold";
            return null;
        }

        /// <summary>Owner -> server: hone a +10 weapon at the anvil you stand by.</summary>
        [Rpc(SendTo.Server)]
        public void HoneRpc()
        {
            var anvil = World.Anvil.Near(transform.position);
            if (anvil != null && ServerHone()) anvil.WorkFxRpc(World.Anvil.Work.Grindstone, 1f); // the grindstone's work (2026-11-12)
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

        /// <summary>The shrine's blessing for the current floor (see <see cref="Blessings"/>).</summary>
        public readonly NetworkVariable<byte> ActiveBlessing = new NetworkVariable<byte>();
        public bool Blessed(Blessing b) => ActiveBlessing.Value == (byte)b;

        /// <summary>How often this player has asked the shrine for other relics this run.</summary>
        public readonly NetworkVariable<byte> ShrineRerolls = new NetworkVariable<byte>();

        public void ServerBless(Blessing b)
        {
            if (!IsServer) return;
            ActiveBlessing.Value = (byte)b;
            // Fortune pays out late in a run too: gold piles up by then, a Sigil never goes to waste.
            if (b == Blessing.Fortune) ServerAddSigil();
            OnStatsChangedServer(StatType.HP);
        }

        /// <summary>Server: the floor is over (or the run): the blessing fades.</summary>
        public void ServerClearBlessing()
        {
            if (!IsServer || ActiveBlessing.Value == 0) return;
            ActiveBlessing.Value = 0;
            ApplyToHealth(refill: false);
        }

        // ---- Gear: one armour, one charm. Stats derive from the item on every peer. ----
        public readonly NetworkVariable<GearItem> Armor = new NetworkVariable<GearItem>();
        public readonly NetworkVariable<GearItem> Charm = new NetworkVariable<GearItem>();

        public GearItem Equipped(GearSlot slot) => slot == GearSlot.Armor ? Armor.Value : Charm.Value;

        // ---- The pack: spare gear carried for the run (Dark and Darker's stash, small). ----
        public const int BagSize = 8;
        private NetworkList<GearItem> bag;
        public int BagCount => bag.Count;
        public GearItem BagItem(int i) => i >= 0 && i < bag.Count ? bag[i] : default;
        public bool BagFull => bag.Count >= BagSize;

        /// <summary>Server: put a piece in the pack. False if it is full.</summary>
        public bool ServerStow(GearItem item)
        {
            if (!IsServer || item.IsEmpty || bag.Count >= BagSize) return false;
            bag.Add(item);
            return true;
        }

        private bool CanManagePack(int i) => IsServer && i >= 0 && i < bag.Count && !GetComponent<Player.PlayerLife>().Downed.Value;
        /// <summary>Scrapping is smith's work: only at the anvil.</summary>
        private bool AtAnvil => World.Anvil.Near(transform.position) != null;

        /// <summary>Owner → server: wear a piece from the pack; what was worn takes its place.</summary>
        [Rpc(SendTo.Server)]
        public void BagEquipRpc(int i)
        {
            if (!CanManagePack(i)) return;
            var item = bag[i];
            bag.RemoveAt(i);
            var old = ServerEquip(item);
            if (!old.IsEmpty) bag.Insert(Mathf.Min(i, bag.Count), old);
        }

        /// <summary>Owner → server: break a piece in the pack down into embers.</summary>
        [Rpc(SendTo.Server)]
        public void BagScrapRpc(int i)
        {
            if (!CanManagePack(i) || !AtAnvil) return;
            var item = bag[i];
            bag.RemoveAt(i);
            ServerAddEmbers(Gear.ScrapValue(item));
            ServerAddPart(Part.Iron, 1 + item.Quality);
            PackFxRpc(0);
            var smithy = World.Anvil.Near(transform.position);
            if (smithy != null) smithy.WorkFxRpc(World.Anvil.Work.Hearth, 1f); // melted down in the forge (2026-11-12)
        }

        /// <summary>Owner → server: set a piece down on the floor (yours alone to see, as ever).</summary>
        [Rpc(SendTo.Server)]
        public void BagDropRpc(int i)
        {
            if (!CanManagePack(i)) return;
            var item = bag[i];
            bag.RemoveAt(i);
            // Put down for anyone: a friend can pick it up (play-test 2026-11-01).
            GearDrop.ServerSpawn(item, GearDrop.Anyone, transform.position + transform.forward * 1.2f + Vector3.up * 0.05f);
        }

        /// <summary>Owner → server: take a worn piece off into the pack.</summary>
        [Rpc(SendTo.Server)]
        public void UnequipRpc(byte slot)
        {
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

        /// <summary>Server: put a piece on. Returns what was in its slot (maybe empty).</summary>
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

        /// <summary>
        /// Owner → server, at the anvil: improve a worn piece. Mode 0 refines its quality,
        /// mode 1 attunes it to the current floor's depth. Paid in embers.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void ReforgeRpc(byte slot, byte mode)
        {
            var anvil = World.Anvil.Near(transform.position);
            if (anvil == null || slot > 1) return;
            var item = Equipped((GearSlot)slot);
            int depth = World.DungeonDirector.GearDepth;
            int cost = mode == 0 ? Gear.RefineCost(item) : Gear.AttuneCost(item, depth);
            if (cost <= 0 || Embers.Value < cost) return;
            Embers.Value -= cost;
            var better = mode == 0 ? new GearItem(item.Kind, item.Quality + 1, item.Depth) : new GearItem(item.Kind, item.Quality, depth);
            ServerEquip(better);
            // Refining is the grindstone's work, attuning the forge's (2026-11-12).
            anvil.WorkFxRpc(mode == 0 ? World.Anvil.Work.Grindstone : World.Anvil.Work.Hearth, 0.7f);
        }

        /// <summary>Server: a dodge readies the Gravecloth Mantle's strike.</summary>
        public const float MartyrDamage = 0.45f, MartyrHeal = 0.85f, WraithstepSpeed = 1.4f, WraithstepSeconds = 1.5f;
        /// <summary>Owner: the Wraithstep Cuirass's burst after a dodge (speed is worked out on the owner).</summary>
        public float WraithstepUntil { get; set; } = -1f;
        public bool Wraithstepping => Time.time < WraithstepUntil;

        public void ServerOnDodge()
        {
            if (Wears(GearId.GraveclothMantle)) dodgeStrikeUntil = Time.time + 2f;
        }

        /// <summary>Server: true once, if a Mantle strike is ready.</summary>
        public bool ServerConsumeDodgeStrike()
        {
            if (Time.time > dodgeStrikeUntil) return false;
            dodgeStrikeUntil = 0f;
            return true;
        }

        /// <summary>Thornhide Jerkin: regeneration while badly hurt, even mid-fight.</summary>
        public bool DesperateRegen => Wears(GearId.ThornhideJerkin) && health.Fraction < 0.4f;
        public bool Mastered(int path) => WeaponPath.Value == path && WeaponLevel.Value >= WeaponPaths.MasteryLevel;

        // ---- Run statistics, for the end-of-run summary. Server-written, reset with the run. ----
        public readonly NetworkVariable<int> Kills = new NetworkVariable<int>();
        public readonly NetworkVariable<int> DamageDealt = new NetworkVariable<int>();
        public readonly NetworkVariable<int> GoldEarned = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Downs = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Revives = new NetworkVariable<int>();
        public readonly NetworkVariable<int> CombosTriggered = new NetworkVariable<int>();
        /// <summary>For the run summary's titles: damage soaked, critical hits, enemies sent into the abyss.</summary>
        public readonly NetworkVariable<int> DamageTaken = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Crits = new NetworkVariable<int>();
        public readonly NetworkVariable<int> AbyssKicks = new NetworkVariable<int>();
        private float takenCarry;

        /// <summary>Server: tally damage this player took.</summary>
        public void ServerCreditTaken(float dealt)
        {
            takenCarry += dealt;
            int whole = Mathf.FloorToInt(takenCarry);
            if (whole > 0) { takenCarry -= whole; DamageTaken.Value += whole; }
        }

        /// <summary>Server: this player set off a class combo.</summary>
        public void ServerCreditCombo() => CombosTriggered.Value++;
        private float damageCarry;

        /// <summary>Server: credit damage this player dealt, and a kill if it was the final blow.</summary>
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
                // Multi-kills: kills in quick succession are called out to the party.
                if (Time.time - lastKillAt > 1.6f) killChain = 0;
                lastKillAt = Time.time;
                killChain++;
                if (killChain >= 2) MultiKillRpc((byte)Mathf.Min(killChain, 6));
            }
        }

        /// <summary>
        /// The player's class. Owner-written, but only honoured in the lobby: the HUD only
        /// offers the choice there, and the server re-checks.
        /// </summary>
        public readonly NetworkVariable<ClassId> ClassId = new NetworkVariable<ClassId>(Classes.ClassId.Knight,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Can the class change now? Only at camp, before a run begins (never between floors).</summary>
        public static bool ClassChangeAllowed => World.DungeonDirector.InLobby && (World.DungeonDirector.Instance == null || !World.DungeonDirector.Instance.RunInProgress);

        /// <summary>
        /// Joined a run already under way: they spawned as the default Knight with the class screen locked and nothing said why.
        /// They choose at camp, until the carriage next leaves.
        /// </summary>
        public readonly NetworkVariable<bool> LateJoiner = new NetworkVariable<bool>();

        public bool CanChangeClass => ClassChangeAllowed || (World.DungeonDirector.InLobby && LateJoiner.Value);

        /// <summary>Owner → server: pick a class. Refused once the run has begun.</summary>
        [Rpc(SendTo.Server)]
        public void ChooseClassRpc(ClassId id)
        {
            if (!CanChangeClass || (int)id < 0 || (int)id >= System.Enum.GetValues(typeof(ClassId)).Length) return;
            ClassId.Value = id;
        }
        private NetworkList<float> bonuses;
        private NetworkList<byte> runeBag;
        public readonly NetworkVariable<byte> Socket0 = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Socket1 = new NetworkVariable<byte>();
        /// <summary>The third socket, opened at weapon +6.</summary>
        public readonly NetworkVariable<byte> Socket2 = new NetworkVariable<byte>();

        public Rune Socketed(int socket) => (Rune)(socket == 0 ? Socket0.Value : socket == 1 ? Socket1.Value : Socket2.Value);
        /// <summary>Is a rune of this kind (any tier) set in the weapon?</summary>
        public bool HasRune(Rune r) => RuneTier(r) >= 0;
        /// <summary>The best tier of this kind set in the weapon, or -1.</summary>
        public int RuneTier(Rune kind)
        {
            // RUNE SPECIALISATION (2026-11-13): the first socket is the MAJOR socket - a rune there counts double
            // (an "effective tier" of 2t + 2, so Runes.Power doubles: 1 -> 2, 1.5 -> 3, 2 -> 4).
            int best = -1;
            kind = Runes.Kind(kind);
            for (int s = 0; s < Runes.MaxSockets; s++)
            {
                var r = Socketed(s);
                if (r != Rune.None && Runes.Kind(r) == kind) best = Mathf.Max(best, s == Runes.MajorSocket ? Runes.Tier(r) * 2 + 2 : Runes.Tier(r));
            }
            return best;
        }

        /// <summary>How many of exactly this rune (kind and tier) are in the bag.</summary>
        public int RuneCount(Rune r)
        {
            int n = 0;
            foreach (var b in runeBag) if (b == (byte)r) n++;
            return n;
        }

        /// <summary>At Ysolde's stones (runes, sigils, ability ranks are her work).</summary>
        public bool AtRunewright => Shop.InReach(Shop.Vendor.Runewright, transform.position, 1f);

        /// <summary>Owner -> server: meld three of one rune into one of the next tier, for embers.</summary>
        [Rpc(SendTo.Server)]
        public void MeldRunesRpc(byte rune)
        {
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

        /// <summary>
        /// Owner → server: set a rune from the bag into the first open empty socket, or, if
        /// both are full, into the first socket (its old rune goes back into the bag).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void SocketRuneRpc(byte rune, sbyte socketIndex = -1)
        {
            int sockets = Runes.SocketsAt(WeaponLevel.Value);
            int bagIndex = runeBag.IndexOf(rune);
            if (!AtRunewright || sockets == 0 || bagIndex < 0) return;
            // Into the socket the player picked; else the first empty one; else the first.
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

        // Server-side relic state.
        private float boneWardReadyAt;
        private bool phoenixSpent;

        /// <summary>Server only.</summary>
        public StatBlock Stats { get; private set; }

        public ClassDefinition Class => ClassDefinition.For(ClassId.Value);

        /// <summary>Raised on every peer when any stat input changes. Owner-side systems re-read stats on this.</summary>
        public event Action StatsChanged;

        private Health health;

        private void Awake()
        {
            health = GetComponent<Health>();
            // NetworkLists must exist before spawn, and NGO requires them constructed in
            // Awake or at declaration so they are registered with the behaviour.
            bonuses = new NetworkList<float>();
            relics = new NetworkList<byte>();
            runeBag = new NetworkList<byte>();
            bag = new NetworkList<GearItem>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) { StartCoroutine(AddPrayerLast()); PathTriads.EnsureHook(); }
            if (IsServer && World.DungeonDirector.Instance != null && World.DungeonDirector.Instance.RunInProgress) LateJoiner.Value = true;
            WeaponLevel.OnValueChanged += (_, __) => StatsChanged?.Invoke();
            bonuses.OnListChanged += _ => StatsChanged?.Invoke();
            relics.OnListChanged += _ => StatsChanged?.Invoke();
            Armor.OnValueChanged += (_, __) => StatsChanged?.Invoke();
            Charm.OnValueChanged += (_, __) => StatsChanged?.Invoke();
            ClassId.OnValueChanged += (_, __) =>
            {
                // A new class is a new character: fresh stat block, nothing carried over.
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

        // ------------------------------------------------------------------
        // Reads (any peer)
        // ------------------------------------------------------------------

        /// <summary>
        /// The effective stat value, computed from synced inputs with the same formula as
        /// <see cref="StatBlock.GetValue"/>. On the server the two always agree: everything
        /// beyond the class base (weapon levels, gear, purchases) is a synced bonus.
        /// </summary>
        public float Value(StatType stat)
        {
            int i = (int)stat;
            float bonus = i < bonuses.Count ? bonuses[i] : 0f;
            return Mathf.Max(0f, Class.BaseStats[i] + bonus + Gear.StatValue(Armor.Value, stat) + Gear.StatValue(Charm.Value, stat));
        }

        /// <summary>
        /// RotMG ATT scaling (0 ATT is half damage, 50 ATT is double base), then damage
        /// relics. Every kit multiplies by this, so relics apply to every class for free.
        /// </summary>
        /// <summary>
        /// How far your attacks reach, as a multiple of the class's base range: every temper at the
        /// anvil adds 5%, a Farsight rune 25%. Any peer (it reads synced values), so visuals agree.
        /// </summary>
        public float RangeMultiplier => 1f + 0.05f * WeaponLevel.Value + (HasRune(Loot.Rune.Farsight) ? 0.25f * Runes.Power(RuneTier(Loot.Rune.Farsight)) : 0f) + (HasRelic(Relic.LongShadow) ? RelicPower(Relic.LongShadow) * 0.15f : 0f);

        /// <summary>Melee gains half as much reach from the same sources.</summary>
        public float ReachMultiplier => 1f + (RangeMultiplier - 1f) * 0.5f;

        /// <summary>Server time the blood altar's Bloodlust (+15% damage) ends.</summary>
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

        /// <summary>SPD 50 is the reference speed; each point either side is ±1%.</summary>
        public float SpeedMultiplier => (1f + (Value(StatType.SPD) - 50f) / 100f) * (HasRelic(Relic.GhostLantern) ? 1f + 0.15f * RelicPower(Relic.GhostLantern) : 1f) * (Blessed(Blessing.Swiftness) ? 1.12f : 1f)
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

        /// <summary>Q and E cooldowns scale by this (Echo Stone).</summary>
        /// <summary>
        /// Q and E cooldowns scale by this. Relics, the blessing and edges ADD up and stop at
        /// <see cref="MaxCooldownCut"/> (2026-10-23: four sources multiplied down to a third of a cooldown,
        /// and abilities stopped being decisions). Ability ranks cut on top of this.
        /// </summary>
        public float CooldownMultiplier => (1f - Mathf.Min(MaxCooldownCut,
            (HasRelic(Relic.EchoStone) ? 0.2f * RelicPower(Relic.EchoStone) : 0f) + (Blessed(Blessing.Swiftness) ? 0.15f : 0f) + (HasEdge(Edge.Quickening) ? Edges.QuickeningCut : 0f)))
            * World.DungeonDirector.OmenCooldown * BuildCooldownScale;
        public const float MaxCooldownCut = 0.4f;

        /// <summary>Rallying Bell: synced so speed and damage agree on every peer.</summary>
        public readonly NetworkVariable<double> RallyUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<float> RallyPower = new NetworkVariable<float>(1f);
        public bool Rallied => NetworkManager != null && RallyUntil.Value > NetworkManager.ServerTime.Time;

        public void ServerRally(float seconds, float power)
        {
            if (!IsServer) return;
            RallyPower.Value = Mathf.Max(Rallied ? RallyPower.Value : 0f, power);
            RallyUntil.Value = NetworkManager.ServerTime.Time + seconds;
        }

        /// <summary>Server: Hunter's Tooth marks the first hit on each elite or boss (by enemy id).</summary>
        public readonly System.Collections.Generic.HashSet<ulong> ToothMarked = new System.Collections.Generic.HashSet<ulong>();

        /// <summary>Server -> owner: the Cracked Hourglass takes seconds off Q and E (cooldowns are timed on the owner).</summary>
        [Rpc(SendTo.Owner)]
        public void ShaveCooldownsRpc(float seconds)
        {
            var kit = GetComponent<Player.NetworkPlayer>()?.ActiveKit;
            kit?.ShaveCooldowns(seconds);
        }

        /// <summary>
        /// Gold found: the bonuses ADD (2026-10-23, the autoplay run: a Gilded Skull III, the Ledger and Fortune
        /// multiplied to x3 and a solo run ended on 15,000 gold), and gold relics grow half as much per tier.
        /// </summary>
        public float GoldMultiplier => 1f + (HasRelic(Relic.GildedSkull) ? 0.6f + 0.3f * RelicTier(Relic.GildedSkull) : 0f) + (Blessed(Blessing.Fortune) ? 0.5f : 0f)
            + (HasRelic(Relic.TallymansLedger) ? 0.2f + 0.1f * RelicTier(Relic.TallymansLedger) : 0f) + BuildGoldBonus;

        // ---- Relics ----

        // A relic byte: the id in the low five bits, its tier (0-2) in the next two.
        private static Relic IdOf(byte b) => (Relic)(b & 31);
        private static int TierOf(byte b) => (b >> 5) & 3;

        public bool HasRelic(Relic r)
        {
            foreach (var b in relics) if (IdOf(b) == r) return true;
            return false;
        }

        public int RelicCount => relics.Count;

        /// <summary>Relics held, each tier counting as one more (for sharing finds fairly).</summary>
        public int RelicWealth { get { int w = relics.Count; for (int i = 0; i < relics.Count; i++) w += RelicTierAt(i); return w; } }

        /// <summary>Ability ranks plus unspent sigils (for sharing finds fairly).</summary>
        public int SigilWealth => RankQ.Value + RankE.Value + Sigils.Value;
        public Relic RelicAt(int i) => IdOf(relics[i]);
        public int RelicTierAt(int i) => TierOf(relics[i]);

        /// <summary>0-2; 0 if not carried.</summary>
        public int RelicTier(Relic r)
        {
            foreach (var b in relics) if (IdOf(b) == r) return TierOf(b);
            return 0;
        }

        /// <summary>How strong a carried relic is: 1, 1.5 or 2 (0 if not carried).</summary>
        public float RelicPower(Relic r) => HasRelic(r) ? Relics.Power(RelicTier(r)) : 0f;

        /// <summary>"Echo Stone II".</summary>
        public string RelicLabel(int i) => Relics.Name(RelicAt(i)) + Relics.TierName(RelicTierAt(i));

        /// <summary>Would <see cref="ServerAddRelic"/> take this relic (as new, or as an empowering)?</summary>
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

        /// <returns>
        /// False if it cannot be taken (the drop stays for a teammate). A relic you carry is empowered
        /// instead; with a full tray, a new relic empowers your weakest one.
        /// </returns>
        public bool ServerAddRelic(Relic r)
        {
            if (!IsServer || !CanTakeRelic(r)) return false;
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
            relics.Add((byte)r);
            if (r == Relic.HeartOfTheDeep || r == Relic.BloodPact) OnStatsChangedServer(StatType.HP);
            RelicGainedRpc((byte)r);
            // Completing a pair awakens its synergy.
            for (int i = 0; i < Relics.Synergies.Length; i++)
            {
                var syn = Relics.Synergies[i];
                if ((syn.a == r || syn.b == r) && HasRelic(syn.a) && HasRelic(syn.b)) SynergyRpc(i);
            }
            return true;
        }

        /// <summary>Server, for tests: take a relic away again.</summary>
        public void ServerDebugRemoveRelic(Relic r)
        {
            if (!IsServer) return;
            for (int i = relics.Count - 1; i >= 0; i--) if (IdOf(relics[i]) == r) relics.RemoveAt(i);
        }

        /// <summary>Server, each floor: once-per-floor relics recharge.</summary>
        public void ServerNewFloor()
        {
            phoenixSpent = false;
            boneWardReadyAt = 0f;
            // Corwin's map is for the next floor down: it charts this one, then it is spent.
            MapRevealed.Value = ScoutMap.Value;
            ScoutMap.Value = false;
        }

        /// <summary>Server: Phoenix Feather. True if it fires (and is used up for this floor).</summary>
        /// <summary>Server: the health fraction the Phoenix raises you to.</summary>
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
                // Melee range: the attacker is whatever enemy stands nearest the hit's origin.
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
            // Iron Psalter: bolts, spit and hexes from across the room glance off the hymns.
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
                foreach (var foe in CombatQuery.Circle(transform.position, 4f, Team.Enemies).ToArray())
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
            // The Wardens' pieces (2026-10-27).
            if (Wears(GearId.AldousOath))
            {
                var to = attackerPosition - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, to) < 60f) damage *= 0.8f;
            }
            // Retribution (a guard edge, 2026-11-12): the one that struck you up close takes a share of it back.
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
                foreach (var foe in CombatQuery.Circle(transform.position, 4.5f, Team.Enemies).ToArray())
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

        /// <summary>Server: the Witchbone Fetish's mark is ready (and is now spent).</summary>
        public bool ServerConsumeFetish()
        {
            if (Time.time < fetishReadyAt) return false;
            fetishReadyAt = Time.time + 8f;
            return true;
        }

        /// <summary>Server: has this player stood (near enough) still for a second? Gravedigger's Coat.</summary>
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
            if (IsServer && Wears(GearId.GravediggersCoat)) StandingStill();
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void ZoneHitFxRpc(Vector3 at, byte zone)
        {
            // 3 = a weak point that burst (full bonus, a small blast); 2 = one still recovering (sparks only).
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

        /// <summary>Server: the Cardinal's Vestments — a pillar of holy fire where a foe fell, a moment later.</summary>
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
            foreach (var foe in CombatQuery.Circle(at, 2.6f, Team.Enemies).ToArray())
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

        /// <summary>Thunder Idol: draws a jagged bolt through each struck enemy.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void LightningFxRpc(Vector3[] points)
        {
            Art.LightningBolt.Spawn(transform.position + Vector3.up * 6f, points);
            Art.Sfx.Play(Art.Sound.Shatter, points[0], 1f, 0.6f);
        }

        /// <summary>DEX 50 is the reference rate; higher DEX shortens swing recovery, floored at 50%.</summary>
        public float AttackTimeMultiplier => Mathf.Max(0.5f, 1.5f - Value(StatType.DEX) / 50f * 0.5f) * (Pouch != null && Pouch.Bloodfury ? RareItems.BloodfuryAttackTime : 1f);

        private Player.Flasks Pouch => pouchCache != null ? pouchCache : (pouchCache = GetComponent<Player.Flasks>());
        private Player.Flasks pouchCache;

        // ------------------------------------------------------------------
        // Server API
        // ------------------------------------------------------------------

        public void ServerAddEmbers(int amount)
        {
            if (!IsServer) return;
            if (amount > 0) amount = Mathf.RoundToInt(amount * BuildEmberScale);
            Embers.Value = Mathf.Max(0, Embers.Value + amount);
        }

        /// <summary>Why the weapon cannot be tempered right now, or null. Any peer; the server re-checks.</summary>
        public string CannotTemperReason()
        {
            if (WeaponLevel.Value >= World.Anvil.MaxLevel) return "Fully tempered";
            if (WeaponLevel.Value >= WeaponPaths.PathLevel && WeaponPath.Value == 0) return "Choose a path first";
            if (OpenEdgeSlot >= 0) return "Choose an edge first";
            if (Embers.Value < World.Anvil.Cost(WeaponLevel.Value)) return "Not enough embers";
            if (Gold.Value < World.Anvil.GoldCost(WeaponLevel.Value)) return "Not enough gold";
            return null;
        }

        /// <summary>Owner → server: temper at the anvil you are standing by.</summary>
        [Rpc(SendTo.Server)]
        public void TemperRpc()
        {
            var anvil = World.Anvil.Near(transform.position);
            if (anvil != null && ServerUpgradeWeapon()) anvil.ForgeFxRpc(WeaponLevel.Value);
        }

        /// <summary>Owner → server: pick the weapon's path (once, at the anvil, from +2).</summary>
        [Rpc(SendTo.Server)]
        public void ChoosePathRpc(byte path)
        {
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

        /// <summary>Server: the armoury's whetstone, a temper with no cost. False if the weapon cannot rise (a path to choose, or the cap).</summary>
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

        /// <summary>The dark mirror's price: a fifth of your max health, until you are back at camp.</summary>
        public readonly NetworkVariable<bool> MirrorCursed = new NetworkVariable<bool>();
        public const float MirrorHealth = 0.8f;

        public void ServerSetMirrorCursed(bool on)
        {
            if (!IsServer || MirrorCursed.Value == on) return;
            MirrorCursed.Value = on;
            ApplyToHealth(refill: false);
        }

        /// <summary>Server: spend embers on the next anvil level. False if it cannot.</summary>
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

        /// <summary>A run-scoped flat bonus, e.g. a whetstone bought from the merchant.</summary>
        public void ServerAddBonus(StatType stat, float amount)
        {
            if (!IsServer) return;
            Stats.AddEquipmentBonus(stat, amount);
            bonuses[(int)stat] += amount;
            OnStatsChangedServer(stat);
        }

        // ---- The party purse: shared pickups, gifts, and evening out ----

        private static System.Collections.Generic.List<PlayerProgress> Party()
        {
            var list = new System.Collections.Generic.List<PlayerProgress>();
            foreach (var l in Player.PlayerLife.All)
                if (l != null && l.TryGetComponent<PlayerProgress>(out var p)) list.Add(p);
            return list;
        }

        /// <summary>Server: gold someone picked up. Shared loot splits it evenly (the finder keeps the odd coins).</summary>
        public static void ServerShareGold(PlayerProgress finder, int total)
        {
            var party = Party();
            if (!World.DungeonDirector.ShareLoot || party.Count <= 1) { finder.ServerAddGold(total); return; }
            int each = total / party.Count, rest = total - each * party.Count;
            foreach (var p in party) p.ServerAddGold(each + (p == finder ? rest : 0));
        }

        /// <summary>Server: embers someone picked up, split the same way.</summary>
        public static void ServerShareEmbers(PlayerProgress finder, int total)
        {
            var party = Party();
            if (!World.DungeonDirector.ShareLoot || party.Count <= 1) { finder.ServerAddEmbers(total); return; }
            total = Mathf.Max(1, Mathf.RoundToInt(total * Enemies.Difficulty.PartyEmbers));
            int each = total / party.Count, rest = total - each * party.Count;
            foreach (var p in party) p.ServerAddEmbers(each + (p == finder ? rest : 0));
        }

        /// <summary>Owner → server: hand some of your gold to an ally.</summary>
        [Rpc(SendTo.Server)]
        public void GiveGoldRpc(ulong toPlayer, int amount)
        {
            amount = Mathf.Min(amount, Gold.Value);
            if (amount <= 0 || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(toPlayer, out var obj) || !obj.TryGetComponent<PlayerProgress>(out var to) || to == this) return;
            Gold.Value -= amount;
            to.Gold.Value += amount;
            GiftRpc(name, to.name, amount + " gold");
        }

        /// <summary>Owner → server: share your gold out so nobody has less than the party's average (you give, you never take).</summary>
        [Rpc(SendTo.Server)]
        public void EvenOutGoldRpc()
        {
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

        /// <summary>Owner → server: give a piece from your pack to the nearest ally (into their pack, or at their feet if it is full).</summary>
        [Rpc(SendTo.Server)]
        public void BagGiftRpc(int i)
        {
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

        /// <summary>Why an item cannot be bought right now, or null if it can. Any peer; the server re-checks.</summary>
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

        /// <summary>Owner → server: buy item <paramref name="index"/> of <paramref name="vendor"/>'s stock (you must be at their stall).</summary>
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
                    // Laid at your feet, so you can compare it before putting it on.
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

        // The merchant's gear piece is one per floor: the seed of the floor it was bought on.
        private int gearBoughtSeed = int.MinValue;

        [Rpc(SendTo.Owner)]
        private void GearBoughtRpc(int seed) => gearBoughtSeed = seed;

        private float grazeStreakUntil;
        private int grazeStreak;
        /// <summary>Tests: grazes this session.</summary>
        public int Grazes { get; private set; }

        /// <summary>Server: an enemy shot slipped past you (<see cref="Combat.Arrows.GrazeRadius"/>): a streak and a tick (no ult charge).</summary>
        public void ServerGraze(Vector3 at)
        {
            var life = GetComponent<Player.PlayerLife>();
            if (life != null && (life.Downed.Value || !life.Targetable)) return;
            Grazes++;
            grazeStreak = Time.time < grazeStreakUntil ? grazeStreak + 1 : 1;
            grazeStreakUntil = Time.time + 2.2f;
            // 2026-11-08 (the user: "weaving will not charge ultimate anymore"): a graze is only a streak and a tick now.
            GrazeRpc(at, (byte)Mathf.Min(255, grazeStreak));
        }

        [Rpc(SendTo.Owner, Delivery = RpcDelivery.Unreliable)]
        private void GrazeRpc(Vector3 at, byte streak) => UI.GameHud.Graze(at, streak);

        /// <summary>Server → owner: one of your hits landed (or killed). Unreliable: a lost marker is no loss.</summary>
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

        /// <summary>Wipes everything the run granted. Called when a run ends or restarts.</summary>
        public void ServerResetRun()
        {
            if (!IsServer) return;
            Stats.ResetRunProgress();
            // Every run starts with a small pouch: two health potions, one of everything else.
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

        private void OnStatsChangedServer(StatType? stat)
        {
            // Raising max HP also heals by the amount added, so it is felt right now.
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
            health.ServerSetMax(hp, refill);
            health.Defense = Stats.GetValue(StatType.DEF) + Lining * Crafting.LiningDefense;
        }
    }
}
