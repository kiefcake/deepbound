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
    /// (on floor 10, the bottom of the Abyss; every Seal left unbroken makes it 15% tougher). A colossal floating ribcage under a three-eyed skull,
    /// ringed with orbiting bone. It fights in patterns, RotMG-style:
    ///
    /// <list type="bullet">
    /// <item><b>The Dirge</b>: a ring of slow shadow bolts with a gap in it. Find the gap.</item>
    /// <item><b>Grasping Dark</b>: a circle opens under every player; step out before it closes.</item>
    /// <item><b>The Toll</b>: a jumpable shockwave.</item>
    /// <item>70%: <b>The Seal Wardens</b>: two Mourners rise; it is warded until they fall. Someone
    ///   must keep watching them while the others break them.</item>
    /// <item>40%: <b>The Silence</b>: the light fails and it pours out a five-armed spiral. Survive.</item>
    /// <item>15%: <b>The Last Morning</b>: everything faster, rings thicker, and the ECLIPSE: pools of dawn light
    ///   open around the arena; when the dark falls, anyone outside one is struck.</item>
    /// <item>After its wardens fall it sings ECHOES of the four it devoured (2026-10-26, "keep working on bosses"), one
    ///   in turn: the Gravewarden's bone lines, the Tide Lich's drowning pools, the Hollow King's crown slam, the
    ///   Cardinal's rose of holy fire.</item>
    /// </list>
    /// </summary>
    public class UnsungBrain : BossBrain
    {
        public override string DisplayName => "The Unsung";
        public override string Epithet => "That Which the Hymn Was For";
        protected override byte EntranceStyle => 5;
        public override string LastWords => "So this... is what a song is for.";
        public override Progression.Deeds.Deed Deed => Progression.Deeds.Deed.Unsung;
        public override string Hint => "Slip through the gaps in its rings, break its wardens while someone keeps watching them, and when the dawn opens, stand in the light.";

        private const float BoltSpeed = 9f, SpiralSpeed = 11.5f;
        private float nextRing, nextGrasp, nextSpiral, spiralAngle;
        private bool lastMorning;

        private struct Grasp { public Vector3 At; public float When; }
        private readonly List<Grasp> grasps = new List<Grasp>();

        // ---- Echoes (after the Seal Wardens) ----
        private bool echoesAwake;
        private int echoIndex;
        private float nextEcho;
        public const float EchoCooldown = 11f, EchoTell = 1.4f;
        private struct Hazard { public int Kind; public Vector3 At, Dir; public float Length, Radius, When; }
        private readonly List<Hazard> hazards = new List<Hazard>();
        private static readonly string[] EchoNames = { "THE GRAVEWARDEN", "THE TIDE LICH", "THE HOLLOW KING", "THE PALE CARDINAL" };
        private static readonly Color[] EchoColors = { new Color(0.85f, 0.8f, 0.7f), new Color(0.45f, 0.8f, 1f), new Color(1f, 0.8f, 0.3f), new Color(1f, 0.65f, 0.3f) };

        // ---- The Eclipse (The Last Morning) ----
        public const float EclipseEvery = 10f, EclipseWarning = 4.5f, DawnRadius = 2.8f;
        private float nextEclipse;
        private readonly List<Vector3> dawnPools = new List<Vector3>();
        private float eclipseAt = -1f;
        /// <summary>For tests: how many Echoes and Eclipses it has sung.</summary>
        public int EchoesSung { get; private set; }
        public int EclipsesFallen { get; private set; }

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
                        () => { Taunt("No more keepers. Then I will sing them myself: every one you killed."); echoesAwake = true; nextEcho = Time.time + 3f; });
                    break;
                case 1:
                    Survive("The Silence", "Hear it? Neither do I. Not one note.", "The light fails: weave through the spiral until it passes", 14f,
                        () => Taunt("Still here. Still small."));
                    grasps.Clear();
                    SilenceRpc(14f);
                    // Three emitters wheel round it at different speeds and directions; one shoots at stragglers.
                    for (int e = 0; e < 3; e++)
                        StartPattern(new Pattern
                        {
                            Arms = 3, Interval = 0.13f, Speed = SpiralSpeed, Spin = e == 1 ? -10f : 12f, FlipEvery = e == 2 ? 2.6f : 0f,
                            Damage = damage * 0.35f, Kind = ProjectileKind.ShadowBolt, Duration = 13.4f, AimedEvery = e == 0 ? 1.2f : 0f, Range = 30f,
                            Origin = Orbit(3.2f + e * 1.4f, e == 1 ? -30f : 38f, e * 120f, 1.2f),
                        });
                    break;
                default:
                    Transition("The Last Morning", "The morning they waited for. It is mine.", 2.6f, () => { lastMorning = true; echoesAwake = true; nextEclipse = Time.time + 2f; });
                    break;
            }
        }

        protected override void Update()
        {
            if (HeldByStun()) return;
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
                ResolveHazards();
                TickEclipse();
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
            if (echoesAwake && !InSurvival && Time.time >= nextEcho)
            {
                nextEcho = Time.time + (lastMorning ? EchoCooldown * 0.75f : EchoCooldown) / Cadence;
                SingEcho(echoIndex++ % 4);
                return true;
            }
            if (Time.time >= nextGrasp)
            {
                nextGrasp = Time.time + (lastMorning ? 6.5f : 9f);
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    float gw = Tell(1.5f);
                    var at = PredictedFeet(p, gw);
                    grasps.Add(new Grasp { At = at, When = Time.time + gw });
                    GraspWarnRpc(at, 2.6f, gw);
                }
                PlayActionRpc(RigAction.Summon, 0.9f);
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
            PlayActionRpc(RigAction.Hurl, 0.6f);
            BoltsRpc(origin, dirs.ToArray(), BoltSpeed * (lastMorning ? 1.25f : 1f));
        }

        private void TickSpiral() { }

        // ------------------------------------------------------------------ echoes

        /// <summary>One of the dead it swallowed, sung back: each a known attack, telegraphed in that boss's colour.</summary>
        private void SingEcho(int which)
        {
            EchoesSung++;
            float tell = Tell(EchoTell);
            castUntil = Time.time + 1f;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Channel, 1f);
            EchoRpc((byte)which);
            Debug.Log("[Boss] Unsung echo " + EchoNames[which]);
            var me = transform.position;
            switch (which)
            {
                case 0: // Grave Rend: a line of bone spikes from it to (and past) every player.
                    foreach (var p in PlayerLife.All)
                    {
                        if (p == null || !p.Targetable) continue;
                        var to = PredictedFeet(p, tell) - me; to.y = 0f;
                        if (to.sqrMagnitude < 0.01f) to = transform.forward;
                        float len = Mathf.Min(to.magnitude + 6f, 26f);
                        var dir = to.normalized;
                        var start = Vfx.Floor(me + dir * 1.2f);
                        hazards.Add(new Hazard { Kind = 0, At = start, Dir = dir, Length = len, Radius = 0.9f, When = Time.time + tell });
                        EchoLineRpc(start, dir, len, 1.8f, tell);
                    }
                    break;
                case 1: // The Tide: a drowning pool under each player that roots whoever it catches.
                    foreach (var p in PlayerLife.All)
                    {
                        if (p == null || !p.Targetable) continue;
                        var at = PredictedFeet(p, tell);
                        hazards.Add(new Hazard { Kind = 1, At = at, Radius = 3f, When = Time.time + tell });
                        TintedCircleRpc(at, 3f, tell, Telegraph.Frost);
                    }
                    break;
                case 2: // Crown's Fall: everything near it is crushed. Get out.
                    hazards.Add(new Hazard { Kind = 2, At = Vfx.Floor(me), Radius = 7.5f, When = Time.time + tell * 1.3f });
                    TintedCircleRpc(Vfx.Floor(me), 7.5f, tell * 1.3f, Telegraph.Harm);
                    break;
                default: // The rose window: four emitters wheel out from it, swaying holy fire.
                    for (int e = 0; e < 4; e++)
                    {
                        float phase = e * 90f;
                        StartPattern(new Pattern
                        {
                            Arms = 2, Interval = 0.2f, Speed = 9.5f, Spin = 9f, Wobble = 8f, Damage = damage * 0.3f,
                            Kind = ProjectileKind.Smite, Duration = 4.5f, Range = 24f,
                            Origin = Orbit(3.5f, 30f, phase, 1.2f),
                        });
                    }
                    break;
            }
        }

        private void ResolveHazards()
        {
            for (int i = hazards.Count - 1; i >= 0; i--)
            {
                var hz = hazards[i];
                if (Time.time < hz.When) continue;
                hazards.RemoveAt(i);
                if (hz.Kind == 0)
                {
                    foreach (var h in new List<Health>(Health.All))
                    {
                        if (h == null || h.Team != Team.Players || !h.IsAlive) continue;
                        var rel = h.transform.position - hz.At; rel.y = 0f;
                        float along = Vector3.Dot(rel, hz.Dir);
                        if (along < -0.5f || along > hz.Length) continue;
                        if ((rel - hz.Dir * along).magnitude > hz.Radius + h.BodyRadius) continue;
                        h.ServerDamage(damage * 1.2f, h.transform.position + Vector3.up, NetworkObjectId);
                        KnockPlayer(h, Vector3.up * 5f);
                    }
                    RendFxRpc(hz.At, hz.Dir, hz.Length);
                }
                else
                {
                    foreach (var v in CombatQuery.Circle(hz.At, hz.Radius, Team.Players))
                    {
                        v.ServerDamage(damage * (hz.Kind == 2 ? 1.6f : 0.9f), hz.At + Vector3.up, NetworkObjectId);
                        if (hz.Kind == 1 && v.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerRoot(1.5f);
                        if (hz.Kind == 2) { var away = v.transform.position - hz.At; away.y = 0f; KnockPlayer(v, away.normalized * 9f + Vector3.up * 4f); }
                    }
                    HazardFxRpc(hz.At, hz.Radius, (byte)hz.Kind);
                }
            }
        }

        // ------------------------------------------------------------------ the eclipse

        /// <summary>The Last Morning: pools of dawn open; when the dark falls everyone outside them is struck.</summary>
        private void TickEclipse()
        {
            if (!lastMorning || InSurvival) return;
            if (eclipseAt < 0f && Time.time >= nextEclipse)
            {
                dawnPools.Clear();
                var room = World.DungeonDirector.Layout != null ? World.DungeonDirector.Layout.BossRoom : null;
                var centre = room != null ? room.Center : transform.position;
                int pools = Mathf.Clamp(StandingParty, 2, 3);
                float startAngle = Random.Range(0f, 360f);
                for (int i = 0; i < pools; i++)
                {
                    var at = centre + Quaternion.Euler(0f, startAngle + i * 360f / pools, 0f) * Vector3.forward * Random.Range(5f, 8.5f);
                    if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
                    dawnPools.Add(at);
                }
                eclipseAt = Time.time + EclipseWarning;
                DawnRpc(dawnPools.ToArray(), DawnRadius, EclipseWarning);
                Taunt(EclipsesFallen == 0 ? "Go on. Stand in your little morning." : "Again. The dark is patient.");
            }
            else if (eclipseAt >= 0f && Time.time >= eclipseAt)
            {
                eclipseAt = -1f;
                nextEclipse = Time.time + EclipseEvery / Cadence;
                EclipsesFallen++;
                var struck = new List<Vector3>();
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    bool safe = false;
                    foreach (var pool in dawnPools)
                    {
                        var d = p.transform.position - pool; d.y = 0f;
                        if (d.magnitude <= DawnRadius + 0.3f) { safe = true; break; }
                    }
                    if (safe) continue;
                    p.Health.ServerDamage(damage * 2.2f, p.transform.position + Vector3.up, NetworkObjectId);
                    struck.Add(p.transform.position);
                }
                EclipseRpc(struck.ToArray());
                Debug.Log("[Boss] Unsung eclipse struck " + struck.Count);
            }
        }

        /// <summary>For tests: begin an Eclipse now.</summary>
        public void ServerDebugEclipse() { lastMorning = true; nextEclipse = 0f; eclipseAt = -1f; }
        /// <summary>For tests: sing one echo now.</summary>
        public void ServerDebugEcho(int which) => SingEcho(which);
        /// <summary>For tests: where the dawn pools are.</summary>
        public IReadOnlyList<Vector3> DawnPools => dawnPools;

        protected override ProjectileKind? QuickKind => null;

        private void ResolveGrasps()
        {
            for (int i = grasps.Count - 1; i >= 0; i--)
            {
                if (Time.time < grasps[i].When) continue;
                var at = grasps[i].At;
                grasps.RemoveAt(i);
                foreach (var v in CombatQuery.Circle(at, 2.6f, Team.Players))
                {
                    v.ServerDamage(damage * 1.3f, at + Vector3.up, NetworkObjectId);
                    // The dark under your feet gets into your head.
                    if (v.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerMania(2f);
                }
                GraspFxRpc(at);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EchoRpc(byte which)
        {
            var c = EchoColors[which % EchoColors.Length];
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 5f, "ECHO: " + EchoNames[which % EchoNames.Length], c);
            Sfx.Play(Sound.Shade, transform.position + Vector3.up * 3f, 1f, 0.55f);
            Vfx.Ring(Vfx.Floor(transform.position) + Vector3.up * 0.05f, 4f, c, 0.6f);
            Vfx.Embers(transform.position + Vector3.up * 3f, 18, c, 3f, 0.06f, 1f);
        }

        /// <summary>The Gravewarden's lines in bone, not the default red (telegraphs stay quiet, 2026-10-21).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void EchoLineRpc(Vector3 start, Vector3 dir, float length, float width, float seconds)
        {
            Telegraph.ShowLine(start, dir, length, width, seconds, this, new Color(0.9f, 0.82f, 0.65f, 0.24f));
            Gather(seconds, 1f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RendFxRpc(Vector3 start, Vector3 dir, float length)
        {
            var bone = new Color(0.9f, 0.85f, 0.75f);
            Sfx.Play(Sound.BoneBreak, start + dir * length * 0.5f, 1f, 0.8f);
            for (float d = 0f; d <= length; d += 1.4f)
            {
                var at = Vfx.Floor(start + dir * d);
                Vfx.Smoke(at, 2, 0.6f, 1.2f);
                Vfx.Embers(at + Vector3.up * 0.4f, 2, bone, 2f, 0.05f, 0.5f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HazardFxRpc(Vector3 at, float radius, byte kind)
        {
            var c = kind == 1 ? new Color(0.45f, 0.8f, 1f) : new Color(1f, 0.8f, 0.35f);
            Sfx.Play(kind == 1 ? Sound.Splash : Sound.Slam, at, 1f, kind == 1 ? 0.7f : 0.6f);
            Vfx.Ring(at + Vector3.up * 0.05f, radius, c, 0.5f);
            Vfx.Flash(at + Vector3.up, c, 5f, radius + 3f, 0.35f);
            Vfx.Smoke(at, kind == 2 ? 8 : 3, radius * 0.4f, 1.4f);
            if (kind == 2) ImpactFxRpcLocal(at);
        }

        private void ImpactFxRpcLocal(Vector3 at)
        {
            var cam = ViewCamera.Current;
            if (cam != null && (cam.transform.position - at).sqrMagnitude < 400f) Player.NetworkPlayer.Local?.ConfirmHit(1.8f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DawnRpc(Vector3[] pools, float radius, float seconds)
        {
            var dawn = new Color(1f, 0.85f, 0.45f, 0.3f);
            foreach (var at in pools)
            {
                Telegraph.Show(at, radius, seconds, null, dawn);
                Vfx.Flash(at + Vector3.up * 2f, new Color(1f, 0.85f, 0.5f), 3f, radius + 4f, seconds);
                Vfx.Embers(at + Vector3.up * 0.3f, 10, new Color(1f, 0.85f, 0.5f), 1.5f, 0.05f, 1.5f);
            }
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 5f, UI.GameHud.FirstTime("eclipse") ? "THE ECLIPSE: STAND IN THE LIGHT" : "THE ECLIPSE", new Color(1f, 0.85f, 0.45f));
            Sfx.Play2D(Sound.Toll, 0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EclipseRpc(Vector3[] struck)
        {
            Art.TheSilence.Begin(1.2f);
            var dark = new Color(0.55f, 0.3f, 1f);
            Sfx.Play2D(Sound.Slam, 0.9f);
            foreach (var at in struck)
            {
                Vfx.Flash(at + Vector3.up, dark, 6f, 6f, 0.4f);
                Vfx.Embers(at + Vector3.up, 12, dark, 4f, 0.06f, 0.8f);
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
            Telegraph.Show(at, radius, seconds, null, Telegraph.Madness);

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
