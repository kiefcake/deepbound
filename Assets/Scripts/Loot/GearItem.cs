using System;
using Unity.Netcode;

namespace Deepbound.Loot
{
    /// <summary>
    /// One piece of gear, as it travels over the network: which named item, its quality,
    /// and the depth it was found at. Everything else (stats, effect, name) is derived from
    /// these three by <see cref="Gear"/>, identically on every peer.
    /// </summary>
    public struct GearItem : INetworkSerializable, IEquatable<GearItem>
    {
        public byte Id;       // GearId; 0 = empty slot
        public byte Quality;  // 0 Worn, 1 Fine, 2 Masterwork, 3 Legendary (boss pieces)
        public byte Depth;    // floor index it dropped on (0-2)

        public bool IsEmpty => Id == 0;
        public GearId Kind => (GearId)Id;

        public GearItem(GearId id, int quality, int depth)
        {
            Id = (byte)id;
            Quality = (byte)Math.Max(0, Math.Min(3, quality));
            Depth = (byte)Math.Max(0, depth);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Quality);
            serializer.SerializeValue(ref Depth);
        }

        public bool Equals(GearItem other) => Id == other.Id && Quality == other.Quality && Depth == other.Depth;
        public override bool Equals(object obj) => obj is GearItem g && Equals(g);
        public override int GetHashCode() => Id | (Quality << 8) | (Depth << 16);
    }
}
