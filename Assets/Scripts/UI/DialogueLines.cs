using Deepbound.Player;
using Deepbound.Progression;
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

        /// <summary>A line about what came home or what hangs in camp now (null if nothing for them).</summary>
        public static string Remembered(FolkId who)
        {
            uint b = CampMemory.Now;
            switch (who)
            {
                case FolkId.Hobb when CampMemory.Has(b, FolkId.Hobb, true): return "*He nods at the chimney.* My father's hammer. I strike the first blow of every morning with it now. You did that.";
                case FolkId.Silas when CampMemory.Has(b, FolkId.Silas, true): return "That strongbox behind me? Old Pell's. Empty now. Mostly. I keep it to remind me what greed weighs.";
                case FolkId.Marta when CampMemory.Has(b, FolkId.Marta, true): return "*She pats the cask on the bar.* Deepcellar Black. One cup a night, for whoever came back. Tonight that's you.";
                case FolkId.Vey when CampMemory.Has(b, FolkId.Vey, true): return "The deep silk is on my rack, darling. I've cut three cloaks from it already. None as good as the one I'm saving.";
                case FolkId.Corwin when CampMemory.Has(b, FolkId.Corwin, true): return "*He glances at the lamp by the gate.* It burns all night now. The horse looks at it. I think he remembers.";
                case FolkId.Aldric when CampMemory.Has(b, FolkId.Aldric, false): return "Saint Ossa rests on my dais again. The pilgrims have started coming. Real ones. I hardly know what to charge.";
                case FolkId.Brann when CampMemory.Has(b, FolkId.Brann, false): return "*He looks up at the standard.* That's my squad flying. Every name is stitched on it. There's room for more. Don't add yours.";
                case FolkId.Ysolde when CampMemory.Has(b, FolkId.Ysolde, false): return "The shards turn over my stones day and night. They sing in chords now. You gave the stones their voice back.";
            }
            if ((b & 1u) != 0 && who == FolkId.Brann) return "That shield over the gate was the Gravewarden's. I salute it every morning. Not the warden. The ones who took it down.";
            if ((b & 2u) != 0 && who == FolkId.Aldric) return "The Lich's crown sits by my shrine and drips. Always drips. I've stopped asking where the water comes from.";
            if ((b & 4u) != 0 && who == FolkId.Hobb) return "The Hollow King's crown is on a pike by the gong. Bad gold. I wouldn't melt it for a horseshoe.";
            if ((b & 8u) != 0 && who == FolkId.Marta) return "Aye, that's the Cardinal's hat over my bar. Drinks are half price for anyone who can reach it. Nobody can.";
            if ((b & 16u) != 0 && who == FolkId.Ysolde) return "The Unsung's skull hums by the well. A low note. The stones answer it a fifth above. I find it restful. Nobody else does.";
            return null;
        }

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
            var mem = Remembered(who);
            var all = new string[b.Length + open + (mem != null ? 1 : 0)];
            int k = 0;
            if (mem != null) all[k++] = mem;
            b.CopyTo(all, k);
            for (int i = 0; i < open; i++) all[k + b.Length + i] = f[i];
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
                case Contract.Cargo: return who switch
                {
                    FolkId.Hobb => "*Hobb looks at the empty hook on the wall.* My father's hammer went down with him. Heavy as sin - you'll crawl carrying it. Carry it out past whatever guards that floor and I'll hang it where it belongs.",
                    FolkId.Silas => "A colleague of mine died rich somewhere below. His strongbox didn't. Carry it out on the grave lift - and every knock spills coin, so don't get hit. What's left, we split. Mostly me.",
                    FolkId.Marta => "There's a cask of Deepcellar Black on a shelf down there, from before the flood. Three knocks and it's vinegar. Carry it like a baby, love. A heavy, sloshing baby.",
                    FolkId.Vey => "Deep silk. A bolt of it, in some drowned merchant's chest. If it tears, it's ruined - three snags and I'll weep. Bring it out on the lift untouched.",
                    FolkId.Corwin => "I dropped my coach lamp the night the road caved in. It's still down there. Still lit, they say. Bring it up on the lift. It'll light your way through the dark, at least.",
                    _ => "There's something of mine down there. Carry it back up.",
                };
                case Contract.Gather: return who switch
                {
                    FolkId.Aldric => "*Aldric lowers his voice and glances at the shrine.* Saint Ossa's bones were scattered below. Knucklebones - they glow gold and whisper. Find them, each in its own room. The shrine needs relics. Real ones, this time.",
                    FolkId.Brann => "My squad's standard. Torn to pieces when they fell - the pieces are scattered room to room. Find them. They don't stay in the ground. Not on my watch.",
                    FolkId.Ysolde => "The old stones shed shards when they're broken. Some are down there, humming in the dark. Bring them back - one per room, they don't like company.",
                    _ => "Things are scattered down there. Gather them.",
                };
                case Contract.Mark: return who switch
                {
                    FolkId.Silas => "Gilt-Tooth Morrow. Robbed me blind on the road, years back - now he robs the dead. Find him. Make him pay. With interest.",
                    FolkId.Hobb => "Something down there eats iron. The Ironeater. Every blade I've sent down comes back chewed. Put it down.",
                    FolkId.Aldric => "A necromancer who wore my order's robes. The Unshriven. He raised my parishioners. Unmake him.",
                    FolkId.Vey => "The Moth-Mother. She wears a gown of the dead's shrouds - my shrouds. I want that gown cut off her.",
                    FolkId.Corwin => "The Highwayman. Shot my lead horse - the live one, back then. He's down there with a bow. Find him before he finds you.",
                    FolkId.Brann => "Corporal Vane. Deserted my squad on the second floor and they died for it. Now he's dead and still running. Run him down.",
                    FolkId.Ysolde => "The Glyph-Eater. It tears runes out of the walls and swallows them. The stones scream at night. Silence it.",
                    FolkId.Marta => "The Cellar King. Something in a crown of barrel hoops, down where the cellars met the crypts. It took my brother's drinking mate. Take it.",
                    _ => "There's a named one below. Hunt it.",
                };
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

        // ---- Their craft (2026-11-08, the user: "be more creative with dialogue, the characters should feel grounded towards
        // the environment and their profession"). Each person has one question only they would answer, and the answer reads
        // YOU: your blade, your cloak, your runes, your drills, your pouch, the floor ahead. ----

        public static string CraftLabel(FolkId who) => who switch
        {
            FolkId.Silas => "What's worth my coin today?",
            FolkId.Hobb => "Look over my weapon.",
            FolkId.Aldric => "Hear my confession.",
            FolkId.Vey => "How do I look?",
            FolkId.Corwin => "What's waiting down the road?",
            FolkId.Brann => "How am I shaping up?",
            FolkId.Ysolde => "Read my runes.",
            FolkId.Marta => "Heard anything at the bar?",
            _ => null,
        };

        public static string Craft(FolkId who, NetworkPlayer me)
        {
            var prog = me != null ? me.GetComponent<PlayerProgress>() : null;
            if (prog == null) return "...";
            var d = DungeonDirector.Instance;
            int next = d != null ? d.NextDepth.Value : 0;
            int k = UnityEngine.Random.Range(0, 2);
            switch (who)
            {
                case FolkId.Hobb:
                {
                    int lv = prog.WeaponLevel.Value, hones = prog.Hones.Value;
                    if (lv == 0) return "*Hobb runs a thumb down it and winces.* Never been to a forge, this. It's a tool, not a weapon. Bring me embers and I'll start it remembering what it's for.";
                    if (lv < 3) return "*He sights down the edge toward the forge light.* +" + lv + ". There's a temper in it now, but it's shallow. Another few heats and it'll stop flinching on bone.";
                    if (lv < 6) return "*Hobb taps the flat against the anvil and listens.* Hear that? That's +" + lv + ". Rings true. Still a ringing that wants more fire, mind.";
                    if (lv < 10) return "+" + lv + ". *He hands it back slowly.* That's my work now, not the dead's. Whoever drops that in a crypt owes me an apology.";
                    if (hones < 5) return "Masterwork. Nothing left to temper. I can hone it - " + hones + " of five so far. After that it's as sharp as iron gets in this world.";
                    return "*Hobb won't take it.* No. There's nothing I can do to that blade but spoil it. Go and use it.";
                }
                case FolkId.Vey:
                {
                    var np = me;
                    int dye = np.CloakDye.Value, aura = np.Aura.Value, pet = np.PetKind.Value;
                    string dyeName = dye > 0 && dye < Cosmetics.Dyes.Length ? Cosmetics.Dyes[dye].Name : null;
                    if (dye == 0 && aura == 0) return "*Vey circles you twice and sighs at the mirror.* Undyed wool. Like a monk who lost a wager. Stand in front of the glass and tell me you don't want better.";
                    string s = dyeName != null ? "The " + dyeName + " suits you. It catches the lantern light, see? There, in the mirror." : "The cloth's plain, but you wear it like you meant it.";
                    if (aura > 0) s += " And that glow - a little loud, darling, but the dead should see you coming.";
                    if (pet > 0) s += " Keep that creature off my bolts.";
                    return s;
                }
                case FolkId.Ysolde:
                {
                    var runes = new System.Collections.Generic.List<string>();
                    foreach (var r in new[] { prog.Socket0.Value, prog.Socket1.Value, prog.Socket2.Value })
                        if (Loot.Runes.Kind((Loot.Rune)r) != Loot.Rune.None) runes.Add(Loot.Runes.Name((Loot.Rune)r));
                    if (runes.Count == 0) return "*She lays her palm on your blade and the standing stones go quiet.* Empty. No words in it at all. The steel is listening for something - bring me a rune and let it hear.";
                    if (runes.Count == 1) return "*The nearest stone flickers.* " + runes[0] + ". One word, said over and over. It's a good word. It's lonely.";
                    return "*The stones hum in turn, one for each.* " + string.Join(", ", runes) + ". They argue, but quietly. That's how you want runes - like old married folk.";
                }
                case FolkId.Brann:
                {
                    var reg = Shop.RegimenOf(prog.ClassId.Value);
                    int total = 0;
                    for (int s = 0; s < 8; s++) total += prog.DrillCount((Core.StatType)s);
                    string drill = reg == Shop.Regimen.Arcane ? "Casting Forms and Meditation" : reg == Shop.Regimen.Skirmish ? "Target Practice and Hill Sprints" : "the Sword Drill and the shield";
                    if (total == 0) return "*Brann pokes your ribs with the practice sword.* Soft. Untested. You're on the " + Shop.RegimenName(prog.ClassId.Value) + " regimen - start with " + drill + ". Dummies don't hit back. Yet.";
                    if (total < 5) return total + " drills in. You've stopped dropping your guard on the turn. Mostly. Keep at " + drill + ".";
                    if (total < 10) return "*He nods at the scarred dummy.* That's your work on it. " + total + " drills. You'd have lasted a week in my old company. That's high praise.";
                    return total + " drills. I've nothing left to shout at you about. Don't tell anyone I said that.";
                }
                case FolkId.Silas:
                {
                    var f = prog.GetComponent<Flasks>();
                    int hp = f != null ? f.Count(Flasks.Kind.Health) : 0;
                    int gold = prog.Gold.Value;
                    if (gold < 30) return "*Silas looks at your purse, then at you.* Today? Advice. It's free and worth the price. Come back heavier.";
                    if (hp <= 1) return "You've " + (hp == 0 ? "no" : "one") + " red bottle in that pouch. Below, that's a coffin with extra steps. Health draughts first. Then talk to me about luxuries.";
                    if (gold > 600) return "*His eyes light up.* " + gold + " gold and you're asking ME? The shrine's relics, then Hobb's fire, then my pendants. Spend it - the dead don't tip.";
                    return k == 0 ? "A Frost flask. Nobody buys them till they're cornered by something with too many teeth. Then they'd pay anything." : "Smelling salts from Corwin and a red draught from me. Cheapest life you'll ever buy.";
                }
                case FolkId.Corwin:
                {
                    if (d == null) return "Road's the road.";
                    int act = DungeonDirector.Act(next);
                    string place = DungeonTheme.FloorName(next);
                    string guard = DungeonDirector.IsWardenFloor(next) ? WardenOf(act) : BossOf(act);
                    string ev = next >= 8 ? " The horse won't look at that road. Neither will I." : "";
                    return "*Corwin squints down the dark pine road.* Next stop: " + place + ". Floor " + (next + 1) + ". I hear " + guard + " keeps the bottom of it." + ev + " I'll have the seats warm when you come up.";
                }
                case FolkId.Aldric:
                {
                    int grudges = 0;
                    foreach (FolkId f in System.Enum.GetValues(typeof(FolkId))) if ((int)f < 10) grudges += prog.Grudge(f);
                    int drunk = prog.GetComponent<Player.PlayerDebuffs>() is var db && db != null ? db.Drunk : 0;
                    if (grudges > 0) return "*Aldric's hands stay folded.* You struck folk who feed you, child. The light forgives. They might not. Make amends - and leave something in the bowl on your way out.";
                    if (drunk >= 2) return "*He sniffs.* You smell of Marta's cellar. Confession is wasted on the merry. Come back when you can kneel without help.";
                    return k == 0 ? "*He listens, eyes closed, beside the brazier.* Is that all? Then you are absolved. The donation bowl is behind you. The light sees it too."
                                  : "*Aldric murmurs over your head.* Whatever you did below, the dead did worse first. Go in peace. Pay at the shrine.";
                }
                case FolkId.Marta:
                {
                    var o = d != null ? d.RunOmen : Omen.None;
                    if (o != Omen.None) return "*Marta leans over the bar.* Folk say there's an omen on you lot. " + DungeonDirector.OmenName(o) + ". Old Pell spilled his mead when he heard. That's a bad sign. Pell never spills.";
                    return Rumours[UnityEngine.Random.Range(0, Rumours.Length)];
                }
            }
            return "...";
        }

        private static readonly string[] Rumours =
        {
            "*Marta wipes a mug that's already clean.* A carter swore he saw the Tallyman on the road at dusk. Ledger and all. He drank three and paid for one.",
            "The band's fiddler says the fourth floor hums back when you play in it. He won't play in it again.",
            "Somebody left a cat down there, they say. Asleep in a shaft. Pet it and it'll pet you back, so to speak.",
            "Brann had a squad once. He doesn't drink here on the night they went down. Leaves a mug full on the end table.",
            "Vey's been asking every delver for deep silk. Says the dye from it makes black look grey. She'd pay well, I'd wager.",
            "Hobb's father's hammer went down with him. He'll tell you he doesn't care. He keeps the hook on the wall empty.",
            "There's a lever room on most floors, I'm told. Three levers, one breath. The ones who make it buy the next round.",
            "Corwin talks to that horse more than he talks to me. And I'm the one who feeds him.",
        };

        private static string WardenOf(int act) => act switch { 0 => "Marrowgut", 1 => "the Drowned Knight", 2 => "Sir Aldous", 3 => "the Choirmaster", _ => "Gorr" };
        private static string BossOf(int act) => act switch { 0 => "the Gravewarden", 1 => "the Tide Lich", 2 => "the Hollow King", 3 => "the Pale Cardinal", _ => "the Unsung" };

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
