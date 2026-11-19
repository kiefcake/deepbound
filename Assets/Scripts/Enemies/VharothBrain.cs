using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// VHAROTH, THE EMBER ARCHON - lord of the Cinder Pit (2026-11-14). The one guardian built on the shape of a classic
    /// stage-cycling dungeon boss (the user named RotMG's Malphas as the example): his fight is a fixed loop of stages
    /// that does not care how hurt he is, he grows and shrinks between them, and four cinder wards orbit him and fire
    /// through all of it.
    /// <list type="number">
    /// <item><b>Striding</b> (huge): he walks you down, throwing three-bolt spreads.</item>
    /// <item><b>Kindling</b> (smaller): a breath of fire where he cannot be touched, then bellows-imps crawl out of the
    /// floor; they burn out when the stage ends. Rings of fire pulse from him.</item>
    /// <item><b>Ember</b> (small, fast, hard to hit): he races round the pit spilling slow spirals.</item>
    /// <item><b>Seekers</b> (smaller): cinder mines that drift after you and burst when they reach you - lead them apart.</item>
    /// </list>
    /// Two thirds: the loop quickens. One third: the Furnace Heart (a stage of spirals he cannot be hurt in), then all
    /// of it faster still. Every stage reads on his body: his size, and the colour of his fire.
    /// </summary>
    public class VharothBrain : BossBrain
    {
        public override string DisplayName => "Vharoth";
        public override string Epithet => "The Ember Archon";
        public override string LastWords => "The fire... does not... need me...";
        public override string Hint => "His wards never stop. Lead the seekers apart.";
        public override Progression.Deeds.Deed Deed => Progression.Deeds.Deed.PitLord;
        protected override float VisualScale => 1.8f;
        protected override float[] PhaseGates => new[] { 0.66f, 0.33f };
        protected override Color ShockwaveColor => Fire;
        protected override ProjectileKind? QuickKind => ProjectileKind.Firebolt;
        public static readonly Color Fire = new Color(1f, 0.45f, 0.12f), Cinder = new Color(1f, 0.75f, 0.3f);

        public enum Stage : byte { Striding, Kindling, Ember, Seekers }
        /// <summary>Any peer: the stage, and his size (1 huge .. 0.5 small).</summary>
        public readonly NetworkVariable<byte> StageValue = new NetworkVariable<byte>();
        public readonly NetworkVariable<float> Size = new NetworkVariable<float>(1f);
        public Stage CurrentStage => (Stage)StageValue.Value;

        private float stageEnds = -1f, nextVolley, nextRing, nextSeeker, pace = 1f;
        private readonly List<EnemyBrain> imps = new List<EnemyBrain>();
        private Vector3 arena;
        private float circleAngle;
        /// <summary>For tests: stages entered, seekers burst.</summary>
        public int StagesEntered { get; private set; }
        public int SeekersBurst { get; private set; }

        private static readonly float[] StageSeconds = { 12f, 10f, 9f, 9f };
        private static readonly float[] StageSize = { 1f, 0.72f, 0.5f, 0.72f };

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            arena = World.DungeonDirector.Layout != null && World.DungeonDirector.Layout.BossRoom != null ? World.DungeonDirector.Layout.BossRoom.Center : transform.position;
        }

        protected override float SpeedScale => CurrentStage == Stage.Ember ? 2.1f : CurrentStage == Stage.Striding ? 0.85f : 1f;

        protected override void OnPhaseGate(int index)
        {
            if (index == 0) Transition("The Bellows Roar", "Burn brighter, little sparks!", 1.6f, () => pace = 0.8f);
            else
            {
                Survive("The Furnace Heart", "The furnace was here before your gods.", "Slip between the arms", 8f, () => { pace = 0.65f; ServerEnrage(9999f); });
                for (int e = 0; e < 3; e++)
                    StartPattern(new Pattern
                    {
                        Arms = 3, Interval = 0.16f, Speed = 9f, Spin = 10f, FlipEvery = 2.8f, Damage = damage * 0.4f, Kind = ProjectileKind.Firebolt,
                        Duration = 7.6f, Range = 24f, Origin = Orbit(2.5f, 50f, e * 120f, 1.4f),
                    });
            }
        }

        private void EnterStage(Stage s)
        {
            // The imps of the last Kindling burn out as the stage turns.
            foreach (var imp in imps) if (imp != null && imp.IsSpawned && !imp.IsDead) { BurnOutRpc(imp.transform.position); imp.NetworkObject.Despawn(); }
            imps.Clear();
            StageValue.Value = (byte)s;
            Size.Value = StageSize[(int)s];
            stageEnds = Time.time + StageSeconds[(int)s] * pace / Cadence;
            StagesEntered++;
            StageFxRpc((byte)s, transform.position);
            // The four cinder wards: always circling, always firing at every one of you.
            StopPatterns();
            for (int w = 0; w < 4; w++)
                StartPattern(new Pattern
                {
                    Arms = 1, Interval = 99f, Speed = 10.5f, Spin = 0f, AimedEvery = (StandingParty <= 1 ? 2.4f : 1.9f) * pace, Damage = damage * 0.35f,
                    Kind = ProjectileKind.Firebolt, Duration = StageSeconds[(int)s] * pace + 0.5f, Range = 22f, Origin = Orbit(2.6f, 110f, w * 90f, 1.6f),
                });
            switch (s)
            {
                case Stage.Striding:
                    nextVolley = Time.time + 1.5f;
                    break;
                case Stage.Kindling:
                    Transition("Kindling", "Up, my bellows. Breathe!", 1.4f, () => imps.AddRange(Summon(1 + scaledPlayers, 4f, 0.8f)));
                    nextRing = Time.time + 2.5f;
                    break;
                case Stage.Ember:
                    circleAngle = Mathf.Atan2(transform.position.z - arena.z, transform.position.x - arena.x) * Mathf.Rad2Deg;
                    busyUntil = stageEnds;
                    StartPattern(new Pattern { Arms = 5, Interval = 0.5f, Speed = 6.5f, Spin = 11f, Damage = damage * 0.4f, Kind = ProjectileKind.Firebolt, Duration = StageSeconds[2] * pace, Range = 20f });
                    break;
                case Stage.Seekers:
                    nextSeeker = Time.time + 0.8f;
                    break;
            }
        }

        protected override void Update()
        {
            if (HeldByStun()) { UpdateLook(); return; }
            if (IsServer && !IsDead && !Dormant && !InEntrance)
            {
                if (stageEnds < 0f) EnterStage(Stage.Striding);
                else if (Time.time >= stageEnds && !Immune) EnterStage((Stage)(((int)CurrentStage + 1) % 4));
                TickStage();
                TickSeekers();
            }
            base.Update();
            UpdateLook();
        }

        private void TickStage()
        {
            switch (CurrentStage)
            {
                case Stage.Kindling:
                    if (Time.time >= nextRing && !Immune)
                    {
                        nextRing = Time.time + 2.6f * pace;
                        var origin = transform.position + Vector3.up * 1.2f;
                        for (int i = 0; i < 14; i++)
                        {
                            var d = Quaternion.Euler(0f, i * 360f / 14f + Random.Range(0f, 25f), 0f) * Vector3.forward;
                            Arrows.ServerFire(origin, d, damage * 0.4f, Team.Players, NetworkObjectId, 8f, null, false, 20f);
                        }
                        RingFxRpc(origin);
                    }
                    break;
                case Stage.Ember:
                    // Racing round the pit, not at you.
                    if (agent != null && agent.enabled && agent.isOnNavMesh)
                    {
                        circleAngle += 70f * Time.deltaTime;
                        var r = 7f;
                        if (World.DungeonDirector.Layout != null && World.DungeonDirector.Layout.BossRoom != null)
                            r = Mathf.Max(4f, Mathf.Min(World.DungeonDirector.Layout.BossRoom.Size.x, World.DungeonDirector.Layout.BossRoom.Size.y) / 2f - 3f);
                        var goal = arena + new Vector3(Mathf.Cos(circleAngle * Mathf.Deg2Rad), 0f, Mathf.Sin(circleAngle * Mathf.Deg2Rad)) * r;
                        agent.isStopped = false;
                        agent.SetDestination(goal);
                    }
                    break;
                case Stage.Seekers:
                    if (Time.time >= nextSeeker && seekers.Count < 8)
                    {
                        nextSeeker = Time.time + 2.4f * pace;
                        PlayActionRpc(RigAction.Hurl, 0.5f);
                        foreach (var p in PlayerLife.All)
                            if (p != null && p.Targetable && seekers.Count < 8)
                                seekers.Add(new Seeker { Pos = transform.position + Vector3.up * 1.4f + Random.insideUnitSphere * 0.8f, Born = Time.time, Fuse = -1f });
                    }
                    break;
            }
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (TryShockwave()) return true;
            if (CurrentStage != Stage.Striding || Time.time < nextVolley || target == null) return false;
            nextVolley = Time.time + 2.2f * pace / Cadence;
            var origin = transform.position + Vector3.up * 2.4f * Size.Value;
            var dir = AimDir(origin, 13f);
            PlayActionRpc(RigAction.Hurl, 0.5f);
            for (int i = -1; i <= 1; i++)
            {
                var d = Quaternion.AngleAxis(i * 10f, Vector3.up) * dir;
                Arrows.ServerFire(origin, d, damage * 0.55f, Team.Players, NetworkObjectId, 13f, null, false, 26f);
                BoltFxRpc(origin, d, 13f);
            }
            HurlFxRpc(origin);
            return true;
        }

        // ---- Seekers: cinder mines that drift after the nearest of you and burst ----

        private struct Seeker { public Vector3 Pos; public float Born, Fuse; }
        private readonly List<Seeker> seekers = new List<Seeker>();
        public const float SeekerSpeed = 3.1f, SeekerReach = 1.5f, SeekerBurst = 2.2f, SeekerFuse = 0.55f, SeekerLife = 8f;
        private float nextSeekerSync;

        private void TickSeekers()
        {
            if (seekers.Count == 0) return;
            for (int i = seekers.Count - 1; i >= 0; i--)
            {
                var s = seekers[i];
                PlayerLife near = null; float nd = float.MaxValue;
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    float d = Vector3.Distance(p.transform.position + Vector3.up, s.Pos);
                    if (d < nd) { nd = d; near = p; }
                }
                if (s.Fuse < 0f)
                {
                    if (near != null) s.Pos = Vector3.MoveTowards(s.Pos, near.transform.position + Vector3.up, SeekerSpeed * Time.deltaTime);
                    if (nd < SeekerReach) { s.Fuse = Time.time + SeekerFuse; SeekerLitRpc(s.Pos); }
                    else if (Time.time - s.Born > SeekerLife) { seekers.RemoveAt(i); SeekerFizzleRpc(s.Pos); continue; }
                }
                else if (Time.time >= s.Fuse)
                {
                    foreach (var v in CombatQuery.Circle(s.Pos - Vector3.up, SeekerBurst, Team.Players)) v.ServerDamage(Signature(damage * 0.9f), s.Pos, NetworkObjectId);
                    SeekersBurst++;
                    SeekerBurstRpc(s.Pos);
                    seekers.RemoveAt(i);
                    continue;
                }
                seekers[i] = s;
            }
            if (Time.time >= nextSeekerSync)
            {
                nextSeekerSync = Time.time + 0.1f;
                var pos = new Vector3[seekers.Count];
                var lit = new bool[seekers.Count];
                for (int i = 0; i < seekers.Count; i++) { pos[i] = seekers[i].Pos; lit[i] = seekers[i].Fuse > 0f; }
                SeekersRpc(pos, lit);
            }
        }

        /// <summary>Tests: a seeker now, at <paramref name="at"/>.</summary>
        public void ServerDebugSeeker(Vector3 at) => seekers.Add(new Seeker { Pos = at, Born = Time.time, Fuse = -1f });

        [Rpc(SendTo.ClientsAndHost)]
        private void SeekersRpc(Vector3[] positions, bool[] lit)
        {
            for (int i = 0; i < seekerOrbs.Count; i++) seekerOrbs[i].gameObject.SetActive(i < positions.Length);
            while (seekerOrbs.Count < positions.Length)
            {
                var orb = MeshKit.Part(null, "Seeker", MeshKit.Ball(0.28f, 10, 8), Mat.Glow, Vector3.zero).transform;
                orb.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                seekerOrbs.Add(orb);
            }
            for (int i = 0; i < positions.Length; i++)
            {
                seekerTargets[i] = positions[i];
                ArtLibrary.SetGlow(seekerOrbs[i].GetComponent<Renderer>(), (lit[i] ? new Color(1f, 0.95f, 0.7f) * 3f : Fire * 1.8f));
                if (lit[i]) seekerOrbs[i].localScale = Vector3.one * (1f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 30f)));
                else seekerOrbs[i].localScale = Vector3.one;
            }
        }

        private readonly List<Transform> seekerOrbs = new List<Transform>();
        private readonly Vector3[] seekerTargets = new Vector3[16];

        [Rpc(SendTo.ClientsAndHost)]
        private void SeekerBurstRpc(Vector3 at)
        {
            Sfx.Play(Sound.Fire, at, 1f, 0.8f);
            Vfx.Fireball(at, SeekerBurst, Fire);
            Vfx.Ring(at - Vector3.up * 1.2f, SeekerBurst, Fire, 0.4f);
        }

        /// <summary>READABILITY (2026-11-19): a lit seeker only flickered; now the ground it will burn shows, and it hisses.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void SeekerLitRpc(Vector3 at)
        {
            Telegraph.Show(at - Vector3.up, SeekerBurst, SeekerFuse, null, Telegraph.Harm);
            Sfx.Play(Sound.Fire, at, 0.7f, 1.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SeekerFizzleRpc(Vector3 at) { Vfx.Smoke(at, 2, 0.3f, 1f); }

        /// <summary>READABILITY (2026-11-19): the striding fan left his hand in silence.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void HurlFxRpc(Vector3 at) => Sfx.Play(Sound.Fire, at, 0.8f, 1.3f);

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltFxRpc(Vector3 origin, Vector3 dir, float speed) => Arrows.SpawnVisual(origin, dir, ProjectileKind.Firebolt, speed, 26f);

        [Rpc(SendTo.ClientsAndHost)]
        private void RingFxRpc(Vector3 origin)
        {
            Sfx.Play(Sound.Fire, origin, 0.9f, 1.1f);
            for (int i = 0; i < 14; i++) Arrows.SpawnVisual(origin, Quaternion.Euler(0f, i * 360f / 14f, 0f) * Vector3.forward, ProjectileKind.Firebolt, 8f, 20f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StageFxRpc(byte stage, Vector3 at)
        {
            Sfx.Play(Sound.Fire, at + Vector3.up, 1f, stage == 2 ? 1.5f : 0.7f);
            Vfx.Embers(at + Vector3.up * 1.4f, 26, stage == 2 ? Cinder : Fire, 6f, 0.07f, 0.9f);
            Vfx.Flash(at + Vector3.up * 1.5f, Fire, 4f, 10f, 0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BurnOutRpc(Vector3 at)
        {
            Vfx.Smoke(at + Vector3.up * 0.5f, 3, 0.5f, 1.2f);
            Vfx.Embers(at + Vector3.up * 0.6f, 10, Fire, 3f, 0.05f, 0.6f);
        }

        protected override void OnDied(Health h)
        {
            base.OnDied(h);
            if (!IsServer) return;
            seekers.Clear();
            SeekersRpc(new Vector3[0], new bool[0]);
            foreach (var imp in imps) if (imp != null && imp.IsSpawned && !imp.IsDead) imp.NetworkObject.Despawn();
        }

        // ---- His body: the size of the stage, and four cinders circling him (any peer) ----

        private Vector3 baseRigScale;
        private float shownSize = 1f, capsuleH, capsuleR, capsuleY;
        private CapsuleCollider capsule;
        private Transform wards;

        private void UpdateLook()
        {
            for (int i = 0; i < seekerOrbs.Count; i++)
                if (seekerOrbs[i].gameObject.activeSelf) seekerOrbs[i].position = Vector3.Lerp(seekerOrbs[i].position, seekerTargets[i], Time.deltaTime * 12f);
            if (Rig == null) return;
            if (baseRigScale == Vector3.zero)
            {
                baseRigScale = Rig.transform.localScale;
                capsule = GetComponent<CapsuleCollider>();
                if (capsule != null) { capsuleH = capsule.height; capsuleR = capsule.radius; capsuleY = capsule.center.y; }
            }
            shownSize = Mathf.MoveTowards(shownSize, IsDead ? shownSize : Size.Value, Time.deltaTime * 1.2f);
            Rig.transform.localScale = baseRigScale * shownSize;
            // He is hard to hit small: the body you can strike shrinks with him.
            if (capsule != null) { capsule.height = capsuleH * shownSize; capsule.radius = capsuleR * shownSize; capsule.center = new Vector3(0f, capsuleY * shownSize, 0f); }
            if (wards == null)
            {
                wards = new GameObject("CinderWards").transform;
                wards.SetParent(transform, false);
                for (int w = 0; w < 4; w++)
                {
                    var c = MeshKit.Part(wards, "Ward", MeshKit.Ball(0.22f, 8, 6), Mat.Glow, Quaternion.Euler(0f, w * 90f, 0f) * Vector3.forward * 2.6f + Vector3.up * 1.6f);
                    ArtLibrary.SetGlow(c.GetComponent<Renderer>(), Cinder * 2f);
                    c.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            wards.gameObject.SetActive(!IsDead && !Dormant);
            wards.localRotation = Quaternion.Euler(0f, Time.time * 110f, 0f);
            // His fire: deep orange striding, white-gold racing as an ember.
            Rig.GlowColor = CurrentStage == Stage.Ember ? Cinder : Fire;
        }

        public override void OnNetworkDespawn()
        {
            foreach (var o in seekerOrbs) if (o != null) Destroy(o.gameObject);
            seekerOrbs.Clear();
            base.OnNetworkDespawn();
        }

        protected override void DressRig(HumanoidRig rig)
        {
            if (rig.Head == null || rig.Torso == null) return;
            // Horns of black iron, a crown of flame, and cracks of fire across the chest.
            foreach (float s in new[] { -1f, 1f })
                ArtLibrary.Tint(MeshKit.Part(rig.Head, "Horn", MeshKit.Cylinder(0.05f, 0f, 0.42f, 6), Mat.Steel, new Vector3(s * 0.11f, 0.2f, -0.02f), new Vector3(-25f, 0f, s * -28f)).GetComponent<Renderer>(), new Color(0.12f, 0.1f, 0.1f));
            for (int i = 0; i < 6; i++)
            {
                var flame = MeshKit.Part(rig.Head, "Flame", MeshKit.Cylinder(0.035f, 0f, 0.22f, 5), Mat.Glow, Quaternion.Euler(0f, i * 60f, 0f) * Vector3.forward * 0.12f + Vector3.up * 0.26f);
                ArtLibrary.SetGlow(flame.GetComponent<Renderer>(), Fire * 2f);
            }
            for (int i = 0; i < 5; i++)
            {
                var crack = MeshKit.Part(rig.Torso, "FireCrack", MeshKit.Box(new Vector3(0.02f, 0.22f, 0.01f)), Mat.Glow, new Vector3(-0.12f + i * 0.06f, 0.25f + (i % 2) * 0.08f, 0.2f), new Vector3(0f, 0f, i * 23f - 40f));
                ArtLibrary.SetGlow(crack.GetComponent<Renderer>(), Fire * 1.6f);
            }
        }
    }
}
