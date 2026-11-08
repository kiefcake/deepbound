using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.World
{
    /// <summary>
    /// BONES, in the gambling tent (reworked 2026-11-08, the user: "please work on every casino game except blackjack").
    /// F opens the table: pick a stake (10 / 25 / 50 / 100) and CALL the throw of two bone dice before they fly:
    /// LOW (2-6) or HIGH (8-12) pays x2.2, SEVEN pays x5.5, DOUBLES pays x5.5 (stake included; each call returns about 92%).
    /// After a win, LET IT RIDE stakes the whole payout on the next call (the streak shows; Mister Bones' tent cheers at 3+).
    /// The server rolls and pays; every peer sees the same two dice tumble out of the cup and land on the rolled faces.
    /// </summary>
    public class DiceTable : Interactable
    {
        public enum Call : byte { Low, Seven, High, Doubles }
        public static readonly int[] Stakes = { 10, 25, 50, 100 };
        public const int MaxRide = 600;
        public const float CupY = 0.995f; // a die's centre resting on the baize

        /// <summary>Gold back (stake included) per gold staked, for a winning call.</summary>
        public static float Pays(Call c) => c == Call.Low || c == Call.High ? 2.2f : 5.5f;
        public static string CallName(Call c) => c switch { Call.Low => "LOW", Call.Seven => "SEVEN", Call.High => "HIGH", _ => "DOUBLES" };
        public static string CallText(Call c) => c switch { Call.Low => "2 to 6", Call.Seven => "exactly 7", Call.High => "8 to 12", _ => "any pair" };
        public static bool Wins(Call c, int a, int b)
        {
            int s = a + b;
            return c switch { Call.Low => s <= 6, Call.Seven => s == 7, Call.High => s >= 8, _ => a == b };
        }
        public static int Payout(Call c, int a, int b, int stake) => Wins(c, a, b) ? Mathf.RoundToInt(stake * Pays(c)) : 0;

        /// <summary>The local player has the Bones panel open.</summary>
        public static bool LocalOpen { get; private set; }
        private static DiceTable localTable;

        private float busyUntil;
        // Local panel state.
        private int stakeIndex = 0, lastWin, streak;
        private string result;
        private Color resultColor = Color.white;
        private float openedAt, resultAt = -10f;
        private readonly Transform[] dice = new Transform[2];

        public override float Range => 2.4f;

        public override string Prompt(PlayerProgress who) =>
            LocalOpen ? null : who.Gold.Value < Stakes[0] ? "Bones: you need " + Stakes[0] + " gold to play" : "Play Bones  -  call the throw";

        protected override void ServerUse(PlayerProgress who) => OpenRpc(RpcTarget.Single(who.OwnerClientId, RpcTargetUse.Temp));

        [Rpc(SendTo.SpecifiedInParams)]
        private void OpenRpc(RpcParams p = default)
        {
            LocalOpen = true;
            localTable = this;
            openedAt = Time.time;
            result = null;
            Sfx.Play2D(Sound.UiOpen, 0.6f);
        }

        public static void CloseLocal()
        {
            if (!LocalOpen) return;
            LocalOpen = false;
            localTable = null;
            Sfx.Play2D(Sound.UiClose, 0.6f);
        }

        /// <summary>Owner: call the throw for a stake.</summary>
        public void Call_(Call c, int stake)
        {
            var me = NetworkPlayer.Local;
            if (me == null) return;
            lastCall = c;
            CallRpc(me.NetworkObject, (byte)c, stake);
        }

        [Rpc(SendTo.Server)]
        private void CallRpc(NetworkObjectReference player, byte call, int stake)
        {
            if (!player.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who)) return;
            if (Vector3.Distance(obj.transform.position, transform.position) > Range + 1.5f) return;
            if (Time.time < busyUntil || call > 3) return;
            stake = Mathf.Clamp(stake, Stakes[0], MaxRide);
            if (who.Gold.Value < stake) return;
            busyUntil = Time.time + 1f;
            int a = Random.Range(1, 7), b = Random.Range(1, 7);
            int win = Payout((Call)call, a, b, stake);
            who.ServerAddGold(win - stake);
            RollRpc(who.name, who.OwnerClientId, call, (byte)a, (byte)b, stake, win);
        }

        /// <summary>Tests: one call, as the server, for a player.</summary>
        public int ServerDebugCall(PlayerProgress who, Call c, int stake, int a, int b)
        {
            int win = Payout(c, a, b, stake);
            who.ServerAddGold(win - stake);
            RollRpc(who.name, who.OwnerClientId, (byte)c, (byte)a, (byte)b, stake, win);
            return win;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RollRpc(string who, ulong client, byte call, byte a, byte b, int stake, int win)
        {
            Sfx.Play(Sound.Dice, transform.position + Vector3.up, 0.9f);
            // Both dice leave the cup and land on the rolled faces.
            for (int i = 0; i < 2; i++)
            {
                if (dice[i] == null) continue;
                var tumble = dice[i].GetComponent<DieTumble>() ?? dice[i].gameObject.AddComponent<DieTumble>();
                var land = new Vector3(0.05f + i * 0.17f + Random.Range(-0.04f, 0.04f), CupY, -0.1f + Random.Range(-0.12f, 0.12f));
                tumble.Throw(new Vector3(-0.35f, CupY + 0.25f, 0.2f), land, i == 0 ? a : b, Random.Range(0f, 360f), i * 0.06f);
            }
            var c = (Call)call;
            bool won = win > 0;
            var color = won ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.45f, 0.4f);
            string throwText = a + " + " + b + " = " + (a + b) + (a == b ? " (doubles)" : "");
            UI.GameHud.Feed(who + " calls " + CallName(c) + " at Bones: " + throwText + "  -  " + (won ? "wins " + (win - stake) + " gold!" : "loses " + stake), color);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 1.6f, won ? "+" + (win - stake) : "-" + stake, color);
            if (won) Sfx.Play(Sound.Coins, transform.position + Vector3.up, 1f, 1.1f);
            if (NetworkManager != null && client == NetworkManager.LocalClientId)
            {
                resultAt = Time.time;
                result = throwText + "   " + (won ? "YOU WIN " + win + " gold" : "the house takes " + stake);
                resultColor = color;
                lastWin = win;
                streak = won ? streak + 1 : 0;
                if (streak >= 3) UI.CampChatter.Say(FolkId.Bones, streak + " in a row? *rattle* The bones like you tonight.", 4f);
            }
        }

        private void Update()
        {
            if (!LocalOpen || localTable != this) return;
            var me = NetworkPlayer.Local;
            if (me == null || !DungeonDirector.InLobby || Vector3.Distance(me.transform.position, transform.position) > Range + 1.5f) { CloseLocal(); return; }
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame || (UI.Binds.Down(UI.Bind.Use) && Time.time - openedAt > 0.3f)) { CloseLocal(); return; }
            if (kb.leftArrowKey.wasPressedThisFrame) stakeIndex = Mathf.Max(0, stakeIndex - 1);
            if (kb.rightArrowKey.wasPressedThisFrame) stakeIndex = Mathf.Min(Stakes.Length - 1, stakeIndex + 1);
            for (int i = 0; i < 4; i++)
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) Call_((Call)i, Stakes[stakeIndex]);
            if (kb.rKey.wasPressedThisFrame && CanRide(me)) Call_(lastCall, Mathf.Min(MaxRide, lastWin));
        }

        private Call lastCall = Call.High;
        private bool CanRide(NetworkPlayer me) => lastWin > 0 && Time.time - resultAt < 30f && me.GetComponent<PlayerProgress>().Gold.Value >= Mathf.Min(MaxRide, lastWin);

        private void OnGUI()
        {
            if (!LocalOpen || localTable != this) return;
            var me = NetworkPlayer.Local;
            var prog = me != null ? me.GetComponent<PlayerProgress>() : null;
            if (prog == null) return;
            useGUILayout = false;
            UI.UiSkin.Apply();
            float W = UI.UiScale.W, H = UI.UiScale.H;
            float pw = 560f, ph = 330f;
            var r = new Rect(W - pw - 40f, H * 0.5f - ph * 0.5f, pw, ph);
            UI.UiSkin.Panel(r, 0.95f);
            GUI.Label(new Rect(r.x + 20, r.y + 10, pw - 40, 28), "BONES   ·   call the throw", new GUIStyle(UI.UiSkin.Heading) { fontSize = 20 });
            GUI.Label(new Rect(r.x + 20, r.y + 40, pw - 40, 20), "Two bone dice. Call it before they fly. Gold: " + prog.Gold.Value, UI.UiSkin.Small);

            // Stake.
            float y = r.y + 66f;
            GUI.Label(new Rect(r.x + 20, y + 4, 80, 24), "STAKE", new GUIStyle(UI.UiSkin.Small) { fontSize = 13, normal = { textColor = UI.UiSkin.Gold } });
            for (int i = 0; i < Stakes.Length; i++)
            {
                var b = new Rect(r.x + 90 + i * 74, y, 66, 28);
                if (i == stakeIndex) UI.UiSkin.Rect(new Rect(b.x - 2, b.y - 2, b.width + 4, b.height + 4), UI.UiSkin.Gold);
                GUI.enabled = prog.Gold.Value >= Stakes[i];
                if (GUI.Button(b, Stakes[i].ToString(), UI.UiSkin.Button)) stakeIndex = i;
                GUI.enabled = true;
            }
            GUI.Label(new Rect(r.x + 400, y + 4, 140, 24), "← → to change", UI.UiSkin.Small);

            // Calls.
            y += 44f;
            int stake = Stakes[stakeIndex];
            for (int i = 0; i < 4; i++)
            {
                var c = (Call)i;
                var b = new Rect(r.x + 20 + (i % 2) * ((pw - 50) / 2 + 10), y + (i / 2) * 58, (pw - 50) / 2, 50);
                GUI.enabled = prog.Gold.Value >= stake && Time.time > busyLocalUntil;
                if (GUI.Button(b, "", UI.UiSkin.Button)) { Call_(c, stake); lastCall = c; busyLocalUntil = Time.time + 1f; }
                GUI.enabled = true;
                GUI.Label(new Rect(b.x + 12, b.y + 4, b.width - 20, 24), (i + 1) + ".  " + CallName(c) + "   " + CallText(c), new GUIStyle(UI.UiSkin.Body) { fontSize = 16, fontStyle = FontStyle.Bold });
                GUI.Label(new Rect(b.x + 12, b.y + 26, b.width - 20, 20), "pays x" + Pays(c).ToString("0.0") + "  (" + Mathf.RoundToInt(stake * Pays(c)) + " back)", new GUIStyle(UI.UiSkin.Small) { normal = { textColor = UI.UiSkin.Gold } });
            }
            y += 124f;
            if (result != null)
                GUI.Label(new Rect(r.x + 20, y, pw - 40, 28), result, new GUIStyle(UI.UiSkin.Heading) { fontSize = 18, normal = { textColor = resultColor } });
            y += 32f;
            if (CanRide(me))
            {
                int ride = Mathf.Min(MaxRide, lastWin);
                if (GUI.Button(new Rect(r.x + 20, y, 300, 30), "R.  LET IT RIDE  -  " + ride + " on " + CallName(lastCall) + (streak > 1 ? "   (streak " + streak + ")" : ""), UI.UiSkin.Button))
                    Call_(lastCall, ride);
            }
            GUI.Label(new Rect(r.xMax - 180, r.yMax - 26, 170, 20), "Esc / F to leave the table", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleRight });
        }
        private float busyLocalUntil;

        private void OnDisable() { if (localTable == this) { LocalOpen = false; localTable = null; } }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // A low round table, a green baize top, the house's leather cup, and two bone dice resting by it.
            MeshKit.Part(transform, "Leg", MeshKit.Cylinder(0.15f, 0.25f, 0.85f, 10), Mat.Wood, Vector3.zero);
            MeshKit.Part(transform, "Top", MeshKit.Cylinder(0.75f, 0.75f, 0.08f, 16), Mat.Wood, Vector3.up * 0.85f);
            var baize = MeshKit.Part(transform, "Baize", MeshKit.Cylinder(0.68f, 0.68f, 0.02f, 16), Mat.Cloth, Vector3.up * 0.93f);
            ArtLibrary.Tint(baize.GetComponent<Renderer>(), new Color(0.12f, 0.32f, 0.16f));
            // Three painted bands on the baize: low, seven, high.
            var bandColors = new[] { new Color(0.25f, 0.35f, 0.6f), new Color(0.7f, 0.55f, 0.2f), new Color(0.6f, 0.18f, 0.16f) };
            for (int i = 0; i < 3; i++)
            {
                var band = MeshKit.Part(transform, "Band" + i, MeshKit.Box(new Vector3(0.28f, 0.004f, 0.18f)), Mat.Cloth, new Vector3(-0.3f + i * 0.3f, 0.942f, -0.42f));
                ArtLibrary.Tint(band.GetComponent<Renderer>(), bandColors[i]);
            }
            MeshKit.Part(transform, "Cup", MeshKit.Cylinder(0.1f, 0.13f, 0.22f, 10), Mat.Leather, new Vector3(-0.35f, 0.94f, 0.2f));
            for (int i = 0; i < 2; i++)
            {
                dice[i] = DiceArt.Build(transform, "Die" + i, 0.085f);
                dice[i].localPosition = new Vector3(0.1f + i * 0.15f, CupY, -0.1f);
                dice[i].localRotation = DiceArt.FaceUp(i == 0 ? 6 : 1, i * 30f);
            }
        }
    }
}
