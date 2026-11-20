using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    // =====================================================================================================================
    // THE OSSUARY - MOTHER MARROW, who knits the dead
    // =====================================================================================================================

    /// <summary>
    /// Mother Marrow: a crone who knits bone. The sequence:
    /// <list type="number">
    /// <item><b>The Lash</b>: she whips bone lashes down lanes at each of you (red lanes, then a crack), and rains marrow
    /// in circles round whoever keeps away.</item>
    /// <item><b>The Cocoon</b> (at 70% and 40%): she shuts herself in a cage of bone and her knitters climb out of the floor;
    /// the cage stands while any knitter lives (one more each time). The threads between them and her show which.</item>
    /// <item><b>Unknit</b> (at 15%): the cage falls apart for good - she goes into a fury, lashing twice as often.</item>
    /// </list>
    /// </summary>
    public class MotherMarrowBrain : LairBrain
    {
        public override string DisplayName => "Mother Marrow";
        public override string Epithet => "Who Knits the Dead";
        public override string LastWords => "Unravel... me... gently...";
        public override string Hint => "Kill her knitters to break the cocoon.";
        protected override float VisualScale => 1.55f;
        protected override float[] PhaseGates => new[] { 0.7f, 0.4f, 0.15f };
        protected override Color ShockwaveColor => new Color(0.95f, 0.88f, 0.7f);
        public static readonly Color BoneLight = new Color(0.95f, 0.88f, 0.7f);

        private float nextLash, nextRain;
        private bool unknit;
        private int knitters = 1;
        private struct Lash { public Vector3 From, To; public float When; }
        private readonly List<Lash> lashes = new List<Lash>();
        private struct Fall { public Vector3 At; public float When; }
        private readonly List<Fall> falls = new List<Fall>();
        /// <summary>For tests.</summary>
        public int LashesCracked { get; private set; }

        protected override void OnEngaged() { nextLash = Time.time + 2.5f; nextRain = Time.time + 6f; }

        protected override void OnPhaseGate(int index)
        {
            if (index < 2)
            {
                knitters++;
                var k = Summon(knitters + scaledPlayers - 1, 5f, 1.4f);
                InvulnerableWhile("The Cocoon", index == 0 ? "Hush now. Mother is knitting." : "More hands. MORE HANDS.", "Her knitters hold the cage", k, 22f,
                    () => Taunt("You pulled a stitch. Rude."));
            }
            else
                Transition("Unknit", "Then I will knit you instead!", 1.5f, () => { unknit = true; ServerEnrage(9999f); nextLash = Time.time + 0.8f; });
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (Time.time >= nextLash)
            {
                nextLash = Time.time + (unknit ? 3.2f : 5.5f) / Cadence;
                float tell = Tell(0.9f);
                PlayActionRpc(RigAction.WindSweep, tell);
                castUntil = busyUntil = Time.time + tell + 0.2f;
                var from = transform.position + Vector3.up * 0.2f;
                foreach (var p in Standing())
                {
                    var to = PredictedFeet(p, tell);
                    var dir = to - from; dir.y = 0f;
                    if (dir.sqrMagnitude < 0.5f) dir = transform.forward;
                    float len = Mathf.Min(16f, dir.magnitude + 4f);
                    FreeLineTelegraphRpc(from, dir.normalized, len, 1.2f, tell);
                    lashes.Add(new Lash { From = from, To = from + dir.normalized * len, When = Time.time + tell });
                }
                return true;
            }
            if (Time.time >= nextRain && distanceToTarget > 5f)
            {
                nextRain = Time.time + (unknit ? 4.5f : 7f) / Cadence;
                PlayActionRpc(RigAction.Cast, 0.8f);
                foreach (var p in Standing())
                    for (int i = 0; i < 3; i++)
                    {
                        var at = i == 0 ? PredictedFeet(p, 1.3f) : p.transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(1.5f, 3.5f);
                        TintedCircleRpc(at, 1.6f, Tell(1.3f), BoneLight);
                        falls.Add(new Fall { At = at, When = Time.time + Tell(1.3f) });
                    }
                return true;
            }
            return false;
        }

        protected override void Update()
        {
            if (IsServer && !IsDead && !IsEcho)
            {
                for (int i = lashes.Count - 1; i >= 0; i--)
                {
                    if (Time.time < lashes[i].When) continue;
                    LaneHit(lashes[i].From, lashes[i].To, 0.6f, Signature(damage * 1.1f));
                    LashFxRpc(lashes[i].From, lashes[i].To);
                    LashesCracked++;
                    lashes.RemoveAt(i);
                }
                for (int i = falls.Count - 1; i >= 0; i--)
                {
                    if (Time.time < falls[i].When) continue;
                    foreach (var v in CombatQuery.Circle(falls[i].At, 1.6f, Team.Players)) v.ServerDamage(Signature(damage * 0.9f), falls[i].At + Vector3.up, NetworkObjectId);
                    FallFxRpc(falls[i].At);
                    falls.RemoveAt(i);
                }
            }
            base.Update();
            UpdateCage();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LashFxRpc(Vector3 from, Vector3 to)
        {
            Sfx.Play(Sound.BoneBreak, from, 0.9f, 0.8f);
            for (int i = 0; i <= 8; i++) Vfx.Embers(Vector3.Lerp(from, to, i / 8f) + Vector3.up * 0.3f, 2, BoneLight, 2f, 0.06f, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FallFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.BoneBreak, at, 0.8f, 1.1f);
            BoneBurst.Spawn(at + Vector3.up * 0.2f, 0.6f, false, Mat.Bone);
        }

        // The cocoon: ribs of bone closing round her while she is warded (any peer, from the stance).
        private Transform cage;
        private void UpdateCage()
        {
            bool on = CurrentStance == Stance.Invulnerable && !IsDead;
            if (on && cage == null)
            {
                cage = new GameObject("BoneCocoon").transform;
                cage.SetParent(transform, false);
                for (int i = 0; i < 10; i++)
                {
                    var rib = MeshKit.Part(cage, "Rib", MeshKit.Box(new Vector3(0.12f, 3.6f, 0.12f)), Mat.Bone, Quaternion.Euler(0f, i * 36f, 0f) * Vector3.forward * 1.25f + Vector3.up * 1.6f,
                        new Vector3(0f, i * 36f, 0f) + new Vector3(-12f, 0f, 0f));
                    rib.transform.localRotation = Quaternion.Euler(0f, i * 36f, 0f) * Quaternion.Euler(-14f, 0f, 0f);
                }
            }
            if (cage != null && cage.gameObject.activeSelf != on)
            {
                cage.gameObject.SetActive(on);
                BoneBurst.Spawn(transform.position + Vector3.up * 1.4f, 1f, false, Mat.Bone);
            }
        }

        protected override void DressRig(HumanoidRig rig)
        {
            if (rig.Torso == null || rig.Head == null) return;
            // A shawl of knitted vertebrae, and long bone needles in her hands.
            for (int i = 0; i < 12; i++)
            {
                float a = (-75f + i * 13.6f) * Mathf.Deg2Rad;
                MeshKit.Part(rig.Torso, "Vertebra", MeshKit.Box(new Vector3(0.07f, 0.05f, 0.05f)), Mat.Bone, new Vector3(Mathf.Sin(a) * 0.26f, 0.5f - Mathf.Abs(Mathf.Sin(a)) * 0.08f, Mathf.Cos(a) * 0.2f));
            }
            foreach (var arm in new[] { rig.ArmL, rig.ArmR })
                if (arm != null) MeshKit.Part(arm, "Needle", MeshKit.Cylinder(0.02f, 0f, 0.9f, 5), Mat.Bone, new Vector3(0f, -0.75f, 0.12f), new Vector3(70f, 0f, 0f));
            GlowPart(MeshKit.Part(rig.Head, "Eyes", MeshKit.Box(new Vector3(0.14f, 0.025f, 0.01f)), Mat.Glow, new Vector3(0f, 0.08f, 0.15f)), BoneLight * 1.6f);
            // 2026-11-20 visual pass (a keeper should not read as a thin hag): a hump of woven bone, a headdress of
            // finger bones, and a cradle of small skulls in her arms.
            GuardianKit.Marrow(rig, BoneLight);
        }
    }
}
