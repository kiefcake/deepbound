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

            if (!IsServer) return;

            home = transform.position;
            health.Defense = defense;
            health.Died += OnDied;
            health.DamagedServer += OnDamagedServer;

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
            float hpScale = (1f + 0.6f * Mathf.Max(0, players - 1)) * (1f + 0.25f * depth);
            damage *= 1f + 0.15f * depth;
            depthForLoot = depth;
            health.ServerSetMax(maxHealth * hpScale, refill: true);
        }

        // ------------------------------------------------------------------
        // Server loop
        // ------------------------------------------------------------------

        protected virtual void Update()
        {
            if (!IsServer || IsDead || !agent.enabled || !agent.isOnNavMesh) return;

            ApplyKnockback();

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
                agent.SetDestination(target.transform.position);
            }

            if (Time.time >= nextRattle)
            {
                nextRattle = Time.time + Random.Range(2.5f, 5f);
                RattleRpc();
            }
        }

        /// <summary>Bosses override this to use abilities. Return true if an action was started.</summary>
        protected virtual bool ChooseSpecialAttack(float distanceToTarget) => false;

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
            if (taunter != null && Time.time < tauntUntil && taunter.IsAlive) return taunter;

            // Stay on the current target while it is alive and within leash.
            if (target != null && target.IsAlive && FlatDistance(target.transform.position) < leashRange &&
                FlatDistance(home) < leashRange)
            {
                return target;
            }

            Health best = null;
            float bestDist = aggroRange;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.Downed.Value || !p.Health.IsAlive) continue;
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

        public void ServerTaunt(Health taunterHealth, float seconds)
        {
            taunter = taunterHealth;
            tauntUntil = Time.time + seconds;
            target = taunterHealth;
        }

        public virtual void ServerKnockback(Vector3 velocity) => knockback += velocity;

        private void OnDamagedServer(float amount, ulong attackerId)
        {
            // Getting hit from out of view still pulls aggro onto the attacker.
            if (target == null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj) &&
                obj.TryGetComponent<Health>(out var attacker) && attacker.Team == Team.Players)
            {
                target = attacker;
                AlertPack(attacker);
            }
        }

        protected virtual void OnDied(Health _)
        {
            IsDead = true;
            attacking = false;
            agent.isStopped = true;
            agent.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            PlayActionRpc(RigAction.Death, 1.1f);
            if (!NoLoot && lootPrefab != null)
            {
                LootDrop.ServerSpawnFromTable(lootPrefab, Loot, transform.position, 1f + 0.5f * depthForLoot);
            }
            Invoke(nameof(Despawn), 4f);
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
                    foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
                    break;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RattleRpc() => Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.5f);

        private void OnHurtVisual(float amount)
        {
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.8f, 1.2f);
            if (Rig != null && Rig.Current == RigAction.None) Rig.Play(RigAction.Stagger, 0.2f);
        }
    }
}
