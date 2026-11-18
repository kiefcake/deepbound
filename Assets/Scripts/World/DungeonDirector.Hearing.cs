using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// HEARING (2026-11-16, after the audio pass; Dark and Darker's rule): the dead hear you run and fight. Walking is
    /// quiet, so creeping past a room is a choice; sprinting carries <see cref="SprintNoise"/> m and a fight (a blow struck
    /// or taken) <see cref="FightNoise"/> m, and stone between halves it. An idle enemy that hears goes to look where
    /// the noise came from (<see cref="EnemyBrain.ServerInvestigate"/>), a little off the mark, and sees you if you are
    /// still there. A hidden Rogue makes no noise. Server.
    /// </summary>
    public partial class DungeonDirector
    {
        public const float SprintNoise = 11f, FightNoise = 16f, WallDamp = 0.55f, HearEvery = 0.25f, SprintHeard = 6f, InvestigateSeconds = 7f;

        private float nextHear;
        private readonly Dictionary<PlayerLife, Vector3> heardFrom = new Dictionary<PlayerLife, Vector3>();
        private static readonly Dictionary<ulong, float> struckAt = new Dictionary<ulong, float>();
        private static bool hearingHooked;
        /// <summary>Tests: noises that sent someone to look, this session.</summary>
        public int NoisesHeard { get; private set; }

        /// <summary>How far this player is heard right now (0: silent).</summary>
        public static float NoiseOf(PlayerLife p, float speed)
        {
            if (p == null || !p.Targetable || p.Hidden.Value || p.Downed.Value) return 0f;
            var hp = p.Health;
            bool fighting = Time.time - hp.LastHurtTime < 0.6f || (struckAt.TryGetValue(p.NetworkObjectId, out float s) && Time.time - s < 0.6f);
            if (fighting) return FightNoise;
            return speed > SprintHeard ? SprintNoise : 0f;
        }

        private void TickHearing()
        {
            if (!hearingHooked)
            {
                hearingHooked = true;
                Health.ServerAnyDamaged += (h, lost, from) => { if (h != null && h.Team != Team.Players && from != ulong.MaxValue) struckAt[from] = Time.time; };
            }
            if (Time.time < nextHear || InLobby || Layout == null) return;
            float dt = HearEvery + (Time.time - nextHear);
            nextHear = Time.time + HearEvery;
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var at = p.transform.position;
                float speed = heardFrom.TryGetValue(p, out var was) ? Vector3.Distance(new Vector3(at.x, 0f, at.z), new Vector3(was.x, 0f, was.z)) / Mathf.Max(0.05f, dt) : 0f;
                heardFrom[p] = at;
                // A teleport or a lift is not a run.
                if (speed > 14f) continue;
                float r = NoiseOf(p, speed);
                if (r <= 0f) continue;
                foreach (var e in EnemyBrain.All)
                {
                    if (e == null || e.IsDead || e.HasTarget || e is BossBrain) continue;
                    float d = Vector3.Distance(e.transform.position, at);
                    if (d > r) continue;
                    if (d > r * WallDamp && Art.Sfx.Occluded(e.transform.position + Vector3.up * 1.5f, at)) continue;
                    // They know roughly where, not exactly.
                    var guess = at + new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
                    if (UnityEngine.AI.NavMesh.SamplePosition(guess, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) guess = hit.position;
                    if (e.ServerInvestigate(guess, InvestigateSeconds)) NoisesHeard++;
                }
            }
        }
    }
}
