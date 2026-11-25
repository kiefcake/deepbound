using System;
using System.Collections.Generic;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Deepbound.UI
{
    public class CampChatter : MonoBehaviour
    {
        private sealed class Speaker
        {
            public FolkId Who;
            public string Name;
            public Vector3 Head;
            public string[] Idle;
            public Func<string> News;
            public string Current;
            public float Until, NextAt, StartledUntil;
            public int IdleIndex;
            public readonly HashSet<string> Said = new HashSet<string>();
        }

        private const float TalkRange = 9f, ReadRange = 16f, NameRange = 11f, BubbleWidth = 280f;

        private readonly Dictionary<FolkId, Speaker> speakers = new Dictionary<FolkId, Speaker>();
        private readonly List<(float at, FolkId who, string text)> pending = new List<(float, FolkId, string)>();
        private readonly HashSet<FolkId> greeted = new HashSet<FolkId>();
        private readonly GUIContent measure = new GUIContent();
        private bool wasInCamp;
        private static CampChatter instance;

        private static GUIStyle bubbleStyle, bubbleNameStyle, nameStyle, tradeStyle;

        private static readonly string[][] Struck =
        {
            new[] { "{0}! Mind where you point that. ...Prices just went up, by the way.", "Again? That's another quarter on everything, friend. Pain is a cost of business.",
                    "Hit me once more and I'll sell you river water and call it a potion.", "I've doubled my prices for you, {0}. I'd triple them, but I'm a reasonable man." },
            new[] { "Oi! I've been hit by better. That dent's going on your bill.", "That's it. Embers cost more for people who swing at the smith.",
                    "I've broken bigger than you over this anvil, {0}. Prices up. Again.", "Keep swinging. Every bruise is on your bill now. Double." },
            new[] { "Peace, child. ...Though the shrine will expect a larger gift from you now.", "Violence on holy ground! That's a tithe, {0}. A big one.",
                    "I forgive you. The offering box does not.", "Strike a priest again and I'll pray AGAINST you, {0}. Free of charge." },
            new[] { "Darling. The velvet. Not the velvet.", "If you tear this coat I will dye you a colour you cannot wash off.",
                    "Out of my light, {0}. Come back with manners. And money.", "*Mistress Vey turns her back on you and measures the air, pointedly.*" },
            new[] { "Easy! Easy! I just drive the thing!", "Hit the driver again and see who gets you down that shaft.",
                    "One more, {0}, and you're walking. All the way down. It's a long way.", "*Corwin pulls his hat over his eyes and refuses to look at you.*" },
            new[] { "*The dealer's jaw clacks shut. It taps the felt, slowly.*", "*It turns one card over: the Ace of Spades. It points it at you.*",
                    "*The dealer folds its hands. It will not deal to {0} until tempers cool.*", "*It sweeps the cards away and simply stares at you.*" },
            new[] { "Hey! I walked all this way with glass on my back! That'll cost you.", "Another one? The prices down here just got deeper, {0}.",
                    "I've survived worse than you on the way down. Prices up.", "You're paying double now. Hit me again and I'll charge for breathing." },
            new[] { "Good! Again! ...Not on ME, recruit. Drills cost more for you now.", "Discipline, {0}! That's a fine on top of the fee.",
                    "I've had sergeants bigger than you cry, {0}. Double fees.", "Twenty laps. And double. Out of my yard." },
            new[] { "The stones felt that, {0}. So will your purse.", "You strike a runewright? Bold. Expensive, but bold.",
                    "I could write your name in a rune that itches forever, {0}. Prices up instead.", "*Ysolde traces something in the air at you. Your teeth ache.*" },
            new[] { "Oi! Not the barmaid, {0}. That's a round on you.", "Two rounds now. And mind the mugs.",
                    "Swing at me again and you'll drink from the horse trough, {0}.", "*Marta sets your mug down, very slowly, out of your reach.*" },
        };

        private static readonly string[] Forgive =
        {
            "Coin heals all wounds, {0}. Most of them. Prices are back where they were.",
            "Hmph. Paid for the dent. We're square, {0}. Don't do it again.",
            "Your coin has washed you clean, {0}. Mostly my coin now. Go in peace.",
            "Paid in full, {0}. The velvet forgives you. I am still deciding.",
            "Square, {0}. Seats are in the back. Hands to yourself.",
            "*The dealer slides your chair back out with one bony finger.*",
            "Fine, fine. Paid in full. The glass forgives you, {0}.",
            "Fine paid. Fall back in, {0}. And keep your elbows in the drill.",
            "The stones are calm again, {0}. So am I. Mostly.",
            "Tab's clear, {0}. Sit down and behave.",
        };

        private static readonly string[] Witness =
        {
            "Don't look at me, {0}. I only sell the potions you'll need afterwards.",
            "Ha! ...No. Don't do that again, {0}.",
            "Peace! Peace, {0}! Save it for the dead.",
            "Barbaric. Simply barbaric.",
            "I saw nothing. I never see anything. That's why I still drive.",
            "*The dealer's jaw clacks: something like a laugh.*",
            "Glass, {0}! There's glass on my back!",
            "Sloppy footwork, {0}. Again, and on the dead next time.",
            "The stones remember that, {0}. So do I.",
            "Not in my tavern, {0}. Take it outside. Or down the shaft.",
        };

        private static readonly string[] Startled =
        {
            "Not near the glassware!", "Ha! Felt that one in my teeth.", "Not on holy ground, please.",
            "Smoke! On the silk!", "Whoa! Whoa! Easy, horses!", "*The dealer brushes ash off the cards.*",
            "My bottles!", "Down! Everyone down! ...Good reflexes. Drill passed.", "*The standing stones flare blue all at once.*",
            "The mugs! Mind the mugs!",
        };

        private static readonly string[] WaveBack =
        {
            "*Silas raises a gloved hand, then thinks better of it and shakes yours instead. Firmly. Business-like.*",
            "Ha! Up top! ...Mind, I've a hand like an anvil.",
            "*Brother Aldric presses a coin-sized blessing into your palm, then takes the coin out of it.*",
            "Darling, no. We do not slap hands. We nod. Like so.",
            "*Corwin tips his hat. He keeps both hands on the reins.*",
            "*The dealer raises one bony palm and meets yours, very gently. Click.*",
            "*The peddler waves back with a bottle in each hand.*",
            "Ha! Good grip. Now give me twenty.",
            "*Ysolde touches your palm with two cold fingers. A rune glows there, then fades.*",
            "*Marta slaps your palm with a wet cloth. It still counts.*",
        };

        private static readonly string[] WaveCold =
        {
            "Hands where I can see them, friend. After last time.", "Oh, now you want to be friends? Pay for the dent first.",
            "Make amends first, child. Then we may be friendly.", "Absolutely not. Not after the velvet.",
            "*Corwin flinches, then pretends he didn't.*", "*The dealer keeps its hands folded.*",
            "Pay first. Wave after.", "Pay your fine first, recruit.", "Not until the stones forgive you.",
            "Settle your tab first, love.",
        };

        private static readonly string[] CatBystander =
        {
            "The cat's worth more than your gold, {0}.", "Kick the dog next, why don't you. We haven't got a dog.",
            "Leave the poor creature be.", "Monster. Absolute monster.", "Even the horses saw that.", "*The dealer slowly shakes its skull.*",
            "That cat's followed me down and back twice, {0}. Leave it be.", "Twenty laps for that, {0}.",
            "The cat is under my protection, {0}. You will not like my protection.", "No cat, no mice, no tavern. Think on that, {0}.",
        };

        private static readonly string[] CatScolds =
        {
            "Not the cat! The cat is not anyone's, {0}, but it is ours.",
            "You swung at the cat. I have nine years of feeding that animal, {0}. Nine.",
            "...the cat, {0}? Truly?",
        };

        private static string Line(string[] table, FolkId who) =>
            (int)who < table.Length && !string.IsNullOrEmpty(table[(int)who]) ? table[(int)who] : null;

        private static PlayerProgress Me => NetworkPlayer.Local != null ? NetworkPlayer.Local.GetComponent<PlayerProgress>() : null;
        private static DungeonDirector Dir => DungeonDirector.Instance;
        private static int FloorsDone => Dir != null ? Dir.NextDepth.Value : 0;
        private static int Seals => Dir != null ? Dir.SealCount : 0;
        private static int ExpeditionHome => Expedition.Count(CampMemory.Now);

        private void OnEnable() => instance = this;
        private void OnDisable() { if (instance == this) instance = null; }

        public static void SayLater(FolkId who, string text, float delay)
        {
            if (instance != null && !string.IsNullOrEmpty(text)) instance.pending.Add((Time.time + delay, who, text));
        }

        public static void Say(FolkId who, string text, float seconds)
        {
            GameHud.Heard?.Invoke("folk", who + ": " + text);
            if (instance == null || !instance.speakers.TryGetValue(who, out var s)) return;
            s.Current = text;
            s.Until = Time.time + seconds;
            s.NextAt = s.Until + Random.Range(4f, 8f);
        }

        public static void SlotResult(string who, int win)
        {
            if (win >= 200) Say(FolkId.Bones, "*The dealer stops shuffling and turns its skull toward the reels. Slowly, it claps.*", 5f);
            else if (win >= 50) SayLater(FolkId.Silas, "A winner! Spend it here, " + who + ". Potions first, regrets later.", 0.5f);
        }

        public static void GongStruck(string who, float damage, bool record)
        {
            if (record && damage >= 60f) Say(FolkId.Hobb, "Hear that? " + Mathf.RoundToInt(damage) + "! " + who + ", you'd make a fair smith. A poor one, but fair.", 6f);
        }

        public static void CardsSettled(string who, int net)
        {
            if (net >= 150) Say(FolkId.Bones, "*The dealer taps the shoe twice: a rare mark of respect for " + who + ".*", 5f);
            else if (net <= -150) Say(FolkId.Bones, "*Its jaw clacks. It pushes " + who + "'s empty chair in, gently.*", 5f);
        }

        public static void Offended(FolkId who, int level, string attacker, Vector3 body)
        {
            if ((int)who < Struck.Length)
            {
                var lines = Struck[(int)who];
                string line = string.Format(lines[Mathf.Clamp(level, 1, lines.Length) - 1], attacker);
                if (who == FolkId.Peddler || instance == null)
                {
                    GameHud.ShowCombo(body + Vector3.up * 2.2f, "!", new Color(1f, 0.55f, 0.3f));
                    GameHud.Feed("The peddler: \"" + line + "\"", new Color(1f, 0.75f, 0.45f));
                }
                else Say(who, line, 6f);
            }

            var fig = Art.FolkIdle.Near(body);
            var from = NetworkPlayer.Local != null ? NetworkPlayer.Local.transform.position : body - Vector3.forward;
            foreach (var p in PlayerLife.All) if (p != null && p.name == attacker) from = p.transform.position;
            if (fig != null) fig.Flinch(from, 1f);

            if (instance != null && Random.value < 0.7f && instance.Nearest(body, 14f, who, out var witness))
            {
                string remark = Line(Witness, witness);
                if (remark != null) SayLater(witness, string.Format(remark, attacker), 1.8f);
            }
        }

        public static void Forgiven(FolkId who, string payer)
        {
            string line = Line(Forgive, who);
            if (line == null) return;
            line = string.Format(line, payer);
            if (who == FolkId.Peddler || instance == null) GameHud.Feed("The peddler: \"" + line + "\"", new Color(1f, 0.85f, 0.55f));
            else Say(who, line, 6f);
        }

        public static void Waved(FolkId folk, string who, PlayerProgress progress, Vector3 from)
        {
            if (instance == null || !instance.speakers.TryGetValue(folk, out var s)) return;
            bool sore = progress != null && progress.Grudge(folk) > 0;
            string line = Line(sore ? WaveCold : WaveBack, folk);
            if (line != null) Say(folk, line, 5f);
            if (!sore && (folk == FolkId.Hobb || folk == FolkId.Bones))
            {
                var at = (s.Head + from) * 0.5f + Vector3.down * 0.4f;
                Art.Sfx.Play(Art.Sound.Block, at, 0.8f, folk == FolkId.Bones ? 2.2f : 1.6f);
                Art.Vfx.Embers(at, 8, new Color(1f, 0.85f, 0.45f), 3f, 0.04f, 0.5f);
                GameHud.ShowCombo(at + Vector3.up * 0.3f, "HIGH FIVE!", new Color(1f, 0.85f, 0.45f));
            }
            var fig = Art.FolkIdle.Near(s.Head);
            if (fig != null && sore) fig.Flinch(from, 0.4f);
        }

        public static void CatScared(string who, Vector3 at)
        {
            if (instance == null) return;
            SayLater(FolkId.Aldric, string.Format(CatScolds[Random.Range(0, CatScolds.Length)], who), 0.4f);
            if (instance.Nearest(at, 12f, FolkId.Aldric, out var bystander))
            {
                string remark = Line(CatBystander, bystander);
                if (remark != null) SayLater(bystander, string.Format(remark, who), 2f);
            }
        }

        public static void Blast(Vector3 at)
        {
            if (instance == null) return;
            foreach (var s in instance.speakers.Values)
            {
                var foot = new Vector3(s.Head.x, at.y, s.Head.z);
                if ((foot - at).sqrMagnitude > 81f || Time.time < s.StartledUntil) continue;
                s.StartledUntil = Time.time + 10f;
                string line = Line(Startled, s.Who);
                if (line != null) Say(s.Who, line, 4f);
                var fig = Art.FolkIdle.Near(foot);
                if (fig != null) fig.Flinch(at, 0.7f);
            }
        }

        public static string NewsFor(FolkId who)
        {
            if (instance == null || !instance.speakers.TryGetValue(who, out var s)) return null;
            string news = s.News?.Invoke();
            if (!string.IsNullOrEmpty(news)) return news;
            if (s.Idle.Length == 0) return null;
            s.IdleIndex = (s.IdleIndex + 1) % s.Idle.Length;
            return s.Idle[s.IdleIndex];
        }

        public static string GreetingFor(FolkId who) => NetworkPlayer.Local != null ? Greeting(who, NetworkPlayer.Local) : null;

        private bool Nearest(Vector3 at, float range, FolkId except, out FolkId found)
        {
            found = except;
            float best = range * range;
            foreach (var s in speakers.Values)
            {
                if (s.Who == except) continue;
                var d = s.Head - at;
                d.y = 0f;
                if (d.sqrMagnitude >= best) continue;
                best = d.sqrMagnitude;
                found = s.Who;
            }
            return found != except;
        }

        private static string Greeting(FolkId who, NetworkPlayer me)
        {
            var prog = me.GetComponent<PlayerProgress>();
            string name = me.name;
            string cls = prog != null ? Classes.ClassDefinition.For(prog.ClassId.Value).Name.ToLowerInvariant() : "delver";
            int k = Random.Range(0, 3);
            var debuffs = me.GetComponent<PlayerDebuffs>();
            if (debuffs != null && debuffs.Drunk >= 2 && Random.value < 0.7f)
                switch (who)
                {
                    case FolkId.Silas: return "*Silas steadies you by the elbow.* All prices are final when you're merry, " + name + ". House rule. My house.";
                    case FolkId.Hobb: return "Not near the forge like that. Last merry delver I let near the coals lost his eyebrows. Both.";
                    case FolkId.Aldric: return "*Aldric makes a small sign.* The light forgives drink. The cobbles, less so. Sit, child.";
                    case FolkId.Vey: return "Darling, you've mead on the hem. On MY hem. Stand still.";
                    case FolkId.Corwin: return "If you're sick in my carriage you're walking down. I mean that.";
                    case FolkId.Brann: return "*Brann sighs.* Drunk on the eve of a descent. My old company would've been proud. Then flogged you.";
                    case FolkId.Ysolde: return "The stones can't read you like this, " + name + ". Everything's swimming.";
                    case FolkId.Marta: return "You've had your share, love. Water's free. Have some.";
                }
            if (FolkRounds.Walks(who) && (FolkPlaces.Now(who) - FolkPlaces.Home(who)).sqrMagnitude > 16f && Random.value < 0.75f)
                switch (who)
                {
                    case FolkId.Silas: return "*Silas shifts a sack on his shoulder.* Fetching stock. Walk with me, " + name + " - the stall's watched, I'm not.";
                    case FolkId.Hobb: return "*Hobb wipes soot onto his apron.* Out for charcoal. Make it quick, the fire won't mind itself.";
                    case FolkId.Aldric: return "*Aldric pauses mid-stride, censer swinging.* Blessing the walls, " + name + ". The walls pay nothing either.";
                    case FolkId.Vey: return "*Vey has pins in her mouth.* Mm. Fetching thread. Talk while I walk, darling.";
                    case FolkId.Corwin: return "Stretching my legs. The horse doesn't need it. Hasn't for years.";
                    case FolkId.Brann: return "*Brann is walking the wall.* Inspecting the watch. They're asleep. As usual. What is it?";
                    case FolkId.Ysolde: return "I'm listening to the far stones. They whisper differently out here. What is it, " + name + "?";
                    case FolkId.Marta: return "*Marta balances a crate of mugs.* Quick, love, these aren't light.";
                }
            switch (who)
            {
                case FolkId.Silas: return k == 0 ? name + "! Still breathing. Good for business." : k == 1 ? "Ah, the " + cls + ". Browse. Touch nothing you cannot pay for." : "Welcome back, " + name + ". Prices are fair. Mostly.";
                case FolkId.Hobb: return k == 0 ? name + ". Let me see that edge." : k == 1 ? "A " + cls + " with a blunt weapon is a dead " + cls + ". Hand it over." : "*Hobb sets the hammer down.* What needs mending, " + name + "?";
                case FolkId.Aldric: return k == 0 ? "Bless you, child. Blessings are, of course, by donation." : k == 1 ? "The light walks with you, " + name + ". For a modest sum it walks faster." : "*Aldric looks up from his prayers.* Ah. A paying pilgrim.";
                case FolkId.Vey: return k == 0 ? name + ", darling. That cloak is a crime." : k == 1 ? "A " + cls + "! Stand still, let me look at you." : "*Vey bites off a thread.* Something new for the dark, " + name + "?";
                case FolkId.Corwin: return k == 0 ? "Seats are in the back, " + name + ". Mind the horse." : k == 1 ? "When you are ready, " + cls + ". The road waits. I do not." : "*Corwin tips his hat.* Down again, " + name + "?";
                case FolkId.Brann: return k == 0 ? name + "! Feet apart, chin down. Again." : k == 1 ? "On your guard, " + cls + ". The yard is open." : "*Brann lowers the practice sword.* You look soft, " + name + ". We can fix that.";
                case FolkId.Marta: return k == 0 ? name + "! Sit down before you fall down." : k == 1 ? "A thirsty " + cls + ". My favourite kind." : "*Marta flicks the cloth over her shoulder.* What'll it be, " + name + "?";
                case FolkId.Ysolde: return k == 0 ? "The stones hum when you are near, " + name + "." : k == 1 ? "A " + cls + "'s hands. Let me read what they can hold." : "*Ysolde's runes dim as she turns.* You have sigils for me, " + name + "?";
                default: return null;
            }
        }

        private void Start()
        {
            Add(FolkId.Silas, "Silas Crane, merchant", Lobby.MerchantSpot + Vector3.up * 2.1f, new[]
            {
                "Health potions are red because of what is in them. Do not ask what is in them.",
                "Mana potions are blue for the same reason. Different thing. Also do not ask.",
                "I have sold to every delver who ever went down that shaft. Repeat customers are rarer.",
                "Gold from below spends the same up here. It just smells of graves. Most gold does.",
                "Holy Water? See the priest. I sell things that work on the living.",
                "Frost flasks are bottled river from the year it froze. Shake well. Throw better.",
                "I have not taken this hood off since the war. Nobody remembers which war. Including me.",
            }, () =>
            {
                var me = Me;
                if (me != null && me.Grudge(FolkId.Silas) > 0) return "Still sore from earlier, friend. Still charging for it, too. Amends are cheaper than bruises.";
                if (me != null && me.CatPeed.Value) return "You smell like the cat's opinion of you. Potion? I'd charge you double for the smell, but I am a kind man.";
                if (Seals >= 1 && Seals < DungeonDirector.SealTotal) return "That fragment you carry hums. Do not try to sell it to me. Please. Do not even put it on the table.";
                if (Seals >= DungeonDirector.SealTotal) return "All four of them. I'll be honest, friend: I don't sell anything for where you're going.";
                if (Deeds.Has(Deeds.Deed.Unsung)) return "You came back from under the Cathedral. Everything in my shop is cheaper to a legend. Not by much.";
                return null;
            });

            Add(FolkId.Hobb, "Hobb Ironside, smith", Lobby.AnvilSpot + Vector3.back * 1.4f + Vector3.up * 2.1f, new[]
            {
                "The anvil only takes embers from below. I tried good coal once. It spat it back at me.",
                "That anvil was here before the castle. The castle was built around it. So was I, nearly.",
                "Bring me embers and I'll bring you an edge. Bring me excuses and I'll bring you a mop.",
                "A rune on a blade is older than the blade, older than me, older than Cathedra. Treat it nice.",
                "Every blade that goes down comes back heavier. Some of that is the blood. Most is the dark.",
                "Strike while it's hot? Down there it's always cold. Strike anyway.",
                "I sell embers, if you're too soft to dig them out of the dead yourself. Plate too. And one piece cut for you.",
            }, () =>
            {
                var me = Me;
                if (me != null && me.Grudge(FolkId.Hobb) > 0) return "Mind your elbows near my anvil. The bill's still higher for you, and it'll stay that way till you pay for the dent.";
                if (FloorsDone >= 1) return "Back up already? That means you didn't die. Good. Embers. Now.";
                if (me != null && me.WeaponLevel.Value >= 4) return "That blade's near as good as I can make it. Any better and it'll want a name.";
                if (Deeds.Has(Deeds.Deed.Gravewarden)) return "You put the old Baron down? My grandfather swore he'd hold that door till the end of days. Fair enough. It ended.";
                return null;
            });

            Add(FolkId.Aldric, "Brother Aldric, shrine", Lobby.ShrineSpot + Vector3.left * 1.2f + Vector3.up * 2.1f, new[]
            {
                "I will pray for you, child. For a small donation. A large one buys a louder prayer.",
                "The gods hear every prayer. Paid ones first. That's not doctrine, that's queueing.",
                "*Brother Aldric murmurs a psalm, opens one eye, and checks your belt for a purse.*",
                "Bless you. That one was free. The next will cost you ten.",
                "Indulgences! Forgiveness for every sin, priced by the sin. Grave-robbing is on special.",
                "I prayed for the last party that went down. Well. They hadn't paid in advance.",
                "The font is holy. The coins in it are mine. That's the arrangement.",
                "Kneel with me, child. Not too near the offering box.",
                "Lord, protect this delver... *he peeks up* ...if this delver tithes.",
                "Your soul looks heavy. I can lighten it. Your purse too.",
                "The cat is not mine. The cat is not anyone's. I feed it, and it permits this. I bill the castle.",
                "A prayer from me will catch you once, down there, when you fall. Forty-odd gold. Cheaper than a coffin.",
            }, () =>
            {
                var me = Me;
                if (me != null && me.Grudge(FolkId.Aldric) > 0) return "I have prayed for your temper, child. Prayers for the violent cost extra. Amends cost more.";
                if (me != null && me.PrayedFor.Value) return "My prayer goes with you, child. Paid in full. Do try not to need it.";
                if (me != null && me.Gold.Value >= 200) return "That purse looks heavy, child. Let the shrine carry some of it for you. For your soul.";
                if (me != null && me.CatWhisker.Value) return "The cat gave you a whisker. It has never given me one. I have fed it every day for nine years.";
                if (Journal.Count >= 6) return "You have been reading the old pages. Then you know what the hymn was really for.";
                if (Deeds.Has(Deeds.Deed.Cardinal)) return "You silenced the Cardinal. I will sing for them tonight. Somebody should.";
                if (FloorsDone >= 1 && Deeds.Has(Deeds.Deed.Gravewarden)) return "You passed the first door. Was the Baron at peace, at the end? Don't tell me. Let me guess yes.";
                return null;
            });

            Add(FolkId.Vey, "Mistress Vey, tailor", Lobby.TailorSpot + Vector3.right * 0.6f + Vector3.up * 2.1f, new[]
            {
                "A cloak says who you are before you open your mouth. Yours is mumbling.",
                "Renown is the only coin I take. Gold is for people with no taste.",
                "I can dye anything. I once dyed a ghoul. It was much improved.",
                "Bloodstains are not a colour, darling. They are a confession.",
                "The dead down there are terribly dressed. Four hundred years in the same shroud.",
            }, () =>
            {
                var me = Me;
                if (me != null && me.Grudge(FolkId.Vey) > 0) return "Oh. It's you. The one with the elbows. Do keep your distance from the silk.";
                if (me != null && me.CatPeed.Value) return "Is that... cat? On the good wool? Out. Out, and come back clean.";
                if (Deeds.Has(Deeds.Deed.SawAbyss)) return "You came back from below the Cathedral with your cloak intact. I am, genuinely, moved.";
                if (Deeds.Wins >= 1) return "A delver who finished the descent. Let me do something about that cloak before they write songs.";
                return null;
            });

            Add(FolkId.Corwin, "Corwin, carriage driver", Lobby.CarriageAt + new Vector3(0f, 3.1f, 2.2f), new[]
            {
                "I don't go down. The carriage goes down. I hold the reins and keep my eyes shut.",
                "Tell me how hard a road you want (F), then find a seat on the back. Think of somewhere nice. It helps. Not much.",
                "The horses won't look at the shaft either. Smart horses.",
                "Out the gate, down the old road, into the trees. After that it is not a road any more.",
                "Forty years on this seat. Never once seen the bottom. Intend to keep it that way.",
            }, () =>
            {
                var me = Me;
                if (me != null && me.Grudge(FolkId.Corwin) > 0) return "I'll drive you. I'll not talk to you. Sit on the back and keep your hands to yourself.";
                if (Seals >= DungeonDirector.SealTotal) return "Four Seals... I'll take you as far as the road goes, friend. Below the Cathedral, you walk.";
                if (Deeds.Wipes >= 1 && Deeds.Wipes < 3) return "I drove an empty carriage back up last time. Don't make me do that again.";
                if (Deeds.Wipes >= 3) return "I've driven that carriage back empty " + Deeds.Wipes + " times now. I've started talking to the seats.";
                if (Deeds.BossesFelled == 0 && Deeds.Wipes == 0) return "First time down? Keep your head, keep your potions, and pet the cat before you pick it up.";
                return null;
            });

            Add(FolkId.Bones, "Mister Bones, dealer", Lobby.BlackjackSpot + Vector3.back * 1f + Vector3.up * 2.2f, new[]
            {
                "*The dealer taps the felt twice.*",
                "*The dealer shuffles. It has been shuffling since before the castle was built.*",
                "*Its jaw clicks. It may be a laugh. It may be the cards.*",
                "*It deals itself a hand, looks at it, and folds. It always folds when nobody is playing.*",
                "*It points one finger at the shoe, then at you, then at the shaft. Nobody knows what it means.*",
            }, () => Me != null && Me.Grudge(FolkId.Bones) >= 3 ? "*The dealer keeps one empty hand raised toward you. Not yet.*"
                : ExpeditionHome >= Expedition.Members ? "*The dealer lays a whistle, a lantern, beads and a ring on the felt, then the Jack of Spades. Scratched under the jack: JORY. It deals you in.*"
                : ExpeditionHome == 3 ? "*The dealer has stopped shuffling. It looks at the roll on the tent wall, at the one name scratched out, and then at you.*"
                : ExpeditionHome == 2 ? "*The dealer sets two cards apart from the deck and will not deal them. It touches each one, gently.*"
                : ExpeditionHome == 1 ? "*The dealer turns its skull toward the roll on the tent wall, and the candle under it, for a long time.*"
                : Deeds.Has(Deeds.Deed.Unsung) ? "*The dealer stops shuffling. For the first time anyone can remember, it bows.*" : null);

            Add(FolkId.Brann, "Sergeant Brann, drillmaster", Lobby.YardSpot + Vector3.up * 2.2f, new[]
            {
                "Feet apart! Weight forward! You'll thank me when something with three arms comes at you.",
                "Every drill costs more than the last. That's not greed, recruit. That's interest.",
                "I trained half the delvers in this yard. The other half I buried in it.",
                "Swing, step, block. Swing, step, block. The dead don't get bored. Neither do I.",
                "Gold for sweat. Best trade in the castle.",
            }, () =>
            {
                var me = Me;
                if (me != null && me.Grudge(FolkId.Brann) > 0) return "You owe this yard a fine, recruit. Pay it and fall in.";
                if (FloorsDone >= 6) return "You've gone deeper than most I've trained. Don't let it go to your head. Go to your feet.";
                return null;
            });

            Add(FolkId.Ysolde, "Ysolde, runewright", Lobby.RunewrightSpot + Vector3.up * 2.1f, new[]
            {
                "Bring me three of a kind and I'll make one worth all three. The stones like a good bargain.",
                "A Sigil is a promise an ability makes to you. I just help it keep its word.",
                "Runes don't wear out. People do. Mind which one you look after.",
                "The stones hum louder when someone comes back from below. They're humming now.",
                "Set a rune crooked and it sings off key for the rest of the run. I set them straight.",
            }, () =>
            {
                var me = Me;
                if (me != null && me.Grudge(FolkId.Ysolde) > 0) return "The stones are still angry with you. Make amends.";
                if (me != null && me.RuneBagCount >= 3) return "You're carrying runes I could meld, you know. Three alike and I'll make one better.";
                return null;
            });

            Add(FolkId.Marta, "Marta, alewife", Lobby.TavernCenter + Vector3.up * 2.1f, new[]
            {
                "Mead's honest. Honey, water, time and a little of whatever fell in.",
                "The band knows four songs. They'll tell you it's five. It's four.",
                "Delvers drink to forget. Then they come back up and drink to remember who didn't.",
                "Silas sells potions. I sell the thing that makes you brave enough to need them.",
                "Corwin doesn't drink. Says the horse can smell it. The horse is dead, I tell him. Doesn't help.",
            }, () =>
            {
                var me = Me;
                if (me != null && me.TryGetComponent<PlayerDebuffs>(out var d) && d.Drunk >= PlayerDebuffs.DrunkMax) return "You're done, love. Sit. Water's free.";
                if (FloorsDone >= 4) return "You've been deeper than anyone who drinks here. Except the ones who don't come back to drink.";
                return null;
            });
        }

        private void Add(FolkId who, string name, Vector3 head, string[] idle, Func<string> news)
        {
            speakers[who] = new Speaker { Who = who, Name = name, Head = head, Idle = idle, News = news, NextAt = Time.time + Random.Range(0.5f, 3f), IdleIndex = Random.Range(0, idle.Length) };
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (now < pending[i].at) continue;
                Say(pending[i].who, pending[i].text, 5f);
                pending.RemoveAt(i);
            }

            var me = NetworkPlayer.Local;
            if (me == null) return;
            var mePos = me.transform.position;
            bool inCamp = DungeonDirector.InLobby;
            if (!inCamp && wasInCamp) greeted.Clear();
            wasInCamp = inCamp;

            foreach (var s in speakers.Values)
            {
                var fig = Art.FolkIdle.Of(s.Who);
                if (fig != null) s.Head = fig.HeadTop;
                if (inCamp && fig != null && !greeted.Contains(s.Who) && Vector3.Distance(mePos, fig.transform.position) < Art.FolkIdle.AttendRange)
                {
                    greeted.Add(s.Who);
                    string hello = Greeting(s.Who, me);
                    if (hello != null)
                    {
                        s.Current = hello;
                        s.Until = now + 5f;
                        s.NextAt = s.Until + Random.Range(3f, 6f);
                    }
                }

                if (now < s.Until && s.Current != null) continue;
                if ((mePos - s.Head).sqrMagnitude > TalkRange * TalkRange)
                {
                    if (now > s.Until) s.Current = null;
                    continue;
                }
                if (now < s.NextAt) continue;
                string news = s.News?.Invoke();
                if (news != null && s.Said.Add(news)) s.Current = news;
                else if (s.Idle.Length > 0) s.Current = s.Idle[s.IdleIndex++ % s.Idle.Length];
                s.Until = now + 6.5f;
                s.NextAt = s.Until + Random.Range(4f, 8f);
            }
        }

        private void OnGUI()
        {
            UiTimer.Begin();
            DrawAll();
            UiTimer.Mark("CampChatter");
        }

        private static void EnsureStyles()
        {
            if (bubbleStyle != null) return;
            bubbleStyle = new GUIStyle(UiSkin.Small) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperCenter };
            bubbleNameStyle = new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            nameStyle = new GUIStyle(UiSkin.Small) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            tradeStyle = new GUIStyle(UiSkin.Small) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }

        private void DrawAll()
        {
            useGUILayout = false;
            var me = NetworkPlayer.Local;
            var cam = Art.ViewCamera.Current;
            if (me == null || cam == null || PauseMenu.Open || GameHud.MenuOpen || GameHud.PanelShowing || Minimap.LargeOpen) return;
            UiSkin.Apply();
            EnsureStyles();
            float h = UiScale.H;
            DrawNames(cam, h);
            var camPos = cam.transform.position;
            float now = Time.time;
            foreach (var s in speakers.Values)
            {
                if (s.Current == null || now > s.Until) continue;
                var screen = UiScale.World(cam, s.Head);
                float dist = Vector3.Distance(camPos, s.Head);
                if (screen.z <= 0f || dist > ReadRange) continue;
                float a = Mathf.Clamp01((s.Until - now) / 0.6f) * Mathf.Clamp01((ReadRange - dist) / 3f);
                bubbleStyle.fontStyle = s.Current.StartsWith("*") ? FontStyle.Italic : FontStyle.Normal;
                bubbleStyle.normal.textColor = new Color(0.95f, 0.92f, 0.85f, a);
                bubbleNameStyle.normal.textColor = new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, a);
                measure.text = s.Current;
                float bh = bubbleStyle.CalcHeight(measure, BubbleWidth - 16f) + 26f;
                var r = new Rect(screen.x - BubbleWidth / 2f, h - screen.y - bh, BubbleWidth, bh);
                UiSkin.Rect(r, new Color(0.04f, 0.035f, 0.03f, 0.78f * a));
                GUI.Label(new Rect(r.x + 8, r.y + 4, BubbleWidth - 16, 18), s.Name.ToUpperInvariant(), bubbleNameStyle);
                GUI.Label(new Rect(r.x + 8, r.y + 22, BubbleWidth - 16, bh - 24), s.Current, bubbleStyle);
            }
        }

        private void DrawNames(Camera cam, float h)
        {
            if (!DungeonDirector.InLobby) return;
            var camPos = cam.transform.position;
            foreach (var f in Art.FolkIdle.All)
            {
                if (f == null || string.IsNullOrEmpty(f.DisplayName)) continue;
                if (f.Who.HasValue && speakers.TryGetValue(f.Who.Value, out var sp) && sp.Current != null && Time.time < sp.Until) continue;
                var top = f.HeadTop + Vector3.down * 0.25f;
                float dist = Vector3.Distance(camPos, top);
                if (dist > NameRange || dist < 0.8f) continue;
                var screen = UiScale.World(cam, top);
                if (screen.z <= 0f) continue;
                if (Physics.Linecast(camPos, top, out var hit, ~0, QueryTriggerInteraction.Ignore) && hit.distance < dist - 0.6f && !hit.transform.IsChildOf(f.transform)) continue;
                float a = Mathf.Clamp01((NameRange - dist) / 3f);
                float y = h - screen.y;
                var shadow = new Color(0f, 0f, 0f, 0.7f * a);
                Shadowed(new Rect(screen.x - 120, y - 30, 240, 18), f.DisplayName, nameStyle, new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, a), shadow);
                if (!string.IsNullOrEmpty(f.Trade))
                    Shadowed(new Rect(screen.x - 120, y - 13, 240, 14), f.Trade.ToUpperInvariant(), tradeStyle, new Color(0.88f, 0.85f, 0.78f, a), shadow);
            }
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, Color colour, Color shadow)
        {
            style.normal.textColor = shadow;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = colour;
            GUI.Label(r, text, style);
        }
    }
}
