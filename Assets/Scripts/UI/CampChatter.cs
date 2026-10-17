using System.Collections.Generic;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// The people of Castle Ravenhold talk. Walk near one and they say something over their head:
    /// news first (what you have done: a guardian felled, a Seal carried, the cat's verdict, a party
    /// lost), then idle talk. Each line is local to the listener and reads their own deeds, run and
    /// journal, so the camp remembers you. Built with the camp; gone when you descend.
    /// </summary>
    public class CampChatter : MonoBehaviour
    {
        private class Speaker
        {
            public string Name;
            public Vector3 Head;
            public string[] Idle;
            public System.Func<string> News; // a line for what has happened, or null
            public string Current;
            public float Until, NextAt, StartledUntil;
            public int IdleIndex;
            public readonly HashSet<string> Said = new HashSet<string>();
        }

        private readonly List<Speaker> speakers = new List<Speaker>();
        private static CampChatter instance;

        private void OnEnable() => instance = this;
        private void OnDisable() { if (instance == this) instance = null; }

        // What each of them says when struck, by grudge level 1..4. {0} is who did it.
        private static readonly string[][] Struck =
        {
            new[] { "{0}! Mind where you point that. ...Prices just went up, by the way.", "Again? That's another quarter on everything, friend. Pain is a cost of business.",
                    "Hit me once more and I'll sell you river water and call it a potion.", "I've doubled my prices for you, {0}. I'd triple them, but I'm a reasonable man." },
            new[] { "Oi! I've been hit by better. That dent's going on your bill.", "That's it. Embers cost more for people who swing at the smith.",
                    "I've broken bigger than you over this anvil, {0}. Prices up. Again.", "Keep swinging. Every bruise is on your bill now. Double." },
            new[] { "Peace, child. ...Though the shrine will expect a larger gift from you now.", "Violence on holy ground. The font weighs heavier for you, {0}.",
                    "I forgive you. The offering box does not.", "The dead are watching you do this. So am I. So is the price." },
            new[] { "Darling. The velvet. Not the velvet.", "If you tear this coat I will dye you a colour you cannot wash off.",
                    "Out of my light, {0}. Come back with manners. And money.", "*Mistress Vey turns her back on you and measures the air, pointedly.*" },
            new[] { "Easy! Easy! I just drive the thing!", "Hit the driver again and see who gets you down that shaft.",
                    "One more, {0}, and you're walking. All the way down. It's a long way.", "*Corwin pulls his hat over his eyes and refuses to look at you.*" },
            new[] { "*The dealer's jaw clacks shut. It taps the felt, slowly.*", "*It turns one card over: the Ace of Spades. It points it at you.*",
                    "*The dealer folds its hands. It will not deal to {0} until tempers cool.*", "*It sweeps the cards away and simply stares at you.*" },
        };

        private static readonly string[] Forgive =
        {
            "Coin heals all wounds, {0}. Most of them. Prices are back where they were.",
            "Hmph. Paid for the dent. We're square, {0}. Don't do it again.",
            "The shrine accepts your penance, {0}. Go in peace. Swing at the dead instead.",
            "", "", "",
        };

        private static readonly string[] Startled =
        {
            "Not near the glassware!", "Ha! Felt that one in my teeth.", "Not on holy ground, please.",
            "Smoke! On the silk!", "Whoa! Whoa! Easy, horses!", "*The dealer brushes ash off the cards.*",
        };

        /// <summary>Any peer: a camp character was struck by <paramref name="attacker"/> (grudge now <paramref name="level"/>).</summary>
        public static void Offended(FolkId who, int level, string attacker, Vector3 body)
        {
            var lines = Struck[(int)who];
            Say(who, string.Format(lines[Mathf.Clamp(level, 1, lines.Length) - 1], attacker), 6f);
            var fig = Art.FolkIdle.Near(body);
            var from = NetworkPlayer.Local != null ? NetworkPlayer.Local.transform.position : body - Vector3.forward;
            foreach (var p in Player.PlayerLife.All) if (p != null && p.name == attacker) from = p.transform.position;
            if (fig != null) fig.Flinch(from, 1f);
        }

        /// <summary>Any peer: amends were paid.</summary>
        public static void Forgiven(FolkId who, string payer)
        {
            var line = Forgive[(int)who];
            if (!string.IsNullOrEmpty(line)) Say(who, string.Format(line, payer), 6f);
        }

        /// <summary>Any peer: something exploded at camp. Everyone near it jumps and says so.</summary>
        public static void Blast(Vector3 at)
        {
            if (instance == null) return;
            for (int i = 0; i < instance.speakers.Count; i++)
            {
                var s = instance.speakers[i];
                var foot = new Vector3(s.Head.x, at.y, s.Head.z);
                if ((foot - at).sqrMagnitude > 9f * 9f || Time.time < s.StartledUntil) continue;
                s.StartledUntil = Time.time + 10f;
                Say((FolkId)i, Startled[i], 4f);
                var fig = Art.FolkIdle.Near(foot);
                if (fig != null) fig.Flinch(at, 0.7f);
            }
        }

        /// <summary>Make a speaker say something now (over whatever they were saying).</summary>
        public static void Say(FolkId who, string text, float seconds)
        {
            if (instance == null || (int)who >= instance.speakers.Count) return;
            var s = instance.speakers[(int)who];
            s.Current = text;
            s.Until = Time.time + seconds;
            s.NextAt = s.Until + Random.Range(4f, 8f);
        }

        private static PlayerProgress Me => NetworkPlayer.Local != null ? NetworkPlayer.Local.GetComponent<PlayerProgress>() : null;
        private static DungeonDirector Dir => DungeonDirector.Instance;
        private static int FloorsDone => Dir != null ? Dir.NextDepth.Value : 0;
        private static int Seals => Dir != null ? Dir.SealCount : 0;

        private void Start()
        {
            Add("Silas Crane, merchant", Lobby.MerchantSpot + Vector3.up * 2.1f, new[]
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
                if (me != null && me.CatPeed.Value) return "You smell like the cat's opinion of you. Potion? I'd charge you double for the smell, but I am a kind man.";
                if (Seals >= 1 && Seals < 3) return "That fragment you carry hums. Do not try to sell it to me. Please. Do not even put it on the table.";
                if (Seals >= 3) return "Three of them. I'll be honest, friend: I don't sell anything for where you're going.";
                if (Deeds.Has(Deeds.Deed.Unsung)) return "You came back from under the Cathedral. Everything in my shop is cheaper to a legend. Not by much.";
                return null;
            });

            Add("Hobb Ironside, smith", Lobby.AnvilSpot + Vector3.back * 1.4f + Vector3.up * 2.1f, new[]
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
                if (FloorsDone >= 1) return "Back up already? That means you didn't die. Good. Embers. Now.";
                if (me != null && me.WeaponLevel.Value >= 4) return "That blade's near as good as I can make it. Any better and it'll want a name.";
                if (Deeds.Has(Deeds.Deed.Gravewarden)) return "You put the old Baron down? My grandfather swore he'd hold that door till the end of days. Fair enough. It ended.";
                return null;
            });

            Add("Brother Aldric, shrine", Lobby.ShrineSpot + Vector3.left * 1.2f + Vector3.up * 2.1f, new[]
            {
                "The shrine grants one blessing for one descent. Whose blessing? Kneel, and don't ask.",
                "The lantern at the top of the shaft was lit from the Cardinal's own censer. Keep it lit.",
                "A waystone gives back what the dark takes. It has never yet refused a soul. Never yet.",
                "I pray for every delver. Out loud, so the dead can hear it and know you're spoken for.",
                "The Holy Water comes from the font. The font is fed from below. I try not to think about it.",
                "The cat? The cat is not mine. The cat is not anyone's. I feed it, and it permits this.",
                "The relics on the altar came up in dead hands. Give to the font, and one may go down again in yours.",
            }, () =>
            {
                var me = Me;
                if (me != null && me.CatWhisker.Value) return "The cat gave you a whisker. It has never given me one. I have fed it every day for nine years.";
                if (Journal.Count >= 6) return "You have been reading the old pages. Then you know what the hymn was really for.";
                if (Deeds.Has(Deeds.Deed.Cardinal)) return "You silenced the Cardinal. I will sing for them tonight. Somebody should.";
                if (FloorsDone >= 1 && Deeds.Has(Deeds.Deed.Gravewarden)) return "You passed the first door. Was the Baron at peace, at the end? Don't tell me. Let me guess yes.";
                return null;
            });

            Add("Mistress Vey, tailor", Lobby.TailorSpot + Vector3.right * 0.6f + Vector3.up * 2.1f, new[]
            {
                "A cloak says who you are before you open your mouth. Yours is mumbling.",
                "Renown is the only coin I take. Gold is for people with no taste.",
                "I can dye anything. I once dyed a ghoul. It was much improved.",
                "Bloodstains are not a colour, darling. They are a confession.",
                "The dead down there are terribly dressed. Four hundred years in the same shroud.",
            }, () =>
            {
                var me = Me;
                if (me != null && me.CatPeed.Value) return "Is that... cat? On the good wool? Out. Out, and come back clean.";
                if (Deeds.Has(Deeds.Deed.SawAbyss)) return "You came back from below the Cathedral with your cloak intact. I am, genuinely, moved.";
                if (Deeds.Wins >= 1) return "A delver who finished the descent. Let me do something about that cloak before they write songs.";
                return null;
            });

            Add("Corwin, carriage driver", Lobby.BoardingCenter + new Vector3(5.6f, 2.6f, 1f), new[]
            {
                "I don't go down. The carriage goes down. I hold the reins and keep my eyes shut.",
                "Stand in the gold circle when you're ready. Think of somewhere nice. It helps. Not much.",
                "The horses won't look at the shaft either. Smart horses.",
                "The light takes you down and the light brings you up. I just drive the bit in between.",
                "Forty years on this seat. Never once seen the bottom. Intend to keep it that way.",
            }, () =>
            {
                if (Seals >= 3) return "Three Seals... The carriage won't go where that leads, friend. You'll be walking that part.";
                if (Deeds.Wipes >= 1 && Deeds.Wipes < 3) return "I drove an empty carriage back up last time. Don't make me do that again.";
                if (Deeds.Wipes >= 3) return "I've driven that carriage back empty " + Deeds.Wipes + " times now. I've started talking to the seats.";
                if (Deeds.BossesFelled == 0 && Deeds.Wipes == 0) return "First time down? Keep your head, keep your potions, and pet the cat before you pick it up.";
                return null;
            });

            Add("Mister Bones, dealer", Lobby.BlackjackSpot + Vector3.back * 1f + Vector3.up * 2.2f, new[]
            {
                "*The dealer taps the felt twice.*",
                "*The dealer shuffles. It has been shuffling since before the castle was built.*",
                "*Its jaw clicks. It may be a laugh. It may be the cards.*",
                "*It deals itself a hand, looks at it, and folds. It always folds when nobody is playing.*",
                "*It points one finger at the shoe, then at you, then at the shaft. Nobody knows what it means.*",
            }, () => Deeds.Has(Deeds.Deed.Unsung) ? "*The dealer stops shuffling. For the first time anyone can remember, it bows.*" : null);
        }

        private void Add(string name, Vector3 head, string[] idle, System.Func<string> news)
        {
            speakers.Add(new Speaker { Name = name, Head = head, Idle = idle, News = news, NextAt = Time.time + Random.Range(0.5f, 3f), IdleIndex = Random.Range(0, idle.Length) });
        }

        private void Update()
        {
            var me = NetworkPlayer.Local;
            if (me == null) return;
            foreach (var s in speakers)
            {
                bool near = Vector3.Distance(me.transform.position, s.Head) < 9f;
                if (Time.time < s.Until && s.Current != null) continue; // still saying something (a reaction, perhaps)
                if (!near) { if (Time.time > s.Until) s.Current = null; continue; }
                if (Time.time < s.NextAt) continue;
                // News first (each line once a visit), then the idle talk in turn.
                string news = s.News?.Invoke();
                if (news != null && s.Said.Add(news)) s.Current = news;
                else { s.Current = s.Idle[s.IdleIndex % s.Idle.Length]; s.IdleIndex++; }
                s.Until = Time.time + 6.5f;
                s.NextAt = s.Until + Random.Range(4f, 8f);
            }
        }

        private void OnGUI()
        {
            var me = NetworkPlayer.Local;
            var cam = Art.ViewCamera.Current;
            if (me == null || cam == null || PauseMenu.Open || GameHud.MenuOpen) return;
            UiSkin.Apply();
            float h = Screen.height;
            foreach (var s in speakers)
            {
                if (s.Current == null || Time.time > s.Until) continue;
                var screen = cam.WorldToScreenPoint(s.Head);
                float dist = Vector3.Distance(cam.transform.position, s.Head);
                if (screen.z <= 0f || dist > 16f) continue;
                float a = Mathf.Clamp01((s.Until - Time.time) / 0.6f) * Mathf.Clamp01((16f - dist) / 3f);
                var body = new GUIStyle(UiSkin.Small) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperCenter, fontStyle = s.Current.StartsWith("*") ? FontStyle.Italic : FontStyle.Normal,
                    normal = { textColor = new Color(0.95f, 0.92f, 0.85f, a) } };
                const float bw = 280f;
                float bh = body.CalcHeight(new GUIContent(s.Current), bw - 16f) + 26f;
                var r = new Rect(screen.x - bw / 2f, h - screen.y - bh, bw, bh);
                UiSkin.Rect(r, new Color(0.04f, 0.035f, 0.03f, 0.78f * a));
                GUI.Label(new Rect(r.x + 8, r.y + 4, bw - 16, 18), s.Name.ToUpperInvariant(), new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, a) } });
                GUI.Label(new Rect(r.x + 8, r.y + 22, bw - 16, bh - 24), s.Current, body);
            }
        }
    }
}
