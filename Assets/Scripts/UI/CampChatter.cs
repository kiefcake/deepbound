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
            public float Until, NextAt;
            public int IdleIndex;
            public readonly HashSet<string> Said = new HashSet<string>();
        }

        private readonly List<Speaker> speakers = new List<Speaker>();

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
                "No refunds on Holy Water. The priest gets very particular about where it came from.",
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
                if (screen.z <= 0f || dist > 11f) continue;
                float a = Mathf.Clamp01((s.Until - Time.time) / 0.6f) * Mathf.Clamp01((11f - dist) / 2f);
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
