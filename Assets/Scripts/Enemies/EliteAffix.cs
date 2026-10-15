namespace Deepbound.Enemies
{
    /// <summary>
    /// What makes an elite more than a bigger skeleton. APPEND ONLY (synced as a byte).
    /// Most affixes are answered by a class combo, so elites are where the party learns to
    /// play together: stun or chill a Warded elite to break its ward, Mark a Vampiric one
    /// to stop it healing, chill a Swift one to bring it back to walking pace.
    /// </summary>
    public enum EliteAffix : byte { None = 0, Warded = 1, Vampiric = 2, Volatile = 3, Swift = 4, Splitting = 5, Thorned = 6 }

    public static class EliteAffixes
    {
        public const int Count = 6;

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
                default: return "9AF0A0";
            }
        }
    }
}
