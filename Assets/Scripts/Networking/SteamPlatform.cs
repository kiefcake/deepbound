using System;
using System.Collections.Generic;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Networking
{
    public class SteamPlatform : MonoBehaviour
    {
        public const uint AppId = 480;
        public const string LobbyKey = "deepbound";

        public static SteamPlatform Instance { get; private set; }
        public static bool Ready => Instance != null && Instance.ready;

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
                Problem = "Steam could not start.";
                Debug.Log("[Steam] init threw: " + e.Message);
                return;
            }
            ready = true;
            Problem = "";
            SteamNetworkingUtils.InitRelayNetworkAccess();
            RaiseSendBuffer();
            created = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            entered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => Join(r.m_steamIDLobby.m_SteamID));
            presenceJoin = Callback<GameRichPresenceJoinRequested_t>.Create(r => JoinFromConnect(r.m_rgchConnect));
            invited = Callback<LobbyInvite_t>.Create(OnInvited);
            Debug.Log("[Steam] ready as " + SteamFriends.GetPersonaName() + " (" + SteamUser.GetSteamID() + ")");

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "+connect_lobby") JoinFromConnect("+connect_lobby " + args[i + 1]);
        }

        private static void RaiseSendBuffer()
        {
            var size = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
            try
            {
                System.Runtime.InteropServices.Marshal.WriteInt32(size, 8 * 1024 * 1024);
                bool ok = SteamNetworkingUtils.SetConfigValue(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize,
                    ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, size);
                Debug.Log("[Steam] send buffer 8 MB: " + ok);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(size); }
        }

        public static string MyName => Ready ? SteamFriends.GetPersonaName() : "";
        public static ulong MyId => Ready ? SteamUser.GetSteamID().m_SteamID : 0;
        public static bool InLobby => Ready && Instance.lobby != CSteamID.Nil;

        private float retryAt;

        private void Update()
        {
            if (!ready)
            {
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

        public static void OpenLobby()
        {
            if (!Ready) return;
            Instance.LeaveLobby();
            Instance.created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, NetworkLauncher.MaxPlayers));
        }

        private void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK)
            {
                Debug.Log("[Steam] lobby failed: " + r.m_eResult + (ioFailure ? " (io failure)" : ""));
                UI.GameHud.Feed("Steam could not open your party to friends (" + r.m_eResult + "). Host again to retry.", new Color(1f, 0.6f, 0.4f));
                return;
            }
            lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(lobby, LobbyKey, NetworkLauncher.BuildId);
            SteamMatchmaking.SetLobbyData(lobby, "host", SteamUser.GetSteamID().m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(lobby, "name", SteamFriends.GetPersonaName());

            SteamFriends.SetRichPresence("connect", "+connect_lobby " + lobby.m_SteamID);
            SteamFriends.SetRichPresence(LobbyKey, NetworkLauncher.BuildId);
            SteamFriends.SetRichPresence("status", "Hosting a Deepbound party");
            Debug.Log("[Steam] lobby " + lobby.m_SteamID + " open");
        }

        public static bool InviteFriend(ulong friend) => InLobby && SteamMatchmaking.InviteUserToLobby(Instance.lobby, new CSteamID(friend));

        public static void OpenInviteOverlay() { if (InLobby) SteamFriends.ActivateGameOverlayInviteDialog(Instance.lobby); }

        public void LeaveLobby()
        {
            if (!ready) return;
            if (lobby != CSteamID.Nil) SteamMatchmaking.LeaveLobby(lobby);
            lobby = CSteamID.Nil;
            SteamFriends.ClearRichPresence();
        }

        public static void SessionEnded() { if (Ready) Instance.LeaveLobby(); }

        private void OnInvited(LobbyInvite_t r)
        {
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

        public static void Join(ulong lobbyId)
        {
            if (!Ready || lobbyId == 0)
            {
                if (lobbyId != 0 && Instance != null && Instance.launcher != null) Instance.launcher.SetStatus("Steam is not ready, so the invite cannot be answered. Start Steam and try again.");
                return;
            }
            var net = NetworkManager.Singleton;
            if (net != null && (net.IsClient || net.IsServer)) net.Shutdown();
            Instance.LeaveLobby();
            Instance.Invites.RemoveAll(i => i.Lobby == lobbyId);
            Instance.launcher.SetStatus("Finding your friend's party on Steam...");
            Instance.entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)));
        }

        private static string WhyNoLobby(EChatRoomEnterResponse why)
        {
            switch (why)
            {
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist: return "That party has closed.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull: return "That party is full.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed: return "That party is friends-only: only the host's Steam friends can join.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited: return "Your Steam account is limited and cannot join parties yet.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseCommunityBan: return "Steam will not let this account join that party.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseMemberBlockedYou:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseYouBlockedMember: return "Someone in that party and you have blocked each other on Steam.";
                default: return "Steam could not join that party (error " + (int)why + "). Try again.";
            }
        }

        private void OnLobbyEntered(LobbyEnter_t r, bool ioFailure)
        {
            var id = new CSteamID(r.m_ulSteamIDLobby);

            if (ioFailure || r.m_EChatRoomEnterResponse != 1)
            {
                Debug.Log("[Steam] lobby join failed: response " + r.m_EChatRoomEnterResponse + (ioFailure ? " (io failure)" : ""));
                launcher.SetStatus(ioFailure ? "Could not reach Steam to join that party. Try again in a moment." : WhyNoLobby((EChatRoomEnterResponse)r.m_EChatRoomEnterResponse));
                return;
            }
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
