using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    public class GravewardenBrain : BossBrain
    {
        protected override DashStyle Dash => DashStyle.ShieldCharge;
        [SerializeField] private float chargeWindup = 1.3f;
        [SerializeField] private float chargeSpeed = 17f;
        [SerializeField] private float chargeDamage = 40f;
        [SerializeField] private float chargeCooldown = 10f;
        [SerializeField] private float chargeMaxLength = 20f;
        [SerializeField] private float dazeSeconds = 2.8f;
        [SerializeField] private float frontalDamageTaken = 0.25f;

        [SerializeField] private float frontalDamageTakenSolo = 0.55f;

        [SerializeField] private float openingSeconds = 1.2f;
        private float openUntil;

        public const float SoloFrontal = 0.4f, Opening = 1.2f, SweepCycle = 0.9f + 1.1f;

        public bool Opened => Time.time < openUntil;
        [SerializeField] private float dazedDamageTaken = 1.5f;

        public override string DisplayName => "The Gravewarden";
        public override string Epithet => "Keeper of the First Door";
        protected override byte EntranceStyle => 1;
        public override string LastWords => "The door... is open. Forgive me. I held it as long as I could.";
        public override Progression.Deeds.Deed Deed => Progression.Deeds.Deed.Gravewarden;
        public override string Hint => "Its shield turns blows from the front. Strike after each sweep, from behind, or when it charges into stone.";

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

        private bool Desperate => health.Fraction < 0.3f;

        protected override float[] PhaseGates => new[] { 0.5f, 0.2f };
        private bool lastStand;
        private float nextExhale;
        private bool exhaledOnce;

        protected override void OnPhaseGate(int index)
        {
            if (index == 0)
                Transition("Grave Rend", "You knock at the first door. Now it knocks back.");
            else
                Transition("Last Stand", "No more walls between us. Only the grave.", 2.4f, () =>
                {
                    lastStand = true;
                    nextExhale = Time.time + 4f;
                    moveSpeed *= 1.12f;
                    LastStandRpc();
                    PhaseHint.Value = "Its shield is gone: strike from any side, but its blows come faster";
                });
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LastStandRpc()
        {
            if (Rig == null) return;
            foreach (var t in Rig.GetComponentsInChildren<Transform>(true))
                if (t.name == "TowerShield") t.gameObject.SetActive(false);
            Sfx.Play(Sound.Slam, transform.position, 1f, 0.7f);
            Rig.GlowColor = new Color(1f, 0.35f, 0.15f);
        }

        private float ShieldModifier(float damage, Vector3 attackerPosition)
        {
            if (Dazed) return damage * dazedDamageTaken;

            var to = attackerPosition - transform.position;
            to.y = 0f;

            if (to.sqrMagnitude < 0.25f) return damage;
            if (!lastStand && charge != ChargeState.Charging && !Opened && Vector3.Angle(transform.forward, to) < 55f)
            {
                ShieldClangRpc();
                return damage * (scaledPlayers <= 1 ? frontalDamageTakenSolo : frontalDamageTaken);
            }
            return damage;
        }

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
                var dir = PredictedFeet(p, RendWindup) - transform.position;
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
        private void RendWarnRpc(Vector3 from, Vector3 dir) { Telegraph.ShowLine(from, dir, RendLength, RendWidth, RendWindup, null); Gather(RendWindup, 1f); }

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
            if (HeldByStun()) return;
            if (IsServer && !IsDead) ResolveRends();

            if (IsServer && !IsDead && lastStand && charge == ChargeState.None && !Dazed && Time.time >= nextExhale && !PatternRunning)
            {
                nextExhale = Time.time + 11f;
                for (int e = 0; e < 2; e++)
                    StartPattern(new Pattern
                    {
                        Arms = 5, Interval = 0.2f, Speed = 8f, Spin = e == 0 ? 10f : -10f, Damage = damage * 0.35f, Kind = ProjectileKind.Soul,
                        Duration = 3.2f, Range = 22f, Origin = Orbit(2.4f, e == 0 ? 50f : -50f, e * 180f, 1.4f),
                    });
                if (!exhaledOnce) { exhaledOnce = true; Taunt("The grave breathes out."); }
            }
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
            windup = lastStand ? 0.72f : 0.9f;
            attackArc = 140f;
            base.BeginAttack();
        }

        protected override void Strike()
        {
            base.Strike();

            openUntil = Time.time + openingSeconds;
            OpeningRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OpeningRpc()
        {
            UI.GameHud.ShowCombo(transform.position + transform.forward * 0.8f + Vector3.up * 3.6f, "OPENING", new Color(1f, 0.85f, 0.45f));
            Sfx.Play(Sound.Block, transform.position + Vector3.up * 2f, 0.5f, 0.6f);
        }

        private void BeginChargeWindup()
        {
            chargeDir = (target.TryGetComponent<Player.PlayerLife>(out var quarry) ? PredictedFeet(quarry, Tell(chargeWindup)) : target.transform.position) - transform.position;
            chargeDir.y = 0f;
            chargeDir.Normalize();

            var from = transform.position;
            chargeLength = chargeMaxLength;
            if (NavMesh.Raycast(from, from + chargeDir * chargeMaxLength, out var hit, NavMesh.AllAreas)) chargeLength = hit.distance;

            charge = ChargeState.Windup;
            chargeAt = Time.time + Tell(chargeWindup);
            nextCharge = Time.time + (Desperate ? chargeCooldown * 0.5f : chargeCooldown);
            trampled.Clear();

            Debug.Log("[Boss] Gravewarden charge wind-up, lane " + chargeLength.ToString("F1") + "m");
            PlayActionRpc(RigAction.Charge, Tell(chargeWindup));
            LineTelegraphRpc(from, chargeDir, chargeLength + 1f, 2.6f, Tell(chargeWindup));
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
                victim.ServerDamage(Signature(chargeDamage), transform.position - chargeDir, NetworkObjectId);

                var side = Vector3.Cross(Vector3.up, chargeDir);
                if (Vector3.Dot(victim.transform.position - transform.position, side) < 0f) side = -side;
                KnockPlayer(victim, (side * 1.2f + chargeDir).normalized * 13f + Vector3.up * 4f);
            }

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

        protected override void OnBroken()
        {
            if (charge == ChargeState.Windup) charge = ChargeState.None;
            else if (charge == ChargeState.Charging) EndCharge(hitWall: false);
        }

        public override void ServerStun(float seconds)
        {
            if (charge == ChargeState.Windup)
            {
                charge = ChargeState.None;
                CancelTelegraphRpc();
            }
            if (charge == ChargeState.Charging) return;
            base.ServerStun(Mathf.Min(seconds, 0.5f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShieldClangRpc() => Sfx.Play(Sound.Block, transform.position + transform.forward * 0.8f + Vector3.up * 1.6f, 0.9f, 0.7f);
    }
}
