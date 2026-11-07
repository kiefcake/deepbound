using Deepbound.Art;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// H: raise a hand. On its own it is a wave; two players doing it near each other within
    /// a couple of seconds is a HIGH FIVE, and both fight a little harder for a while
    /// (Brothers in Arms: +10% damage for 20 s).
    /// <para>
    /// HOLD H for the EMOTE WHEEL (2026-11-01, the user: "please add an emote wheel"), like the ping wheel: push the
    /// mouse toward a gesture and let go - cheer, bow, laugh, dance, point, beckon, sit. Everyone near sees your body do
    /// it and a line over your head; let go in the middle to cancel. The wave on the wheel is the same as a tap.
    /// </para>
    /// </summary>
    public class Emotes : NetworkBehaviour
    {
        public const float Window = 2f, Reach = 3.5f, BuffSeconds = 20f;
        private const float HoldForWheel = 0.22f;

        /// <summary>The wheel, clockwise from the top.</summary>
        public static readonly Emote[] Wheel = { Emote.Wave, Emote.Cheer, Emote.Salute, Emote.Bow, Emote.Laugh, Emote.Clap, Emote.Dance, Emote.Shrug, Emote.Point, Emote.Beckon, Emote.Kneel, Emote.Sit };
        public static string Name(Emote e) => e == Emote.Wave ? "WAVE / HIGH FIVE" : e.ToString().ToUpperInvariant();
        /// <summary>How long each gesture plays (sitting lasts until you move).</summary>
        public static float Seconds(Emote e) => e == Emote.Sit || e == Emote.Kneel ? 600f : e == Emote.Dance ? 6f : e == Emote.Clap ? 2.6f : e == Emote.Shrug ? 1.5f : e == Emote.Bow ? 1.8f : e == Emote.Laugh ? 2.4f : 2.2f;
        private static string Line(Emote e) => e switch
        {
            Emote.Cheer => "cheers!", Emote.Bow => "bows", Emote.Laugh => "laughs", Emote.Dance => "dances",
            Emote.Point => "points", Emote.Beckon => "beckons", Emote.Sit => "sits down",
            Emote.Salute => "salutes", Emote.Kneel => "kneels", Emote.Shrug => "shrugs", Emote.Clap => "applauds", _ => "waves",
        };

        private static string OwnLine(Emote e) => e switch
        {
            Emote.Cheer => "cheer!", Emote.Bow => "bow", Emote.Laugh => "laugh", Emote.Dance => "dance",
            Emote.Point => "point", Emote.Beckon => "beckon", Emote.Sit => "sit down",
            Emote.Salute => "salute", Emote.Kneel => "kneel", Emote.Shrug => "shrug", Emote.Clap => "applaud", _ => "wave",
        };

        private float readyAt, lastRaisedAt = -10f, heldSince = -1f;
        private Vector2 wheelAim;
        private bool wheelOpen;
        public static bool WheelOpenLocal { get; private set; }
        private PlayerLife life;
        private NetworkPlayer player;

        /// <summary>Server clock time the buff runs out (synced so the HUD can show it).</summary>
        public readonly NetworkVariable<double> BrothersUntil = new NetworkVariable<double>();

        public bool Brothers => NetworkManager != null && NetworkManager.IsListening && NetworkManager.ServerTime.Time < BrothersUntil.Value;

        private void Awake()
        {
            life = GetComponent<PlayerLife>();
            player = GetComponent<NetworkPlayer>();
        }

        private void Update()
        {
            if (!IsOwner) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (life.Downed.Value || UI.GameHud.MenuOpen) { CloseWheel(); heldSince = -1f; return; }
            if (UI.Binds.Down(UI.Bind.Emote)) { heldSince = Time.time; wheelAim = Vector2.zero; }
            if (heldSince < 0f) return;
            if (UI.Binds.Held(UI.Bind.Emote))
            {
                if (!wheelOpen && Time.time - heldSince >= HoldForWheel) { wheelOpen = true; WheelOpenLocal = true; FirstPersonMotor.LookHeld = true; }
                var mouse = Mouse.current;
                if (wheelOpen && mouse != null) wheelAim = Vector2.ClampMagnitude(wheelAim + mouse.delta.ReadValue(), 120f);
                return;
            }
            // Let go: a tap waves (and high-fives), the wheel plays what it points at.
            bool fromWheel = wheelOpen;
            var choice = WheelChoice();
            heldSince = -1f;
            CloseWheel();
            if (Time.time < readyAt) return;
            if (fromWheel && choice == Emote.None) return;
            readyAt = Time.time + 1.2f;
            if (!fromWheel || choice == Emote.Wave) RaiseRpc();
            else EmoteRpc(choice);
        }

        private void CloseWheel()
        {
            if (!wheelOpen) return;
            wheelOpen = false;
            WheelOpenLocal = false;
            FirstPersonMotor.LookHeld = false;
        }

        private Emote WheelChoice()
        {
            if (wheelAim.magnitude < 30f) return Emote.None;
            float ang = Mathf.Atan2(wheelAim.x, wheelAim.y) * Mathf.Rad2Deg; // 0 = up, 90 = right
            float step = 360f / Wheel.Length;
            int slice = Mathf.RoundToInt(Mathf.Repeat(ang, 360f) / step) % Wheel.Length;
            return Wheel[slice];
        }

        /// <summary>Screenshots and tests: show the wheel pushed toward <paramref name="aim"/>.</summary>
        public void DebugShowWheel(bool on, Vector2 aim) { wheelOpen = on; WheelOpenLocal = on; wheelAim = aim; }

        /// <summary>Owner (tests): play a gesture as if chosen on the wheel.</summary>
        public void DebugEmote(Emote e) { if (IsOwner) EmoteRpc(e); }

        [Rpc(SendTo.Server)]
        private void EmoteRpc(Emote e)
        {
            if (e == Emote.None || life.Downed.Value) return;
            EmoteFxRpc(e);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EmoteFxRpc(Emote e)
        {
            player.Rig?.PlayEmote(e, Seconds(e));
            var head = transform.position + Vector3.up * 2.1f;
            if (e == Emote.Cheer) Sfx.Play(Sound.WarCry, head, 0.4f, 1.5f);
            else if (e == Emote.Laugh) Sfx.Play(Sound.Whisper, head, 0.5f, 1.6f);
            else if (e == Emote.Dance) Sfx.Play(Sound.Step, transform.position, 0.6f, 1.2f);
            else if (e == Emote.Clap) Sfx.Play(Sound.Block, head - Vector3.up * 0.6f, 0.25f, 2.2f);
            if (!IsOwner) UI.GameHud.ShowCombo(head, name + " " + Line(e), new Color(0.95f, 0.9f, 0.75f));
            else UI.GameHud.Feed("You " + OwnLine(e), new Color(0.85f, 0.82f, 0.7f));
        }

        private void OnGUI()
        {
            useGUILayout = false;
            if (!IsOwner || !wheelOpen) return;
            float w = UI.UiScale.W, h = UI.UiScale.H;
            var c = new Vector2(w / 2f, h / 2f);
            var choice = WheelChoice();
            UI.UiSkin.Apply();
            GUI.depth = -15;
            UI.UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.3f));
            var gold = UI.UiSkin.Gold;
            for (int i = 0; i < Wheel.Length; i++)
            {
                var e = Wheel[i];
                float a = i * (360f / Wheel.Length) * Mathf.Deg2Rad;
                var pos = c + new Vector2(Mathf.Sin(a) * 250f, -Mathf.Cos(a) * 180f);
                bool on = e == choice;
                UI.UiSkin.Rect(new Rect(pos.x - 72, pos.y - 18, 144, 36), new Color(0f, 0f, 0f, on ? 0.9f : 0.72f));
                if (on) UI.UiSkin.Rect(new Rect(pos.x - 72, pos.y + 16, 144, 3), gold);
                GUI.Label(new Rect(pos.x - 72, pos.y - 18, 144, 36), Name(e),
                    new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = on ? gold : new Color(0.85f, 0.82f, 0.75f, 0.85f) } });
            }
            GUI.Label(new Rect(c.x - 120, c.y - 12, 240, 24), "EMOTE", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.6f) } });
            var dot = c + new Vector2(wheelAim.x, -wheelAim.y) * 0.8f;
            UI.UiSkin.Rect(new Rect(dot.x - 4, dot.y - 4, 8, 8), Color.white);
        }

        /// <summary>Server: play a gesture (the give wheel's salute and bow).</summary>
        public void ServerPlay(Emote e) { if (IsServer && e != Emote.None && !life.Downed.Value) EmoteFxRpc(e); }

        /// <summary>Server: a high five with a chosen ally (the give wheel), no need for both to press.</summary>
        public void ServerHighFiveWith(Emotes other)
        {
            if (!IsServer || other == null || other == this) return;
            if (Vector3.Distance(other.transform.position, transform.position) > GiveWheel.Reach + 2f) return;
            double until = NetworkManager.ServerTime.Time + BuffSeconds;
            BrothersUntil.Value = until;
            other.BrothersUntil.Value = until;
            HighFiveRpc((transform.position + other.transform.position) / 2f + Vector3.up * 1.7f, name, other.name);
        }

        [Rpc(SendTo.Server)]
        private void RaiseRpc()
        {
            lastRaisedAt = Time.time;
            foreach (var other in FindObjectsByType<Emotes>(FindObjectsSortMode.None))
            {
                if (other == this || Time.time - other.lastRaisedAt > Window) continue;
                if (Vector3.Distance(other.transform.position, transform.position) > Reach) continue;
                double until = NetworkManager.ServerTime.Time + BuffSeconds;
                BrothersUntil.Value = until;
                other.BrothersUntil.Value = until;
                other.lastRaisedAt = -10f;
                lastRaisedAt = -10f;
                HighFiveRpc((transform.position + other.transform.position) / 2f + Vector3.up * 1.7f, name, other.name);
                return;
            }
            WaveRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WaveRpc()
        {
            player.Rig?.PlayEmote(Emote.Wave, Seconds(Emote.Wave));
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 1.6f, 0.35f, 1.6f);
            // No ally to slap? The camp folk answer a raised hand in their own ways.
            if (World.DungeonDirector.InLobby)
            {
                float best = 3.2f * 3.2f; int who = -1;
                foreach (var (id, at) in World.Lobby.FolkBodies)
                {
                    var d = at - transform.position; d.y = 0f;
                    if (d.sqrMagnitude < best) { best = d.sqrMagnitude; who = (int)id; }
                }
                if (who >= 0) UI.CampChatter.Waved((World.FolkId)who, name, GetComponent<Progression.PlayerProgress>(), transform.position);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HighFiveRpc(Vector3 at, string a, string b)
        {
            Sfx.Play(Sound.Block, at, 1f, 1.8f);
            Sfx.Play(Sound.Relic, at, 0.7f, 1.3f);
            Vfx.Flash(at, new Color(1f, 0.85f, 0.4f), 5f, 6f, 0.3f);
            Vfx.Embers(at, 16, new Color(1f, 0.85f, 0.4f), 4f, 0.05f, 0.6f);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.4f, "HIGH FIVE!", new Color(1f, 0.85f, 0.4f));
            UI.GameHud.Feed(a + " and " + b + ": Brothers in Arms (+10% damage)", new Color(1f, 0.85f, 0.4f));
        }
    }
}
