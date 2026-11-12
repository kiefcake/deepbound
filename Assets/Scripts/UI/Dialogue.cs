using System.Collections.Generic;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    /// <summary>
    /// TALKING TO THE CAMP FOLK (2026-11-08, the user: "tie the dialogue option to the character not where they are,
    /// give the important npcs, and tavern maid dialogue options. they should also give out quests").
    /// Look at Silas, Hobb, Aldric, Vey, Corwin, Brann, Ysolde or Marta within talking range - wherever their errands have
    /// taken them - and press F. The panel offers their trade (the stall panel, the wardrobe, the runes, a mead), WORK
    /// (their quest, see <c>DungeonDirector.Bounties</c>), the news, who they are (more with favour) and goodbye.
    /// Number keys or clicks choose; Esc, F or walking off ends it. Local, on the owner; added by <see cref="NetworkPlayer"/>.
    /// </summary>
    public class Dialogue : MonoBehaviour
    {
        /// <summary>The camp person under the local player's crosshair, within talking range (null: nobody).</summary>
        public static Art.FolkIdle Aimed { get; private set; }
        public static bool Open { get; private set; }
        public static FolkId? Talking => Open && local != null ? local.who : (FolkId?)null;

        private static Dialogue local;
        private NetworkPlayer player;
        private PlayerLife life;
        private PlayerProgress progress;

        private enum Page { Main, Work }
        private FolkId who;
        private Page page;
        private string line;
        private int about;
        private float openedAt;
        /// <summary>Who we asked the server to hold still (through the talk and any panel it opened).</summary>
        private FolkId? holding;
        private readonly List<(string label, System.Action act)> options = new List<(string, System.Action)>();

        public void Init(NetworkPlayer p)
        {
            player = p;
            life = GetComponent<PlayerLife>();
            progress = GetComponent<PlayerProgress>();
            if (p.IsOwner) local = this;
        }

        public static string NameOf(FolkId w) => w switch
        {
            FolkId.Silas => "Silas", FolkId.Hobb => "Hobb", FolkId.Aldric => "Brother Aldric", FolkId.Vey => "Mistress Vey",
            FolkId.Corwin => "Corwin", FolkId.Bones => "Mister Bones", FolkId.Brann => "Sergeant Brann", FolkId.Ysolde => "Ysolde",
            FolkId.Marta => "Marta", _ => "the peddler",
        };

        /// <summary>F was pressed with nothing nearer to do: start talking to whoever is aimed at. True if it did.</summary>
        public static bool TryBegin()
        {
            if (local == null || Aimed == null || Open || !Aimed.Who.HasValue) return false;
            local.Begin(Aimed.Who.Value);
            return true;
        }

        /// <summary>Tests: open a conversation without aiming.</summary>
        public static void DebugBegin(FolkId w) { if (local != null) local.Begin(w); }
        public static void DebugClose() { if (local != null) local.Close(); }
        /// <summary>Tests: the option labels showing now.</summary>
        public static List<string> DebugOptions()
        {
            var l = new List<string>();
            if (local != null) { local.Build(); foreach (var o in local.options) l.Add(o.label); }
            return l;
        }
        public static void DebugChoose(int i) { if (local != null) { local.Build(); if (i >= 0 && i < local.options.Count) local.options[i].act(); } }
        public static string DebugLine => local != null ? local.line : null;

        private void Begin(FolkId w)
        {
            who = w;
            page = Page.Main;
            about = 0;
            openedAt = Time.time;
            line = CampChatter.GreetingFor(w) ?? "Yes?";
            if (progress != null && progress.Grudge(w) >= 2) line = "*" + NameOf(w) + " looks at you coldly.* What.";
            Open = true;
            Hold(w);
            Art.Sfx.Play2D(Art.Sound.UiOpen, 0.6f);
        }

        private void Close()
        {
            if (!Open) return;
            Open = false;
            Art.Sfx.Play2D(Art.Sound.UiClose, 0.6f);
        }

        private GameHud Hud => GetComponent<GameHud>();

        /// <summary>Keep <paramref name="w"/> at their place while the local player uses their station (released with the panel).</summary>
        public static void HoldFor(FolkId w) { if (local != null) local.Hold(w); }

        private void Hold(FolkId w)
        {
            if (holding == w) return;
            Release();
            holding = w;
            progress?.TalkHoldRpc((byte)w, true);
        }

        private void Release()
        {
            if (holding == null) return;
            progress?.TalkHoldRpc((byte)holding.Value, false);
            holding = null;
        }

        private void Update()
        {
            if (player == null || !player.IsOwner) return;
            bool can = DungeonDirector.InLobby && life != null && !life.Downed.Value;
            Aimed = can && !Open && !OtherPanelOpen && !GameHud.MenuOpen ? FindAimed() : null;
            // A workstation the crosshair actually enters, nearer than the person, comes first (2026-11-12): Hobb stands
            // right behind his anvil, Silas behind his table; looking up at their faces still talks to them.
            if (Aimed != null && World.CampStations.Aimed(player.AimOrigin, player.AimForward, out float sd) != null
                && sd < Vector3.Distance(player.AimOrigin, Vector3.Lerp(Aimed.transform.position, Aimed.HeadTop, 0.65f)))
                Aimed = null;
            // Held through the talk AND the stall / wardrobe / runes it opened; let go once every panel is shut.
            if (holding != null && !Open && !GameHud.MenuOpen && !Wardrobe.Open) Release();
            if (!Open) return;
            if (!can || !FolkPlaces.InReach(who, transform.position, 1.2f)) { Close(); return; }
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (Binds.Down(Bind.Use) && Time.time - openedAt > 0.25f) { Close(); return; }
            if (kb != null)
                for (int i = 0; i < 9; i++)
                    if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                    {
                        Build();
                        if (i < options.Count) options[i].act();
                        break;
                    }
        }

        private static bool OtherPanelOpen => BlackjackTable.LocalOpen || DiceTable.LocalOpen || PokerDiceTable.LocalSeated || CharacterSelect.Open || PauseMenu.Open || Wardrobe.Open || DevMenu.Open;

        /// <summary>The camp person nearest the crosshair, within talking range and roughly looked at.</summary>
        private Art.FolkIdle FindAimed()
        {
            var eye = player.AimOrigin;
            var fwd = player.AimForward;
            Art.FolkIdle best = null;
            float bestAngle = float.MaxValue;
            foreach (var f in Art.FolkIdle.All)
            {
                if (f == null || !f.Who.HasValue || !DungeonDirector.GivesQuests(f.Who.Value)) continue;
                var feet = f.transform.position;
                var flat = feet - transform.position; flat.y = 0f;
                if (flat.magnitude > FolkPlaces.TalkRange(f.Who.Value)) continue;
                var chest = Vector3.Lerp(feet, f.HeadTop, 0.65f);
                float angle = Vector3.Angle(fwd, chest - eye);
                float limit = flat.magnitude < 1.6f ? 40f : 26f;
                if (angle > limit || angle >= bestAngle) continue;
                best = f; bestAngle = angle;
            }
            return best;
        }

        // ---- The choices ----
        private void Build()
        {
            options.Clear();
            var d = DungeonDirector.Instance;
            if (page == Page.Work && d != null)
            {
                if (WorkOffer(d, out _)) options.Add(("I'll do it.", () =>
                {
                    progress.TakeQuestRpc((byte)who);
                    line = DialogueLines.Accept(who);
                    page = Page.Main;
                    Art.Sfx.Play2D(Art.Sound.PageTurn, 0.7f);
                }));
                options.Add(("Not now.", () => { page = Page.Main; line = "Suit yourself."; }));
                return;
            }
            options.Add((DialogueLines.TradeLabel(who), Trade));
            if (who == FolkId.Hobb && Anvil.Near(transform.position) != null) options.Add(("Work the forge.", () => { Close(); Hud?.OpenForge(); }));
            var craft = DialogueLines.CraftLabel(who);
            if (craft != null) options.Add((craft, () => { line = DialogueLines.Craft(who, player); }));
            options.Add(("Got any work?", () => { page = Page.Work; line = null; }));
            options.Add(("What's the news?", () => { line = CampChatter.NewsFor(who) ?? "Nothing worth the breath."; }));
            options.Add(("Tell me about yourself.", () =>
            {
                var lines = DialogueLines.About(who, d != null ? d.Favour(who) : 0);
                line = lines[about % lines.Length];
                about++;
            }));
            options.Add(("Farewell.", Close));
        }

        private void Trade()
        {
            var hud = Hud;
            switch (who)
            {
                case FolkId.Silas: Close(); hud?.OpenStall(Shop.Vendor.Merchant); break;
                case FolkId.Hobb: Close(); hud?.OpenStall(Shop.Vendor.Smith); break;
                case FolkId.Aldric: Close(); hud?.OpenStall(Shop.Vendor.Shrine); break;
                case FolkId.Corwin: Close(); hud?.OpenStall(Shop.Vendor.Carriage); break;
                case FolkId.Brann: Close(); hud?.OpenStall(Shop.Vendor.Yard); break;
                case FolkId.Ysolde: Close(); hud?.OpenRunes(); break;
                case FolkId.Vey: Close(); Wardrobe.RequestOpen(); break;
                case FolkId.Marta:
                    var keep = TavernKeep.Instance;
                    if (keep == null) { line = "The bar's shut, love."; break; }
                    if (progress.Gold.Value < TavernKeep.MeadPrice) { line = "Mead's " + TavernKeep.MeadPrice + " gold, love. Come back with coin."; break; }
                    keep.UseRpc(player.NetworkObject);
                    line = "*Marta fills a horn and slides it over.* Mind the foam.";
                    break;
            }
        }

        /// <summary>The Work page's state: true when there is an offer you can take now (text in <paramref name="say"/>).</summary>
        private bool WorkOffer(DungeonDirector d, out string say)
        {
            int idx = d.QuestIndexFrom(who);
            if (idx >= 0)
            {
                var q = d.Quests[idx];
                say = q.Status == DungeonDirector.ContractStatus.Taken ? "You've got my job. It starts when you reach the next floor."
                    : "You're on my job: " + DungeonDirector.QuestTask(q) + "  (" + q.Progress + " / " + q.Goal + ")";
                return false;
            }
            if (progress != null && progress.Grudge(who) >= 3) { say = DialogueLines.Grudged(who); return false; }
            string why = d.CannotTakeQuest(who);
            if (why != null)
            {
                say = why.StartsWith("That was all") ? "That's all I have for you this visit. Come back after the next floor."
                    : why.StartsWith("Three") ? "You've three jobs on already. Finish one and come back." : why;
                return false;
            }
            var offer = d.OfferOf(who);
            say = DialogueLines.Pitch(who, offer.Contract, offer.Special);
            return true;
        }

        // ---- Drawing ----
        private GUIStyle optStyle, nameStyle, bodyStyle, taskStyle;

        private void OnGUI()
        {
            if (!Open || player == null || !player.IsOwner) return;
            useGUILayout = false;
            UiSkin.Apply();
            Build();
            float W = UiScale.W, H = UiScale.H;
            optStyle ??= new GUIStyle(UiSkin.Button) { alignment = TextAnchor.MiddleLeft, fontSize = 16, padding = new RectOffset(14, 10, 4, 4) };
            nameStyle ??= new GUIStyle(UiSkin.Heading) { fontSize = 20 };
            bodyStyle ??= new GUIStyle(UiSkin.Body) { fontSize = 16 };
            taskStyle ??= new GUIStyle(UiSkin.Body) { fontSize = 14 };

            var d = DungeonDirector.Instance;
            string say = line;
            string task = null, reward = null;
            if (page == Page.Work && d != null)
            {
                bool offer = WorkOffer(d, out var w);
                say = w;
                if (offer)
                {
                    var q = d.OfferOf(who);
                    task = (q.Special ? "SPECIAL  -  " : "") + DungeonDirector.ContractName(q.Contract).ToUpperInvariant() + ":  " + DungeonDirector.QuestTask(q) + " on the next floor";
                    reward = "Reward: " + DungeonDirector.QuestReward(q) + "  +  " + NameOf(who) + "'s favour";
                }
            }

            // To the right of the crosshair so the person's face stays in view (the controls hint hides while it is open).
            float pw = Mathf.Min(580f, W * 0.46f);
            float textH = bodyStyle.CalcHeight(new GUIContent(say ?? ""), pw - 40f);
            var taskBold = new GUIStyle(taskStyle) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.95f, 0.9f, 0.8f) } };
            var rewardGold = new GUIStyle(taskStyle) { normal = { textColor = UiSkin.Gold } };
            float taskH = task != null ? taskBold.CalcHeight(new GUIContent(task), pw - 40f) : 0f;
            float rewardH = reward != null ? rewardGold.CalcHeight(new GUIContent(reward), pw - 40f) : 0f;
            float extra = (task != null ? taskH + 6f : 0f) + (reward != null ? rewardH + 4f : 0f);
            float ph = 54f + textH + extra + 14f + options.Count * 34f + 14f;
            // Above the potion belt and the ability bar (it sat over both).
            var r = new Rect(W - pw - 36f, Mathf.Max(70f, H - ph - Mathf.Max(150f, H * 0.2f)), pw, ph);
            UiSkin.Panel(r, 0.95f);

            var fig = Art.FolkIdle.Of(who);
            string title = (fig != null ? fig.DisplayName : NameOf(who)) + (fig != null && !string.IsNullOrEmpty(fig.Trade) ? "   ·   " + fig.Trade : "");
            GUI.Label(new Rect(r.x + 20, r.y + 10, pw - 200, 30), title, nameStyle);
            if (d != null)
            {
                int fav = d.Favour(who);
                string pips = "";
                for (int i = 0; i < DungeonDirector.MaxFavourDiscount; i++) pips += i < fav ? "●" : "○";
                GUI.Label(new Rect(r.xMax - 200, r.y + 14, 180, 24), "FAVOUR  " + pips, new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleRight, fontSize = 13, normal = { textColor = UiSkin.Gold } });
            }
            float y = r.y + 46f;
            GUI.Label(new Rect(r.x + 20, y, pw - 40, textH), say ?? "", bodyStyle);
            y += textH + 6f;
            if (task != null) { GUI.Label(new Rect(r.x + 20, y, pw - 40, taskH), task, taskBold); y += taskH + 6f; }
            if (reward != null) { GUI.Label(new Rect(r.x + 20, y, pw - 40, rewardH), reward, rewardGold); y += rewardH + 4f; }
            y += 8f;
            for (int i = 0; i < options.Count; i++)
            {
                if (GUI.Button(new Rect(r.x + 20, y, pw - 40, 30), (i + 1) + ".   " + options[i].label, optStyle))
                {
                    options[i].act();
                    break;
                }
                y += 34f;
            }
        }

        private void OnDisable()
        {
            if (local == this) { Open = false; Aimed = null; }
            if (player != null && player.IsOwner && player.IsSpawned) Release();
        }
    }
}
