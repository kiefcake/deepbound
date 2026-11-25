using System.Collections.Generic;

namespace Deepbound.Classes
{
    public enum AbilitySlot : byte { Q = 0, E = 1 }

    public static class AbilityRanks
    {
        public const int MaxRank = 5, Tiers = 4;

        public const float PotencyPerRank = 0.15f, CooldownPerRank = 0.06f;

        public static int SigilCost(int rank) => rank >= 4 ? 3 : rank >= 3 ? 2 : 1;

        public const int MeltEmbers = 40;

        public struct Option
        {
            public string Name, Text;
            public Option(string name, string text) { Name = name; Text = text; }
        }

        public static Option Get(ClassId id, AbilitySlot slot, int tier, bool b)
        {
            if (!table.TryGetValue((id, slot), out var list) || tier < 1 || tier > Tiers) return new Option("", "");
            return list[(tier - 1) * 2 + (b ? 1 : 0)];
        }

        private static Option O(string n, string t) => new Option(n, t);

        private static readonly Dictionary<(ClassId, AbilitySlot), Option[]> table = new Dictionary<(ClassId, AbilitySlot), Option[]>
        {
            { (ClassId.Knight, AbilitySlot.Q), new[] {
                O("Ricochet", "The bash also strikes and stuns the nearest other enemy within 6 m."),
                O("Shield Rush", "The lunge travels twice as far, driving the first enemy hit before you and knocking it back."),
                O("Aftershock", "The ground cracks on impact, then erupts, stunning all enemies within 3.5 m again."),
                O("Hilt Strike", "Your next swing on a bashed enemy within 2.5 s deals triple damage and knocks it flying."),
                O("Domino", "The bashed enemy is hurled backwards, striking and stunning all enemies within 2 m of where it lands."),
                O("Braced", "For 2 s after a bash, you block attacks from all directions without raising your shield."),
                O("Shield Throw", "The bash also throws a spectral shield 12 m forward that damages everything in its path, out and back."),
                O("Earthsplitter", "The bash ends in a slam that sends an 8 m fissure forward, damaging and stunning all enemies it crosses."),
            } },
            { (ClassId.Knight, AbilitySlot.E), new[] {
                O("Challenge", "The toughest taunted enemy is Marked: it takes increased damage from the whole party."),
                O("Rally", "Nearby allies are Rallied for the duration: increased movement speed and damage."),
                O("Iron Thorns", "For the duration, attackers take 50% of the damage they deal to you."),
                O("Unbreakable", "For the duration, blocking costs no stamina and your guard cannot break."),
                O("Dread", "Nearby non-elite enemies cower for 1.5 s before engaging you."),
                O("Bastion", "For the duration, allies within 8 m take 25% less damage."),
                O("Echo", "The cry repeats after 3 s, taunting all nearby enemies again."),
                O("Last Oath", "For the duration, a blow that would down you leaves you at 1 health and ends the cry instead."),
            } },

            { (ClassId.Mage, AbilitySlot.Q), new[] {
                O("Undertow", "The wave pulls enemies toward you instead of pushing them away."),
                O("Floodwater", "The wave leaves a flooded strip for 5 s that chills enemies standing in it."),
                O("Ice-Cold", "The wave chills every enemy it hits, priming them to Shatter."),
                O("Second Swell", "A second wave follows shortly after the first."),
                O("Maelstrom Ring", "The wave expands in all directions, with slightly reduced range."),
                O("Crushing Weight", "Each enemy hit takes 25% more damage for every other enemy caught (up to double)."),
                O("Whirlpool", "A whirlpool forms where the wave breaks, pulling enemies into its centre for 3 s."),
                O("Scalding Steam", "Firebolts on enemies soaked in the last 4 s burst into steam, damaging everything nearby."),
            } },
            { (ClassId.Mage, AbilitySlot.E), new[] {
                O("Comet Trail", "The meteor carves a burning trail from you to the impact point."),
                O("Gravity Well", "Just before impact, the meteor pulls all enemies within 7 m into its blast radius."),
                O("Twin Star", "A second meteor strikes the same spot shortly after."),
                O("Molten Crater", "The crater burns for 5 s, scorching enemies standing in it."),
                O("Starshower", "Three smaller stars scatter around the crater after impact."),
                O("Frostfall", "The meteor is made of ice: enemies hit are frozen for 1.5 s and left chilled."),
                O("Chain of Stars", "Each kill calls a star down on the nearest other enemy (up to three)."),
                O("Aftermath", "The crater erupts again 2 s later, with a wider blast."),
            } },

            { (ClassId.Cleric, AbilitySlot.Q), new[] {
                O("Holy Stride", "The consecrated ground follows you for its duration."),
                O("Thrown Blessing", "Consecrate lands at your aim point (up to 16 m) instead of at your feet."),
                O("Searing Ground", "On landing, enemies inside are scorched and stunned for 1 s."),
                O("Sanctuary", "Allies inside who would be downed are left at 1 health instead (once per ally, per cast)."),
                O("Judgement", "Every 1.5 s, a pillar of light strikes an enemy inside."),
                O("Purifying", "Allies standing inside are cleansed of afflictions."),
                O("Hallowed Burst", "On expiry, it bursts: allies inside are healed for 20%, and enemies inside are damaged and stunned."),
                O("Everburning", "Each enemy killed inside extends the duration by 2 s and heals allies inside."),
            } },
            { (ClassId.Cleric, AbilitySlot.E), new[] {
                O("Thorned Aegis", "Attackers take a third of the damage they deal to shielded allies."),
                O("Swift Wings", "Shielded allies are Rallied for 4 s: increased movement speed and damage."),
                O("Mending", "The Aegis also heals each shielded ally for 25% of their health."),
                O("Purge", "The Aegis cleanses afflictions and grants immunity to new ones for 4 s."),
                O("Halo", "A ring of light surrounds you for 4 s, knocking back enemies that cross it."),
                O("Martyr's Share", "The most wounded ally receives a shield twice as strong."),
                O("Breaking Light", "A broken Aegis bursts, damaging and stunning nearby enemies."),
                O("Echo of Grace", "An unbroken shield converts its remaining value into healing when it expires."),
            } },

            { (ClassId.Rogue, AbilitySlot.Q), new[] {
                O("Shadow Gash", "You arrive striking, causing the enemy to bleed for 4 s."),
                O("Double Step", "You can Shadowstep again for free within 3 s."),
                O("Opener", "Your next hit within 2 s deals double damage and stuns."),
                O("Switch", "The enemy is thrown to your starting position and dazed."),
                O("Death Mark", "The enemy is Marked for 6 s, taking increased damage from the whole party."),
                O("Hamstring", "The enemy is chilled and slowed for 4 s."),
                O("Blade Dance", "The step also strikes up to two more enemies within 8 m of the target."),
                O("Unseen", "You cannot be targeted for 1.5 s after a step."),
            } },
            { (ClassId.Rogue, AbilitySlot.E), new[] {
                O("Choking Smoke", "Enemies inside the cloud are poisoned while it lasts."),
                O("Blinding Flash", "The bomb flashes, stunning enemies within 5 m for 1.5 s."),
                O("Caltrops", "The cloud is scattered with caltrops that damage and slow enemies crossing it."),
                O("Vanishing Act", "The smoke cleanses you and restores 15% of your health."),
                O("Flashpowder", "The bomb explodes in fire, burning and scorching everything within 4 m."),
                O("Ghost's Stride", "While hidden in the smoke, you are Rallied: increased movement speed and damage."),
                O("Smoke Chain", "Kills made from the smoke drop a new bomb at your feet (with a short cooldown)."),
                O("Marked for Death", "All enemies within 8 m are Marked when the bomb bursts."),
            } },

            { (ClassId.Ranger, AbilitySlot.Q), new[] {
                O("Marking Net", "Enemies caught in the net are Marked, taking increased damage from the whole party."),
                O("Barbed Net", "Enemies caught in the net bleed for 4 s."),
                O("Tether", "The net pulls all caught enemies into its centre."),
                O("Second Net", "You can fire a second Snare Shot for free within 3 s."),
                O("Shock Net", "When the net releases, it shocks and stuns enemies still inside."),
                O("Hunter's Larder", "Killing a snared enemy reduces Snare Shot's cooldown by 4 s and restores Focus."),
                O("Weighted Blast", "The net's weights explode shortly after landing."),
                O("Chain Net", "A second net springs onto the nearest enemy within 10 m that the first one missed."),
            } },
            { (ClassId.Ranger, AbilitySlot.E), new[] {
                O("Frost Arrows", "The volley chills every enemy it hits."),
                O("Fire Arrows", "The volley scorches every enemy it hits."),
                O("Creeping Barrage", "Each wave of the volley advances 1 m away from you."),
                O("Pinning Rain", "Each wave briefly pins non-elite enemies in place."),
                O("The Last Arrow", "The volley ends with a great arrow that deals triple damage and knocks enemies back."),
                O("Sheltering Rain", "Allies beneath the volley take 20% less damage while it lasts."),
                O("Seeker Volley", "The volley follows the nearest snared or Marked enemy."),
                O("Rain of Marks", "Enemies hit by three waves are Marked."),
            } },

            { (ClassId.Flagellant, AbilitySlot.Q), new[] {
                O("Dragnet", "Enemies within 3 m of the target are pulled in with it."),
                O("Lash", "The chain damages every enemy along its path."),
                O("Hooked", "The pulled enemy bleeds for 4 s, and you heal for the damage dealt."),
                O("Chain Sweep", "You finish the pull with a sweep, damaging and knocking down enemies within 2.5 m."),
                O("Shackled", "The pulled enemy is held twice as long and Marked."),
                O("Reel-In Crash", "When the chain pulls you to a heavy enemy, you land with a Crash."),
                O("Double Chain", "The chain also pulls a second enemy within 8 m."),
                O("Penance", "Each enemy pulled heals you for 6% and grants 20 Fervor."),
            } },
            { (ClassId.Flagellant, AbilitySlot.E), new[] {
                O("Shockwave", "The impact knocks enemies back and stuns them 50% longer."),
                O("Brand", "The impact leaves holy fire burning on the ground for 4 s."),
                O("Second Leap", "You can leap again for free within 3 s of landing."),
                O("Blood Rain", "The Fervor you spend heals allies within 5 m."),
                O("Crater Pull", "Enemies within 8 m are pulled to your landing point."),
                O("Martyr's Fall", "The leap costs 10% of your health and deals double damage."),
                O("Quake", "All enemies within 6 m of the impact are stunned for 2 s."),
                O("Afterquake", "Three ripples spread from the impact, each wider than the last."),
            } },

            { (ClassId.PlagueDoctor, AbilitySlot.Q), new[] {
                O("Creeping Cloud", "The cloud drifts toward the nearest enemy."),
                O("Clinging Fog", "Enemies inside are chilled and slowed."),
                O("Contagious", "Enemies that die inside release a small cloud of their own."),
                O("Fog of Mercy", "Allies inside heal for 3% of their health per second."),
                O("Choking", "Every 2 s, enemies inside choke, interrupting their attacks."),
                O("Volatile", "A vial bursting inside ignites the cloud for heavy damage, consuming it."),
                O("Twin Clouds", "Creates two clouds, one on each side of your aim point."),
                O("Plague Wind", "When the cloud expires, it blows outward, Blighting all enemies within 10 m."),
            } },
            { (ClassId.PlagueDoctor, AbilitySlot.E), new[] {
                O("Bloodlust", "Allies gain Bloodlust (15% more damage) for 6 s."),
                O("Vapour Shield", "Affected allies gain a shield worth 15% of their health."),
                O("Purge", "Each affliction cleansed from an ally damages enemies around them."),
                O("Deep Breath", "The ward lasts twice as long and heals 2% of health per second."),
                O("Exhale", "The mask also leaves a Miasma cloud at your feet."),
                O("Repel", "Knocks back all enemies within 5 m."),
                O("Plague Lord", "Blights all enemies within 8 m."),
                O("Last Breath", "Revives a nearby downed ally at 25% health."),
            } },
        };
    }
}
