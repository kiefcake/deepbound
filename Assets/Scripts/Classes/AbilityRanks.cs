using System.Collections.Generic;

namespace Deepbound.Classes
{
    public enum AbilitySlot : byte { Q = 0, E = 1 }

    /// <summary>
    /// Q and E each have five ranks, bought with Sigils at Ysolde's stones.
    ///
    /// REWORK (2026-11-20, the user: "upgrades on abilities should be unique fun and interesting, not +30 dmg or +30 size"):
    /// every rank from 2 to 5 is a CHOICE between two options that change what the ability does (tiers 1-4; option A or B).
    /// Choices are locked for the run (the user's call). Each rank also adds a quiet <see cref="PotencyPerRank"/> of strength,
    /// so the ability keeps pace with the deeper floors, but the headline of every rank is the choice.
    /// The effects live in each kit, gated by <c>PlayerProgress.A(slot, tier)</c> / <c>B(slot, tier)</c>.
    /// </summary>
    public static class AbilityRanks
    {
        public const int MaxRank = 5, Tiers = 4;
        public const float PotencyPerRank = 0.1f;

        /// <summary>Sigils to go from <paramref name="rank"/> to the next: 1, 1, 2, 3 (seven to master one ability).</summary>
        public static int SigilCost(int rank) => rank >= 4 ? 3 : rank >= 3 ? 2 : 1;

        /// <summary>Embers a spare Sigil melts into once both abilities are mastered.</summary>
        public const int MeltEmbers = 40;

        public struct Option
        {
            public string Name, Text;
            public Option(string name, string text) { Name = name; Text = text; }
        }

        /// <summary>The option at <paramref name="tier"/> (1-4, = rank 2-5); <paramref name="b"/> picks the second.</summary>
        public static Option Get(ClassId id, AbilitySlot slot, int tier, bool b)
        {
            if (!table.TryGetValue((id, slot), out var list) || tier < 1 || tier > Tiers) return new Option("", "");
            return list[(tier - 1) * 2 + (b ? 1 : 0)];
        }

        private static Option O(string n, string t) => new Option(n, t);

        private static readonly Dictionary<(ClassId, AbilitySlot), Option[]> table = new Dictionary<(ClassId, AbilitySlot), Option[]>
        {
            // ---------------- KNIGHT ----------------
            { (ClassId.Knight, AbilitySlot.Q), new[] {
                O("Ricochet", "The bash's force leaps on to the nearest other enemy within 6 m: it is struck and stunned too."),
                O("Shield Rush", "The lunge runs twice as far, and the first enemy you meet is driven before you and hurled back."),
                O("Aftershock", "The ground cracks where you bash; a moment later it heaves, stunning everything within 3.5 m again."),
                O("Hilt Strike", "Your next swing within 2.5 s on a bashed enemy is a hilt-blow: triple damage, and it is knocked flying."),
                O("Domino", "A bashed enemy is hurled into the ones behind it: everything within 2 m of where it lands is struck and stunned."),
                O("Braced", "For 2 s after a bash your shield guards you from every side, without holding it up."),
                O("Shield Throw", "The bash also hurls a ghostly shield 12 m along your aim; it cuts through everything in its path, out and back."),
                O("Earthsplitter", "The bash ends in a slam: a fissure runs 8 m ahead and bursts, throwing up and stunning all it crosses."),
            } },
            { (ClassId.Knight, AbilitySlot.E), new[] {
                O("Challenge", "The toughest enemy you taunt is marked: the whole party hits it harder."),
                O("Rally", "Allies in reach are rallied while the cry lasts: they run faster and hit harder."),
                O("Iron Thorns", "While the cry lasts, whatever strikes you takes half the blow back."),
                O("Unbreakable", "While the cry lasts, blocking costs no stamina and your guard cannot break."),
                O("Dread", "Ordinary enemies in reach cower for 1.5 s before they come for you."),
                O("Bastion", "While the cry lasts, allies within 8 m of you take a quarter less."),
                O("Echo", "The cry sounds again 3 s later, taunting everything anew."),
                O("Last Oath", "While the cry lasts, a blow that would down you leaves you at 1 health and ends the cry instead."),
            } },
            // ---------------- MAGE ----------------
            { (ClassId.Mage, AbilitySlot.Q), new[] {
                O("Undertow", "The wave drags what it strikes back toward you instead of washing it away."),
                O("Floodwater", "The wave leaves a flooded strip for 5 s; enemies wading in it are chilled."),
                O("Ice-Cold", "Everything the wave strikes is chilled, ready to Shatter."),
                O("Second Swell", "A second wave rolls out just after the first."),
                O("Maelstrom Ring", "The wave rolls out all round you, a little shorter."),
                O("Crushing Weight", "Each enemy struck takes a quarter more for every other one the wave catches (up to double)."),
                O("Whirlpool", "Where the wave breaks, a whirlpool turns for 3 s, dragging the dead into its eye."),
                O("Scalding Steam", "A Firebolt on anything the wave soaked in the last 4 s bursts into steam, scalding everything near."),
            } },
            { (ClassId.Mage, AbilitySlot.E), new[] {
                O("Comet Trail", "The meteor ploughs a burning furrow from you to where it lands."),
                O("Gravity Well", "Just before it lands, the meteor drags everything within 7 m into its circle."),
                O("Twin Star", "A second stone falls on the same spot a moment later."),
                O("Molten Crater", "The crater burns for 5 s, scorching anything that stands in it."),
                O("Starshower", "Three small stars scatter round the crater after the impact."),
                O("Frostfall", "The meteor is ice: everything it hits is frozen solid for 1.5 s and left chilled."),
                O("Chain of Stars", "Each enemy the meteor kills calls a star down on the nearest other foe (up to three)."),
                O("Aftermath", "Two seconds later the crater erupts again, wider."),
            } },
            // ---------------- CLERIC ----------------
            { (ClassId.Cleric, AbilitySlot.Q), new[] {
                O("Holy Stride", "The hallowed ground follows you for as long as it lasts."),
                O("Thrown Blessing", "Consecrate lands where you aim (up to 16 m), not at your feet."),
                O("Searing Ground", "As it lands, the ground sears: enemies inside are scorched and stunned for a second."),
                O("Sanctuary", "An ally inside who would be downed is left at 1 health instead (once each, per cast)."),
                O("Judgement", "Every 1.5 s a pillar of light strikes an enemy inside."),
                O("Purifying", "Allies who stand inside are cleansed of afflictions."),
                O("Hallowed Burst", "When it ends it bursts: allies inside are healed 20%, enemies inside are hurt and stunned."),
                O("Everburning", "Each enemy that dies inside keeps it burning 2 s longer and heals everyone in it."),
            } },
            { (ClassId.Cleric, AbilitySlot.E), new[] {
                O("Thorned Aegis", "Whatever strikes a shielded ally takes a third of the blow back."),
                O("Swift Wings", "Shielded allies are rallied for 4 s: they run faster and hit harder."),
                O("Mending", "The Aegis also heals everyone it shields for 25% of their health."),
                O("Purge", "The Aegis cleanses afflictions and wards against new ones for 4 s."),
                O("Halo", "A ring of light 5 m round you for 4 s throws back any enemy that crosses it."),
                O("Martyr's Share", "The most wounded ally gets a shield twice as strong."),
                O("Breaking Light", "When an Aegis breaks, it bursts: the enemies round that ally are hurt and stunned."),
                O("Echo of Grace", "A shield that runs out unbroken pays back what is left of it as healing."),
            } },
            // ---------------- ROGUE ----------------
            { (ClassId.Rogue, AbilitySlot.Q), new[] {
                O("Shadow Gash", "You arrive cutting: the enemy bleeds for 4 s."),
                O("Double Step", "Within 3 s of a Shadowstep you can step again, free."),
                O("Opener", "Your next hit within 2 s deals double and stuns."),
                O("Switch", "The enemy is flung to where you stood, dazed."),
                O("Death Mark", "The enemy is marked for 6 s: the whole party hits it harder."),
                O("Hamstring", "The enemy is hamstrung: chilled and slowed for 4 s."),
                O("Blade Dance", "The step cuts through up to two more enemies within 8 m of the first on its way."),
                O("Unseen", "For 1.5 s after a step nothing can target you."),
            } },
            { (ClassId.Rogue, AbilitySlot.E), new[] {
                O("Choking Smoke", "Enemies in the cloud cough and choke, poisoned while it lasts."),
                O("Blinding Flash", "The bomb flashes: enemies within 5 m are stunned for 1.5 s."),
                O("Caltrops", "The cloud is strewn with caltrops: enemies crossing it are cut and slowed."),
                O("Vanishing Act", "The smoke cleanses you and mends 15% of your health."),
                O("Flashpowder", "The bomb bursts in fire: everything within 4 m is burned and scorched."),
                O("Ghost's Stride", "While the smoke hides you, you are rallied: you run faster and hit harder."),
                O("Smoke Chain", "A kill from the smoke drops a fresh bomb at your feet (once every few seconds)."),
                O("Marked for Death", "Every enemy within 8 m is marked as the bomb bursts."),
            } },
            // ---------------- RANGER ----------------
            { (ClassId.Ranger, AbilitySlot.Q), new[] {
                O("Marking Net", "Everything the net catches is marked: the whole party hits it harder."),
                O("Barbed Net", "Everything the net catches bleeds for 4 s."),
                O("Tether", "The net hauls everything it catches into its middle."),
                O("Second Net", "Within 3 s of a Snare Shot you can loose another, free."),
                O("Shock Net", "When the net lets go it shocks: everything still in it is stunned again."),
                O("Hunter's Larder", "Killing a snared enemy readies Snare Shot 4 s sooner and returns Focus."),
                O("Weighted Blast", "The stone weights explode a moment after the net lands."),
                O("Chain Net", "A second net springs onto the nearest enemy within 10 m that the first one missed."),
            } },
            { (ClassId.Ranger, AbilitySlot.E), new[] {
                O("Frost Arrows", "The volley chills everything it hits."),
                O("Fire Arrows", "The volley scorches everything it hits."),
                O("Creeping Barrage", "The volley walks forward a metre each wave, away from you."),
                O("Pinning Rain", "Each wave pins ordinary enemies in place for a moment."),
                O("The Last Arrow", "The volley ends with one great arrow: triple damage, and it throws them back."),
                O("Sheltering Rain", "Allies under the arrows take a fifth less while they fall."),
                O("Seeker Volley", "The volley follows the nearest snared or marked enemy."),
                O("Rain of Marks", "Anything struck by three waves is marked."),
            } },
            // ---------------- FLAGELLANT ----------------
            { (ClassId.Flagellant, AbilitySlot.Q), new[] {
                O("Dragnet", "Everything within 3 m of the target is dragged in with it."),
                O("Lash", "The chain whips everything along its line on the way out."),
                O("Hooked", "The dragged enemy bleeds for 4 s, and you heal for what it bleeds."),
                O("Chain Sweep", "You haul it in with a sweep: everything within 2.5 m of you is hurt and knocked off its feet."),
                O("Shackled", "The dragged enemy is held twice as long and marked."),
                O("Reel-In Crash", "When the chain drags you to something heavy, you land in a crash."),
                O("Double Chain", "The chain leaps on to a second enemy within 8 m and drags it too."),
                O("Penance", "Each enemy you drag heals you 6% and gives 20 Fervor."),
            } },
            { (ClassId.Flagellant, AbilitySlot.E), new[] {
                O("Shockwave", "The crash hurls enemies back and stuns them half again as long."),
                O("Brand", "The crash leaves holy fire burning on the ground for 4 s."),
                O("Second Leap", "Within 3 s of landing you can leap again, free."),
                O("Blood Rain", "Allies within 5 m are healed by the Fervor you spend."),
                O("Crater Pull", "Enemies within 8 m are dragged to where you land."),
                O("Martyr's Fall", "The crash costs you 10% of your health and hits twice as hard."),
                O("Quake", "Everything within 6 m of the crash is stunned for 2 s."),
                O("Afterquake", "Three ripples spread from the crash, each wider than the last."),
            } },
            // ---------------- PLAGUE DOCTOR ----------------
            { (ClassId.PlagueDoctor, AbilitySlot.Q), new[] {
                O("Creeping Cloud", "The cloud drifts toward the nearest enemy."),
                O("Clinging Fog", "Enemies inside are chilled and slowed."),
                O("Contagious", "Enemies that die inside burst into a small cloud of their own."),
                O("Fog of Mercy", "Allies inside are healed 3% a second."),
                O("Choking", "Every 2 s the enemies inside choke on it, and their wind-ups are cut short."),
                O("Volatile", "A vial that bursts inside ignites the cloud: a heavy blast, and the cloud is spent."),
                O("Twin Clouds", "Two clouds, one each side of where you aim."),
                O("Plague Wind", "When the cloud ends it blows outward, blighting every enemy within 10 m."),
            } },
            { (ClassId.PlagueDoctor, AbilitySlot.E), new[] {
                O("Bloodlust", "The mask's breath steadies the party: Bloodlust (+15% damage) for 6 s."),
                O("Vapour Shield", "Everyone it reaches is shielded for 15% of their health."),
                O("Purge", "Every affliction cleansed from an ally burns the enemies round them."),
                O("Deep Breath", "The ward lasts twice as long and mends 2% a second while it holds."),
                O("Exhale", "The mask also leaves a Miasma cloud at your feet."),
                O("Repel", "The breath throws back every enemy within 5 m."),
                O("Plague Lord", "Every enemy within 8 m is blighted."),
                O("Last Breath", "A downed ally in reach is lifted to their feet at 25%."),
            } },
        };
    }
}
