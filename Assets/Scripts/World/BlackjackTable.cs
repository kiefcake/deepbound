using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Deadman's Hand: blackjack against a skeleton dealer, played to casino rules.
    ///
    /// <list type="bullet">
    /// <item>A six-deck shoe, reshuffled when three quarters have been dealt.</item>
    /// <item>You choose your bet (5 to 500 gold, never more than you carry) before the deal.</item>
    /// <item>The dealer peeks for blackjack under an Ace or a ten, stands on all 17s (S17).</item>
    /// <item>Blackjack pays 3:2. Insurance (half your bet) is offered against an Ace and pays 2:1.</item>
    /// <item>DOUBLE on any first two cards (one more card, bet doubled). SPLIT any pair, up to four
    ///   hands; split Aces get one card each and a split 21 is not a blackjack. SURRENDER the first
    ///   two cards for half your bet back (late surrender, after the peek).</item>
    /// </list>
    ///
    /// Server-authoritative: the shoe and every hand live on the server; each player is sent their
    /// own table state by an owner RPC and acts through server RPCs.
    /// </summary>
    public class BlackjackTable : Interactable
    {
        public const int MinBet = 5, MaxBet = 100, Decks = 6, MaxHands = 4;
        /// <summary>Win this much in one camp visit and the dealer stops dealing to you until you come back from the next floor.</summary>
        public const int CutOff = 300;
        /// <summary>Four stools: four players can sit and play at once, each against the dealer.</summary>
        public const int Stools = 4;
        public enum Phase : byte { Betting, Insurance, Playing, Done }

        private class Seat
        {
            public Phase Phase;
            public readonly List<List<byte>> Hands = new List<List<byte>>();
            public readonly List<int> Bets = new List<int>();
            public readonly List<bool> Doubled = new List<bool>(), Surrendered = new List<bool>();
            public readonly List<byte> House = new List<byte>();
            public int Active, Insurance, LastBet = 20;
            public bool SplitAces;
            public string Result = "";
            public float LastAction;
            /// <summary>Net winnings this camp visit, and which visit (the next depth when it was counted).</summary>
            public int NetThisVisit, Visit = -1;
        }

        private readonly Dictionary<ulong, Seat> seats = new Dictionary<ulong, Seat>();
        private readonly List<byte> shoe = new List<byte>();

        // ---- The local player's view of their seat ----
        public static bool LocalOpen { get; private set; }
        private static BlackjackTable localTable;
        private Phase seenPhase;
        private byte[][] seenHands = new byte[0][];
        private int[] seenBets = new int[0];
        private byte[] seenFlags = new byte[0];
        private byte[] seenHouse = new byte[0];
        private int seenActive, seenInsurance;
        private bool seenHoleHidden;
        private string seenResult = "";
        private int betChoice = 20;

        public override float Range => 3.4f;

        public override string Prompt(PlayerProgress who)
        {
            if (who.IsOwner && LocalOpen && localTable == this) return null;
            if (CutOffFor(who)) return "Deadman's Hand: the dealer has cut you off until the next floor";
            return who.Gold.Value < MinBet ? "Deadman's Hand: you need " + MinBet + " gold" : "Sit at Deadman's Hand (blackjack, up to " + Stools + " players)";
        }

        // ------------------------------------------------------------------
        // Cards: byte = suit * 13 + (rank - 1); rank 1 = Ace, 11-13 = J Q K.
        // ------------------------------------------------------------------

        public static int Rank(byte card) => card % 13 + 1;
        public static int Suit(byte card) => card / 13;
        private static int Value(byte card) { int r = Rank(card); return r >= 10 ? 10 : r; }

        public static int Total(IList<byte> cards, out bool soft)
        {
            int total = 0, aces = 0;
            foreach (var c in cards) { int v = Value(c); if (v == 1) { aces++; v = 11; } total += v; }
            while (total > 21 && aces > 0) { total -= 10; aces--; }
            soft = aces > 0;
            return total;
        }
        public static int Total(IList<byte> cards) => Total(cards, out _);
        private static bool Natural(IList<byte> cards) => cards.Count == 2 && Total(cards) == 21;

        private byte Draw()
        {
            if (shoe.Count < Decks * 52 / 4) Shuffle();
            var c = shoe[shoe.Count - 1];
            shoe.RemoveAt(shoe.Count - 1);
            return c;
        }

        private void Shuffle(bool announce = true)
        {
            shoe.Clear();
            for (int d = 0; d < Decks; d++) for (int c = 0; c < 52; c++) shoe.Add((byte)c);
            for (int i = shoe.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (shoe[i], shoe[j]) = (shoe[j], shoe[i]); }
            if (announce && IsServer) ShuffleFxRpc();
        }

        // ------------------------------------------------------------------
        // Server: the game
        // ------------------------------------------------------------------

        protected override void ServerUse(PlayerProgress who)
        {
            if (!seats.TryGetValue(who.NetworkObjectId, out var seat)) seats[who.NetworkObjectId] = seat = new Seat { Phase = Phase.Betting };
            seat.LastAction = Time.time;
            Send(who, seat);
        }

        private bool TryGet(NetworkObjectReference player, out PlayerProgress who, out Seat seat)
        {
            seat = null;
            who = null;
            if (!player.TryGet(out var obj) || !obj.TryGetComponent(out who)) return false;
            if (Vector3.Distance(obj.transform.position, transform.position) > Range + 2.5f) return false;
            if (!seats.TryGetValue(who.NetworkObjectId, out seat)) seats[who.NetworkObjectId] = seat = new Seat { Phase = Phase.Betting };
            seat.LastAction = Time.time;
            return true;
        }

        /// <summary>Owner → server: place a bet and deal.</summary>
        [Rpc(SendTo.Server)]
        public void DealRpc(NetworkObjectReference player, int bet)
        {
            if (!TryGet(player, out var who, out var seat) || (seat.Phase != Phase.Betting && seat.Phase != Phase.Done)) return;
            // Strike the dealer and it will not deal to you until the grudge eases.
            if (who.Grudge(FolkId.Bones) >= 3) { RefusedRpc(who.name); return; }
            // Won too much this visit: the house has seen enough of you until the next floor.
            if (CutOffFor(who)) { CutOffRpc(who.name); return; }
            bet = Mathf.Clamp(bet, MinBet, MaxBet);
            if (who.Gold.Value < bet) return;
            ServerDeal(who, seat, bet, null);
        }

        private static int Visit => DungeonDirector.Instance != null ? DungeonDirector.Instance.NextDepth.Value : 0;

        /// <summary>Server: is this player cut off for the rest of this camp visit?</summary>
        public bool CutOffFor(PlayerProgress who) => seats.TryGetValue(who.NetworkObjectId, out var seat) && seat.Visit == Visit && seat.NetThisVisit >= CutOff;

        [Rpc(SendTo.ClientsAndHost)]
        private void CutOffRpc(string who)
        {
            UI.CampChatter.Say(FolkId.Bones, "*The dealer gathers " + who + "'s cards, taps the table twice and points down the shaft. Come back after the next floor.*", 6f);
            if (NetworkPlayer.Local != null && NetworkPlayer.Local.name == who) UI.GameHud.Feed("Mister Bones has cut you off: you've won " + CutOff + "+ gold this visit. Come back after the next floor.", new Color(1f, 0.75f, 0.4f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RefusedRpc(string who) => UI.CampChatter.Say(FolkId.Bones, "*The dealer slides " + who + "'s chips back across the felt. Not tonight.*", 5f);

        /// <summary>Server (tests): deal with chosen cards (player's first two, then dealer's two).</summary>
        /// <summary>Tests: set this visit's net winnings at the table.</summary>
        public void ServerDebugWinnings(PlayerProgress who, int net)
        {
            if (!seats.TryGetValue(who.NetworkObjectId, out var seat)) seats[who.NetworkObjectId] = seat = new Seat();
            seat.Visit = Visit;
            seat.NetThisVisit = net;
        }

        /// <summary>Tests: the stake of the hand in play.</summary>
        public int ServerStake(PlayerProgress who) => seats.TryGetValue(who.NetworkObjectId, out var s) ? s.LastBet : 0;

        public void ServerDebugDeal(PlayerProgress who, int bet, byte[] cards)
        {
            if (!seats.TryGetValue(who.NetworkObjectId, out var seat)) seats[who.NetworkObjectId] = seat = new Seat();
            seat.Phase = Phase.Betting;
            ServerDeal(who, seat, bet, cards);
        }

        private void ServerDeal(PlayerProgress who, Seat seat, int bet, byte[] forced)
        {
            who.ServerAddGold(-bet);
            seat.LastBet = bet;
            seat.Hands.Clear(); seat.Bets.Clear(); seat.Doubled.Clear(); seat.Surrendered.Clear(); seat.House.Clear();
            seat.Active = 0; seat.Insurance = 0; seat.SplitAces = false; seat.Result = "";
            var hand = new List<byte>();
            byte Next(int i) => forced != null && i < forced.Length ? forced[i] : Draw();
            hand.Add(Next(0)); seat.House.Add(Next(2)); hand.Add(Next(1)); seat.House.Add(Next(3));
            seat.Hands.Add(hand); seat.Bets.Add(bet); seat.Doubled.Add(false); seat.Surrendered.Add(false);
            DealFxRpc();
            // An Ace showing: insurance first. Otherwise the dealer peeks under a ten, then play begins.
            if (Rank(seat.House[0]) == 1) { seat.Phase = Phase.Insurance; Send(who, seat); return; }
            AfterPeek(who, seat);
        }

        /// <summary>Owner → server: take insurance (true) or decline it, against the dealer's Ace.</summary>
        [Rpc(SendTo.Server)]
        public void InsuranceRpc(NetworkObjectReference player, bool take)
        {
            if (!TryGet(player, out var who, out var seat) || seat.Phase != Phase.Insurance) return;
            if (take)
            {
                int cost = seat.Bets[0] / 2;
                if (who.Gold.Value >= cost) { who.ServerAddGold(-cost); seat.Insurance = cost; }
            }
            AfterPeek(who, seat);
        }

        private void AfterPeek(PlayerProgress who, Seat seat)
        {
            bool dealerNatural = Natural(seat.House);
            bool peeks = Rank(seat.House[0]) == 1 || Value(seat.House[0]) == 10;
            if (peeks && dealerNatural) { Settle(who, seat); return; }
            if (seat.Insurance > 0) seat.Result = "No dealer blackjack: insurance lost. ";
            if (Natural(seat.Hands[0])) { Settle(who, seat); return; }
            seat.Phase = Phase.Playing;
            Send(who, seat);
        }

        [Rpc(SendTo.Server)]
        public void HitRpc(NetworkObjectReference player)
        {
            if (!TryGet(player, out var who, out var seat) || seat.Phase != Phase.Playing) return;
            var hand = seat.Hands[seat.Active];
            hand.Add(Draw());
            DealFxRpc();
            if (Total(hand) >= 21) NextHand(who, seat);
            else Send(who, seat);
        }

        [Rpc(SendTo.Server)]
        public void StandRpc(NetworkObjectReference player)
        {
            if (!TryGet(player, out var who, out var seat)) return;
            if (seat.Phase == Phase.Insurance) { AfterPeek(who, seat); return; }
            if (seat.Phase != Phase.Playing) return;
            NextHand(who, seat);
        }

        public static bool CanDouble(int cards, bool splitAces) => cards == 2 && !splitAces;

        [Rpc(SendTo.Server)]
        public void DoubleRpc(NetworkObjectReference player)
        {
            if (!TryGet(player, out var who, out var seat) || seat.Phase != Phase.Playing) return;
            var hand = seat.Hands[seat.Active];
            int bet = seat.Bets[seat.Active];
            if (!CanDouble(hand.Count, seat.SplitAces) || who.Gold.Value < bet) return;
            who.ServerAddGold(-bet);
            seat.Bets[seat.Active] = bet * 2;
            seat.Doubled[seat.Active] = true;
            hand.Add(Draw());
            DealFxRpc();
            NextHand(who, seat);
        }

        public static bool CanSplit(IList<byte> hand, int hands) => hand.Count == 2 && Rank(hand[0]) == Rank(hand[1]) && hands < MaxHands;

        [Rpc(SendTo.Server)]
        public void SplitRpc(NetworkObjectReference player)
        {
            if (!TryGet(player, out var who, out var seat) || seat.Phase != Phase.Playing) return;
            var hand = seat.Hands[seat.Active];
            int bet = seat.Bets[seat.Active];
            if (!CanSplit(hand, seat.Hands.Count) || seat.SplitAces || who.Gold.Value < bet) return;
            who.ServerAddGold(-bet);
            var second = new List<byte> { hand[1] };
            hand.RemoveAt(1);
            seat.Hands.Insert(seat.Active + 1, second);
            seat.Bets.Insert(seat.Active + 1, bet);
            seat.Doubled.Insert(seat.Active + 1, false);
            seat.Surrendered.Insert(seat.Active + 1, false);
            hand.Add(Draw());
            second.Add(Draw());
            DealFxRpc();
            if (Rank(hand[0]) == 1)
            {
                // Split Aces: one card each, and that is all.
                seat.SplitAces = true;
                Settle(who, seat);
                return;
            }
            if (Total(hand) >= 21) NextHand(who, seat);
            else Send(who, seat);
        }

        public static bool CanSurrender(int cards, int hands) => cards == 2 && hands == 1;

        [Rpc(SendTo.Server)]
        public void SurrenderRpc(NetworkObjectReference player)
        {
            if (!TryGet(player, out var who, out var seat) || seat.Phase != Phase.Playing) return;
            if (!CanSurrender(seat.Hands[0].Count, seat.Hands.Count)) return;
            seat.Surrendered[0] = true;
            Settle(who, seat);
        }

        private void NextHand(PlayerProgress who, Seat seat)
        {
            seat.Active++;
            if (seat.Active < seat.Hands.Count)
            {
                // A split hand that already stands at 21 moves straight on.
                if (Total(seat.Hands[seat.Active]) >= 21) { NextHand(who, seat); return; }
                Send(who, seat);
                return;
            }
            Settle(who, seat);
        }

        /// <summary>The dealer plays out (unless every hand is already bust or surrendered), then each hand is paid.</summary>
        private void Settle(PlayerProgress who, Seat seat)
        {
            seat.Active = Mathf.Min(seat.Active, seat.Hands.Count - 1);
            bool split = seat.Hands.Count > 1;
            bool anyLive = false;
            for (int i = 0; i < seat.Hands.Count; i++)
                if (!seat.Surrendered[i] && Total(seat.Hands[i]) <= 21 && !(Natural(seat.Hands[i]) && !split)) anyLive = true;
            bool dealerNatural = Natural(seat.House);
            if (anyLive && !dealerNatural)
                while (true)
                {
                    int t = Total(seat.House, out bool soft);
                    if (t > 17 || (t == 17)) break; // stands on all 17s
                    seat.House.Add(Draw());
                }
            int house = Total(seat.House);
            int paid = 0, staked = 0;
            var lines = new System.Text.StringBuilder(seat.Result);
            if (seat.Insurance > 0 && dealerNatural)
            {
                paid += seat.Insurance * 3;
                lines.Append("Insurance pays " + seat.Insurance * 2 + ". ");
            }
            staked += seat.Insurance;
            for (int i = 0; i < seat.Hands.Count; i++)
            {
                var hand = seat.Hands[i];
                int bet = seat.Bets[i], mine = Total(hand);
                staked += bet;
                string label = split ? "Hand " + (i + 1) + ": " : "";
                bool natural = Natural(hand) && !split;
                if (seat.Surrendered[i]) { paid += bet / 2; lines.Append(label + "surrendered, " + bet / 2 + " back. "); }
                else if (mine > 21) lines.Append(label + "bust. ");
                else if (natural && !dealerNatural) { paid += bet + bet * 3 / 2; lines.Append(label + "BLACKJACK! "); }
                else if (dealerNatural && !natural) lines.Append(label + "dealer blackjack. ");
                else if (natural && dealerNatural) { paid += bet; lines.Append(label + "push. "); }
                else if (house > 21 || mine > house) { paid += bet * 2; lines.Append(label + "win. "); }
                else if (mine == house) { paid += bet; lines.Append(label + "push. "); }
                else lines.Append(label + "lose. ");
            }
            who.ServerAddGold(paid);
            int net = paid - staked;
            if (seat.Visit != Visit) { seat.Visit = Visit; seat.NetThisVisit = 0; }
            seat.NetThisVisit += net;
            if (seat.NetThisVisit >= CutOff) CutOffRpc(who.name);
            lines.Append(net > 0 ? "  +" + net + " gold" : net < 0 ? "  " + net + " gold" : "  even");
            seat.Result = lines.ToString();
            seat.Phase = Phase.Done;
            Send(who, seat);
            if (net != 0) ResultFxRpc(who.name, net);
        }

        private void Send(PlayerProgress who, Seat seat)
        {
            // Flatten for the RPC: hand lengths, then all cards; flags per hand (bit 0 doubled, bit 1 surrendered).
            var lengths = new List<byte>();
            var cards = new List<byte>();
            var flags = new List<byte>();
            for (int i = 0; i < seat.Hands.Count; i++)
            {
                lengths.Add((byte)seat.Hands[i].Count);
                cards.AddRange(seat.Hands[i]);
                flags.Add((byte)((seat.Doubled[i] ? 1 : 0) | (seat.Surrendered[i] ? 2 : 0)));
            }
            bool holeHidden = seat.Phase == Phase.Insurance || seat.Phase == Phase.Playing;
            SeatRpc((byte)seat.Phase, lengths.ToArray(), cards.ToArray(), flags.ToArray(), seat.Bets.ToArray(), seat.House.ToArray(), holeHidden,
                seat.Active, seat.Insurance, seat.Result, seat.LastBet, RpcTarget.Single(who.OwnerClientId, RpcTargetUse.Temp));
            BroadcastTable();
        }

        /// <summary>Server: everyone at the table sees who else is playing and how they stand.</summary>
        private void BroadcastTable()
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var kv in seats)
            {
                if (n >= Stools || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(kv.Key, out var o) || o == null) continue;
                var st = kv.Value;
                string state = st.Phase == Phase.Playing || st.Phase == Phase.Insurance ? (st.Hands.Count > 0 ? "playing " + Total(st.Hands[Mathf.Min(st.Active, st.Hands.Count - 1)]) : "playing")
                    : st.Phase == Phase.Done ? (st.Result.Contains("+") ? "won" : st.Result.Contains("-") ? "lost" : "even") : "betting";
                if (st.Visit == Visit && st.NetThisVisit >= CutOff) state = "cut off";
                sb.Append(n > 0 ? "   ·   " : "").Append(o.name.Replace("(Clone)", "").Trim()).Append(": ").Append(state);
                n++;
            }
            TableRpc(sb.ToString());
        }

        private string tableLine = "";

        [Rpc(SendTo.ClientsAndHost)]
        private void TableRpc(string line) => tableLine = line;

        [Rpc(SendTo.SpecifiedInParams)]
        private void SeatRpc(byte phase, byte[] lengths, byte[] cards, byte[] flags, int[] bets, byte[] house, bool holeHidden, int active, int insurance, string result, int lastBet, RpcParams p = default)
        {
            seenPhase = (Phase)phase;
            seenHands = new byte[lengths.Length][];
            int at = 0;
            for (int i = 0; i < lengths.Length; i++) { seenHands[i] = new byte[lengths[i]]; System.Array.Copy(cards, at, seenHands[i], 0, lengths[i]); at += lengths[i]; }
            seenFlags = flags; seenBets = bets; seenHouse = house; seenHoleHidden = holeHidden; seenActive = active; seenInsurance = insurance; seenResult = result;
            if (!LocalOpen) betChoice = Mathf.Clamp(lastBet, MinBet, MaxBet);
            LocalOpen = true;
            localTable = this;
        }

        // ---- State for tests ----
        public Phase ServerPhase(PlayerProgress who) => seats.TryGetValue(who.NetworkObjectId, out var s) ? s.Phase : Phase.Betting;
        public int ServerHandCount(PlayerProgress who) => seats.TryGetValue(who.NetworkObjectId, out var s) ? s.Hands.Count : 0;

        [Rpc(SendTo.ClientsAndHost)]
        private void DealFxRpc() => Sfx.Play(Sound.Card, transform.position + Vector3.up, 0.8f, Random.Range(0.9f, 1.2f));

        [Rpc(SendTo.ClientsAndHost)]
        private void ShuffleFxRpc()
        {
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.6f, 1.8f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 1.6f, "THE DEALER SHUFFLES", new Color(0.85f, 0.8f, 0.7f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ResultFxRpc(string who, int net)
        {
            var color = net > 0 ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.6f, 0.5f);
            UI.GameHud.Feed(who + " at Deadman's Hand: " + (net > 0 ? "+" : "") + net + " gold", color);
            UI.CampChatter.CardsSettled(who, net);
            if (net > 0) Sfx.Play(Sound.Coins, transform.position + Vector3.up, 1f);
        }

        // ------------------------------------------------------------------
        // Local: the table panel
        // ------------------------------------------------------------------

        private void Update()
        {
            // Server: a seat left too long, or a player who walked off, is settled as it stands.
            if (IsServer && seats.Count > 0)
            {
                foreach (var id in new List<ulong>(seats.Keys))
                {
                    var seat = seats[id];
                    if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var o) || o == null) { seats.Remove(id); continue; }
                    bool live = seat.Phase == Phase.Playing || seat.Phase == Phase.Insurance;
                    bool gone = Vector3.Distance(o.transform.position, transform.position) > 6f || Time.time - seat.LastAction > 90f;
                    if (live && gone) Settle(o.GetComponent<PlayerProgress>(), seat);
                    else if (!live && gone) seats.Remove(id);
                }
            }
            if (localTable != this || !LocalOpen) return;
            var local = NetworkPlayer.Local;
            if (local == null || Vector3.Distance(local.transform.position, transform.position) > 5f) { LocalOpen = false; return; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (seenPhase == Phase.Playing || seenPhase == Phase.Insurance) StandRpc(local.NetworkObject);
                LocalOpen = false;
            }
        }

        private static readonly string[] Suits = { "♠", "♥", "♦", "♣" };
        private static Font suitFont;
        private static Font SuitFont
        {
            get
            {
                if (suitFont == null) { try { suitFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Symbol", "Arial Unicode MS", "Arial" }, 30); } catch { } }
                return suitFont;
            }
        }
        private static string RankName(byte c) { int r = Rank(c); return r == 1 ? "A" : r == 11 ? "J" : r == 12 ? "Q" : r == 13 ? "K" : r.ToString(); }

        private void OnGUI()
        {
            if (localTable != this || !LocalOpen) return;
            UI.UiSkin.Apply();
            GUI.depth = -15;
            var local = NetworkPlayer.Local;
            if (local == null) return;
            var prog = local.GetComponent<PlayerProgress>();
            float w = Screen.width, h = Screen.height;
            float pw = Mathf.Min(760f, w - 40f), ph = 430f, px = w / 2f - pw / 2f, py = Mathf.Max(20f, h - ph - 120f);
            UI.UiSkin.Panel(new Rect(px - 10, py - 10, pw + 20, ph + 20), 0.96f);
            // Felt.
            UI.UiSkin.Rect(new Rect(px, py, pw, ph), new Color(0.06f, 0.2f, 0.1f, 0.95f));
            GUI.Label(new Rect(px, py + 6, pw, 26), "DEADMAN'S HAND", new GUIStyle(UI.UiSkin.Heading) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(px, py + 30, pw, 18), "Blackjack pays 3 to 2   ·   Dealer stands on all 17s   ·   Insurance pays 2 to 1   ·   6 decks",
                new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.85f, 0.8f, 0.6f) } });
            GUI.Label(new Rect(px + pw - 180, py + 6, 170, 22), prog.Gold.Value + " gold", new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleRight, normal = { textColor = UI.UiSkin.Gold } });

            bool dealt = seenPhase != Phase.Betting;
            if (dealt)
            {
                DrawRow(px + 20, py + 56, "DEALER", seenHouse, seenHoleHidden, -1, false);
                float rowY = py + 168;
                float rowW = seenHands.Length > 1 ? (pw - 40) / seenHands.Length : pw - 40;
                for (int i = 0; i < seenHands.Length; i++)
                {
                    bool active = seenPhase == Phase.Playing && i == seenActive;
                    string tag = (seenHands.Length > 1 ? "HAND " + (i + 1) : "YOU") + "   bet " + (i < seenBets.Length ? seenBets[i] : 0) +
                        (i < seenFlags.Length && (seenFlags[i] & 1) != 0 ? "  (doubled)" : "") + (i < seenFlags.Length && (seenFlags[i] & 2) != 0 ? "  (surrendered)" : "");
                    if (active) UI.UiSkin.Rect(new Rect(px + 16 + i * rowW, rowY - 4, rowW - 8, 118), new Color(1f, 0.85f, 0.4f, 0.12f));
                    DrawRow(px + 20 + i * rowW, rowY, tag, seenHands[i], false, seenHands.Length > 1 ? 34f : 62f, active);
                }
                if (seenInsurance > 0) GUI.Label(new Rect(px + 20, py + 290, 300, 18), "Insurance: " + seenInsurance, UI.UiSkin.Small);
            }
            else GUI.Label(new Rect(px, py + 120, pw, 40), "Place your bet.", new GUIStyle(UI.UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 24 });

            float by = py + ph - 100;
            switch (seenPhase)
            {
                case Phase.Betting:
                case Phase.Done:
                    if (seenPhase == Phase.Done)
                        GUI.Label(new Rect(px + 10, by - 34, pw - 20, 30), seenResult, new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold, normal = { textColor = UI.UiSkin.Gold } });
                    DrawBetControls(px, by, pw, prog, local);
                    break;
                case Phase.Insurance:
                    GUI.Label(new Rect(px, by - 30, pw, 24), "The dealer shows an Ace. Insurance costs " + (seenBets.Length > 0 ? seenBets[0] / 2 : 0) + " and pays 2 to 1 if the dealer has blackjack.",
                        new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleCenter });
                    if (GUI.Button(new Rect(px + pw / 2f - 250, by + 10, 230, 44), "TAKE INSURANCE", UI.UiSkin.Button)) InsuranceRpc(local.NetworkObject, true);
                    if (GUI.Button(new Rect(px + pw / 2f + 20, by + 10, 230, 44), "NO INSURANCE", UI.UiSkin.Button)) InsuranceRpc(local.NetworkObject, false);
                    break;
                case Phase.Playing:
                    var hand = seenActive < seenHands.Length ? seenHands[seenActive] : new byte[0];
                    int bet = seenActive < seenBets.Length ? seenBets[seenActive] : 0;
                    bool canDouble = CanDouble(hand.Length, false) && prog.Gold.Value >= bet;
                    bool canSplit = CanSplit(hand, seenHands.Length) && prog.Gold.Value >= bet;
                    bool canSurrender = CanSurrender(hand.Length, seenHands.Length);
                    float bw = (pw - 60) / 5f;
                    if (Button(px + 20, by + 10, bw, "HIT", true)) HitRpc(local.NetworkObject);
                    if (Button(px + 20 + (bw + 5), by + 10, bw, "STAND", true)) StandRpc(local.NetworkObject);
                    if (Button(px + 20 + (bw + 5) * 2, by + 10, bw, "DOUBLE", canDouble)) DoubleRpc(local.NetworkObject);
                    if (Button(px + 20 + (bw + 5) * 3, by + 10, bw, "SPLIT", canSplit)) SplitRpc(local.NetworkObject);
                    if (Button(px + 20 + (bw + 5) * 4, by + 10, bw, "SURRENDER", canSurrender)) SurrenderRpc(local.NetworkObject);
                    GUI.Label(new Rect(px, by + 60, pw, 18), "Double: one card, bet doubled   ·   Split: a pair into two hands   ·   Surrender: give up, half back",
                        new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
                    break;
            }
            GUI.Label(new Rect(px, py + ph - 20, pw, 18), "Esc or walk away to leave the table", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.7f, 0.7f, 0.6f) } });
        }

        private static bool Button(float x, float y, float width, string text, bool enabled)
        {
            var was = GUI.enabled;
            GUI.enabled = enabled;
            bool pressed = GUI.Button(new Rect(x, y, width, 44), text, UI.UiSkin.Button);
            GUI.enabled = was;
            return pressed && enabled;
        }

        private void DrawBetControls(float px, float by, float pw, PlayerProgress prog, NetworkPlayer local)
        {
            int max = Mathf.Min(MaxBet, prog.Gold.Value);
            betChoice = Mathf.Clamp(betChoice, MinBet, Mathf.Max(MinBet, max));
            float cx = px + pw / 2f;
            if (Button(cx - 330, by + 10, 60, "-25", betChoice - 25 >= MinBet)) betChoice -= 25;
            if (Button(cx - 265, by + 10, 50, "-5", betChoice - 5 >= MinBet)) betChoice -= 5;
            GUI.Label(new Rect(cx - 210, by + 10, 120, 44), "BET  " + betChoice, new GUIStyle(UI.UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
            if (Button(cx - 85, by + 10, 50, "+5", betChoice + 5 <= max)) betChoice += 5;
            if (Button(cx - 30, by + 10, 60, "+25", betChoice + 25 <= max)) betChoice += 25;
            if (Button(cx + 35, by + 10, 60, "MAX", max >= MinBet)) betChoice = Mathf.Max(MinBet, max);
            bool can = prog.Gold.Value >= betChoice && betChoice >= MinBet;
            if (Button(cx + 105, by + 10, 220, can ? (seenPhase == Phase.Done ? "DEAL AGAIN" : "DEAL") : "NOT ENOUGH GOLD", can)) DealRpc(local.NetworkObject, betChoice);
            GUI.Label(new Rect(px, by + 60, pw, 18), "Bets from " + MinBet + " to " + MaxBet + " gold   ·   win " + CutOff + " in one visit and the dealer cuts you off until the next floor", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
            if (!string.IsNullOrEmpty(tableLine))
                GUI.Label(new Rect(px, by + 80, pw, 18), "AT THE TABLE:  " + tableLine, new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = UI.UiSkin.Gold } });
        }

        private void DrawRow(float x, float y, string who, byte[] cards, bool holeHidden, float spacing, bool active)
        {
            int shownTotal = Total(holeHidden && cards.Length > 0 ? new[] { cards[0] } : cards, out bool soft);
            string total = holeHidden ? shownTotal + " + ?" : (soft && shownTotal < 21 ? "soft " : "") + shownTotal + (shownTotal > 21 ? "  BUST" : Natural(cards) ? "  BLACKJACK" : "");
            GUI.Label(new Rect(x, y, 400, 20), who + "    " + total, new GUIStyle(UI.UiSkin.Body) { fontStyle = active ? FontStyle.Bold : FontStyle.Normal, normal = { textColor = active ? UI.UiSkin.Gold : new Color(0.9f, 0.9f, 0.85f) } });
            float step = spacing < 0 ? 62f : spacing;
            for (int i = 0; i < cards.Length; i++)
            {
                var r = new Rect(x + i * step, y + 24, 56, 78);
                UI.UiSkin.Rect(new Rect(r.x + 2, r.y + 2, r.width, r.height), new Color(0f, 0f, 0f, 0.4f));
                UI.UiSkin.Rect(r, new Color(0.96f, 0.93f, 0.85f));
                if (holeHidden && i == 1)
                {
                    UI.UiSkin.Rect(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), new Color(0.4f, 0.08f, 0.1f));
                    UI.UiSkin.Rect(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 20), new Color(0.55f, 0.15f, 0.15f));
                    continue;
                }
                bool red = Suit(cards[i]) == 1 || Suit(cards[i]) == 2;
                var ink = red ? new Color(0.72f, 0.1f, 0.1f) : new Color(0.1f, 0.08f, 0.08f);
                GUI.Label(new Rect(r.x + 5, r.y + 2, r.width, 22), RankName(cards[i]), new GUIStyle(UI.UiSkin.Heading) { fontSize = 18, alignment = TextAnchor.UpperLeft, normal = { textColor = ink } });
                GUI.Label(new Rect(r.x, r.y + 20, r.width, r.height - 20), Suits[Suit(cards[i])], new GUIStyle() { font = SuitFont, fontSize = 30, alignment = TextAnchor.MiddleCenter, normal = { textColor = ink } });
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) Shuffle(announce: false);
            // A long felt table, candles, and the dealer: a skeleton waiting with the shoe.
            MeshKit.Part(transform, "Top", MeshKit.Box(new Vector3(2.6f, 0.08f, 1.1f)), Mat.Wood, Vector3.up * 0.88f);
            var felt = MeshKit.Part(transform, "Felt", MeshKit.Box(new Vector3(2.4f, 0.02f, 0.92f)), Mat.Cloth, Vector3.up * 0.93f);
            for (int st = 0; st < Stools; st++)
            {
                float sx = -0.96f + st * 0.64f;
                MeshKit.Part(transform, "Stool", MeshKit.Cylinder(0.17f, 0.15f, 0.06f, 10), Mat.Wood, new Vector3(sx, 0.62f, -0.95f));
                MeshKit.Part(transform, "StoolLeg", MeshKit.Cylinder(0.04f, 0.05f, 0.6f, 6), Mat.Wood, new Vector3(sx, 0.3f, -0.95f));
                var chips = MeshKit.Part(transform, "Chips", MeshKit.Cylinder(0.04f, 0.04f, 0.03f, 8), Mat.Gold, new Vector3(sx, 0.95f, -0.3f));
            }
            ArtLibrary.Tint(felt.GetComponent<Renderer>(), new Color(0.1f, 0.28f, 0.14f));
            foreach (float x in new[] { -1.2f, 1.2f })
                foreach (float z in new[] { -0.45f, 0.45f })
                    MeshKit.Part(transform, "Leg", MeshKit.Box(new Vector3(0.08f, 0.86f, 0.08f)), Mat.Wood, new Vector3(x, 0.43f, z));
            MeshKit.Part(transform, "Shoe", MeshKit.Box(new Vector3(0.18f, 0.1f, 0.3f)), Mat.Wood, new Vector3(0.55f, 0.99f, 0.25f));
            for (int i = 0; i < 3; i++)
                MeshKit.Part(transform, "Card", MeshKit.Box(new Vector3(0.1f, 0.005f, 0.14f)), Mat.Bone, new Vector3(-0.3f + i * 0.14f, 0.95f, 0.1f), new Vector3(0f, i * 8f - 8f, 0f));
            var dealer = CharacterArt.BuildSkeleton(transform, CharacterArt.SkeletonKind.Warrior);
            dealer.transform.localPosition = new Vector3(0f, 0f, 0.95f);
            dealer.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Props.Candles(transform, transform.position + transform.right * 0.7f + Vector3.up * 0.93f, new Color(1f, 0.6f, 0.3f));
        }
    }
}
