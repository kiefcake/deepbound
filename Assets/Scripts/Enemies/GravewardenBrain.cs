using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Gravewarden: floor 1's boss, and the fight that teaches positioning.
    ///
    /// <list type="bullet">
    /// <item><b>Tower Shield</b> (passive) — hits from the front deal a quarter damage, with a
    ///   loud clang so you know. Answer: the tank holds its attention while everyone else
    ///   works its back and flanks.</item>
    /// <item><b>Mace Sweep</b> — a wide, slow melee arc. Answer: block, or step back.</item>
    /// <item><b>Gaoler's Charge</b> — a red lane fills from its feet toward a player; then it
    ///   charges down the lane, trampling and flinging everyone in it. Answer: step out of
    ///   the lane — and if it slams into a wall or pillar, it is <b>dazed</b>: shield down,
    ///   taking extra damage from every side. Baiting the charge into stone is the fight.
    ///   A Shield Bash during the wind-up cancels it.</item>
    /// </list>
    /// Below 30% it enrages and charges twice as often.
    /// </summary>
    public class GravewardenBrain : BossBrain
    {
        [SerializeField] private float chargeWindup = 1.3f;
        [SerializeField] private float chargeSpeed = 17f;
        [SerializeField] private float chargeDamage = 40f;
        [SerializeField] private float chargeCooldown = 10f;
        [SerializeField] private float chargeMaxLength = 20f;
        [SerializeField] private float dazeSeconds = 2.8f;
        [SerializeField] private float frontalDamageTaken = 0.25f;
        [SerializeField] private float dazedDamageTaken = 1.5f;

        public override string DisplayName => "The Gravewarden";

        private enum ChargeState { None, Windup, Charging }
        private ChargeState charge;
        private float chargeAt, chargeLength, charged;
        private Vector3 chargeDir;
        private float nextCharge;
        private float dazedUntil;
        private readonly HashSet<Health> trampled = new HashSet<Health>();

        public bool Dazed => Time.time < dazedUntil;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                health.IncomingModifiers.Add(ShieldModifier);
                nextCharge = Time.time + 5f;
            }
        }

        private bool Enraged => health.Fraction < 0.3f;

        private float ShieldModifier(float damage, Vector3 attackerPosition)
        {
            if (Dazed) return damage * dazedDamageTaken;

            var to = attackerPosition - transform.position;
            to.y = 0f;
            if (charge != ChargeState.Charging && Vector3.Angle(transform.forward, to) < 55f)
            {
                ShieldClangRpc();
                return damage * frontalDamageTaken;
            }
            return damage;
        }

        // ---- Grave Rend (below half health): a line of bone spikes rips toward every player. ----
        private float nextRend = -1f;
        private struct Rend { public Vector3 From, Dir; public float When; }
        private readonly System.Collections.Generic.List<Rend> rends = new System.Collections.Generic.List<Rend>();
        private const float RendLength = 15f, RendWidth = 1.7f, RendWindup = 1.1f;

        private bool TryGraveRend()
        {
            if (health.Fraction >= 0.5f) return false;
            if (nextRend < 0f) nextRend = Time.time + 1.5f;
            if (Time.time < nextRend) return false;
            nextRend = Time.time + 9f;
            castUntil = Time.time + RendWindup + 0.2f;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Slam, RendWindup + 0.2f);
            foreach (var p in Player.PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                var dir = p.transform.position - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.5f || dir.magnitude > RendLength + 4f) continue;
                dir.Normalize();
                rends.Add(new Rend { From = transform.position, Dir = dir, When = Time.time + RendWindup });
                RendWarnRpc(transform.position, dir);
            }
            Debug.Log("[Boss] Gravewarden grave rend x" + rends.Count);
            return true;
        }

        private void ResolveRends()
        {
            for (int i = rends.Count - 1; i >= 0; i--)
            {
                if (Time.time < rends[i].When) continue;
                var r = rends[i];
                rends.RemoveAt(i);
                foreach (var p in Player.PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    var to = p.transform.position - r.From;
                    to.y = 0f;
                    float along = Vector3.Dot(to, r.Dir);
                    if (along < 0f || along > RendLength) continue;
                    if ((to - r.Dir * along).magnitude > RendWidth * 0.5f + 0.3f) continue;
                    p.Health.ServerDamage(damage * 1.1f, r.From + Vector3.up, NetworkObjectId);
                    KnockPlayer(p.Health, Vector3.up * 6f + r.Dir * 2f);
                }
                RendFxRpc(r.From, r.Dir);
            }
        }

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void RendWarnRpc(Vector3 from, Vector3 dir) => Telegraph.ShowLine(from, dir, RendLength, RendWidth, RendWindup, null);

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void RendFxRpc(Vector3 from, Vector3 dir)
        {
            Sfx.Play(Sound.BoneBreak, from + dir * 4f + Vector3.up, 1f, 0.7f);
            var bone = new Color(0.9f, 0.85f, 0.7f);
            for (int i = 1; i <= 7; i++)
            {
                var at = from + dir * (i * RendLength / 7f);
                Vfx.IceSpikes(at, 0.8f, 4);
                Vfx.Embers(at, 3, bone, 3f, 0.05f, 0.5f);
            }
            Vfx.Smoke(from + dir * RendLength * 0.5f, 3, 2f, 1.4f);
        }

        protected override void Update()
        {
            if (IsServer && !IsDead) ResolveRends();
            if (IsServer && !IsDead)
            {
                if (charge == ChargeState.Windup)
                {
                    agent.isStopped = true;
                    transform.rotation = Quaternion.LookRotation(chargeDir);
                    if (Time.time >= chargeAt) BeginRun();
                    return;
                }
                if (charge == ChargeState.Charging)
                {
                    StepCharge();
                    return;
                }
                if (Dazed)
                {
                    agent.isStopped = true;
                    return;
                }
            }
            base.Update();
        }

        // Shield Quake: the Warden slams its shield and the floor ripples out.
        protected override float ShockwaveCooldown => 13f;
        protected override Color ShockwaveColor => new Color(1f, 0.55f, 0.2f);

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (TryShockwave()) return true;
            if (TryGraveRend()) return true;
            if (Time.time < nextCharge || distanceToTarget < 4.5f) return false;
            if (!CombatQuery.ClearLine(transform.position + Vector3.up * 1.2f, target)) return false;

            BeginChargeWindup();
            return true;
        }

        protected override void BeginAttack()
        {
            windup = 0.9f;
            attackArc = 140f;
            base.BeginAttack();
        }

        private void BeginChargeWindup()
        {
            chargeDir = target.transform.position - transform.position;
            chargeDir.y = 0f;
            chargeDir.Normalize();

            // The lane runs until it meets the navmesh edge — a wall, or a pillar's footprint.
            var from = transform.position;
            chargeLength = chargeMaxLength;
            if (NavMesh.Raycast(from, from + chargeDir * chargeMaxLength, out var hit, NavMesh.AllAreas)) chargeLength = hit.distance;

            charge = ChargeState.Windup;
            chargeAt = Time.time + chargeWindup;
            nextCharge = Time.time + (Enraged ? chargeCooldown * 0.5f : chargeCooldown);
            trampled.Clear();

            Debug.Log("[Boss] Gravewarden charge wind-up, lane " + chargeLength.ToString("F1") + "m");
            PlayActionRpc(RigAction.Windup, chargeWindup);
            LineTelegraphRpc(from, chargeDir, chargeLength + 1f, 2.6f, chargeWindup);
        }

        private void BeginRun()
        {
            charge = ChargeState.Charging;
            charged = 0f;
            PlayActionRpc(RigAction.Bash, 0.6f);
        }

        private void StepCharge()
        {
            float step = chargeSpeed * Time.deltaTime;
            var before = transform.position;
            agent.Move(chargeDir * step);
            float moved = Vector3.Distance(before, transform.position);
            charged += moved;

            foreach (var victim in CombatQuery.Circle(transform.position, 1.8f, Team.Players))
            {
                if (!trampled.Add(victim)) continue;
                victim.ServerDamage(chargeDamage, transform.position - chargeDir, NetworkObjectId);
                // Flung aside and back, out of the lane.
                var side = Vector3.Cross(Vector3.up, chargeDir);
                if (Vector3.Dot(victim.transform.position - transform.position, side) < 0f) side = -side;
                KnockPlayer(victim, (side * 1.2f + chargeDir).normalized * 13f + Vector3.up * 4f);
            }

            // Stopped short of a full step: it ran into something solid. The lane was
            // measured to the navmesh edge, so reaching its end against a wall counts too.
            bool blocked = moved < step * 0.4f;
            if (blocked)
            {
                EndCharge(hitWall: true);
            }
            else if (charged >= chargeLength + 0.5f)
            {
                EndCharge(hitWall: false);
            }
        }

        private void EndCharge(bool hitWall)
        {
            charge = ChargeState.None;
            Debug.Log("[Boss] Gravewarden charge ended after " + charged.ToString("F1") + "m, " + (hitWall ? "DAZED" : "no wall") + ", trampled " + trampled.Count);
            if (hitWall)
            {
                dazedUntil = Time.time + dazeSeconds;
                busyUntil = dazedUntil;
                PlayActionRpc(RigAction.Stagger, dazeSeconds);
                ImpactFxRpc(transform.position + chargeDir, 14f);
            }
            else
            {
                busyUntil = Time.time + 0.8f;
            }
        }

        public override void ServerStun(float seconds)
        {
            // A bash during the wind-up stops the charge before it starts.
            if (charge == ChargeState.Windup)
            {
                charge = ChargeState.None;
                CancelTelegraphRpc();
            }
            if (charge == ChargeState.Charging) return; // you cannot bash a charging bull
            base.ServerStun(Mathf.Min(seconds, 0.5f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShieldClangRpc() => Sfx.Play(Sound.Block, transform.position + transform.forward * 0.8f + Vector3.up * 1.6f, 0.9f, 0.7f);
    }
}
