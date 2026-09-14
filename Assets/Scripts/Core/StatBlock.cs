using System;

namespace Deepbound.Core
{
    /// <summary>
    /// Per-character stat container: base values from the class, plus the three things
    /// that modify them during a run.
    ///
    /// <para>
    /// The four contributions are kept in separate arrays rather than folded into one
    /// running total, because each is cleared and recomputed on a different schedule:
    /// equipment changes whenever gear is swapped, temporary modifiers expire on their own
    /// timers, and potion investment only ever goes up until the run ends. Folding them
    /// together would make "unequip this sword" impossible to do correctly without
    /// remembering what the value was before it was equipped — a class of bug worth
    /// designing out rather than fixing later.
    /// </para>
    ///
    /// <para>
    /// Indexed by <c>(int)StatType</c> into fixed-size arrays rather than by dictionary.
    /// Stats are read in per-frame code — movement reads SPD, attacks read ATT and DEX,
    /// regeneration reads VIT and WIS — and an array index is a bounds check where a
    /// dictionary lookup is a hash, a bucket probe and an equality comparison.
    /// </para>
    ///
    /// <para>
    /// Plain C# class, deliberately not a MonoBehaviour or a serialised field. Unity's
    /// built-in serialiser cannot round-trip this, and it should not try: stats are
    /// run-scoped state owned by the server, built at runtime from a
    /// <see cref="Deepbound.Classes.ClassDefinition"/>. Persisting anything here across
    /// runs would contradict the game's cosmetics-only progression rule.
    /// </para>
    /// </summary>
    public class StatBlock
    {
        /// <summary>
        /// Potions needed to fully max one stat. One potion grants 1/5 of that stat's
        /// potion pool, so five max it.
        ///
        /// This is the single knob for the whole potion economy — retuning it retunes
        /// every stat uniformly, which is the point. Do not special-case per stat.
        /// </summary>
        public const int PotionsToMaxStat = 5;

        private readonly float[] baseValues = new float[Stats.Count];
        private readonly float[] potionPool = new float[Stats.Count];
        private readonly int[] potionsApplied = new int[Stats.Count];
        private readonly float[] equipmentBonus = new float[Stats.Count];
        private readonly float[] temporaryBonus = new float[Stats.Count];

        /// <summary>
        /// Raised whenever any contribution changes. The HUD and stat screen subscribe to
        /// this rather than polling every stat every frame.
        /// </summary>
        public event Action Changed;

        // ------------------------------------------------------------------
        // Base values — set once from the class definition
        // ------------------------------------------------------------------

        public void SetBase(StatType stat, float value)
        {
            baseValues[(int)stat] = value;
            Changed?.Invoke();
        }

        public float GetBase(StatType stat) => baseValues[(int)stat];

        /// <summary>
        /// The total this stat gains from being fully potioned. Set per class, so a class
        /// can have a deeper pool in the stat that defines it.
        /// </summary>
        public void SetPotionPool(StatType stat, float pool)
        {
            potionPool[(int)stat] = pool;
            Changed?.Invoke();
        }

        public float GetPotionPool(StatType stat) => potionPool[(int)stat];

        // ------------------------------------------------------------------
        // Potion investment — run-scoped progression
        // ------------------------------------------------------------------

        public int PotionsApplied(StatType stat) => potionsApplied[(int)stat];

        public bool IsMaxed(StatType stat) => potionsApplied[(int)stat] >= PotionsToMaxStat;

        /// <summary>
        /// Apply one potion of a stat. Returns false if that stat is already maxed, so the
        /// caller can refuse to consume the item rather than silently wasting it.
        /// </summary>
        public bool ApplyPotion(StatType stat)
        {
            if (IsMaxed(stat)) return false;

            potionsApplied[(int)stat]++;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// All-stat potion: one potion's worth of every stat at once, skipping any already
        /// maxed. Returns how many stats it actually advanced, so a fully maxed character
        /// does not consume the item for nothing.
        /// </summary>
        public int ApplyAllStatPotion()
        {
            int advanced = 0;

            for (int i = 0; i < Stats.Count; i++)
            {
                if (potionsApplied[i] >= PotionsToMaxStat) continue;
                potionsApplied[i]++;
                advanced++;
            }

            if (advanced > 0) Changed?.Invoke();
            return advanced;
        }

        // ------------------------------------------------------------------
        // Equipment — recomputed wholesale whenever gear changes
        // ------------------------------------------------------------------

        /// <summary>
        /// Additive rather than assigning, so a weapon and a piece of armour can both
        /// contribute to the same stat without one clobbering the other. Call
        /// <see cref="ClearEquipmentBonuses"/> first when recomputing from scratch.
        /// </summary>
        public void AddEquipmentBonus(StatType stat, float amount)
        {
            equipmentBonus[(int)stat] += amount;
            Changed?.Invoke();
        }

        public void ClearEquipmentBonuses()
        {
            Array.Clear(equipmentBonus, 0, equipmentBonus.Length);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------
        // Temporary modifiers — buffs and debuffs
        // ------------------------------------------------------------------

        /// <summary>
        /// Applied by status effects. The caller owns the lifetime: whatever adds a
        /// temporary bonus is responsible for subtracting the same amount when it expires.
        /// </summary>
        public void AddTemporaryBonus(StatType stat, float amount)
        {
            temporaryBonus[(int)stat] += amount;
            Changed?.Invoke();
        }

        public void ClearTemporaryBonuses()
        {
            Array.Clear(temporaryBonus, 0, temporaryBonus.Length);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------
        // Reads
        // ------------------------------------------------------------------

        /// <summary>
        /// The effective value of a stat: base, plus the fraction of the potion pool
        /// earned, plus equipment, plus temporary modifiers.
        ///
        /// Clamped at zero — a strong enough debuff stack could otherwise drive SPD or DEX
        /// negative, which reads downstream as walking backwards or a negative attack
        /// interval.
        /// </summary>
        public float GetValue(StatType stat)
        {
            int i = (int)stat;
            float potionFraction = (float)potionsApplied[i] / PotionsToMaxStat;
            float total = baseValues[i]
                          + potionPool[i] * potionFraction
                          + equipmentBonus[i]
                          + temporaryBonus[i];

            return total < 0f ? 0f : total;
        }

        /// <summary>
        /// The value this stat would have with every potion applied. Used by the stat
        /// screen to show progress toward the cap, and by vendors deciding whether a potion
        /// is worth offering.
        /// </summary>
        public float GetMaxedValue(StatType stat)
        {
            int i = (int)stat;
            return baseValues[i] + potionPool[i] + equipmentBonus[i] + temporaryBonus[i];
        }

        // ------------------------------------------------------------------
        // Run lifecycle
        // ------------------------------------------------------------------

        /// <summary>
        /// Reset everything a run grants, leaving the class's base values and potion pools
        /// intact.
        ///
        /// This is what enforces the game's central progression rule: nothing earned inside
        /// a run survives it. Called when a run ends, whether by completion or by a wipe.
        /// </summary>
        public void ResetRunProgress()
        {
            Array.Clear(potionsApplied, 0, potionsApplied.Length);
            Array.Clear(equipmentBonus, 0, equipmentBonus.Length);
            Array.Clear(temporaryBonus, 0, temporaryBonus.Length);
            Changed?.Invoke();
        }
    }
}
