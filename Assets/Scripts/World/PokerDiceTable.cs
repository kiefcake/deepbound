using System;
using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.World
{
    /// <summary>The whole table, synced as one value (memcpy). Dice: 3 bits each, seat * 5 + die.</summary>
    public struct PokerState : INetworkSerializeByMemcpy, IEquatable<PokerState>
    {
        public ulong Seat0, Seat1, Seat2, Seat3;
        public ulong Dice;
        public double Deadline;
        public int Pot, Ante, Round, Held;
        public byte Phase, InMask, DoneMask, FoldMask, MatchMask, Turn, Bettor, WinMask, Dealer;

        public ulong SeatOf(int k) => k switch { 0 => Seat0, 1 => Seat1, 2 => Seat2, _ => Seat3 };
        public void SetSeat(int k, ulong id) { if (k == 0) Seat0 = id; else if (k == 1) Seat1 = id; else if (k == 2) Seat2 = id; else Seat3 = id; }
        public int Die(int seat, int i) => (int)((Dice >> ((seat * 5 + i) * 3)) & 7);
        public void SetDie(int seat, int i, int v) { int s = (seat * 5 + i) * 3; Dice = (Dice & ~(7UL << s)) | ((ulong)(v & 7) << s); }
        public int HeldOf(int seat) => (Held >> (seat * 5)) & 31;
        public void SetHeld(int seat, int mask) { Held = (Held & ~(31 << (seat * 5))) | ((mask & 31) << (seat * 5)); }
        public bool Equals(PokerState o) => Seat0 == o.Seat0 && Seat1 == o.Seat1 && Seat2 == o.Seat2 && Seat3 == o.Seat3 && Dice == o.Dice
            && Deadline == o.Deadline && Pot == o.Pot && Ante == o.Ante && Round == o.Round && Held == o.Held && Phase == o.Phase && InMask == o.InMask
            && DoneMask == o.DoneMask && FoldMask == o.FoldMask && MatchMask == o.MatchMask && Turn == o.Turn && Bettor == o.Bettor && WinMask == o.WinMask && Dealer == o.Dealer;
    }

    /// <summary>
    /// SKULL POKER, the players' game in the gambling tent (2026-11-08, the user: "make a multiplayer game where players
    /// gamble against each other"). Poker dice for two to four players, no house: every coin on the table belongs to
    /// someone sitting at it.
    /// <list type="number">
    /// <item>Take a stool (F). Anyone seated picks the ANTE (10 / 25 / 50 / 100) and deals (Space): everyone seated who
    /// can pay antes into the pot and rolls five bone dice under their own cup. Only you see yours.</item>
    /// <item>HOLD what you like (1-5 or click) and REROLL the rest once (R), or STAND (S). The others see how many dice you
    /// kept - a tell, or a bluff.</item>
    /// <item>One round of betting, in turn from the dealer's left: CHECK or BET one ante; once someone has bet, the rest
    /// CALL or FOLD. Folding gives up your share; if everyone else folds, the bettor takes the pot unseen.</item>
    /// <item>SHOWDOWN: the cups lift. Five of a kind, four of a kind, full house, straight, three of a kind, two pair, a pair,
    /// then high dice. The best hand takes the pot (a tie splits it).</item>
    /// </list>
    /// Walk off (or F) to leave; leaving mid-hand folds. Turns time out (hold: stand; betting: check, or fold to a bet).
    /// <para>OLD WICK (2026-11-08, the user's list: "NPC delvers / a poker bot"): a delver back from the deep pulls up a
    /// stool when exactly one player sits alone (<see cref="Wick"/> in a seat). He plays honestly and simply - holds his
    /// pairs, bets strong hands, calls fair ones, bluffs now and then - with a purse of real house gold,
    /// <see cref="WickPurse"/> a camp visit: he leaves when it is gone ("cleaned out") or when a second player sits down,
    /// since the table is meant for players. What he wins leaves the economy.</para>
    /// </summary>
    public class PokerDiceTable : Interactable
    {
        public const ulong NoOne = ulong.MaxValue;
        /// <summary>Old Wick's seat id (never a real client id).</summary>
        public const ulong Wick = ulong.MaxValue - 1;
        public const string WickName = "Old Wick";
        public const int WickPurse = 150;
        /// <summary>Tests: let Wick sit even in test mode.</summary>
        public static bool DebugWick;
        public readonly NetworkVariable<int> WickGold = new NetworkVariable<int>(WickPurse);
        public const int Seats = 4, DiceCount = 5;
        public enum Phase : byte { Idle, Rolling, Betting, Showdown }
        public static readonly int[] Antes = { 10, 25, 50, 100 };
        public const float HoldSeconds = 20f, TurnSeconds = 15f, ShowSeconds = 8f, LeaveDistance = 3.4f;
        public const float FeltY = 0.9f, StoolRadius = 1.45f, DiceRadius = 0.5f, DieSize = 0.075f;

        public readonly NetworkVariable<PokerState> State = new NetworkVariable<PokerState>(new PokerState
        {
            Seat0 = NoOne, Seat1 = NoOne, Seat2 = NoOne, Seat3 = NoOne, Bettor = 255, Turn = 255, Ante = 25,
        });

        public static bool LocalSeated => localTable != null;
        /// <summary>Tests: players stay seated wherever they stand.</summary>
        public static bool DebugNoDistance;
        private static PokerDiceTable localTable;
        public override float Range => 2.6f;

        private static double Now => NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;

        /// <summary>Stool k's direction from the table centre (local).</summary>
        public static Vector3 SeatDir(int k)
        {
            float a = (45f + 90f * k) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        public int SeatOfClient(ulong client)
        {
            var s = State.Value;
            for (int k = 0; k < Seats; k++) if (s.SeatOf(k) == client) return k;
            return -1;
        }

        public override string Prompt(PlayerProgress who)
        {
            if (localTable == this) return null;
            int free = 0;
            for (int k = 0; k < Seats; k++) if (State.Value.SeatOf(k) == NoOne) free++;
            return free == 0 ? null : "Sit at Skull Poker  -  dice against the other players  (" + (Seats - free) + " seated)";
        }

        // ================= Server =================

        protected override void ServerUse(PlayerProgress who) => ServerSit(who);

        public bool ServerSit(PlayerProgress who)
        {
            var s = State.Value;
            if (SeatOfClient(who.OwnerClientId) >= 0) return false;
            int best = -1; float bestD = float.MaxValue;
            for (int k = 0; k < Seats; k++)
            {
                if (s.SeatOf(k) != NoOne) continue;
                float d = Vector3.Distance(who.transform.position, transform.TransformPoint(SeatDir(k) * StoolRadius));
                if (d < bestD) { bestD = d; best = k; }
            }
            if (best < 0) return false;
            s.SetSeat(best, who.OwnerClientId);
            State.Value = s;
            TableFeedRpc(who.name + " sits down at Skull Poker.", false);
            return true;
        }

        [Rpc(SendTo.Server)]
        public void LeaveRpc(NetworkObjectReference player)
        {
            if (player.TryGet(out var obj)) ServerLeave(obj.OwnerClientId);
        }

        public void ServerLeave(ulong client)
        {
            int k = SeatOfClient(client);
            if (k < 0) return;
            var s = State.Value;
            byte bit = (byte)(1 << k);
            if ((s.InMask & bit) != 0 && (s.Phase == (byte)Phase.Rolling || s.Phase == (byte)Phase.Betting)) { s.FoldMask |= bit; s.DoneMask |= bit; }
            s.SetSeat(k, NoOne);
            State.Value = s;
            ServerAdvance();
        }

        [Rpc(SendTo.Server)]
        public void DealRpc(NetworkObjectReference player, int ante)
        {
            if (!player.TryGet(out var obj) || SeatOfClient(obj.OwnerClientId) < 0) return;
            ServerDeal(SeatOfClient(obj.OwnerClientId), ante);
        }

        /// <summary>Server: everyone seated who can pay antes in and rolls. False when fewer than two can play.</summary>
        public bool ServerDeal(int dealer, int ante)
        {
            var s = State.Value;
            if (s.Phase != (byte)Phase.Idle && s.Phase != (byte)Phase.Showdown) return false;
            ante = Array.IndexOf(Antes, ante) >= 0 ? ante : Antes[1];
            var players = new List<(int seat, ulong id)>();
            for (int k = 0; k < Seats; k++)
            {
                ulong id = s.SeatOf(k);
                if (id != NoOne && CanPay(id, ante)) players.Add((k, id));
            }
            if (players.Count < 2) { TableFeedRpc("Skull Poker needs two players who can pay the " + ante + " gold ante.", true); return false; }
            s.Ante = ante; s.Pot = 0; s.InMask = 0; s.DoneMask = 0; s.FoldMask = 0; s.MatchMask = 0; s.WinMask = 0; s.Held = 0;
            s.Bettor = 255; s.Turn = 255; s.Dealer = (byte)dealer; s.Round++;
            foreach (var (seat, id) in players)
            {
                if (!Spend(id, ante)) continue;
                s.Pot += ante;
                s.InMask |= (byte)(1 << seat);
                for (int i = 0; i < DiceCount; i++) s.SetDie(seat, i, UnityEngine.Random.Range(1, 7));
            }
            s.Phase = (byte)Phase.Rolling;
            s.Deadline = Now + HoldSeconds;
            State.Value = s;
            TableFeedRpc("Skull Poker: " + players.Count + " players ante " + ante + ". Pot " + s.Pot + ".", false);
            return true;
        }

        [Rpc(SendTo.Server)]
        public void RerollRpc(NetworkObjectReference player, byte holdMask)
        {
            if (player.TryGet(out var obj)) ServerReroll(SeatOfClient(obj.OwnerClientId), holdMask);
        }

        /// <summary>Server: reroll every die not held (all held = stand).</summary>
        public void ServerReroll(int seat, int holdMask)
        {
            var s = State.Value;
            if (seat < 0 || s.Phase != (byte)Phase.Rolling) return;
            byte bit = (byte)(1 << seat);
            if ((s.InMask & bit) == 0 || (s.DoneMask & bit) != 0) return;
            for (int i = 0; i < DiceCount; i++)
                if ((holdMask & (1 << i)) == 0) s.SetDie(seat, i, UnityEngine.Random.Range(1, 7));
            s.SetHeld(seat, holdMask);
            s.DoneMask |= bit;
            State.Value = s;
            if ((holdMask & 31) != 31) RattleRpc((byte)seat, (byte)(DiceCount - Popcount(holdMask & 31)));
            ServerAdvance();
        }

        public enum Act : byte { Check, Bet, Call, Fold }

        [Rpc(SendTo.Server)]
        public void ActRpc(NetworkObjectReference player, byte act)
        {
            if (player.TryGet(out var obj)) ServerAct(SeatOfClient(obj.OwnerClientId), (Act)act);
        }

        public void ServerAct(int seat, Act act)
        {
            var s = State.Value;
            if (seat < 0 || s.Phase != (byte)Phase.Betting || s.Turn != seat) return;
            byte bit = (byte)(1 << seat);
            ulong actor = s.SeatOf(seat);
            string name = NameOf(actor);
            switch (act)
            {
                case Act.Check:
                    if (s.Bettor != 255) return;
                    TableFeedRpc(name + " checks.", false);
                    break;
                case Act.Bet:
                    if (s.Bettor != 255 || !Spend(actor, s.Ante)) return;
                    s.Pot += s.Ante; s.Bettor = (byte)seat; s.MatchMask = bit;
                    TableFeedRpc(name + " BETS " + s.Ante + ". Pot " + s.Pot + ".", false);
                    ChipsRpc((byte)seat);
                    break;
                case Act.Call:
                    if (s.Bettor == 255 || !Spend(actor, s.Ante)) return;
                    s.Pot += s.Ante; s.MatchMask |= bit;
                    TableFeedRpc(name + " calls. Pot " + s.Pot + ".", false);
                    ChipsRpc((byte)seat);
                    break;
                case Act.Fold:
                    s.FoldMask |= bit;
                    TableFeedRpc(name + " folds.", false);
                    break;
            }
            s.DoneMask |= bit;
            State.Value = s;
            ServerAdvance();
        }

        /// <summary>Server: move the hand on after any change (all rolled -> betting; next turn; showdown).</summary>
        private void ServerAdvance()
        {
            var s = State.Value;
            byte alive = (byte)(s.InMask & ~s.FoldMask);
            if ((s.Phase == (byte)Phase.Rolling || s.Phase == (byte)Phase.Betting) && Popcount(alive) <= 1)
            {
                ServerShowdown(alive, unseen: true);
                return;
            }
            if (s.Phase == (byte)Phase.Rolling)
            {
                if ((s.DoneMask & alive) != alive) return;
                s.Phase = (byte)Phase.Betting;
                s.DoneMask = 0; s.MatchMask = 0; s.Bettor = 255;
                s.Turn = (byte)NextSeat(s, s.Dealer, alive, 0);
                s.Deadline = Now + TurnSeconds;
                State.Value = s;
                return;
            }
            if (s.Phase == (byte)Phase.Betting)
            {
                // Still owed: with no bet, everyone alive must act; after a bet, everyone alive must have matched it.
                byte owed = s.Bettor == 255 ? (byte)(alive & ~s.DoneMask) : (byte)(alive & ~s.MatchMask);
                if (owed == 0) { ServerShowdown(alive, unseen: false); return; }
                s.Turn = (byte)NextSeat(s, s.Turn == 255 ? s.Dealer : s.Turn, owed, 0);
                s.Deadline = Now + TurnSeconds;
                State.Value = s;
            }
        }

        private static int NextSeat(PokerState s, int from, byte among, int _)
        {
            for (int i = 1; i <= Seats; i++)
            {
                int k = (from + i) % Seats;
                if ((among & (1 << k)) != 0) return k;
            }
            return from;
        }

        private void ServerShowdown(byte alive, bool unseen)
        {
            var s = State.Value;
            int best = -1;
            byte win = 0;
            for (int k = 0; k < Seats; k++)
            {
                if ((alive & (1 << k)) == 0) continue;
                int score = Score(Hand(s, k));
                if (score > best) { best = score; win = (byte)(1 << k); }
                else if (score == best) win |= (byte)(1 << k);
            }
            int winners = Popcount(win);
            var names = new List<string>();
            if (winners > 0)
            {
                int share = s.Pot / winners, extra = s.Pot - share * winners;
                for (int k = 0; k < Seats; k++)
                {
                    if ((win & (1 << k)) == 0) continue;
                    ulong id = s.SeatOf(k);
                    if (id != NoOne) { Pay(id, share + extra); names.Add(NameOf(id)); }
                    extra = 0;
                }
            }
            s.WinMask = win;
            s.Phase = (byte)Phase.Showdown;
            s.Turn = 255;
            s.Deadline = Now + ShowSeconds;
            if (unseen) s.InMask = 0; // nothing to show: the others folded, the winner keeps their cup down
            State.Value = s;
            int handOf = -1;
            for (int k = 0; k < Seats; k++) if ((win & (1 << k)) != 0) { handOf = k; break; }
            string hand = handOf >= 0 && !unseen ? HandName(Hand(s, handOf)) : null;
            ShowdownRpc(string.Join(" and ", names), s.Pot, hand ?? "", unseen);
        }

        private void Update()
        {
            if (IsServer && IsSpawned) ServerTick();
            if (IsClient) LocalTick();
        }

        private float nextSeatCheck;

        private void ServerTick()
        {
            var s = State.Value;
            if (Time.time >= nextSeatCheck)
            {
                nextSeatCheck = Time.time + 0.5f;
                for (int k = 0; k < Seats; k++)
                {
                    ulong id = s.SeatOf(k);
                    if (id == NoOne || id == Wick) continue;
                    var p = ProgressOf(id);
                    if (p == null || (!DebugNoDistance && !DungeonDirector.InLobby) || (!DebugNoDistance && Flat(p.transform.position - transform.position) > LeaveDistance)) { ServerLeave(id); s = State.Value; }
                }
            }
            TickWick();
            s = State.Value;
            if (Now < s.Deadline) return;
            switch ((Phase)s.Phase)
            {
                case Phase.Rolling:
                    // Time: everyone who has not chosen stands.
                    for (int k = 0; k < Seats; k++)
                        if ((s.InMask & (1 << k)) != 0 && (s.DoneMask & (1 << k)) == 0 && (s.FoldMask & (1 << k)) == 0) ServerReroll(k, 31);
                    break;
                case Phase.Betting:
                    if (s.Turn < Seats) ServerAct(s.Turn, s.Bettor == 255 ? Act.Check : Act.Fold);
                    break;
                case Phase.Showdown:
                    s.Phase = (byte)Phase.Idle;
                    s.Pot = 0;
                    State.Value = s;
                    break;
            }
        }

        // ================= Old Wick =================

        private int wickVisit = -1;
        private float wickSitAt = -1f, wickActAt = -1f;
        private byte wickPhase = 255, wickTurn = 255;
        private int wickRound = -1;
        private static int Visit => DungeonDirector.Instance != null ? DungeonDirector.Instance.NextDepth.Value * 1000 + (int)(DungeonDirector.Instance.RunStartedAt.Value % 997) : 0;

        public static string NameOf(ulong id) => id == Wick ? WickName : ProgressOf(id) is PlayerProgress p ? p.name : "?";
        private bool CanPay(ulong id, int g) => id == Wick ? WickGold.Value >= g : ProgressOf(id) is PlayerProgress p && p.Gold.Value >= g;
        private bool Spend(ulong id, int g)
        {
            if (id != Wick) return ProgressOf(id) is PlayerProgress p && p.ServerSpendGold(g);
            if (WickGold.Value < g) return false;
            WickGold.Value -= g;
            return true;
        }
        private void Pay(ulong id, int g)
        {
            if (id == Wick) WickGold.Value += g;
            else ProgressOf(id)?.ServerAddGold(g);
        }

        public int WickSeat => SeatOfClient(Wick);

        /// <summary>Server: Wick sits with a lone player, plays his hand, and leaves when outnumbered or broke.</summary>
        private void TickWick()
        {
            if (DungeonDirector.TestMode && !DebugWick) return;
            if (wickVisit != Visit) { wickVisit = Visit; WickGold.Value = WickPurse; }
            var s = State.Value;
            int humans = 0;
            for (int k = 0; k < Seats; k++) { ulong id = s.SeatOf(k); if (id != NoOne && id != Wick) humans++; }
            int seat = WickSeat;
            bool idle = s.Phase == (byte)Phase.Idle || s.Phase == (byte)Phase.Showdown;
            if (seat < 0)
            {
                if (humans != 1 || !idle || WickGold.Value < Antes[0]) { wickSitAt = -1f; return; }
                if (wickSitAt < 0f) wickSitAt = Time.time + (DebugWick ? 0f : 4f);
                if (Time.time < wickSitAt) return;
                for (int k = 0; k < Seats; k++)
                    if (s.SeatOf(k) == NoOne) { s.SetSeat(k, Wick); State.Value = s; TableFeedRpc(WickName + ", a delver back from the deep, pulls up a stool. \"One hand. Maybe two. I've coin to lose.\"", false); break; }
                return;
            }
            if (idle && (humans != 1 || WickGold.Value < Mathf.Max(Antes[0], s.Ante)))
            {
                bool broke = WickGold.Value < Mathf.Max(Antes[0], s.Ante);
                s.SetSeat(seat, NoOne);
                State.Value = s;
                TableFeedRpc(broke ? WickName + " turns out his purse. \"Cleaned out. You'll drink on me, then.\" He leaves the table." : WickName + " gets up. \"Your game now. I'll watch.\"", false);
                return;
            }
            // Give him a moment to think before each move.
            if (s.Phase != wickPhase || s.Turn != wickTurn || s.Round != wickRound)
            {
                wickPhase = s.Phase; wickTurn = s.Turn; wickRound = s.Round;
                wickActAt = Time.time + (DebugWick ? 0f : UnityEngine.Random.Range(1.2f, 2.6f));
            }
            if (Time.time < wickActAt) return;
            byte bit = (byte)(1 << seat);
            if (s.Phase == (byte)Phase.Rolling && (s.InMask & bit) != 0 && (s.DoneMask & bit) == 0 && (s.FoldMask & bit) == 0)
                ServerReroll(seat, WickHold(Hand(s, seat)));
            else if (s.Phase == (byte)Phase.Betting && s.Turn == seat)
                ServerAct(seat, WickChoice(Score(Hand(s, seat)) / 1000000, s.Bettor != 255, WickGold.Value >= s.Ante));
        }

        /// <summary>Wick keeps every die in a pair or better, a straight whole, else his best single die.</summary>
        public static int WickHold(int[] dice)
        {
            var count = new int[7];
            foreach (var d in dice) count[d]++;
            if (Score(dice) / 1000000 == 4) return 31;
            int mask = 0;
            for (int i = 0; i < dice.Length; i++) if (count[dice[i]] >= 2) mask |= 1 << i;
            if (mask == 0)
            {
                int hi = 0;
                for (int i = 1; i < dice.Length; i++) if (dice[i] > dice[hi]) hi = i;
                mask = 1 << hi;
            }
            return mask;
        }

        /// <summary>Wick's bet: two pair or better bets/calls; a pair sometimes bets, calls a bet; nothing checks, folds to a bet, and bluffs now and then.</summary>
        public static Act WickChoice(int category, bool facingBet, bool canPay)
        {
            float r = UnityEngine.Random.value;
            if (!facingBet)
                return canPay && (category >= 2 || (category == 1 && r < 0.3f) || (category == 0 && r < 0.1f)) ? Act.Bet : Act.Check;
            return canPay && (category >= 1 || r < 0.2f) ? Act.Call : Act.Fold;
        }

        private static PlayerProgress ProgressOf(ulong client)
        {
            if (client == NoOne) return null;
            foreach (var p in PlayerLife.All)
                if (p != null && p.OwnerClientId == client) return p.GetComponent<PlayerProgress>();
            return null;
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
        private static int Popcount(int m) { int c = 0; while (m != 0) { c += m & 1; m >>= 1; } return c; }

        // ================= Hands =================

        public static int[] Hand(PokerState s, int seat)
        {
            var h = new int[DiceCount];
            for (int i = 0; i < DiceCount; i++) h[i] = s.Die(seat, i);
            return h;
        }

        /// <summary>A comparable score: category x 10^6 + the faces that break ties (most-of first, then highest).</summary>
        public static int Score(int[] dice)
        {
            var count = new int[7];
            foreach (var d in dice) if (d >= 1 && d <= 6) count[d]++;
            var groups = new List<(int n, int face)>();
            for (int f = 6; f >= 1; f--) if (count[f] > 0) groups.Add((count[f], f));
            groups.Sort((a, b) => a.n != b.n ? b.n.CompareTo(a.n) : b.face.CompareTo(a.face));
            int cat;
            bool straight = groups.Count == 5 && (count[1] == 0 || count[6] == 0);
            if (groups[0].n == 5) cat = 7;
            else if (groups[0].n == 4) cat = 6;
            else if (groups[0].n == 3 && groups[1].n == 2) cat = 5;
            else if (straight) cat = 4;
            else if (groups[0].n == 3) cat = 3;
            else if (groups[0].n == 2 && groups[1].n == 2) cat = 2;
            else if (groups[0].n == 2) cat = 1;
            else cat = 0;
            int tie = 0;
            foreach (var g in groups) tie = tie * 7 + g.face;
            for (int i = groups.Count; i < 5; i++) tie *= 7;
            return cat * 1000000 + tie;
        }

        public static string HandName(int[] dice)
        {
            int cat = Score(dice) / 1000000;
            return cat switch
            {
                7 => "FIVE OF A KIND", 6 => "four of a kind", 5 => "a full house", 4 => (Array.IndexOf(dice, 6) >= 0 ? "a high straight" : "a low straight"),
                3 => "three of a kind", 2 => "two pair", 1 => "a pair", _ => "nothing",
            };
        }

        // ================= Everyone =================

        [Rpc(SendTo.ClientsAndHost)]
        private void TableFeedRpc(string text, bool warn)
        {
            var me = NetworkPlayer.Local;
            bool near = me != null && Vector3.Distance(me.transform.position, transform.position) < 12f;
            if (near) UI.GameHud.Feed(text, warn ? new Color(1f, 0.6f, 0.45f) : new Color(0.85f, 0.8f, 1f));
            log.Add(text);
            if (log.Count > 5) log.RemoveAt(0);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RattleRpc(byte seat, byte count)
        {
            Sfx.Play(Sound.Dice, transform.TransformPoint(SeatDir(seat) * DiceRadius) + Vector3.up, 0.8f, 1f);
            rattleUntil[seat] = Time.time + 0.6f;
            UI.GameHud.ShowCombo(transform.TransformPoint(SeatDir(seat) * DiceRadius) + Vector3.up * 1.3f, "rerolls " + count, new Color(0.85f, 0.8f, 1f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChipsRpc(byte seat) => Sfx.Play(Sound.Chips, transform.TransformPoint(SeatDir(seat) * DiceRadius) + Vector3.up, 0.9f);

        [Rpc(SendTo.ClientsAndHost)]
        private void ShowdownRpc(string winners, int pot, string hand, bool unseen)
        {
            var color = new Color(1f, 0.85f, 0.35f);
            string text = winners.Length == 0 ? "Skull Poker: nobody takes the pot." : unseen ? "Skull Poker: " + winners + " takes the pot of " + pot + " - everyone else folded."
                : "Skull Poker: " + winners + " win" + (winners.Contains(" and ") ? "" : "s") + " " + pot + " with " + hand + "!";
            var me = NetworkPlayer.Local;
            if (me != null && Vector3.Distance(me.transform.position, transform.position) < 14f) UI.GameHud.Feed(text, color);
            Sfx.Play(Sound.Coins, transform.position + Vector3.up, 1f, 1f);
            Vfx.Embers(transform.position + Vector3.up * 1.1f, 18, color, 3f, 0.05f, 0.8f);
            log.Add(text);
            if (log.Count > 5) log.RemoveAt(0);
        }

        // ================= Local: the stool, the dice, the panel =================

        private readonly Transform[,] dice = new Transform[Seats, DiceCount];
        private readonly Transform[] cups = new Transform[Seats];
        private readonly float[] rattleUntil = new float[Seats];
        private Transform potPile;
        private readonly List<string> log = new List<string>();
        private PokerState seen;
        private bool seenAny;
        private int mySeat = -1, anteIndex = 1, holdMask;
        private float satAt;

        private void LocalTick()
        {
            var me = NetworkPlayer.Local;
            if (me == null) return;
            var s = State.Value;
            DrawWick(s);
            int seat = SeatOfClient(me.OwnerClientId);
            var motor = me.GetComponent<FirstPersonMotor>();
            // Sit / stand to follow the server.
            if (seat >= 0 && mySeat < 0 && motor != null)
            {
                mySeat = seat; localTable = this; satAt = Time.time; holdMask = 0;
                var at = SeatPoint(seat);
                motor.Teleport(at, Quaternion.LookRotation(transform.TransformDirection(-SeatDir(seat))));
                motor.PinTo(at);
                motor.SetPitch(38f);
                Sfx.Play2D(Sound.UiOpen, 0.6f);
            }
            else if (seat < 0 && mySeat >= 0)
            {
                if (motor != null && motor.Pinned) motor.Unpin(SeatPoint(mySeat) + transform.TransformDirection(SeatDir(mySeat)) * 0.7f);
                if (motor != null) motor.SetPitch(0f);
                mySeat = -1;
                if (localTable == this) localTable = null;
            }
            if (mySeat >= 0 && motor != null)
            {
                // Moved off the stool by something else (a teleport): leave the table.
                if (!motor.Pinned || Flat(me.transform.position - SeatPoint(mySeat)) > 0.35f) { LeaveRpc(me.NetworkObject); motor.Unpin(me.transform.position); mySeat = -1; localTable = null; }
                else motor.PinTo(SeatPoint(mySeat));
            }
            // Dice: a new roll (or a reroll) tumbles; others' stay under their cups until the showdown.
            for (int k = 0; k < Seats; k++)
            {
                bool inHand = (s.InMask & (1 << k)) != 0 && s.Phase != (byte)Phase.Idle;
                bool showFaces = inHand && (k == mySeat || s.Phase == (byte)Phase.Showdown);
                for (int i = 0; i < DiceCount; i++)
                {
                    var d = dice[k, i];
                    if (d == null) continue;
                    d.gameObject.SetActive(showFaces);
                    if (!showFaces) continue;
                    int v = s.Die(k, i);
                    bool changed = !seenAny || seen.Round != s.Round || seen.Die(k, i) != v || (seen.Phase != s.Phase && s.Phase == (byte)Phase.Showdown);
                    if (changed && v >= 1)
                    {
                        var rest = DieRest(k, i);
                        var t = d.GetComponent<DieTumble>() ?? d.gameObject.AddComponent<DieTumble>();
                        t.Throw(CupLocal(k) + Vector3.up * 0.18f, rest, v, UnityEngine.Random.Range(-20f, 20f) + (45f + 90f * k), i * 0.05f, 0.6f);
                    }
                }
                if (cups[k] != null)
                {
                    bool covered = inHand && !showFaces;
                    cups[k].gameObject.SetActive(covered);
                    if (covered) cups[k].localRotation = Quaternion.Euler(Time.time < rattleUntil[k] ? Mathf.Sin(Time.time * 60f) * 8f : 0f, 0f, 0f);
                }
            }
            if (potPile != null)
            {
                float k2 = Mathf.Clamp01(s.Pot / 400f);
                potPile.gameObject.SetActive(s.Pot > 0);
                potPile.localScale = new Vector3(0.6f + k2, 0.4f + k2 * 1.6f, 0.6f + k2);
            }
            if (seen.Round != s.Round) holdMask = 0;
            seen = s; seenAny = true;
            if (mySeat >= 0) LocalInput(me, s);
        }

        private void LocalInput(NetworkPlayer me, PokerState s)
        {
            var kb = Keyboard.current;
            if (kb == null || Time.time - satAt < 0.3f) return;
            if (kb.escapeKey.wasPressedThisFrame || UI.Binds.Down(UI.Bind.Use)) { LeaveRpc(me.NetworkObject); return; }
            var phase = (Phase)s.Phase;
            byte bit = (byte)(1 << mySeat);
            if (phase == Phase.Idle || phase == Phase.Showdown)
            {
                if (kb.leftArrowKey.wasPressedThisFrame) anteIndex = Mathf.Max(0, anteIndex - 1);
                if (kb.rightArrowKey.wasPressedThisFrame) anteIndex = Mathf.Min(Antes.Length - 1, anteIndex + 1);
                if (kb.spaceKey.wasPressedThisFrame) DealRpc(me.NetworkObject, Antes[anteIndex]);
            }
            else if (phase == Phase.Rolling && (s.InMask & bit) != 0 && (s.DoneMask & bit) == 0)
            {
                for (int i = 0; i < DiceCount; i++) if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) holdMask ^= 1 << i;
                if (kb.rKey.wasPressedThisFrame) RerollRpc(me.NetworkObject, (byte)holdMask);
                if (kb.sKey.wasPressedThisFrame) RerollRpc(me.NetworkObject, 31);
            }
            else if (phase == Phase.Betting && s.Turn == mySeat)
            {
                if (s.Bettor == 255) { if (kb.cKey.wasPressedThisFrame) ActRpc(me.NetworkObject, (byte)Act.Check); if (kb.bKey.wasPressedThisFrame) ActRpc(me.NetworkObject, (byte)Act.Bet); }
                else { if (kb.cKey.wasPressedThisFrame) ActRpc(me.NetworkObject, (byte)Act.Call); if (kb.xKey.wasPressedThisFrame) ActRpc(me.NetworkObject, (byte)Act.Fold); }
            }
        }

        private Transform wickBody;

        /// <summary>Old Wick plays standing at his place, behind his stool: a delver in dented armour (the knight rig's sit pose
        /// tipped him over on the stool).</summary>
        private void DrawWick(PokerState s)
        {
            int ws = -1;
            for (int k = 0; k < Seats; k++) if (s.SeatOf(k) == Wick) ws = k;
            if (ws < 0) { if (wickBody != null) wickBody.gameObject.SetActive(false); return; }
            if (wickBody == null)
            {
                wickBody = new GameObject("OldWick").transform;
                wickBody.SetParent(transform, false);
                CharacterArt.BuildKnight(wickBody, new Color(0.38f, 0.33f, 0.26f));
            }
            if (!wickBody.gameObject.activeSelf) wickBody.gameObject.SetActive(true);
            var dir = transform.TransformDirection(SeatDir(ws));
            wickBody.position = transform.TransformPoint(SeatDir(ws) * (StoolRadius + 0.35f));
            wickBody.rotation = Quaternion.LookRotation(-new Vector3(dir.x, 0f, dir.z));
        }

        private Vector3 SeatPoint(int k) => transform.TransformPoint(SeatDir(k) * StoolRadius) + Vector3.up * 0.1f;
        private static Vector3 CupLocal(int k) => SeatDir(k) * (DiceRadius + 0.12f) + Vector3.up * (FeltY + 0.01f);
        private static Vector3 DieRest(int k, int i)
        {
            var dir = SeatDir(k);
            var side = new Vector3(dir.z, 0f, -dir.x);
            return dir * DiceRadius + side * (i - 2) * (DieSize * 1.45f) + Vector3.up * (FeltY + DieSize * 0.5f + 0.012f);
        }

        // ---- The panel ----
        private void OnGUI()
        {
            if (mySeat < 0 || localTable != this) return;
            var me = NetworkPlayer.Local;
            var prog = me != null ? me.GetComponent<PlayerProgress>() : null;
            if (prog == null) return;
            useGUILayout = false;
            UI.UiSkin.Apply();
            var s = State.Value;
            var phase = (Phase)s.Phase;
            float W = UI.UiScale.W, H = UI.UiScale.H;
            float pw = Mathf.Min(620f, W * 0.48f), ph = 410f;
            var r = new Rect(W - pw - 30f, H - ph - Mathf.Max(130f, H * 0.16f), pw, ph);
            UI.UiSkin.Panel(r, 0.95f);
            var head = new GUIStyle(UI.UiSkin.Heading) { fontSize = 20 };
            var small = new GUIStyle(UI.UiSkin.Small) { fontSize = 13 };
            var gold = new GUIStyle(small) { normal = { textColor = UI.UiSkin.Gold } };
            GUI.Label(new Rect(r.x + 18, r.y + 8, pw - 36, 28), "SKULL POKER", head);
            GUI.Label(new Rect(r.x + 18, r.y + 10, pw - 36, 26), "POT " + s.Pot + "   ·   ANTE " + (phase == Phase.Idle ? Antes[anteIndex] : s.Ante) + "   ·   your gold " + prog.Gold.Value,
                new GUIStyle(gold) { alignment = TextAnchor.MiddleRight, fontSize = 15 });
            double left = Math.Max(0d, s.Deadline - Now);

            // The players.
            float y = r.y + 44f;
            for (int k = 0; k < Seats; k++)
            {
                ulong id = s.SeatOf(k);
                if (id == NoOne) continue;
                var p = ProgressOf(id);
                string name = NameOf(id);
                byte bit = (byte)(1 << k);
                bool inHand = (s.InMask & bit) != 0 && phase != Phase.Idle;
                string status;
                if (phase == Phase.Showdown && (s.WinMask & bit) != 0 && !inHand) status = "WINS  -  unseen";
                else if (!inHand) status = phase == Phase.Idle ? "seated" : "sitting out";
                else if ((s.FoldMask & bit) != 0) status = "folded";
                else if (phase == Phase.Rolling) status = (s.DoneMask & bit) != 0 ? "kept " + Popcount(s.HeldOf(k)) + " dice" : "choosing...";
                else if (phase == Phase.Betting) status = s.Turn == k ? "TO ACT (" + Mathf.CeilToInt((float)left) + "s)" : s.Bettor == k ? "BET" : (s.MatchMask & bit) != 0 ? "called" : (s.DoneMask & bit) != 0 ? "checked" : "waiting";
                else status = (s.WinMask & bit) != 0 ? "WINS  -  " + HandName(Hand(s, k)) : HandName(Hand(s, k));
                var rowStyle = new GUIStyle(UI.UiSkin.Body) { fontSize = 15, normal = { textColor = k == mySeat ? UI.UiSkin.Gold : new Color(0.9f, 0.88f, 0.84f) } };
                GUI.Label(new Rect(r.x + 22, y, 200, 22), (s.Dealer == k && phase != Phase.Idle ? "◆ " : "   ") + name + (k == mySeat ? " (you)" : ""), rowStyle);
                GUI.Label(new Rect(r.x + 230, y, pw - 250, 22), status + (p != null && k != mySeat ? "    " + p.Gold.Value + "g" : id == Wick ? "    " + WickGold.Value + "g" : ""), rowStyle);
                y += 24f;
            }

            // Your dice.
            y = r.y + 158f;
            byte mine = (byte)(1 << mySeat);
            bool myHand = (s.InMask & mine) != 0 && phase != Phase.Idle;
            bool choosing = phase == Phase.Rolling && myHand && (s.DoneMask & mine) == 0;
            float tile = 62f, gap = 14f, startX = r.x + (pw - (tile * 5 + gap * 4)) / 2f;
            if (myHand)
            {
                var hand = Hand(s, mySeat);
                for (int i = 0; i < DiceCount; i++)
                {
                    var tr = new Rect(startX + i * (tile + gap), y, tile, tile);
                    bool held = choosing ? (holdMask & (1 << i)) != 0 : phase == Phase.Rolling && (s.HeldOf(mySeat) & (1 << i)) != 0;
                    DiceArt.DrawHud(tr, hand[i], held);
                    if (choosing)
                    {
                        if (GUI.Button(tr, GUIContent.none, GUIStyle.none)) holdMask ^= 1 << i;
                        GUI.Label(new Rect(tr.x, tr.yMax + 4, tile, 18), (i + 1) + (held ? "  HELD" : ""), new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = held ? UI.UiSkin.Gold : new Color(0.7f, 0.68f, 0.62f) } });
                    }
                }
                GUI.Label(new Rect(r.x, y + tile + 24, pw, 24), "You hold " + HandName(hand), new GUIStyle(UI.UiSkin.Heading) { fontSize = 17, alignment = TextAnchor.MiddleCenter });
            }
            else GUI.Label(new Rect(r.x, y + 20, pw, 30), phase == Phase.Idle ? "Waiting for a deal." : "You sit this hand out.", new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontSize = 16 });

            // Actions.
            y = r.y + 270f;
            float bw = (pw - 56f) / 2f;
            var left1 = new Rect(r.x + 20, y, bw, 34);
            var right1 = new Rect(r.x + 36 + bw, y, bw, 34);
            int seated = 0;
            for (int k = 0; k < Seats; k++) if (s.SeatOf(k) != NoOne) seated++;
            if (phase == Phase.Idle || phase == Phase.Showdown)
            {
                for (int i = 0; i < Antes.Length; i++)
                {
                    var b = new Rect(r.x + 20 + i * 70, y, 62, 30);
                    if (i == anteIndex) UI.UiSkin.Rect(new Rect(b.x - 2, b.y - 2, b.width + 4, b.height + 4), UI.UiSkin.Gold);
                    if (GUI.Button(b, Antes[i].ToString(), UI.UiSkin.Button)) anteIndex = i;
                }
                GUI.enabled = seated >= 2;
                if (GUI.Button(new Rect(r.x + 310, y, pw - 330, 30), seated >= 2 ? "SPACE  Deal (ante " + Antes[anteIndex] + ")" : "Waiting for a second player", UI.UiSkin.Button)) DealRpc(me.NetworkObject, Antes[anteIndex]);
                GUI.enabled = true;
            }
            else if (choosing)
            {
                if (GUI.Button(left1, "R  Reroll " + (DiceCount - Popcount(holdMask)) + " dice", UI.UiSkin.Button)) RerollRpc(me.NetworkObject, (byte)holdMask);
                if (GUI.Button(right1, "S  Stand (keep all)", UI.UiSkin.Button)) RerollRpc(me.NetworkObject, 31);
                GUI.Label(new Rect(r.x, y + 38, pw, 20), "Click dice or press 1-5 to hold them.  " + Mathf.CeilToInt((float)left) + "s", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            }
            else if (phase == Phase.Betting && s.Turn == mySeat)
            {
                if (s.Bettor == 255)
                {
                    if (GUI.Button(left1, "C  Check", UI.UiSkin.Button)) ActRpc(me.NetworkObject, (byte)Act.Check);
                    GUI.enabled = prog.Gold.Value >= s.Ante;
                    if (GUI.Button(right1, "B  Bet " + s.Ante, UI.UiSkin.Button)) ActRpc(me.NetworkObject, (byte)Act.Bet);
                    GUI.enabled = true;
                }
                else
                {
                    GUI.enabled = prog.Gold.Value >= s.Ante;
                    if (GUI.Button(left1, "C  Call " + s.Ante, UI.UiSkin.Button)) ActRpc(me.NetworkObject, (byte)Act.Call);
                    GUI.enabled = true;
                    if (GUI.Button(right1, "X  Fold", UI.UiSkin.Button)) ActRpc(me.NetworkObject, (byte)Act.Fold);
                }
                GUI.Label(new Rect(r.x, y + 38, pw, 20), "Your turn  -  " + Mathf.CeilToInt((float)left) + "s", new GUIStyle(gold) { alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                string wait = phase == Phase.Rolling ? "Waiting for the others to choose (" + Mathf.CeilToInt((float)left) + "s)" : phase == Phase.Betting ? "Betting..." : "";
                GUI.Label(new Rect(r.x, y + 6, pw, 22), wait, new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontSize = 15 });
            }

            // The table's last words and the rules in one line.
            y = r.y + 330f;
            for (int i = Mathf.Max(0, log.Count - 2); i < log.Count; i++) { GUI.Label(new Rect(r.x + 20, y, pw - 40, 20), log[i], small); y += 20f; }
            GUI.Label(new Rect(r.x + 20, r.yMax - 24, pw - 40, 20), "5 kind > 4 kind > full house > straight > 3 kind > 2 pair > pair     F / Esc: leave", new GUIStyle(small) { fontSize = 11 });
        }

        private void OnDisable()
        {
            if (localTable == this) localTable = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // A round table, black felt with a gold ring, four stools, a skull candle and the pot bowl in the middle.
            MeshKit.Part(transform, "Leg", MeshKit.Cylinder(0.16f, 0.3f, FeltY - 0.06f, 10), Mat.Wood, Vector3.zero);
            MeshKit.Part(transform, "Top", MeshKit.Cylinder(0.92f, 0.92f, 0.07f, 20), Mat.Wood, Vector3.up * (FeltY - 0.07f));
            var felt = MeshKit.Part(transform, "Felt", MeshKit.Cylinder(0.84f, 0.84f, 0.012f, 20), Mat.Cloth, Vector3.up * FeltY);
            ArtLibrary.Tint(felt.GetComponent<Renderer>(), new Color(0.32f, 0.07f, 0.09f));
            MeshKit.Part(transform, "Ring", MeshKit.Cylinder(0.3f, 0.3f, 0.006f, 20), Mat.Gold, Vector3.up * (FeltY + 0.006f));
            var inner = MeshKit.Part(transform, "RingIn", MeshKit.Cylinder(0.27f, 0.27f, 0.008f, 20), Mat.Cloth, Vector3.up * (FeltY + 0.006f));
            ArtLibrary.Tint(inner.GetComponent<Renderer>(), new Color(0.2f, 0.05f, 0.06f));
            var bowl = MeshKit.Part(transform, "Bowl", MeshKit.Cylinder(0.12f, 0.17f, 0.07f, 12), Mat.Bone, Vector3.up * (FeltY + 0.01f));
            potPile = MeshKit.Pivot(transform, "Pot", Vector3.up * (FeltY + 0.05f));
            MeshKit.Part(potPile, "Coins", MeshKit.Cylinder(0.1f, 0.06f, 0.06f, 10), Mat.Gold, Vector3.zero);
            potPile.gameObject.SetActive(false);
            for (int k = 0; k < Seats; k++)
            {
                var dir = SeatDir(k);
                MeshKit.Part(transform, "Stool" + k, MeshKit.Cylinder(0.2f, 0.22f, 0.5f, 10), Mat.Wood, dir * StoolRadius);
                var cushion = MeshKit.Part(transform, "Cushion" + k, MeshKit.Cylinder(0.21f, 0.2f, 0.05f, 10), Mat.Cloth, dir * StoolRadius + Vector3.up * 0.5f);
                ArtLibrary.Tint(cushion.GetComponent<Renderer>(), new Color(0.45f, 0.1f, 0.12f));
                var cup = MeshKit.Pivot(transform, "Cup" + k, CupLocal(k));
                MeshKit.Part(cup, "Leather", MeshKit.Cylinder(0.075f, 0.06f, 0.14f, 10), Mat.Leather, Vector3.zero);
                cups[k] = cup;
                cup.gameObject.SetActive(false);
                for (int i = 0; i < DiceCount; i++)
                {
                    var d = DiceArt.Build(transform, "Die" + k + i, DieSize);
                    d.localPosition = DieRest(k, i);
                    d.gameObject.SetActive(false);
                    dice[k, i] = d;
                }
            }
            // The skull candle (the table's light).
            MeshKit.Part(transform, "Skull", MeshKit.Ball(0.07f, 8, 6), Mat.Bone, new Vector3(0.18f, FeltY + 0.07f, -0.18f));
            var flame = MeshKit.Part(transform, "Flame", MeshKit.Ball(0.02f, 6, 4), Mat.Glow, new Vector3(0.18f, FeltY + 0.18f, -0.18f));
            ArtLibrary.SetGlow(flame.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.25f) * 2f);
            var l = new GameObject("TableLight").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = new Vector3(0f, FeltY + 1.1f, 0f);
            l.color = new Color(1f, 0.7f, 0.45f);
            l.range = 3.2f;
            l.intensity = 1.4f;
            l.shadows = LightShadows.None;
            _ = bowl;
        }
    }
}
