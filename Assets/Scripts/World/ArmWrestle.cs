using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.World
{
    /// <summary>
    /// ARM-WRESTLING SERGEANT BRANN (2026-11-08, the user's list: "camp pastimes: arm wrestling"). A barrel at Brann's post;
    /// F opens the bout panel: pick a stake (←/→), SPACE to clasp hands. A marker swings across a bar and speeds up as the
    /// bout goes on; SPACE when it crosses the gold mark HEAVES his arm toward the barrel, a miss SLIPS yours back. Brann
    /// leans in all the while, harder the longer it lasts. Pin him: double your stake back (Brann pays
    /// <see cref="PaidWins"/> wins a visit, then it is for pride). Pinned: he keeps it. Everyone sees the two arms over
    /// the barrel tip one way or the other. Only while Brann is at his post.
    /// </summary>
    public class ArmWrestle : Interactable
    {
        public static readonly int[] Stakes = { 10, 25, 50 };
        public const int PaidWins = 3;
        public const float Zone = 0.24f, Heave = 0.11f, Slip = 0.035f, BrannLean = 0.05f, BrannTire = 0.0045f, HeaveGap = 0.18f, Limit = 45f;

        public readonly NetworkVariable<ulong> Wrestler = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<double> StartAt = new NetworkVariable<double>(-1);
        /// <summary>-1 Brann has you pinned .. +1 you have him.</summary>
        public readonly NetworkVariable<float> Balance = new NetworkVariable<float>();
        public readonly NetworkVariable<int> Stake = new NetworkVariable<int>();

        public static bool LocalOpen { get; private set; }
        private static ArmWrestle localTable;
        private static double Now => NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;
        private static int Visit => DungeonDirector.Instance != null ? DungeonDirector.Instance.NextDepth.Value * 1000 + (int)(DungeonDirector.Instance.RunStartedAt.Value % 997) : 0;

        // Server.
        private readonly Dictionary<ulong, (int visit, int wins)> paid = new Dictionary<ulong, (int, int)>();
        private double lastHeave;
        private ulong holder;
        public int LastOutcome { get; private set; } // +1 won, -1 lost, 0 draw

        // Local.
        private int stakeIndex;
        private float openedAt;
        private string result;
        private float resultAt = -10f;
        private Transform arms;

        public override float Range => 2.2f;
        public bool InBout => Wrestler.Value != ulong.MaxValue && StartAt.Value >= 0;

        /// <summary>The swinging marker, -1..1, at a time into the bout: it speeds up as the bout goes on.</summary>
        public static float MarkerAt(double t)
        {
            float tt = (float)System.Math.Max(0, t);
            return Mathf.Sin(tt * 3.0f + tt * tt * 0.035f);
        }

        private static bool BrannHome { get { var d = FolkPlaces.Now(FolkId.Brann) - Lobby.YardSpot; d.y = 0f; return d.magnitude < 1.2f; } }

        public override string Prompt(PlayerProgress who)
        {
            if (LocalOpen) return null;
            if (!BrannHome) return null;
            if (InBout) return "Brann is wrestling " + (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Wrestler.Value, out var o) ? o.name : "someone");
            return "Arm-wrestle Sergeant Brann  -  stake " + Stakes[0] + "-" + Stakes[Stakes.Length - 1] + " gold, pin him for double";
        }

        protected override void ServerUse(PlayerProgress who) => OpenRpc(RpcTarget.Single(who.OwnerClientId, RpcTargetUse.Temp));

        [Rpc(SendTo.SpecifiedInParams)]
        private void OpenRpc(RpcParams p = default)
        {
            LocalOpen = true;
            localTable = this;
            openedAt = Time.time;
            Sfx.Play2D(Sound.UiOpen, 0.6f);
        }

        public static void CloseLocal()
        {
            if (!LocalOpen) return;
            var me = NetworkPlayer.Local;
            if (localTable != null && me != null && localTable.Wrestler.Value == me.NetworkObjectId) localTable.ForfeitRpc(me.NetworkObject);
            LocalOpen = false;
            localTable = null;
            Sfx.Play2D(Sound.UiClose, 0.6f);
        }

        // ---------------------------------------------------------------- server

        [Rpc(SendTo.Server)]
        private void ClaspRpc(NetworkObjectReference player, int stake)
        {
            if (!player.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who)) return;
            if (Vector3.Distance(obj.transform.position, transform.position) > Range + 1.5f) return;
            ServerClasp(who, stake);
        }

        /// <summary>Server: a bout begins (the stake is taken now). Public for tests.</summary>
        public bool ServerClasp(PlayerProgress who, int stake)
        {
            if (InBout || !BrannHome) return false;
            stake = Mathf.Clamp(stake, Stakes[0], Stakes[Stakes.Length - 1]);
            if (who.Gold.Value < stake) return false;
            who.ServerAddGold(-stake);
            Stake.Value = stake;
            Balance.Value = 0f;
            Wrestler.Value = who.NetworkObjectId;
            StartAt.Value = Now + 1.2; // "three... two..."
            lastHeave = -10;
            holder = who.OwnerClientId;
            DungeonDirector.Instance?.ServerHoldFolk(FolkId.Brann, holder, true); // he stays for the bout
            ClaspedRpc(who.name);
            return true;
        }

        [Rpc(SendTo.Server)]
        private void HeaveRpc(NetworkObjectReference player, double at)
        {
            if (!player.TryGet(out var obj) || obj.NetworkObjectId != Wrestler.Value) return;
            ServerHeave(System.Math.Min(at, Now));
        }

        /// <summary>Server: the wrestler heaves at server time <paramref name="at"/>. True if it caught the mark. Public for tests.</summary>
        public bool ServerHeave(double at)
        {
            if (!InBout || at < StartAt.Value || at - lastHeave < HeaveGap) return false;
            lastHeave = at;
            float m = MarkerAt(at - StartAt.Value);
            bool good = Mathf.Abs(m) <= Zone;
            Balance.Value = Mathf.Clamp(Balance.Value + (good ? Heave * (1f - 0.4f * Mathf.Abs(m) / Zone) : -Slip), -1f, 1f);
            HeaveFxRpc(good);
            if (Balance.Value >= 1f) ServerEnd(+1);
            return good;
        }

        [Rpc(SendTo.Server)]
        private void ForfeitRpc(NetworkObjectReference player)
        {
            if (player.TryGet(out var obj) && obj.NetworkObjectId == Wrestler.Value) ServerEnd(-1);
        }

        private void FixedUpdate()
        {
            if (!IsServer || !InBout) return;
            double t = Now - StartAt.Value;
            if (t < 0) return;
            // Brann leans in, harder as it goes on.
            Balance.Value = Mathf.Max(-1f, Balance.Value - Time.fixedDeltaTime * (BrannLean + BrannTire * (float)t));
            if (Balance.Value <= -1f) { ServerEnd(-1); return; }
            if (t > Limit) { ServerEnd(0); return; }
            // Walked away, went down, or gone: a forfeit.
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Wrestler.Value, out var obj) || Vector3.Distance(obj.transform.position, transform.position) > Range + 2f) ServerEnd(-1);
        }

        /// <summary>Server: the bout is over (+1 the player pinned him, -1 Brann won, 0 a draw: stake back).</summary>
        public void ServerEnd(int outcome)
        {
            if (!InBout) return;
            LastOutcome = outcome;
            int stake = Stake.Value, payout = 0;
            string name = "someone";
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Wrestler.Value, out var obj) && obj.TryGetComponent<PlayerProgress>(out var who))
            {
                name = who.name;
                if (outcome == 0) payout = stake;
                else if (outcome > 0)
                {
                    var key = who.OwnerClientId;
                    var rec = paid.TryGetValue(key, out var r) && r.visit == Visit ? r : (Visit, 0);
                    if (rec.Item2 < PaidWins) { payout = stake * 2; paid[key] = (Visit, rec.Item2 + 1); }
                    else payout = stake; // for pride: the stake back
                }
                if (payout > 0) who.ServerAddGold(payout);
            }
            Wrestler.Value = ulong.MaxValue;
            StartAt.Value = -1;
            DungeonDirector.Instance?.ServerHoldFolk(FolkId.Brann, holder, false);
            Balance.Value = outcome > 0 ? 1f : outcome < 0 ? -1f : 0f;
            EndRpc(name, outcome, payout - stake);
        }

        // ---------------------------------------------------------------- everyone

        [Rpc(SendTo.ClientsAndHost)]
        private void ClaspedRpc(string who)
        {
            UI.CampChatter.Say(FolkId.Brann, "*He plants his elbow on the barrel and grins.* Elbow down, wrist straight. On three. One. Two.", 3f);
            Sfx.Play(Sound.Slam, transform.position + Vector3.up, 0.5f, 1.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HeaveFxRpc(bool good)
        {
            Sfx.Play(good ? Sound.Bash : Sound.Hurt, transform.position + Vector3.up, good ? 0.5f : 0.35f, good ? 1.1f : 1.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EndRpc(string who, int outcome, int net)
        {
            Sfx.Play(Sound.Slam, transform.position + Vector3.up, 0.9f, outcome > 0 ? 1.1f : 0.8f);
            string text = outcome > 0 ? who + " pins Sergeant Brann!" + (net > 0 ? "  (+" + net + " gold)" : "  (he's out of coin - for pride)")
                : outcome < 0 ? "Sergeant Brann slams " + who + "'s arm to the barrel" + (net < 0 ? "  (" + net + " gold)" : "")
                : who + " and Brann hold each other to a standstill  (stake back)";
            var me = NetworkPlayer.Local;
            if (me != null && Vector3.Distance(me.transform.position, transform.position) < 16f) UI.GameHud.Feed(text, outcome > 0 ? new Color(1f, 0.85f, 0.4f) : new Color(0.85f, 0.85f, 0.8f));
            UI.CampChatter.Say(FolkId.Brann, outcome > 0 ? "*He shakes out his hand.* ...Lucky. Lucky and strong. Don't tell the squad." : outcome < 0 ? "Down you go. Eat more. Drill more. Come back." : "Hah! Stalemate. My arm's tired of your face.", 4f);
            if (localTable == this) { result = text; resultAt = Time.time; }
        }

        private void Update()
        {
            if (LocalOpen && localTable == this) LocalInput();
            if (arms != null)
            {
                // The clasped arms tip toward whoever is losing; a little tremble while they strain.
                float k = Balance.Value + (InBout ? Mathf.Sin(Time.time * 31f) * 0.015f : 0f);
                arms.localRotation = Quaternion.Euler(0f, 0f, -k * 62f);
            }
        }

        private void LocalInput()
        {
            var me = NetworkPlayer.Local;
            if (me == null || !DungeonDirector.InLobby || Vector3.Distance(me.transform.position, transform.position) > Range + 1.5f) { CloseLocal(); return; }
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame || (UI.Binds.Down(UI.Bind.Use) && Time.time - openedAt > 0.3f)) { CloseLocal(); return; }
            bool mine = Wrestler.Value == me.NetworkObjectId;
            if (!InBout)
            {
                if (kb.leftArrowKey.wasPressedThisFrame) stakeIndex = Mathf.Max(0, stakeIndex - 1);
                if (kb.rightArrowKey.wasPressedThisFrame) stakeIndex = Mathf.Min(Stakes.Length - 1, stakeIndex + 1);
                if (kb.spaceKey.wasPressedThisFrame) ClaspRpc(me.NetworkObject, Stakes[stakeIndex]);
            }
            else if (mine && kb.spaceKey.wasPressedThisFrame) HeaveRpc(me.NetworkObject, Now);
        }

        private void OnGUI()
        {
            if (!LocalOpen || localTable != this) return;
            var me = NetworkPlayer.Local;
            var prog = me != null ? me.GetComponent<PlayerProgress>() : null;
            if (prog == null) return;
            useGUILayout = false;
            UI.UiSkin.Apply();
            float W = UI.UiScale.W, H = UI.UiScale.H;
            float pw = 460f, ph = 250f;
            var r = new Rect(W * 0.5f - pw * 0.5f, H * 0.62f, pw, ph);
            UI.UiSkin.Panel(r, 0.95f);
            GUI.Label(new Rect(r.x + 20, r.y + 10, pw - 40, 28), "ARM-WRESTLE SERGEANT BRANN", new GUIStyle(UI.UiSkin.Heading) { fontSize = 20 });
            if (InBout && Wrestler.Value == me.NetworkObjectId)
            {
                double t = Now - StartAt.Value;
                // The heave bar: the gold mark in the middle, the marker swinging over it.
                var bar = new Rect(r.x + 30, r.y + 56, pw - 60, 26);
                UI.UiSkin.Rect(bar, new Color(0f, 0f, 0f, 0.5f));
                float zw = bar.width * Zone;
                UI.UiSkin.Rect(new Rect(bar.center.x - zw / 2f, bar.y, zw, bar.height), new Color(0.85f, 0.65f, 0.25f, 0.85f));
                float m = t < 0 ? 0f : MarkerAt(t);
                UI.UiSkin.Rect(new Rect(bar.center.x + m * bar.width / 2f - 3f, bar.y - 6f, 6f, bar.height + 12f), Color.white);
                // Who is winning: Brann's side left, yours right.
                var tug = new Rect(r.x + 30, r.y + 104, pw - 60, 16);
                UI.UiSkin.Rect(tug, new Color(0.3f, 0.1f, 0.08f, 0.8f));
                float b = (Balance.Value + 1f) / 2f;
                UI.UiSkin.Rect(new Rect(tug.x, tug.y, tug.width * b, tug.height), new Color(0.95f, 0.8f, 0.4f));
                GUI.Label(new Rect(tug.x, tug.yMax + 2, 120, 20), "BRANN", UI.UiSkin.Small);
                GUI.Label(new Rect(tug.xMax - 120, tug.yMax + 2, 120, 20), "YOU", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.UpperRight });
                GUI.Label(new Rect(r.x + 20, r.y + 150, pw - 40, 24), t < 0 ? "On three..." : "SPACE when the marker crosses the gold!", new GUIStyle(UI.UiSkin.Body) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UI.UiSkin.Gold } });
                GUI.Label(new Rect(r.x + 20, r.y + 178, pw - 40, 20), "Stake " + Stake.Value + " gold   ·   " + Mathf.Max(0, Mathf.CeilToInt(Limit - (float)t)) + " s", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                GUI.Label(new Rect(r.x + 20, r.y + 44, pw - 40, 40), "Catch the swinging marker on the gold mark to heave; miss and you slip. He leans in harder the longer it lasts. Pin him for double your stake.", new GUIStyle(UI.UiSkin.Small) { wordWrap = true });
                float y = r.y + 92;
                GUI.Label(new Rect(r.x + 20, y + 4, 80, 24), "STAKE", new GUIStyle(UI.UiSkin.Small) { fontSize = 13, normal = { textColor = UI.UiSkin.Gold } });
                for (int i = 0; i < Stakes.Length; i++)
                {
                    var bb = new Rect(r.x + 90 + i * 70, y, 60, 28);
                    if (i == stakeIndex) UI.UiSkin.Rect(new Rect(bb.x - 2, bb.y - 2, bb.width + 4, bb.height + 4), UI.UiSkin.Gold);
                    if (GUI.Button(bb, Stakes[i].ToString(), UI.UiSkin.Button)) stakeIndex = i;
                }
                GUI.Label(new Rect(r.x + 20, y + 40, pw - 40, 24), InBout ? "Brann is busy with someone else." : "SPACE  -  clasp hands   (gold: " + prog.Gold.Value + ")", new GUIStyle(UI.UiSkin.Body) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = UI.UiSkin.Gold } });
                if (result != null && Time.time - resultAt < 15f) GUI.Label(new Rect(r.x + 20, y + 70, pw - 40, 40), result, new GUIStyle(UI.UiSkin.Small) { wordWrap = true });
            }
            GUI.Label(new Rect(r.xMax - 200, r.yMax - 24, 190, 20), "Esc / F to step away", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleRight });
        }

        private void OnDisable() { if (localTable == this) { LocalOpen = false; localTable = null; } }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // A barrel on end with a cloth over it, and two forearms clasped above it. The networked object is the barrel; you
            // stand on its +z side (its forward), Brann on the other.
            MeshKit.Part(transform, "Barrel", MeshKit.Cylinder(0.36f, 0.36f, 0.95f, 12), Mat.Wood, Vector3.zero);
            foreach (float h in new[] { 0.12f, 0.8f })
                MeshKit.Part(transform, "Hoop", MeshKit.Cylinder(0.375f, 0.375f, 0.05f, 12), Mat.Steel, Vector3.up * h);
            var cloth = MeshKit.Part(transform, "Cloth", MeshKit.Cylinder(0.4f, 0.4f, 0.02f, 12), Mat.Cloth, Vector3.up * 0.95f);
            ArtLibrary.Tint(cloth.GetComponent<Renderer>(), new Color(0.45f, 0.12f, 0.1f));
            // The arms pivot about the barrel's top centre, in the plane between the two of you.
            arms = MeshKit.Pivot(transform, "Arms", Vector3.up * 0.97f);
            var skin = new Color(0.72f, 0.55f, 0.42f);
            foreach (float side in new[] { 1f, -1f })
            {
                var fore = MeshKit.Part(arms, "Forearm", MeshKit.Box(new Vector3(0.09f, 0.34f, 0.09f)), Mat.Bone, new Vector3(0f, 0.17f, side * 0.16f), new Vector3(side * -25f, 0f, 0f));
                ArtLibrary.Tint(fore.GetComponent<Renderer>(), side > 0 ? skin : skin * 0.85f);
                var sleeve = MeshKit.Part(arms, "Sleeve", MeshKit.Box(new Vector3(0.12f, 0.12f, 0.22f)), Mat.Cloth, new Vector3(0f, 0.03f, side * 0.34f));
                ArtLibrary.Tint(sleeve.GetComponent<Renderer>(), side > 0 ? new Color(0.3f, 0.28f, 0.35f) : new Color(0.4f, 0.12f, 0.1f));
            }
            var fist = MeshKit.Part(arms, "Fists", MeshKit.Box(new Vector3(0.13f, 0.12f, 0.11f)), Mat.Bone, new Vector3(0f, 0.34f, 0f));
            ArtLibrary.Tint(fist.GetComponent<Renderer>(), skin);
            // The pivot rolls about the line between you and Brann (local z): a win tips the fists toward his side.
            arms.localRotation = Quaternion.identity;
        }
    }
}
