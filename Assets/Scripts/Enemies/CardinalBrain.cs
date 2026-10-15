using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Pale Cardinal: floor 4's boss, in the Black Cathedral. A towering prelate in
    /// crimson and gold who fights with the cathedral itself.
    ///
    /// <list type="bullet">
    /// <item><b>Toll of the Bell</b> — a golden shockwave rolls out across the nave. Jump
    ///   it or dodge through it.</item>
    /// <item><b>Judgement</b> — circles of holy fire open under and around every player;
    ///   when they fill, pillars of flame erupt. Keep moving.</item>
    /// <item><b>Choir of the Damned</b> — at two thirds and one third health, Wraiths answer
    ///   the hymn. They are Ethereal: pin them down (chill, stun, mark, consecrate).</item>
    /// <item><b>Last Rites</b> — below a third, everything comes faster.</item>
    /// </list>
    /// </summary>
    public class CardinalBrain : BossBrain
    {
        [SerializeField] private float judgementCooldown = 7.5f, judgementWindup = 1.5f, judgementRadius = 2.4f, judgementDamage = 38f;

        public override string DisplayName => "The Pale Cardinal";
        public override string Epithet => "Last Voice of the Black Cathedral";
        public override string Hint => "Step out of the burning circles, jump the bell's toll, and silence the choir first.";

        private float nextJudgement;
        private bool choir66, choir33;

        private struct Pillar { public Vector3 At; public float When; }
        private readonly List<Pillar> pillars = new List<Pillar>();

        private bool LastRites => health.Fraction < 0.34f;
        protected override float ShockwaveCooldown => LastRites ? 7f : 10f;
        protected override Color ShockwaveColor => new Color(1f, 0.8f, 0.35f);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextJudgement = Time.time + 5f;
        }

        protected override void Update()
        {
            if (IsServer && !IsDead) ResolvePillars();
            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (!choir66 && health.Fraction < 0.66f) { choir66 = true; Summon(1 + scaledPlayers, 5f, 1.8f); return true; }
            if (!choir33 && health.Fraction < 0.33f) { choir33 = true; Summon(2 + scaledPlayers, 5f, 1.8f); return true; }
            if (TryShockwave()) return true;

            if (Time.time >= nextJudgement)
            {
                nextJudgement = Time.time + judgementCooldown * (LastRites ? 0.65f : 1f);
                castUntil = Time.time + 0.9f;
                busyUntil = castUntil;
                PlayActionRpc(RigAction.Cast, 0.9f);
                Debug.Log("[Boss] Cardinal judgement");
                // One circle under each player, and a few scattered near them.
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    AddPillar(p.transform.position);
                    for (int i = 0; i < (LastRites ? 3 : 2); i++)
                        AddPillar(p.transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(2.5f, 5f));
                }
                return true;
            }
            return false;
        }

        private void AddPillar(Vector3 at)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            pillars.Add(new Pillar { At = at, When = Time.time + judgementWindup });
            JudgementWarnRpc(at, judgementRadius, judgementWindup);
        }

        private void ResolvePillars()
        {
            for (int i = pillars.Count - 1; i >= 0; i--)
            {
                if (Time.time < pillars[i].When) continue;
                var at = pillars[i].At;
                pillars.RemoveAt(i);
                foreach (var victim in CombatQuery.Circle(at, judgementRadius, Team.Players))
                    victim.ServerDamage(judgementDamage * Difficulty.Level.Damage, at + Vector3.up, NetworkObjectId);
                PillarFxRpc(at);
                Debug.Log("[Boss] Cardinal pillar erupted");
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void JudgementWarnRpc(Vector3 at, float radius, float seconds) =>
            Telegraph.Show(at, radius, seconds, null, new Color(1f, 0.75f, 0.3f, 0.4f));

        [Rpc(SendTo.ClientsAndHost)]
        private void PillarFxRpc(Vector3 at)
        {
            var holy = new Color(1f, 0.75f, 0.35f);
            Sfx.Play(Sound.Fire, at + Vector3.up, 0.8f, 1.2f);
            Vfx.Flash(at + Vector3.up * 2f, holy, 6f, 8f, 0.5f);
            for (int i = 0; i < 6; i++)
                Vfx.Fireball(at + Vector3.up * (0.5f + i * 0.7f), 1.4f - i * 0.12f, holy);
            Vfx.Embers(at, 14, holy, 5f, 0.06f, 0.8f);
        }
    }
}
