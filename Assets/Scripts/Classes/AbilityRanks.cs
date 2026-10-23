namespace Deepbound.Classes
{
    public enum AbilitySlot : byte { Q = 0, E = 1 }

    /// <summary>
    /// Q and E each have three ranks, bought with Sigils at the anvil. Rank 2 makes the
    /// ability 30% stronger; rank 3 cuts its cooldown by a quarter and adds a twist — and the
    /// twists are chosen to feed the class combos (chill, stun, mark, shields), so a ranked
    /// ability is also a better setup for the rest of the party.
    /// </summary>
    public static class AbilityRanks
    {
        /// <summary>
        /// Five ranks (2026-10-23; there were eight, and ranks 4-8 were five clicks of +x% with no
        /// decision). Rank 4 is +15%; rank 5, MASTERED, is a choice: Overcharged (+30% strength) or
        /// Hastened (30% off the cooldown).
        /// </summary>
        public const int MaxRank = 5;
        public const float Rank2Potency = 1.3f, Rank3Cooldown = 0.75f, Rank4Potency = 1.15f;
        public const float OverchargedPotency = 1.3f, HastenedCooldown = 0.7f;

        /// <summary>Sigils to go from <paramref name="rank"/> to the next: 1, 1, 2, 3 (seven to master one ability).</summary>
        public static int SigilCost(int rank) => rank >= 4 ? 3 : rank >= 3 ? 2 : 1;

        /// <summary>Embers a spare Sigil melts into once both abilities are mastered.</summary>
        public const int MeltEmbers = 40;

        public static string NextRank(ClassId id, AbilitySlot slot, int rank) =>
            rank <= 1 ? "Rank 2: " + Rank2(id, slot)
            : rank == 2 ? "Rank 3: 25% shorter cooldown. " + Twist(id, slot)
            : rank == 3 ? "Rank 4: 15% stronger again."
            : rank == 4 ? "Rank 5, MASTERED - choose: OVERCHARGED (30% stronger) or HASTENED (30% shorter cooldown)."
            : "MASTERED. " + Twist(id, slot);

        public static string Twist(ClassId id, AbilitySlot slot)
        {
            bool q = slot == AbilitySlot.Q;
            switch (id)
            {
                case ClassId.Mage: return q
                    ? "Blink leaves a frost nova where you stood, chilling enemies within 3.5 m."
                    : "A second meteor falls on the same spot a moment later.";
                case ClassId.Cleric: return q
                    ? "Consecrated ground is half again as wide and lasts 9 s."
                    : "Divine Aegis also heals everyone it shields for 25% of their health.";
                case ClassId.Rogue: return q
                    ? "Shadowstep arms your opener: the next hit within 2 s deals double and stuns."
                    : "Smoke Bomb stuns every enemy within 5 m for 1.5 s.";
                case ClassId.Flagellant: return q
                    ? "Chains drag everything within 3 m of the target along with it."
                    : "The leap's crash hurls enemies back and stuns them half again as long.";
                case ClassId.PlagueDoctor: return q
                    ? "Miasma clouds are half again as wide."
                    : "The mask's breath steadies the party: Bloodlust (+15% damage) for 6 s.";
                case ClassId.Ranger: return q
                    ? "Bear traps mark whatever they catch."
                    : "Volley chills everything it hits.";
                default: return q
                    ? "Shield Bash becomes a shockwave: a full half-circle, a metre further."
                    : "War Cry shields every ally within reach for 40.";
            }
        }

        public static string Rank2(ClassId id, AbilitySlot slot)
        {
            bool q = slot == AbilitySlot.Q;
            switch (id)
            {
                case ClassId.Mage: return q ? "Blink 30% further." : "Meteor hits 30% harder and wider.";
                case ClassId.Cleric: return q ? "Consecrate heals and burns 30% more." : "Aegis shields 30% more.";
                case ClassId.Rogue: return q ? "Shadowstep reaches 30% further." : "Smoke lasts 30% longer.";
                case ClassId.Ranger: return q ? "Traps bite 30% harder." : "Volley hits 30% harder.";
                case ClassId.Flagellant: return q ? "Chains reach and bite 30% further and harder." : "The leap's crash hits 30% harder.";
                case ClassId.PlagueDoctor: return q ? "Miasma rots 30% harder." : "The mask heals 30% more.";
                default: return q ? "Bash hits 30% harder and stuns longer." : "War Cry lasts 30% longer and reaches further.";
            }
        }
    }
}
