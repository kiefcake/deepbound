namespace Deepbound.Enemies
{
    public enum EliteAffix : byte { None = 0, Warded = 1, Vampiric = 2, Volatile = 3, Swift = 4, Splitting = 5, Thorned = 6, Frenzied = 7, Gravebound = 8, Hushing = 9, Concussive = 10, Soulbound = 11, Emberbound = 12 }

    public static class EliteAffixes
    {
        public const int Count = 11;
        public static EliteAffix Roll() { int r = UnityEngine.Random.Range(1, Count + 1); return r == 11 ? EliteAffix.Emberbound : (EliteAffix)r; }

        public static string Tag(EliteAffix a)
        {
            switch (a)
            {
                case EliteAffix.Warded: return "WARDED";
                case EliteAffix.Vampiric: return "VAMPIRIC";
                case EliteAffix.Volatile: return "VOLATILE";
                case EliteAffix.Swift: return "SWIFT";
                case EliteAffix.Splitting: return "SPLITTING";
                case EliteAffix.Thorned: return "THORNED";
                case EliteAffix.Frenzied: return "FRENZIED";
                case EliteAffix.Gravebound: return "GRAVEBOUND";
                case EliteAffix.Hushing: return "HUSHING";
                case EliteAffix.Concussive: return "CONCUSSIVE";
                case EliteAffix.Soulbound: return "SOULBOUND";
                case EliteAffix.Emberbound: return "EMBERBOUND";
                default: return "";
            }
        }

        public static string Hex(EliteAffix a)
        {
            switch (a)
            {
                case EliteAffix.Warded: return "B9A8FF";
                case EliteAffix.Vampiric: return "E0405A";
                case EliteAffix.Volatile: return "FF8A2A";
                case EliteAffix.Splitting: return "D8D0B8";
                case EliteAffix.Thorned: return "7FB04A";
                case EliteAffix.Frenzied: return "FF4A3A";
                case EliteAffix.Gravebound: return "A8B4A0";
                case EliteAffix.Hushing: return "C7C7FF";
                case EliteAffix.Concussive: return "F2D98C";
                case EliteAffix.Soulbound: return "C4D6FF";
                case EliteAffix.Emberbound: return "FF7A2A";
                default: return "9AF0A0";
            }
        }
    }
}
