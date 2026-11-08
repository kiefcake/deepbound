using Deepbound.World;

namespace Deepbound.UI
{
    /// <summary>What the camp folk say in conversation (<see cref="Dialogue"/>): their trade, their work, themselves.</summary>
    public static class DialogueLines
    {
        public static string TradeLabel(FolkId who) => who switch
        {
            FolkId.Silas => "Show me your wares.",
            FolkId.Hobb => "Show me what you've made.",
            FolkId.Aldric => "What does the shrine offer?",
            FolkId.Vey => "Let me see the wardrobe.",
            FolkId.Corwin => "About the road down...",
            FolkId.Brann => "I'll take a drill.",
            FolkId.Ysolde => "Work the runes with me.",
            FolkId.Marta => "A horn of mead (" + TavernKeep.MeadPrice + " gold).",
            _ => "Trade.",
        };

        /// <summary>Who they are, a line at a time; more opens up with favour (1, 3, 5).</summary>
        public static string[] About(FolkId who, int favour)
        {
            string[] b, f;
            switch (who)
            {
                case FolkId.Silas:
                    b = new[] { "I buy low and sell to people who are about to die. It's a seller's market.", "Crane's an old name. Older than the castle. We've always been the ones selling the rope.", "The hood? Keeps the rain off. And the questions." };
                    f = new[] { "You pay your debts. That makes you rarer than a whole relic, friend.", "I had a partner once. Went down to fetch a 'sure thing'. I still keep his stool behind the stall.", "Between us: half my stock came up from below in the pockets of people who didn't. I sell it back to the ones who might." };
                    break;
                case FolkId.Hobb:
                    b = new[] { "Thirty years at that anvil. It's never once said thank you. Neither has a customer.", "My father was a smith. His father too. None of us ever made a nail.", "Iron remembers. Hit it wrong and it holds it against you forever. Like people." };
                    f = new[] { "You bring back what you take. That's more than most blades do.", "I forged the portcullis down on the first floor. Don't tell the dead. They're still sore about it.", "There's an edge I've been saving. For someone who'll come back. Keep coming back, and we'll see." };
                    break;
                case FolkId.Aldric:
                    b = new[] { "I took my vows young. Then I took donations. One of those has served me better.", "The light is real, child. It's just expensive to keep lit.", "I pray for every delver who goes down. Out loud, so they can hear what they're paying for." };
                    f = new[] { "You give without being asked. The light notices. So do I. Mostly I do.", "I was a gravedigger before the cloth. I know exactly how deep the dead lie, and they do not stay there.", "Here is a thing I do not say for coin: I am afraid of what's under the Cathedral. Come back up. Please." };
                    break;
                case FolkId.Vey:
                    b = new[] { "I dressed a duchess once. She was buried in it. Still the best-dressed thing down there.", "Cloth is armour for the soul, darling. Plate is armour for the idiot.", "Every stitch is a promise. I keep mine. Delvers don't, mostly. They die." };
                    f = new[] { "You have taste. Unusual, for someone who smells of crypt.", "I sewed Corwin's coat. He's worn it every day for eleven years. That's love, or laziness.", "The colours I keep for you are the ones I'd keep for myself. Don't make me bury them." };
                    break;
                case FolkId.Corwin:
                    b = new[] { "I drive. They ride. Some ride back. I don't ask which before we leave.", "The horse was alive when I started this job. Neither of us has said anything about it since.", "I know every rut in that road. I named most of them after passengers." };
                    f = new[] { "You always come back. I like a fare who comes back.", "I had a family, before. The road took them, or I took the road. It's the same thing at night.", "Some nights I hear wheels behind me on the way back. I never look. You shouldn't either." };
                    break;
                case FolkId.Brann:
                    b = new[] { "Twenty years in the King's Guard. The King's dead. The guard isn't. Draw your own lesson.", "I train delvers. Most of them die anyway. Fewer of them die stupid.", "The yard is the only honest place in this castle. Wood doesn't lie about a bad stance." };
                    f = new[] { "You listen. Most recruits only hear.", "I lost a squad on the second floor, before your time. Every drill I give you is one they didn't get.", "If I ever go down there again, I'd want you at my back. That's not a compliment. That's an order." };
                    break;
                case FolkId.Ysolde:
                    b = new[] { "The stones were here first. The castle is the stones' idea of a joke.", "Runes are words the world forgot it said. I remind it.", "I don't sleep much. The stones talk. Mostly about you lot." };
                    f = new[] { "The stones like you. They've started humming when you come up the road.", "I was born under the oldest stone. My mother said it sang the night I came. It hasn't stopped.", "There is a rune under the Cathedral I've never seen, only heard. If you find it, don't read it. Bring it to me." };
                    break;
                case FolkId.Marta:
                    b = new[] { "I keep the Lantern lit and the mugs full. One of those is harder than it looks.", "Every delver who walks through that door gets one drink on the house. The first. The last one, they pay for.", "The band came for one night. That was six years ago." };
                    f = new[] { "You tip. You'd be amazed how rare that is in a place like this.", "My brother went down. He's the reason there's a lantern over the door. I light it every night.", "I've a cask in the cellar I'm saving for the day someone comes back from the bottom. Don't make me drink it alone." };
                    break;
                default:
                    return new[] { "..." };
            }
            int open = favour >= 5 ? 3 : favour >= 3 ? 2 : favour >= 1 ? 1 : 0;
            var all = new string[b.Length + open];
            b.CopyTo(all, 0);
            for (int i = 0; i < open; i++) all[b.Length + i] = f[i];
            return all;
        }

        /// <summary>How they ask for the job.</summary>
        public static string Pitch(FolkId who, Contract c, bool special)
        {
            if (special) return who switch
            {
                FolkId.Silas => "You've been good for business. Here's a real job, and a real price: do it and the gilded skull on my shelf is yours.",
                FolkId.Hobb => "I've an edge I've been saving. Earn it. Do this and I'll set my best fang on your steel.",
                FolkId.Aldric => "The light asks something harder of you. Answer it and take the Psalter. I've carried it long enough.",
                FolkId.Vey => "This one matters, darling. Do it and I'll give you a cloak of shadow, cut to you alone.",
                FolkId.Corwin => "There's a fare I owe. Settle it below and the purse I keep for the road is yours.",
                FolkId.Brann => "A real order, soldier. Carry it and you carry the horn of my old company.",
                FolkId.Ysolde => "The stones want a test. Pass it and the echo stone is yours. It has waited a long time.",
                FolkId.Marta => "I've a favour to ask. A big one. Do it, and you'll have something from the cellar money can't buy.",
                _ => "There's work.",
            };
            switch (c)
            {
                case Contract.Salvage: return who == FolkId.Vey ? "The old houses below kept their finery in chests. Open some for me; I want the lining, you want the rest." : "Chests, friend. The dead hoard and I sell. Open a few and we both do well.";
                case Contract.GoldRun: return who == FolkId.Marta ? "The cellar's empty and the brewer wants paying. Bring back coin from below. Plenty of it." : "Gold has a way of finding the brave. Find it back. A lot of it.";
                case Contract.TreasureHunt: return who == FolkId.Ysolde ? "Behind some walls down there the stones have been sealed up. Break one open. They want to breathe." : "There's always a hollow wall. Behind it, always something worth the trouble. Find one.";
                case Contract.Ironmonger: return "The big ones wear good iron. Break a few of them and I'll melt it into something that's yours.";
                case Contract.ChampionHunt: return who == FolkId.Ysolde ? "One of the dead below wears a name. Names are runes. Take it from them." : "There's a champion down there with a name and a reputation. Take both.";
                case Contract.EliteHunter: return who == FolkId.Brann ? "The marked ones. Elites. Champions. The ones the others follow. Cut down the officers, the rank breaks." : "The marked ones fight dirty and carry better steel. Bring them down.";
                case Contract.Exorcist: return who == FolkId.Ysolde ? "The casters down there twist runes they don't understand. Silence them before they break something." : "The ones who call the dead back up. Necromancers, wraiths, hexers. Put them to rest. Properly.";
                case Contract.Untouchable: return who == FolkId.Brann ? "One floor. Nobody goes down. Not once. That's a drill, and it's the only one that matters." : "Walk through the next floor and let none of you fall. The light likes a full count.";
                case Contract.Slayer: return who == FolkId.Corwin ? "The road's crowded with them. Thin the herd, and the horse sleeps easier." : who == FolkId.Aldric ? "Many restless souls below. Put a good number of them back to sleep, for the shrine." : "Numbers, recruit. Put enough of them down and the rest think twice.";
                case Contract.SwiftBlade: return "Fast in, fast out. Get to the guardian before the far bell. I'll wait for no one who dawdles.";
                case Contract.RoomSweep: return who == FolkId.Marta ? "The rats come up from below when the rooms down there get crowded. Clear a few, would you?" : "Clear the rooms on the way. A clean road is a quick road.";
                case Contract.UrnBreaker: return who == FolkId.Vey ? "Grave-dust makes the deepest black dye there is. Break urns. Bring me the colour." : "The old urns down there are full of... well. Break them. It's good luck, they say. For me.";
                default: return "There's work, if you want it.";
            }
        }

        public static string Accept(FolkId who) => who switch
        {
            FolkId.Silas => "Pleasure doing business.",
            FolkId.Hobb => "Good. Don't bend anything you can't straighten.",
            FolkId.Aldric => "Go with the light. It's already paid for.",
            FolkId.Vey => "Marvellous. Try not to bleed on anything I made.",
            FolkId.Corwin => "I'll have the seat warm.",
            FolkId.Brann => "Move out!",
            FolkId.Ysolde => "The stones will be listening.",
            FolkId.Marta => "That's a dear. First round's on me when you're back.",
            _ => "Good.",
        };

        public static string Grudged(FolkId who) => who == FolkId.Marta ? "Not after that, love. Pay your bill, then we'll talk." : "After what you did? Make amends first. Then we'll talk work.";
    }
}
