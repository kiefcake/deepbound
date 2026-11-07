using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>The three crafting parts (2026-11-07). Append only: their counts are packed in <see cref="PlayerProgress.Parts"/>.</summary>
    public enum Part : byte { Bone, Cloth, Iron }

    /// <summary>What a recipe makes. Append only (sent by index over the network).</summary>
    public enum CraftKind : byte { Potion, Lining, Edge, Boots, Caltrops, Rattle }

    /// <summary>
    /// Crafting from dungeon parts (2026-11-07, the user: parts reset each run; recipes for potions and flasks, run upgrades,
    /// and traps and tools; crafted ANYWHERE from the inventory). Bone, cloth and iron drop from the dead, urns and chests
    /// (<see cref="Loot.LootTable"/>) and scrapping gear gives iron. Recipes are fixed; <see cref="PlayerProgress.CraftRpc"/>
    /// spends the parts on the server.
    /// </summary>
    public static class Crafting
    {
        public struct Recipe
        {
            public string Name, Description;
            public int Bone, Cloth, Iron;
            public CraftKind Kind;
            public int Arg;   // Potion: the Flasks.Kind
        }

        public const int MaxUpgrade = 3, MaxTools = 5, PartCap = 999;
        public const float LiningDefense = 2f, EdgeDamage = 0.03f, BootsSpeed = 0.03f;

        public static readonly Recipe[] Recipes =
        {
            new Recipe { Name = "Health Potion", Description = "Bone ash and boiled linen: the old field remedy.", Bone = 4, Cloth = 2, Kind = CraftKind.Potion, Arg = 0 },
            new Recipe { Name = "Class Draught", Description = "Your class's draught (slot 2).", Bone = 3, Cloth = 3, Kind = CraftKind.Potion, Arg = 1 },
            new Recipe { Name = "Frost Flask", Description = "Iron flask packed with grave-cold marrow.", Bone = 2, Iron = 3, Kind = CraftKind.Potion, Arg = 2 },
            new Recipe { Name = "Holy Water", Description = "A bone-stoppered vial, blessed by habit.", Bone = 2, Cloth = 2, Iron = 2, Kind = CraftKind.Potion, Arg = 3 },
            new Recipe { Name = "Padded Lining", Description = "+2 DEF for the run (up to 3).", Cloth = 6, Iron = 2, Kind = CraftKind.Lining },
            new Recipe { Name = "Whetted Edge", Description = "+3% damage for the run (up to 3).", Bone = 2, Iron = 6, Kind = CraftKind.Edge },
            new Recipe { Name = "Hobnailed Boots", Description = "+3% speed for the run (up to 3).", Cloth = 4, Iron = 4, Kind = CraftKind.Boots },
            new Recipe { Name = "Caltrops", Description = "Throw (Z): a patch of spikes for 8 s that chills and cuts what walks it.", Iron = 3, Kind = CraftKind.Caltrops },
            new Recipe { Name = "Bone Rattle", Description = "Throw (Z): the dead nearby turn to the noise for 4 s and forget you.", Bone = 3, Cloth = 1, Kind = CraftKind.Rattle },
        };

        public static string PartName(Part p) => p == Part.Bone ? "bone" : p == Part.Cloth ? "cloth" : "iron";
        public static Color PartColor(Part p) => p == Part.Bone ? new Color(0.92f, 0.88f, 0.76f) : p == Part.Cloth ? new Color(0.75f, 0.55f, 0.9f) : new Color(0.7f, 0.76f, 0.82f);

        public static string Cost(Recipe r)
        {
            var sb = new System.Text.StringBuilder();
            void Add(int n, string what) { if (n <= 0) return; if (sb.Length > 0) sb.Append(" + "); sb.Append(n).Append(' ').Append(what); }
            Add(r.Bone, "bone"); Add(r.Cloth, "cloth"); Add(r.Iron, "iron");
            return sb.ToString();
        }

        public static string ToolName(CraftKind k) => k == CraftKind.Caltrops ? "Caltrops" : k == CraftKind.Rattle ? "Bone Rattle" : "";

        // ---- Tools on the field: caltrops (server ticked by the thrower's ToolBelt) and the rattle's lure.
        public const float CaltropRadius = 2f, CaltropSeconds = 8f, CaltropTick = 0.5f, CaltropDamage = 4f, ToolRange = 12f;
        public const float RattleRadius = 10f, RattleSeconds = 4f;
    }
}
