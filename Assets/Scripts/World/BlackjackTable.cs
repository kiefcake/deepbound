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
        private BlackjackView view;
        private float resultShownAt = -10f;
        private string lastResult = "";

        // What the view (BlackjackView) reads.
        public Phase SeenPhase => seenPhase;
        public byte[][] SeenHands => seenHands;
        public byte[] SeenHouse => seenHouse;
        public bool SeenHoleHidden => seenHoleHidden;
        public int[] SeenBets => seenBets;
        public int BetChoice => betChoice;

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
            if (!string.IsNullOrEmpty(result) && result != lastResult && seenPhase == Phase.Done) resultShownAt = Time.time;
            lastResult = result;
            bool opening = !LocalOpen || localTable != this;
            LocalOpen = true;
            localTable = this;
            if (opening && view != null) view.SitLocal();
        }

        // ---- State for tests ----
        public Phase ServerPhase(PlayerProgress who) => seats.TryGetValue(who.NetworkObjectId, out var s) ? s.Phase : Phase.Betting;
        public int ServerHandCount(PlayerProgress who) => seats.TryGetValue(who.NetworkObjectId, out var s) ? s.Hands.Count : 0;

        [Rpc(SendTo.ClientsAndHost)]
        private void DealFxRpc()
        {
            Sfx.Play(Sound.Card, transform.position + Vector3.up, 0.8f, Random.Range(0.9f, 1.2f));
            // The dealer's hand goes out over the felt with each deal.
            if (dealerRig != null) dealerRig.PlayEmote(Emote.Point, 0.5f);
        }

        private HumanoidRig dealerRig;

        [Rpc(SendTo.ClientsAndHost)]
        private void ShuffleFxRpc()
        {
            Sfx.Play(Sound.Chips, transform.position + Vector3.up, 0.8f);
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
            if (local == null || Vector3.Distance(local.transform.position, transform.position) > 5f || local.GetComponent<PlayerLife>().Downed.Value) { Leave(); return; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || UI.PauseMenu.Open) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (seenPhase == Phase.Playing || seenPhase == Phase.Insurance) StandRpc(local.NetworkObject);
                Leave();
                return;
            }
            // Keys, casino style: the bet with the arrows or the wheel, SPACE deals and hits.
            var prog = local.GetComponent<PlayerProgress>();
            var no = local.NetworkObject;
            bool shift = kb.shiftKey.isPressed;
            switch (seenPhase)
            {
                case Phase.Betting:
                case Phase.Done:
                    int step = shift ? 25 : 5;
                    float wheel = UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y : 0f;
                    if (kb.rightArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || wheel > 0.1f) betChoice += step;
                    if (kb.leftArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || wheel < -0.1f) betChoice -= step;
                    betChoice = Mathf.Clamp(betChoice, MinBet, Mathf.Max(MinBet, Mathf.Min(MaxBet, prog.Gold.Value)));
                    if ((kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) && prog.Gold.Value >= betChoice) DealRpc(no, betChoice);
                    break;
                case Phase.Insurance:
                    if (kb.yKey.wasPressedThisFrame) InsuranceRpc(no, true);
                    if (kb.nKey.wasPressedThisFrame) InsuranceRpc(no, false);
                    break;
                case Phase.Playing:
                    var hand = seenActive < seenHands.Length ? seenHands[seenActive] : new byte[0];
                    int bet = seenActive < seenBets.Length ? seenBets[seenActive] : 0;
                    if (kb.spaceKey.wasPressedThisFrame) HitRpc(no);
                    else if (kb.sKey.wasPressedThisFrame) StandRpc(no);
                    else if (kb.dKey.wasPressedThisFrame && CanDouble(hand.Length, false) && prog.Gold.Value >= bet) DoubleRpc(no);
                    else if (kb.pKey.wasPressedThisFrame && CanSplit(hand, seenHands.Length) && prog.Gold.Value >= bet) SplitRpc(no);
                    else if (kb.rKey.wasPressedThisFrame && CanSurrender(hand.Length, seenHands.Length)) SurrenderRpc(no);
                    break;
            }
        }

        /// <summary>Local: get up from the table (moved: something else already moved the player).</summary>
        public void Leave(bool moved = false)
        {
            LocalOpen = false;
            if (view != null) view.StandLocal(moved);
        }

        private static string RankName(byte c) { int r = Rank(c); return r == 1 ? "A" : r == 11 ? "J" : r == 12 ? "Q" : r == 13 ? "K" : r.ToString(); }

        private void OnGUI()
        {
            Deepbound.UI.UiTimer.Begin();
            OnGUIBody();
            Deepbound.UI.UiTimer.Mark("Blackjack");
        }

        private static GUIStyle pill, big, keyCap, label;

        /// <summary>
        /// The seated HUD (2026-11-07, casino style): no panel over the felt. Totals float beside the cards, the gold and the
        /// rules sit at the top, and a strip of actions with their keys runs along the bottom.
        /// </summary>
        private void OnGUIBody()
        {
            useGUILayout = false; // no GUILayout here: skip the layout pass (perf)
            if (localTable != this || !LocalOpen) return;
            UI.UiSkin.Apply();
            GUI.depth = -15;
            var local = NetworkPlayer.Local;
            var cam = Art.ViewCamera.Current;
            if (local == null || cam == null) return;
            var prog = local.GetComponent<PlayerProgress>();
            float w = UI.UiScale.W, h = UI.UiScale.H;
            pill ??= new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16, wordWrap = false };
            big ??= new GUIStyle(UI.UiSkin.Title) { alignment = TextAnchor.MiddleCenter, fontSize = 40 };
            keyCap ??= new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13, wordWrap = false, normal = { textColor = new Color(0.1f, 0.08f, 0.06f) } };
            label ??= new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleLeft, fontSize = 17, wordWrap = false };

            // Top: the table's name, its rules, your gold, who else plays.
            UI.UiSkin.Rect(new Rect(0, 0, w, 64), new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(24, 8, 400, 28), "DEADMAN'S HAND", new GUIStyle(UI.UiSkin.Heading) { fontSize = 22 });
            GUI.Label(new Rect(24, 36, w * 0.6f, 20), "Blackjack pays 3 to 2  ·  Dealer stands on all 17s  ·  Insurance pays 2 to 1  ·  Bets " + MinBet + "-" + MaxBet,
                new GUIStyle(UI.UiSkin.Small) { wordWrap = false, normal = { textColor = new Color(0.85f, 0.8f, 0.6f) } });
            GUI.Label(new Rect(w - 324, 10, 300, 28), prog.Gold.Value + " gold", new GUIStyle(UI.UiSkin.Heading) { alignment = TextAnchor.MiddleRight, fontSize = 22, normal = { textColor = UI.UiSkin.Gold } });
            if (!string.IsNullOrEmpty(tableLine))
                GUI.Label(new Rect(w - 724, 38, 700, 20), tableLine, new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleRight, wordWrap = false });

            // Totals beside the cards.
            bool dealt = seenPhase != Phase.Betting;
            if (dealt && view != null && view.Stool >= 0)
            {
                if (seenHouse.Length > 0)
                {
                    int shown = Total(seenHoleHidden ? new[] { seenHouse[0] } : seenHouse, out bool hsoft);
                    Pill(cam, view.HouseLabelPoint, "DEALER  " + (seenHoleHidden ? shown + " + ?" : TotalText(seenHouse, shown, hsoft)), new Color(0.85f, 0.82f, 0.75f));
                }
                for (int i = 0; i < seenHands.Length; i++)
                {
                    int t = Total(seenHands[i], out bool soft);
                    bool active = seenPhase == Phase.Playing && i == seenActive;
                    string tag = TotalText(seenHands[i], t, soft) + (i < seenFlags.Length && (seenFlags[i] & 1) != 0 ? "  x2" : "") + (i < seenFlags.Length && (seenFlags[i] & 2) != 0 ? "  SURRENDERED" : "");
                    Pill(cam, view.HandLabelPoint(i, seenHands.Length), tag, active ? UI.UiSkin.Gold : new Color(0.9f, 0.88f, 0.84f), active);
                }
            }

            // The result, large, for a few seconds.
            if (seenPhase == Phase.Done && Time.time - resultShownAt < 4f && !string.IsNullOrEmpty(seenResult))
            {
                float a = Mathf.Clamp01((4f - (Time.time - resultShownAt)) / 0.6f);
                bool won = seenResult.Contains("+"), lost = seenResult.Contains("-");
                var col = won ? new Color(1f, 0.85f, 0.35f, a) : lost ? new Color(0.95f, 0.45f, 0.4f, a) : new Color(0.9f, 0.9f, 0.85f, a);
                UI.UiSkin.Rect(new Rect(0, h * 0.2f, w, 70), new Color(0f, 0f, 0f, 0.5f * a));
                GUI.Label(new Rect(0, h * 0.2f, w, 70), UI.UiSkin.Fit(seenResult.ToUpperInvariant(), big, w - 80), new GUIStyle(big) { normal = { textColor = col } });
            }

            // The bottom strip: what you can do, and its key.
            float by = h - 88;
            UI.UiSkin.Rect(new Rect(0, by - 12, w, 100), new Color(0f, 0f, 0f, 0.55f));
            var no = local.NetworkObject;
            float x = 40f;
            switch (seenPhase)
            {
                case Phase.Betting:
                case Phase.Done:
                {
                    int max = Mathf.Min(MaxBet, prog.Gold.Value);
                    betChoice = Mathf.Clamp(betChoice, MinBet, Mathf.Max(MinBet, max));
                    GUI.Label(new Rect(x, by, 90, 40), "BET", new GUIStyle(label) { fontStyle = FontStyle.Bold, normal = { textColor = UI.UiSkin.Gold } });
                    x += 56;
                    if (Act(ref x, by, "<", "", betChoice - 5 >= MinBet)) betChoice -= 5;
                    GUI.Label(new Rect(x, by, 90, 40), betChoice.ToString(), new GUIStyle(big) { fontSize = 28 });
                    x += 96;
                    if (Act(ref x, by, ">", "", betChoice + 5 <= max)) betChoice += 5;
                    if (Act(ref x, by, "MAX", "", max >= MinBet)) betChoice = Mathf.Max(MinBet, max);
                    x += 30;
                    bool can = prog.Gold.Value >= betChoice && betChoice >= MinBet;
                    if (Act(ref x, by, "SPACE", can ? (seenPhase == Phase.Done ? "DEAL AGAIN" : "DEAL") : "NOT ENOUGH GOLD", can, wide: true)) DealRpc(no, betChoice);
                    Hint(by, "Wheel or arrows change the bet (Shift: 25)   ·   Win " + CutOff + " in one visit and the dealer stops dealing to you until the next floor");
                    break;
                }
                case Phase.Insurance:
                    GUI.Label(new Rect(x, by, 560, 40), "The dealer shows an Ace. Insure for " + (seenBets.Length > 0 ? seenBets[0] / 2 : 0) + "?", label);
                    x += 560;
                    if (Act(ref x, by, "Y", "TAKE INSURANCE", true, wide: true)) InsuranceRpc(no, true);
                    if (Act(ref x, by, "N", "NO INSURANCE", true, wide: true)) InsuranceRpc(no, false);
                    break;
                case Phase.Playing:
                {
                    var hand = seenActive < seenHands.Length ? seenHands[seenActive] : new byte[0];
                    int bet = seenActive < seenBets.Length ? seenBets[seenActive] : 0;
                    if (Act(ref x, by, "SPACE", "HIT", true, wide: true)) HitRpc(no);
                    if (Act(ref x, by, "S", "STAND", true, wide: true)) StandRpc(no);
                    if (Act(ref x, by, "D", "DOUBLE DOWN", CanDouble(hand.Length, false) && prog.Gold.Value >= bet, wide: true)) DoubleRpc(no);
                    if (Act(ref x, by, "P", "SPLIT", CanSplit(hand, seenHands.Length) && prog.Gold.Value >= bet, wide: true)) SplitRpc(no);
                    if (Act(ref x, by, "R", "SURRENDER", CanSurrender(hand.Length, seenHands.Length), wide: true)) SurrenderRpc(no);
                    Hint(by, (seenHands.Length > 1 ? "Playing hand " + (seenActive + 1) + " of " + seenHands.Length + "   ·   " : "") + "Double: one more card for twice the bet   ·   Split a pair into two hands   ·   Surrender: half back");
                    break;
                }
            }
            GUI.Label(new Rect(w - 260, by, 236, 40), "ESC  leave the table", new GUIStyle(label) { alignment = TextAnchor.MiddleRight, fontSize = 14, normal = { textColor = new Color(0.75f, 0.72f, 0.68f) } });
        }

        private static string TotalText(IList<byte> cards, int total, bool soft) =>
            total > 21 ? total + "  BUST" : Natural(cards) ? "BLACKJACK" : (soft && total < 21 ? "soft " : "") + total;

        private static void Pill(Camera cam, Vector3 world, string text, Color color, bool strong = false)
        {
            var p = UI.UiScale.World(cam, world);
            if (p.z <= 0f) return;
            float tw = pill.CalcSize(new GUIContent(text)).x + 24f;
            var r = new Rect(p.x - tw / 2f, UI.UiScale.H - p.y - 15f, tw, 30f);
            UI.UiSkin.Rect(r, new Color(0f, 0f, 0f, strong ? 0.8f : 0.62f));
            if (strong) UI.UiSkin.Rect(new Rect(r.x, r.yMax - 2, r.width, 2), color);
            GUI.Label(r, text, new GUIStyle(pill) { normal = { textColor = color } });
        }

        /// <summary>A key cap and its action; clickable too. Advances <paramref name="x"/>.</summary>
        private static bool Act(ref float x, float y, string key, string text, bool enabled, bool wide = false)
        {
            float kw = Mathf.Max(30f, keyCap.CalcSize(new GUIContent(key)).x + 14f);
            float tw = string.IsNullOrEmpty(text) ? 0f : label.CalcSize(new GUIContent(text)).x + 10f;
            var whole = new Rect(x, y, kw + 8f + tw, 40f);
            bool hover = enabled && whole.Contains(Event.current.mousePosition);
            UI.UiSkin.Rect(new Rect(x, y + 6, kw, 28), enabled ? (hover ? new Color(1f, 0.9f, 0.6f) : new Color(0.88f, 0.82f, 0.7f)) : new Color(0.4f, 0.38f, 0.35f));
            GUI.Label(new Rect(x, y + 6, kw, 28), key, keyCap);
            if (tw > 0f) GUI.Label(new Rect(x + kw + 8f, y, tw, 40f), text, new GUIStyle(label) { normal = { textColor = enabled ? (hover ? UI.UiSkin.Gold : new Color(0.92f, 0.9f, 0.85f)) : new Color(0.5f, 0.48f, 0.45f) } });
            x += whole.width + (wide ? 26f : 12f);
            return enabled && GUI.Button(whole, GUIContent.none, GUIStyle.none);
        }

        private static void Hint(float by, string text) =>
            GUI.Label(new Rect(40, by + 46, UI.UiScale.W - 340, 20), text, new GUIStyle(UI.UiSkin.Small) { wordWrap = false });

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) Shuffle(announce: false);
            // A half-round casino table (2026-11-07): green felt, a padded rail, four betting circles, four stools, the shoe and
            // the chip rack at the dealer's straight edge. The players' side is local -Z; the dealer stands at +Z.
            float fy = BlackjackView.FeltY;
            var felt = MeshKit.Part(transform, "Felt", MeshKit.Lathe("bjFelt", new[] { new Vector2(1.26f, 0f), new Vector2(0f, 0.0001f) }, 24, true, 180f), Mat.Cloth,
                new Vector3(0f, fy, 0f), new Vector3(0f, 90f, 0f));
            ArtLibrary.Tint(felt.GetComponent<Renderer>(), new Color(0.06f, 0.26f, 0.13f));
            var rail = MeshKit.Part(transform, "Rail", MeshKit.Lathe("bjRail", new[] { new Vector2(1.44f, -0.07f), new Vector2(1.45f, 0.03f), new Vector2(1.36f, 0.075f), new Vector2(1.26f, 0.0f) }, 24, false, 180f), Mat.Leather,
                new Vector3(0f, fy, 0f), new Vector3(0f, 90f, 0f));
            ArtLibrary.Tint(rail.GetComponent<Renderer>(), new Color(0.22f, 0.1f, 0.07f));
            MeshKit.Part(transform, "Skirt", MeshKit.Lathe("bjSkirt", new[] { new Vector2(1.4f, -0.07f), new Vector2(1.3f, -0.2f) }, 24, false, 180f), Mat.Wood, new Vector3(0f, fy, 0f), new Vector3(0f, 90f, 0f));
            MeshKit.Part(transform, "Edge", MeshKit.Box(new Vector3(2.9f, 0.09f, 0.12f)), Mat.Wood, new Vector3(0f, fy - 0.02f, 0.06f));
            MeshKit.Part(transform, "Pedestal", MeshKit.Cylinder(0.32f, 0.22f, 0.86f, 10), Mat.Wood, new Vector3(0f, 0f, -0.45f));
            MeshKit.Part(transform, "Foot", MeshKit.Cylinder(0.55f, 0.5f, 0.06f, 12), Mat.Wood, new Vector3(0f, 0f, -0.45f));
            foreach (float x in new[] { -1.15f, 1.15f }) MeshKit.Part(transform, "Leg", MeshKit.Box(new Vector3(0.08f, fy, 0.08f)), Mat.Wood, new Vector3(x, fy / 2f, 0.08f));
            // Betting circles and stools.
            for (int st = 0; st < Stools; st++)
            {
                var d = BlackjackView.StoolDir(st);
                var ring = MeshKit.Part(transform, "BetCircle", MeshKit.Lathe("bjRing", new[] { new Vector2(0.075f, 0f), new Vector2(0.064f, 0f) }, 18), Mat.Gold, d * BlackjackView.BetRadius + Vector3.up * (fy + 0.0015f));
                ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var sp = d * BlackjackView.StoolRadius;
                MeshKit.Part(transform, "Stool", MeshKit.Cylinder(0.2f, 0.19f, 0.08f, 12), Mat.Leather, sp + Vector3.up * 0.66f);
                MeshKit.Part(transform, "StoolLeg", MeshKit.Cylinder(0.04f, 0.05f, 0.66f, 6), Mat.Steel, sp);
                MeshKit.Part(transform, "StoolFoot", MeshKit.Cylinder(0.2f, 0.2f, 0.03f, 10), Mat.Steel, sp);
            }
            // The dealer's side: chip rack and shoe.
            var colours = new[] { new Color(0.08f, 0.08f, 0.09f), new Color(0.12f, 0.45f, 0.2f), new Color(0.62f, 0.1f, 0.1f), new Color(0.85f, 0.82f, 0.75f), new Color(0.15f, 0.2f, 0.55f) };
            MeshKit.Part(transform, "Rack", MeshKit.Box(new Vector3(0.62f, 0.03f, 0.13f)), Mat.Wood, new Vector3(-0.1f, fy + 0.012f, -0.06f));
            for (int c = 0; c < colours.Length; c++)
            {
                var stack = MeshKit.Part(transform, "RackChips", MeshKit.Cylinder(0.024f, 0.024f, 0.045f, 10), Mat.Bone, new Vector3(-0.36f + c * 0.13f, fy + 0.03f, -0.06f), new Vector3(90f, 0f, 0f));
                ArtLibrary.Tint(stack.GetComponent<Renderer>(), colours[c]);
            }
            MeshKit.Part(transform, "Shoe", MeshKit.Box(new Vector3(0.12f, 0.1f, 0.24f)), Mat.Wood, BlackjackView.ShoeLocal + new Vector3(0f, -0.03f, 0.04f), new Vector3(-12f, -20f, 0f));
            dealerRig = CharacterArt.BuildSkeleton(transform, CharacterArt.SkeletonKind.Warrior);
            dealerRig.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            dealerRig.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // A dealer, not a soldier: no weapon, no shield.
            foreach (var part in dealerRig.GetComponentsInChildren<Transform>(true))
                if (part != null && (part.name == "Grip" || part.name.Contains("Shield"))) Destroy(part.gameObject);
            Props.Candles(transform, transform.TransformPoint(new Vector3(-1.2f, fy, 0.25f)), new Color(1f, 0.6f, 0.3f));
            // A lamp over the felt, so the cards read in the tent's gloom.
            var lamp = new GameObject("TableLamp").AddComponent<Light>();
            lamp.transform.SetParent(transform, false);
            lamp.transform.localPosition = new Vector3(0f, 2.3f, -0.35f);
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.82f, 0.6f);
            lamp.range = 4.5f;
            lamp.intensity = 2.2f;
            lamp.shadows = LightShadows.None;
            MeshKit.Part(transform, "LampShade", MeshKit.Lathe("bjShade", new[] { new Vector2(0.28f, 0f), new Vector2(0.12f, 0.16f), new Vector2(0.03f, 0.2f) }, 12), Mat.Steel, new Vector3(0f, 2.35f, -0.35f));
            var bulb = MeshKit.Part(transform, "LampGlow", MeshKit.Ball(0.07f, 8, 6), Mat.Glow, new Vector3(0f, 2.33f, -0.35f));
            ArtLibrary.SetGlow(bulb.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.45f) * 1.4f);
            MeshKit.Part(transform, "LampChain", MeshKit.Box(new Vector3(0.015f, 0.6f, 0.015f)), Mat.Steel, new Vector3(0f, 2.85f, -0.35f));
            view = gameObject.AddComponent<BlackjackView>();
            view.Init(this);
        }
    }
}
