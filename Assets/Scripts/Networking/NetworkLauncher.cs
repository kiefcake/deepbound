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

        private void Start()
        {
            Net.OnClientDisconnectCallback += HandleDisconnect;
            Net.OnTransportFailure += HandleTransportFailure;

            ApplyCommandLine(Environment.GetCommandLineArgs());
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
            }

            if (Array.IndexOf(args, "-host") >= 0) StartHost();
            else if (Array.IndexOf(args, "-client") >= 0) StartClient();

            int smoke = Array.IndexOf(args, "-smoketest");
            if (smoke >= 0)
            {
                float seconds = smoke + 1 < args.Length && float.TryParse(args[smoke + 1], out var s) ? s : 15f;
                gameObject.AddComponent<SmokeTest>().Run(seconds);
            }

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

            // While playing with the mouse captured the session panel is just clutter;
            // Esc frees the mouse and brings it back.
            if (connected && Cursor.lockState == CursorLockMode.Locked)
            {
                PanelRect = Rect.zero;
                return;
            }

            PanelRect = connected
                ? new Rect(10, 10, 220, 70)
                : new Rect(Mathf.Max(20f, Screen.width * 0.06f), Screen.height / 2f - 120, 300, 240);

            GUILayout.BeginArea(PanelRect, GUI.skin.box);

            if (connected) DrawSessionPanel();
            else DrawConnectPanel();

            GUILayout.EndArea();
        }

        private void DrawConnectPanel()
        {
            GUILayout.Label("DEEPBOUND");
            GUILayout.Label("1-4 player co-op dungeon descent");
            GUILayout.Space(6);

            if (GUILayout.Button("Host", GUILayout.Height(32))) StartHost();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Address", GUILayout.Width(60));
            address = GUILayout.TextField(address);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Join", GUILayout.Height(32))) StartClient();

            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Space(6);
                GUILayout.Label(status);
            }
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
