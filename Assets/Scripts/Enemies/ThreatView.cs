using Unity.Netcode;

namespace Deepbound.Enemies
{
    /// <summary>
    /// What an elite, champion or boss's rage looks like to the party (synced a few times a second):
    /// up to four players' rage and who it is hunting. Plain data, copied byte for byte.
    /// </summary>
    public struct ThreatView : INetworkSerializeByMemcpy, System.IEquatable<ThreatView>
    {
        public ulong Id0, Id1, Id2, Id3;
        public float T0, T1, T2, T3;
        /// <summary>The NetworkObjectId of the player it is after (0: nobody).</summary>
        public ulong Target;

        public int Count => (Id0 != 0 ? 1 : 0) + (Id1 != 0 ? 1 : 0) + (Id2 != 0 ? 1 : 0) + (Id3 != 0 ? 1 : 0);

        public void Get(int i, out ulong id, out float threat)
        {
            switch (i)
            {
                case 0: id = Id0; threat = T0; break;
                case 1: id = Id1; threat = T1; break;
                case 2: id = Id2; threat = T2; break;
                default: id = Id3; threat = T3; break;
            }
        }

        public void Set(int i, ulong id, float threat)
        {
            switch (i)
            {
                case 0: Id0 = id; T0 = threat; break;
                case 1: Id1 = id; T1 = threat; break;
                case 2: Id2 = id; T2 = threat; break;
                default: Id3 = id; T3 = threat; break;
            }
        }

        public float Of(ulong id)
        {
            for (int i = 0; i < 4; i++) { Get(i, out var a, out var t); if (a == id && a != 0) return t; }
            return 0f;
        }

        public bool Equals(ThreatView o) => Id0 == o.Id0 && Id1 == o.Id1 && Id2 == o.Id2 && Id3 == o.Id3 && Target == o.Target
            && System.Math.Abs(T0 - o.T0) < 0.5f && System.Math.Abs(T1 - o.T1) < 0.5f && System.Math.Abs(T2 - o.T2) < 0.5f && System.Math.Abs(T3 - o.T3) < 0.5f;
    }
}
