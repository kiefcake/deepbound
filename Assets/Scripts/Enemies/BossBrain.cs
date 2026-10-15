using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// What every boss shares: the boss-bar identity, party scaling, guaranteed loot,
    /// immunity to knockback, summoning, and the telegraph and impact effects. Each floor's
    /// boss subclasses this with its own mechanics.
    ///
    /// Design rule for every boss: each mechanic has one visible tell and one answer, and
    /// the answers are different skills — so a fight tests the whole kit and the whole
    /// party, not just damage output.
    /// </summary>
    public abstract class BossBrain : EnemyBrain
    {
        public static BossBrain Active { get; private set; }

        [Header("Boss")]
        [SerializeField] protected NetworkObject minionPrefab;

        public abstract string DisplayName { get; }
        public Health Health => health;

        protected int scaledPlayers = 1, scaledDepth;
        protected float castUntil;

        public void SetMinionPrefab(NetworkObject prefab) => minionPrefab = prefab;

        protected override LootTable Loot => LootTable.SkeletonKing;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Active = this;
            if (IsServer) health.DamagedServer += (_, __) => Dormant = false;
        }

        public override void OnNetworkDespawn()
        {
            if (Active == this) Active = null;
            base.OnNetworkDespawn();
        }

        public void ServerScaleBoss(int players, int depth)
        {
            scaledPlayers = players;
            scaledDepth = depth;
            ServerScale(players, depth);
        }

        protected bool Casting => Time.time < castUntil;

        // ---- Fury: a fight that drags past a few minutes makes the boss hit much harder. ----
        public const float FurySeconds = 210f;
        private float fightStartedAt = -1f;
        private bool furious;

        private void TickFury()
        {
            if (Dormant || furious) return;
            if (fightStartedAt < 0f) { fightStartedAt = Time.time; return; }
            if (Time.time - fightStartedAt < FurySeconds) return;
            furious = true;
            damage *= 1.4f;
            moveSpeed *= 1.15f;
            FuryRpc(DisplayName);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FuryRpc(string who)
        {
            UI.GameHud.ShowTitleCard(who.ToUpperInvariant() + " GROWS FURIOUS", "Finish this, quickly");
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 2f, 1f, 0.6f);
            if (Rig != null) Rig.GlowColor = new Color(1f, 0.15f, 0.05f);
        }

        // ---- Shockwave: an expanding ring that hits grounded players as it passes. ----
        protected virtual float ShockwaveCooldown => 0f;   // 0 = this boss has none
        protected virtual Color ShockwaveColor => new Color(1f, 0.6f, 0.3f);
        protected virtual float ShockwaveDamage => damage * 0.9f;
        private float nextShockwave = -1f, shockwaveAt = -1f;

        private class Wave { public Vector3 Origin; public float Start, Speed, Max, Damage; public readonly HashSet<ulong> Hit = new HashSet<ulong>(); }
        private readonly List<Wave> waves = new List<Wave>();

        /// <summary>Server: start a shockwave wind-up if one is due. Call from ChooseSpecialAttack.</summary>
        protected bool TryShockwave()
        {
            if (ShockwaveCooldown <= 0f) return false;
            // The first comes a little after the fight starts (or after a phase unlocks it).
            if (nextShockwave < 0f) nextShockwave = Time.time + Mathf.Min(6f, ShockwaveCooldown * 0.6f);
            if (Time.time < nextShockwave) return false;
            nextShockwave = Time.time + ShockwaveCooldown;
            shockwaveAt = Time.time + 1.1f;
            castUntil = shockwaveAt + 0.3f;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Slam, 1.3f);
            CircleTelegraphRpc(transform.position, 2.5f, 1.1f, false);
            return true;
        }

        /// <summary>Server: launch a wave now (bosses may also call this from their own attacks).</summary>
        protected void LaunchShockwave(Vector3 origin, float speed, float max, float dmg)
        {
            waves.Add(new Wave { Origin = origin, Start = Time.time, Speed = speed, Max = max, Damage = dmg });
            Debug.Log("[Boss] " + DisplayName + " shockwave");
            ShockwaveFxRpc(origin, speed, max, ShockwaveColor);
        }

        private void TickShockwaves()
        {
            if (shockwaveAt > 0f && Time.time >= shockwaveAt)
            {
                shockwaveAt = -1f;
                LaunchShockwave(transform.position, 9f, 16f, ShockwaveDamage);
            }
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i];
                float r = (Time.time - w.Start) * w.Speed;
                if (r > w.Max + 1f) { waves.RemoveAt(i); continue; }
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable || w.Hit.Contains(p.NetworkObjectId)) continue;
                    var d = p.transform.position - w.Origin;
                    // Airborne players clear the wave: jump it.
                    if (d.y > 0.45f) continue;
                    d.y = 0f;
                    if (Mathf.Abs(d.magnitude - r) > 0.8f) continue;
                    w.Hit.Add(p.NetworkObjectId);
                    Debug.Log("[Boss] shockwave hit " + p.name);
                    p.Health.ServerDamage(w.Damage, w.Origin + Vector3.up, NetworkObjectId);
                    KnockPlayer(p.Health, d.normalized * 7f + Vector3.up * 3f);
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShockwaveFxRpc(Vector3 origin, float speed, float max, Color color)
        {
            ShockwaveRing.Spawn(origin, speed, max, color);
            Sfx.Play(Sound.Slam, origin + Vector3.up, 1f, 0.6f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, origin) < 18f) local.AddShake(0.35f);
        }

        /// <summary>
        /// Server: asleep on the throne until the arena seals (or it is hit). The director
        /// wakes it; damage wakes it too, so a party cannot plink it from the doorway.
        /// </summary>
        public bool Dormant { get; set; }

        protected override bool IsBoss => true;
        protected override bool Heavy => true;

        protected override void Update()
        {
            if (IsServer && !IsDead) { TickShockwaves(); TickFury(); }
            if (IsServer && !IsDead && Dormant)
            {
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
                return;
            }
            if (IsServer && !IsDead && Casting)
            {
                agent.isStopped = true;
                return;
            }
            base.Update();
        }

        /// <summary>A cast-time summon of the minion prefab in a ring around the boss.</summary>
        protected NetworkObject guardPrefab;

        /// <summary>A second kind of minion some bosses call (the Hollow King's royal guard).</summary>
        public void SetGuardPrefab(NetworkObject prefab) => guardPrefab = prefab;

        protected void Summon(int count, float radius, float castTime = 1.6f) => SummonOf(minionPrefab, count, radius, castTime);

        protected void SummonOf(NetworkObject prefab, int count, float radius, float castTime = 1.6f)
        {
            var minionPrefab = prefab;
            castUntil = Time.time + castTime;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Cast, castTime);
            SummonFxRpc();

            if (minionPrefab == null) return;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
                var minion = Instantiate(minionPrefab, pos, Quaternion.LookRotation(transform.position - pos));
                minion.Spawn(destroyWithScene: true);
                var brain = minion.GetComponent<EnemyBrain>();
                brain.NoLoot = true;
                brain.ServerScale(scaledPlayers, scaledDepth);
            }
        }

        /// <summary>Server: shove a player. Movement is owner-authoritative, so this asks their client to do it.</summary>
        protected static void KnockPlayer(Health victim, Vector3 velocity)
        {
            if (victim.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(velocity);
        }

        // ------------------------------------------------------------------
        // Presentation
        // ------------------------------------------------------------------

        /// <param name="keyed">Keyed telegraphs can be cancelled (a slam interrupted by a bash).</param>
        [Rpc(SendTo.ClientsAndHost)]
        protected void CircleTelegraphRpc(Vector3 center, float radius, float duration, bool keyed)
        {
            Telegraph.Show(center, radius, duration, keyed ? this : null);
        }

        [Rpc(SendTo.ClientsAndHost)]
        protected void LineTelegraphRpc(Vector3 start, Vector3 direction, float length, float width, float duration)
        {
            Telegraph.ShowLine(start, direction, length, width, duration, this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        protected void CancelTelegraphRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        protected void ImpactFxRpc(Vector3 at, float shakeRange)
        {
            Sfx.Play(Sound.Slam, at, 1f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < shakeRange) local.ViewModelBlockImpact();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SummonFxRpc() => Sfx.Play(Sound.Summon, transform.position + Vector3.up * 2f, 1f);
    }
}
