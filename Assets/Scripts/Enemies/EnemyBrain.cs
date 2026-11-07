using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Melee enemy AI. Runs only on the server; clients see the result through
    /// NetworkTransform and the animation RPCs.
    ///
    /// <para>
    /// The loop is deliberately readable — this is a Dark-and-Darker-style fight where
    /// the player's skill is reading tells. Every attack has a wind-up the player can see
    /// (weapon drawn back, eyes flaring), and the wind-up's length on screen comes from the
    /// same number the server waits on. Shield Bash interrupts a wind-up outright.
    /// </para>
    ///
    /// Aggro is shared: when one skeleton notices you, every skeleton in its room does —
    /// a room is a fight, not a queue of one-on-ones.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(NavMeshAgent))]
    public partial class EnemyBrain : NetworkBehaviour, ICrowdControl
    {
        [Header("Stats (scaled by depth and party size at spawn)")]
        [SerializeField] protected float maxHealth = 75f;

        /// <summary>Unscaled stats, for the balance report.</summary>
        public float BaseHealth => maxHealth;
        public float BaseDamage => damage;
        /// <summary>Server: hit harder or softer (a Panoply guard).</summary>
        public void ServerScaleDamage(float k) { if (IsServer) damage *= k; }
        /// <summary>Chance a fallen champion leaves a Sigil for each player.</summary>
        public const float ChampionSigilChance = 0.75f;
        public float BaseDefense => defense;
        public float AttackCycle => windup + recovery;
        [SerializeField] protected float damage = 16f;
        [SerializeField] protected float defense = 2f;

        [Header("Behaviour")]
        [SerializeField] protected float moveSpeed = 3.4f;
        [SerializeField] protected float attackRange = 2.1f;
        [SerializeField] protected float attackArc = 100f;
        [SerializeField] protected float windup = 0.6f;
        [SerializeField] protected float recovery = 0.9f;
        [SerializeField] protected float aggroRange = 13f;
        [SerializeField] protected float leashRange = 40f;
        [SerializeField] protected CharacterArt.SkeletonKind visualKind = CharacterArt.SkeletonKind.Warrior;
        [SerializeField] private NetworkObject lootPrefab;

        /// <summary>Server: boss summons drop nothing, or the boss becomes a gold farm.</summary>
        public bool NoLoot { get; set; }
        protected virtual LootTable Loot => LootTable.Skeleton;
        protected int depthForLoot;

        protected Health health;
        protected NavMeshAgent agent;
        public HumanoidRig Rig { get; private set; }

        protected Health target;
        private Health taunter;
        private float tauntUntil;
        protected float stunnedUntil;
        protected float busyUntil;
        protected bool attacking;
        private float strikeAt;
        private Vector3 home;
        private Vector3 knockback;
        protected float repathAt;
        private float nextRattle;

        public bool IsDead { get; private set; }

        /// <summary>
        /// Elite: tougher, harder-hitting, bigger, gilded — and with a real chance at a
        /// relic. Synced so every peer draws the gold.
        /// </summary>
        public readonly NetworkVariable<bool> Elite = new NetworkVariable<bool>();

        /// <summary>A named champion's title ("Grimbold the Unburied"); empty for everyone else.</summary>
        public readonly NetworkVariable<Unity.Collections.FixedString64Bytes> Title = new NetworkVariable<Unity.Collections.FixedString64Bytes>();
        public bool IsChampion => Title.Value.Length > 0;

        /// <summary>Server: when this elite next sings in the Bone Choir (a floor event).</summary>
        [System.NonSerialized] public float ChoirReadyAt;
        private bool championAnnounced;

        private static readonly string[] ChampionNames = { "Grimbold", "Morwen", "Garrick", "Hesk", "Vorn", "Ilsa", /* not Aldric or Ysolde: those are camp folk */ "Bram", "Cael", "Odo", "Sabine", "Tormund", "Wenna" };
        /// <summary>Append only: each is also a trait (Enemies/ChampionTraits).</summary>
        public static readonly string[] Epithets = { "the Unburied", "Gravefist", "the Hollow", "Bonegnawer", "the Patient", "Ashenhand", "the Tithe-Taker", "Oathbreaker", "the Pale", "Marrowgrin" };

        /// <summary>Server: make this the floor's champion — an elite with a name, more of everything, and a hoard.</summary>
        public void ServerMakeChampion()
        {
            if (IsBoss) return;
            if (!Elite.Value) ServerMakeElite();
            // The first floor's champion is a new party's first real wall: it hits a little softer there (its 60+ blows
            // took a fresh Cleric or Plague Doctor near death a dozen times in the autoplay runs).
            damage *= depthForLoot == 0 ? 0.9f : 1.15f;
            health.ServerSetMax(health.Max.Value * 1.6f, refill: true);
            health.Defense += 2f;
            SetChampionTitle(Pick(ChampionNames, usedNames), Pick(Epithets, usedEpithets));
        }

        /// <summary>Which of <see cref="ChampionTraits"/> this champion carries (server); -1 for everyone else.</summary>
        public int ChampionTrait { get; private set; } = -1;
        public int Depth => depthForLoot;
        private bool championRose, oathCalled;
        private int tithe;

        private void SetChampionTitle(string name, string epithet)
        {
            Title.Value = name + " " + epithet;
            if (ChampionTrait >= 0) return;
            ChampionTrait = System.Array.IndexOf(Epithets, epithet);
            ChampionTraits.EnsureHook();
            if (ChampionTrait == ChampionTraits.Patient) damage *= 1.5f;
            if (ChampionTrait == ChampionTraits.Unburied)
                health.IncomingModifiers.Add((dmg, from) =>
                {
                    // Getting up, it cannot be finished (the whole point of the Unburied); a killing blow still is one.
                    if (Time.time < risingUntil && dmg < BossBrain.KillDamage) return 0f;
                    if (championRose || IsDead || dmg < health.Current.Value || dmg >= BossBrain.KillDamage) return dmg;
                    championRose = true;
                    StartCoroutine(ChampionRise());
                    return Mathf.Max(0f, health.Current.Value - 1f);
                });
        }

        /// <summary>For tests: make this a champion carrying a given trait.</summary>
        public void ServerDebugChampion(int trait)
        {
            if (IsBoss) return;
            if (!Elite.Value) ServerMakeElite();
            SetChampionTitle("Test", Epithets[Mathf.Clamp(trait, 0, Epithets.Length - 1)]);
        }

        /// <summary>The Unburied: it falls, and gets up.</summary>
        private float risingUntil;

        private System.Collections.IEnumerator ChampionRise()
        {
            risingUntil = Time.time + 1.4f;
            ServerStun(1.6f);
            ChampionRiseRpc();
            yield return new WaitForSeconds(1.3f);
            if (!IsDead && health.IsAlive) health.ServerHeal(health.Max.Value * 0.4f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChampionRiseRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.6f, "IT WILL NOT STAY DOWN", new Color(1f, 0.75f, 0.3f));
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 1f, 0.6f);
            Vfx.Ring(Vfx.Floor(transform.position) + Vector3.up * 0.05f, 2f, new Color(1f, 0.75f, 0.3f), 0.6f);
        }

        /// <summary>Oathbreaker: wounded to half, it calls two of the dead (once).</summary>
        public void ServerOathbreak()
        {
            if (oathCalled || IsDead || health.Fraction >= 0.5f) return;
            oathCalled = true;
            for (int i = 0; i < 2; i++)
                World.DungeonDirector.Instance?.ServerDebugSpawn("skeleton", transform.position + Quaternion.Euler(0f, 90f + i * 180f, 0f) * transform.forward * 2.5f, false);
            OathRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OathRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.6f, "TO ME, OATHSWORN!", new Color(1f, 0.75f, 0.3f));
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 1.5f, 1f, 0.8f);
        }

        /// <summary>The Tithe-Taker pockets what it stole; it all spills back, and half again, when it falls.</summary>
        public void ServerTithe(int gold, Vector3 at)
        {
            tithe += gold;
            TitheRpc(gold, at);
        }

        /// <summary>For tests: gold it carries.</summary>
        public int Tithe => tithe;

        [Rpc(SendTo.ClientsAndHost)]
        private void TitheRpc(int gold, Vector3 at)
        {
            UI.GameHud.ShowCombo(at + Vector3.up * 0.6f, "-" + gold + " GOLD", new Color(1f, 0.82f, 0.3f));
            Sfx.Play(Sound.Coins, at, 0.7f, 0.8f);
        }

        // A run's champions never share a name or an epithet (an autoplay run met three Osrics, 2026-10-24); the
        // pools refill once spent, and on a new run.
        private static readonly HashSet<string> usedNames = new HashSet<string>(), usedEpithets = new HashSet<string>();
        private static double namesRun = -1;

        private static string Pick(string[] pool, HashSet<string> used)
        {
            var director = World.DungeonDirector.Instance;
            double run = director != null ? director.RunStartedAt.Value : 0;
            if (run != namesRun) { namesRun = run; usedNames.Clear(); usedEpithets.Clear(); }
            if (used.Count >= pool.Length) used.Clear();
            string pick;
            do pick = pool[UnityEngine.Random.Range(0, pool.Length)]; while (used.Contains(pick));
            used.Add(pick);
            return pick;
        }

        /// <summary>Server: shoved off a ledge — land below, a little the worse for it.</summary>
        public void ServerFallTo(Vector3 ground, float damageFraction, ulong by)
        {
            if (IsDead || !agent.enabled) return;
            agent.Warp(ground);
            knockback = Vector3.zero;
            ServerStun(0.8f);
            health.ServerDamage(health.Max.Value * Mathf.Clamp(damageFraction, 0.05f, 0.4f), ground + Vector3.up * 3f, by);
        }

        /// <summary>Server: shoved into a chasm. It does not climb back out.</summary>
        public void ServerFallIntoAbyss(Vector3 at, ulong by)
        {
            if (IsDead) return;
            NoLoot = true;
            health.ServerDamage(1e6f, at + Vector3.up, by);
            StartCoroutine(Plummet(at));
        }

        private System.Collections.IEnumerator Plummet(Vector3 at)
        {
            yield return null;
            var p = new Vector3(at.x, transform.position.y, at.z);
            float v = 0f;
            for (float t = 0f; t < 1.6f; t += Time.deltaTime)
            {
                v += 20f * Time.deltaTime;
                p.y -= v * Time.deltaTime;
                transform.position = p;
                yield return null;
            }
        }

        /// <summary>The elite's affix (see <see cref="EliteAffix"/>). Synced for the status tags.</summary>
        public readonly NetworkVariable<byte> Affix = new NetworkVariable<byte>();
        public EliteAffix AffixKind => (EliteAffix)Affix.Value;
        private bool wardUp;

        /// <summary>Bosses opt out of poise breaks and backstabs; they have their own answers.</summary>
        protected virtual bool IsBoss => false;

        /// <summary>Heavy enemies cannot be shoved around by a bash.</summary>
        protected virtual bool Heavy => false;

        protected int scaledPlayersBase = 1;

        /// <summary>How much more health the party's size gave this enemy than it would have against one player.</summary>
        public float PartyHealthScale => Difficulty.PartyHealth(scaledPlayersBase, IsBoss, depthForLoot) / Difficulty.PartyHealth(1, IsBoss, depthForLoot);
        private float surroundAngle;

        protected virtual void Awake()
        {
            health = GetComponent<Health>();
            agent = GetComponent<NavMeshAgent>();
            agent.enabled = false;
        }

        /// <summary>Extra size for the body (a Warden is a giant of its kind).</summary>
        protected virtual float VisualScale => 1f;
        /// <summary>Every peer, once the body is built and before it is merged: add a creature's own dressing.</summary>
        protected virtual void DressRig(HumanoidRig rig) { }

        /// <summary>Walking dead climb out of the floor when they appear; spirits, statues and mimics do not.</summary>
        private static bool RisesFromGround(CharacterArt.SkeletonKind k) =>
            k != CharacterArt.SkeletonKind.Wraith && k != CharacterArt.SkeletonKind.Mimic && k != CharacterArt.SkeletonKind.Gargoyle && k != CharacterArt.SkeletonKind.Mourner && k != CharacterArt.SkeletonKind.Unsung;

        /// <summary>Every live enemy (any peer), for the Bestiary and other look-ups.</summary>
        public static readonly List<EnemyBrain> All = new List<EnemyBrain>();
        private float nextAcquire;

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this)) All.Add(this);
            gameObject.AddComponent<StunDaze>().Init(this);
            if (!IsBoss) gameObject.AddComponent<EnemyMurmur>().Init(this);
            CharacterArt.SkeletonVariant = (int)((NetworkObjectId * 2654435761UL) & 0x7FFFFFFF);
            Rig = CharacterArt.BuildSkeleton(transform, visualKind);
            if (Rig != null)
            {
                if (VisualScale != 1f) Rig.transform.localScale *= VisualScale;
                DressRig(Rig);
            }
            // Fewer, bigger meshes: same look, a fraction of the draw calls.
            MeshKit.MergeRig(Rig);
            if (Rig != null) Rig.WatchViewer = true;
            // Champions stand larger (applied after the rig exists, so late joiners see it too).
            // (Multiplied, once: it used to SET 1.22 and threw away a brute's own size and the elite growth.)
            ApplySize();
            Title.OnValueChanged += (_, t) => { if (t.Length > 0) ScaleAsChampion(); };
            if (Title.Value.Length > 0) ScaleAsChampion();
            if (Rig != null && !IsBoss && RisesFromGround(visualKind)) Rig.gameObject.AddComponent<RiseFromGround>();
            if (Rig != null && (IsBoss || Heavy || Rig.transform.lossyScale.y >= 1.12f)) FitBodyToRig();
            health.Hurt += OnHurtVisual;
            if (GetComponent<EnemyStatusFx>() == null) gameObject.AddComponent<EnemyStatusFx>();
            Elite.OnValueChanged += (_, now) => ApplyEliteVisual(now);
            if (Elite.Value) ApplyEliteVisual(true);

            if (!IsServer) return;

            home = transform.position;
            health.Defense = defense;
            health.Died += OnDied;
            health.DamagedServer += OnDamagedServer;
            // From behind hurts more - bosses too, if someone holds their rage so another can get round them.
            health.IncomingModifiers.Add(BackstabModifier);
            health.IncomingModifiers.Add((d, from) => Enraged ? d * EnrageTaken : d);
            // Each enemy takes its own place around whoever it is fighting, so a pack
            // surrounds you instead of queueing in a line.
            surroundAngle = (NetworkObjectId * 137.508f) % 360f;

            agent.enabled = true;
            agent.speed = moveSpeed;
            agent.angularSpeed = 540f;
            agent.acceleration = 30f;
            agent.stoppingDistance = attackRange * 0.75f;
            agent.autoBraking = true;
            if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }

        /// <summary>Server: scale for party size and dungeon depth, then fill health. Call right after Spawn.</summary>
        public void ServerScale(int players, int depth)
        {
            scaledPlayersBase = players;
            if (!sizeStatsApplied) { sizeStatsApplied = true; ServerApplySizeStats(); }
            float hpScale = (IsBoss ? Difficulty.PartyHealth(players, true, depth) : Difficulty.EnemyHealth(players) * Difficulty.RankHealth) * Difficulty.DepthHealth(depth) * Difficulty.Level.Health;
            damage *= Difficulty.Damage(players) * Difficulty.DepthDamage(depth) * Difficulty.Level.Damage * (IsBoss ? 1f : Difficulty.RankDamage);
            depthForLoot = depth;
            health.ServerSetMax(maxHealth * hpScale, refill: true);
        }

        /// <summary>
        /// Server: the party grew or shrank mid-floor (a friend joined, or dropped out): rescale this one's health and
        /// bite to the new size, keeping how hurt it is. A two-player room no longer stays four-player tough after two leave.
        /// </summary>
        public void ServerRescaleParty(int players)
        {
            players = Mathf.Max(1, players);
            if (!IsServer || players == scaledPlayersBase || health == null || !health.IsAlive) return;
            float k = Difficulty.PartyHealth(players, IsBoss, depthForLoot) / Difficulty.PartyHealth(scaledPlayersBase, IsBoss, depthForLoot);
            float frac = health.Fraction;
            health.ServerSetMax(health.Max.Value * k, refill: false);
            health.ServerSetFraction(frac);
            damage *= Difficulty.Damage(players) / Difficulty.Damage(scaledPlayersBase);
            scaledPlayersBase = players;
            if (this is BossBrain bossBrain) bossBrain.SyncScaledPlayers(players);
        }

        /// <summary>
        /// Frenzied (2026-10-20): below half health it attacks far faster (wind-ups and recoveries x0.6), and
        /// says so once. Burst it down from high, or stun it when it turns.
        /// </summary>
        public const float FrenzyPace = 0.6f;
        private bool frenzyAnnounced;
        public bool Frenzied => AffixKind == EliteAffix.Frenzied && health != null && health.Fraction < 0.5f;

        /// <summary>Synced looks for the status visuals: bit 0 the ward is up, bit 1 enraged, bit 2 a soulbound twin lies severed.</summary>
        public readonly NetworkVariable<byte> FxFlags = new NetworkVariable<byte>();
        public bool WardedNow => (FxFlags.Value & 1) != 0;
        public bool Enraged => (FxFlags.Value & 2) != 0;
        private float enragedUntil;

        /// <summary>
        /// Weak point rage (2026-10-24, the user: "maybe a certain mob gets enraged when you hit them there"): the
        /// beasts with a weak point you can strike - brutes, ghouls, gargoyles, frost wights, plaguebearers, mimics -
        /// go berserk for a while when it bursts: faster on their feet and in their swings, but they take 15% more.
        /// </summary>
        public bool EnragesOnWeakHit => visualKind == CharacterArt.SkeletonKind.Brute || visualKind == CharacterArt.SkeletonKind.Ghoul
            || visualKind == CharacterArt.SkeletonKind.Gargoyle || visualKind == CharacterArt.SkeletonKind.FrostWight
            || visualKind == CharacterArt.SkeletonKind.Plaguebearer || visualKind == CharacterArt.SkeletonKind.Mimic;
        public const float EnrageSeconds = 6f, EnragePace = 0.75f, EnrageSpeed = 1.35f, EnrageTaken = 1.15f;

        public void ServerEnrage(float seconds)
        {
            if (!IsServer || IsDead) return;
            bool was = Enraged;
            enragedUntil = Mathf.Max(enragedUntil, Time.time + seconds);
            if (!was) { FxFlags.Value = (byte)(FxFlags.Value | 2); EnrageRpc(); }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EnrageRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.6f, "ENRAGED!", new Color(1f, 0.25f, 0.15f));
            if (VoicePitch > 0f) Sfx.Play(Sound.Roar, transform.position + Vector3.up * 1.6f, 0.9f, VoicePitch);
            Vfx.Ring(transform.position + Vector3.up * 0.05f, 2f, new Color(1f, 0.25f, 0.15f), 0.5f);
        }
        protected float Pace
        {
            get
            {
                if (!Frenzied) return (Enraged ? EnragePace : 1f) * World.DungeonDirector.OmenPace * (ChampionTrait == ChampionTraits.Patient ? 1.3f : 1f);
                if (!frenzyAnnounced && IsServer) { frenzyAnnounced = true; FrenzyRpc(); }
                return FrenzyPace * World.DungeonDirector.OmenPace;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FrenzyRpc()
        {
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 1.6f, 0.8f, 1.4f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.4f, "FRENZY!", new Color(1f, 0.3f, 0.22f));
            Vfx.Ring(transform.position + Vector3.up * 0.05f, 1.6f, new Color(1f, 0.3f, 0.22f), 0.5f);
        }

        /// <summary>Server: promote to elite. Call after <see cref="ServerScale"/>.</summary>
        public void ServerMakeElite(EliteAffix forced = EliteAffix.None)
        {
            if (IsBoss) return;
            Elite.Value = true;
            damage *= 1.3f;
            moveSpeed *= 1.1f;
            health.ServerSetMax(health.Max.Value * 2.2f, refill: true);
            health.Defense += 3f;

            var affix = forced != EliteAffix.None ? forced : (EliteAffix)UnityEngine.Random.Range(1, EliteAffixes.Count + 1);
            Affix.Value = (byte)affix;
            if (affix == EliteAffix.Warded)
            {
                wardUp = true;
                health.IncomingModifiers.Add(WardModifier);
            }
            if (affix == EliteAffix.Swift) moveSpeed *= 1.35f;
            if (affix == EliteAffix.Vampiric) EnsureVampireHook();
            if (affix == EliteAffix.Thorned) EnsureThornHook();
            if (affix == EliteAffix.Hushing || affix == EliteAffix.Concussive) EnsureAfflictHook();
        }

        private static bool afflictHooked;

        /// <summary>Hushing: its blows SILENCE you (2.5 s). Concussive: its blows DAZE you (1.5 s). Both have their grace.</summary>
        private static void EnsureAfflictHook()
        {
            if (afflictHooked) return;
            afflictHooked = true;
            Health.ServerAnyDamaged += (target, dealt, attackerId) =>
            {
                if (target == null || target.Team != Team.Players || dealt <= 0f) return;
                var net = NetworkManager.Singleton;
                if (net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj)) return;
                if (!obj.TryGetComponent<EnemyBrain>(out var brain) || brain.IsDead || !target.TryGetComponent<Player.PlayerDebuffs>(out var deb)) return;
                if (brain.AffixKind == EliteAffix.Hushing) deb.ServerSilence(2.5f);
                else if (brain.AffixKind == EliteAffix.Concussive) deb.ServerDaze(1.5f);
            };
        }

        private static bool thornHooked;

        /// <summary>Thorned: a fifth of what you deal in melee range comes back to you. Fight it from range.</summary>
        private static void EnsureThornHook()
        {
            if (thornHooked) return;
            thornHooked = true;
            Health.ServerAnyDamaged += (target, dealt, attackerId) =>
            {
                if (target == null || target.Team != Team.Enemies || dealt <= 0f || attackerId == ulong.MaxValue) return;
                if (!target.TryGetComponent<EnemyBrain>(out var brain) || brain.AffixKind != EliteAffix.Thorned) return;
                var net = NetworkManager.Singleton;
                if (net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj)) return;
                if (!obj.TryGetComponent<Health>(out var attacker) || attacker.Team != Team.Players) return;
                if (Vector3.Distance(attacker.transform.position, target.transform.position) > 3.5f) return;
                attacker.ServerDamage(dealt * 0.2f, target.transform.position);
                brain.ThornFxRpc(attacker.Center);
            };
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ThornFxRpc(Vector3 at) => Vfx.Embers(at, 4, new Color(0.5f, 0.75f, 0.3f), 2.5f, 0.04f, 0.3f);

        /// <summary>Warded: most damage turned aside until a stun or a chill breaks the ward.</summary>
        private float WardModifier(float dealt, Vector3 attackerPosition)
        {
            if (!wardUp) return dealt;
            if (IsStunned || health.Chilled.Value)
            {
                ServerBreakWard();
                return dealt;
            }
            return dealt * 0.35f;
        }

        private void ServerBreakWard()
        {
            if (!wardUp) return;
            wardUp = false;
            Affix.Value = (byte)EliteAffix.None;
            Classes.Combos.Popup(health, Classes.ComboKind.WardBroken);
        }

        private static bool vampireHooked;

        private static void EnsureVampireHook()
        {
            if (vampireHooked) return;
            vampireHooked = true;
            Health.ServerAnyDamaged += (target, dealt, attackerId) =>
            {
                if (target.Team != Team.Players) return;
                var net = NetworkManager.Singleton;
                if (net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj)) return;
                if (!obj.TryGetComponent<EnemyBrain>(out var brain) || brain.IsDead || brain.AffixKind != EliteAffix.Vampiric) return;
                // A Mark cuts the drain: the Ranger's answer to a Vampiric elite.
                if (!brain.health.Marked.Value) brain.health.ServerHeal(dealt * 0.35f);
            };
        }

        private float BackstabModifier(float dealt, Vector3 attackerPosition)
        {
            var to = attackerPosition - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f || Vector3.Angle(transform.forward, to) < 115f) return dealt;
            BackstabRpc();
            return dealt * (IsBoss ? Difficulty.BossBackstabMultiplier : Difficulty.BackstabMultiplier);
        }

        private bool eliteGrown;

        /// <summary>A gilded enemy: bigger, gold-eyed, wreathed in a slow gold ring.</summary>
        private void ApplyEliteVisual(bool elite)
        {
            if (!elite || Rig == null || eliteGrown) return;
            eliteGrown = true;
            Grow(1.18f);
            if (flash != null) flash.Invalidate();
            Rig.GlowColor = new Color(1f, 0.78f, 0.25f);
            var ring = MeshKit.Part(transform, "EliteAura", MeshKit.Lathe("eliteRing", new[]
            {
                new Vector2(0.6f, 0f), new Vector2(0.7f, 0.02f), new Vector2(0.6f, 0.04f),
            }, 20, false), Mat.Glow, Vector3.up * 0.04f);
            ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.25f) * 1.8f);
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var light = new GameObject("EliteLight").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.localPosition = Vector3.up * 1.2f;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.3f);
            light.range = 4f;
            light.intensity = 2f;
        }

        // ------------------------------------------------------------------
        // Server loop
        // ------------------------------------------------------------------

        /// <summary>
        /// Big creatures: size the hit capsule to the model itself (shoulders, shields, robes),
        /// so what you see is what you hit. Weapons and glow are left out.
        /// </summary>
        private void FitBodyToRig()
        {
            var capsule = GetComponent<CapsuleCollider>();
            if (capsule == null) return;
            float top = 0f, rx = 0f, rz = 0f;
            bool any = false;
            foreach (var mf in Rig.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<Renderer>();
                if (mf.sharedMesh == null || r == null) continue;
                string n = mf.name;
                if (r.sharedMaterial != null && (r.sharedMaterial.name.Contains("Glow") || r.sharedMaterial.name.Contains("Telegraph"))) continue;
                if (n.Contains("Blade") || n.Contains("Sword") || n.Contains("Staff") || n.Contains("Mace") || n.Contains("Spear") || n.Contains("Axe") || n.Contains("Trail")) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    top = Mathf.Max(top, p.y);
                    rx = Mathf.Max(rx, Mathf.Abs(p.x));
                    rz = Mathf.Max(rz, Mathf.Abs(p.z));
                    any = true;
                }
            }
            if (!any) return;
            // Bounding extents overstate a round body: take a bit less than the wider side.
            float radius = Mathf.Clamp(Mathf.Lerp(Mathf.Min(rx, rz), Mathf.Max(rx, rz), 0.5f) * 0.85f, 0.3f, 1.8f);
            float height = Mathf.Clamp(top, radius * 2f, 8f);
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = new Vector3(0f, height / 2f, 0f);
            health.InvalidateBody();
        }

        protected virtual void Update()
        {
            if (IsServer)
            {
                if (!IsDead) ServerTickThreat();
                ServerSyncStatus();
                if (wardUp && (IsStunned || health.Chilled.Value)) ServerBreakWard();
            }
            if (!IsServer || IsDead || !agent.enabled || !agent.isOnNavMesh) return;

            ApplyKnockback();
            // Chill slows the walk, not the attack timing: you get time to reposition, not
            // a free win.
            agent.speed = (health.Chilled.Value ? moveSpeed * 0.5f : moveSpeed) * SpeedScale * World.DungeonDirector.WaterSlow(transform.position) * (Enraged ? EnrageSpeed : 1f) * World.DungeonDirector.OmenSpeed * World.DungeonDirector.EventSpeed;
            // Keep the synced looks current: the ward, and rage running out.
            byte fx = (byte)((wardUp ? 1 : 0) | (Enraged && Time.time < enragedUntil ? 2 : 0) | (Severed ? 4 : 0));
            if (FxFlags.Value != fx) FxFlags.Value = fx;

            if (Time.time < stunnedUntil)
            {
                agent.isStopped = true;
                return;
            }

            if (lunging) { StepLunge(); return; }
            if (lungeStartAt > 0f)
            {
                agent.isStopped = true;
                if (Time.time >= lungeStartAt) { lungeStartAt = -1f; lunging = true; }
                return;
            }

            if (attacking)
            {
                FaceTarget(0.35f);
                if (Time.time >= strikeAt)
                {
                    attacking = false;
                    Strike();
                }
                return;
            }

            if (Time.time < busyUntil)
            {
                agent.isStopped = true;
                return;
            }

            if (Lured)
            {
                target = null;
                agent.isStopped = false;
                if (Time.time >= repathAt) { repathAt = Time.time + 0.4f; agent.SetDestination(lureAt); }
                return;
            }
            target = PickTarget();
            // First sight of you: a beat to rear up and rattle, so being spotted reads (and costs them a moment).
            if (target != null && !engaged)
            {
                engaged = true;
                if (!IsBoss && visualKind != CharacterArt.SkeletonKind.Gargoyle && visualKind != CharacterArt.SkeletonKind.Mourner && visualKind != CharacterArt.SkeletonKind.Mimic)
                {
                    busyUntil = Mathf.Max(busyUntil, Time.time + 0.4f);
                    var look = target.transform.position - transform.position; look.y = 0f;
                    if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
                    PlayActionRpc(RigAction.Cast, 0.4f);
                    NoticeRpc();
                }
            }
            else if (target == null) engaged = false;
            if (target != null && IsChampion && !championAnnounced)
            {
                championAnnounced = true;
                ChampionSeenRpc(Title.Value.ToString());
            }
            if (target == null)
            {
                ReturnHome();
                return;
            }

            float dist = FlatDistance(target.transform.position);
            if (ChooseSpecialAttack(dist)) return;
            if (TryLunge(dist)) return;

            if (dist <= attackRange)
            {
                BeginAttack();
                return;
            }

            agent.isStopped = false;
            if (Time.time >= repathAt)
            {
                repathAt = Time.time + 0.25f;
                agent.SetDestination(ChaseDestination(dist));
            }

            if (Time.time >= nextRattle)
            {
                nextRattle = Time.time + Random.Range(2.5f, 5f);
                RattleRpc();
            }
        }

        /// <summary>
        /// Where to run while chasing. Far away: straight at the target. Close in: to this
        /// enemy's own slot on a ring around the target, so packs fan out and flank.
        /// </summary>
        protected virtual Vector3 ChaseDestination(float distanceToTarget)
        {
            var t = target.transform.position;
            if (distanceToTarget > 6f) return t;
            var slot = Quaternion.Euler(0f, surroundAngle, 0f) * Vector3.forward * (attackRange * 0.8f);
            var p = t + slot;
            return NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas) ? hit.position : t;
        }

        /// <summary>Bosses override this to use abilities. Return true if an action was started.</summary>
        protected virtual bool ChooseSpecialAttack(float distanceToTarget) => false;

        /// <summary>True during a telegraphed wind-up: the window a heavy hit can break.</summary>
        protected bool WindingUp => attacking;

        /// <summary>Interrupt whatever is being wound up, with a stagger. Subclasses cancel their own telegraphs too.</summary>
        protected virtual void InterruptWindup(float stagger)
        {
            attacking = false;
            busyUntil = Time.time + stagger;
            PlayActionRpc(RigAction.Stagger, stagger);
        }

        // ---- Move set: every rank-and-file melee enemy mixes its swing with a heavy blow, a two-hit
        //      flurry, a wide sweep and a lunge. Each has its own tell (Status bits 8/16/32, the HUD glyph). ----
        public enum Move : byte { Swing = 0, Heavy = 1, Flurry = 2, Sweep = 3, Lunge = 4 }
        protected Move currentMove;
        private int flurryLeft;
        private float lungeReadyAt;
        private bool lunging;
        private Vector3 lungeDir;
        private float lungeTravelled;
        private readonly System.Collections.Generic.HashSet<Health> lungeHit = new System.Collections.Generic.HashSet<Health>();
        public const float LungeMin = 3f, LungeMax = 7f, LungeSpeed = 13f, LungeCooldown = 7f;

        /// <summary>Does this enemy use the shared move set? Melee rank and file only.</summary>
        protected virtual bool UsesMoveSet => !IsBoss && attackRange <= 3f && damage > 0f;

        private Move PickMove()
        {
            if (!UsesMoveSet) return Move.Swing;
            float r = Random.value;
            return r < 0.5f ? Move.Swing : r < 0.7f ? Move.Heavy : r < 0.86f ? Move.Flurry : Move.Sweep;
        }

        protected virtual void BeginAttack()
        {
            currentMove = PickMove();
            float w = windup * Pace * (currentMove == Move.Heavy ? 1.7f : currentMove == Move.Sweep ? 1.25f : 1f);
            flurryLeft = currentMove == Move.Flurry ? 1 : 0;
            attacking = true;
            agent.isStopped = true;
            strikeAt = Time.time + w;
            busyUntil = strikeAt + recovery * Pace * (currentMove == Move.Heavy ? 1.3f : 1f);
            PlayActionRpc(RigAction.Windup, w);
        }

        protected virtual void Strike()
        {
            PlayActionRpc(RigAction.Swing, 0.3f);
            float mult = currentMove == Move.Heavy ? 1.8f : currentMove == Move.Flurry ? 0.7f : currentMove == Move.Sweep ? 0.8f : 1f;
            float arc = currentMove == Move.Heavy ? Mathf.Min(attackArc, 60f) : currentMove == Move.Sweep ? 220f : attackArc;
            float reach = attackRange + (currentMove == Move.Sweep ? 0.8f : 0.5f);
            var origin = transform.position + Vector3.up * 1.2f;
            foreach (var victim in CombatQuery.Arc(origin, transform.forward, reach, arc, Team.Players))
            {
                victim.ServerDamage(damage * mult, transform.position, NetworkObjectId);
                if (currentMove == Move.Heavy && victim.TryGetComponent<KnockbackReceiver>(out var kb))
                    kb.KnockbackRpc((victim.transform.position - transform.position).normalized * 6f + Vector3.up * 2f);
            }
            if (currentMove == Move.Heavy) ImpactThudRpc(transform.position + transform.forward * 1.2f);
            // Flurry: the second blow follows fast.
            if (flurryLeft > 0)
            {
                flurryLeft--;
                attacking = true;
                strikeAt = Time.time + 0.32f;
                busyUntil = strikeAt + recovery * Pace;
                PlayActionRpc(RigAction.Windup, 0.32f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ImpactThudRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 0.6f, 1.3f);
            Vfx.Smoke(at, 2, 0.4f, 0.8f);
        }

        /// <summary>Server: start a lunge if one fits (mid range, clear line, off cooldown). Returns true if it began.</summary>
        private bool TryLunge(float dist)
        {
            if (!UsesMoveSet || Time.time < lungeReadyAt || dist < LungeMin || dist > LungeMax || target == null) return false;
            if (!CombatQuery.ClearLine(transform.position + Vector3.up * 1.2f, target)) return false;
            lungeReadyAt = Time.time + LungeCooldown * Random.Range(0.8f, 1.3f);
            if (Random.value > 0.55f) return false;
            lungeDir = target.transform.position - transform.position;
            lungeDir.y = 0f;
            lungeDir.Normalize();
            transform.rotation = Quaternion.LookRotation(lungeDir);
            currentMove = Move.Lunge;
            agent.isStopped = true;
            lungeTravelled = 0f;
            lungeHit.Clear();
            float w = 0.55f;
            lungeStartAt = Time.time + w;
            busyUntil = lungeStartAt + 0.6f + recovery;
            PlayActionRpc(RigAction.Windup, w);
            LungeWarnRpc(transform.position, lungeDir, Mathf.Min(dist + 1.5f, LungeMax + 1f), w);
            return true;
        }
        private float lungeStartAt = -1f;

        private void StepLunge()
        {
            float step = LungeSpeed * Time.deltaTime;
            var before = transform.position;
            agent.Move(lungeDir * step);
            float moved = Vector3.Distance(before, transform.position);
            lungeTravelled += moved;
            foreach (var victim in CombatQuery.Circle(transform.position + Vector3.up * 0.9f, 1.1f, Team.Players))
            {
                if (!lungeHit.Add(victim)) continue;
                victim.ServerDamage(damage * 1.25f, transform.position - lungeDir, NetworkObjectId);
            }
            if (moved < step * 0.3f || lungeTravelled >= LungeMax + 1f || lungeHit.Count > 0)
            {
                lunging = false;
                PlayActionRpc(RigAction.Swing, 0.3f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LungeWarnRpc(Vector3 from, Vector3 dir, float length, float seconds) =>
            Telegraph.ShowLine(from, dir, length, 1.2f, seconds, this);

        protected virtual Health PickTarget()
        {
            if (taunter != null && Time.time < tauntUntil && PlayerLife.CanTarget(taunter)) return taunter;

            // Rage: hunt whoever has angered it most - but only change its mind when someone clearly
            // overtakes the one it is after (by a fifth), so the tank can hold it and a Rogue get round it.
            Health raged = null;
            float ragedThreat = 0f, currentThreat = 0f;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !PlayerLife.CanTarget(p.Health) || FlatDistance(p.transform.position) > leashRange) continue;
                threat.TryGetValue(p.NetworkObjectId, out var t);
                if (target == p.Health) currentThreat = t;
                if (t > ragedThreat) { ragedThreat = t; raged = p.Health; }
            }
            bool targetValid = target != null && PlayerLife.CanTarget(target) && FlatDistance(target.transform.position) < leashRange && FlatDistance(home) < leashRange;
            if (raged != null && FlatDistance(home) < leashRange)
            {
                if (!targetValid || (raged != target && ragedThreat > currentThreat * SwitchThreshold)) return raged;
                return target;
            }
            // Stay on the current target while it is alive, visible and within leash.
            if (targetValid) return target;

            // Looking costs a linecast per player: an idle pack in range did that every frame. Staggered, and too quick to see.
            if (Time.time < nextAcquire) return null;
            nextAcquire = Time.time + 0.15f + (NetworkObjectId % 5) * 0.02f;

            Health best = null;
            float bestDist = aggroRange;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                float d = FlatDistance(p.transform.position);
                if (d < bestDist && CombatQuery.ClearLine(transform.position + Vector3.up * 1.5f, p.Health))
                {
                    best = p.Health;
                    bestDist = d;
                }
            }

            if (best != null && target == null) AlertPack(best);
            if (best != null) ServerAddThreat(best.NetworkObjectId, 1f);
            return best;
        }

        // ------------------------------------------------------------------
        // Rage (threat): who it wants dead
        // ------------------------------------------------------------------

        private readonly Dictionary<ulong, float> threat = new Dictionary<ulong, float>();
        private float lastThreatTick, nextThreatSync;
        private static readonly List<ulong> threatKeys = new List<ulong>();

        /// <summary>A rival must exceed the current target's rage by this much to take it.</summary>
        public const float SwitchThreshold = 1.2f;

        /// <summary>Synced for elites, champions and bosses (the HUD's rage meter).</summary>
        public readonly NetworkVariable<ThreatView> Rage = new NetworkVariable<ThreatView>();

        /// <summary>
        /// How much rage a hit raises, by class: the Knight's whole job is to be the thing it hates
        /// most; the Rogue works unnoticed until it strikes. (Healing raises rage too, see ServerHealThreat.)
        /// </summary>
        public static float ClassThreat(Classes.ClassId c) => c == Classes.ClassId.Knight ? 2.6f : c == Classes.ClassId.Rogue ? 0.7f
            : c == Classes.ClassId.Ranger ? 0.85f : c == Classes.ClassId.Cleric ? 0.9f
            : c == Classes.ClassId.Flagellant ? 1.6f : c == Classes.ClassId.PlagueDoctor ? 0.9f : 1f;

        /// <summary>How well this one leads a moving target: by depth, sharper for elites and bosses.</summary>
        protected float LeadSkill => Mathf.Clamp01(Difficulty.Lead(depthForLoot) + (Elite.Value ? 0.25f : 0f) + (IsBoss ? 0.15f : 0f));

        /// <summary>Server: the direction to fire a projectile of <paramref name="speed"/> at the target, leading it.</summary>
        protected Vector3 AimDir(Vector3 origin, float speed) => target != null ? Aim.Lead(target, origin, speed, LeadSkill) - origin : transform.forward;

        public void ServerAddThreat(ulong player, float amount)
        {
            if (!IsServer || amount <= 0f) return;
            threat.TryGetValue(player, out var t);
            threat[player] = t + amount;
        }

        public float ThreatOf(ulong player) => threat.TryGetValue(player, out var t) ? t : 0f;

        /// <summary>Server (tests): move it, and its post, to a spot.</summary>
        public void ServerDebugPlace(Vector3 at)
        {
            if (NavMesh.SamplePosition(at, out var nh, 3f, NavMesh.AllAreas)) at = nh.position;
            if (agent != null && agent.enabled) agent.Warp(at); else transform.position = at;
            home = at;
        }

        /// <summary>Server: who it is after right now (tests).</summary>
        public Health CurrentTarget => target;

        private float TopThreat()
        {
            float top = 0f;
            foreach (var kv in threat) if (kv.Value > top) top = kv.Value;
            return top;
        }

        /// <summary>Server: someone mended an ally in this fight. Everything fighting near the healer resents it a little.</summary>
        public static void ServerHealThreat(Health healer, float healed)
        {
            if (healer == null || healed <= 0f) return;
            int n = 0;
            foreach (var h in Health.All)
                if (h != null && h.Team == Team.Enemies && h.IsAlive && h.TryGetComponent<EnemyBrain>(out var b) && b.target != null
                    && (h.transform.position - healer.transform.position).sqrMagnitude < 25f * 25f) n++;
            if (n == 0) return;
            float share = healed * 0.5f / n;
            foreach (var h in Health.All)
                if (h != null && h.Team == Team.Enemies && h.IsAlive && h.TryGetComponent<EnemyBrain>(out var b) && b.target != null
                    && (h.transform.position - healer.transform.position).sqrMagnitude < 25f * 25f) b.ServerAddThreat(healer.NetworkObjectId, share);
        }

        /// <summary>Server, each frame it thinks: rage cools slowly; standing in its face keeps it warm; the meter is synced.</summary>
        private void ServerTickThreat()
        {
            float dt = Mathf.Min(0.5f, Time.time - lastThreatTick);
            lastThreatTick = Time.time;
            if (dt <= 0f) return;
            threatKeys.Clear();
            threatKeys.AddRange(threat.Keys);
            float decay = Mathf.Exp(-0.035f * dt);
            foreach (var k in threatKeys)
            {
                float t = threat[k] * decay;
                if (t < 0.5f || !NetworkManager.SpawnManager.SpawnedObjects.ContainsKey(k)) threat.Remove(k);
                else threat[k] = t;
            }
            // Presence: whoever stands at arm's length is the obvious thing to hit.
            foreach (var p in PlayerLife.All)
                if (p != null && PlayerLife.CanTarget(p.Health) && FlatDistance(p.transform.position) < attackRange + 1.8f)
                    ServerAddThreat(p.NetworkObjectId, 3f * dt);

            bool show = IsBoss || Elite.Value || IsChampion;
            if (!show || Time.time < nextThreatSync) return;
            nextThreatSync = Time.time + 0.25f;
            var view = new ThreatView { Target = target != null ? target.NetworkObjectId : 0 };
            int i = 0;
            foreach (var kv in threat)
            {
                if (i >= 4) break;
                view.Set(i++, kv.Key, kv.Value);
            }
            if (!Rage.Value.Equals(view)) Rage.Value = view;
        }

        /// <summary>Wakes up every idle enemy within earshot onto the same target.</summary>
        private void AlertPack(Health found)
        {
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || h == health) continue;
                if (Vector3.Distance(h.transform.position, transform.position) > 12f) continue;
                if (h.TryGetComponent<EnemyBrain>(out var other) && other.target == null) other.target = found;
            }
        }

        private float nextIdleStroll;

        /// <summary>Idle undead shuffle about their post instead of standing like furniture (not archers on watch, statues or bosses).</summary>
        private bool IdleWanders => !IsBoss && visualKind != CharacterArt.SkeletonKind.Archer && visualKind != CharacterArt.SkeletonKind.Gargoyle &&
            visualKind != CharacterArt.SkeletonKind.Mourner && visualKind != CharacterArt.SkeletonKind.Mimic;

        // ---- Patrols (2026-10-20): a band of the dead that walks between rooms ----

        private Vector3[] patrol;
        private int patrolIndex, patrolStep = 1;
        private EnemyBrain patrolLeader;
        private Vector3 patrolOffset;

        /// <summary>Server: on a patrol (its post walks the route; a cleared room is not always safe).</summary>
        public bool OnPatrol => patrol != null;
        /// <summary>Every peer: this one marches with a patrol (for the tip and the minimap).</summary>
        public readonly NetworkVariable<bool> Patrolling = new NetworkVariable<bool>();
        public bool PatrolSynced => Patrolling.Value;
        /// <summary>Server, for tests: where the patrol is heading.</summary>
        public Vector3 PatrolGoal => home;

        /// <summary>
        /// Server: walk <paramref name="route"/> back and forth, or (with a <paramref name="leader"/>) keep
        /// <paramref name="offset"/> from the leader's post; if the leader dies, carry on alone.
        /// </summary>
        public void ServerSetPatrol(Vector3[] route, EnemyBrain leader, Vector3 offset)
        {
            if (route == null || route.Length < 2) return;
            patrol = route;
            patrolLeader = leader;
            patrolOffset = offset;
            patrolIndex = 0;
            patrolStep = 1;
            home = route[0] + offset;
            Patrolling.Value = true;
        }

        private void TickPatrol()
        {
            if (patrolLeader != null && (patrolLeader.IsDead || !patrolLeader.IsSpawned)) patrolLeader = null;
            if (patrolLeader != null)
            {
                home = patrolLeader.home + patrolOffset;
                return;
            }
            if (FlatDistance(home) > 2.5f) return;
            if (patrolIndex + patrolStep < 0 || patrolIndex + patrolStep >= patrol.Length) patrolStep = -patrolStep;
            patrolIndex += patrolStep;
            home = patrol[patrolIndex] + patrolOffset;
        }

        private void ReturnHome()
        {
            agent.isStopped = false;
            if (patrol != null)
            {
                // On the march: an unhurried walk to the next room, the band keeping together.
                TickPatrol();
                agent.speed *= 0.5f;
                if (Time.time >= repathAt)
                {
                    repathAt = Time.time + 0.6f;
                    agent.SetDestination(home);
                }
                return;
            }
            if (FlatDistance(home) > 4f || !IdleWanders)
            {
                if (FlatDistance(home) > 1f && Time.time >= repathAt)
                {
                    repathAt = Time.time + 1f;
                    agent.SetDestination(home);
                }
                return;
            }
            // At its post: a slow, aimless shuffle now and then.
            agent.speed *= 0.3f;
            if (Time.time >= nextIdleStroll)
            {
                nextIdleStroll = Time.time + Random.Range(4f, 10f);
                var p = home + new Vector3(Random.Range(-2.5f, 2.5f), 0f, Random.Range(-2.5f, 2.5f));
                if (NavMesh.SamplePosition(p, out var hit, 1.2f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
            }
        }

        protected void FaceTarget(float sharpness)
        {
            if (target == null) return;
            var to = target.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return;
            // During a wind-up the enemy only tracks slowly — strafing around a big swing
            // is a legitimate dodge.
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime / sharpness);
        }

        private void ApplyKnockback()
        {
            if (knockback.sqrMagnitude < 0.01f) return;
            agent.Move(knockback * Time.deltaTime);
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, Time.deltaTime * 30f);
        }

        protected float FlatDistance(Vector3 p)
        {
            var d = p - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        // ------------------------------------------------------------------
        // Crowd control
        // ------------------------------------------------------------------

        /// <summary>Server: abandon the attack being wound up (no stagger).</summary>
        protected void CancelWindup()
        {
            attacking = false;
            busyUntil = 0f;
        }

        public virtual void ServerStun(float seconds)
        {
            if (IsDead) return;
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
            attacking = false;
            lunging = false;
            lungeStartAt = -1f;
            flurryLeft = 0;
            busyUntil = 0f;
            PlayActionRpc(RigAction.Stagger, Mathf.Min(seconds, 0.6f));
        }

        private Vector3 lureAt;
        private float lureUntil;
        public bool Lured => Time.time < lureUntil;

        /// <summary>
        /// Server: a bone rattle (2026-11-07, crafted tool): it forgets everyone and walks to the noise for a while.
        /// Bosses and statues pay no mind.
        /// </summary>
        public void ServerLure(Vector3 at, float seconds)
        {
            if (!IsServer || IsDead || IsBoss || visualKind == CharacterArt.SkeletonKind.Mourner || visualKind == CharacterArt.SkeletonKind.Mimic) return;
            lureAt = at;
            lureUntil = Time.time + seconds;
            threat.Clear();
            target = null;
            taunter = null;
            attacking = false;
        }

        /// <summary>Server: a player shouted nearby (proximity voice). Idle enemies turn on them.</summary>
        public virtual void ServerHear(Health who)
        {
            if (IsDead || who == null || !PlayerLife.CanTarget(who)) return;
            if (target == null) target = who;
        }

        /// <summary>Server: wake up hunting the nearest targetable player (ambush spawns).</summary>
        public void ServerAggroNearest()
        {
            float best = float.MaxValue;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                float d = FlatDistance(p.transform.position);
                if (d < best) { best = d; target = p.Health; }
            }
        }

        public bool IsStunned => Time.time < stunnedUntil;

        /// <summary>
        /// Server, at the top of a subclass's Update: stunned, it does nothing of its own (no casts, no
        /// shots, no specials) and the base update holds it still. Returns true when it handled the frame.
        /// Fixed 2026-10-20: subclasses used to keep casting and shooting through a stun.
        /// </summary>
        /// <summary>Server: held still until then (a boss's entrance), like a stun but with no EXPOSED.</summary>
        protected float heldUntil;
        public bool InEntrance => Time.time < heldUntil;

        protected bool HeldByStun()
        {
            if (!IsServer || IsDead || (!IsStunned && !InEntrance)) return false;
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            ServerTickThreat();
            ServerSyncStatus();
            return true;
        }

        /// <summary>Per-type walk-speed scale (archers creep while repositioning).</summary>
        protected virtual float SpeedScale => 1f;

        /// <summary>Synced crowd-control state, for the status tags over enemies: bit 0 stunned, bit 1 taunted.</summary>
        public readonly NetworkVariable<byte> Status = new NetworkVariable<byte>();

        private void ServerSyncStatus()
        {
            // Bits: 1 stunned, 2 taunted, 4 winding up an attack (the HUD shows a "!" tell).
            bool lungeTell = lungeStartAt > 0f || lunging;
            byte s = (byte)((IsStunned ? 1 : 0) | (Taunter != null ? 2 : 0) | (attacking || lungeTell ? 4 : 0)
                | (attacking && currentMove == Move.Heavy ? 8 : 0) | (attacking && currentMove == Move.Sweep ? 16 : 0) | (lungeTell ? 32 : 0));
            if (Status.Value != s) Status.Value = s;
        }

        /// <summary>Who has this enemy taunted right now, or null.</summary>
        public Health Taunter => taunter != null && Time.time < tauntUntil ? taunter : null;

        /// <summary>Server: lose track of someone (they vanished into smoke). True if it was hunting them.</summary>
        public bool ServerForget(Health h)
        {
            bool was = target == h || taunter == h;
            if (h != null) threat.Remove(h.NetworkObjectId);
            if (target == h) target = null;
            if (taunter == h) taunter = null;
            return was;
        }

        public void ServerTaunt(Health taunterHealth, float seconds)
        {
            taunter = taunterHealth;
            tauntUntil = Time.time + seconds;
            target = taunterHealth;
            // A taunt does not just turn its head: it seizes the top of its rage, with room to spare.
            if (taunterHealth != null) threat[taunterHealth.NetworkObjectId] = TopThreat() * 1.3f + 40f;
        }

        public virtual void ServerKnockback(Vector3 velocity)
        {
            if (!Heavy) knockback += velocity;
        }

        /// <summary>Server: drag this enemy to a spot (the Flagellant's chain). False if it is too heavy to move.</summary>
        public bool ServerYank(Vector3 to)
        {
            if (Heavy || IsBoss || IsDead || agent == null) return false;
            if (!NavMesh.SamplePosition(to, out var hit, 2.5f, NavMesh.AllAreas)) return false;
            agent.Warp(hit.position);
            return true;
        }

        private void OnDamagedServer(float amount, ulong attackerId)
        {
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var who) && who.TryGetComponent<Progression.PlayerProgress>(out var hitter))
                ServerAddThreat(attackerId, amount * ClassThreat(hitter.ClassId.Value));
            // Poise: a big enough single hit breaks a wind-up.
            if (!IsBoss && WindingUp && amount >= health.Max.Value * Difficulty.PoiseBreakFraction)
            {
                InterruptWindup(0.6f);
                PoiseBreakRpc();
            }

            // Getting hit from out of view still pulls aggro onto the attacker.
            if (target == null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj) &&
                obj.TryGetComponent<Health>(out var attacker) && attacker.Team == Team.Players && PlayerLife.CanTarget(attacker))
            {
                target = attacker;
                AlertPack(attacker);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChampionFallenRpc(string title) => UI.GameHud.ShowTitleCard(title.ToUpperInvariant() + " FALLS", "A champion's hoard spills across the floor");

        [Rpc(SendTo.ClientsAndHost)]
        private void ChampionSeenRpc(string title)
        {
            string how = ChampionTraits.Describe(title);
            UI.GameHud.Feed("A champion stirs: " + title + (how.Length > 0 ? " - " + how : ""), new Color(1f, 0.75f, 0.3f));
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 1.5f, 1f, 0.7f);
        }

        protected virtual void OnDied(Health _)
        {
            // Once only: the autoplay run saw a champion's death (card, hoard) twice in the same moment.
            if (IsDead) return;
            if (AffixKind == EliteAffix.Volatile || ChampionTrait == ChampionTraits.Marrowgrin) StartCoroutine(VolatileBlast());
            if (tithe > 0) World.DungeonDirector.Instance?.ServerDropLoot(transform.position + Vector3.up * 0.5f, LootKind.Gold, Mathf.RoundToInt(tithe * 1.5f), transform.position, fixedSum: true);
            if (AffixKind == EliteAffix.Splitting) World.DungeonDirector.Instance?.ServerSpawnSplit(transform.position, scaledPlayersBase, depthForLoot);
            // Gravebound: its bones gather to rise again, unless someone stamps them out.
            if (AffixKind == EliteAffix.Gravebound) World.DungeonDirector.Instance?.ServerGravebound(this);
            IsDead = true;
            attacking = false;
            agent.isStopped = true;
            agent.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            PlayActionRpc(RigAction.Death, 1.1f);
            if (IsChampion && !NoLoot)
            {
                // The champion's hoard: gear for everyone and a Sigil.
                GearDrop.ServerSpawnForParty(transform.position, 1f, 0);
                // And a rare draught or tome for the party (2026-10-24).
                World.DungeonDirector.Instance?.ServerDropLoot(transform.position + Vector3.up * 0.5f + Vector3.back * 0.8f, LootKind.RareItem, (int)Progression.RareItems.Random(), transform.position);
                // A Sigil for each player (it was one for the whole party), three champions in four (2026-11-07 economy).
                for (int i = 0; i < Difficulty.Party; i++)
                    if (Random.value < ChampionSigilChance) World.DungeonDirector.Instance?.ServerDropLoot(transform.position + Vector3.up * 0.5f + Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 0.8f, LootKind.Sigil, 1, transform.position);
                ChampionFallenRpc(Title.Value.ToString());
                World.DungeonDirector.Instance?.ServerSealFound(depthForLoot);
            }
            if (!NoLoot && lootPrefab != null)
            {
                LootDrop.ServerSpawnFromTable(lootPrefab, Elite.Value ? LootTable.Elite : Loot, transform.position, (1f + 0.3f * World.DungeonDirector.Tier(depthForLoot)) /* gold: gentler with depth (2026-10-21, 2,200 gold by floor 4); the setting's gold is in Difficulty.GoldIncome now */ * World.DungeonDirector.OmenGold * World.DungeonDirector.EventGold * (Elite.Value ? World.DungeonDirector.EventEliteLoot : 1f),
                    this is BossBrain || IsChampion || this is HoarderBrain || this is MimicBrain ? Difficulty.FixedLootShare(Difficulty.Party) : Difficulty.KillLootShare(Difficulty.Party));
                // Gear, rolled for each player: always from a boss, sometimes from an elite.
                if (this is BossBrain)
                {
                    GearDrop.ServerSpawnForParty(transform.position, 1f, 1, (BossBrain)this);
                    // Guardians no longer drop a Sigil each (2026-11-07 economy): with the champions' that was 20 a run
                    // against the 14 that rank both abilities to 5. Champions, chests, elites and the dungeon's odd corners carry them.
                }
                else if (Elite.Value) GearDrop.ServerSpawnForParty(transform.position, 0.18f, 0);
            }
            Invoke(nameof(Despawn), 4f);
        }

        /// <summary>Volatile: the corpse swells and bursts. Get clear of the ring.</summary>
        private System.Collections.IEnumerator VolatileBlast()
        {
            var at = transform.position;
            VolatileWarnRpc(at);
            yield return new WaitForSeconds(1.3f);
            foreach (var victim in CombatQuery.Circle(at, 3.2f, Team.Players))
                victim.ServerDamage(damage * 1.6f, at, NetworkObjectId);
            VolatileBoomRpc(at);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void VolatileWarnRpc(Vector3 at) => Telegraph.Show(at, 3.2f, 1.3f, null, new Color(1f, 0.45f, 0.1f, 0.4f));

        [Rpc(SendTo.ClientsAndHost)]
        private void VolatileBoomRpc(Vector3 at)
        {
            Sfx.Play(Sound.Fire, at + Vector3.up, 1f, 0.7f);
            BoneBurst.Spawn(at + Vector3.up * 0.5f, 1.2f, false, Mat.Flame);
            var fire = new Color(1f, 0.5f, 0.15f);
            Vfx.Fireball(at + Vector3.up * 0.6f, 3f, fire);
            Vfx.Ring(at, 3.2f, fire, 0.4f);
            Vfx.Flash(at + Vector3.up, fire, 10f, 10f, 0.6f);
            FxSprite.Scorch(at, 2.4f);
            var local = Player.NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 12f) local.AddShake(0.35f);
        }

        private void Despawn()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn();
        }

        // ------------------------------------------------------------------
        // Client presentation
        // ------------------------------------------------------------------

        /// <summary>
        /// The creatures with throats (2026-10-24, recorded growls): the pitch of their voice, 0 for the bony dead,
        /// who rattle instead.
        /// </summary>
        protected float VoicePitch
        {
            get
            {
                switch (visualKind)
                {
                    case CharacterArt.SkeletonKind.Ghoul: return 1.15f;
                    case CharacterArt.SkeletonKind.Brute: return 0.7f;
                    case CharacterArt.SkeletonKind.Gargoyle: return 0.8f;
                    case CharacterArt.SkeletonKind.Mimic: return 0.95f;
                    case CharacterArt.SkeletonKind.Plaguebearer: return 0.85f;
                    case CharacterArt.SkeletonKind.FrostWight: return 0.75f;
                    case CharacterArt.SkeletonKind.Entling: return 1.35f;
                    case CharacterArt.SkeletonKind.Hoarder: return 1.25f;
                    default: return 0f;
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        protected void PlayActionRpc(RigAction action, float duration)
        {
            Rig?.Play(action, duration);
            float voice = VoicePitch;
            switch (action)
            {
                case RigAction.Swing: Sfx.Play(Sound.Swing, transform.position + Vector3.up, 0.7f, 0.8f); break;
                case RigAction.Windup:
                    if (voice > 0f && (Random.value < 0.35f || Elite.Value)) Sfx.Play(Sound.MonsterAttack, transform.position + Vector3.up * 1.4f, 0.7f, voice);
                    // The voiceless (bones) wound up in silence: the dodge window could not be heard from behind.
                    else if (voice <= 0f) Sfx.Play(Sound.Rattle, transform.position + Vector3.up * 1.2f, 0.4f, 1.3f);
                    break;
                case RigAction.Death:
                    Sfx.Play(Sound.BoneBreak, transform.position + Vector3.up);
                    if (voice > 0f) Sfx.Play(Sound.MonsterDie, transform.position + Vector3.up * 1.2f, 0.9f, voice);
                    BoneBurst.Spawn(transform.position, Rig != null ? Rig.transform.lossyScale.y : 1f, Elite.Value);
                    foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
                    // The rank and file fall apart once they have crumpled; bosses keep their fall.
                    if (!(this is BossBrain)) StartCoroutine(FallApart(duration * 0.5f));
                    break;
            }
        }

        private System.Collections.IEnumerator FallApart(float after)
        {
            yield return new WaitForSeconds(after);
            if (Rig != null)
            {
                SoulWisp.Release(Rig.Head != null ? Rig.Head.position : transform.position + Vector3.up, Rig.GlowColor);
                BoneScatter.Break(Rig);
            }
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up * 0.5f, 0.8f, 0.9f);
        }

        private bool engaged;

        [Rpc(SendTo.ClientsAndHost)]
        private void NoticeRpc()
        {
            float top = Rig != null ? Rig.transform.lossyScale.y * 2.1f : 2.1f;
            UI.GameHud.ShowCombo(transform.position + Vector3.up * top, "!", new Color(1f, 0.85f, 0.3f));
            if (VoicePitch > 0f) Sfx.Play(Sound.Growl, transform.position + Vector3.up * 1.4f, 0.8f, VoicePitch);
            else Sfx.Play(Sound.Rattle, transform.position + Vector3.up * 1.4f, 0.5f, Random.Range(1.15f, 1.4f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BackstabRpc() => Sfx.Play(Sound.Hit, transform.position + Vector3.up * 1.2f, 0.8f, 0.75f);

        [Rpc(SendTo.ClientsAndHost)]
        private void PoiseBreakRpc()
        {
            Sfx.Play(Sound.Bash, transform.position + Vector3.up * 1.2f, 0.9f, 1.2f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.2f, "BROKEN", new Color(1f, 0.85f, 0.4f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RattleRpc() => Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.5f);

        private HitFlash flash;

        private float nextImpactFx;

        private void OnHurtVisual(float amount)
        {
            if (flash == null) flash = gameObject.AddComponent<HitFlash>();
            flash.Flash(Mathf.Clamp01(0.5f + amount / Mathf.Max(1f, health.Max.Value) * 4f));
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.8f, 1.2f);
            if (Rig != null)
            {
                // Every hit jolts the body away from where it came; a heavy one still staggers.
                // The server knows the attacker's spot; clients take the nearest player as the source.
                var from = IsServer ? health.LastHitAt : transform.position - transform.forward;
                if (!IsServer)
                {
                    float best = float.MaxValue;
                    foreach (var pl in PlayerLife.All)
                    {
                        if (pl == null) continue;
                        float dd = (pl.transform.position - transform.position).sqrMagnitude;
                        if (dd < best) { best = dd; from = pl.transform.position; }
                    }
                }
                float hitStrength = amount / Mathf.Max(1f, health.Max.Value) * 6f;
                Rig.Flinch(from, hitStrength);
                if (Time.time >= nextImpactFx)
                {
                    nextImpactFx = Time.time + 0.07f;
                    var at = health.Center + Vector3.up * 0.2f;
                    var toward = from - at;
                    toward.y = 0f;
                    at += toward.normalized * Mathf.Min(health.BodyRadius, 0.8f);
                    bool stone = visualKind == CharacterArt.SkeletonKind.Gargoyle || visualKind == CharacterArt.SkeletonKind.Mourner;
                    Vfx.HitImpact(at, from, hitStrength, Elite.Value ? new Color(1f, 0.8f, 0.35f) : new Color(1f, 0.85f, 0.6f), stone ? Mat.Rubble : Mat.Bone);
                }
                if (Rig.Current == RigAction.None && amount > health.Max.Value * 0.08f) Rig.Play(RigAction.Stagger, 0.2f);
            }
        }
    }
}
