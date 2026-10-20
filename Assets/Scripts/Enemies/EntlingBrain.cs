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
            holdUntil = Time.time + HoldLimit;
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
            LatchRpc(held.NetworkObjectId, false);
            held = null;
        }

        protected override void OnDied(Health h)
        {
            Release();
            base.OnDied(h);
        }

        public override void OnNetworkDespawn()
        {
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
