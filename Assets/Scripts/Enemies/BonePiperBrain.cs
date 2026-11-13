using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Bone Piper (2026-10-21, the user: "more bullet hell, one more enemy, change up the shot and
    /// spray pattern"): a wandering skeleton in a wide-brimmed hat playing a bagpipe of bone. It stands
    /// still and PLAYS: each tune is a different pattern of pale soulfire, unlike anything the
    /// Orbweaver fires.
    /// <list type="bullet">
    /// <item>ROSE: two interlocking five-petal blooms (speed varies round the ring, so it opens like a flower).</item>
    /// <item>SCISSORS: two streams that sweep across each other and back, closing on you then opening.</item>
    /// <item>SEEDPODS: three slow shells lobbed at and beside you that burst into rings where they land.</item>
    /// <item>MARCH: three walls of notes marching at you, each with a gap somewhere new.</item>
    /// </list>
    /// It cannot move while it plays, a stun or a kick ends the tune, and its bag glows: a weak point.
    /// Each tune's name floats over it as it starts, so you learn to read what is coming.
    /// </summary>
    public class BonePiperBrain : EnemyBrain
    {
        public enum Song : byte { Rose, Scissors, Seedpods, March }

        [SerializeField] private float preferredRange = 11f, songCooldown = 6.5f;
        public const float SoulSpeed = 7f, SoulRange = 22f;
        public static readonly Color Soul = new Color(0.4f, 1f, 0.9f);

        private float nextSong, songStartAt = -1f, songEnds = -1f, songBegan, nextTick;
        private int step;
        private float aimYaw;
        private Song song;
        private int order;
        private static readonly Song[] Cycle = { Song.Rose, Song.March, Song.Scissors, Song.Seedpods };

        /// <summary>Tests: volleys fired and the last song played.</summary>
        public int VolleysFired { get; private set; }
        public Song LastSong => song;
        public bool Playing => songEnds > Time.time;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextSong = Time.time + 2.5f;
            order = Random.Range(0, Cycle.Length);
        }

        // 0.45 until 2026-11-13: a whole Rose read as scenery. Each shot now hurts (dodging them is the point).
        private float SoulDamage => damage * 0.85f;
        private Vector3 Mouth => transform.position + Vector3.up * 1.25f + transform.forward * 0.35f;
        private static float TellScale => Difficulty.Tell(World.DungeonDirector.Instance != null ? World.DungeonDirector.Instance.Floor.Value.y : 0);

        protected override void Strike()
        {
            // Between tunes: three quick notes in a tight fan at you.
            if (target == null) return;
            var origin = Mouth;
            var dir = AimDir(origin, SoulSpeed * 1.4f);
            dir.y = 0f;
            float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
            for (int s = -1; s <= 1; s++) Fire(origin, yaw + s * 8f, SoulSpeed * 1.4f);
            FanRpc(origin, yaw, SoulSpeed * 1.4f);
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;
            if (Time.time < nextSong || Playing || songStartAt > 0f || distanceToTarget > 24f) return false;
            ServerBeginSong(Cycle[order++ % Cycle.Length]);
            return true;
        }

        /// <summary>Server: start a tune now (tests and the dev menu).</summary>
        public void ServerBeginSong(Song which)
        {
            song = which;
            float tell = 0.75f * TellScale;
            songStartAt = Time.time + tell;
            busyUntil = songStartAt + SongLength(which) + 0.3f;
            nextSong = busyUntil + songCooldown;
            PlayActionRpc(RigAction.Cast, tell);
            TellRpc(transform.position, (byte)which);
        }

        private static float SongLength(Song s) => s == Song.Rose ? 0.7f : s == Song.Scissors ? 2.4f : s == Song.Seedpods ? 0.7f : 1.3f;

        protected override void Update()
        {
            if (HeldByStun()) { songStartAt = -1f; songEnds = -1f; return; }
            if (IsServer && !IsDead) TickSong();
            base.Update();
        }

        public override void ServerStun(float seconds)
        {
            songStartAt = -1f;
            songEnds = -1f;
            base.ServerStun(seconds);
        }

        private void TickSong()
        {
            if (songStartAt > 0f && Time.time >= songStartAt)
            {
                songStartAt = -1f;
                songBegan = Time.time;
                songEnds = Time.time + SongLength(song);
                nextTick = Time.time;
                step = 0;
                // The tune is aimed where you were when it began: move.
                var to = target != null ? target.transform.position - transform.position : transform.forward;
                to.y = 0f;
                aimYaw = to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to).eulerAngles.y : transform.eulerAngles.y;
            }
            if (songEnds < 0f || Time.time > songEnds + 0.05f || Time.time < nextTick) return;
            var at = Mouth;
            switch (song)
            {
                case Song.Rose:
                {
                    // Two blooms, the second turned half a petal and a little slower, so they interlock.
                    float phase = aimYaw + step * 36f;
                    float scale = step == 0 ? 1f : 0.82f;
                    for (int k = 0; k < RoseCount; k++)
                    {
                        float yaw = phase + k * 360f / RoseCount;
                        Fire(at, yaw, RoseSpeed(k) * scale);
                    }
                    RoseRpc(at, phase, scale);
                    nextTick = Time.time + 0.55f;
                    if (++step >= 2) songEnds = -1f;
                    break;
                }
                case Song.Scissors:
                {
                    // Two streams crossing: they meet on you, part, and close again.
                    float t = Time.time - songBegan;
                    float off = 62f * Mathf.Cos(t * 2.7f);
                    Fire(at, aimYaw + off, SoulSpeed * 1.15f);
                    Fire(at, aimYaw - off, SoulSpeed * 1.15f);
                    PairRpc(at, aimYaw + off, aimYaw - off, SoulSpeed * 1.15f);
                    nextTick = Time.time + 0.085f;
                    break;
                }
                case Song.Seedpods:
                {
                    // Three shells at you and to either side; each bursts into a ring where it falls.
                    float dist = target != null ? Mathf.Clamp(Vector3.Distance(target.transform.position, transform.position) - 1f, 5f, 11f) : 8f;
                    float yaw = aimYaw + (step - 1) * 34f;
                    var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    int seed = step;
                    float range = step == 1 ? dist : dist * 0.8f;
                    Arrows.ServerFire(at, dir, SoulDamage * 1.4f, Team.Players, NetworkObjectId, PodSpeed, null, false, range, 0f, end => Burst(end, seed));
                    PodRpc(at, dir, range);
                    nextTick = Time.time + 0.22f;
                    if (++step >= 3) songEnds = -1f;
                    break;
                }
                default:
                {
                    // A wall of notes across your path with a gap somewhere: find it.
                    int gap = Random.Range(1, WallNotes - 3);
                    for (int k = 0; k < WallNotes; k++)
                    {
                        if (k >= gap && k < gap + 3) continue;
                        Fire(WallOrigin(at, aimYaw, k), aimYaw, WallSpeed);
                    }
                    WallRpc(at, aimYaw, (byte)gap);
                    nextTick = Time.time + 0.6f;
                    if (++step >= 3) songEnds = -1f;
                    break;
                }
            }
            VolleysFired++;
        }

        // ---- Pattern maths (shared by the server shots and every peer's visuals) ----

        private const int RoseCount = 30, WallNotes = 13, BurstCount = 10;
        private const float PodSpeed = 5.5f, WallSpeed = 5.6f, WallSpacing = 0.8f;

        /// <summary>Five petals: speed rises and falls five times round the ring.</summary>
        private static float RoseSpeed(int k) => SoulSpeed * (0.5f + 0.5f * Mathf.Abs(Mathf.Cos(2.5f * k * 2f * Mathf.PI / RoseCount)));

        private static Vector3 WallOrigin(Vector3 at, float yaw, int k)
        {
            var right = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward;
            return at + right * (k - (WallNotes - 1) / 2f) * WallSpacing;
        }

        private void Fire(Vector3 at, float yaw, float speed, float range = SoulRange) =>
            Arrows.ServerFire(at, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, SoulDamage, Team.Players, NetworkObjectId, speed, null, false, range);

        private void Burst(Vector3 end, int seed)
        {
            if (!IsSpawned) return;
            var at = end + Vector3.up * 0.1f;
            float offset = seed * 12f;
            for (int k = 0; k < BurstCount; k++) Fire(at, offset + k * 360f / BurstCount, SoulSpeed * 0.85f, 12f);
            BurstRpc(at, offset);
        }

        // ---- Presentation ----

        private static readonly string[] SongNames = { "ROSE", "SCISSORS", "SEEDPODS", "MARCH" };
        private static readonly float[] SongNotes = { 1f, 1.26f, 0.84f, 1.12f };

        private static void Visual(Vector3 at, float yaw, float speed, float range = SoulRange) =>
            Arrows.SpawnVisual(at, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, ProjectileKind.Soul, speed, range);

        [Rpc(SendTo.ClientsAndHost)]
        private void TellRpc(Vector3 at, byte which)
        {
            var head = at + Vector3.up * 2.4f;
            Vfx.Flash(head, Soul, 2f, 5f, 0.6f);
            Vfx.Embers(head, 10, Soul, 1.2f, 0.05f, 0.7f);
            UI.GameHud.ShowCombo(head + Vector3.up * 0.3f, SongNames[which % 4], Soul);
            // The opening bar of the tune: three breathy notes.
            float p = SongNotes[which % 4];
            Sfx.Play(Sound.Pipe, at + Vector3.up * 1.3f, 0.55f, p);
            if (!IsServer) Rig?.Play(RigAction.Cast, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FanRpc(Vector3 at, float yaw, float speed)
        {
            for (int s = -1; s <= 1; s++) Visual(at, yaw + s * 8f, speed);
            Sfx.Play(Sound.Pipe, at, 0.3f, 1.5f);
            if (!IsServer) Rig?.Play(RigAction.Cast, 0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RoseRpc(Vector3 at, float phase, float scale)
        {
            for (int k = 0; k < RoseCount; k++) Visual(at, phase + k * 360f / RoseCount, RoseSpeed(k) * scale);
            Sfx.Play(Sound.Pipe, at, 0.5f, scale > 0.9f ? 1.19f : 1.5f);
        }

        [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
        private void PairRpc(Vector3 at, float yawA, float yawB, float speed)
        {
            Visual(at, yawA, speed);
            Visual(at, yawB, speed);
            if (Random.value < 0.18f) Sfx.Play(Sound.Pipe, at, 0.25f, Random.Range(0.9f, 1.6f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PodRpc(Vector3 at, Vector3 dir, float range)
        {
            Arrows.SpawnVisual(at, dir, ProjectileKind.Seedpod, PodSpeed, range);
            Sfx.Play(Sound.Pipe, at, 0.4f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BurstRpc(Vector3 at, float offset)
        {
            for (int k = 0; k < BurstCount; k++) Visual(at, offset + k * 360f / BurstCount, SoulSpeed * 0.85f, 12f);
            Vfx.Flash(at + Vector3.up * 0.3f, Soul, 2f, 4f, 0.2f);
            Sfx.Play(Sound.Shatter, at, 0.35f, 1.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WallRpc(Vector3 at, float yaw, byte gap)
        {
            for (int k = 0; k < WallNotes; k++)
            {
                if (k >= gap && k < gap + 3) continue;
                Visual(WallOrigin(at, yaw, k), yaw, WallSpeed);
            }
            Sfx.Play(Sound.Pipe, at, 0.5f, 0.94f);
        }
    }
}
