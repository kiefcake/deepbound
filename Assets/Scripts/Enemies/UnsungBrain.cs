using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Unsung: what the Cardinal's hymn was sung to keep asleep, at the bottom of the Abyss
    /// (reached only with all three Seals). A colossal floating ribcage under a three-eyed skull,
    /// ringed with orbiting bone. It fights in patterns, RotMG-style:
    ///
    /// <list type="bullet">
    /// <item><b>The Dirge</b>: a ring of slow shadow bolts with a gap in it. Find the gap.</item>
    /// <item><b>Grasping Dark</b>: a circle opens under every player; step out before it closes.</item>
    /// <item><b>The Toll</b>: a jumpable shockwave.</item>
    /// <item>70%: <b>The Seal Wardens</b>: two Mourners rise; it is warded until they fall. Someone
    ///   must keep watching them while the others break them.</item>
    /// <item>40%: <b>The Silence</b>: the light fails and it pours out a five-armed spiral. Survive.</item>
    /// <item>15%: <b>The Last Morning</b>: everything faster, rings thicker.</item>
    /// </list>
    /// </summary>
    public class UnsungBrain : BossBrain
    {
        public override string DisplayName => "The Unsung";
        public override string Epithet => "That Which the Hymn Was For";
        public override string LastWords => "So this... is what a song is for.";
        public override Progression.Deeds.Deed Deed => Progression.Deeds.Deed.Unsung;
        public override string Hint => "Slip through the gaps in its rings, step out of the grasping dark, and break its wardens while someone keeps watching them.";

        private const float BoltSpeed = 7f, SpiralSpeed = 7.5f;
        private float nextRing, nextGrasp, nextSpiral, spiralAngle;
        private bool lastMorning;

        private struct Grasp { public Vector3 At; public float When; }
        private readonly List<Grasp> grasps = new List<Grasp>();

        protected override float ShockwaveCooldown => lastMorning ? 8f : 12f;
        protected override Color ShockwaveColor => new Color(0.6f, 0.35f, 1f);
        protected override float[] PhaseGates => new[] { 0.7f, 0.4f, 0.15f };

        private Transform ringA, ringB;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextRing = Time.time + 3f;
            nextGrasp = Time.time + 7f;
            if (Rig != null)
            {
                ringA = Rig.transform.Find("BoneRingA");
                ringB = Rig.transform.Find("BoneRingB");
            }
        }

        protected override void OnPhaseGate(int index)
        {
            switch (index)
            {
                case 0:
                    var wardens = guardPrefab != null ? SummonOf(guardPrefab, 2, 6f, 1.6f) : new List<EnemyBrain>();
                    InvulnerableWhile("The Seal Wardens", "You broke my seals. Then meet the ones who kept them.",
                        "Its Mourners ward it: keep one player watching them while the others break them", wardens, 28f,
                        () => Taunt("No more keepers. No more song."));
                    break;
                case 1:
                    Survive("The Silence", "Hear it? Neither do I. Not one note.", "The light fails: weave through the spiral until it passes", 14f,
                        () => Taunt("Still here. Still small."));
                    spiralAngle = Random.Range(0f, 360f);
                    nextSpiral = Time.time + 0.8f;
                    grasps.Clear();
                    SilenceRpc(14f);
                    break;
                default:
                    Transition("The Last Morning", "The morning they waited for. It is mine.", 2.6f, () => lastMorning = true);
                    break;
            }
        }

        protected override void Update()
        {
            // Everyone: it floats, and its bone rings turn.
            if (Rig != null)
            {
                var lp = Rig.transform.localPosition;
                Rig.transform.localPosition = new Vector3(lp.x, 0.6f + Mathf.Sin(Time.time * 1.1f) * 0.25f, lp.z);
                if (ringA != null) ringA.localRotation = Quaternion.Euler(20f, Time.time * 40f, 0f);
                if (ringB != null) ringB.localRotation = Quaternion.Euler(-25f, -Time.time * 28f, 10f);
            }
            if (IsServer && !IsDead && !Dormant)
            {
                ResolveGrasps();
                TickSpiral();
            }
            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (TryShockwave()) return true;
            if (Time.time >= nextRing)
            {
                nextRing = Time.time + (lastMorning ? 2.6f : 4.2f);
                FireRing();
                castUntil = Time.time + 0.6f;
                busyUntil = castUntil;
                return true;
            }
            if (Time.time >= nextGrasp)
            {
                nextGrasp = Time.time + (lastMorning ? 6.5f : 9f);
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    var at = p.transform.position;
                    grasps.Add(new Grasp { At = at, When = Time.time + 1.5f });
                    GraspWarnRpc(at, 2.6f, 1.5f);
                }
                PlayActionRpc(RigAction.Cast, 0.9f);
                castUntil = Time.time + 0.9f;
                busyUntil = castUntil;
                return true;
            }
            return false;
        }

        /// <summary>The Dirge: a full ring of bolts with a three-bolt gap, aimed so the gap is somewhere new.</summary>
        private void FireRing()
        {
            int count = lastMorning ? 24 : 18;
            int gapAt = Random.Range(0, count);
            var origin = transform.position + Vector3.up * 1.3f;
            var dirs = new List<Vector3>();
            float offset = Random.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                int d = (i - gapAt + count) % count;
                if (d < 3) continue; // the gap
                var dir = Quaternion.Euler(0f, offset + i * 360f / count, 0f) * Vector3.forward;
                dirs.Add(dir);
                Arrows.ServerFire(origin, dir, damage * 0.55f, Team.Players, NetworkObjectId, BoltSpeed * (lastMorning ? 1.25f : 1f));
            }
            PlayActionRpc(RigAction.Cast, 0.6f);
            BoltsRpc(origin, dirs.ToArray(), BoltSpeed * (lastMorning ? 1.25f : 1f));
        }

        private void TickSpiral()
        {
            if (!InSurvival || Time.time < nextSpiral) return;
            nextSpiral = Time.time + 0.12f;
            const int arms = 5;
            spiralAngle += 11f;
            var origin = transform.position + Vector3.up * 1.2f;
            var dirs = new Vector3[arms];
            for (int i = 0; i < arms; i++)
            {
                dirs[i] = Quaternion.Euler(0f, spiralAngle + i * 360f / arms, 0f) * Vector3.forward;
                Arrows.ServerFire(origin, dirs[i], damage * 0.4f, Team.Players, NetworkObjectId, SpiralSpeed);
            }
            BoltsRpc(origin, dirs, SpiralSpeed);
        }

        private void ResolveGrasps()
        {
            for (int i = grasps.Count - 1; i >= 0; i--)
            {
                if (Time.time < grasps[i].When) continue;
                var at = grasps[i].At;
                grasps.RemoveAt(i);
                foreach (var v in CombatQuery.Circle(at, 2.6f, Team.Players))
                    v.ServerDamage(damage * 1.3f, at + Vector3.up, NetworkObjectId);
                GraspFxRpc(at);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltsRpc(Vector3 origin, Vector3[] dirs, float speed)
        {
            foreach (var d in dirs) Arrows.SpawnVisual(origin, d, ProjectileKind.ShadowBolt, speed);
            Sfx.Play(Sound.Whisper, origin, 0.7f, 0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GraspWarnRpc(Vector3 at, float radius, float seconds) =>
            Telegraph.Show(at, radius, seconds, null, new Color(0.55f, 0.25f, 1f, 0.4f));

        [Rpc(SendTo.ClientsAndHost)]
        private void GraspFxRpc(Vector3 at)
        {
            var c = new Color(0.55f, 0.3f, 1f);
            Sfx.Play(Sound.Slam, at, 0.8f, 0.7f);
            Vfx.Flash(at + Vector3.up, c, 5f, 7f, 0.4f);
            Vfx.Ring(at, 2.6f, c, 0.4f);
            for (int i = 0; i < 5; i++) Vfx.Embers(at + Random.insideUnitSphere * 1.5f, 4, c, 3f, 0.06f, 0.8f);
        }

        /// <summary>Every peer: the light fails for a while (fog thickens, the ambient drops).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void SilenceRpc(float seconds) => Art.TheSilence.Begin(seconds);
    }
}
