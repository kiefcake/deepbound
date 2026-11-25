using UnityEngine;

namespace Deepbound.World
{
    public static class FolkPlaces
    {
        public static Vector3 Home(FolkId who)
        {
            if (who == FolkId.Marta) return TavernKeep.Instance != null ? TavernKeep.Instance.MartaAt : Lobby.TavernCenter;
            foreach (var (id, at) in Lobby.FolkBodies) if (id == who) return at;
            return Lobby.Origin;
        }

        public static Vector3 Now(FolkId who)
        {
            if (FolkRounds.Walks(who))
            {
                var pose = FolkRounds.At(who);
                if (pose.Away) return pose.Position;
            }
            return Home(who);
        }

        public static float TalkRange(FolkId who) => who == FolkId.Corwin ? 3.7f : who == FolkId.Marta ? 3.2f : 3.4f;

        public static bool InReach(FolkId who, Vector3 at, float slack = 0f)
        {
            var d = Now(who) - at;
            d.y = 0f;
            return d.magnitude <= TalkRange(who) + slack;
        }
    }
}
