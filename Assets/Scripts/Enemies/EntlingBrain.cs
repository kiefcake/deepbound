using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Entling: a knee-high knot of root and rot that sprints at you, and when it reaches you
    /// wraps its roots round your legs. While it holds on you cannot move a step (you can still
    /// fight): it squeezes a little every second until it is killed, by you or by a friend.
    /// Fragile, fast, and it never lets go on its own (a long safety limit aside), so the answer
    /// is to kill it on the way in, or to turn round and cut it off you.
    /// From floor 3 (the Drowned Vaults: roots through the stone) down.
    /// </summary>
    public class EntlingBrain : EnemyBrain
    {
        private PlayerDebuffs held;
        private float holdUntil, nextSqueeze;
        public const float HoldLimit = 12f;

        protected override bool UsesMoveSet => false;
        public bool Holding => held != null;
        /// <summary>The one it is after (tests).</summary>
        public Health Quarry => target;

        private static readonly System.Collections.Generic.List<EntlingBrain> Live = new System.Collections.Generic.List<EntlingBrain>();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) Live.Add(this);
        }

        /// <summary>Someone else's: another living Entling is after this player or already holds them.</summary>
        private bool Claimed(Health p)
        {
            foreach (var e in Live)
            {
                if (e == null || e == this || e.IsDead) continue;
                if (e.target == p || (e.held != null && e.held.gameObject == p.gameObject)) return true;
            }
            return false;
        }

        private bool RootedByAnother(Health p) =>
            p.TryGetComponent<PlayerDebuffs>(out var d) && d.RootedBy.Value != ulong.MaxValue && d.RootedBy.Value != NetworkObjectId;

        /// <summary>
        /// One each (2026-10-21, the user: "entlings should target a player each if possible"): a pack of
        /// Entlings spreads out, each going for a player no other Entling is after or holding, nearest first.
        /// Only when every standing player is spoken for do two share one, by the usual rules.
        /// </summary>
        protected override Health PickTarget()
        {
            if (held != null) return target;
            bool Usable(Health p) => p != null && PlayerLife.CanTarget(p) && FlatDistance(p.transform.position) < leashRange;
            if (Usable(target) && !RootedByAnother(target) && !Claimed(target)) return target;
            Health best = null;
            float bestDist = target != null ? leashRange : aggroRange * 1.6f;
            foreach (var pl in PlayerLife.All)
            {
                if (pl == null || !pl.Targetable || !Usable(pl.Health) || RootedByAnother(pl.Health) || Claimed(pl.Health)) continue;
                float d = FlatDistance(pl.transform.position);
                if (d < bestDist) { bestDist = d; best = pl.Health; }
            }
            if (best != null) { ServerAddThreat(best.NetworkObjectId, 1f); return best; }
            return base.PickTarget();
        }

        protected override void BeginAttack()
        {
            // The tell is short: a crouch and a rattle of twigs, then the leap.
            windup = 0.35f;
            attackArc = 80f;
            base.BeginAttack();
        }

        protected override void Strike()
        {
            PlayActionRpc(RigAction.Swing, 0.3f);
            if (target == null || held != null) return;
            if (FlatDistance(target.transform.position) > attackRange + 0.7f) return;
            if (!target.TryGetComponent<PlayerDebuffs>(out var deb) || deb.RootedBy.Value != ulong.MaxValue) return;
            // Latch on: beside the player, facing them, roots round their legs.
            held = deb;
            holdUntil = Time.time + (target.TryGetComponent<Progression.PlayerProgress>(out var tp) && tp.HasRelic(Deepbound.Loot.Relic.RootbreakerCharm) ? 3f : HoldLimit);
            nextSqueeze = Time.time + 1f;
            deb.ServerHoldRoots(NetworkObjectId);
            var side = transform.position - target.transform.position;
            side.y = 0f;
            var spot = target.transform.position + side.normalized * 0.9f;
            if (agent.enabled && agent.isOnNavMesh) agent.Warp(spot);
            transform.rotation = Quaternion.LookRotation(-side.normalized);
            target.ServerDamage(damage, transform.position, NetworkObjectId);
            LatchRpc(target.NetworkObjectId, true);
        }

        protected override void Update()
        {
            // Stunned, it still lets go when its time is up (the player's side checks too).
            if (IsServer && !IsDead && held != null && (Time.time >= holdUntil || held.RootedBy.Value != NetworkObjectId)) Release();
            if (HeldByStun()) return;
            if (IsServer && !IsDead && held != null)
            {
                var hp = held.GetComponent<Health>();
                bool lost = hp == null || !hp.IsAlive || held.GetComponent<PlayerLife>().Downed.Value || Time.time >= holdUntil;
                if (lost) { Release(); }
                else
                {
                    agent.isStopped = true;
                    busyUntil = Time.time + 0.3f;
                    var to = held.transform.position - transform.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
                    if (Time.time >= nextSqueeze)
                    {
                        nextSqueeze = Time.time + 1f;
                        hp.ServerDamage(damage * 0.45f, transform.position, NetworkObjectId);
                        PlayActionRpc(RigAction.Bash, 0.4f);
                    }
                }
            }
            base.Update();
        }

        private void Release()
        {
            if (held == null) return;
            held.ServerReleaseRoots(NetworkObjectId);
            // Despawning (the floor ending) cannot send RPCs; the cages notice their Entling is gone.
            if (IsSpawned) LatchRpc(held.NetworkObjectId, false);
            held = null;
        }

        protected override void OnDied(Health h)
        {
            Live.Remove(this);
            Release();
            base.OnDied(h);
        }

        public override void OnNetworkDespawn()
        {
            Live.Remove(this);
            if (IsServer) Release();
            base.OnNetworkDespawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LatchRpc(ulong player, bool on)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(player, out var obj)) return;
            if (on)
            {
                RootCage.Attach(obj.transform, this);
                Sfx.Play(Sound.Creak, obj.transform.position, 1f, 0.7f);
                UI.GameHud.ShowCombo(obj.transform.position + Vector3.up * 2.1f, "ENTANGLED", new Color(0.55f, 0.85f, 0.35f));
                if (obj.TryGetComponent<NetworkPlayer>(out var np) && np.IsOwner)
                    UI.GameHud.Feed("Roots hold your legs: kill the Entling to break free", new Color(0.55f, 0.85f, 0.35f));
            }
            else
            {
                RootCage.Detach(obj.transform);
                Sfx.Play(Sound.BoneBreak, obj.transform.position, 0.8f, 0.8f);
            }
        }
    }
}
