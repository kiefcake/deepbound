using System;
using Deepbound.Player;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Deepbound.Networking
{
    /// <summary>
    /// M0 connection panel: host a session or join one by IP address.
    ///
    /// <para>
    /// This is development scaffolding, not the shipped flow. Players will join through
    /// Steam friend invites (design doc 2.2.1), which replaces the IP field entirely. Until
    /// then, UnityTransport over localhost or LAN lets two instances on one machine connect,
    /// which is how the networked gameplay gets built and debugged.
    /// </para>
    ///
    /// <para>
    /// Also reads command-line flags so a second instance can connect without clicking:
    /// <c>-host</c>, <c>-client</c>, and <c>-address 192.168.1.20</c>.
    /// </para>
    /// </summary>
    public class NetworkLauncher : MonoBehaviour
    {
        [SerializeField] private string address = "127.0.0.1";
        [SerializeField] private ushort port = 7777;

        /// <summary>
        /// Renders the menu before anyone has spawned. Turned off once the local player's
        /// own camera takes over, and back on after disconnecting.
        /// </summary>
        [SerializeField] private Camera menuCamera;

        /// <summary>Spawned once by the server when a session starts. Runs floors and the run.</summary>
        [SerializeField] private NetworkObject directorPrefab;

        private string status = "";
        private PortMapper.State lastMapState = PortMapper.State.Idle;

        /// <summary>Screen area the panel occupies, in IMGUI coordinates (origin top-left).</summary>
        public static Rect PanelRect { get; private set; }

        private NetworkManager Net => NetworkManager.Singleton;

        /// <summary>The last connection message ("Connecting...", "Disconnected: ..."), for the title screen.</summary>
        public string Status => status;

        /// <summary>This build's identity: host and guest must run the same build (a mismatch fails in strange ways later).</summary>
        public static string BuildId => Application.version + "/" + (string.IsNullOrEmpty(Application.buildGUID) ? "editor" : Application.buildGUID.Substring(0, Mathf.Min(8, Application.buildGUID.Length)));
        public const int MaxPlayers = 4;
        public const string LastAddressKey = "db.lastJoin";

        /// <summary>A test run (smoke, shots, boss test): no router is touched and nothing is saved.</summary>
        public static bool TestRun { get; private set; }
        public ushort Port => port;

        private void Start()
        {
            gameObject.AddComponent<UI.TitleScreen>().Init(this);
            // No music in headless runs (smoke tests): there is no one to hear it.
            if (!Application.isBatchMode) gameObject.AddComponent<UI.AudioDirector>();
            Net.OnClientDisconnectCallback += HandleDisconnect;
            Net.OnTransportFailure += HandleTransportFailure;
            Net.OnServerStopped += HandleServerStopped;

            // Same build or no game: the host checks what each guest sends (2026-10-21, internet play).
            Net.NetworkConfig.ConnectionApproval = true;
            Net.ConnectionApprovalCallback = Approve;
            Net.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(BuildId);
            // Give up on a dead address in about 12 s, not a minute.
            Transport.MaxConnectAttempts = 12;
            Transport.ConnectTimeoutMS = 1000;

            var cl = Environment.GetCommandLineArgs();
            TestRun = Application.isBatchMode || Array.Exists(cl, a => a == "-smoketest" || a == "-shots" || a == "-bosstest" || a == "-classdps" || a == "-noupnp" || a == "-autoplay");

            ApplyCommandLine(Environment.GetCommandLineArgs());
            ApplyWindowMode(Environment.GetCommandLineArgs());
        }

        /// <summary>
        /// Start fullscreen (or windowed, if the player chose that in Options) — unless the
        /// command line set the window explicitly, as the screenshot runs do. Unity would
        /// otherwise remember whatever the last run used, test runs included.
        /// </summary>
        private static void ApplyWindowMode(string[] args)
        {
            if (Application.isBatchMode || Array.IndexOf(args, "-screen-fullscreen") >= 0) return;
            if (PlayerPrefs.GetInt(UI.Settings.FullscreenKey, 1) == 1)
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
            else if (Screen.fullScreen)
                Screen.fullScreen = false;
        }

        private void OnDestroy()
        {
            if (Net == null) return;
            Net.OnClientDisconnectCallback -= HandleDisconnect;
            Net.OnTransportFailure -= HandleTransportFailure;
            Net.OnServerStopped -= HandleServerStopped;
        }

        private void OnApplicationQuit() => PortMapper.Close();

        private void HandleServerStopped(bool wasHost) => PortMapper.Close();

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            string theirs = request.Payload != null && request.Payload.Length > 0 ? System.Text.Encoding.UTF8.GetString(request.Payload) : "unknown";
            bool self = request.ClientNetworkId == NetworkManager.ServerClientId;
            response.CreatePlayerObject = true;
            response.Pending = false;
            if (!self && theirs != BuildId)
            {
                response.Approved = false;
                response.Reason = "Different game version: the host runs build " + BuildId + ", you run " + theirs + ". You both need the same zip.";
                return;
            }
            if (!self && Net.ConnectedClientsIds.Count >= MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "The party is full (" + MaxPlayers + " players).";
                return;
            }
            response.Approved = true;
        }

        private void Update()
        {
            if (menuCamera != null) menuCamera.enabled = NetworkPlayer.Local == null;
            // Tell the host once how friends get in, when the router has answered.
            var mapState = PortMapper.Current;
            if (mapState != lastMapState && mapState != PortMapper.State.Searching && mapState != PortMapper.State.Idle && Net.IsHost && NetworkPlayer.Local != null)
            {
                lastMapState = mapState;
                UI.GameHud.Feed(PortMapper.Line + "   (Esc: copy it)", mapState == PortMapper.State.Open && !PortMapper.SharedAddress ? new Color(0.95f, 0.82f, 0.45f) : new Color(0.9f, 0.6f, 0.45f));
            }
            if (mapState == PortMapper.State.Idle) lastMapState = PortMapper.State.Idle;
        }

        private void ApplyCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-address" && i + 1 < args.Length) address = args[i + 1];
                // Tests use their own port, so they never meet a game that is already open.
                if (args[i] == "-port" && i + 1 < args.Length && ushort.TryParse(args[i + 1], out var p)) port = p;
            }

            if (Array.IndexOf(args, "-host") >= 0) StartHost();
            else if (Array.IndexOf(args, "-client") >= 0) StartClient();

            int smoke = Array.IndexOf(args, "-smoketest");
            if (smoke >= 0)
            {
                float seconds = smoke + 1 < args.Length && float.TryParse(args[smoke + 1], out var s) ? s : 15f;
                gameObject.AddComponent<SmokeTest>().Run(seconds);
            }

            int bossTest = Array.IndexOf(args, "-bosstest");
            if (bossTest >= 0 && bossTest + 1 < args.Length) gameObject.AddComponent<SmokeTest>().BossTest(args[bossTest + 1], this);

            if (Array.IndexOf(args, "-classdps") >= 0) gameObject.AddComponent<SmokeTest>().ClassDps(this);

            int auto = Array.IndexOf(args, "-autoplay");
            if (auto >= 0 && auto + 1 < args.Length) gameObject.AddComponent<AutoPlay>().Run(args[auto + 1], this, args);

            int shots = Array.IndexOf(args, "-shots");
            if (shots >= 0 && shots + 1 < args.Length) gameObject.AddComponent<SmokeTest>().Screenshots(args[shots + 1], this);
        }

        public void StartHost()
        {
            // Listen on every interface, not just loopback, so someone on the same LAN can
            // join too. The address field is only used when joining.
            Transport.SetConnectionData(address, port, "0.0.0.0");
            if (!Net.StartHost())
            {
                status = "Could not host - is port " + port + " already in use?";
                return;
            }

            status = "";
            Instantiate(directorPrefab).Spawn();
            // Ask the home router to let friends in (UPnP; nothing outside the house is contacted).
            if (!TestRun) PortMapper.Open(port);
        }

        public void StartClient(string joinAddress)
        {
            string typed = string.IsNullOrWhiteSpace(joinAddress) ? address : joinAddress.Trim();
            if (!ParseAddress(typed, out var ip, out var joinPort, out var problem)) { status = problem; return; }
            address = ip;
            port = joinPort;
            if (!TestRun) { PlayerPrefs.SetString(LastAddressKey, typed); PlayerPrefs.Save(); }
            StartClient();
        }

        /// <summary>"1.2.3.4", "1.2.3.4:7777" or a host name (looked up here). Spaces and a pasted "http://" are forgiven.</summary>
        public static bool ParseAddress(string text, out string ip, out ushort joinPort, out string problem)
        {
            ip = null; joinPort = 7777; problem = null;
            text = (text ?? "").Trim().Replace(" ", "");
            int scheme = text.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) text = text.Substring(scheme + 3);
            text = text.TrimEnd('/');
            int colon = text.LastIndexOf(':');
            if (colon > 0 && text.IndexOf(':') == colon)
            {
                if (!ushort.TryParse(text.Substring(colon + 1), out joinPort) || joinPort == 0) { problem = "That port is not a number between 1 and 65535."; return false; }
                text = text.Substring(0, colon);
            }
            if (text.Length == 0) { problem = "Type your friend's address first."; return false; }
            if (System.Net.IPAddress.TryParse(text, out var parsed)) { ip = parsed.ToString(); return true; }
            try
            {
                foreach (var a in System.Net.Dns.GetHostAddresses(text))
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { ip = a.ToString(); return true; }
                problem = "Found " + text + " but it has no IPv4 address.";
            }
            catch { problem = "Could not find '" + text + "': check the address."; }
            return false;
        }

        private void StartClient()
        {
            Transport.SetConnectionData(address, port);
            status = Net.StartClient() ? "Connecting to " + address + ":" + port + "..." : "Could not start client.";
        }

        private UnityTransport Transport => (UnityTransport)Net.NetworkConfig.NetworkTransport;

        private void HandleDisconnect(ulong clientId)
        {
            // Fires on the client for its own disconnect, and on the server for each
            // client that leaves. Only the first case means "we are no longer connected".
            if (Net.IsServer || clientId != Net.LocalClientId) return;

            status = string.IsNullOrEmpty(Net.DisconnectReason)
                ? "No answer from " + address + ":" + port + ". Is the host in the game? Did their router open the port (their pause menu says)? Did they let Deepbound through Windows Firewall?"
                : Net.DisconnectReason;
        }

        private void HandleTransportFailure()
        {
            status = "Network transport failed.";
        }

        private void OnGUI()
        {
            useGUILayout = false; // no GUILayout here: skip the layout pass (perf)
            bool connected = Net.IsClient || Net.IsServer;

            // Not connected: the title screen owns the screen.
            if (!connected)
            {
                PanelRect = Rect.zero;
                return;
            }

            // While playing with the mouse captured the session panel is just clutter;
            // Esc frees the mouse and brings it back.
            if (connected && Cursor.lockState == CursorLockMode.Locked)
            {
                PanelRect = Rect.zero;
                return;
            }

            // The party panel lives in the pause menu now; nothing in the corner.
            PanelRect = Rect.zero;
        }

        private void DrawSessionPanel()
        {

            string role = Net.IsHost ? "Host" : Net.IsServer ? "Server" : "Client";
            string players = Net.IsServer ? "  |  Players: " + Net.ConnectedClientsIds.Count : "";
            GUILayout.Label(role + players);

            if (GUILayout.Button("Disconnect"))
            {
                Net.Shutdown();
                status = "";
            }
        }
    }
}
