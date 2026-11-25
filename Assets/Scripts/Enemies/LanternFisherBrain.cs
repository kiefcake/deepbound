using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    public class LanternFisherBrain : LairBrain
    {
        public override string DisplayName => "The Lantern Fisher";
        public override string Epithet => "That Which Waits Under the Stair";
        public override string LastWords => "Such... a pretty... light...";
        public override string Hint => "Never walk to the light.";
        protected override float VisualScale => 1.7f;
        protected override float[] PhaseGates => new[] { 0.5f };
        protected override Color ShockwaveColor => new Color(0.45f, 0.85f, 0.75f);
        public static readonly Color LureColor = new Color(0.55f, 1f, 0.75f);

        public readonly NetworkVariable<bool> Submerged = new NetworkVariable<bool>();
        public readonly NetworkVariable<Vector3> LurePos = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<ulong> Hooked = new NetworkVariable<ulong>(ulong.MaxValue);

        private enum Stage { Lure, Surfaced, Reel }
        private Stage stage = Stage.Surfaced;
        private float stageEnds, nextBite, biteAt = -1f;
        private Vector3 biteSpot;
        private bool surfaceWarned;

        public const float BiteRadius = 1.9f, BiteTell = 0.45f, SurfaceTell = 1f, SurfaceRadius = 2.4f;
        private PlayerLife lureTarget;
        private float reelHp;
        private bool deep;

        public int Bites { get; private set; }
        public int LoopsDone { get; private set; }

        protected override void OnEngaged() { BeginLure(); }

        protected override void OnPhaseGate(int index)
        {
            deep = true;
            Transition("The Black Water", "Come... closer...", 1.4f, () => Summon(scaledPlayers + 1, 4f, 0.8f));
        }

        private void BeginLure()
        {
            stage = Stage.Lure;
            stageEnds = Time.time + (deep ? 6f : 8f);
            biteAt = -1f;
            surfaceWarned = false;
            lureTarget = NearestStanding(transform.position);
            Submerged.Value = true;
            LurePos.Value = transform.position + Vector3.up * 1.6f;
            Survive("The Lure", "", "", stageEnds - Time.time);
            SplashFxRpc(transform.position);
            if (agent != null && agent.enabled) agent.isStopped = true;
        }

        public void ServerDebugSurface() { if (IsServer && Submerged.Value) Surface(); }

        private void Surface()
        {
            var at = LurePos.Value; at.y = transform.position.y;
            Warp(UnityEngine.AI.NavMesh.SamplePosition(at, out var h, 3f, UnityEngine.AI.NavMesh.AllAreas) ? h.position : at);
            Submerged.Value = false;
            EndSurvival();
            stage = Stage.Surfaced;
            stageEnds = Time.time + 6.5f;
            SplashFxRpc(transform.position);
            ImpactFxRpc(transform.position, 12f);
            biteAt = -1f;
            foreach (var v in CombatQuery.Circle(transform.position, SurfaceRadius, Team.Players)) v.ServerDamage(Signature(damage * 1.2f), transform.position, NetworkObjectId);
            if (agent != null && agent.enabled) agent.isStopped = false;
            nextSpecial = Time.time + 2.5f;
            LoopsDone++;
        }

        private void BeginReel()
        {
            var victim = NearestStanding(transform.position);
            if (victim == null) { BeginLure(); return; }
            stage = Stage.Reel;
            stageEnds = Time.time + 4f;
            Hooked.Value = victim.NetworkObjectId;
            reelHp = health.Current.Value - health.Max.Value * (deep ? 0.06f : 0.05f);
            castUntil = busyUntil = stageEnds;
            PlayActionRpc(RigAction.Channel, 4f);
            Taunt(StandingParty > 1 ? "One for the pot." : "Hold still, little fish.");
        }

        private float nextSpecial;

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (stage != Stage.Surfaced || Time.time < nextSpecial || target == null) return false;
            nextSpecial = Time.time + 3.5f / Cadence;

            var origin = transform.position + Vector3.up * 2f;
            var dir = AimDir(origin, 12f);
            PlayActionRpc(RigAction.Hurl, 0.6f);
            for (int i = -2; i <= 2; i++)
            {
                var d = Quaternion.AngleAxis(i * 8f, Vector3.up) * dir;
                Arrows.ServerFire(origin, d, damage * 0.5f, Team.Players, NetworkObjectId, 12f, null, false, 22f);
                InkFxRpc(origin, d);
            }
            SprayFxRpc(origin);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SprayFxRpc(Vector3 at) => Sfx.Play(Sound.Slime, at, 0.9f, 0.8f);

        [Rpc(SendTo.ClientsAndHost)]
        private void BiteWarnRpc(Vector3 at, float radius, float seconds)
        {
            Telegraph.Show(at, radius, seconds, null, Telegraph.Harm);
            Sfx.Play(Sound.Splash, at + Vector3.up, 0.8f, 1.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SurfaceWarnRpc(Vector3 at, float radius, float seconds)
        {
            Telegraph.Show(at, radius, seconds, null, Telegraph.Harm);
            Sfx.Play(Sound.Splash, at + Vector3.up, 1f, 0.45f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void InkFxRpc(Vector3 origin, Vector3 dir) => Arrows.SpawnVisual(origin, dir, ProjectileKind.Spit, 12f, 22f);

        protected override void Update()
        {
            if (IsServer && !IsDead && Engaged.Value) TickStages();
            base.Update();
            UpdateLook();
        }

        protected override bool CanDash => base.CanDash && !Submerged.Value;

        private void TickStages()
        {
            switch (stage)
            {
                case Stage.Lure:
                {
                    if (lureTarget == null || !lureTarget.Targetable) lureTarget = NearestStanding(LurePos.Value);
                    var lp = LurePos.Value;
                    bool surfacing = Time.time >= stageEnds - SurfaceTell;
                    if (surfacing && !surfaceWarned)
                    {
                        surfaceWarned = true;
                        SurfaceWarnRpc(new Vector3(lp.x, transform.position.y, lp.z), SurfaceRadius, Mathf.Max(0.1f, stageEnds - Time.time));
                    }
                    if (lureTarget != null && !surfacing)
                    {
                        var goal = lureTarget.transform.position + Vector3.up * 1.6f;
                        lp = Vector3.MoveTowards(lp, goal, (deep ? 3.6f : 2.8f) * Time.deltaTime);
                        var c = RoomCenter; float r = RoomRadius;
                        lp.x = Mathf.Clamp(lp.x, c.x - r, c.x + r); lp.z = Mathf.Clamp(lp.z, c.z - r, c.z + r);
                        LurePos.Value = lp;
                    }
                    if (biteAt > 0f && Time.time >= biteAt)
                    {
                        biteAt = -1f;
                        nextBite = Time.time + 1.2f;
                        foreach (var p in Standing())
                        {
                            var flat = p.transform.position - biteSpot; flat.y = 0f;
                            if (flat.magnitude > BiteRadius) continue;
                            p.Health.ServerDamage(Signature(damage * 1.4f), biteSpot, NetworkObjectId);
                            Bites++;
                            BiteFxRpc(new Vector3(biteSpot.x, p.transform.position.y, biteSpot.z));
                            break;
                        }
                    }
                    else if (biteAt < 0f && Time.time >= nextBite && !surfacing)
                        foreach (var p in Standing())
                        {
                            var flat = p.transform.position - lp; flat.y = 0f;
                            if (flat.magnitude > BiteRadius) continue;
                            biteAt = Time.time + BiteTell;
                            biteSpot = lp;
                            BiteWarnRpc(new Vector3(lp.x, p.transform.position.y, lp.z), BiteRadius, BiteTell);
                            break;
                        }
                    if (Time.time >= stageEnds) Surface();
                    break;
                }
                case Stage.Surfaced:
                    if (Time.time >= stageEnds) BeginReel();
                    break;
                case Stage.Reel:
                {
                    PlayerLife hooked = null;
                    foreach (var p in Standing()) if (p.NetworkObjectId == Hooked.Value) hooked = p;
                    bool broken = health.Current.Value <= reelHp;
                    if (hooked == null || broken || Time.time >= stageEnds)
                    {
                        if (hooked != null && !broken)
                        {
                            hooked.Health.ServerDamage(Signature(damage * 1.6f), transform.position, NetworkObjectId);
                            BiteFxRpc(hooked.transform.position);
                            Bites++;
                        }
                        else if (broken) { Taunt("The line! My line!"); ServerAddStagger(1f); }
                        Hooked.Value = ulong.MaxValue;
                        castUntil = busyUntil = 0f;
                        BeginLure();
                        break;
                    }
                    var pull = transform.position - hooked.transform.position; pull.y = 0f;
                    if (pull.magnitude > 2.2f && Time.frameCount % 6 == 0) KnockPlayer(hooked.Health, pull.normalized * (deep ? 7.5f : 6f));
                    break;
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SplashFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Splash, at, 1f, 0.6f);
            Vfx.Ring(at + Vector3.up * 0.05f, 3.5f, new Color(0.4f, 0.7f, 0.65f), 0.6f);
            Vfx.Smoke(at, 3, 1.2f, 1.4f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BiteFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 1f, 0.7f);
            BoneBurst.Spawn(at + Vector3.up * 0.3f, 0.8f, false, Mat.Bone);
            Vfx.Embers(at + Vector3.up * 0.4f, 10, LureColor, 3f, 0.05f, 0.5f);
        }

        private Transform lure, line;
        private Light lureLight;
        private void UpdateLook()
        {
            if (Rig != null && Rig.gameObject.activeSelf == Submerged.Value) Rig.gameObject.SetActive(!Submerged.Value);
            bool showLure = Submerged.Value && !IsDead;
            if (showLure && lure == null)
            {
                lure = MeshKit.Part(null, "Lure", MeshKit.Ball(0.18f, 10, 8), Mat.Glow, Vector3.zero).transform;
                GlowPart(lure.gameObject, LureColor * 2.2f);
                lureLight = lure.gameObject.AddComponent<Light>();
                lureLight.type = LightType.Point; lureLight.color = LureColor; lureLight.range = 7f; lureLight.intensity = 3f; lureLight.shadows = LightShadows.None;
            }
            if (lure != null)
            {
                lure.gameObject.SetActive(showLure);
                if (showLure) lure.position = LurePos.Value + Vector3.up * 0.15f * Mathf.Sin(Time.time * 2.4f);
            }
            PlayerLife hooked = null;
            if (Hooked.Value != ulong.MaxValue) foreach (var p in PlayerLife.All) if (p != null && p.NetworkObjectId == Hooked.Value) hooked = p;
            if (hooked != null && !IsDead)
            {
                if (line == null) { line = MeshKit.Part(null, "ReelLine", MeshKit.Box(Vector3.one), Mat.Glow, Vector3.zero).transform; GlowPart(line.gameObject, LureColor * 1.2f); }
                var a = transform.position + Vector3.up * 2.2f; var b = hooked.transform.position + Vector3.up * 1.1f;
                line.gameObject.SetActive(true);
                line.position = (a + b) / 2f; line.rotation = Quaternion.LookRotation(b - a); line.localScale = new Vector3(0.035f, 0.035f, Vector3.Distance(a, b));
            }
            else if (line != null) line.gameObject.SetActive(false);
        }

        public override void OnNetworkDespawn()
        {
            if (lure != null) Destroy(lure.gameObject);
            if (line != null) Destroy(line.gameObject);
            base.OnNetworkDespawn();
        }

        protected override void DressRig(HumanoidRig rig)
        {
            if (rig.Head == null) return;

            var stalk = MeshKit.Pivot(rig.Head, "Stalk", new Vector3(0f, 0.2f, 0.05f));
            for (int i = 0; i < 6; i++)
                MeshKit.Part(stalk, "Seg", MeshKit.Cylinder(0.04f - i * 0.004f, 0.035f - i * 0.004f, 0.22f, 6), Mat.Bone, new Vector3(0f, 0.1f + i * 0.17f, 0.04f + i * i * 0.025f), new Vector3(10f + i * 14f, 0f, 0f));
            GlowPart(MeshKit.Part(stalk, "Lantern", MeshKit.Ball(0.1f, 8, 6), Mat.Glow, new Vector3(0f, 1.05f, 0.95f)), LureColor * 1.6f);
            for (int i = 0; i < 9; i++)
                MeshKit.Part(rig.Head, "Tooth", MeshKit.Cylinder(0.018f, 0f, 0.11f, 4), Mat.Bone, new Vector3(-0.12f + i * 0.03f, -0.08f, 0.16f), new Vector3(180f, 0f, 0f));

            GuardianKit.Fisher(rig);
        }
    }
}
