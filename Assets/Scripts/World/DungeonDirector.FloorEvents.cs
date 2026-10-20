using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>Append only: synced.</summary>
    public enum FloorEvent : byte { None, Gilded, Swarm, Marching, BloodTithe, Bountiful }

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
    /// </list>
    /// </summary>
    public partial class DungeonDirector
    {
        public const float FloorEventChance = 0.4f, TitheHeal = 0.04f, TitheHurt = 1.15f;

        public readonly NetworkVariable<byte> FloorEventValue = new NetworkVariable<byte>();
        public static FloorEvent CurrentEvent => Instance != null && !InLobby ? (FloorEvent)Instance.FloorEventValue.Value : FloorEvent.None;

        public static string EventName(FloorEvent e) =>
            e == FloorEvent.Gilded ? "Gilded Halls" : e == FloorEvent.Swarm ? "The Swarm" : e == FloorEvent.Marching ? "The Dead March"
            : e == FloorEvent.BloodTithe ? "Blood Tithe" : e == FloorEvent.Bountiful ? "Bountiful" : "";

        public static string EventText(FloorEvent e) =>
            e == FloorEvent.Gilded ? "the dead carry far more gold"
            : e == FloorEvent.Swarm ? "more of the dead; more embers"
            : e == FloorEvent.Marching ? "more patrols walk the halls; more gold"
            : e == FloorEvent.BloodTithe ? "each kill mends you; every blow hurts more"
            : e == FloorEvent.Bountiful ? "every cleared room leaves something behind"
            : "";

        public static Color EventColor(FloorEvent e) =>
            e == FloorEvent.Gilded ? new Color(1f, 0.82f, 0.35f) : e == FloorEvent.Swarm ? new Color(0.85f, 0.75f, 0.6f)
            : e == FloorEvent.Marching ? new Color(0.8f, 0.85f, 0.9f) : e == FloorEvent.BloodTithe ? new Color(0.95f, 0.25f, 0.2f)
            : e == FloorEvent.Bountiful ? new Color(0.55f, 0.95f, 0.5f) : Color.white;

        // Effects, read on the server.
        public static float EventGold => CurrentEvent == FloorEvent.Gilded ? 1.6f : CurrentEvent == FloorEvent.Marching ? 1.25f : 1f;
        public static float EventCrowd => CurrentEvent == FloorEvent.Swarm ? 1.35f : 1f;
        public static float EventEmbers => CurrentEvent == FloorEvent.Swarm ? 1.4f : 1f;
        public static int EventPatrols => CurrentEvent == FloorEvent.Marching ? 2 : 0;
        public static float EventClearChance => CurrentEvent == FloorEvent.Bountiful ? 1f : RoomClearRewardChance;

        /// <summary>Server: roll this floor's event (before enemies spawn, so the crowd and patrols see it).</summary>
        private void RollFloorEvent(int depth)
        {
            var e = FloorEvent.None;
            if (!TestMode && depth >= 1 && Random.value < FloorEventChance) e = (FloorEvent)Random.Range(1, 6);
            FloorEventValue.Value = (byte)e;
            Combat.Health.FloorPlayerDamageScale = e == FloorEvent.BloodTithe ? TitheHurt : 1f;
        }

        private void ClearFloorEvent()
        {
            FloorEventValue.Value = 0;
            Combat.Health.FloorPlayerDamageScale = 1f;
        }

        /// <summary>Server, for tests: force an event on this floor.</summary>
        public void ServerDebugEvent(FloorEvent e)
        {
            FloorEventValue.Value = (byte)e;
            Combat.Health.FloorPlayerDamageScale = e == FloorEvent.BloodTithe ? TitheHurt : 1f;
        }

        /// <summary>Server: a player landed a killing blow (Blood Tithe pays it back in blood).</summary>
        public static void ServerOnPlayerKill(Combat.Health killer)
        {
            if (CurrentEvent != FloorEvent.BloodTithe || killer == null || !killer.IsAlive) return;
            killer.ServerHeal(killer.Max.Value * TitheHeal);
        }
    }
}
