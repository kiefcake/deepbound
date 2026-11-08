using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Where a camp person is RIGHT NOW, the same on every peer (the rounds run on the server clock). Trading,
    /// talking and the server's reach checks follow the person, not their stall (2026-11-08, the user: "tie the
    /// dialogue option to the character not where they are").
    /// </summary>
    public static class FolkPlaces
    {
        public static Vector3 Home(FolkId who)
        {
            if (who == FolkId.Marta) return TavernKeep.Instance != null ? TavernKeep.Instance.MartaAt : Lobby.TavernCenter;
            foreach (var (id, at) in Lobby.FolkBodies) if (id == who) return at;
            return Lobby.Origin;
        }

        /// <summary>Their feet now: on an errand, where the errand has them; otherwise home.</summary>
        public static Vector3 Now(FolkId who)
        {
            if (FolkRounds.Walks(who))
            {
                var pose = FolkRounds.At(who);
                if (pose.Away) return pose.Position;
            }
            return Home(who);
        }

        /// <summary>How close you stand to talk or trade (Corwin sits up on the carriage box, Marta behind her bar).</summary>
        public static float TalkRange(FolkId who) => who == FolkId.Corwin ? 3.7f : who == FolkId.Marta ? 3.2f : 3.4f;

        /// <summary>Within talking range, flat distance (people stand on boxes and behind counters).</summary>
        public static bool InReach(FolkId who, Vector3 at, float slack = 0f)
        {
            var d = Now(who) - at;
            d.y = 0f;
            return d.magnitude <= TalkRange(who) + slack;
        }
    }
}
