using System.Collections;
using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Bone Reels, in the gambling tent: pull the lever for a flat stake; three reels of
    /// skulls, bones, candles, coins and crowns spin and stop one by one. Three of a kind pays
    /// big (crowns most), any pair pays double (crowns triple), skulls pay nothing. The server
    /// rolls; every peer watches the reels land on the same symbols. Returns about 86%.
    /// 2026-11-08 rework (the user: "work on every casino game except blackjack"):
    /// <list type="bullet">
    /// <item>THE POT: a progressive jackpot shown on the machine. Every losing pull feeds it 40% of the stake; three crowns
    /// win the whole pot (it starts again at <see cref="PotSeed"/>).</item>
    /// <item>THE NUDGE: after a pull that paid nothing, for <see cref="NudgeSeconds"/> you may pull again for
    /// <see cref="NudgeCost"/>: the reel with your best symbol is HELD and the other two spin; only three of a kind
    /// pays on a nudge.</item>
    /// </list>
    /// </summary>
    public class SlotMachine : Interactable
    {
        public const int Stake = 10;
        public const int PotSeed = 250, PotCap = 4000, NudgeCost = 5;
        public const float NudgeSeconds = 6f, PotShare = 0.4f;
        /// <summary>The progressive pot (three crowns win it).</summary>
        public readonly NetworkVariable<int> Pot = new NetworkVariable<int>(PotSeed);
        // Server: who may nudge, until when, and the three symbols they are nudging from.
        private ulong nudgeFor = ulong.MaxValue;
        private float nudgeUntil;
        private readonly Symbol[] nudgeFrom = new Symbol[3];
        // Local: a nudge offer for the local player (prompt text).
        private float localNudgeUntil;
        private int localHeld = -1;
        public enum Symbol : byte { Skull, Bone, Candle, Coin, Crown }
        private static readonly float[] Weights = { 30f, 28f, 20f, 14f, 8f };
        private static readonly string[] Names = { "SKULL", "BONE", "CANDLE", "COIN", "CROWN" };

        private float busyUntil;

        // What the reels show, for the on-screen card: each reel's symbol, and whether it has stopped yet.
        private readonly byte[] shown = new byte[3];
        private readonly bool[] landed = new bool[3];
        private float cardUntil = -1f;
        private int cardWin = -1;
        private string cardWho;
        private static readonly Color[] SymbolColors =
        {
            new Color(0.85f, 0.85f, 0.8f), new Color(0.95f, 0.9f, 0.75f), new Color(1f, 0.6f, 0.25f), new Color(1f, 0.82f, 0.3f), new Color(1f, 0.92f, 0.45f),
        };
        private readonly Transform[] reels = new Transform[3];
        private Transform lever;

        public override float Range => 2.2f;

        public override string Prompt(PlayerProgress who)
        {
            if (Time.time < busyUntil) return null;
            bool mine = Unity.Netcode.NetworkManager.Singleton != null && who.OwnerClientId == Unity.Netcode.NetworkManager.Singleton.LocalClientId;
            if (mine && Time.time < localNudgeUntil && localHeld >= 0 && who.Gold.Value >= NudgeCost)
                return "NUDGE  -  hold the " + Names[shown[localHeld]] + ", spin the other two  -  " + NudgeCost + " gold (only three of a kind pays)";
            return who.Gold.Value < Stake ? "Bone Reels: you need " + Stake + " gold" : "Pull the Bone Reels  -  " + Stake + " gold   ·   THE POT: " + Pot.Value;
        }

        /// <summary>The reel showing the best symbol (the one a nudge holds).</summary>
        public static int BestReel(Symbol a, Symbol b, Symbol c)
        {
            int best = 0;
            var s = new[] { a, b, c };
            for (int i = 1; i < 3; i++) if (s[i] > s[best]) best = i;
            return best;
        }

        private static Symbol Roll()
        {
            float total = 0f;
            foreach (var w in Weights) total += w;
            float r = Random.value * total;
            for (int i = 0; i < Weights.Length; i++) { if (r < Weights[i]) return (Symbol)i; r -= Weights[i]; }
            return Symbol.Skull;
        }

        /// <summary>The payout (gold returned, stake included) for three symbols.</summary>
        public static int Payout(Symbol a, Symbol b, Symbol c)
        {
            if (a == b && b == c)
            {
                switch (a)
                {
                    case Symbol.Crown: return Stake * 25; // the server pays THE POT instead (see ServerUse)
                    case Symbol.Coin: return Stake * 10;
                    case Symbol.Candle: return Stake * 6;
                    case Symbol.Bone: return Stake * 4;
                    default: return 0;
                }
            }
            Symbol pair = a == b || a == c ? a : b == c ? b : Symbol.Skull;
            bool hasPair = a == b || a == c || b == c;
            if (!hasPair || pair == Symbol.Skull) return 0;
            return pair == Symbol.Crown ? Stake * 3 : Stake * 2;
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (Time.time < busyUntil) return;
            bool nudge = nudgeFor == who.OwnerClientId && Time.time < nudgeUntil;
            int cost = nudge ? NudgeCost : Stake;
            if (who.Gold.Value < cost) return;
            busyUntil = Time.time + 2.2f;
            int held = nudge ? BestReel(nudgeFrom[0], nudgeFrom[1], nudgeFrom[2]) : -1;
            var r = new Symbol[3];
            for (int i = 0; i < 3; i++) r[i] = i == held ? nudgeFrom[i] : Roll();
            ServerSettle(who, r, held, cost);
        }

        /// <summary>Server: pay a pull (forced symbols in tests). <paramref name="held"/> is the nudged reel or -1.</summary>
        public int ServerSettle(PlayerProgress who, Symbol[] r, int held, int cost)
        {
            nudgeFor = ulong.MaxValue;
            bool triple = r[0] == r[1] && r[1] == r[2];
            int win = held >= 0 && !triple ? 0 : Payout(r[0], r[1], r[2]);
            bool jackpot = triple && r[0] == Symbol.Crown;
            if (jackpot) { win = Pot.Value; Pot.Value = PotSeed; }
            else if (win == 0) Pot.Value = Mathf.Min(PotCap, Pot.Value + Mathf.Max(1, Mathf.RoundToInt(cost * PotShare)));
            who.ServerAddGold(win - cost);
            // Nothing paid on a full pull: a nudge is on offer.
            if (win == 0 && held < 0)
            {
                nudgeFor = who.OwnerClientId;
                nudgeUntil = Time.time + 2.2f + NudgeSeconds;
                for (int i = 0; i < 3; i++) nudgeFrom[i] = r[i];
            }
            SpinRpc(who.name, who.OwnerClientId, (byte)r[0], (byte)r[1], (byte)r[2], win, (sbyte)held, cost, jackpot);
            return win;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SpinRpc(string who, ulong client, byte a, byte b, byte c, int win, sbyte held, int cost, bool jackpot)
        {
            busyUntil = Time.time + 2.2f;
            stakeShown = cost;
            heldShown = held;
            jackpotShown = jackpot;
            bool mine = NetworkManager != null && client == NetworkManager.LocalClientId;
            localNudgeUntil = mine && win == 0 && held < 0 ? Time.time + 2.2f + NudgeSeconds : 0f;
            localHeld = BestReel((Symbol)a, (Symbol)b, (Symbol)c);
            StartCoroutine(Spin(who, new[] { a, b, c }, win));
            StartCoroutine(AfterSpin(who, win));
        }
        private int stakeShown = Stake, heldShown = -1;
        private bool jackpotShown;

        private IEnumerator AfterSpin(string who, int win)
        {
            yield return new WaitForSeconds(2.3f);
            UI.CampChatter.SlotResult(who, win);
        }

        private IEnumerator Spin(string who, byte[] stops, int win)
        {
            Sfx.Play(Sound.Bash, transform.position + Vector3.up * 1.2f, 0.5f, 1.4f);
            Sfx.Play(Sound.SlotSpin, transform.position + Vector3.up * 1.2f, 0.8f, 1.2f);
            float start = Time.time;
            cardWho = who;
            cardWin = -1;
            cardUntil = Time.time + 6f;
            for (int i = 0; i < 3; i++) landed[i] = false;
            var spin = new float[3];
            bool[] stopped = new bool[3];
            while (Time.time - start < 1.9f)
            {
                float t = Time.time - start;
                if (lever != null) lever.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 70f, Mathf.Sin(Mathf.Clamp01(t / 0.4f) * Mathf.PI)), 0f, 0f);
                for (int i = 0; i < 3; i++)
                {
                    if (reels[i] == null) continue;
                    float stopAt = i == heldShown ? 0f : 0.8f + i * 0.45f;
                    if (t < stopAt) { spin[i] += Time.deltaTime * 1400f; shown[i] = (byte)((int)(spin[i] / 72f) % 5); }
                    else if (!stopped[i])
                    {
                        stopped[i] = true;
                        shown[i] = stops[i];
                        landed[i] = true;
                        // Land with the rolled symbol facing out.
                        spin[i] = -stops[i] * 72f;
                        Sfx.Play(Sound.Block, reels[i].position, 0.35f, 1.8f);
                    }
                    reels[i].localRotation = Quaternion.Euler(spin[i], 0f, 0f);
                }
                yield return null;
            }
            cardWin = win;
            cardUntil = Time.time + 4.5f;
            string line = Names[stops[0]] + " | " + Names[stops[1]] + " | " + Names[stops[2]];
            int paid = stakeShown;
            var color = win > paid ? new Color(1f, 0.85f, 0.35f) : win == paid ? Color.white : new Color(1f, 0.45f, 0.4f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.1f, jackpotShown ? "THE POT!" : win > 0 ? "+" + (win - paid) : "-" + paid, color);
            UI.GameHud.Feed(who + (heldShown >= 0 ? " nudges" : " pulls") + " the Bone Reels: " + line + (jackpotShown ? "  -  THREE CROWNS! THE POT: " + win + " gold!" : win > 0 ? "  -  wins " + win + " gold!" : "  -  the dead keep it."), color);
            if (jackpotShown) UI.GameHud.ShowTitleCard("THE POT IS WON", who + " takes " + win + " gold", new Color(1f, 0.85f, 0.35f));
            if (win >= Stake * 6) { Sfx.Play(Sound.Relic, transform.position + Vector3.up, 1f); Vfx.Embers(transform.position + Vector3.up * 1.6f, 24, new Color(1f, 0.8f, 0.3f), 4f, 0.07f, 1f); }
            else if (win > 0) Sfx.Play(Sound.Coins, transform.position + Vector3.up, 1f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
        }

        /// <summary>The result, big and plain: three symbol tiles and what they paid. Shown to anyone near the machine.</summary>
                private void OnGUI()
        {
            Deepbound.UI.UiTimer.Begin();
            OnGUIBody();
            Deepbound.UI.UiTimer.Mark("Slots");
        }

        private void OnGUIBody()
        {
            useGUILayout = false; // no GUILayout here: skip the layout pass (perf)
            if (Time.time > cardUntil || DiceTable.LocalOpen || PokerDiceTable.LocalSeated || BlackjackTable.LocalOpen) return;
            var local = Player.NetworkPlayer.Local;
            if (local == null || Vector3.Distance(local.transform.position, transform.position) > 7f) return;
            UI.UiSkin.Apply();
            float w = UI.UiScale.W, h = UI.UiScale.H;
            float tile = 120f, gap = 14f, pw = tile * 3 + gap * 2 + 40, ph = 230f;
            float px = w / 2f - pw / 2f, py = h * 0.5f + 40f;
            UI.UiSkin.Panel(new Rect(px, py, pw, ph), 0.94f);
            GUI.Label(new Rect(px, py + 8, pw, 24), "THE BONE REELS" + (cardWho != null ? "   -   " + cardWho : "") + "      POT " + Pot.Value, new GUIStyle(UI.UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(px + 20 + i * (tile + gap), py + 40, tile, tile * 0.8f);
                var c = SymbolColors[shown[i]];
                UI.UiSkin.Rect(r, landed[i] ? new Color(c.r, c.g, c.b, 0.9f) : new Color(0.3f, 0.28f, 0.25f, 0.9f));
                UI.UiSkin.Rect(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), new Color(0.08f, 0.06f, 0.05f, 0.95f));
                GUI.Label(r, Names[shown[i]], new GUIStyle(UI.UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 22,
                    normal = { textColor = landed[i] ? c : new Color(0.6f, 0.58f, 0.55f) } });
            }
            string result;
            Color rc;
            if (cardWin < 0) { result = "spinning..."; rc = new Color(0.8f, 0.78f, 0.72f); }
            else if (cardWin == 0) { result = "NOTHING  -  lost " + stakeShown + (Time.time < localNudgeUntil ? "  ·  F: NUDGE " + NudgeCost : " gold"); rc = new Color(1f, 0.45f, 0.4f); }
            else if (jackpotShown) { result = "THE POT!  +" + (cardWin - stakeShown) + " gold"; rc = new Color(1f, 0.85f, 0.35f); }
            else if (cardWin >= Stake * 6) { result = "JACKPOT!  +" + (cardWin - stakeShown) + " gold"; rc = new Color(1f, 0.85f, 0.35f); }
            else { result = "WIN  +" + (cardWin - stakeShown) + " gold"; rc = new Color(0.6f, 1f, 0.55f); }
            GUI.Label(new Rect(px, py + 146, pw, 34), result, new GUIStyle(UI.UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 26, normal = { textColor = rc } });
            if (heldShown >= 0) GUI.Label(new Rect(px + 20 + heldShown * (tile + gap), py + 40 + tile * 0.8f - 2, tile, 18), "HELD", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = UI.UiSkin.Gold } });
            GUI.Label(new Rect(px, py + 186, pw, 34), "3 crowns = THE POT   3 coins x10   3 candles x6   3 bones x4   any pair x2 (crowns x3)   skulls pay nothing",
                new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = true });
        }

        private void BuildVisual()
        {
            // The cabinet: dark wood, gilt trim, a window over three reels, a lever on the right.
            var cab = MeshKit.Part(transform, "Cabinet", MeshKit.Box(new Vector3(1f, 1.7f, 0.7f)), Mat.Wood, Vector3.up * 0.85f);
            ArtLibrary.Tint(cab.GetComponent<Renderer>(), new Color(0.45f, 0.28f, 0.2f));
            MeshKit.Part(transform, "TrimTop", MeshKit.Box(new Vector3(1.06f, 0.08f, 0.76f)), Mat.Gold, Vector3.up * 1.72f);
            MeshKit.Part(transform, "TrimBase", MeshKit.Box(new Vector3(1.06f, 0.1f, 0.76f)), Mat.Gold, Vector3.up * 0.05f);
            var window = MeshKit.Part(transform, "Window", MeshKit.Box(new Vector3(0.84f, 0.36f, 0.04f)), Mat.Dark, new Vector3(0f, 1.2f, 0.34f));
            for (int i = 0; i < 3; i++)
            {
                var reel = MeshKit.Pivot(transform, "Reel" + i, new Vector3((i - 1) * 0.26f, 1.2f, 0.2f));
                MeshKit.Part(reel, "Drum", MeshKit.Cylinder(0.17f, 0.17f, 0.22f, 15), Mat.Bone, new Vector3(-0.11f, 0f, 0f), new Vector3(0f, 0f, 90f));
                // Five symbols round the drum, 72 degrees apart; symbol 0 faces out at rest.
                for (int s = 0; s < 5; s++)
                {
                    var face = MeshKit.Pivot(reel, "Face" + s, Vector3.zero, new Vector3(s * 72f, 0f, 0f));
                    var sym = MeshKit.Pivot(face, "Symbol", new Vector3(0f, 0f, 0.18f));
                    sym.localScale = Vector3.one * 1.7f;
                    BuildSymbol(sym, (Symbol)s, Vector3.zero);
                }
                reels[i] = reel;
            }
            var arm = MeshKit.Pivot(transform, "LeverPivot", new Vector3(0.56f, 1.05f, 0f));
            MeshKit.Part(arm, "Arm", MeshKit.Cylinder(0.025f, 0.025f, 0.5f, 6), Mat.Steel, Vector3.zero);
            MeshKit.Part(arm, "Knob", MeshKit.Ball(0.06f, 8, 6), Mat.Bone, Vector3.up * 0.52f);
            lever = arm;
            // A skull on top, candles either side.
            MeshKit.Part(transform, "Skull", MeshKit.Ball(0.12f, 8, 6), Mat.Bone, new Vector3(0f, 1.86f, 0.1f));
            foreach (float x in new[] { -0.38f, 0.38f })
            {
                MeshKit.Part(transform, "Candle", MeshKit.Cylinder(0.03f, 0.03f, 0.16f, 6), Mat.Bone, new Vector3(x, 1.76f, 0.15f));
                var flame = MeshKit.Part(transform, "Flame", MeshKit.Ball(0.025f, 6, 4), Mat.Glow, new Vector3(x, 1.95f, 0.15f));
                ArtLibrary.SetGlow(flame.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.25f) * 2f);
            }
            var l = new GameObject("ReelLight").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = new Vector3(0f, 1.3f, 0.7f);
            l.color = new Color(1f, 0.7f, 0.4f);
            l.range = 2.5f;
            l.intensity = 1.2f;
            l.shadows = LightShadows.None;
        }

        private static void BuildSymbol(Transform at, Symbol s, Vector3 p)
        {
            switch (s)
            {
                case Symbol.Skull:
                    MeshKit.Part(at, "Skull", MeshKit.Ball(0.05f, 8, 6), Mat.Bone, p);
                    MeshKit.Part(at, "EyeL", MeshKit.Ball(0.013f, 4, 3), Mat.Dark, p + new Vector3(-0.018f, 0.008f, 0.042f));
                    MeshKit.Part(at, "EyeR", MeshKit.Ball(0.013f, 4, 3), Mat.Dark, p + new Vector3(0.018f, 0.008f, 0.042f));
                    break;
                case Symbol.Bone:
                    MeshKit.Part(at, "Shaft", MeshKit.Cylinder(0.012f, 0.012f, 0.1f, 5), Mat.Bone, p + new Vector3(-0.05f, 0f, 0f), new Vector3(0f, 0f, 90f));
                    MeshKit.Part(at, "EndL", MeshKit.Ball(0.02f, 5, 4), Mat.Bone, p + new Vector3(-0.05f, 0f, 0f));
                    MeshKit.Part(at, "EndR", MeshKit.Ball(0.02f, 5, 4), Mat.Bone, p + new Vector3(0.05f, 0f, 0f));
                    break;
                case Symbol.Candle:
                    MeshKit.Part(at, "Wax", MeshKit.Cylinder(0.015f, 0.015f, 0.07f, 6), Mat.Bone, p + new Vector3(0f, -0.04f, 0f));
                    var f = MeshKit.Part(at, "Flame", MeshKit.Ball(0.015f, 5, 4), Mat.Glow, p + new Vector3(0f, 0.04f, 0f));
                    ArtLibrary.SetGlow(f.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.2f) * 2f);
                    break;
                case Symbol.Coin:
                    MeshKit.Part(at, "Coin", MeshKit.Cylinder(0.045f, 0.045f, 0.012f, 12), Mat.Gold, p, new Vector3(90f, 0f, 0f));
                    break;
                default:
                    MeshKit.Part(at, "Band", MeshKit.Cylinder(0.04f, 0.04f, 0.025f, 10), Mat.Gold, p + new Vector3(0f, -0.02f, 0f), new Vector3(90f, 0f, 0f));
                    for (int i = -1; i <= 1; i++)
                        MeshKit.Part(at, "Point", MeshKit.Lathe("crownPoint", new[] { new Vector2(0.012f, 0f), new Vector2(0f, 0.035f) }, 4), Mat.Gold, p + new Vector3(i * 0.025f, 0.0f, 0.012f));
                    break;
            }
        }
    }
}
