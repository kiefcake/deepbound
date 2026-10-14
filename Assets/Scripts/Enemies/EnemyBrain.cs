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
    public class EnemyBrain : NetworkBehaviour, ICrowdControl
    {
        [Header("Stats (scaled by depth and party size at spawn)")]
        [SerializeField] protected float maxHealth = 75f;
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
        private int depthForLoot;

        protected Health health;
        protected NavMeshAgent agent;
        public HumanoidRig Rig { get; private set; }

        protected Health target;
        private Health taunter;
        private float tauntUntil;
        protected float stunnedUntil;
        protected float busyUntil;
        private bool attacking;
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

        /// <summary>The elite's affix (see <see cref="EliteAffix"/>). Synced for the status tags.</summary>
        public readonly NetworkVariable<byte> Affix = new NetworkVariable<byte>();
        public EliteAffix AffixKind => (EliteAffix)Affix.Value;
        private bool wardUp;

        /// <summary>Bosses opt out of poise breaks and backstabs; they have their own answers.</summary>
        protected virtual bool IsBoss => false;

        /// <summary>Heavy enemies cannot be shoved around by a bash.</summary>
        protected virtual bool Heavy => false;

        protected int scaledPlayersBase = 1;
        private float surroundAngle;

        protected virtual void Awake()
        {
            health = GetComponent<Health>();
            agent = GetComponent<NavMeshAgent>();
            agent.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            Rig = CharacterArt.BuildSkeleton(transform, visualKind);
            health.Hurt += OnHurtVisual;
            Elite.OnValueChanged += (_, now) => ApplyEliteVisual(now);
            if (Elite.Value) ApplyEliteVisual(true);

            if (!IsServer) return;

            home = transform.position;
            health.Defense = defense;
            health.Died += OnDied;
            health.DamagedServer += OnDamagedServer;
            if (!IsBoss) health.IncomingModifiers.Add(BackstabModifier);
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
            float hpScale = (IsBoss ? Difficulty.BossHealth(players) : Difficulty.EnemyHealth(players)) * Difficulty.DepthHealth(depth) * Difficulty.Level.Health;
            damage *= Difficulty.Damage(players) * Difficulty.DepthDamage(depth) * Difficulty.Level.Damage;
            depthForLoot = depth;
            health.ServerSetMax(maxHealth * hpScale, refill: true);
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
        }

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
            return dealt * Difficulty.BackstabMultiplier;
        }

        /// <summary>A gilded enemy: bigger, gold-eyed, wreathed in a slow gold ring.</summary>
        private void ApplyEliteVisual(bool elite)
        {
            if (!elite || Rig == null) return;
            Rig.transform.localScale *= 1.18f;
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

        protected virtual void Update()
        {
            if (IsServer)
            {
                ServerSyncStatus();
                if (wardUp && (IsStunned || health.Chilled.Value)) ServerBreakWard();
            }
            if (!IsServer || IsDead || !agent.enabled || !agent.isOnNavMesh) return;

            ApplyKnockback();
            // Chill slows the walk, not the attack timing: you get time to reposition, not
            // a free win.
            agent.speed = health.Chilled.Value ? moveSpeed * 0.5f : moveSpeed;

            if (Time.time < stunnedUntil)
            {
                agent.isStopped = true;
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

            target = PickTarget();
            if (target == null)
            {
                ReturnHome();
                return;
            }

            float dist = FlatDistance(target.transform.position);
            if (ChooseSpecialAttack(dist)) return;

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

        protected virtual void BeginAttack()
        {
            attacking = true;
            agent.isStopped = true;
            strikeAt = Time.time + windup;
            busyUntil = strikeAt + recovery;
            PlayActionRpc(RigAction.Windup, windup);
        }

        protected virtual void Strike()
        {
            PlayActionRpc(RigAction.Swing, 0.3f);
            var origin = transform.position + Vector3.up * 1.2f;
            foreach (var victim in CombatQuery.Arc(origin, transform.forward, attackRange + 0.5f, attackArc, Team.Players))
            {
                victim.ServerDamage(damage, transform.position, NetworkObjectId);
            }
        }

        protected Health PickTarget()
        {
            if (taunter != null && Time.time < tauntUntil && PlayerLife.CanTarget(taunter)) return taunter;

            // Stay on the current target while it is alive, visible and within leash.
            if (target != null && PlayerLife.CanTarget(target) && FlatDistance(target.transform.position) < leashRange &&
                FlatDistance(home) < leashRange)
            {
                return target;
            }

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
            return best;
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

        private void ReturnHome()
        {
            agent.isStopped = false;
            if (FlatDistance(home) > 1f && Time.time >= repathAt)
            {
                repathAt = Time.time + 1f;
                agent.SetDestination(home);
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

        public virtual void ServerStun(float seconds)
        {
            if (IsDead) return;
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
            attacking = false;
            busyUntil = 0f;
            PlayActionRpc(RigAction.Stagger, Mathf.Min(seconds, 0.6f));
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

        /// <summary>Synced crowd-control state, for the status tags over enemies: bit 0 stunned, bit 1 taunted.</summary>
        public readonly NetworkVariable<byte> Status = new NetworkVariable<byte>();

        private void ServerSyncStatus()
        {
            byte s = (byte)((IsStunned ? 1 : 0) | (Taunter != null ? 2 : 0));
            if (Status.Value != s) Status.Value = s;
        }

        /// <summary>Who has this enemy taunted right now, or null.</summary>
        public Health Taunter => taunter != null && Time.time < tauntUntil ? taunter : null;

        public void ServerTaunt(Health taunterHealth, float seconds)
        {
            taunter = taunterHealth;
            tauntUntil = Time.time + seconds;
            target = taunterHealth;
        }

        public virtual void ServerKnockback(Vector3 velocity)
        {
            if (!Heavy) knockback += velocity;
        }

        private void OnDamagedServer(float amount, ulong attackerId)
        {
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

        protected virtual void OnDied(Health _)
        {
            if (AffixKind == EliteAffix.Volatile) StartCoroutine(VolatileBlast());
            IsDead = true;
            attacking = false;
            agent.isStopped = true;
            agent.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            PlayActionRpc(RigAction.Death, 1.1f);
            if (!NoLoot && lootPrefab != null)
            {
                LootDrop.ServerSpawnFromTable(lootPrefab, Elite.Value ? LootTable.Elite : Loot, transform.position, (1f + 0.5f * depthForLoot) * Difficulty.Level.Gold);
                // Gear, rolled for each player: always from a boss, sometimes from an elite.
                if (this is BossBrain)
                {
                    GearDrop.ServerSpawnForParty(transform.position, 1f, 1);
                    // A Sigil for each player: every boss is a chance to rank up an ability.
                    for (int i = 0; i < NetworkManager.ConnectedClientsIds.Count; i++)
                        World.DungeonDirector.Instance?.ServerDropLoot(transform.position + Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward * 1.6f, LootKind.Sigil, 1);
                }
                else if (Elite.Value) GearDrop.ServerSpawnForParty(transform.position, 0.3f, 0);
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
        }

        private void Despawn()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn();
        }

        // ------------------------------------------------------------------
        // Client presentation
        // ------------------------------------------------------------------

        [Rpc(SendTo.ClientsAndHost)]
        protected void PlayActionRpc(RigAction action, float duration)
        {
            Rig?.Play(action, duration);
            switch (action)
            {
                case RigAction.Swing: Sfx.Play(Sound.Swing, transform.position + Vector3.up, 0.7f, 0.8f); break;
                case RigAction.Death:
                    Sfx.Play(Sound.BoneBreak, transform.position + Vector3.up);
                    BoneBurst.Spawn(transform.position, Rig != null ? Rig.transform.lossyScale.y : 1f, Elite.Value);
                    foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
                    break;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BackstabRpc() => Sfx.Play(Sound.Hit, transform.position + Vector3.up * 1.2f, 0.8f, 0.75f);

        [Rpc(SendTo.ClientsAndHost)]
        private void PoiseBreakRpc()
        {
            Sfx.Play(Sound.Bash, transform.position + Vector3.up * 1.2f, 0.9f, 1.2f);
            UI.GameHud.Feed("Wind-up broken!", new Color(1f, 0.85f, 0.4f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RattleRpc() => Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.5f);

        private HitFlash flash;

        private void OnHurtVisual(float amount)
        {
            if (flash == null) flash = gameObject.AddComponent<HitFlash>();
            flash.Flash(Mathf.Clamp01(0.5f + amount / Mathf.Max(1f, health.Max.Value) * 4f));
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.8f, 1.2f);
            if (Rig != null && Rig.Current == RigAction.None) Rig.Play(RigAction.Stagger, 0.2f);
        }
    }
}
