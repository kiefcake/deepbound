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
    /// ARM-WRESTLING AT THE GUTTED LANTERN (reworked 2026-11-13, the user: "move the arm wrestling game closer to the campfire
    /// and tavern ... please rework it also"). A barrel in the tavern yard where BIG GORM, a drover who never leaves it, takes
    /// all comers. It is a contest of GRIP now, not a timing bar:
    /// <list type="bullet">
    /// <item>HOLD SPACE to STRAIN: your arm drives his down, but your <see cref="GripA"/> drains; let go and it comes back.
    /// Empty it and your hand CRAMPS (<see cref="CrampSeconds"/>): no strain at all.</item>
    /// <item>Gorm leans in all the time, and every few seconds he gathers himself (WARN, a red "!") and SURGES. Strain through
    /// a surge and you hold him; be resting and he shoves you most of the way to the wood.</item>
    /// <item>After every surge he is TIRED for a moment: strain then and your push counts x<see cref="TiredBonus"/>.</item>
    /// </list>
    /// So: rest while he leans, brace for the surge, drive when he tires. Pin him: double your stake (<see cref="PaidWins"/>
    /// paid wins a visit). Delver against delver: no Gorm; both strain, and the better-managed grip wins both stakes.
    /// The bout runs on its own clock (<see cref="ServerSimulate"/>), so tests can play it out in one frame.
    /// </summary>
    public class ArmWrestle : Interactable
    {
        public static readonly int[] Stakes = { 10, 25, 50 };
        public const int PaidWins = 3;
        public const float Limit = 45f, CountIn = 1.2f;
        public const float StrainPush = 0.16f, GripDrain = 0.36f, GripRecover = 0.36f, CrampSeconds = 1.0f;
        public const float GormLean = 0.045f, GormLeanGrow = 0.001f, GormSurge = 0.62f, SurgeHeld = 0.22f, TiredBonus = 3.2f;
        public const float WarnSeconds = 0.75f, SurgeSeconds = 1.1f, TiredSeconds = 1.4f;

        public enum Phase : byte { Lean = 0, Warn = 1, Surge = 2, Tired = 3 }

        public readonly NetworkVariable<ulong> Wrestler = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<double> StartAt = new NetworkVariable<double>(-1);
        /// <summary>-1 you are pinned .. +1 you have him (in a delvers' bout +1 is the challenger).</summary>
        public readonly NetworkVariable<float> Balance = new NetworkVariable<float>();
        public readonly NetworkVariable<int> Stake = new NetworkVariable<int>();
        /// <summary>Grip left: the wrestler's (A) and the delver across the barrel's (B).</summary>
        public readonly NetworkVariable<float> GripA = new NetworkVariable<float>(1f), GripB = new NetworkVariable<float>(1f);
        public readonly NetworkVariable<Phase> GormPhase = new NetworkVariable<Phase>();
        /// <summary>Bits: 1 = A straining, 2 = B straining, 4 = A cramped, 8 = B cramped (for the panel and the arms).</summary>
        public readonly NetworkVariable<byte> Strain = new NetworkVariable<byte>();
        /// <summary>Delver against delver: who holds the far side. MaxValue = Gorm; <see cref="Waiting"/> = a challenge stands.</summary>
        public readonly NetworkVariable<ulong> Rival = new NetworkVariable<ulong>(ulong.MaxValue);
        public const ulong Waiting = ulong.MaxValue - 1;
        public const float WaitLimit = 40f;
        /// <summary>Tests: one player may take both sides.</summary>
        public static bool DebugSelfMatch;

        public static bool LocalOpen { get; private set; }
        private static ArmWrestle localTable;
        private static double Now => NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;
        private static int Visit => DungeonDirector.Instance != null ? DungeonDirector.Instance.NextDepth.Value * 1000 + (int)(DungeonDirector.Instance.RunStartedAt.Value % 997) : 0;

        // Server.
        private readonly Dictionary<ulong, (int visit, int wins)> paid = new Dictionary<ulong, (int, int)>();
        private double waitSince;
        private float boutT, phaseLeft, crampA, crampB;
        private bool strainA, strainB;
        private System.Random rng = new System.Random(7);
        public int LastOutcome { get; private set; } // +1 won, -1 lost, 0 draw
        /// <summary>Server, tests: the bout's own clock.</summary>
        public float BoutTime => boutT;

        // Local.
        private int stakeIndex;
        private float openedAt;
        private string result;
        private float resultAt = -10f;
        private bool sentStrain;
        private Transform arms, gorm;

        public override float Range => 2.2f;
        public bool InBout => Wrestler.Value != ulong.MaxValue && StartAt.Value >= 0;
        public bool Delvers => Rival.Value != ulong.MaxValue;
        public bool WaitingForRival => Wrestler.Value != ulong.MaxValue && Rival.Value == Waiting && StartAt.Value < 0;
        private string NameOf(ulong id) => NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var o) ? o.name : "someone";

        public override string Prompt(PlayerProgress who)
        {
            if (LocalOpen) return null;
            if (WaitingForRival) return Wrestler.Value == who.NetworkObjectId ? "Your challenge stands  -  waiting for another delver" : "Take " + NameOf(Wrestler.Value) + "'s challenge  -  " + Stake.Value + " gold each, winner takes both";
            if (InBout) return Delvers ? NameOf(Wrestler.Value) + " and " + NameOf(Rival.Value) + " are wrestling" : "Big Gorm is wrestling " + NameOf(Wrestler.Value);
            return "Arm-wrestle Big Gorm  -  stake " + Stakes[0] + "-" + Stakes[Stakes.Length - 1] + " gold, pin him for double (or challenge a delver)";
        }

        protected override void ServerUse(PlayerProgress who) => OpenRpc(RpcTarget.Single(who.OwnerClientId, RpcTargetUse.Temp));

        [Rpc(SendTo.SpecifiedInParams)]
        private void OpenRpc(RpcParams p = default)
        {
            LocalOpen = true;
            localTable = this;
            openedAt = Time.time;
            sentStrain = false;
            Sfx.Play2D(Sound.UiOpen, 0.6f);
        }

        public static void CloseLocal()
        {
            if (!LocalOpen) return;
            var me = NetworkPlayer.Local;
            if (localTable != null && me != null && (localTable.Wrestler.Value == me.NetworkObjectId || (localTable.Delvers && localTable.Rival.Value == me.NetworkObjectId))) localTable.ForfeitRpc(me.NetworkObject);
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

        private void ResetBout()
        {
            Balance.Value = 0f;
            GripA.Value = GripB.Value = 1f;
            Strain.Value = 0;
            strainA = strainB = false;
            crampA = crampB = 0f;
            boutT = -CountIn;
            GormPhase.Value = Phase.Lean;
            phaseLeft = 2.2f + (float)rng.NextDouble() * 1.5f;
        }

        /// <summary>Server: a bout with Gorm begins (the stake is taken now). Public for tests.</summary>
        public bool ServerClasp(PlayerProgress who, int stake)
        {
            if (InBout || WaitingForRival) return false;
            stake = Mathf.Clamp(stake, Stakes[0], Stakes[Stakes.Length - 1]);
            if (who.Gold.Value < stake) return false;
            who.ServerAddGold(-stake);
            Stake.Value = stake;
            Wrestler.Value = who.NetworkObjectId;
            Rival.Value = ulong.MaxValue;
            StartAt.Value = Now + CountIn;
            rng = new System.Random((int)(who.NetworkObjectId * 31 + (ulong)Visit));
            ResetBout();
            ClaspedRpc(who.name);
            return true;
        }

        [Rpc(SendTo.Server)]
        private void ChallengeRpc(NetworkObjectReference player, int stake)
        {
            if (!player.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who)) return;
            if (Vector3.Distance(obj.transform.position, transform.position) > Range + 1.5f) return;
            if (WaitingForRival && Wrestler.Value != who.NetworkObjectId) ServerAccept(who);
            else ServerChallenge(who, stake);
        }

        /// <summary>Server: a delver stakes and waits for another to take them on. Public for tests.</summary>
        public bool ServerChallenge(PlayerProgress who, int stake)
        {
            if (InBout || WaitingForRival) return false;
            stake = Mathf.Clamp(stake, Stakes[0], Stakes[Stakes.Length - 1]);
            if (who.Gold.Value < stake) return false;
            who.ServerAddGold(-stake);
            Stake.Value = stake;
            Balance.Value = 0f;
            Wrestler.Value = who.NetworkObjectId;
            Rival.Value = Waiting;
            StartAt.Value = -1;
            waitSince = Now;
            ChallengedRpc(who.name, stake);
            return true;
        }

        /// <summary>Server: someone takes the waiting challenge (pays the same stake); the bout begins. Public for tests.</summary>
        public bool ServerAccept(PlayerProgress who)
        {
            if (!WaitingForRival || (who.NetworkObjectId == Wrestler.Value && !DebugSelfMatch) || who.Gold.Value < Stake.Value) return false;
            who.ServerAddGold(-Stake.Value);
            Rival.Value = who.NetworkObjectId;
            StartAt.Value = Now + CountIn;
            ResetBout();
            ClaspedRpc(who.name);
            return true;
        }

        [Rpc(SendTo.Server)]
        private void StrainRpc(NetworkObjectReference player, bool on)
        {
            if (!player.TryGet(out var obj)) return;
            if (obj.NetworkObjectId == Wrestler.Value) ServerSetStrain(+1, on);
            else if (Delvers && obj.NetworkObjectId == Rival.Value) ServerSetStrain(-1, on);
        }

        /// <summary>Server: one side starts or stops straining (+1 the wrestler, -1 the delver across). Public for tests.</summary>
        public void ServerSetStrain(int side, bool on)
        {
            if (!InBout) return;
            if (side > 0) strainA = on; else strainB = on;
        }

        /// <summary>
        /// Server: the bout moves on by <paramref name="dt"/> seconds of its own clock: grips, Gorm's lean / warn / surge / tired
        /// cycle, the balance, the end. FixedUpdate calls it; tests call it in a loop to play a bout out at once.
        /// </summary>
        public void ServerSimulate(float dt)
        {
            if (!InBout) return;
            boutT += dt;
            if (boutT < 0f) return;
            bool a = Side(ref strainA, ref crampA, GripA, dt);
            bool b = Delvers && Side(ref strainB, ref crampB, GripB, dt);
            float push = 0f;
            float tired = !Delvers && GormPhase.Value == Phase.Tired ? TiredBonus : 1f;
            if (a) push += StrainPush * tired;
            if (b) push -= StrainPush;
            if (!Delvers)
            {
                // Gorm's cycle.
                phaseLeft -= dt;
                if (phaseLeft <= 0f)
                {
                    var next = GormPhase.Value == Phase.Lean ? Phase.Warn : GormPhase.Value == Phase.Warn ? Phase.Surge : GormPhase.Value == Phase.Surge ? Phase.Tired : Phase.Lean;
                    GormPhase.Value = next;
                    phaseLeft = next == Phase.Warn ? WarnSeconds : next == Phase.Surge ? SurgeSeconds : next == Phase.Tired ? TiredSeconds : 2f + (float)rng.NextDouble() * 2.5f;
                    if (next == Phase.Surge) SurgeFxRpc(a);
                }
                float lean = GormLean + GormLeanGrow * boutT;
                if (GormPhase.Value == Phase.Surge) push -= a ? GormSurge * SurgeHeld + StrainPush : GormSurge; // braced, your strain only holds him
                else if (GormPhase.Value != Phase.Tired) push -= lean;
            }
            Balance.Value = Mathf.Clamp(Balance.Value + push * dt, -1f, 1f);
            Strain.Value = (byte)((a ? 1 : 0) | (b ? 2 : 0) | (crampA > 0f ? 4 : 0) | (crampB > 0f ? 8 : 0));
            if (Balance.Value >= 1f) ServerEnd(+1);
            else if (Balance.Value <= -1f) ServerEnd(-1);
            else if (boutT > Limit) ServerEnd(0);
        }

        /// <summary>One side's grip this step: true if it is straining (and not cramped).</summary>
        private static bool Side(ref bool strain, ref float cramp, NetworkVariable<float> grip, float dt)
        {
            if (cramp > 0f) { cramp -= dt; grip.Value = Mathf.Min(1f, grip.Value + GripRecover * 0.5f * dt); return false; }
            if (strain)
            {
                grip.Value = Mathf.Max(0f, grip.Value - GripDrain * dt);
                if (grip.Value <= 0f) { cramp = CrampSeconds; strain = false; return false; }
                return true;
            }
            grip.Value = Mathf.Min(1f, grip.Value + GripRecover * dt);
            return false;
        }

        [Rpc(SendTo.Server)]
        private void ForfeitRpc(NetworkObjectReference player)
        {
            if (!player.TryGet(out var obj)) return;
            if (obj.NetworkObjectId == Wrestler.Value) { if (WaitingForRival) ServerCancelWait(); else ServerEnd(-1); }
            else if (InBout && Delvers && obj.NetworkObjectId == Rival.Value) ServerEnd(+1);
        }

        /// <summary>Server: nobody took the challenge (or the challenger left): the stake goes back.</summary>
        public void ServerCancelWait()
        {
            if (!WaitingForRival) return;
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Wrestler.Value, out var obj) && obj.TryGetComponent<PlayerProgress>(out var who)) who.ServerAddGold(Stake.Value);
            Wrestler.Value = ulong.MaxValue;
            Rival.Value = ulong.MaxValue;
            StartAt.Value = -1;
        }

        private void FixedUpdate()
        {
            if (!IsServer) return;
            if (WaitingForRival)
            {
                if (Now - waitSince > WaitLimit || !Near(Wrestler.Value)) ServerCancelWait();
                return;
            }
            if (!InBout) return;
            if (!Near(Wrestler.Value)) { ServerEnd(-1); return; }
            if (Delvers && !DebugSelfMatch && !Near(Rival.Value)) { ServerEnd(+1); return; }
            ServerSimulate(Time.fixedDeltaTime);
        }

        private bool Near(ulong id) => NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj) && Vector3.Distance(obj.transform.position, transform.position) <= Range + 2f;

        /// <summary>Server: the bout is over (+1 the player pinned him, -1 Gorm won, 0 a draw: stake back).</summary>
        public void ServerEnd(int outcome)
        {
            if (!InBout) return;
            LastOutcome = outcome;
            strainA = strainB = false;
            Strain.Value = 0;
            if (Delvers) { ServerEndDelvers(outcome); return; }
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
            Balance.Value = outcome > 0 ? 1f : outcome < 0 ? -1f : 0f;
            EndRpc(name, outcome, payout - stake);
        }

        private void ServerEndDelvers(int outcome)
        {
            int stake = Stake.Value;
            PlayerProgress Get(ulong id) => NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var o) && o.TryGetComponent<PlayerProgress>(out var p) ? p : null;
            var a = Get(Wrestler.Value);
            var b = Get(Rival.Value);
            string an = a != null ? a.name : "someone", bn = b != null ? b.name : "someone";
            if (outcome == 0) { a?.ServerAddGold(stake); b?.ServerAddGold(stake); }
            else (outcome > 0 ? a : b)?.ServerAddGold(stake * 2);
            Wrestler.Value = ulong.MaxValue;
            Rival.Value = ulong.MaxValue;
            StartAt.Value = -1;
            Balance.Value = outcome > 0 ? 1f : outcome < 0 ? -1f : 0f;
            DelversEndRpc(outcome > 0 ? an : bn, outcome > 0 ? bn : an, outcome, stake);
        }

        // ---------------------------------------------------------------- everyone

        private void Say(string text)
        {
            var me = NetworkPlayer.Local;
            if (me != null && Vector3.Distance(me.transform.position, transform.position) < 14f) UI.GameHud.Feed("Big Gorm: " + text, new Color(0.9f, 0.8f, 0.6f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChallengedRpc(string who, int stake)
        {
            Sfx.Play(Sound.Slam, transform.position + Vector3.up, 0.4f, 1.4f);
            if (NetworkPlayer.Local != null && DungeonDirector.InLobby) UI.GameHud.Feed(who + " plants an elbow on Gorm's barrel: " + stake + " gold to any delver who'll take them on.", new Color(1f, 0.85f, 0.4f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DelversEndRpc(string winner, string loser, int outcome, int stake)
        {
            Sfx.Play(Sound.Slam, transform.position + Vector3.up, 0.9f, 1f);
            string text = outcome == 0 ? winner + " and " + loser + " hold each other to a standstill  (stakes back)" : winner + " slams " + loser + "'s arm to the barrel  (+" + stake + " gold)";
            var me = NetworkPlayer.Local;
            if (me != null && Vector3.Distance(me.transform.position, transform.position) < 16f) UI.GameHud.Feed(text, new Color(1f, 0.85f, 0.4f));
            if (localTable == this) { result = text; resultAt = Time.time; }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ClaspedRpc(string who)
        {
            if (!Delvers) Say("*cracks his knuckles* Elbow on the wood. I lean, you hold. When I heave, you'd best be heaving back.");
            Sfx.Play(Sound.Slam, transform.position + Vector3.up, 0.5f, 1.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SurgeFxRpc(bool braced)
        {
            Sfx.Play(braced ? Sound.Bash : Sound.Hurt, transform.position + Vector3.up, braced ? 0.55f : 0.4f, braced ? 0.9f : 1.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EndRpc(string who, int outcome, int net)
        {
            Sfx.Play(Sound.Slam, transform.position + Vector3.up, 0.9f, outcome > 0 ? 1.1f : 0.8f);
            string text = outcome > 0 ? who + " pins Big Gorm!" + (net > 0 ? "  (+" + net + " gold)" : "  (his purse is dry - for pride)")
                : outcome < 0 ? "Big Gorm slams " + who + "'s arm to the barrel" + (net < 0 ? "  (" + net + " gold)" : "")
                : who + " and Gorm hold each other to a standstill  (stake back)";
            var me = NetworkPlayer.Local;
            if (me != null && Vector3.Distance(me.transform.position, transform.position) < 16f) UI.GameHud.Feed(text, outcome > 0 ? new Color(1f, 0.85f, 0.4f) : new Color(0.85f, 0.85f, 0.8f));
            Say(outcome > 0 ? "*stares at his hand* ...Buy me a mead and we never speak of it." : outcome < 0 ? "Hah! Down she goes. You strained when you should have rested." : "Stalemate. My arm's numb and so's yours.");
            if (localTable == this) { result = text; resultAt = Time.time; }
        }

        private void Update()
        {
            if (LocalOpen && localTable == this) LocalInput();
            if (arms != null)
            {
                // The clasped arms tip toward whoever is losing; they shake while someone strains, hard in a surge.
                bool straining = (Strain.Value & 3) != 0, surging = InBout && !Delvers && GormPhase.Value == Phase.Surge;
                float shake = InBout ? (straining ? 0.03f : 0.012f) * (surging ? 2f : 1f) : 0f;
                float k = Balance.Value + Mathf.Sin(Time.time * 37f) * shake;
                arms.localRotation = Quaternion.Euler(0f, 0f, -k * 62f);
            }
            if (gorm != null)
            {
                // Gorm hunches into a surge and sags when he tires.
                float lean = !InBout || Delvers ? 0f : GormPhase.Value == Phase.Surge ? 14f : GormPhase.Value == Phase.Warn ? 8f : GormPhase.Value == Phase.Tired ? -6f : 3f;
                gorm.localRotation = Quaternion.Slerp(gorm.localRotation, Quaternion.Euler(lean, 0f, 0f), Time.deltaTime * 8f);
            }
        }

        private void LocalInput()
        {
            var me = NetworkPlayer.Local;
            if (me == null || !DungeonDirector.InLobby || Vector3.Distance(me.transform.position, transform.position) > Range + 1.5f) { CloseLocal(); return; }
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame || (UI.Binds.Down(UI.Bind.Use) && Time.time - openedAt > 0.3f)) { CloseLocal(); return; }
            bool mine = Wrestler.Value == me.NetworkObjectId || (Delvers && Rival.Value == me.NetworkObjectId);
            if (WaitingForRival)
            {
                if (Wrestler.Value != me.NetworkObjectId && kb.spaceKey.wasPressedThisFrame) ChallengeRpc(me.NetworkObject, Stake.Value);
            }
            else if (!InBout)
            {
                if (kb.leftArrowKey.wasPressedThisFrame) stakeIndex = Mathf.Max(0, stakeIndex - 1);
                if (kb.rightArrowKey.wasPressedThisFrame) stakeIndex = Mathf.Min(Stakes.Length - 1, stakeIndex + 1);
                if (kb.spaceKey.wasPressedThisFrame && Time.time - openedAt > 0.2f) ClaspRpc(me.NetworkObject, Stakes[stakeIndex]);
                if (kb.cKey.wasPressedThisFrame) ChallengeRpc(me.NetworkObject, Stakes[stakeIndex]);
                sentStrain = false;
            }
            else if (mine)
            {
                bool held = kb.spaceKey.isPressed;
                if (held != sentStrain) { sentStrain = held; StrainRpc(me.NetworkObject, held); }
            }
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
            float pw = 480f, ph = 262f;
            var r = new Rect(W * 0.5f - pw * 0.5f, H * 0.6f, pw, ph);
            UI.UiSkin.Panel(r, 0.95f);
            bool rivalSide = Delvers && Rival.Value == me.NetworkObjectId;
            GUI.Label(new Rect(r.x + 20, r.y + 10, pw - 40, 28), Delvers ? "ARM-WRESTLING AT GORM'S BARREL" : "ARM-WRESTLE BIG GORM", new GUIStyle(UI.UiSkin.Heading) { fontSize = 20 });
            if (InBout && (Wrestler.Value == me.NetworkObjectId || rivalSide))
            {
                double t = Now - StartAt.Value;
                // Who is winning: their side left, yours right.
                var tug = new Rect(r.x + 30, r.y + 52, pw - 60, 22);
                UI.UiSkin.Rect(tug, new Color(0.3f, 0.1f, 0.08f, 0.85f));
                float b = ((rivalSide ? -Balance.Value : Balance.Value) + 1f) / 2f;
                UI.UiSkin.Rect(new Rect(tug.x, tug.y, tug.width * b, tug.height), new Color(0.95f, 0.8f, 0.4f));
                UI.UiSkin.Rect(new Rect(tug.center.x - 1f, tug.y - 3f, 2f, tug.height + 6f), new Color(1f, 1f, 1f, 0.5f));
                string them = !Delvers ? "GORM" : NameOf(rivalSide ? Wrestler.Value : Rival.Value).ToUpperInvariant();
                GUI.Label(new Rect(tug.x, tug.yMax + 2, 200, 20), them, UI.UiSkin.Small);
                GUI.Label(new Rect(tug.xMax - 120, tug.yMax + 2, 120, 20), "YOU", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.UpperRight });
                // Your grip.
                float grip = rivalSide ? GripB.Value : GripA.Value;
                bool cramped = (Strain.Value & (rivalSide ? 8 : 4)) != 0, straining = (Strain.Value & (rivalSide ? 2 : 1)) != 0;
                var gr = new Rect(r.x + 30, r.y + 104, pw - 60, 14);
                UI.UiSkin.Rect(gr, new Color(0f, 0f, 0f, 0.5f));
                UI.UiSkin.Rect(new Rect(gr.x, gr.y, gr.width * grip, gr.height), cramped ? new Color(0.8f, 0.2f, 0.15f) : grip < 0.3f ? new Color(0.95f, 0.55f, 0.2f) : new Color(0.55f, 0.85f, 0.5f));
                GUI.Label(new Rect(gr.x, gr.yMax + 1, 260, 18), cramped ? "CRAMP!  let it come back" : straining ? "GRIP  -  straining" : "GRIP", UI.UiSkin.Small);
                // Gorm's mood: the thing to read.
                string call; Color cc;
                if (t < 0) { call = "On three..."; cc = UI.UiSkin.Gold; }
                else if (Delvers) { call = "HOLD SPACE to strain - mind your grip"; cc = UI.UiSkin.Gold; }
                else switch (GormPhase.Value)
                {
                    case Phase.Warn: call = "!  HE GATHERS HIMSELF - BRACE (hold SPACE)"; cc = new Color(1f, 0.4f, 0.3f); break;
                    case Phase.Surge: call = "HE HEAVES!  HOLD!"; cc = new Color(1f, 0.25f, 0.2f); break;
                    case Phase.Tired: call = "HE'S TIRED - DRIVE (hold SPACE)"; cc = new Color(0.6f, 1f, 0.5f); break;
                    default: call = "He leans in. Rest your grip, or strain while it lasts."; cc = new Color(0.9f, 0.85f, 0.7f); break;
                }
                GUI.Label(new Rect(r.x + 20, r.y + 146, pw - 40, 26), call, new GUIStyle(UI.UiSkin.Body) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = cc } });
                GUI.Label(new Rect(r.x + 20, r.y + 180, pw - 40, 20), "Stake " + Stake.Value + " gold   ·   " + Mathf.Max(0, Mathf.CeilToInt(Limit - (float)t)) + " s", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                GUI.Label(new Rect(r.x + 20, r.y + 44, pw - 40, 48), WaitingForRival
                    ? (Wrestler.Value == me.NetworkObjectId ? "Your elbow is on the barrel, " + Stake.Value + " gold staked. Waiting for another delver to take you on (Esc / F withdraws, the stake comes back)."
                        : NameOf(Wrestler.Value) + " waits with " + Stake.Value + " gold on the barrel. Match it and the winner takes both.")
                    : "HOLD SPACE to strain; it drains your grip, so rest while he leans. When he gathers himself (!), brace; when he tires, drive. Pin him for double. C challenges a delver.", new GUIStyle(UI.UiSkin.Small) { wordWrap = true });
                float y = r.y + 98;
                GUI.Label(new Rect(r.x + 20, y + 4, 80, 24), "STAKE", new GUIStyle(UI.UiSkin.Small) { fontSize = 13, normal = { textColor = UI.UiSkin.Gold } });
                for (int i = 0; i < Stakes.Length; i++)
                {
                    var bb = new Rect(r.x + 90 + i * 70, y, 60, 28);
                    if (i == stakeIndex) UI.UiSkin.Rect(new Rect(bb.x - 2, bb.y - 2, bb.width + 4, bb.height + 4), UI.UiSkin.Gold);
                    if (GUI.Button(bb, Stakes[i].ToString(), UI.UiSkin.Button)) stakeIndex = i;
                }
                string act = InBout ? "The barrel is taken - wait your turn."
                    : WaitingForRival ? (Wrestler.Value == me.NetworkObjectId ? "Waiting for a challenger..." : "SPACE  -  take the challenge   (gold: " + prog.Gold.Value + ")")
                    : "SPACE  -  wrestle Gorm     C  -  challenge a delver";
                GUI.Label(new Rect(r.x + 20, y + 40, pw - 40, 24), act, new GUIStyle(UI.UiSkin.Body) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = UI.UiSkin.Gold } });
                if (result != null && Time.time - resultAt < 15f) GUI.Label(new Rect(r.x + 20, y + 70, pw - 40, 40), result, new GUIStyle(UI.UiSkin.Small) { wordWrap = true });
            }
            GUI.Label(new Rect(r.xMax - 200, r.yMax - 24, 190, 20), "Esc / F to step away", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleRight });
        }

        private void OnDisable() { if (localTable == this) { LocalOpen = false; localTable = null; } }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // A barrel on end with a cloth over it, two forearms clasped above it, and Big Gorm on the far side. You stand on the
            // barrel's +z side (its forward), Gorm on the other.
            MeshKit.Part(transform, "Barrel", MeshKit.Cylinder(0.36f, 0.36f, 0.95f, 12), Mat.Wood, Vector3.zero);
            foreach (float h in new[] { 0.12f, 0.8f })
                MeshKit.Part(transform, "Hoop", MeshKit.Cylinder(0.375f, 0.375f, 0.05f, 12), Mat.Steel, Vector3.up * h);
            var cloth = MeshKit.Part(transform, "Cloth", MeshKit.Cylinder(0.4f, 0.4f, 0.02f, 12), Mat.Cloth, Vector3.up * 0.95f);
            ArtLibrary.Tint(cloth.GetComponent<Renderer>(), new Color(0.45f, 0.12f, 0.1f));
            // A mug and a tally of chalk marks: the barrel is his.
            MeshKit.Part(transform, "Mug", MeshKit.Cylinder(0.05f, 0.045f, 0.12f, 8), Mat.Wood, new Vector3(0.24f, 0.96f, -0.18f));
            arms = MeshKit.Pivot(transform, "Arms", Vector3.up * 0.97f);
            var skin = new Color(0.72f, 0.55f, 0.42f);
            foreach (float side in new[] { 1f, -1f })
            {
                var fore = MeshKit.Part(arms, "Forearm", MeshKit.Box(new Vector3(side > 0 ? 0.09f : 0.12f, 0.34f, side > 0 ? 0.09f : 0.12f)), Mat.Bone, new Vector3(0f, 0.17f, side * 0.16f), new Vector3(side * -25f, 0f, 0f));
                ArtLibrary.Tint(fore.GetComponent<Renderer>(), side > 0 ? skin : skin * 0.8f);
                var sleeve = MeshKit.Part(arms, "Sleeve", MeshKit.Box(new Vector3(0.12f, 0.12f, 0.22f)), Mat.Cloth, new Vector3(0f, 0.03f, side * 0.34f));
                ArtLibrary.Tint(sleeve.GetComponent<Renderer>(), side > 0 ? new Color(0.3f, 0.28f, 0.35f) : new Color(0.32f, 0.24f, 0.16f));
            }
            var fist = MeshKit.Part(arms, "Fists", MeshKit.Box(new Vector3(0.13f, 0.12f, 0.11f)), Mat.Bone, new Vector3(0f, 0.34f, 0f));
            ArtLibrary.Tint(fist.GetComponent<Renderer>(), skin);
            arms.localRotation = Quaternion.identity;
            // Big Gorm: a broad drover, stood leaning on his side of the barrel (scaled up a touch), pivoting at the hips.
            var hip = MeshKit.Pivot(transform, "GormHip", new Vector3(0f, 0f, -0.75f));
            var fig = Props.Townsfolk(hip, transform.position + transform.rotation * new Vector3(0f, 0f, -0.75f), transform.position + Vector3.up * 0.9f, Props.Folk.Drinker);
            if (fig != null)
            {
                fig.localScale = new Vector3(1.18f, 1.08f, 1.18f);
                // The Drinker body carries Old Hollis's name tag: Gorm is his own man (2026-11-13 shots).
                var idle = fig.GetComponentInChildren<Art.FolkIdle>();
                if (idle != null) { idle.DisplayName = "Big Gorm"; idle.Trade = "Arm-wrestler"; }
            }
            gorm = hip;
        }
    }
}
