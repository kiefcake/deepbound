using Deepbound.Loot;
using Deepbound.Player;
using UnityEngine;
using Deepbound.Core;

namespace Deepbound.Progression
{
    public enum Boon : byte { KeenCompanion, Quartermaster, LightSleeper, Packrat, Tinker, TreasureSense, Cartographer, Pathfinder }

    public static class Legacy
    {
        public struct Info { public string Name, Effect; public int Cost; }

        public static readonly Info[] All =
        {
            new Info { Name = "Keen Companion", Effect = "Your pet fetches gold and embers from 12 m away instead of 8.", Cost = 140 },
            new Info { Name = "Quartermaster", Effect = "Every run starts with 3 bone, 3 cloth and 3 iron.", Cost = 200 },
            new Info { Name = "Light Sleeper", Effect = "You revive fallen allies 15% faster.", Cost = 160 },
            new Info { Name = "Packrat", Effect = "Gold and embers drift to you from 1.5x as far.", Cost = 120 },
            new Info { Name = "Tinker", Effect = "Carry up to 8 of each crafted tool instead of 5.", Cost = 180 },
            new Info { Name = "Treasure Sense", Effect = "Unopened chests within 25 m show on your minimap.", Cost = 260 },
            new Info { Name = "Cartographer", Effect = "Cracked walls within 18 m show on your minimap.", Cost = 220 },
            new Info { Name = "Pathfinder", Effect = "The way to the guardian's arena is marked on your minimap from the start.", Cost = 320 },
        };

        public const float KeenFetchRadius = 12f, PackratMagnet = 1.5f, LightSleeperRate = 1.15f;
        public const int TinkerTools = 8, QuartermasterParts = 3;
        public const float ChestSenseRange = 25f, WallSenseRange = 18f;

        private const string KeyBits = "db.legacy", KeyLast = "db.lastRelics", KeyHeir = "db.heirloom";

        public static ushort Bits => Cosmetics.TestMode ? (ushort)0 : (ushort)Prefs.GetInt(KeyBits, 0);
        public static bool Owned(Boon b) => (Bits & (1 << (int)b)) != 0;

        public static bool Has(NetworkPlayer who, Boon b) => who != null && (who.LegacyBits.Value & (1 << (int)b)) != 0;
        public static bool Has(Component who, Boon b) => who != null && who.TryGetComponent<NetworkPlayer>(out var np) && Has(np, b);

        public static string CannotBuy(Boon b)
        {
            if (Cosmetics.TestMode) return "Not in tests";
            if (Owned(b)) return "Owned";
            return Cosmetics.Renown < All[(int)b].Cost ? "Need " + All[(int)b].Cost + " Renown" : null;
        }

        public static bool Buy(Boon b)
        {
            if (CannotBuy(b) != null || !Cosmetics.SpendRenown(All[(int)b].Cost)) return false;
            Prefs.SetInt(KeyBits, Bits | (1 << (int)b));
            Prefs.Save();
            if (NetworkPlayer.Local != null) NetworkPlayer.Local.RefreshLegacy();
            return true;
        }

        public static void SaveRelics(PlayerProgress me)
        {
            if (Cosmetics.TestMode || me == null) return;
            var ids = new System.Collections.Generic.List<string>();
            for (int i = 0; i < me.RelicCount; i++) ids.Add(((int)me.RelicAt(i)).ToString());
            Prefs.SetString(KeyLast, string.Join(",", ids));
            Prefs.Save();
        }

        public static Relic[] LastRelics()
        {
            var list = new System.Collections.Generic.List<Relic>();
            if (Cosmetics.TestMode) return list.ToArray();
            foreach (var part in Prefs.GetString(KeyLast, "").Split(','))
                if (int.TryParse(part, out var id) && id >= 0 && id < Relics.Count && !list.Contains((Relic)id)) list.Add((Relic)id);
            return list.ToArray();
        }

        public static Relic? Heirloom
        {
            get
            {
                if (Cosmetics.TestMode) return null;
                int v = Prefs.GetInt(KeyHeir, 0) - 1;
                return v >= 0 && v < Relics.Count ? (Relic)v : (Relic?)null;
            }
        }

        public static void ChooseHeirloom(Relic r)
        {
            if (Cosmetics.TestMode) return;
            Prefs.SetInt(KeyHeir, (int)r + 1);
            Prefs.Save();
        }

        public static void ConsumeHeirloom()
        {
            if (Cosmetics.TestMode) return;
            Prefs.DeleteKey(KeyHeir);
            Prefs.Save();
        }

        public static string Note = "";
    }
}
