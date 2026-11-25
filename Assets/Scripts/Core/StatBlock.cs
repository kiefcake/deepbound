using System;

namespace Deepbound.Core
{
    public class StatBlock
    {
        public const int PotionsToMaxStat = 5;

        private readonly float[] baseValues = new float[Stats.Count];
        private readonly float[] potionPool = new float[Stats.Count];
        private readonly int[] potionsApplied = new int[Stats.Count];
        private readonly float[] equipmentBonus = new float[Stats.Count];
        private readonly float[] temporaryBonus = new float[Stats.Count];

        public event Action Changed;

        public void SetBase(StatType stat, float value)
        {
            baseValues[(int)stat] = value;
            Changed?.Invoke();
        }

        public float GetBase(StatType stat) => baseValues[(int)stat];

        public void SetPotionPool(StatType stat, float pool)
        {
            potionPool[(int)stat] = pool;
            Changed?.Invoke();
        }

        public float GetPotionPool(StatType stat) => potionPool[(int)stat];

        public int PotionsApplied(StatType stat) => potionsApplied[(int)stat];

        public bool IsMaxed(StatType stat) => potionsApplied[(int)stat] >= PotionsToMaxStat;

        public bool ApplyPotion(StatType stat)
        {
            if (IsMaxed(stat)) return false;

            potionsApplied[(int)stat]++;
            Changed?.Invoke();
            return true;
        }

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

        public float GetMaxedValue(StatType stat)
        {
            int i = (int)stat;
            return baseValues[i] + potionPool[i] + equipmentBonus[i] + temporaryBonus[i];
        }

        public void ResetRunProgress()
        {
            Array.Clear(potionsApplied, 0, potionsApplied.Length);
            Array.Clear(equipmentBonus, 0, equipmentBonus.Length);
            Array.Clear(temporaryBonus, 0, temporaryBonus.Length);
            Changed?.Invoke();
        }
    }
}
