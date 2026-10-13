using System;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Core;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>
    /// A player's run-scoped power: the stat block, potions drunk, bonuses bought, and
    /// gold. Everything here is wiped when the run ends — the game's central rule.
    ///
    /// <para>
    /// The authoritative <see cref="StatBlock"/> lives on the server. Potion counts and
    /// bonuses are mirrored into network lists so every peer can compute the same stat
    /// values: the owner needs SPD for movement and DEX for swing speed without asking the
    /// server, and the HUD's stat panel needs all eight.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerProgress : NetworkBehaviour
    {
        public readonly NetworkVariable<int> Gold = new NetworkVariable<int>();

        /// <summary>
        /// The player's class. Owner-written, but only honoured in the lobby: the HUD only
        /// offers the choice there, and the server re-checks.
        /// </summary>
        public readonly NetworkVariable<ClassId> ClassId = new NetworkVariable<ClassId>(Classes.ClassId.Knight,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private NetworkList<byte> potions;
        private NetworkList<float> bonuses;

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
            potions = new NetworkList<byte>();
            bonuses = new NetworkList<float>();
        }

        public override void OnNetworkSpawn()
        {
            potions.OnListChanged += _ => StatsChanged?.Invoke();
            bonuses.OnListChanged += _ => StatsChanged?.Invoke();
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

            Stats = Class.Build();
            for (int i = 0; i < Core.Stats.Count; i++)
            {
                potions.Add(0);
                bonuses.Add(0f);
            }
            ApplyToHealth(refill: true);
        }

        // ------------------------------------------------------------------
        // Reads (any peer)
        // ------------------------------------------------------------------

        public int PotionsApplied(StatType stat) => (int)stat < potions.Count ? potions[(int)stat] : 0;

        /// <summary>
        /// The effective stat value, computed from synced inputs with the same formula as
        /// <see cref="StatBlock.GetValue"/>. On the server the two always agree.
        /// </summary>
        public float Value(StatType stat)
        {
            int i = (int)stat;
            float potionFraction = (float)PotionsApplied(stat) / StatBlock.PotionsToMaxStat;
            float bonus = i < bonuses.Count ? bonuses[i] : 0f;
            return Mathf.Max(0f, Class.BaseStats[i] + Class.PotionPools[i] * potionFraction + bonus);
        }

        public bool IsMaxed(StatType stat) => PotionsApplied(stat) >= StatBlock.PotionsToMaxStat;

        /// <summary>RotMG ATT scaling: 0 ATT is half damage, 50 ATT is double base.</summary>
        public float AttackMultiplier => 0.5f + Value(StatType.ATT) / 50f;

        /// <summary>SPD 50 is the reference speed; each point either side is ±1%.</summary>
        public float SpeedMultiplier => 1f + (Value(StatType.SPD) - 50f) / 100f;

        /// <summary>DEX 50 is the reference rate; higher DEX shortens swing recovery, floored at 50%.</summary>
        public float AttackTimeMultiplier => Mathf.Max(0.5f, 1.5f - Value(StatType.DEX) / 50f * 0.5f);

        // ------------------------------------------------------------------
        // Server API
        // ------------------------------------------------------------------

        /// <returns>False if the stat is already maxed, so the potion is not consumed.</returns>
        public bool ServerDrinkPotion(StatType stat)
        {
            if (!IsServer || !Stats.ApplyPotion(stat)) return false;
            potions[(int)stat] = (byte)Stats.PotionsApplied(stat);
            OnStatsChangedServer(stat);
            return true;
        }

        /// <returns>How many stats advanced. Zero means everything is maxed.</returns>
        public int ServerDrinkAllPotion()
        {
            if (!IsServer) return 0;
            int advanced = Stats.ApplyAllStatPotion();
            for (int i = 0; i < Core.Stats.Count; i++) potions[i] = (byte)Stats.PotionsApplied((StatType)i);
            if (advanced > 0) OnStatsChangedServer(null);
            return advanced;
        }

        /// <summary>A run-scoped flat bonus, e.g. a whetstone bought from the merchant.</summary>
        public void ServerAddBonus(StatType stat, float amount)
        {
            if (!IsServer) return;
            Stats.AddEquipmentBonus(stat, amount);
            bonuses[(int)stat] += amount;
            OnStatsChangedServer(stat);
        }

        public void ServerAddGold(int amount)
        {
            if (IsServer) Gold.Value = Mathf.Max(0, Gold.Value + amount);
        }

        public bool ServerSpendGold(int amount)
        {
            if (!IsServer || Gold.Value < amount) return false;
            Gold.Value -= amount;
            return true;
        }

        public float Bonus(StatType stat) => (int)stat < bonuses.Count ? bonuses[(int)stat] : 0f;

        /// <summary>Why an item cannot be bought right now, or null if it can. Any peer; the server re-checks.</summary>
        public string CannotBuyReason(int index)
        {
            if (index < 0 || index >= Shop.Items.Length) return "Unknown item";
            var item = Shop.Items[index];
            if (Gold.Value < item.Price) return "Not enough gold";
            switch (item.Kind)
            {
                case Shop.ItemKind.Heal: return health.Current.Value >= health.Max.Value ? "Already at full health" : null;
                case Shop.ItemKind.Potion: return IsMaxed(item.Stat) ? "Stat maxed" : null;
                default: return Bonus(item.Stat) + item.Amount > item.Cap + 0.01f ? "Sold out" : null;
            }
        }

        /// <summary>Owner → server: buy catalogue item <paramref name="index"/> from the merchant.</summary>
        [Rpc(SendTo.Server)]
        public void BuyRpc(int index)
        {
            var layout = World.DungeonDirector.Layout;
            if (layout == null || Vector3.Distance(transform.position, layout.MerchantPosition) > Shop.MerchantRange + 1f) return;
            if (CannotBuyReason(index) != null) return;

            var item = Shop.Items[index];
            bool bought;
            switch (item.Kind)
            {
                case Shop.ItemKind.Heal:
                    bought = true;
                    health.ServerHeal(health.Max.Value * item.Amount);
                    break;
                case Shop.ItemKind.Potion:
                    bought = ServerDrinkPotion(item.Stat);
                    break;
                default:
                    ServerAddBonus(item.Stat, item.Amount);
                    bought = true;
                    break;
            }

            if (bought)
            {
                Gold.Value -= item.Price;
                PurchasedRpc();
            }
        }

        [Rpc(SendTo.Owner)]
        private void PurchasedRpc() => Art.Sfx.Play2D(Art.Sound.Purchase, 0.8f);

        /// <summary>Wipes everything the run granted. Called when a run ends or restarts.</summary>
        public void ServerResetRun()
        {
            if (!IsServer) return;
            Stats.ResetRunProgress();
            for (int i = 0; i < Core.Stats.Count; i++)
            {
                potions[i] = 0;
                bonuses[i] = 0f;
            }
            Gold.Value = 0;
            ApplyToHealth(refill: true);
        }

        private void OnStatsChangedServer(StatType? stat)
        {
            // An HP potion also heals by the amount it added — drinking one mid-fight should
            // feel like it did something right now, not just raise a ceiling.
            float before = health.Max.Value;
            ApplyToHealth(refill: false);
            if (stat == null || stat == StatType.HP) health.ServerHeal(health.Max.Value - before);
        }

        private void ApplyToHealth(bool refill)
        {
            health.ServerSetMax(Stats.GetValue(StatType.HP), refill);
            health.Defense = Stats.GetValue(StatType.DEF);
        }
    }
}
