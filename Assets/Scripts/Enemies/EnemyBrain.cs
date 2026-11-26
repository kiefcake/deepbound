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
    [RequireComponent(typeof(Health), typeof(NavMeshAgent))]
    public partial class EnemyBrain : NetworkBehaviour, ICrowdControl
    {
        [Header("Stats (scaled by depth and party size at spawn)")]
        [SerializeField] protected float maxHealth = 75f;

        public float BaseHealth => maxHealth;
        public float BaseDamage => damage;

        public void ServerScaleDamage(float k) { if (IsServer) damage *= k; }

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
        protected float strikeAt;
        private Vector3 home;
        private Vector3 knockback;
        protected float repathAt;
        private float nextRattle;

        public bool IsDead { get; private set; }

        public readonly NetworkVariable<bool> Elite = new NetworkVariable<bool>();

        public readonly NetworkVariable<Unity.Collections.FixedString64Bytes> Title = new NetworkVariable<Unity.Collections.FixedString64Bytes>();
        public bool IsChampion => Title.Value.Length > 0;

        [System.NonSerialized] public float ChoirReadyAt;
        private bool championAnnounced;

        private static readonly string[] ChampionNames = { "Grimbold", "Morwen", "Garrick", "Hesk", "Vorn", "Ilsa",  "Bram", "Cael", "Odo", "Sabine", "Tormund", "Wenna" };

        public static readonly string[] Epithets = { "the Unburied", "Gravefist", "the Hollow", "Bonegnawer", "the Patient", "Ashenhand", "the Tithe-Taker", "Oathbreaker", "the Pale", "Marrowgrin" };

        public void ServerMakeChampion()
        {
            if (IsBoss) return;
            if (!Elite.Value) ServerMakeElite();

            damage *= depthForLoot == 0 ? 0.9f : 1.15f;
            health.ServerSetMax(health.Max.Value * 1.6f, refill: true);
            health.Defense += 2f;
            SetChampionTitle(Pick(ChampionNames, usedNames), Pick(Epithets, usedEpithets));
        }

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
                    if (Time.time < risingUntil && dmg < BossBrain.KillDamage) return 0f;
                    if (championRose || IsDead || dmg < health.Current.Value || dmg >= BossBrain.KillDamage) return dmg;
                    championRose = true;
                    StartCoroutine(ChampionRise());
                    return Mathf.Max(0f, health.Current.Value - 1f);
                });
        }

        public void ServerDebugChampion(int trait)
        {
            if (IsBoss) return;
            if (!Elite.Value) ServerMakeElite();
            SetChampionTitle("Test", Epithets[Mathf.Clamp(trait, 0, Epithets.Length - 1)]);
        }

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

        public void ServerTithe(int gold, Vector3 at)
        {
            tithe += gold;
            TitheRpc(gold, at);
        }

        public int Tithe => tithe;

        [Rpc(SendTo.ClientsAndHost)]
        private void TitheRpc(int gold, Vector3 at)
        {
            UI.GameHud.ShowCombo(at + Vector3.up * 0.6f, "-" + gold + " GOLD", new Color(1f, 0.82f, 0.3f));
            Sfx.Play(Sound.Coins, at, 0.7f, 0.8f);
        }

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

        public void ServerFallTo(Vector3 ground, float damageFraction, ulong by)
        {
            if (IsDead || !agent.enabled) return;
            agent.Warp(ground);
            knockback = Vector3.zero;
            ServerStun(0.8f);
            health.ServerDamage(health.Max.Value * Mathf.Clamp(damageFraction, 0.05f, 0.4f), ground + Vector3.up * 3f, by);
        }

        public const float LeapMinDrop = 1.6f, LeapMaxDrop = 11f, LeapReach = 11f, LeapCrouch = 0.45f, LeapFlight = 0.6f, LeapHitRadius = 1.8f, LeapDamage = 0.9f;
        private float nextLeapCheck;
        public int LeapsTaken { get; private set; }

        protected bool CanLeapDown => !(this is BossBrain) && !(this is GargoyleBrain) && !(this is MournerBrain) && !(this is WraithBrain)
            && !(this is MimicBrain) && !(this is AbyssEyeBrain) && !Patrolling.Value;

        private bool TryLeapDown(float dist)
        {
            if (target == null || Time.time < nextLeapCheck || !CanLeapDown) return false;
            nextLeapCheck = Time.time + 0.5f;
            var at = target.transform.position;
            float drop = transform.position.y - at.y;
            if (drop < LeapMinDrop || drop > LeapMaxDrop || dist > LeapReach) return false;

            if (agent.pathPending || (agent.hasPath && agent.remainingDistance < dist * 1.6f + 3f)) return false;
            var back = transform.position - at; back.y = 0f;
            var want = at + (back.sqrMagnitude > 0.01f ? back.normalized * 1.4f : Vector3.zero);
            if (!NavMesh.SamplePosition(want, out var hit, 2f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - at.y) > 1.2f) return false;
            StartCoroutine(LeapDown(hit.position));
            return true;
        }

        private System.Collections.IEnumerator LeapDown(Vector3 land)
        {
            LeapsTaken++;
            busyUntil = Time.time + LeapCrouch + LeapFlight + 0.35f;
            agent.isStopped = true;
            var look = land - transform.position; look.y = 0f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
            PlayActionRpc(RigAction.Cast, LeapCrouch);
            NoticeRpc();
            LeapTellRpc(land, LeapCrouch + LeapFlight);
            yield return new WaitForSeconds(LeapCrouch);
            if (IsDead || !agent.enabled) yield break;
            agent.updatePosition = false;
            var from = transform.position;
            for (float t = 0f; t < LeapFlight; t += Time.deltaTime)
            {
                float k = t / LeapFlight;
                var p = Vector3.Lerp(from, land, k);
                p.y += Mathf.Sin(k * Mathf.PI) * 1.1f;
                transform.position = p;
                yield return null;
            }
            transform.position = land;
            if (agent.enabled) { agent.Warp(land); agent.updatePosition = true; }
            if (IsDead) yield break;
            foreach (var pl in PlayerLife.All)
            {
                if (pl == null || pl.Downed.Value) continue;
                var d = pl.transform.position - land;
                if (Mathf.Abs(d.y) > 1.6f || new Vector2(d.x, d.z).magnitude > LeapHitRadius) continue;
                if (pl.TryGetComponent<Health>(out var victim)) victim.Tag("leap").ServerDamage(damage * LeapDamage, transform.position, NetworkObjectId);
            }
            LeapLandRpc(land);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LeapTellRpc(Vector3 at, float seconds)
        {
            Telegraph.Show(at, LeapHitRadius, seconds, null, new Color(1f, 0.35f, 0.2f, 0.4f));

            Sfx.Play(Sound.Rattle, transform.position + Vector3.up * 1.2f, 0.7f, 0.75f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LeapLandRpc(Vector3 at)
        {
            Vfx.Smoke(at + Vector3.up * 0.2f, 3, 0.5f, 1.4f);
            Sfx.Play(Sound.Step, at, 0.9f, 0.6f);
        }

        public void ServerDebugLeap(Vector3 land) { if (IsServer && !IsDead) StartCoroutine(LeapDown(land)); }

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

        public readonly NetworkVariable<byte> Affix = new NetworkVariable<byte>();
        public EliteAffix AffixKind => (EliteAffix)Affix.Value;
        private bool wardUp;

        protected virtual bool IsBoss => false;

        protected virtual bool Heavy => false;

        protected int scaledPlayersBase = 1;

        public float PartyHealthScale => Difficulty.PartyHealth(scaledPlayersBase, IsBoss, depthForLoot) / Difficulty.PartyHealth(1, IsBoss, depthForLoot);
        private float surroundAngle;

        protected virtual void Awake()
        {
            health = GetComponent<Health>();
            agent = GetComponent<NavMeshAgent>();
            agent.enabled = false;
        }

        protected virtual float VisualScale => 1f;

        protected virtual void DressRig(HumanoidRig rig) { }

        private static bool RisesFromGround(CharacterArt.SkeletonKind k) =>
            k != CharacterArt.SkeletonKind.Wraith && k != CharacterArt.SkeletonKind.Mimic && k != CharacterArt.SkeletonKind.Gargoyle && k != CharacterArt.SkeletonKind.Mourner && k != CharacterArt.SkeletonKind.Unsung;

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

                if (World.DungeonDirector.InPit && !IsBoss)
                {
                    foreach (var r in Rig.GetComponentsInChildren<Renderer>())
                        if (r.sharedMaterial != null && !r.sharedMaterial.name.Contains("Glow")) ArtLibrary.Tint(r, new Color(0.3f, 0.2f, 0.17f));
                    Rig.GlowColor = new Color(1f, 0.45f, 0.12f);
                }
            }

            MeshKit.MergeRig(Rig);
            if (Rig != null) Rig.WatchViewer = true;

            ApplySize();
            Title.OnValueChanged += (_, t) => { if (t.Length > 0) ScaleAsChampion(); };
            if (Title.Value.Length > 0) ScaleAsChampion();
            StartEntrance();
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

            health.IncomingModifiers.Add(BackstabModifier);
            health.IncomingModifiers.Add(UnawareModifier);
            health.IncomingModifiers.Add((d, from) => Enraged ? d * EnrageTaken : d);

            surroundAngle = (NetworkObjectId * 137.508f) % 360f;

            agent.enabled = true;
            agent.speed = moveSpeed;
            agent.angularSpeed = 540f;
            agent.acceleration = 30f;
            agent.stoppingDistance = attackRange * 0.75f;
            agent.autoBraking = true;
            if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }

        public void ServerScale(int players, int depth)
        {
            scaledPlayersBase = players;
            if (!sizeStatsApplied) { sizeStatsApplied = true; ServerApplySizeStats(); }
            float hpScale = (IsBoss ? Difficulty.PartyHealth(players, true, depth) : Difficulty.EnemyHealth(players) * Difficulty.RankHealth) * Difficulty.DepthHealth(depth) * Difficulty.Level.Health;
            damage *= Difficulty.Damage(players) * Difficulty.DepthDamage(depth) * Difficulty.Level.Damage * (IsBoss ? 1f : Difficulty.RankDamage);
            depthForLoot = depth;
            health.ServerSetMax(maxHealth * hpScale, refill: true);
        }

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

        public const float FrenzyPace = 0.6f;

        public const float MinWindup = 0.3f;
        private bool frenzyAnnounced;
        public bool Frenzied => AffixKind == EliteAffix.Frenzied && health != null && health.Fraction < 0.5f;

        public readonly NetworkVariable<byte> FxFlags = new NetworkVariable<byte>();
        public bool WardedNow => (FxFlags.Value & 1) != 0;
        public bool Enraged => (FxFlags.Value & 2) != 0;
        private float enragedUntil;

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
            global::Deepbound.Art.Vfx.Ring(transform.position + Vector3.up * 0.05f, 2f, new Color(1f, 0.25f, 0.15f), 0.5f); global::Deepbound.Art.Vfx.Embers(transform.position + Vector3.up * 1.6f, 12, new Color(1f, 0.25f, 0.15f), 4f, 0.06f, 0.6f);
            if (VoicePitch > 0f) Sfx.Play(Sound.Roar, transform.position + Vector3.up * 1.6f, 0.9f, VoicePitch);
            Vfx.Ring(transform.position + Vector3.up * 0.05f, 2f, new Color(1f, 0.25f, 0.15f), 0.5f);
        }
        protected float Pace
        {
            get
            {
                float lap = Elite.Value ? World.DungeonDirector.LapElitePace : 1f;
                if (!Frenzied) return (Enraged ? EnragePace : 1f) * World.DungeonDirector.OmenPace * lap * (ChampionTrait == ChampionTraits.Patient ? 1.3f : 1f);
                if (!frenzyAnnounced && IsServer) { frenzyAnnounced = true; FrenzyRpc(); }
                return FrenzyPace * World.DungeonDirector.OmenPace * lap;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FrenzyRpc()
        {
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 1.6f, 0.8f, 1.4f);
            Vfx.Ring(transform.position + Vector3.up * 0.05f, 1.6f, new Color(1f, 0.3f, 0.22f), 0.5f);
        }

        public void ServerMakeElite(EliteAffix forced = EliteAffix.None)
        {
            if (IsBoss) return;
            Elite.Value = true;
            damage *= 1.3f;
            moveSpeed *= 1.1f;
            health.ServerSetMax(health.Max.Value * 2.2f, refill: true);
            health.Defense += 3f;

            var affix = forced != EliteAffix.None ? forced : World.DungeonDirector.CurrentEvent == World.FloorEvent.EmberMarch ? EliteAffix.Emberbound : EliteAffixes.Roll();
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

        public const int MaxSwingers = 3;
        public const float TurnRing = 1.3f, LoseSightSeconds = 6f, LoseSightNear = 6f, BlindSeconds = 12f, SearchSeconds = 8f;

        private float swingUntil, circleDir, nextSightCheck, lastSeenTime, blindUntil;
        private Vector3 lastSeenAt;
        private Health lastSeenOf, blindTo;

        public bool Swinging => attacking || Time.time < swingUntil;

        public int TurnsWaited { get; private set; }
        public int LostTrack { get; private set; }

        protected virtual bool TakesTurns => !IsBoss && attackRange <= 3.5f;

        private bool Searcher => !IsBoss && agent != null && agent.enabled && visualKind != CharacterArt.SkeletonKind.Mourner
            && visualKind != CharacterArt.SkeletonKind.Mimic && visualKind != CharacterArt.SkeletonKind.Gargoyle;
        private bool LostTo(Health h) => h != null && h == blindTo && Time.time < blindUntil;

        private bool WaitForTurn()
        {
            if (!TakesTurns || target == null || Swinging) return false;
            int busy = 0;
            foreach (var e in All)
                if (e != null && e != this && !e.IsDead && e.target == target && e.TakesTurns && e.Swinging) busy++;
            int max = MaxSwingers + (Difficulty.Level == Difficulty.Settings[Difficulty.TormentIndex] ? 1 : 0);
            if (busy < max) return false;

            TurnsWaited++;
            if (circleDir == 0f) circleDir = NetworkObjectId % 2 == 0 ? 1f : -1f;
            surroundAngle += circleDir * 35f * Time.deltaTime;
            agent.isStopped = false;
            if (Time.time >= repathAt)
            {
                repathAt = Time.time + 0.3f;
                var t = target.transform.position;
                var slot = t + Quaternion.Euler(0f, surroundAngle, 0f) * Vector3.forward * (attackRange + TurnRing);
                agent.SetDestination(NavMesh.SamplePosition(slot, out var hit, 1.5f, NavMesh.AllAreas) ? hit.position : transform.position);
            }
            FaceTarget(0.25f);
            return true;
        }

        private void TickSight()
        {
            if (Time.time < nextSightCheck) return;
            nextSightCheck = Time.time + 0.3f + (NetworkObjectId % 4) * 0.03f;
            if (lastSeenOf != target) { lastSeenOf = target; lastSeenAt = target.transform.position; lastSeenTime = Time.time; }
            if (taunter != null && Time.time < tauntUntil) return;
            if (CombatQuery.ClearLine(transform.position + Vector3.up * 1.5f, target))
            {
                lastSeenAt = target.transform.position;
                lastSeenTime = Time.time;
                return;
            }
            if (Time.time - lastSeenTime < LoseSightSeconds || FlatDistance(target.transform.position) < LoseSightNear) return;
            blindTo = target;
            blindUntil = Time.time + BlindSeconds;
            LostTrack++;
            target = null;
        }

        private void SearchFor(Health lost)
        {
            if (lost == null || !lost.IsAlive || FlatDistance(home) > leashRange * 0.8f) return;
            if (lost.TryGetComponent<PlayerLife>(out var pl) && pl.Downed.Value) return;
            var at = lastSeenOf == lost ? lastSeenAt : lost.transform.position;
            ServerInvestigate(at, SearchSeconds);
        }

        public void ServerDebugLoseTrack()
        {
            if (target == null) return;
            lastSeenOf = target; lastSeenAt = target.transform.position;
            blindTo = target; blindUntil = Time.time + BlindSeconds; LostTrack++;
            var was = target; target = null;
            SearchFor(was);
        }

        public int Openers { get; private set; }

        private float UnawareModifier(float dealt, Vector3 attackerPosition)
        {
            if (IsBoss || target != null || Investigating || Entering || dealt >= BossBrain.KillDamage) return dealt;
            if (Time.time - health.LastHurtTime < Difficulty.UnawareCalm) return dealt;
            Openers++;
            UnawareRpc();
            return dealt * Difficulty.UnawareMultiplier;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void UnawareRpc()
        {
            float top = Rig != null ? Rig.transform.lossyScale.y * 2.1f : 2.1f;
            UI.GameHud.ShowCombo(transform.position + Vector3.up * top, "UNAWARE", new Color(0.95f, 0.85f, 0.55f));
            Sfx.Play(Sound.Hit, transform.position + Vector3.up * 1.2f, 0.9f, 0.6f);
        }

        private bool eliteGrown;

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
                ServerTickEmberbound();
                ServerSyncStatus();
                if (wardUp && (IsStunned || health.Chilled.Value)) ServerBreakWard();
            }
            if (!IsServer || IsDead || !agent.enabled || !agent.isOnNavMesh) return;

            ApplyKnockback();

            agent.speed = (health.Chilled.Value ? moveSpeed * 0.5f : moveSpeed) * SpeedScale * World.DungeonDirector.WaterSlow(transform.position) * (Enraged ? EnrageSpeed : 1f) * World.DungeonDirector.OmenSpeed * World.DungeonDirector.EventSpeed;

            byte fx = (byte)((wardUp ? 1 : 0) | (Enraged && Time.time < enragedUntil ? 2 : 0) | (Severed ? 4 : 0));
            if (FxFlags.Value != fx) FxFlags.Value = fx;

            TickHunter();
            if (Time.time < enteringUntil || Time.time < stunnedUntil)
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
                if (LocksSwing) transform.rotation = Quaternion.LookRotation(strikeDir);
                else FaceTarget(0.35f);
                if (Time.time >= strikeAt)
                {
                    attacking = false;
                    Strike();
                }
                return;
            }

            if (recoverPoseAt > 0f && Time.time >= recoverPoseAt)
            {
                recoverPoseAt = -1f;
                float left = busyUntil - Time.time;
                if (left > 0.25f && !IsStunned && !attacking) PlayActionRpc(RigAction.Recover, left);
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
            var hadTarget = target;
            target = PickTarget();
            if (target != null && Searcher) TickSight();

            if (target == null && hadTarget != null && Searcher) SearchFor(hadTarget);

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
            if (target == null && Time.time < investigateUntil)
            {
                agent.isStopped = false;
                if (Time.time >= repathAt) { repathAt = Time.time + 0.4f; agent.SetDestination(investigateAt); }
                if (FlatDistance(investigateAt) < 1.4f) investigateUntil = Mathf.Min(investigateUntil, Time.time + 1.2f);
                return;
            }
            if (target != null) investigateUntil = 0f;
            if (target == null)
            {
                ReturnHome();
                return;
            }

            float dist = FlatDistance(target.transform.position);
            if (TryBossDash(dist)) return;
            if (ChooseSpecialAttack(dist)) return;
            if (TryLunge(dist)) return;
            if (TryLeapDown(dist)) return;

            if (dist <= attackRange + TurnRing + 0.6f && WaitForTurn()) return;
            if (dist <= attackRange)
            {
                BeginAttack();
                swingUntil = busyUntil;
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

        protected virtual Vector3 ChaseDestination(float distanceToTarget)
        {
            var t = target.transform.position;
            if (distanceToTarget > 6f) return t;
            var slot = Quaternion.Euler(0f, surroundAngle, 0f) * Vector3.forward * (attackRange * 0.8f);
            var p = t + slot;
            return NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas) ? hit.position : t;
        }

        protected virtual bool ChooseSpecialAttack(float distanceToTarget) => false;

        protected bool WindingUp => attacking;

        protected virtual void InterruptWindup(float stagger)
        {
            attacking = false;
            busyUntil = Time.time + stagger;
            PlayActionRpc(RigAction.Stagger, stagger);
        }

        public enum Move : byte { Swing = 0, Heavy = 1, Flurry = 2, Sweep = 3, Lunge = 4 }
        protected Move currentMove;
        private int flurryLeft;
        private float lungeReadyAt;
        private bool lunging;
        private Vector3 lungeDir;
        private float lungeTravelled;
        private readonly System.Collections.Generic.HashSet<Health> lungeHit = new System.Collections.Generic.HashSet<Health>();
        public const float LungeMin = 3f, LungeMax = 7f, LungeSpeed = 13f, LungeCooldown = 7f;

        protected virtual bool UsesMoveSet => !IsBoss && attackRange <= 3f && damage > 0f;

        private Move PickMove()
        {
            if (!UsesMoveSet) return Move.Swing;
            float r = Random.value;
            return r < 0.5f ? Move.Swing : r < 0.7f ? Move.Heavy : r < 0.86f ? Move.Flurry : Move.Sweep;
        }

        public bool ServerDebugSwing()
        {
            if (target == null) target = PickTarget();
            if (target == null || IsStunned) return false;
            BeginAttack();
            return attacking;
        }

        public Vector3 StrikeDirection => strikeDir;
        public bool WindingSwing => attacking;

        public bool Recovering => IsServer ? (!attacking && !IsStunned && Time.time < busyUntil && Time.time - lastStrikeAt < 3f) : (Status.Value & 64) != 0;
        private float lastStrikeAt = -10f, recoverPoseAt = -1f;

        protected virtual bool LocksSwing => IsBoss;

        protected Vector3 strikeDir = Vector3.forward;

        protected float MoveArc => currentMove == Move.Heavy ? Mathf.Min(attackArc, 60f) : currentMove == Move.Sweep ? 220f : attackArc;
        protected float MoveReach => attackRange + (currentMove == Move.Sweep ? 0.8f : 0.5f);

        protected virtual void BeginAttack()
        {
            currentMove = PickMove();
            if (target != null)
            {
                var aim = target.transform.position - transform.position; aim.y = 0f;
                if (aim.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(aim);
            }
            strikeDir = transform.forward; strikeDir.y = 0f;
            if (strikeDir.sqrMagnitude < 0.001f) strikeDir = Vector3.forward;
            strikeDir.Normalize();

            float w = Mathf.Max(MinWindup, windup * Pace * (currentMove == Move.Heavy ? 1.7f : currentMove == Move.Sweep ? 1.25f : 1f));
            flurryLeft = currentMove == Move.Flurry ? 1 : 0;
            attacking = true;
            agent.isStopped = true;
            strikeAt = Time.time + w;
            busyUntil = strikeAt + recovery * Pace * (currentMove == Move.Heavy ? 1.3f : 1f);
            PlayActionRpc(currentMove == Move.Heavy ? RigAction.WindHeavy : currentMove == Move.Sweep ? RigAction.WindSweep : RigAction.Windup, w);
        }

        protected virtual void Strike()
        {
            PlayActionRpc(currentMove == Move.Heavy ? RigAction.Cleave : currentMove == Move.Sweep ? RigAction.SweepStrike : RigAction.Swing,
                currentMove == Move.Heavy ? 0.5f : currentMove == Move.Sweep ? 0.42f : 0.3f);
            float mult = currentMove == Move.Heavy ? 1.8f : currentMove == Move.Flurry ? 0.7f : currentMove == Move.Sweep ? 0.8f : 1f;
            float arc = MoveArc;
            float reach = MoveReach;
            var origin = transform.position + Vector3.up * 1.2f;
            var swingDir = LocksSwing ? strikeDir : transform.forward;
            foreach (var victim in CombatQuery.Arc(origin, swingDir, reach, arc, Team.Players))
            {
                victim.ServerDamage(damage * mult, transform.position, NetworkObjectId);
                if (currentMove == Move.Heavy && victim.TryGetComponent<KnockbackReceiver>(out var kb))
                    kb.KnockbackRpc((victim.transform.position - transform.position).normalized * 6f + Vector3.up * 2f);
            }
            if (currentMove == Move.Heavy) ImpactThudRpc(transform.position + transform.forward * 1.2f);
            lastStrikeAt = Time.time;
            recoverPoseAt = flurryLeft > 0 ? -1f : Time.time + (currentMove == Move.Heavy ? 0.5f : currentMove == Move.Sweep ? 0.42f : 0.3f);

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
            lungeSpeedNow = LungeSpeed; lungeMaxNow = LungeMax; lungeDamageNow = 1.25f;
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
        private float lungeSpeedNow = LungeSpeed, lungeMaxNow = LungeMax, lungeDamageNow = 1.25f;

        public const float BossDashMin = 9f, BossDashMax = 18f, BossDashSpeed = 24f, BossDashCooldown = 9f, BossDashTell = 0.75f, FarSeconds = 2f;
        protected float bossDashReadyAt;
        private float farSince = -1f;

        public int BossDashes { get; private set; }

        protected virtual bool CanDash => IsBoss && moveSpeed > 0.5f;

        private bool TryBossDash(float dist)
        {
            if (!CanDash || target == null) return false;
            if (dist < BossDashMin) { farSince = -1f; return false; }
            if (farSince < 0f) { farSince = Time.time; return false; }
            if (Time.time - farSince < FarSeconds || Time.time < bossDashReadyAt) return false;
            if (!CombatQuery.ClearLine(transform.position + Vector3.up * 1.2f, target)) return false;
            return ServerBossDash();
        }

        public virtual bool ServerBossDash()
        {
            if (target == null) target = PickTarget();
            if (target == null || !agent.enabled || !agent.isOnNavMesh || IsStunned) return false;
            float dist = FlatDistance(target.transform.position);
            lungeDir = target.transform.position - transform.position;
            lungeDir.y = 0f;
            if (lungeDir.sqrMagnitude < 0.01f) return false;
            lungeDir.Normalize();
            transform.rotation = Quaternion.LookRotation(lungeDir);
            currentMove = Move.Lunge;
            attacking = false;
            agent.isStopped = true;
            lungeSpeedNow = BossDashSpeed;
            lungeMaxNow = Mathf.Clamp(dist - 1.2f, 3f, BossDashMax);
            lungeDamageNow = 0.9f;
            lungeTravelled = 0f;
            lungeHit.Clear();
            lungeStartAt = Time.time + BossDashTell;
            busyUntil = lungeStartAt + lungeMaxNow / BossDashSpeed + 0.4f + recovery * 0.5f;
            bossDashReadyAt = Time.time + BossDashCooldown;
            farSince = -1f;
            BossDashes++;
            PlayActionRpc(RigAction.Windup, BossDashTell);
            LungeWarnRpc(transform.position, lungeDir, lungeMaxNow + 1.5f, BossDashTell);
            DashRoarRpc(transform.position);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DashRoarRpc(Vector3 at)
        {
            Sfx.Play(Sound.Roar, at + Vector3.up * 1.5f, 0.9f, 1.1f);
            Vfx.Smoke(at, 3, 0.5f, 0.7f);
        }

        private void StepLunge()
        {
            float step = lungeSpeedNow * Time.deltaTime;
            var before = transform.position;
            agent.Move(lungeDir * step);
            float moved = Vector3.Distance(before, transform.position);
            lungeTravelled += moved;
            foreach (var victim in CombatQuery.Circle(transform.position + Vector3.up * 0.9f, 1.1f, Team.Players))
            {
                if (!lungeHit.Add(victim)) continue;
                victim.ServerDamage(damage * lungeDamageNow, transform.position - lungeDir, NetworkObjectId);
                if (IsBoss && victim.TryGetComponent<KnockbackReceiver>(out var dashKb)) dashKb.KnockbackRpc(lungeDir * 7f + Vector3.up * 2.5f);
            }
            if (moved < step * 0.3f || lungeTravelled >= lungeMaxNow + 1f || lungeHit.Count > 0)
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

            Health raged = null;
            float ragedThreat = 0f, currentThreat = 0f;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !PlayerLife.CanTarget(p.Health) || FlatDistance(p.transform.position) > leashRange || LostTo(p.Health)) continue;
                threat.TryGetValue(p.NetworkObjectId, out var t);
                if (target == p.Health) currentThreat = t;
                if (t > ragedThreat) { ragedThreat = t; raged = p.Health; }
            }
            bool targetValid = target != null && PlayerLife.CanTarget(target) && FlatDistance(target.transform.position) < leashRange && FlatDistance(home) < leashRange && !LostTo(target);
            if (raged != null && FlatDistance(home) < leashRange)
            {
                if (!targetValid || (raged != target && ragedThreat > currentThreat * SwitchThreshold)) return raged;
                return target;
            }

            if (targetValid) return target;

            if (Time.time < nextAcquire) return null;
            nextAcquire = Time.time + 0.15f + (NetworkObjectId % 5) * 0.02f;

            Health best = null;
            float bestDist = aggroRange;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                float d = FlatDistance(p.transform.position) / (p.Crouched.Value && !engaged ? PlayerLife.CrouchSightScale : 1f);
                if (d < bestDist && CombatQuery.ClearLine(transform.position + Vector3.up * 1.5f, p.Health))
                {
                    best = p.Health;
                    bestDist = d;
                }
            }

            if (best != null && best == blindTo) blindTo = null;
            if (best != null && target == null) AlertPack(best);
            if (best != null) ServerAddThreat(best.NetworkObjectId, 1f);
            return best;
        }

        private readonly Dictionary<ulong, float> threat = new Dictionary<ulong, float>();
        private float lastThreatTick, nextThreatSync;
        private static readonly List<ulong> threatKeys = new List<ulong>();

        public const float SwitchThreshold = 1.2f;

        public readonly NetworkVariable<ThreatView> Rage = new NetworkVariable<ThreatView>();

        public static float ClassThreat(Classes.ClassId c) => c == Classes.ClassId.Knight ? 2.6f : c == Classes.ClassId.Rogue ? 0.7f
            : c == Classes.ClassId.Ranger ? 0.85f : c == Classes.ClassId.Cleric ? 0.9f
            : c == Classes.ClassId.Flagellant ? 1.6f : c == Classes.ClassId.PlagueDoctor ? 0.9f : 1f;

        protected float LeadSkill => Mathf.Clamp01(Difficulty.Lead(depthForLoot) + (Elite.Value ? 0.25f : 0f) + (IsBoss ? 0.15f : 0f));

        protected Vector3 AimDir(Vector3 origin, float speed) => target != null ? Aim.Lead(target, origin, speed, LeadSkill) - origin : transform.forward;

        public void ServerAddThreat(ulong player, float amount)
        {
            if (!IsServer || amount <= 0f) return;
            threat.TryGetValue(player, out var t);
            threat[player] = t + amount;
        }

        public float ThreatOf(ulong player) => threat.TryGetValue(player, out var t) ? t : 0f;

        public void ServerDebugPlace(Vector3 at)
        {
            if (NavMesh.SamplePosition(at, out var nh, 3f, NavMesh.AllAreas)) at = nh.position;
            if (agent != null && agent.enabled) agent.Warp(at); else transform.position = at;
            home = at;
        }

        public Health CurrentTarget => target;

        private float TopThreat()
        {
            float top = 0f;
            foreach (var kv in threat) if (kv.Value > top) top = kv.Value;
            return top;
        }

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

        private bool IdleWanders => !IsBoss && visualKind != CharacterArt.SkeletonKind.Archer && visualKind != CharacterArt.SkeletonKind.Gargoyle &&
            visualKind != CharacterArt.SkeletonKind.Mourner && visualKind != CharacterArt.SkeletonKind.Mimic;

        private Vector3[] patrol;
        private int patrolIndex, patrolStep = 1;
        private EnemyBrain patrolLeader;
        private Vector3 patrolOffset;

        public bool OnPatrol => patrol != null;

        public readonly NetworkVariable<bool> Patrolling = new NetworkVariable<bool>();
        public bool PatrolSynced => Patrolling.Value;

        public Vector3 PatrolGoal => home;

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

        protected void CancelWindup()
        {
            attacking = false;
            busyUntil = 0f;
        }

        public virtual void ServerStun(float seconds)
        {
            if (IsDead || !IsSpawned) return;
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

        private Vector3 investigateAt;
        private float investigateUntil;
        public bool Investigating => Time.time < investigateUntil;

        public bool HasTarget => target != null;

        public int Heard { get; private set; }

        public virtual bool ServerInvestigate(Vector3 at, float seconds)
        {
            if (!IsServer || IsDead || IsBoss || target != null || Lured) return false;
            if (visualKind == CharacterArt.SkeletonKind.Mourner || visualKind == CharacterArt.SkeletonKind.Mimic || visualKind == CharacterArt.SkeletonKind.Gargoyle) return false;
            bool fresh = !Investigating;
            investigateAt = at;
            investigateUntil = Time.time + seconds;
            Heard++;
            if (fresh) HeardRpc();
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HeardRpc()
        {
            float top = Rig != null ? Rig.transform.lossyScale.y * 2.1f : 2.1f;
            UI.GameHud.ShowCombo(transform.position + Vector3.up * top, "?", new Color(0.78f, 0.76f, 0.7f));
            if (VoicePitch > 0f) Sfx.Play(Sound.Growl, transform.position + Vector3.up * 1.4f, 0.35f, VoicePitch * 0.9f);
            else Sfx.Play(Sound.Rattle, transform.position + Vector3.up * 1.4f, 0.3f, Random.Range(0.8f, 0.95f));
        }

        public virtual void ServerHear(Health who)
        {
            if (IsDead || who == null || !PlayerLife.CanTarget(who)) return;
            if (target == null) target = who;
        }

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

        protected float heldUntil;
        public bool InEntrance => Time.time < heldUntil;

        public void ServerDebugPose(RigAction action, float seconds)
        {
            if (!IsServer) return;
            heldUntil = Time.time + seconds;
            PlayActionRpc(action, seconds);
        }

        protected bool HeldByStun()
        {
            if (!IsServer || IsDead || (!IsStunned && !InEntrance)) return false;
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            ServerTickThreat();
            ServerSyncStatus();
            return true;
        }

        protected virtual float SpeedScale => 1f;

        public readonly NetworkVariable<byte> Status = new NetworkVariable<byte>();

        private void ServerSyncStatus()
        {
            bool lungeTell = lungeStartAt > 0f || lunging;
            byte s = (byte)((IsStunned ? 1 : 0) | (Taunter != null ? 2 : 0) | (attacking || lungeTell ? 4 : 0)
                | (attacking && currentMove == Move.Heavy ? 8 : 0) | (attacking && currentMove == Move.Sweep ? 16 : 0) | (lungeTell ? 32 : 0) | (Recovering ? 64 : 0));
            if (Status.Value != s) Status.Value = s;
        }

        public Health Taunter => taunter != null && Time.time < tauntUntil ? taunter : null;

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

            if (taunterHealth != null) threat[taunterHealth.NetworkObjectId] = TopThreat() * 1.3f + 40f;
        }

        public virtual void ServerKnockback(Vector3 velocity)
        {
            if (!Heavy) knockback += velocity;
        }

        public bool ServerYank(Vector3 to, float delay = 0f)
        {
            if (Heavy || IsBoss || IsDead || agent == null) return false;
            if (!NavMesh.SamplePosition(to, out var hit, 2.5f, NavMesh.AllAreas)) return false;
            if (haul != null) StopCoroutine(haul);
            haul = StartCoroutine(Haul(hit.position, delay));
            return true;
        }

        public const float HaulSpeed = 15f;
        private Coroutine haul;

        public static float HaulSeconds(float distance) => 0.12f + distance / HaulSpeed;

        private System.Collections.IEnumerator Haul(Vector3 to, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            float t0 = Time.time, limit = HaulSeconds(Vector3.Distance(transform.position, to)) + 0.4f;
            while (!IsDead && agent != null && agent.enabled && agent.isOnNavMesh && Time.time - t0 < limit)
            {
                var d = to - transform.position;
                d.y = 0f;

                float speed = HaulSpeed * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01((Time.time - t0) / 0.12f));
                float step = speed * Time.deltaTime;
                if (d.magnitude <= step) { agent.Move(d); break; }
                agent.Move(d.normalized * step);
                yield return null;
            }
            haul = null;
        }

        private void OnDamagedServer(float amount, ulong attackerId)
        {
            if (blindTo != null && blindTo.NetworkObjectId == attackerId) blindTo = null;
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var who) && who.TryGetComponent<Progression.PlayerProgress>(out var hitter))
                ServerAddThreat(attackerId, amount * ClassThreat(hitter.ClassId.Value));

            if (!IsBoss && WindingUp && amount >= health.Max.Value * Difficulty.PoiseBreakFraction)
            {
                InterruptWindup(0.6f);
                PoiseBreakRpc();
            }

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
            if (IsDead) return;
            if (AffixKind == EliteAffix.Volatile || ChampionTrait == ChampionTraits.Marrowgrin) StartCoroutine(VolatileBlast());
            if (tithe > 0) World.DungeonDirector.Instance?.ServerDropLoot(transform.position + Vector3.up * 0.5f, LootKind.Gold, Mathf.RoundToInt(tithe * 1.5f), transform.position, fixedSum: true);
            if (AffixKind == EliteAffix.Splitting) World.DungeonDirector.Instance?.ServerSpawnSplit(transform.position, scaledPlayersBase, depthForLoot);

            if (AffixKind == EliteAffix.Gravebound) World.DungeonDirector.Instance?.ServerGravebound(this);
            else World.DungeonDirector.Instance?.ServerRestless(this);
            IsDead = true;
            attacking = false;
            agent.isStopped = true;
            agent.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            PlayActionRpc(RigAction.Death, 1.1f);
            if (IsChampion && !NoLoot)
            {
                GearDrop.ServerSpawnForParty(transform.position, 1f, 0);

                World.DungeonDirector.Instance?.ServerDropLoot(transform.position + Vector3.up * 0.5f + Vector3.back * 0.8f, LootKind.RareItem, (int)Progression.RareItems.Random(), transform.position);

                for (int i = 0; i < Difficulty.Party; i++)
                    if (Random.value < ChampionSigilChance) World.DungeonDirector.Instance?.ServerDropLoot(transform.position + Vector3.up * 0.5f + Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 0.8f, LootKind.Sigil, 1, transform.position);
                ChampionFallenRpc(Title.Value.ToString());
                World.DungeonDirector.Instance?.ServerSealFound(depthForLoot);
            }
            if (!NoLoot && lootPrefab != null)
            {
                LootDrop.ServerSpawnFromTable(lootPrefab, Elite.Value ? LootTable.Elite : Loot, transform.position, (1f + 0.3f * World.DungeonDirector.Tier(depthForLoot))  * World.DungeonDirector.OmenGold * World.DungeonDirector.EventGold * (Elite.Value ? World.DungeonDirector.EventEliteLoot : 1f),
                    this is BossBrain || IsChampion || this is HoarderBrain || this is MimicBrain ? Difficulty.FixedLootShare(Difficulty.Party) : Difficulty.KillLootShare(Difficulty.Party));

                if (this is BossBrain)
                {
                    GearDrop.ServerSpawnForParty(transform.position, 1f, 1, (BossBrain)this);
                }
                else if (Elite.Value) GearDrop.ServerSpawnForParty(transform.position, 0.18f * Difficulty.DropCut, 0);
            }
            Invoke(nameof(Despawn), 4f);
        }

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
        private void VolatileWarnRpc(Vector3 at)
        {
            Telegraph.Show(at, 3.2f, 1.3f, null, new Color(1f, 0.45f, 0.1f, 0.4f));

            Sfx.Play(Sound.Fire, at + Vector3.up * 0.8f, 0.8f, 0.7f);
        }

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
                case RigAction.Swing: case RigAction.SweepStrike: Sfx.Play(Sound.Swing, transform.position + Vector3.up, 0.7f, action == RigAction.SweepStrike ? 0.65f : 0.8f); break;
                case RigAction.Cleave: Sfx.Play(Sound.Swing, transform.position + Vector3.up, 0.8f, 0.55f); break;
                case RigAction.WindHeavy:
                case RigAction.WindSweep:
                case RigAction.Windup:
                    if (voice > 0f && (Random.value < 0.35f || Elite.Value)) Sfx.Play(Sound.MonsterAttack, transform.position + Vector3.up * 1.4f, 0.7f, voice);

                    else if (voice <= 0f) Sfx.Play(Sound.Rattle, transform.position + Vector3.up * 1.2f, 0.4f, 1.3f);
                    break;
                case RigAction.Death:
                    Sfx.Play(Sound.BoneBreak, transform.position + Vector3.up);
                    if (voice > 0f) Sfx.Play(Sound.MonsterDie, transform.position + Vector3.up * 1.2f, 0.9f, voice);
                    BoneBurst.Spawn(transform.position, Rig != null ? Rig.transform.lossyScale.y : 1f, Elite.Value);
                    foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

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
