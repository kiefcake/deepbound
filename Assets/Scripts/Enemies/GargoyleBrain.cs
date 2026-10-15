using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    /// <summary>
    /// A Gargoyle: a stone statue crouched by a wall, like the dozens of carved ones in the
    /// dungeon — until someone walks too close or strikes it. While stone it shrugs off
    /// nearly everything. Awake, it flies: claws up close, and every few seconds a dive along
    /// a telegraphed line. After a dive it lands hard and is briefly grounded and exposed.
    /// </summary>
    public class GargoyleBrain : EnemyBrain
    {
        [SerializeField] private float wakeRange = 5f, diveCooldown = 6.5f, diveWindup = 0.8f, diveSpeed = 16f, diveLength = 11f;

        public readonly NetworkVariable<bool> Woken = new NetworkVariable<bool>();

        private float nextDive, diveAt = -1f, groundedUntil;
        private bool diving;
        private Vector3 diveDir;
        private float dived;
        private readonly HashSet<Health> struck = new HashSet<Health>();
        private Transform wingL, wingR;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Rig != null)
            {
                wingL = Rig.Torso.Find("WingL");
                wingR = Rig.Torso.Find("WingR");
            }
            if (!IsServer) return;
            health.IncomingModifiers.Add((dmg, from) =>
            {
                if (!Woken.Value) { Wake(); return dmg * 0.1f; }
                return Time.time < groundedUntil ? dmg * 1.3f : dmg;
            });
        }

        private void Wake()
        {
            if (Woken.Value) return;
            Woken.Value = true;
            nextDive = Time.time + 2.5f;
            busyUntil = Time.time + 0.9f;
            WakeRpc();
        }

        protected override void Update()
        {
            // Everyone: stone stays still; awake it hovers and beats its wings.
            if (Rig != null)
            {
                float hover = Woken.Value && !IsDead && Time.time >= groundedUntil ? 1.1f + Mathf.Sin(Time.time * 3f + NetworkObjectId) * 0.15f : 0f;
                var lp = Rig.transform.localPosition;
                Rig.transform.localPosition = new Vector3(0f, Mathf.MoveTowards(lp.y, hover, Time.deltaTime * 4f), 0f);
                float flap = Woken.Value && !IsDead ? Mathf.Sin(Time.time * (diving ? 4f : 11f)) * 35f : 0f;
                if (wingL != null) wingL.localRotation = Quaternion.Euler(0f, -20f - flap, 25f);
                if (wingR != null) wingR.localRotation = Quaternion.Euler(0f, 20f + flap, -25f);
                if (!Woken.Value && Rig.GlowRenderers != null)
                    foreach (var g in Rig.GlowRenderers) if (g != null) ArtLibrary.SetGlow(g, new Color(0.3f, 0.05f, 0.03f));
            }

            if (!IsServer || IsDead) { if (!IsServer) base.Update(); return; }

            if (!Woken.Value)
            {
                agent.isStopped = true;
                foreach (var p in PlayerLife.All)
                    if (p != null && p.Targetable && Vector3.Distance(p.transform.position, transform.position) < wakeRange) { Wake(); break; }
                return;
            }

            if (diving) { StepDive(); return; }
            if (diveAt > 0f)
            {
                agent.isStopped = true;
                transform.rotation = Quaternion.LookRotation(diveDir);
                if (Time.time >= diveAt) { diveAt = -1f; diving = true; dived = 0f; struck.Clear(); DiveFxRpc(); }
                return;
            }
            if (Time.time < groundedUntil) { agent.isStopped = true; return; }
            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (Time.time < nextDive || distanceToTarget < 3f || distanceToTarget > 16f) return false;
            if (!CombatQuery.ClearLine(transform.position + Vector3.up * 1.5f, target)) return false;
            nextDive = Time.time + diveCooldown;
            diveDir = target.transform.position - transform.position;
            diveDir.y = 0f;
            diveDir.Normalize();
            diveAt = Time.time + diveWindup;
            DiveWarnRpc(transform.position, diveDir, Mathf.Min(diveLength, distanceToTarget + 3f));
            return true;
        }

        private void StepDive()
        {
            float step = diveSpeed * Time.deltaTime;
            var before = transform.position;
            agent.Move(diveDir * step);
            float moved = Vector3.Distance(before, transform.position);
            dived += moved;
            foreach (var victim in CombatQuery.Circle(transform.position + Vector3.up * 0.8f, 1.5f, Team.Players))
            {
                if (!struck.Add(victim)) continue;
                victim.ServerDamage(damage * 1.4f, transform.position - diveDir, NetworkObjectId);
                if (victim.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(diveDir * 8f + Vector3.up * 4f);
            }
            if (moved < step * 0.4f || dived >= diveLength)
            {
                diving = false;
                groundedUntil = Time.time + 1.6f;
                LandRpc();
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WakeRpc()
        {
            Sfx.Play(Sound.Slam, transform.position + Vector3.up, 0.8f, 1.4f);
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 1.5f, 0.8f, 1.6f);
            Vfx.Smoke(transform.position, 3, 0.6f, 1.2f);
            BoneBurst.Spawn(transform.position + Vector3.up * 0.8f, 0.8f, false, Mat.StoneWall);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.4f, "IT LIVES", new Color(0.9f, 0.4f, 0.3f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DiveWarnRpc(Vector3 from, Vector3 dir, float length)
        {
            Telegraph.ShowLine(from, dir, length, 2f, diveWindup, null);
            Sfx.Play(Sound.Whisper, from + Vector3.up * 1.5f, 1f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DiveFxRpc() => Sfx.Play(Sound.Swing, transform.position + Vector3.up, 1f, 0.5f);

        [Rpc(SendTo.ClientsAndHost)]
        private void LandRpc()
        {
            Sfx.Play(Sound.Slam, transform.position, 0.9f, 0.9f);
            Vfx.Smoke(transform.position, 3, 0.8f, 1.2f);
            Vfx.Ring(transform.position, 2f, new Color(0.8f, 0.75f, 0.7f), 0.35f);
        }
    }
}
