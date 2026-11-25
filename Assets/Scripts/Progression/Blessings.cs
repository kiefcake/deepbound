using UnityEngine;

namespace Deepbound.Progression
{
    public enum Blessing : byte { None = 0, Wrath = 1, Bulwark = 2, Swiftness = 3, Fortune = 4, Vigor = 5 }

    public static class Blessings
    {
        public const int Count = 5;

        public static string Name(Blessing b)
        {
            switch (b)
            {
                case Blessing.Wrath: return "Blessing of Wrath";
                case Blessing.Bulwark: return "Blessing of the Bulwark";
                case Blessing.Swiftness: return "Blessing of Swiftness";
                case Blessing.Fortune: return "Blessing of Fortune";
                case Blessing.Vigor: return "Blessing of Vigor";
                default: return "No blessing";
            }
        }

        public static string Describe(Blessing b)
        {
            switch (b)
            {
                case Blessing.Wrath: return "+15% damage dealt";
                case Blessing.Bulwark: return "15% less damage taken";
                case Blessing.Swiftness: return "+12% speed, abilities recharge 15% faster";
                case Blessing.Fortune: return "+50% gold and embers picked up, and a Sigil now";
                case Blessing.Vigor: return "+25% maximum health";
                default: return "";
            }
        }

        public static Color Color(Blessing b)
        {
            switch (b)
            {
                case Blessing.Wrath: return new Color(1f, 0.4f, 0.3f);
                case Blessing.Bulwark: return new Color(0.7f, 0.8f, 1f);
                case Blessing.Swiftness: return new Color(0.5f, 1f, 0.6f);
                case Blessing.Fortune: return new Color(1f, 0.85f, 0.35f);
                default: return new Color(1f, 0.55f, 0.65f);
            }
        }

        public static Blessing OfferFor(ulong clientId, int campSeed) => OffersFor(clientId, campSeed)[0];

        public static Blessing[] OffersFor(ulong clientId, int campSeed)
        {
            var rng = new System.Random(campSeed * 17 + (int)clientId * 131);
            int a = rng.Next(Count), b = rng.Next(Count - 1);
            if (b >= a) b++;
            return new[] { (Blessing)(1 + a), (Blessing)(1 + b) };
        }

        public static int Price(int depth) => 60 + 30 * depth;
    }
}
