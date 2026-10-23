using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>Append only: synced.</summary>
    public enum FloorEvent : byte { None, Gilded, Swarm, Marching, BloodTithe, Bountiful, LongNight, Embertide, BoneChoir }

    /// <summary>
    /// Floor events (2026-10-20): an omen sets the whole run; an event is one floor's twist, rolled when the
    /// floor begins (40% from floor 2, never in tests) and named on the floor banner. Each trades a risk for
    /// a reward, so no two descents feel alike:
    /// <list type="bullet">
    /// <item><b>Gilded Halls</b>: the dead carry 60% more gold.</item>
    /// <item><b>The Swarm</b>: 35% more of the dead; 40% more embers.</item>
    /// <item><b>The Dead March</b>: two more patrols walk the halls; 25% more gold.</item>
    /// <item><b>Blood Tithe</b>: every kill mends the killer 4%, but you take 15% more damage.</item>
    /// <item><b>Bountiful</b>: every cleared room leaves something behind.</item>
    /// <item><b>The Long Night</b> (2026-10-23): twice the elites, and they carry half as much again.</item>
    /// <item><b>Embertide</b>: the dead smoulder when struck, so any frost cracks them (Thermal Shock); their blows hurt 10% more.</item>
    /// <item><b>The Bone Choir</b>: elites sing slow rings of souls to weave through; 30% more embers.</item>
    /// </list>
    /// </summary>
    public partial class DungeonDirector
    {
        public const float FloorEventChance = 0.4f, TitheHeal = 0.04f, TitheHurt = 1.15f;

        public readonly NetworkVariable<byte> FloorEventValue = new NetworkVariable<byte>();
        public static FloorEvent CurrentEvent => Instance != null && !InLobby ? (FloorEvent)Instance.FloorEventValue.Value : FloorEvent.None;

        public static string EventName(FloorEvent e) =>
            e == FloorEvent.Gilded ? "Gilded Halls" : e == FloorEvent.Swarm ? "The Swarm" : e == FloorEvent.Marching ? "The Dead March"
            : e == FloorEvent.BloodTithe ? "Blood Tithe" : e == FloorEvent.Bountiful ? "Bountiful"
            : e == FloorEvent.LongNight ? "The Long Night" : e == FloorEvent.Embertide ? "Embertide" : e == FloorEvent.BoneChoir ? "The Bone Choir" : "";

        public static string EventText(FloorEvent e) =>
            e == FloorEvent.Gilded ? "the dead carry far more gold"
            : e == FloorEvent.Swarm ? "more of the dead; more embers"
            : e == FloorEvent.Marching ? "more patrols walk the halls; more gold"
            : e == FloorEvent.BloodTithe ? "each kill mends you; every blow hurts more"
            : e == FloorEvent.Bountiful ? "every cleared room leaves something behind"
            : e == FloorEvent.LongNight ? "twice the elites, and richer ones"
            : e == FloorEvent.Embertide ? "the dead smoulder: frost cracks them (Thermal Shock); their blows hurt more"
            : e == FloorEvent.BoneChoir ? "elites sing rings of souls; more embers"
            : "";

        public static Color EventColor(FloorEvent e) =>
            e == FloorEvent.Gilded ? new Color(1f, 0.82f, 0.35f) : e == FloorEvent.Swarm ? new Color(0.85f, 0.75f, 0.6f)
            : e == FloorEvent.Marching ? new Color(0.8f, 0.85f, 0.9f) : e == FloorEvent.BloodTithe ? new Color(0.95f, 0.25f, 0.2f)
            : e == FloorEvent.Bountiful ? new Color(0.55f, 0.95f, 0.5f)
            : e == FloorEvent.LongNight ? new Color(0.6f, 0.55f, 0.95f) : e == FloorEvent.Embertide ? new Color(1f, 0.55f, 0.85f)
            : e == FloorEvent.BoneChoir ? new Color(0.85f, 0.95f, 1f) : Color.white;

        // Effects, read on the server.
        public static float EventGold => CurrentEvent == FloorEvent.Gilded ? 1.6f : CurrentEvent == FloorEvent.Marching ? 1.25f : 1f;
        public static float EventCrowd => CurrentEvent == FloorEvent.Swarm ? 1.35f : 1f;
        public static float EventEmbers => CurrentEvent == FloorEvent.Swarm ? 1.4f : CurrentEvent == FloorEvent.BoneChoir ? 1.3f : 1f;
        public static float EventElites => CurrentEvent == FloorEvent.LongNight ? 2f : 1f;
        public static float EventEliteLoot => CurrentEvent == FloorEvent.LongNight ? 1.5f : 1f;
        public const float EmbertideHurt = 1.1f, ChoirInterval = 6f, ChoirRange = 18f;
        public static int EventPatrols => CurrentEvent == FloorEvent.Marching ? 2 : 0;
        public static float EventClearChance => CurrentEvent == FloorEvent.Bountiful ? 1f : RoomClearRewardChance;

        /// <summary>Server: roll this floor's event (before enemies spawn, so the crowd and patrols see it).</summary>
        private void RollFloorEvent(int depth)
        {
            var e = FloorEvent.None;
            if (!TestMode && depth >= 1 && Random.value < FloorEventChance) e = (FloorEvent)Random.Range(1, 9);
            FloorEventValue.Value = (byte)e;
            Combat.Health.FloorPlayerDamageScale = PlayerHurtFor(e);
        }

        private void ClearFloorEvent()
        {
            FloorEventValue.Value = 0;
            Combat.Health.FloorPlayerDamageScale = 1f;
        }

        /// <summary>Server, for tests: force an event on this floor.</summary>
        public void ServerDebugEvent(FloorEvent e)
        {
            HookEmbertide();
            FloorEventValue.Value = (byte)e;
            Combat.Health.FloorPlayerDamageScale = PlayerHurtFor(e);
        }

        private static float PlayerHurtFor(FloorEvent e) => e == FloorEvent.BloodTithe ? TitheHurt : e == FloorEvent.Embertide ? EmbertideHurt : 1f;

        private static bool embertideHooked;

        /// <summary>Server, once: Embertide scorches whatever a player strikes (so frost then cracks it).</summary>
        private static void HookEmbertide()
        {
            if (embertideHooked) return;
            embertideHooked = true;
            Combat.Health.ServerAnyDamaged += (h, dealt, by) =>
            {
                if (CurrentEvent != FloorEvent.Embertide || h == null || h.Team != Combat.Team.Enemies || !h.IsAlive || h.Scorched.Value || h.Chilled.Value) return;
                var nm = NetworkManager.Singleton;
                if (nm == null || by == ulong.MaxValue || !nm.SpawnManager.SpawnedObjects.TryGetValue(by, out var o) || !o.TryGetComponent<Progression.PlayerProgress>(out _)) return;
                h.ServerScorch(6f, by);
            };
        }

        private float nextChoir;

        /// <summary>Server, each frame of a floor: the Bone Choir's elites sing.</summary>
        private void TickBoneChoir()
        {
            HookEmbertide();
            if (CurrentEvent != FloorEvent.BoneChoir || Time.time < nextChoir) return;
            nextChoir = Time.time + 0.5f;
            foreach (var b in Enemies.EnemyBrain.All)
            {
                if (b == null || b.IsDead || !b.Elite.Value || b is Enemies.BossBrain || Time.time < b.ChoirReadyAt) continue;
                var nearest = Player.PlayerLife.All.Find(p => p != null && !p.Downed.Value && Vector3.Distance(p.transform.position, b.transform.position) < ChoirRange);
                if (nearest == null) continue;
                b.ChoirReadyAt = Time.time + ChoirInterval + Random.Range(0f, 1.5f);
                var origin = b.transform.position + Vector3.up * 1.4f;
                float start = Random.Range(0f, 360f);
                const int arms = 10;
                for (int i = 0; i < arms; i++)
                {
                    var dir = Quaternion.Euler(0f, start + i * 360f / arms, 0f) * Vector3.forward;
                    Combat.Arrows.ServerFire(origin, dir, 9f * Enemies.Difficulty.DepthDamage(Layout != null ? Layout.Depth : 0), Combat.Team.Players, b.NetworkObjectId, 7.5f, null, false, 16f);
                }
                ChoirRingRpc(origin, start, arms);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChoirRingRpc(Vector3 origin, float start, int arms)
        {
            for (int i = 0; i < arms; i++)
                Combat.Arrows.SpawnVisual(origin, Quaternion.Euler(0f, start + i * 360f / arms, 0f) * Vector3.forward, Combat.ProjectileKind.Soul, 7.5f, 16f);
            Art.Sfx.Play(Art.Sound.Whisper, origin, 0.6f, 0.8f);
        }

        /// <summary>Server: a player landed a killing blow (Blood Tithe pays it back in blood).</summary>
        public static void ServerOnPlayerKill(Combat.Health killer)
        {
            if (CurrentEvent != FloorEvent.BloodTithe || killer == null || !killer.IsAlive) return;
            killer.ServerHeal(killer.Max.Value * TitheHeal);
        }
    }
}
