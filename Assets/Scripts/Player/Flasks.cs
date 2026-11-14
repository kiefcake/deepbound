using System.Collections;
using System.Linq;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// The potion pouch, for every class: up to <see cref="Max"/> of each kind. Health and mana
    /// potions are drunk; frost and holy flasks are thrown at the crosshair. Potions drop from the
    /// dead, come out of chests, and are sold by the merchant. There is no passive regeneration:
    /// the pouch is how you heal between fights.
    ///
    /// <list type="bullet">
    /// <item><b>1 — Health Potion.</b> Mends 45% of your health over 3 seconds.</item>
    /// <item><b>2 — the class DRAUGHT</b> (2026-10-28: it was a "Mana Potion" for classes without mana). Each class
    ///   brews its own: Mana Potion (Mage, Cleric), Draught of Iron (Knight, stamina), Adder's Tonic (Rogue, energy),
    ///   Hawkeye Draught (Ranger, focus), Martyr's Blood (Flagellant, fervor), Reagent Phial (Plague Doctor). It refills
    ///   70% and then, for <see cref="SurgeSeconds"/>, the resource never runs dry (SURGE).</item>
    /// <item><b>3 — Frost Flask.</b> Thrown: chills everything it touches (Shatter!), freezes what stands in water.</item>
    /// <item><b>4 — Holy Water.</b> Thrown: the dead reel (stunned) and burn; allies in the splash are cleansed and mended.</item>
    /// </list>
    /// </summary>
    public class Flasks : NetworkBehaviour
    {
        public enum Kind : byte { Health, Mana, Frost, Holy }
        public static readonly Kind[] All = { Kind.Health, Kind.Mana, Kind.Frost, Kind.Holy };
        public const int Max = 5;
        public const float HealFraction = 0.45f, HealSeconds = 3f, ManaFraction = 0.7f, SurgeSeconds = 3f;
        private const float Range = 16f, Flight = 0.55f, ThrowCooldown = 0.8f, DrinkCooldown = 1.2f;

        public readonly NetworkVariable<byte> Health = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Mana = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Frost = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Holy = new NetworkVariable<byte>();

        /// <summary>The fifth slot (see <see cref="RareItems"/>): one rare draught or tome.</summary>
        public readonly NetworkVariable<byte> Rare = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> IronbarkUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> BloodfuryUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> StasisUntil = new NetworkVariable<double>();
        private double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : 0;
        public RareItem RareHeld => (RareItem)Rare.Value;
        public bool Ironbark => Now < IronbarkUntil.Value;
        public bool Bloodfury => Now < BloodfuryUntil.Value;
        public bool InStasis => Now < StasisUntil.Value;
        private float rareReadyAt;

        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;
        private float readyAt;
        private float healingUntil;

        public static Color ColorOf(Kind k) => k == Kind.Health ? new Color(0.95f, 0.2f, 0.2f) : k == Kind.Mana ? DraughtColor(LocalClass)
            : k == Kind.Frost ? new Color(0.55f, 0.85f, 1f) : new Color(1f, 0.92f, 0.6f);
        public static string NameOf(Kind k) => k == Kind.Health ? "Health" : k == Kind.Mana ? DraughtShort(LocalClass) : k == Kind.Frost ? "Frost" : "Holy";
        public static string FullName(Kind k) => k == Kind.Health ? "Health Potion" : k == Kind.Mana ? DraughtName(LocalClass) : k == Kind.Frost ? "Frost Flask" : "Holy Water";

        /// <summary>The local player's class (the pouch's names and colours are drawn for them).</summary>
        public static Classes.ClassId LocalClass
        {
            get
            {
                var me = NetworkPlayer.Local;
                return me != null && me.TryGetComponent<PlayerProgress>(out var p) ? p.ClassId.Value : Classes.ClassId.Mage;
            }
        }

        public static string DraughtName(Classes.ClassId c) => c switch
        {
            Classes.ClassId.Knight => "Draught of Iron",
            Classes.ClassId.Rogue => "Adder's Tonic",
            Classes.ClassId.Ranger => "Hawkeye Draught",
            Classes.ClassId.Flagellant => "Martyr's Blood",
            Classes.ClassId.PlagueDoctor => "Reagent Phial",
            _ => "Mana Potion",
        };

        public static string DraughtShort(Classes.ClassId c) => c switch
        {
            Classes.ClassId.Knight => "Iron", Classes.ClassId.Rogue => "Adder", Classes.ClassId.Ranger => "Hawkeye",
            Classes.ClassId.Flagellant => "Blood", Classes.ClassId.PlagueDoctor => "Phial", _ => "Mana",
        };

        /// <summary>What the draught fills, in words.</summary>
        public static string DraughtResource(Classes.ClassId c) => c switch
        {
            Classes.ClassId.Knight => "stamina", Classes.ClassId.Rogue => "energy", Classes.ClassId.Ranger => "focus",
            Classes.ClassId.Flagellant => "fervor", Classes.ClassId.PlagueDoctor => "reagents", _ => "mana",
        };

        /// <summary>The draught takes the colour of the bar it fills.</summary>
        public static Color DraughtColor(Classes.ClassId c) => c switch
        {
            Classes.ClassId.Knight => new Color(0.9f, 0.68f, 0.18f),
            Classes.ClassId.Rogue => new Color(0.85f, 0.9f, 0.3f),
            Classes.ClassId.Ranger => new Color(0.45f, 0.85f, 0.4f),
            Classes.ClassId.Flagellant => new Color(0.9f, 0.2f, 0.15f),
            Classes.ClassId.PlagueDoctor => new Color(0.5f, 0.95f, 0.35f),
            Classes.ClassId.Cleric => new Color(0.95f, 0.8f, 0.35f),
            _ => new Color(0.3f, 0.5f, 1f),
        };

        public static string DraughtText(Classes.ClassId c) =>
            "refills 70% of your " + DraughtResource(c) + ", then for " + SurgeSeconds.ToString("F0") + " s it never runs dry";

        /// <summary>Owner: the draught's SURGE - the class resource is held full.</summary>
        public bool Surging => Time.time < surgeUntil;
        private float surgeUntil, nextSurgeTop;
        public static bool Drunk(Kind k) => k == Kind.Health || k == Kind.Mana;

        private NetworkVariable<byte> Slot(Kind k) => k == Kind.Health ? Health : k == Kind.Mana ? Mana : k == Kind.Frost ? Frost : Holy;
        public int Count(Kind k) => Slot(k).Value;
        public bool IsFull(Kind k) => Count(k) >= Max;

        /// <summary>Is a health potion mending you right now? (HUD.)</summary>
        public bool Healing => Time.time < healingUntil;

        private void Awake()
        {
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        public override void OnNetworkSpawn()
        {
            // Stone takes nothing; ironbark takes half.
            if (IsServer && life != null) life.Health.IncomingModifiers.Add((d, from) => InStasis ? 0f : (Ironbark ? d * RareItems.IronbarkTaken : d) * (InSanctuary(transform.position) ? RareItems.SanctuaryTaken : 1f));
        }

        /// <summary>Server: a rare draught or tome into the fifth slot. False if it is already full (the find stays on the floor).</summary>
        public bool ServerGiveRare(RareItem r)
        {
            if (!IsServer || r == RareItem.None || Rare.Value != 0) return false;
            Rare.Value = (byte)r;
            RareAddedRpc((byte)r);
            return true;
        }

        [Rpc(SendTo.Owner)]
        private void RareAddedRpc(byte r)
        {
            var item = (RareItem)r;
            UI.GameHud.Pickup("rare", 1, " " + RareItems.Name(item) + "  (5 to use)", RareItems.Color(item));
            Sfx.Play2D(Sound.Relic, 0.6f);
        }

        /// <summary>Owner -> server: use whatever is in the fifth slot.</summary>
        [Rpc(SendTo.Server)]
        public void UseRareRpc() => ServerUseRare();

        public bool ServerUseRare()
        {
            var r = RareHeld;
            if (!IsServer || r == RareItem.None || life.Downed.Value || InStasis) return false;
            double now = Now;
            switch (r)
            {
                case RareItem.Ironbark:
                    IronbarkUntil.Value = now + RareItems.IronbarkSeconds;
                    life.Health.ServerShield(life.Health.Max.Value * RareItems.IronbarkShield, RareItems.IronbarkSeconds);
                    if (TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerCleanse();
                    break;
                case RareItem.Bloodfury:
                    BloodfuryUntil.Value = now + RareItems.BloodfurySeconds;
                    break;
                case RareItem.GraveStillness:
                    StasisUntil.Value = now + RareItems.StasisSeconds;
                    // The dead lose you: no rage, and nobody may take you for a target until a moment after.
                    life.VeiledUntil = Time.time + RareItems.StasisSeconds + RareItems.StasisGrace;
                    foreach (var e in Enemies.EnemyBrain.All) if (e != null) e.ServerForget(life.Health);
                    break;
                case RareItem.PhoenixTome:
                    if (World.DungeonDirector.Instance == null || !World.DungeonDirector.Instance.ServerSummonPhoenix(progress)) return false;
                    break;
                case RareItem.StormTome:
                    StartCoroutine(Storm());
                    break;
                case RareItem.SanctuaryTome:
                    var at = transform.position;
                    Sanctuaries.Add((at, Time.time + RareItems.SanctuarySeconds));
                    StartCoroutine(SanctuaryMend(at));
                    SanctuaryFxRpc(at);
                    break;
            }
            Rare.Value = 0;
            RareUsedRpc((byte)r);
            return true;
        }

        // ---- Tomes ----

        /// <summary>Server: live sanctuaries (centre, end time). Anyone standing in one takes less.</summary>
        private static readonly System.Collections.Generic.List<(Vector3 at, float until)> Sanctuaries = new System.Collections.Generic.List<(Vector3, float)>();

        public static bool InSanctuary(Vector3 p)
        {
            for (int i = Sanctuaries.Count - 1; i >= 0; i--)
            {
                if (Time.time >= Sanctuaries[i].until) { Sanctuaries.RemoveAt(i); continue; }
                var d = p - Sanctuaries[i].at; d.y = 0f;
                if (d.sqrMagnitude <= RareItems.SanctuaryRadius * RareItems.SanctuaryRadius && Mathf.Abs(p.y - Sanctuaries[i].at.y) < 3f) return true;
            }
            return false;
        }

        private System.Collections.IEnumerator SanctuaryMend(Vector3 at)
        {
            float until = Time.time + RareItems.SanctuarySeconds;
            while (Time.time < until)
            {
                foreach (var pl in PlayerLife.All)
                {
                    if (pl == null || pl.Downed.Value) continue;
                    var d = pl.transform.position - at; d.y = 0f;
                    if (d.sqrMagnitude <= RareItems.SanctuaryRadius * RareItems.SanctuaryRadius) pl.Health.ServerHeal(pl.Health.Max.Value * RareItems.SanctuaryHealPerSecond * 0.5f);
                }
                yield return new WaitForSeconds(0.5f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SanctuaryFxRpc(Vector3 at)
        {
            var gold = new Color(1f, 0.9f, 0.55f, 0.28f);
            Telegraph.Show(at, RareItems.SanctuaryRadius, RareItems.SanctuarySeconds, null, gold);
            Vfx.Ring(at, RareItems.SanctuaryRadius, new Color(1f, 0.9f, 0.55f), 0.8f);
            Vfx.Embers(at + Vector3.up, 30, new Color(1f, 0.9f, 0.55f), 5f, 0.06f, 1.4f);
            Sfx.Play(Sound.Revive, at + Vector3.up, 1f, 0.8f);
        }

        /// <summary>Server: the Tome of the Storm, a bolt every 0.6 s on one of the dead near the reader.</summary>
        private System.Collections.IEnumerator Storm()
        {
            float until = Time.time + RareItems.StormSeconds;
            var struck = new System.Collections.Generic.List<Combat.Health>();
            while (Time.time < until && life != null && !life.Downed.Value)
            {
                struck.Clear();
                foreach (var h in Combat.Health.All)
                    if (h != null && h.Team == Team.Enemies && h.IsAlive && Vector3.Distance(h.transform.position, transform.position) <= RareItems.StormRange
                        && !(h.TryGetComponent<Enemies.GargoyleBrain>(out var g) && !g.Woken.Value)) struck.Add(h);
                if (struck.Count > 0)
                {
                    var t = struck[Random.Range(0, struck.Count)];
                    t.Tag("Flasks").ServerDamage(RareItems.StormDamage * (progress != null ? progress.AttackMultiplier : 1f), t.Center + Vector3.up * 4f, NetworkObjectId);
                    if (t.IsAlive && t.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(RareItems.StormStun);
                    StormFxRpc(t.Center);
                }
                yield return new WaitForSeconds(RareItems.StormEvery);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StormFxRpc(Vector3 at)
        {
            LightningBolt.Spawn(at + Vector3.up * 9f, new[] { at });
            Sfx.Play(Sound.Shatter, at, 0.9f, 0.6f);
            Vfx.Flash(at, new Color(0.6f, 0.75f, 1f), 4f, 6f, 0.15f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RareUsedRpc(byte r)
        {
            var item = (RareItem)r;
            var c = RareItems.Color(item);
            var at = transform.position + Vector3.up;
            Sfx.Play(RareItems.IsTome(item) ? Sound.Summon : Sound.Drink, at + Vector3.up * 0.5f, 0.9f, RareItems.IsTome(item) ? 1f : 0.8f);
            Vfx.Ring(transform.position, 2f, c, 0.5f);
            Vfx.Embers(at, 16, c, 3f, 0.06f, 0.9f);
            UI.GameHud.Feed(name + (RareItems.IsTome(item) ? " reads the " : " drinks the ") + RareItems.Name(item), c);
            if (item == RareItem.GraveStillness) StillnessShell.Spawn(transform, RareItems.StasisSeconds);
            if (IsOwner) player.ViewModelCast(false);
        }

        /// <summary>Server: put potions in the pouch. Returns how many fit (the rest stay where they were).</summary>
        /// <summary>Server: one bottle out of the pouch (a gift). False if there is none.</summary>
        public bool ServerTakeOne(Kind k)
        {
            if (!IsServer) return false;
            var slot = Slot(k);
            if (slot.Value == 0) return false;
            slot.Value--;
            return true;
        }

        /// <summary>A potion's full name for a given class (the draught differs by class).</summary>
        public static string FullNameFor(Kind k, Classes.ClassId c) => k == Kind.Health ? "Health Potion" : k == Kind.Mana ? DraughtName(c) : k == Kind.Frost ? "Frost Flask" : "Holy Water";

        public int ServerAdd(Kind k, int count)
        {
            if (!IsServer || count <= 0) return 0;
            var slot = Slot(k);
            int fit = Mathf.Min(count, Max - slot.Value);
            if (fit <= 0) return 0;
            slot.Value = (byte)(slot.Value + fit);
            AddedRpc((byte)k, (byte)slot.Value);
            return fit;
        }

        [Rpc(SendTo.Owner)]
        private void AddedRpc(byte kind, byte now)
        {
            var k = (Kind)kind;
            // One toast with the count (it used to be a feed line and a pickup toast for the same bottle).
            UI.GameHud.Pickup("potion" + kind, 1, " " + FullName(k) + "  (" + now + " / " + Max + ")", ColorOf(k));
            Sfx.Play2D(Sound.Potion, 0.5f);
        }

        /// <summary>Server: a new run's pouch: two health potions, one of everything else.</summary>
        public void ServerStartingPouch()
        {
            Health.Value = 2; Mana.Value = 1; Frost.Value = 1; Holy.Value = 1;
        }

        /// <summary>Server: run over, the pouch empties with everything else.</summary>
        public void ServerReset() { Health.Value = 0; Mana.Value = 0; Frost.Value = 0; Holy.Value = 0; Rare.Value = 0; IronbarkUntil.Value = BloodfuryUntil.Value = StasisUntil.Value = 0; }

        private void Update()
        {
            if (!IsOwner) return;
            TickSurge();
            var kb = Keyboard.current;
            if (kb == null || Time.time < readyAt || !life.CanAct || !motor.ActionsReady) return;
            if (UI.Binds.Down(UI.Bind.Rare) && !(World.DungeonDirector.InLobby && PlayerProgress.ClassChangeAllowed) && Time.time >= rareReadyAt)
            {
                rareReadyAt = Time.time + 1f;
                if (Rare.Value == 0) UI.GameHud.Feed("Nothing in the rare slot (5): draughts and tomes are found on champions, elites, in chests, and Silas keeps one", new Color(0.8f, 0.7f, 0.5f));
                else if (World.DungeonDirector.InLobby) UI.GameHud.Feed("Save it for the dungeon", new Color(0.8f, 0.7f, 0.5f));
                else UseRareRpc();
                return;
            }
            Kind? k = UI.Binds.Down(UI.Bind.Potion1) ? Kind.Health : UI.Binds.Down(UI.Bind.Potion2) ? Kind.Mana
                : UI.Binds.Down(UI.Bind.Potion3) ? Kind.Frost : UI.Binds.Down(UI.Bind.Potion4) ? Kind.Holy : (Kind?)null;
            // Number keys pick a class at camp before a run; there the pouch stays shut.
            if (k == null || (World.DungeonDirector.InLobby && PlayerProgress.ClassChangeAllowed)) return;
            if (Count(k.Value) <= 0) { UI.GameHud.Feed("No " + FullName(k.Value) + " in your pouch", new Color(0.8f, 0.6f, 0.5f)); readyAt = Time.time + 0.3f; return; }
            if (k == Kind.Health && life.Health.Current.Value >= life.Health.Max.Value - 0.5f) { UI.GameHud.Feed("You are already at full health", new Color(0.8f, 0.7f, 0.6f)); readyAt = Time.time + 0.3f; return; }
            var kit = player.ActiveKit;
            if (k == Kind.Mana && kit != null && kit.Resource >= kit.ResourceMax - 0.5f) { UI.GameHud.Feed("Your " + kit.ResourceName.ToLowerInvariant() + " is already full", new Color(0.6f, 0.7f, 0.9f)); readyAt = Time.time + 0.3f; return; }

            if (Drunk(k.Value))
            {
                readyAt = Time.time + DrinkCooldown;
                DrinkRpc((byte)k.Value);
                return;
            }
            readyAt = Time.time + ThrowCooldown;
            var origin = player.AimOrigin;
            var dir = player.AimForward;
            Vector3 aim;
            if (Physics.Raycast(origin, dir, out var hit, Range, ~0, QueryTriggerInteraction.Ignore)) aim = hit.point;
            else aim = Physics.Raycast(origin + dir * Range, Vector3.down, out var down, 30f, ~0, QueryTriggerInteraction.Ignore) ? down.point : origin + dir * Range;
            ThrowRpc((byte)k.Value, aim);
        }

        // ---- Drinking ----

        [Rpc(SendTo.Server)]
        private void DrinkRpc(byte kind)
        {
            var k = (Kind)kind;
            if (!life.CanAct || Count(k) <= 0 || !Drunk(k)) return;
            Slot(k).Value--;
            World.DungeonDirector.Instance?.QuestOnPotion();
            if (k == Kind.Health) StartCoroutine(Mend());
            else ManaOwnerRpc();
            DrinkFxRpc(kind);
        }

        /// <summary>Server: the health potion's mend, a little at a time (going down ends it).</summary>
        private IEnumerator Mend()
        {
            const int ticks = 12;
            float each = life.Health.Max.Value * HealFraction / ticks
                * (TryGetComponent<Progression.PlayerProgress>(out var pp) && pp.HasEdge(Progression.Edge.SecondWind) ? Progression.Edges.SecondWindHeal : 1f)
                * (pp != null ? pp.BuildPotionScale : 1f);
            healingUntil = Time.time + HealSeconds;
            // Shared Vow: half as much again for the nearest standing ally within 12 m.
            Health ally = null;
            float allyEach = 0f;
            if (TryGetComponent<Progression.PlayerProgress>(out var vow) && vow.HasRelic(Loot.Relic.SharedVow))
            {
                float best = 12f;
                foreach (var pl in PlayerLife.All)
                {
                    if (pl == null || pl == life || pl.Downed.Value) continue;
                    float d = Vector3.Distance(pl.transform.position, transform.position);
                    if (d < best) { best = d; ally = pl.Health; }
                }
                if (ally != null) allyEach = ally.Max.Value * HealFraction * 0.5f * vow.RelicPower(Loot.Relic.SharedVow) / ticks;
            }
            for (int i = 0; i < ticks; i++)
            {
                if (life.Downed.Value) yield break;
                life.Health.ServerHeal(each);
                if (ally != null && ally.IsAlive) ally.ServerHeal(allyEach);
                yield return new WaitForSeconds(HealSeconds / ticks);
            }
        }

        [Rpc(SendTo.Owner)]
        private void ManaOwnerRpc()
        {
            player.ActiveKit?.RestoreResource(ManaFraction);
            surgeUntil = Time.time + SurgeSeconds;
            if (!IsOwner) global::Deepbound.Art.Vfx.Embers(transform.position + Vector3.up * 1.2f, 12, DraughtColor(progress.ClassId.Value), 3f, 0.05f, 0.7f);
        }

        /// <summary>Owner, each frame: hold the resource full while the draught surges.</summary>
        private void TickSurge()
        {
            if (!Surging || Time.time < nextSurgeTop) return;
            nextSurgeTop = Time.time + 0.2f;
            player.ActiveKit?.RestoreResource(1f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DrinkFxRpc(byte kind)
        {
            var k = (Kind)kind;
            Sfx.Play(Sound.Drink, transform.position + Vector3.up * 1.5f, 0.8f);
            var drunkColor = k == Kind.Mana ? DraughtColor(progress.ClassId.Value) : ColorOf(k);
            Vfx.Embers(transform.position + Vector3.up * 1.2f, 8, drunkColor, 1.5f, 0.05f, 0.9f);
            if (IsOwner) UI.AfflictionOverlay.Drank(k == Kind.Health ? 0 : 1, drunkColor);
            if (k == Kind.Health) healingUntil = Time.time + HealSeconds;
            if (IsOwner) player.ViewModelCast(false);
        }

        /// <summary>Tests: drink as the owner would (host only).</summary>
        public void ServerDebugDrink(Kind k) => DrinkRpc((byte)k);

        // ---- Throwing ----

        /// <summary>Tests: throw as the owner would (host only).</summary>
        public void ServerDebugThrow(Kind k, Vector3 at)
        {
            if (Count(k) <= 0) ServerAdd(k, 1);
            ThrowRpc((byte)k, at);
        }

        [Rpc(SendTo.Server)]
        private void ThrowRpc(byte kind, Vector3 aim)
        {
            var k = (Kind)kind;
            if (!life.CanAct || Count(k) <= 0 || Drunk(k)) return;
            Slot(k).Value--;
            var from = transform.position + Vector3.up * 1.5f;
            if (Vector3.Distance(aim, from) > Range + 2f) aim = from + (aim - from).normalized * Range;
            // Land on the floor under the aim point (a wall hit drops to its foot).
            if (Physics.Raycast(aim + Vector3.up * 0.5f, Vector3.down, out var ground, 6f, ~0, QueryTriggerInteraction.Ignore)) aim = ground.point;
            ThrowFxRpc(kind, from, aim);
            StartCoroutine(Land(k, aim));
        }

        private IEnumerator Land(Kind k, Vector3 at)
        {
            yield return new WaitForSeconds(Flight);
            float att = progress.AttackMultiplier;
            switch (k)
            {
                case Kind.Frost:
                    foreach (var foe in CombatQuery.Circle(at, 3.6f, Team.Enemies).ToArray())
                    {
                        foe.Tag("Flasks").ServerDamage(10f * att, at, NetworkObjectId);
                        foe.ServerChill(4f);
                    }
                    break;
                case Kind.Holy:
                    foreach (var foe in CombatQuery.Circle(at, 4f, Team.Enemies).ToArray())
                    {
                        foe.Tag("Flasks").ServerDamage(24f * att, at, NetworkObjectId);
                        if (foe.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(1.8f);
                    }
                    // Holy water is the one thing the Tallyman flinches from.
                    var tally = World.Tallyman.Active;
                    if (tally != null && Vector3.Distance(tally.transform.position, at) <= 4.5f) tally.ServerRepel(World.Tallyman.RepelSeconds);
                    foreach (var ally in CombatQuery.Circle(at, 4f, Team.Players).ToArray())
                    {
                        if (ally.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerCleanse();
                        ally.ServerHeal(ally.Max.Value * 0.2f);
                    }
                    break;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ThrowFxRpc(byte kind, Vector3 from, Vector3 to)
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.3f);
            Sfx.Play(Sound.Swing, from, 0.6f, 1.4f);
            FlaskFlight.Spawn((Kind)kind, from, to, Flight);
        }
    }
}
