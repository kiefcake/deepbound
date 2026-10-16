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
    public class PlayerProgress : NetworkBehaviour
    {
        public readonly NetworkVariable<int> Gold = new NetworkVariable<int>();

        /// <summary>Weapon upgrade material, spent at the anvil.</summary>
        public readonly NetworkVariable<int> Embers = new NetworkVariable<int>();

        /// <summary>The class weapon's anvil level, 0 to <see cref="World.Anvil.MaxLevel"/>.</summary>
        public readonly NetworkVariable<int> WeaponLevel = new NetworkVariable<int>();

        /// <summary>Ability rank-ups, spent at the anvil.</summary>
        public readonly NetworkVariable<int> Sigils = new NetworkVariable<int>();
        public readonly NetworkVariable<byte> RankQ = new NetworkVariable<byte>(1);
        public readonly NetworkVariable<byte> RankE = new NetworkVariable<byte>(1);

        /// <summary>Effective rank: bought ranks, plus one for the Hollow Crown (max 3).</summary>
        public int Rank(AbilitySlot slot) => Mathf.Min(AbilityRanks.MaxRank, (slot == AbilitySlot.Q ? RankQ.Value : RankE.Value) + (Wears(GearId.HollowCrown) ? 1 : 0));

        /// <summary>Ranks actually bought with Sigils (what the anvil can still raise).</summary>
        public int BoughtRank(AbilitySlot slot) => slot == AbilitySlot.Q ? RankQ.Value : RankE.Value;

        /// <summary>Rank 2 and up: 30% stronger.</summary>
        public float Potency(AbilitySlot slot) => Rank(slot) >= 2 ? AbilityRanks.Rank2Potency : 1f;

        /// <summary>Rank 3: the twist.</summary>
        public bool Twist(AbilitySlot slot) => Rank(slot) >= 3;

        /// <summary>Q/E cooldown scale: relics and gear, and a quarter off at rank 3.</summary>
        public float CooldownFor(AbilitySlot slot) => CooldownMultiplier * (Twist(slot) ? AbilityRanks.Rank3Cooldown : 1f);

        public void ServerAddSigil()
        {
            if (!IsServer) return;
            Sigils.Value++;
            SigilRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SigilRpc() => UI.GameHud.Feed(name + " found a Sigil  -  empower an ability at the anvil", LootInfo.SigilColor);

        /// <summary>Owner → server: spend a Sigil on Q or E, at the anvil.</summary>
        [Rpc(SendTo.Server)]
        public void EmpowerRpc(byte slot)
        {
            var anvil = World.Anvil.Near(transform.position);
            if (anvil == null || Sigils.Value < 1 || slot > 1) return;
            var rank = slot == 0 ? RankQ : RankE;
            if (rank.Value >= AbilityRanks.MaxRank || Rank((AbilitySlot)slot) >= AbilityRanks.MaxRank) return;
            Sigils.Value--;
            rank.Value++;
            anvil.ForgeFxRpc(rank.Value + 2);
            EmpoweredRpc(slot, rank.Value);
        }

        /// <summary>Owner → server: with both abilities at rank 3, a spare Sigil melts into 30 embers.</summary>
        [Rpc(SendTo.Server)]
        public void MeltSigilRpc()
        {
            var anvil = World.Anvil.Near(transform.position);
            if (anvil == null || Sigils.Value < 1 || Rank(AbilitySlot.Q) < AbilityRanks.MaxRank || Rank(AbilitySlot.E) < AbilityRanks.MaxRank) return;
            Sigils.Value--;
            Embers.Value += 30;
            anvil.ForgeFxRpc(1);
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

        /// <summary>The shrine's blessing for the current floor (see <see cref="Blessings"/>).</summary>
        public readonly NetworkVariable<byte> ActiveBlessing = new NetworkVariable<byte>();
        public bool Blessed(Blessing b) => ActiveBlessing.Value == (byte)b;

        public void ServerBless(Blessing b)
        {
            if (!IsServer) return;
            ActiveBlessing.Value = (byte)b;
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
            if (!CanManagePack(i)) return;
            var item = bag[i];
            bag.RemoveAt(i);
            ServerAddEmbers(Gear.ScrapValue(item));
            PackFxRpc(0);
        }

        /// <summary>Owner → server: set a piece down on the floor (yours alone to see, as ever).</summary>
        [Rpc(SendTo.Server)]
        public void BagDropRpc(int i)
        {
            if (!CanManagePack(i)) return;
            var item = bag[i];
            bag.RemoveAt(i);
            GearDrop.ServerSpawn(item, OwnerClientId, transform.position + transform.forward * 1.2f + Vector3.up * 0.05f);
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

        private void ServerUnequip(GearSlot which)
        {
            var slot = which == GearSlot.Armor ? Armor : Charm;
            var old = slot.Value;
            foreach (var s in Gear.StatsOf(old)) Stats.AddEquipmentBonus(s, -Gear.StatValue(old, s));
            slot.Value = default;
            health.HealMultiplier = Wears(GearId.TidewovenRobe) ? 1.3f : 1f;
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
            health.HealMultiplier = Wears(GearId.TidewovenRobe) ? 1.3f : 1f;
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
            anvil.ForgeFxRpc(3);
        }

        /// <summary>Server: a dodge readies the Gravecloth Mantle's strike.</summary>
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

        /// <summary>Owner → server: pick a class. Refused once the run has begun.</summary>
        [Rpc(SendTo.Server)]
        public void ChooseClassRpc(ClassId id)
        {
            if (!ClassChangeAllowed || (int)id < 0 || (int)id >= System.Enum.GetValues(typeof(ClassId)).Length) return;
            ClassId.Value = id;
        }
        private NetworkList<float> bonuses;
        private NetworkList<byte> runeBag;
        public readonly NetworkVariable<byte> Socket0 = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Socket1 = new NetworkVariable<byte>();

        public Rune Socketed(int socket) => (Rune)(socket == 0 ? Socket0.Value : Socket1.Value);
        public bool HasRune(Rune r) => Socketed(0) == r || Socketed(1) == r;
        public int RuneBagCount => runeBag.Count;
        public Rune RuneInBag(int i) => (Rune)runeBag[i];

        public void ServerAddRune(Rune r)
        {
            if (!IsServer || r == Rune.None) return;
            runeBag.Add((byte)r);
            RuneFoundRpc((byte)r);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RuneFoundRpc(byte r) => UI.GameHud.Feed(name + " found a " + Runes.Name((Rune)r) + "  -  set it at the anvil", Runes.Color((Rune)r));

        /// <summary>
        /// Owner → server: set a rune from the bag into the first open empty socket, or, if
        /// both are full, into the first socket (its old rune goes back into the bag).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void SocketRuneRpc(byte rune)
        {
            var anvil = World.Anvil.Near(transform.position);
            int sockets = Runes.SocketsAt(WeaponLevel.Value);
            int bagIndex = runeBag.IndexOf(rune);
            if (anvil == null || sockets == 0 || bagIndex < 0) return;
            int target = 0;
            for (int s = 0; s < sockets; s++) if (Socketed(s) == Rune.None) { target = s; break; }
            var socket = target == 0 ? Socket0 : Socket1;
            runeBag.RemoveAt(bagIndex);
            if (socket.Value != 0) runeBag.Add(socket.Value);
            socket.Value = rune;
            anvil.ForgeFxRpc(2);
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
        public float SpeedMultiplier => (1f + (Value(StatType.SPD) - 50f) / 100f) * (HasRelic(Relic.GhostLantern) ? 1.15f : 1f) * (Blessed(Blessing.Swiftness) ? 1.12f : 1f)
            * World.DungeonDirector.WaterSlow(transform.position)
            * (TryGetComponent<Player.PlayerDebuffs>(out var deb) ? deb.SpeedMultiplier : 1f)
            * World.Carryable.SpeedFor(NetworkObjectId);

        /// <summary>Q and E cooldowns scale by this (Echo Stone).</summary>
        public float CooldownMultiplier => (HasRelic(Relic.EchoStone) ? 0.7f : 1f) * (Wears(GearId.CrackedHourglass) ? 0.82f : 1f) * (Blessed(Blessing.Swiftness) ? 0.85f : 1f);

        public float GoldMultiplier => (HasRelic(Relic.GildedSkull) ? 1.6f : 1f) * (Blessed(Blessing.Fortune) ? 1.5f : 1f);

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
            // Completing a pair awakens its synergy.
            for (int i = 0; i < Relics.Synergies.Length; i++)
            {
                var syn = Relics.Synergies[i];
                if ((syn.a == r || syn.b == r) && HasRelic(syn.a) && HasRelic(syn.b)) SynergyRpc(i);
            }
            return true;
        }

        /// <summary>Server, each floor: once-per-floor relics recharge.</summary>
        public void ServerNewFloor()
        {
            phoenixSpent = false;
            boneWardReadyAt = 0f;
        }

        /// <summary>Server: Phoenix Feather. True if it fires (and is used up for this floor).</summary>
        /// <summary>Server: the health fraction the Phoenix raises you to.</summary>
        public float PhoenixFraction => Relics.Has(this, Relics.Synergy.Undying) ? 1f : 0.5f;

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
            if (Wears(GearId.AshenShroud) && Time.time >= shroudReadyAt && (health.Current.Value - damage) / Mathf.Max(1f, health.Max.Value) < 0.3f)
            {
                shroudReadyAt = Time.time + 15f;
                foreach (var foe in CombatQuery.Circle(transform.position, 4.5f, Team.Enemies).ToArray())
                    foe.ServerDamage(40f * AttackMultiplier, transform.position, NetworkObjectId);
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
            bool weak = zone == (byte)HitZone.Weak;
            var c = weak ? new Color(1f, 0.5f, 0.2f) : new Color(1f, 0.92f, 0.55f);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.25f, weak ? "WEAK POINT" : "HEADSHOT", c);
            Art.Vfx.Embers(at, 6, c, 3f, 0.04f, 0.35f);
            if (IsOwner)
            {
                Art.Sfx.Play(Art.Sound.Shatter, at, 0.5f, weak ? 1.1f : 1.6f);
                Player.NetworkPlayer.Local?.ConfirmHit(weak ? 1.4f : 1.1f);
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
                foe.ServerDamage(30f * AttackMultiplier, at, NetworkObjectId);
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
            var items = Shop.For(OwnerClientId);
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
                case Shop.ItemKind.Relic: return HasRelic((Relic)(int)item.Amount) ? "Owned" : null;
                default: return Bonus(item.Stat) + item.Amount > item.Cap + 0.01f ? "Sold out" : null;
            }
        }

        /// <summary>Owner → server: buy catalogue item <paramref name="index"/> from the merchant.</summary>
        [Rpc(SendTo.Server)]
        public void BuyRpc(int index)
        {
            var merchant = World.DungeonDirector.MerchantPosition;
            if (merchant == null || Vector3.Distance(transform.position, merchant.Value) > Shop.MerchantRange + 1f) return;
            if (CannotBuyReason(index) != null) return;

            var item = Shop.For(OwnerClientId)[index];
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

        // The merchant's gear piece is one per floor: the seed of the floor it was bought on.
        private int gearBoughtSeed = int.MinValue;

        [Rpc(SendTo.Owner)]
        private void GearBoughtRpc(int seed) => gearBoughtSeed = seed;

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
            ActiveBlessing.Value = 0;
            Sigils.Value = 0;
            RankQ.Value = RankE.Value = 1;
            Armor.Value = default;
            runeBag.Clear();
            bag.Clear();
            Socket0.Value = Socket1.Value = 0;
            Charm.Value = default;
            health.HealMultiplier = 1f;
            Kills.Value = DamageDealt.Value = GoldEarned.Value = Downs.Value = Revives.Value = CombosTriggered.Value = 0;
            DamageTaken.Value = Crits.Value = AbyssKicks.Value = 0;
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
            if (Blessed(Blessing.Vigor)) hp *= 1.25f;
            health.ServerSetMax(hp, refill);
            health.Defense = Stats.GetValue(StatType.DEF);
        }
    }
}
