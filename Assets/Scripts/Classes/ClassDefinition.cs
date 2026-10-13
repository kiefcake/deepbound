using Deepbound.Core;

namespace Deepbound.Classes
{
    /// <summary>
    /// A class's base stats and potion pools. Plain data; <see cref="Build"/> turns it into
    /// a run-scoped <see cref="StatBlock"/>.
    ///
    /// Defined in code for M1 because there is exactly one class. When the other seven
    /// arrive (M3) this becomes a ScriptableObject so balance can be tuned without a
    /// recompile.
    /// </summary>
    public class ClassDefinition
    {
        public string Name;
        public string Role;
        public float[] BaseStats = new float[Stats.Count];
        public float[] PotionPools = new float[Stats.Count];

        public StatBlock Build()
        {
            var block = new StatBlock();
            for (int i = 0; i < Stats.Count; i++)
            {
                block.SetBase((StatType)i, BaseStats[i]);
                block.SetPotionPool((StatType)i, PotionPools[i]);
            }
            return block;
        }

        private static ClassDefinition Make(string name, string role,
            float hp, float mp, float att, float def, float spd, float dex, float vit, float wis,
            float hpPool, float mpPool, float attPool, float defPool, float spdPool, float dexPool, float vitPool, float wisPool)
        {
            var c = new ClassDefinition { Name = name, Role = role };
            c.BaseStats = new[] { hp, mp, att, def, spd, dex, vit, wis };
            c.PotionPools = new[] { hpPool, mpPool, attPool, defPool, spdPool, dexPool, vitPool, wisPool };
            return c;
        }

        /// <summary>
        /// The Knight: tank. High HP and DEF, deep DEF pool, slow-ish. Damage comes from
        /// steady swings rather than burst; its value is holding a doorway while the
        /// party does the killing.
        /// </summary>
        public static readonly ClassDefinition Knight = Make("Knight", "Tank",
            hp: 240, mp: 100, att: 20, def: 6, spd: 45, dex: 45, vit: 40, wis: 20,
            hpPool: 160, mpPool: 60, attPool: 25, defPool: 14, spdPool: 20, dexPool: 25, vitPool: 30, wisPool: 25);
    }
}
