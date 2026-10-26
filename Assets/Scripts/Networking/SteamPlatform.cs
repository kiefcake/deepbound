using System;
using System.Collections.Generic;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Networking
{
    /// <summary>
    /// Steam for playing with friends (2026-10-26): starts the Steam API, keeps a friends-only LOBBY while you host,
    /// takes invites and "Join Game" from Steam, and lists your friends for the in-game invite and join panels.
    ///
    /// <para>
    /// Until Deepbound has its own Steam app it runs as Valve's public test app, Spacewar (<see cref="AppId"/> 480):
    /// friends see you "playing Spacewar". Every lobby we make carries <see cref="LobbyKey"/> = our build id, so a
    /// stranger's Spacewar lobby is never joined, and a friend on a different build is told why.
    /// </para>
    ///
    /// <para>
    /// Off in every test run (smoke, shots, autoplay) and when Steam is not running: then hosting and joining fall
    /// back to an IP address, exactly as before.
    /// </para>
    /// </summary>
    public class SteamPlatform : MonoBehaviour
    {
        public const uint AppId = 480;
        public const string LobbyKey = "deepbound";

        public static SteamPlatform Instance { get; private set; }
        public static bool Ready => Instance != null && Instance.ready;
        /// <summary>Why Steam is not available ("" while it is).</summary>
        public static string Problem { get; private set; } = "";

        private bool ready;
        private NetworkLauncher launcher;
        private CSteamID lobby = CSteamID.Nil;
        private CallResult<LobbyCreated_t> created;
        private CallResult<LobbyEnter_t> entered;
        private Callback<GameLobbyJoinRequested_t> joinRequested;
        private Callback<GameRichPresenceJoinRequested_t> presenceJoin;
        private Callback<LobbyInvite_t> invited;

        public struct Invite { public ulong From, Lobby; public string Name; public float At; }
        /// <summary>Invites waiting for an answer (newest last); each is dropped after <see cref="InviteSeconds"/>.</summary>
        public readonly List<Invite> Invites = new List<Invite>();
        public const float InviteSeconds = 120f;

        public struct Friend { public ulong Id; public string Name; public bool Online, InDeepbound; public ulong Lobby; }
        private readonly List<Friend> friends = new List<Friend>();
        private float friendsAt = -10f;

        public static void Boot(NetworkLauncher owner)
        {
            if (Instance != null) return;
            var go = new GameObject("Steam");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SteamPlatform>();
            Instance.launcher = owner;
            Instance.Init();
        }

        private void Init()
        {
            try
            {
                // Run as Spacewar without Steam launching us: the app id rides in the environment (steam_appid.txt
                // next to the exe says the same).
                Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
                Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());
                if (!Packsize.Test()) { Problem = "Steam library mismatch."; return; }
                var result = SteamAPI.InitEx(out string error);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    bool closed = result == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient || !SteamAPI.IsSteamRunning();
                    Problem = closed ? "Steam is not running - start Steam to play with friends" : "Steam: " + error;
                    Debug.Log("[Steam] not available: " + result + " " + error);
                    return;
                }
            }
            catch (Exception e)
            {
                // Missing steam_api64.dll or similar: play on without Steam.
                Problem = "Steam could not start.";
                Debug.Log("[Steam] init threw: " + e.Message);
                return;
            }
            ready = true;
            Problem = "";
            SteamNetworkingUtils.InitRelayNetworkAccess();
            created = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            entered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => Join(r.m_steamIDLobby.m_SteamID));
            presenceJoin = Callback<GameRichPresenceJoinRequested_t>.Create(r => JoinFromConnect(r.m_rgchConnect));
            invited = Callback<LobbyInvite_t>.Create(OnInvited);
            Debug.Log("[Steam] ready as " + SteamFriends.GetPersonaName() + " (" + SteamUser.GetSteamID() + ")");
            // Started by a friend's "Join Game" while closed: Steam passes the lobby on the command line.
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "+connect_lobby") JoinFromConnect("+connect_lobby " + args[i + 1]);
        }

        public static string MyName => Ready ? SteamFriends.GetPersonaName() : "";
        public static ulong MyId => Ready ? SteamUser.GetSteamID().m_SteamID : 0;
        public static bool InLobby => Ready && Instance.lobby != CSteamID.Nil;

        private float retryAt;

        private void Update()
        {
            if (!ready)
            {
                // Steam opened after the game: pick it up (only between parties, every few seconds).
                var net = NetworkManager.Singleton;
                bool inParty = net != null && (net.IsClient || net.IsServer);
                if (!inParty && Problem.StartsWith("Steam is not running") && Time.unscaledTime >= retryAt)
                {
                    retryAt = Time.unscaledTime + 5f;
                    if (SteamAPI.IsSteamRunning()) Init();
                }
                return;
            }
            SteamAPI.RunCallbacks();
            Invites.RemoveAll(i => Time.unscaledTime - i.At > InviteSeconds);
        }

        private void OnApplicationQuit()
        {
            if (!ready) return;
            LeaveLobby();
            SteamAPI.Shutdown();
            ready = false;
        }

        // ---------------------------------------------------------------- hosting

        /// <summary>Host: open a friends-only lobby for the party that just started.</summary>
        public static void OpenLobby()
        {
            if (!Ready) return;
            Instance.LeaveLobby();
            Instance.created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, NetworkLauncher.MaxPlayers));
        }

        private void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK) { Debug.Log("[Steam] lobby failed: " + r.m_eResult); return; }
            lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(lobby, LobbyKey, NetworkLauncher.BuildId);
            SteamMatchmaking.SetLobbyData(lobby, "host", SteamUser.GetSteamID().m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(lobby, "name", SteamFriends.GetPersonaName());
            // Friends see "Join Game" on you in their Steam friends list.
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + lobby.m_SteamID);
            SteamFriends.SetRichPresence(LobbyKey, NetworkLauncher.BuildId);
            SteamFriends.SetRichPresence("status", "Hosting a Deepbound party");
            Debug.Log("[Steam] lobby " + lobby.m_SteamID + " open");
        }

        /// <summary>Host: invite one friend (they get a Steam notification and an in-game prompt).</summary>
        public static bool InviteFriend(ulong friend) => InLobby && SteamMatchmaking.InviteUserToLobby(Instance.lobby, new CSteamID(friend));

        /// <summary>Host: Steam's own invite dialog (needs the overlay; the in-game list works without it).</summary>
        public static void OpenInviteOverlay() { if (InLobby) SteamFriends.ActivateGameOverlayInviteDialog(Instance.lobby); }

        public void LeaveLobby()
        {
            if (!ready) return;
            if (lobby != CSteamID.Nil) SteamMatchmaking.LeaveLobby(lobby);
            lobby = CSteamID.Nil;
            SteamFriends.ClearRichPresence();
        }

        /// <summary>The session ended (host stopped or guest left): leave the lobby too.</summary>
        public static void SessionEnded() { if (Ready) Instance.LeaveLobby(); }

        // ---------------------------------------------------------------- joining

        private void OnInvited(LobbyInvite_t r)
        {
            // Only invites to a game of ours (Spacewar's id) are worth showing.
            if (r.m_ulGameID != AppId) return;
            Invites.RemoveAll(i => i.From == r.m_ulSteamIDUser);
            Invites.Add(new Invite { From = r.m_ulSteamIDUser, Lobby = r.m_ulSteamIDLobby, Name = SteamFriends.GetFriendPersonaName(new CSteamID(r.m_ulSteamIDUser)), At = Time.unscaledTime });
            if (Player.NetworkPlayer.Local != null) UI.GameHud.Feed(Invites[Invites.Count - 1].Name + " invited you to their party: Esc to answer", new Color(0.55f, 0.8f, 1f));
        }

        private void JoinFromConnect(string connect)
        {
            if (string.IsNullOrEmpty(connect)) return;
            var parts = connect.Split(' ');
            for (int i = 0; i + 1 < parts.Length; i++)
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out var id)) { Join(id); return; }
        }

        /// <summary>Join a friend's lobby; once inside, connect to its host (any party you are in is left first).</summary>
        public static void Join(ulong lobbyId)
        {
            if (!Ready || lobbyId == 0) return;
            var net = NetworkManager.Singleton;
            if (net != null && (net.IsClient || net.IsServer)) net.Shutdown();
            Instance.LeaveLobby();
            Instance.Invites.RemoveAll(i => i.Lobby == lobbyId);
            Instance.launcher.SetStatus("Finding your friend's party on Steam...");
            Instance.entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)));
        }

        private void OnLobbyEntered(LobbyEnter_t r, bool ioFailure)
        {
            var id = new CSteamID(r.m_ulSteamIDLobby);
            // 1 = k_EChatRoomEnterResponseSuccess
            if (ioFailure || r.m_EChatRoomEnterResponse != 1) { launcher.SetStatus("Could not join that party (it may have closed, or it is full)."); return; }
            string build = SteamMatchmaking.GetLobbyData(id, LobbyKey);
            if (string.IsNullOrEmpty(build)) { SteamMatchmaking.LeaveLobby(id); launcher.SetStatus("That is not a Deepbound party."); return; }
            if (build != NetworkLauncher.BuildId)
            {
                SteamMatchmaking.LeaveLobby(id);
                launcher.SetStatus("Different game version: they run build " + build + ", you run " + NetworkLauncher.BuildId + ". You both need the same zip.");
                return;
            }
            lobby = id;
            var host = SteamMatchmaking.GetLobbyOwner(id);
            if (!ulong.TryParse(SteamMatchmaking.GetLobbyData(id, "host"), out ulong hostId)) hostId = host.m_SteamID;
            string name = SteamMatchmaking.GetLobbyData(id, "name");
            SteamFriends.SetRichPresence("status", "In a Deepbound party");
            launcher.StartSteamClient(hostId, string.IsNullOrEmpty(name) ? SteamFriends.GetFriendPersonaName(new CSteamID(hostId)) : name);
        }

        // ---------------------------------------------------------------- friends

        /// <summary>Friends, those in a Deepbound party first, then online, then the rest (refreshed every 2 s).</summary>
        public static List<Friend> Friends()
        {
            if (!Ready) return Instance != null ? Instance.friends : new List<Friend>();
            var me = Instance;
            if (Time.unscaledTime - me.friendsAt < 2f) return me.friends;
            me.friendsAt = Time.unscaledTime;
            me.friends.Clear();
            int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < n; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                var state = SteamFriends.GetFriendPersonaState(id);
                var f = new Friend { Id = id.m_SteamID, Name = SteamFriends.GetFriendPersonaName(id), Online = state != EPersonaState.k_EPersonaStateOffline };
                if (SteamFriends.GetFriendGamePlayed(id, out var game) && game.m_gameID.AppID().m_AppId == AppId)
                {
                    f.InDeepbound = !string.IsNullOrEmpty(SteamFriends.GetFriendRichPresence(id, LobbyKey));
                    if (game.m_steamIDLobby.IsValid()) f.Lobby = game.m_steamIDLobby.m_SteamID;
                }
                me.friends.Add(f);
            }
            me.friends.Sort((a, b) =>
            {
                int ka = a.Lobby != 0 ? 0 : a.InDeepbound ? 1 : a.Online ? 2 : 3, kb = b.Lobby != 0 ? 0 : b.InDeepbound ? 1 : b.Online ? 2 : 3;
                return ka != kb ? ka.CompareTo(kb) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return me.friends;
        }
    }
}
