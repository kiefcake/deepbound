using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    // =====================================================================================================================
    // THE BLACK CATHEDRAL - MOTHER VEYLE, THE MIRROR-NUN
    // =====================================================================================================================

    /// <summary>
    /// Mother Veyle, the Mirror-Nun. A loop of three:
    /// <list type="number">
    /// <item><b>The Glass</b>: she is four. The copies cast as she does, and a strike on one shatters it - and the glass cuts
    /// whoever stands close. Only the real one casts a shadow on the floor.</item>
    /// <item><b>Penance</b>: two beams of holy light sweep the nave like clock hands (cannot be hurt; step over the gaps).</item>
    /// <item><b>Confession</b>: a ring closes round one of you and she steps into it: a duel. In the ring she takes more from
    /// the one she chose; leaving it scalds.</item>
    /// </list>
    /// Below half there are five of her in the glass.
    /// </summary>
    public class MirrorNunBrain : LairBrain
    {
        public override string DisplayName => "Mother Veyle";
        public override string Epithet => "The Mirror-Nun";
        public override string LastWords => "Look... at me... no, at ME...";
        public override string Hint => "Only the true one casts a shadow.";
        protected override float VisualScale => 1.3f;
        protected override float[] PhaseGates => IsEcho ? System.Array.Empty<float>() : new[] { 0.5f };
        protected override Color ShockwaveColor => new Color(0.85f, 0.8f, 1f);
        public static readonly Color Holy = new Color(1f, 0.92f, 0.7f), Glass = new Color(0.8f, 0.75f, 1f);

        private enum Stage { Glass, Penance, Confession }
        private Stage stage;
        private float stageEnds, nextBolt;
        private int copies = 3;
        private PlayerLife confessor;
        private Vector3 ringAt;
        public const float RingRadius = 5f;
        /// <summary>For tests.</summary>
        public int Shattered { get; private set; }
        public int LoopsDone { get; private set; }

        protected override void OnEngaged()
        {
            if (IsServer) health.IncomingModifiers.Add((dmg, from) =>
            {
                // In the ring, the one she chose cuts deeper (a duel favours the bold).
                if (stage == Stage.Confession && confessor != null && Vector3.Distance(from, confessor.transform.position) < 2.5f) return dmg * 1.4f;
                return dmg;
            });
            BeginGlass();
        }

        protected override void OnPhaseGate(int index)
        {
            copies = 4;
            Transition("Five Panes", "See how many of me there are? All of them are sorry.", 1.4f, null);
        }

        private void BeginGlass()
        {
            stage = Stage.Glass;
            stageEnds = Time.time + 14f;
            // Her and her copies, shuffled round a circle: you cannot know which went where.
            var spots = new List<Vector3>();
            for (int i = 0; i <= copies; i++)
                spots.Add(RoomCenter + Quaternion.Euler(0f, i * 360f / (copies + 1) + Random.Range(-15f, 15f), 0f) * Vector3.forward * RoomRadius * 0.6f);
            for (int i = spots.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (spots[i], spots[j]) = (spots[j], spots[i]); }
            var from = transform.position;
            Warp(spots[0]);
            for (int i = 1; i < spots.Count; i++) SpawnEcho(2, spots[i]);
            GlassFxRpc(from);
        }

        protected override void EchoHit(float dmg, Vector3 from)
        {
            var m = Master as MirrorNunBrain;
            if (m != null) m.Shattered++;
            Shatter(true);
        }

        private void BeginPenance()
        {
            foreach (var e in echoes.ToArray()) if (e != null) e.Shatter(false);
            stage = Stage.Penance;
            stageEnds = Time.time + 8f;
            Warp(UnityEngine.AI.NavMesh.SamplePosition(RoomCenter, out var h, 3f, UnityEngine.AI.NavMesh.AllAreas) ? h.position : RoomCenter);
            Survive("Penance", "Kneel, children.", "", 7.6f);
            for (int e = 0; e < 2; e++)
                StartPattern(new Pattern
                {
                    Arms = 2, Interval = 0.07f, Speed = 15f, Spin = e == 0 ? 3.2f : -3.2f, Damage = damage * 0.4f, Kind = ProjectileKind.Smite,
                    Duration = 7.4f, Range = RoomRadius + 4f, Origin = Orbit(0.3f, 0f, e * 90f, 1.2f),
                });
        }

        private void BeginConfession()
        {
            confessor = NearestStanding(transform.position);
            if (confessor == null) { BeginGlass(); return; }
            stage = Stage.Confession;
            stageEnds = Time.time + 7f;
            ringAt = confessor.transform.position;
            Warp(ringAt + (transform.position - ringAt).normalized * 2f);
            ConfessRpc(confessor.NetworkObjectId, RingRadius, 7f);
            Taunt(StandingParty > 1 ? "You. Confess. The others may watch." : "Confess, child.");
        }

        private float nextScald;

        protected override void Update()
        {
            if (IsServer && !IsDead && !IsEcho && Engaged.Value)
            {
                switch (stage)
                {
                    case Stage.Glass:
                        if (Time.time >= stageEnds || echoes.Count == 0) BeginPenance();
                        break;
                    case Stage.Penance:
                        if (Time.time >= stageEnds) BeginConfession();
                        break;
                    case Stage.Confession:
                        if (confessor != null && confessor.Targetable && Time.time >= nextScald)
                        {
                            var off = confessor.transform.position - ringAt; off.y = 0f;
                            if (off.magnitude > RingRadius) { nextScald = Time.time + 0.5f; confessor.Health.ServerDamage(Signature(damage * 0.35f), ringAt, NetworkObjectId); }
                        }
                        if (Time.time >= stageEnds || confessor == null || !confessor.Targetable) { confessor = null; LoopsDone++; BeginGlass(); }
                        break;
                }
            }
            if (IsServer && IsEcho && Master != null && !Master.IsSpawned) Shatter(false);
            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            // Every one of her casts a fan of holy bolts - copies too: the glass hurts as much as she does.
            if (Time.time < nextBolt || target == null || InSurvival) return false;
            nextBolt = Time.time + (IsEcho ? 4.2f : 3.2f) / Cadence;
            var origin = transform.position + Vector3.up * 1.8f;
            var dir = AimDir(origin, 11f);
            PlayActionRpc(RigAction.Cast, 0.5f);
            for (int i = -1; i <= 1; i++)
            {
                var d = Quaternion.AngleAxis(i * 12f, Vector3.up) * dir;
                Arrows.ServerFire(origin, d, damage * 0.45f, Team.Players, NetworkObjectId, 11f, null, false, 24f);
                BoltFxRpc(origin, d);
            }
            CastFxRpc(origin);
            return true;
        }

        /// <summary>READABILITY (2026-11-19): her fans (and every copy's) left in silence.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void CastFxRpc(Vector3 at) => Sfx.Play(Sound.Whisper, at, 0.7f, 1.4f);

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltFxRpc(Vector3 origin, Vector3 dir) => Arrows.SpawnVisual(origin, dir, ProjectileKind.Smite, 11f, 24f);

        [Rpc(SendTo.ClientsAndHost)]
        private void GlassFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Shatter, at, 1f, 1.4f);
            Vfx.Flash(at + Vector3.up, Glass, 4f, 10f, 0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ConfessRpc(ulong player, float radius, float seconds)
        {
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(player, out var obj)) return;
            Telegraph.Show(obj.transform.position, radius, seconds, null, new Color(Holy.r, Holy.g, Holy.b, 0.18f));
            Sfx.Play(Sound.Toll, obj.transform.position + Vector3.up * 2f, 0.7f, 1.5f);
        }

        protected override void OnRoleChanged()
        {
            if (Rig == null || Role.Value != 2) return;
            // A copy: glass, not flesh - and glass casts no shadow (the only way to tell).
            foreach (var r in Rig.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        protected override void DressRig(HumanoidRig rig)
        {
            if (rig.Head == null || rig.Torso == null) return;
            // A wimple of white and a halo of mirror shards turning about her head.
            var hood = rig.Head.Find("Hood");
            if (hood != null) DestroyImmediate(hood.gameObject);
            TintPart(MeshKit.Part(rig.Head, "Wimple", MeshKit.Box(new Vector3(0.34f, 0.12f, 0.3f)), Mat.Cloth, new Vector3(0f, -0.12f, 0f)), new Color(0.9f, 0.88f, 0.84f));
            var halo = MeshKit.Pivot(rig.Head, "Shards", new Vector3(0f, 0.35f, -0.05f));
            halo.gameObject.AddComponent<Spin>().DegreesPerSecond = 30f;
            for (int i = 0; i < 8; i++)
            {
                var c = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 0.36f;
                GlowPart(MeshKit.Part(halo, "Shard", MeshKit.Box(new Vector3(0.07f, 0.16f, 0.01f)), Mat.Glow, c, new Vector3(0f, i * 45f, 15f)), Glass * 0.8f);
            }
        }
    }
}
