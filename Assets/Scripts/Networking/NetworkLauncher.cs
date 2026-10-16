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

        /// <summary>Screen area the panel occupies, in IMGUI coordinates (origin top-left).</summary>
        public static Rect PanelRect { get; private set; }

        private NetworkManager Net => NetworkManager.Singleton;

        /// <summary>The last connection message ("Connecting...", "Disconnected: ..."), for the title screen.</summary>
        public string Status => status;

        private void Start()
        {
            gameObject.AddComponent<UI.TitleScreen>().Init(this);
            // No music in headless runs (smoke tests): there is no one to hear it.
            if (!Application.isBatchMode) gameObject.AddComponent<UI.AudioDirector>();
            Net.OnClientDisconnectCallback += HandleDisconnect;
            Net.OnTransportFailure += HandleTransportFailure;

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
        }

        private void Update()
        {
            if (menuCamera != null) menuCamera.enabled = NetworkPlayer.Local == null;
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
        }

        public void StartClient(string joinAddress)
        {
            address = string.IsNullOrWhiteSpace(joinAddress) ? address : joinAddress.Trim();
            StartClient();
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
                ? "Disconnected (no host at " + address + ":" + port + "?)"
                : "Disconnected: " + Net.DisconnectReason;
        }

        private void HandleTransportFailure()
        {
            status = "Network transport failed.";
        }

        private void OnGUI()
        {
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
