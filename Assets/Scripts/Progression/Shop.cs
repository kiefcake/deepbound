using System.Collections.Generic;
using Deepbound.Core;
using Deepbound.Loot;

namespace Deepbound.Progression
{
    /// <summary>
    /// The merchant's catalogue. Everything bought is run-scoped, like everything else.
    ///
    /// Gold has one job — turn a good floor into a stronger next floor — so the shop sells
    /// healing, embers for the anvil next door, small stat bonuses, and two relics.
    /// Prices are fixed so players can plan around them.
    /// </summary>
    public static class Shop
    {
        /// <summary>Value 1 was the retired stat potion.</summary>
        public enum ItemKind : byte { Heal, Embers = 1, Bonus = 2, Relic = 3, Gear = 4, Sigil = 5, Flasks = 6, Potion = 7, Prayer = 8, Drill = 9, Map = 10, Salts = 11, Restock = 12, Rope = 13 }

        public struct Item
        {
            public string Name;
            public string Description;
            public int Price;
            public ItemKind Kind;
            public StatType Stat;
            public float Amount;
            /// <summary>For bonuses: the most this stat can be raised by purchases.</summary>
            public float Cap;
            /// <summary>For gear: the piece on offer.</summary>
            public Loot.GearItem Gear;
            /// <summary>Which camp vendor sells it.</summary>
            public Vendor Seller;
        }

        public const float MerchantRange = 3.2f;
        public const float StallRange = 3.6f;

        /// <summary>
        /// Who sells it. Camp trade is split by craft: the merchant keeps provisions, the smith sells
        /// what the anvil eats (embers, plate, a fitted piece, sigils), the shrine keeps holy water and
        /// relics. Down in the dungeon the travelling peddler carries only potions, marked up.
        /// </summary>
        /// <remarks>Yard: the training yard's drill board (stats for gold). Carriage: Corwin, at the carriage (maps, salts).</remarks>
        public enum Vendor : byte { Merchant = 0, Smith = 1, Shrine = 2, Yard = 3, Carriage = 4, Runewright = 5 }

        public static string VendorTitle(Vendor v) => v == Vendor.Smith ? "HOBB'S WARES" : v == Vendor.Shrine ? "THE SHRINE'S OFFERINGS"
            : v == Vendor.Yard ? "SERGEANT BRANN, DRILLMASTER" : v == Vendor.Carriage ? "CORWIN, THE DRIVER" : v == Vendor.Runewright ? "YSOLDE, RUNEWRIGHT"
            : World.DungeonDirector.InLobby ? "SILAS CRANE, MERCHANT" : "THE PEDDLER";

        public static string VendorLine(Vendor v) => v == Vendor.Smith ? "\"Embers, plate and a blade's worth of sense. Pay up.\""
            : v == Vendor.Shrine ? "\"Blessed things, for a gift to the shrine. The dead respect a full font.\""
            : v == Vendor.Yard ? "\"Pay up and fall in. Every drill's dearer than the last, because every drill's harder.\""
            : v == Vendor.Runewright ? "\"Runes, sigils and the stuff that makes them sing. Mind the stones; they bite.\""
            : v == Vendor.Carriage ? "\"I've driven folk to the door of every floor. I know a thing or two about the way down.\""
            : World.DungeonDirector.InLobby ? "\"Coin for courage, friend. It all spends the same down there.\"" : "\"Long way down to carry glass. Prices reflect it.\"";

        /// <summary>Where a vendor stands right now (null: not here).</summary>
        public static UnityEngine.Vector3? Position(Vendor v) => v == Vendor.Merchant ? World.DungeonDirector.MerchantPosition
            : !World.DungeonDirector.InLobby ? (UnityEngine.Vector3?)null
            : v == Vendor.Smith ? World.Lobby.AnvilSpot : v == Vendor.Yard ? World.Lobby.YardSpot : v == Vendor.Carriage ? World.Lobby.CarriageStall
            : v == Vendor.Runewright ? World.Lobby.RunewrightSpot : World.Lobby.ShrineSpot;

        public static float Range(Vendor v) => v == Vendor.Merchant ? MerchantRange : v == Vendor.Carriage ? 4.3f : StallRange;

        public static bool InReach(Vendor v, UnityEngine.Vector3 at, float slack = 0f)
        {
            var p = Position(v);
            return p != null && UnityEngine.Vector3.Distance(at, p.Value) <= Range(v) + slack;
        }

        /// <summary>
        /// Today's common stock for a vendor: its share of the catalogue (two relics at the shrine,
        /// chosen from the floor's seed so every peer sees the same two; the relic id rides in Amount).
        /// </summary>
        public static Item[] Current(Vendor vendor)
        {
            var director = World.DungeonDirector.Instance;
            int seed = director != null ? director.Floor.Value.x : 0;
            int depth = World.DungeonDirector.GearDepth;
            bool camp = World.DungeonDirector.InLobby;
            var list = new List<Item>();
            foreach (var it in Items)
            {
                // The peddler carries every flask and nothing else.
                if (camp ? it.Seller == vendor : vendor == Vendor.Merchant && it.Kind == ItemKind.Potion) list.Add(it);
            }
            if (camp && vendor == Vendor.Shrine)
            {
                var rng = new System.Random(seed);
                int a = rng.Next(Relics.Count), b = rng.Next(Relics.Count - 1);
                if (b >= a) b++;
                foreach (int r in new[] { a, b })
                {
                    var relic = (Relic)r;
                    list.Add(new Item { Name = Relics.Name(relic), Description = Relics.Describe(relic), Price = 110 + 30 * depth, Kind = ItemKind.Relic, Amount = r, Seller = Vendor.Shrine });
                }
            }
            if (camp && vendor == Vendor.Shrine)
                list.Add(new Item { Name = "Brother Aldric's Prayer", Description = "He prays for you, loudly. The first blow that would down you leaves you at 1 health instead (once)",
                    Price = 45 + 15 * depth, Kind = ItemKind.Prayer, Seller = Vendor.Shrine });
            float tier = World.DungeonDirector.Tier(depth);
            if (camp && vendor == Vendor.Smith)
            {
                list.Add(new Item { Name = "Commission another piece", Description = "Hobb puts away the piece he made you and forges a fresh one (a new roll)",
                    Price = UnityEngine.Mathf.RoundToInt(30 + 15 * tier), Kind = ItemKind.Restock, Seller = Vendor.Smith });
            }
            if (camp && vendor == Vendor.Carriage)
            {
                list.Add(new Item { Name = "Scout's Map", Description = "Corwin's chart of the next floor: every room on your map from the start",
                    Price = UnityEngine.Mathf.RoundToInt(40 + 20 * tier), Kind = ItemKind.Map, Seller = Vendor.Carriage });
                list.Add(new Item { Name = "Smelling Salts", Description = "If you go down, you come round on your own 5 s later, at 30% health (once)",
                    Price = UnityEngine.Mathf.RoundToInt(110 + 30 * tier), Kind = ItemKind.Salts, Seller = Vendor.Carriage });
                list.Add(new Item { Name = "Climbing Rope", Description = "From floor 2: at the start room's waystone, the party climbs back to camp. The run goes on; that floor starts over",
                    Price = UnityEngine.Mathf.RoundToInt(60 + 20 * tier), Kind = ItemKind.Rope, Seller = Vendor.Carriage });
            }
            if (camp && vendor == Vendor.Yard)
                foreach (var d in Drills) list.Add(d);
            if (camp && vendor == Vendor.Runewright)
                list.Add(new Item { Name = "Sigil", Description = "Empower an ability one rank, here at the stones", Price = 150 + 50 * depth, Kind = ItemKind.Sigil, Seller = Vendor.Runewright });
            return list.ToArray();
        }

        /// <summary>
        /// One player's stock at a vendor: the common stock plus, at the smith, a gear piece picked for
        /// them alone (seeded by floor and player, so it is stable and differs between players).
        /// </summary>
        public static Item[] For(ulong clientId, Vendor vendor)
        {
            var director = World.DungeonDirector.Instance;
            int seed = director != null ? director.Floor.Value.x : 0;
            int depth = World.DungeonDirector.GearDepth;
            var list = new List<Item>(Current(vendor));
            if (vendor == Vendor.Smith && World.DungeonDirector.InLobby)
            {
                var buyerP = PlayerProgress.ForClient(clientId);
                var rng = new System.Random(seed * 31 + (int)clientId * 7919 + 5 + (buyerP != null ? buyerP.StockRerolls.Value * 104729 : 0));
                // The smith's trade is armour: a piece fitted for you.
                var armours = Loot.Gear.RollableOf(Loot.GearSlot.Armor);
                var id = armours[rng.Next(armours.Length)];
                double r = rng.NextDouble();
                int quality = r < 0.2 ? 2 : r < 0.7 ? 1 : 0;
                var gear = new Loot.GearItem(id, quality, depth);
                list.Add(new Item { Name = Loot.Gear.Name(gear), Description = Loot.Gear.StatLine(gear) + ". " + Loot.Gear.Effect(gear.Kind), Price = 60 + 40 * quality + 25 * depth, Kind = ItemKind.Gear, Gear = gear, Seller = Vendor.Smith });
            }
            if (vendor == Vendor.Merchant && World.DungeonDirector.InLobby)
            {
                // Silas keeps a tray of pendants and charms: two, picked for you this visit.
                var buyerM = PlayerProgress.ForClient(clientId);
                var rng = new System.Random(seed * 17 + (int)clientId * 6007 + 11 + (buyerM != null ? buyerM.StockRerolls.Value * 7919 : 0));
                var charms = new List<Loot.GearId>(Loot.Gear.RollableOf(Loot.GearSlot.Charm));
                for (int k = 0; k < 2 && charms.Count > 0; k++)
                {
                    int pick = rng.Next(charms.Count);
                    var id = charms[pick];
                    charms.RemoveAt(pick);
                    double r = rng.NextDouble();
                    int quality = r < 0.15 ? 2 : r < 0.6 ? 1 : 0;
                    var gear = new Loot.GearItem(id, quality, depth);
                    list.Add(new Item { Name = "Pendant: " + Loot.Gear.Name(gear), Description = Loot.Gear.StatLine(gear) + ". " + Loot.Gear.Effect(gear.Kind), Price = 55 + 35 * quality + 20 * depth, Kind = ItemKind.Gear, Gear = gear, Seller = Vendor.Merchant });
                }
            }
            var buyer = PlayerProgress.ForClient(clientId);
            // Drills cost more each time you take one.
            if (vendor == Vendor.Yard && buyer != null)
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Kind == ItemKind.Drill)
                    {
                        var it = list[i];
                        it.Price += DrillStep * buyer.DrillCount(it.Stat);
                        list[i] = it;
                    }
            // The peddler charges for the trip; a vendor you have hit charges you for that.
            float mult = !World.DungeonDirector.InLobby ? PeddlerMarkup : 1f;
            if (buyer != null) mult *= buyer.PriceMultiplier(vendor);
            if (mult != 1f)
                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i];
                    it.Price = UnityEngine.Mathf.RoundToInt(it.Price * mult);
                    list[i] = it;
                }
            return list.ToArray();
        }

        /// <summary>
        /// The training yard's drills: a stat for the run, each one dearer than the last (Cap = how many
        /// times). Gold's long-term sink: there is always one more drill to pay for.
        /// </summary>
        public static readonly Item[] Drills =
        {
            new Item { Name = "Sword Drill", Description = "+2 ATT for the run", Price = 60, Kind = ItemKind.Drill, Stat = StatType.ATT, Amount = 2f, Cap = 6, Seller = Vendor.Yard },
            new Item { Name = "Shield Drill", Description = "+1 DEF for the run", Price = 55, Kind = ItemKind.Drill, Stat = StatType.DEF, Amount = 1f, Cap = 6, Seller = Vendor.Yard },
            new Item { Name = "Footwork", Description = "+3 DEX for the run (faster attacks)", Price = 50, Kind = ItemKind.Drill, Stat = StatType.DEX, Amount = 3f, Cap = 6, Seller = Vendor.Yard },
            new Item { Name = "Endurance Run", Description = "+15 max HP for the run", Price = 50, Kind = ItemKind.Drill, Stat = StatType.HP, Amount = 15f, Cap = 6, Seller = Vendor.Yard },
            new Item { Name = "Breathing", Description = "+10 max MP for the run", Price = 40, Kind = ItemKind.Drill, Stat = StatType.MP, Amount = 10f, Cap = 5, Seller = Vendor.Yard },
        };
        public const int DrillStep = 30;

        /// <summary>Deep in the dungeon, the peddler charges for the trip.</summary>
        public const float PeddlerMarkup = 1.35f;


        public static readonly Item[] Items =
        {
            // Silas: provisions.
            new Item { Name = "Health Potion", Description = "For the pouch (1): mends 45% of your health over 3 s. Carry up to 5", Price = 18, Kind = ItemKind.Potion, Amount = 0f },
            new Item { Name = "Mana Potion", Description = "For the pouch (2): refills 70% of your mana, stamina, focus or energy. Up to 5", Price = 15, Kind = ItemKind.Potion, Amount = 1f },
            new Item { Name = "Frost Flask", Description = "For the pouch (3): thrown, chills everything it splashes. Up to 5", Price = 25, Kind = ItemKind.Potion, Amount = 2f },
            new Item { Name = "Hearty Stew", Description = "+30 max HP for the run (max +90)", Price = 55, Kind = ItemKind.Bonus, Stat = StatType.HP, Amount = 30f, Cap = 90f },
            new Item { Name = "Swift Boots", Description = "+5 SPD for the run (max +10)", Price = 50, Kind = ItemKind.Bonus, Stat = StatType.SPD, Amount = 5f, Cap = 10f },
            // Hobb: what the anvil eats, and plate.
            new Item { Name = "Ember Pouch", Description = "10 embers for the anvil", Price = 45, Kind = ItemKind.Embers, Amount = 10f, Seller = Vendor.Smith },
            new Item { Name = "Ember Sack", Description = "25 embers for the anvil", Price = 100, Kind = ItemKind.Embers, Amount = 25f, Seller = Vendor.Smith },
            new Item { Name = "Riveted Plate", Description = "+2 DEF for the run (max +6)", Price = 60, Kind = ItemKind.Bonus, Stat = StatType.DEF, Amount = 2f, Cap = 6f, Seller = Vendor.Smith },
            // The shrine: holy water from the font.
            new Item { Name = "Holy Water", Description = "For the pouch (4): thrown, stuns the dead, cleanses and mends allies. Up to 5", Price = 30, Kind = ItemKind.Potion, Amount = 3f, Seller = Vendor.Shrine },
        };
    }
}
