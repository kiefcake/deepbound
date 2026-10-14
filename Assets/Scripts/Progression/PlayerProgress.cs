using System;
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
    public class PlayerProgress : NetworkBehaviour
    {
        public readonly NetworkVariable<int> Gold = new NetworkVariable<int>();

        /// <summary>Weapon upgrade material, spent at the anvil.</summary>
        public readonly NetworkVariable<int> Embers = new NetworkVariable<int>();

        /// <summary>The class weapon's anvil level, 0 to <see cref="World.Anvil.MaxLevel"/>.</summary>
        public readonly NetworkVariable<int> WeaponLevel = new NetworkVariable<int>();

        /// <summary>The weapon's chosen path: 0 = not yet, 1 or 2. See <see cref="WeaponPaths"/>.</summary>
        public readonly NetworkVariable<byte> WeaponPath = new NetworkVariable<byte>();

        public bool OnPath(int path) => WeaponPath.Value == path;
        public bool Mastered(int path) => WeaponPath.Value == path && WeaponLevel.Value >= WeaponPaths.MasteryLevel;

        // ---- Run statistics, for the end-of-run summary. Server-written, reset with the run. ----
        public readonly NetworkVariable<int> Kills = new NetworkVariable<int>();
        public readonly NetworkVariable<int> DamageDealt = new NetworkVariable<int>();
        public readonly NetworkVariable<int> GoldEarned = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Downs = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Revives = new NetworkVariable<int>();
        private float damageCarry;

        /// <summary>Server: credit damage this player dealt, and a kill if it was the final blow.</summary>
        public void ServerCreditDamage(float dealt, bool killed)
        {
            damageCarry += dealt;
            int whole = Mathf.FloorToInt(damageCarry);
            if (whole > 0)
            {
                damageCarry -= whole;
                DamageDealt.Value += whole;
            }
            if (killed) Kills.Value++;
        }

        /// <summary>
        /// The player's class. Owner-written, but only honoured in the lobby: the HUD only
        /// offers the choice there, and the server re-checks.
        /// </summary>
        public readonly NetworkVariable<ClassId> ClassId = new NetworkVariable<ClassId>(Classes.ClassId.Knight,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private NetworkList<float> bonuses;
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
        }

        public override void OnNetworkSpawn()
        {
            WeaponLevel.OnValueChanged += (_, __) => StatsChanged?.Invoke();
            bonuses.OnListChanged += _ => StatsChanged?.Invoke();
            relics.OnListChanged += _ => StatsChanged?.Invoke();
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
            health.IncomingModifiers.Add(RelicDefense);

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
            return Mathf.Max(0f, Class.BaseStats[i] + bonus);
        }

        /// <summary>
        /// RotMG ATT scaling (0 ATT is half damage, 50 ATT is double base), then damage
        /// relics. Every kit multiplies by this, so relics apply to every class for free.
        /// </summary>
        public float AttackMultiplier
        {
            get
            {
                float m = 0.5f + Value(StatType.ATT) / 50f;
                if (HasRelic(Relic.BerserkersBrand) && health.Fraction < 0.5f) m *= 1.4f;
                if (HasRelic(Relic.BloodPact)) m *= 1.3f;
                return m;
            }
        }

        /// <summary>SPD 50 is the reference speed; each point either side is ±1%.</summary>
        public float SpeedMultiplier => (1f + (Value(StatType.SPD) - 50f) / 100f) * (HasRelic(Relic.GhostLantern) ? 1.15f : 1f);

        /// <summary>Q and E cooldowns scale by this (Echo Stone).</summary>
        public float CooldownMultiplier => HasRelic(Relic.EchoStone) ? 0.7f : 1f;

        public float GoldMultiplier => HasRelic(Relic.GildedSkull) ? 1.6f : 1f;

        // ---- Relics ----

        public bool HasRelic(Relic r)
        {
            foreach (var b in relics) if (b == (byte)r) return true;
            return false;
        }

        public int RelicCount => relics.Count;
        public Relic RelicAt(int i) => (Relic)relics[i];

        /// <returns>False if already owned (the drop stays for a teammate).</returns>
        public bool ServerAddRelic(Relic r)
        {
            if (!IsServer || HasRelic(r)) return false;
            relics.Add((byte)r);
            if (r == Relic.HeartOfTheDeep || r == Relic.BloodPact) OnStatsChangedServer(StatType.HP);
            RelicGainedRpc((byte)r);
            return true;
        }

        /// <summary>Server, each floor: once-per-floor relics recharge.</summary>
        public void ServerNewFloor()
        {
            phoenixSpent = false;
            boneWardReadyAt = 0f;
        }

        /// <summary>Server: Phoenix Feather. True if it fires (and is used up for this floor).</summary>
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
                boneWardReadyAt = Time.time + 12f;
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
                if (nearest != null) nearest.ServerDamage(damage * 0.4f, transform.position, NetworkObjectId);
            }
            return damage;
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

        /// <summary>Thunder Sigil: draws a jagged bolt through each struck enemy.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void LightningFxRpc(Vector3[] points)
        {
            Art.LightningBolt.Spawn(transform.position + Vector3.up * 6f, points);
            Art.Sfx.Play(Art.Sound.Shatter, points[0], 1f, 0.6f);
        }

        /// <summary>DEX 50 is the reference rate; higher DEX shortens swing recovery, floored at 50%.</summary>
        public float AttackTimeMultiplier => Mathf.Max(0.5f, 1.5f - Value(StatType.DEX) / 50f * 0.5f);

        // ------------------------------------------------------------------
        // Server API
        // ------------------------------------------------------------------

        public void ServerAddEmbers(int amount)
        {
            if (!IsServer) return;
            Embers.Value = Mathf.Max(0, Embers.Value + amount);
        }

        /// <summary>Why the weapon cannot be tempered right now, or null. Any peer; the server re-checks.</summary>
        public string CannotTemperReason()
        {
            if (WeaponLevel.Value >= World.Anvil.MaxLevel) return "Fully tempered";
            if (WeaponLevel.Value >= WeaponPaths.PathLevel && WeaponPath.Value == 0) return "Choose a path first";
            if (Embers.Value < World.Anvil.Cost(WeaponLevel.Value)) return "Not enough embers";
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
            if (WeaponPath.Value != 0 || WeaponLevel.Value < WeaponPaths.PathLevel || path < 1 || path > 2) return;
            WeaponPath.Value = path;
            PathChosenRpc(path);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PathChosenRpc(byte path)
        {
            Art.Sfx.Play(Art.Sound.Relic, transform.position + Vector3.up, 0.7f, 1.2f);
            UI.GameHud.Feed(name + "'s " + ClassDefinition.WeaponName(ClassId.Value) + " takes the path of the " + WeaponPaths.Name(ClassId.Value, path), LootInfo.EmberColor);
        }

        /// <summary>Server: spend embers on the next anvil level. False if it cannot.</summary>
        public bool ServerUpgradeWeapon()
        {
            if (!IsServer || CannotTemperReason() != null) return false;
            int cost = World.Anvil.Cost(WeaponLevel.Value);
            if (Embers.Value < cost) return false;
            Embers.Value -= cost;
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
        public string CannotBuyReason(int index)
        {
            var items = Shop.Current;
            if (index < 0 || index >= items.Length) return "Unknown item";
            var item = items[index];
            if (Gold.Value < item.Price) return "Not enough gold";
            switch (item.Kind)
            {
                case Shop.ItemKind.Heal: return health.Current.Value >= health.Max.Value ? "Already at full health" : null;
                case Shop.ItemKind.Embers: return null;
                case Shop.ItemKind.Relic: return HasRelic((Relic)(int)item.Amount) ? "Owned" : null;
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

            var item = Shop.Current[index];
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
                case Shop.ItemKind.Relic:
                    bought = ServerAddRelic((Relic)(int)item.Amount);
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
            for (int i = 0; i < Core.Stats.Count; i++) bonuses[i] = 0f;
            Gold.Value = 0;
            Embers.Value = 0;
            WeaponLevel.Value = 0;
            WeaponPath.Value = 0;
            Kills.Value = DamageDealt.Value = GoldEarned.Value = Downs.Value = Revives.Value = 0;
            damageCarry = 0f;
            relics.Clear();
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
            if (HasRelic(Relic.HeartOfTheDeep)) hp += 70f;
            if (HasRelic(Relic.BloodPact)) hp *= 0.75f;
            health.ServerSetMax(hp, refill);
            health.Defense = Stats.GetValue(StatType.DEF);
        }
    }
}
