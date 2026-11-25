using System;
using Deepbound.Player;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Deepbound.Core;

namespace Deepbound.Networking
{
    public class NetworkLauncher : MonoBehaviour
    {
        [SerializeField] private string address = "127.0.0.1";
        [SerializeField] private ushort port = 7777;

        [SerializeField] private Camera menuCamera;

        [SerializeField] private NetworkObject directorPrefab;

        private string status = "";
        private UnityTransport unity;
        private SteamTransport steam;

        public bool OverSteam { get; private set; }
        private string steamHostName = "";
        private PortMapper.State lastMapState = PortMapper.State.Idle;

        public static Rect PanelRect { get; private set; }

        private NetworkManager Net => NetworkManager.Singleton;

        public string Status => status;

        public static string BuildId => Application.version + "/" + (string.IsNullOrEmpty(Application.buildGUID) ? "editor" : Application.buildGUID.Substring(0, Mathf.Min(8, Application.buildGUID.Length)));
        public const int MaxPlayers = 4;
        public const string LastAddressKey = "db.lastJoin";

        public static bool TestRun { get; private set; }
        public ushort Port => port;

        private void Start()
        {
            gameObject.AddComponent<UI.TitleScreen>().Init(this);

            if (!Application.isBatchMode) gameObject.AddComponent<UI.AudioDirector>();
            Net.OnClientDisconnectCallback += HandleDisconnect;
            Net.OnTransportFailure += HandleTransportFailure;
            Net.OnServerStopped += HandleServerStopped;

            Net.NetworkConfig.ConnectionApproval = true;
            Net.ConnectionApprovalCallback = Approve;
            Net.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(BuildId);
            unity = (UnityTransport)Net.NetworkConfig.NetworkTransport;
            Net.OnClientStopped += HandleClientStopped;

            Transport.MaxConnectAttempts = 12;
            Transport.ConnectTimeoutMS = 1000;

            var cl = Environment.GetCommandLineArgs();
            TestRun = Application.isBatchMode || Array.Exists(cl, a => a == "-smoketest" || a == "-shots" || a == "-bosstest" || a == "-classdps" || a == "-noupnp" || a == "-autoplay" || a == "-camptour" || a == "-talkshots" || a == "-ixshots" || a == "-portraits" || a == "-gallery");

            bool steamCheck = Array.IndexOf(cl, "-steamcheck") >= 0;
            if ((!TestRun || steamCheck) && Array.IndexOf(cl, "-nosteam") < 0) SteamPlatform.Boot(this);
            if (steamCheck) StartCoroutine(SteamCheck());

            ApplyCommandLine(Environment.GetCommandLineArgs());
            ApplyWindowMode(Environment.GetCommandLineArgs());
        }

        private static void ApplyWindowMode(string[] args)
        {
            if (Application.isBatchMode || Array.IndexOf(args, "-screen-fullscreen") >= 0) return;
            UI.Settings.ApplyDisplay();
        }

        private void OnDestroy()
        {
            if (Net == null) return;
            Net.OnClientDisconnectCallback -= HandleDisconnect;
            Net.OnTransportFailure -= HandleTransportFailure;
            Net.OnServerStopped -= HandleServerStopped;
            Net.OnClientStopped -= HandleClientStopped;
        }

        private void OnApplicationQuit() => PortMapper.Close();

        private void HandleServerStopped(bool wasHost) { PortMapper.Close(); SteamPlatform.SessionEnded(); }

        private void HandleClientStopped(bool wasHost) => SteamPlatform.SessionEnded();

        private System.Collections.IEnumerator SteamCheck()
        {
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("[SteamCheck] ready " + SteamPlatform.Ready + (SteamPlatform.Ready ? " as " + SteamPlatform.MyName : ", problem: " + SteamPlatform.Problem));
            if (SteamPlatform.Ready)
            {
                StartHost();
                float until = Time.realtimeSinceStartup + 15f;
                while (Time.realtimeSinceStartup < until && !SteamPlatform.InLobby) yield return null;
                Debug.Log("[SteamCheck] hosting " + (Net.IsHost ? "yes" : "no") + ", over Steam " + OverSteam + ", lobby open " + SteamPlatform.InLobby + ", friends listed " + SteamPlatform.Friends().Count);
                Net.Shutdown();
                yield return new WaitForSecondsRealtime(1f);
            }
            Application.Quit();
        }

        public void SetStatus(string text) => status = text ?? "";

        private void UseTransport(bool overSteam)
        {
            OverSteam = overSteam;
            if (overSteam && steam == null) steam = Net.TryGetComponent<SteamTransport>(out var st) ? st : Net.gameObject.AddComponent<SteamTransport>();
            Net.NetworkConfig.NetworkTransport = overSteam ? (NetworkTransport)steam : unity;
        }

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

            int tour = Array.IndexOf(args, "-camptour");
            if (tour >= 0 && tour + 1 < args.Length) gameObject.AddComponent<CampTour>().Run(args[tour + 1], this);

            int talk = Array.IndexOf(args, "-talkshots");
            if (talk >= 0 && talk + 1 < args.Length) gameObject.AddComponent<SmokeTest>().TalkShots(args[talk + 1], this);

            int ix = Array.IndexOf(args, "-ixshots");
            if (ix >= 0 && ix + 1 < args.Length) gameObject.AddComponent<SmokeTest>().IxShots(args[ix + 1], this);

            int gal = Array.IndexOf(args, "-gallery");
            if (gal >= 0 && gal + 1 < args.Length) gameObject.AddComponent<SmokeTest>().Gallery(args[gal + 1], this);

            int por = Array.IndexOf(args, "-portraits");
            if (por >= 0 && por + 1 < args.Length) gameObject.AddComponent<SmokeTest>().Portraits(args[por + 1], this);

            int shots = Array.IndexOf(args, "-shots");
            if (shots >= 0 && shots + 1 < args.Length) gameObject.AddComponent<SmokeTest>().Screenshots(args[shots + 1], this);
        }

        public void StartHost()
        {
            if (SteamPlatform.Ready) StartSteamHost();
            else StartHostByAddress();
        }

        private void StartSteamHost()
        {
            UseTransport(true);
            if (!Net.StartHost())
            {
                status = "Could not host over Steam - try again, or host by address.";
                UseTransport(false);
                return;
            }
            status = "";
            UI.LoadingScreen.Begin(UI.LoadingScreen.Kind.Hosting, 0f);
            Instantiate(directorPrefab).Spawn();
            SteamPlatform.OpenLobby();
        }

        public void StartSteamClient(ulong hostSteamId, string hostName)
        {
            if (Net.IsClient || Net.IsServer) Net.Shutdown();
            UseTransport(true);
            steam.TargetSteamId = hostSteamId;
            steamHostName = string.IsNullOrEmpty(hostName) ? "your friend" : hostName;
            status = Net.StartClient() ? "Joining " + steamHostName + "'s party through Steam..." : "Could not start the Steam connection.";
            if (Net.IsClient) UI.LoadingScreen.Begin(UI.LoadingScreen.Kind.Joining, 0f);
        }

        public void StartHostByAddress()
        {
            UseTransport(false);

            Transport.SetConnectionData(address, port, "0.0.0.0");
            if (!Net.StartHost())
            {
                status = "Could not host - is port " + port + " already in use?";
                return;
            }

            status = "";
            UI.LoadingScreen.Begin(UI.LoadingScreen.Kind.Hosting, 0f);
            Instantiate(directorPrefab).Spawn();

            if (!TestRun) PortMapper.Open(port);
        }

        public void StartClient(string joinAddress)
        {
            string typed = string.IsNullOrWhiteSpace(joinAddress) ? address : joinAddress.Trim();
            if (!ParseAddress(typed, out var ip, out var joinPort, out var problem)) { status = problem; return; }
            address = ip;
            port = joinPort;
            if (!TestRun) { Prefs.SetString(LastAddressKey, typed); Prefs.Save(); }
            StartClient();
        }

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
            UseTransport(false);
            Transport.SetConnectionData(address, port);
            status = Net.StartClient() ? "Connecting to " + address + ":" + port + "..." : "Could not start client.";
            if (Net.IsClient) UI.LoadingScreen.Begin(UI.LoadingScreen.Kind.Joining, 0f);
        }

        private UnityTransport Transport => unity != null ? unity : (UnityTransport)Net.NetworkConfig.NetworkTransport;

        private void HandleDisconnect(ulong clientId)
        {
            if (Net.IsServer || clientId != Net.LocalClientId) return;

            if (OverSteam)
            {
                status = !string.IsNullOrEmpty(Net.DisconnectReason) ? Net.DisconnectReason
                    : !string.IsNullOrEmpty(SteamTransport.LastProblem) ? SteamTransport.LastProblem
                    : "Lost the connection to " + steamHostName + "'s party (Steam).";
                return;
            }
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
            useGUILayout = false;
            bool connected = Net.IsClient || Net.IsServer;

            if (!connected)
            {
                PanelRect = Rect.zero;
                return;
            }

            if (connected && Cursor.lockState == CursorLockMode.Locked)
            {
                PanelRect = Rect.zero;
                return;
            }

            PanelRect = Rect.zero;
        }

    }
}
