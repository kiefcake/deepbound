using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Networking
{
    /// <summary>
    /// Netcode over Steam's peer-to-peer sockets (2026-10-26, "work on multiplayer for testing": the user's router
    /// opens no ports, so friends could not reach a host). Steam finds the host by SteamID and relays the traffic
    /// through Valve's network when a direct route fails, so nobody forwards a port or installs a VPN.
    ///
    /// <para>
    /// A polling transport: <see cref="PollEvent"/> hands NGO one queued event at a time; connection changes arrive
    /// on Steam's status callback (run by <see cref="SteamPlatform"/>), messages are read from a poll group (host) or
    /// the one connection (guest). Transport ids are the Steam connection handles; the host is <see cref="ServerClientId"/> 0.
    /// </para>
    /// </summary>
    public class SteamTransport : NetworkTransport
    {
        /// <summary>Guest: the SteamID of the host to connect to (set before StartClient).</summary>
        public ulong TargetSteamId;

        public override ulong ServerClientId => 0;

        /// <summary>Why the last Steam connection ended, in words for the join screen ("" if it ended cleanly).</summary>
        public static string LastProblem { get; private set; } = "";
        private float connectStartedAt, nextSendWarn;
        private int sendFailures;

        /// <summary>
        /// Steam refuses any one message over 512 KB, and NGO can hand us a bigger batch (the first sync of a whole floor).
        /// Every message carries a one-byte frame: <see cref="Whole"/>, or a run of <see cref="Part"/> chunks ending in
        /// <see cref="Last"/>. Big ones are always reliable, and reliable messages arrive in order, so a chunk run is never interleaved.
        /// </summary>
        private const byte Whole = 0, Part = 1, Last = 2;
        public const int ChunkSize = 256 * 1024;
        private readonly Dictionary<uint, System.IO.MemoryStream> partial = new Dictionary<uint, System.IO.MemoryStream>();
        /// <summary>For tests: the largest message handed to Steam and the most chunks one was split into.</summary>
        public static int LargestSent { get; private set; }

        private struct Pending { public NetworkEvent Kind; public ulong Client; public ArraySegment<byte> Data; public float At; }

        private readonly Queue<Pending> events = new Queue<Pending>();
        private readonly Dictionary<ulong, HSteamNetConnection> peers = new Dictionary<ulong, HSteamNetConnection>();
        private readonly HashSet<uint> connected = new HashSet<uint>();
        private readonly IntPtr[] inbox = new IntPtr[256];
        private Callback<SteamNetConnectionStatusChangedCallback_t> statusChanged;
        private HSteamListenSocket listen = HSteamListenSocket.Invalid;
        private HSteamNetPollGroup group = HSteamNetPollGroup.Invalid;
        private HSteamNetConnection toHost = HSteamNetConnection.Invalid;
        private bool server, running;
        private IntPtr sendBuffer = IntPtr.Zero;
        private int sendCapacity;

        public override bool IsSupported => SteamPlatform.Ready;

        public override void Initialize(NetworkManager networkManager = null) { }

        public override bool StartServer()
        {
            if (!SteamPlatform.Ready) return false;
            server = true;
            Begin();
            listen = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            group = SteamNetworkingSockets.CreatePollGroup();
            return listen != HSteamListenSocket.Invalid;
        }

        public override bool StartClient()
        {
            if (!SteamPlatform.Ready || TargetSteamId == 0) return false;
            server = false;
            Begin();
            var who = new SteamNetworkingIdentity();
            who.SetSteamID(new CSteamID(TargetSteamId));
            LastProblem = "";
            connectStartedAt = Time.realtimeSinceStartup;
            Debug.Log("[Steam] connecting to host " + TargetSteamId);
            toHost = SteamNetworkingSockets.ConnectP2P(ref who, 0, 0, null);
            return toHost != HSteamNetConnection.Invalid;
        }

        private void Begin()
        {
            events.Clear();
            peers.Clear();
            connected.Clear();
            partial.Clear();
            statusChanged ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);
            running = true;
        }

        private void OnStatus(SteamNetConnectionStatusChangedCallback_t s)
        {
            if (!running) return;
            var conn = s.m_hConn;
            uint id = conn.m_HSteamNetConnection;
            switch (s.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    // Someone knocking on our listen socket: let them in (the build and party size are checked by
                    // the launcher's connection approval, which can send a reason before it closes them).
                    if (server && s.m_info.m_hListenSocket == listen)
                    {
                        if (SteamNetworkingSockets.AcceptConnection(conn) == EResult.k_EResultOK)
                            SteamNetworkingSockets.SetConnectionPollGroup(conn, group);
                        else SteamNetworkingSockets.CloseConnection(conn, 0, "accept failed", false);
                    }
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FindingRoute:
                    Debug.Log("[Steam] connection " + id + " finding a route (" + (Time.realtimeSinceStartup - connectStartedAt).ToString("F1") + " s)");
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    // Direct or through Valve's relays: worth knowing when a friend reports lag.
                    bool relayed = (s.m_info.m_nFlags & Constants.k_nSteamNetworkConnectionInfoFlags_Relayed) != 0;
                    Debug.Log("[Steam] connection " + id + " connected " + (relayed ? "through a relay" : "directly") + (server ? "" : " after " + (Time.realtimeSinceStartup - connectStartedAt).ToString("F1") + " s") + ": " + s.m_info.m_szConnectionDescription);
                    MarkConnected(conn);
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    Debug.Log("[Steam] connection " + id + " closed (" + s.m_info.m_eState + "): reason " + s.m_info.m_eEndReason + " " + s.m_info.m_szEndDebug);
                    if (!server && conn == toHost) LastProblem = Explain(s.m_info.m_eEndReason, connected.Contains(id));
                    SteamNetworkingSockets.CloseConnection(conn, 0, null, false);
                    bool was = connected.Remove(id);
                    if (server) { if (was) { peers.Remove(id); Queue(NetworkEvent.Disconnect, id); } }
                    // A guest whose host vanished, or who never got through: either way NGO hears "disconnected".
                    else if (conn == toHost) { toHost = HSteamNetConnection.Invalid; Queue(NetworkEvent.Disconnect, ServerClientId); }
                    break;
            }
        }

        /// <summary>Steam's end reason, as something a player can act on.</summary>
        public static string Explain(int reason, bool wasIn)
        {
            if (reason >= 1000 && reason < 3000) return "";
            switch (reason)
            {
                case 3001: return "Steam is in offline mode - go online in Steam and try again.";
                case 3002: case 5005: case 5006: return "Steam could not reach its relay network - check your internet connection, then try again.";
                case 3004: return "Steam's network settings could not be loaded - restart Steam and try again.";
                case 3006: case 4007: case 5008: case 5009:
                    return "Steam could not open a route between you two (a strict router or firewall). Try again in a minute; if it keeps failing, use JOIN BY ADDRESS over a VPN such as Tailscale.";
                case 4001: case 5003:
                    return wasIn ? "The connection to the host timed out (their game may have frozen or their internet dropped)." : "The host did not answer. Is their party still open? Ask them to press PLAY again and re-invite you.";
                case 4006: return "Different game version - you both need the same zip.";
                case 5010: return "The host's party is not running any more.";
            }
            return wasIn ? "Lost the connection (Steam reason " + reason + ")." : "Could not connect (Steam reason " + reason + "). Try again; send both Player.log files if it keeps happening.";
        }

        private void MarkConnected(HSteamNetConnection conn)
        {
            uint id = conn.m_HSteamNetConnection;
            if (!connected.Add(id)) return;
            if (server) { peers[id] = conn; Queue(NetworkEvent.Connect, id); }
            else Queue(NetworkEvent.Connect, ServerClientId);
        }

        private void Queue(NetworkEvent kind, ulong client, ArraySegment<byte> data = default) =>
            events.Enqueue(new Pending { Kind = kind, Client = client, Data = data, At = Time.realtimeSinceStartup });

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            if (running && events.Count == 0) Receive();
            if (events.Count == 0)
            {
                clientId = 0; payload = default; receiveTime = Time.realtimeSinceStartup;
                return NetworkEvent.Nothing;
            }
            var e = events.Dequeue();
            clientId = e.Client; payload = e.Data; receiveTime = e.At;
            return e.Kind;
        }

        private void Receive()
        {
            int n = server
                ? (group != HSteamNetPollGroup.Invalid ? SteamNetworkingSockets.ReceiveMessagesOnPollGroup(group, inbox, inbox.Length) : 0)
                : (toHost != HSteamNetConnection.Invalid ? SteamNetworkingSockets.ReceiveMessagesOnConnection(toHost, inbox, inbox.Length) : 0);
            for (int i = 0; i < n; i++)
            {
                var msg = SteamNetworkingMessage_t.FromIntPtr(inbox[i]);
                uint from = msg.m_conn.m_HSteamNetConnection;
                // A message can beat its own "connected" callback by a frame: count the sender in first.
                if (!connected.Contains(from)) MarkConnected(msg.m_conn);
                if (msg.m_cbSize >= 1)
                {
                    byte frame = Marshal.ReadByte(msg.m_pData);
                    int size = msg.m_cbSize - 1;
                    if (frame == Whole)
                    {
                        var bytes = new byte[size];
                        Marshal.Copy(msg.m_pData + 1, bytes, 0, size);
                        Queue(NetworkEvent.Data, server ? from : ServerClientId, new ArraySegment<byte>(bytes));
                    }
                    else
                    {
                        if (!partial.TryGetValue(from, out var buf)) partial[from] = buf = new System.IO.MemoryStream();
                        var bytes = new byte[size];
                        Marshal.Copy(msg.m_pData + 1, bytes, 0, size);
                        buf.Write(bytes, 0, size);
                        if (frame == Last)
                        {
                            partial.Remove(from);
                            Queue(NetworkEvent.Data, server ? from : ServerClientId, new ArraySegment<byte>(buf.ToArray()));
                        }
                    }
                }
                SteamNetworkingMessage_t.Release(inbox[i]);
            }
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            HSteamNetConnection conn;
            if (server) { if (!peers.TryGetValue(clientId, out conn)) return; }
            else conn = toHost;
            if (conn == HSteamNetConnection.Invalid || payload.Count == 0) return;
            bool unreliable = networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced;
            int flags = unreliable
                ? Constants.k_nSteamNetworkingSend_Unreliable | Constants.k_nSteamNetworkingSend_NoNagle
                : Constants.k_nSteamNetworkingSend_Reliable | Constants.k_nSteamNetworkingSend_NoNagle;
            LargestSent = Mathf.Max(LargestSent, payload.Count);
            if (payload.Count <= ChunkSize || unreliable) { SendFrame(conn, Whole, payload.Array, payload.Offset, payload.Count, flags, networkDelivery); return; }
            // Too big for one Steam message: reliable chunks, reassembled on the other side.
            for (int off = 0; off < payload.Count; off += ChunkSize)
            {
                int len = Mathf.Min(ChunkSize, payload.Count - off);
                SendFrame(conn, off + len >= payload.Count ? Last : Part, payload.Array, payload.Offset + off, len, flags, networkDelivery);
            }
        }

        private void SendFrame(HSteamNetConnection conn, byte frame, byte[] data, int offset, int count, int flags, NetworkDelivery networkDelivery)
        {
            if (count + 1 > sendCapacity)
            {
                if (sendBuffer != IntPtr.Zero) Marshal.FreeHGlobal(sendBuffer);
                sendCapacity = Mathf.NextPowerOfTwo(count + 1);
                sendBuffer = Marshal.AllocHGlobal(sendCapacity);
            }
            Marshal.WriteByte(sendBuffer, frame);
            Marshal.Copy(data, offset, sendBuffer + 1, count);
            var sent = SteamNetworkingSockets.SendMessageToConnection(conn, sendBuffer, (uint)(count + 1), flags, out _);
            // A dropped reliable message would desync the guest without a word: say so in the log (a few a second at most).
            if (sent != EResult.k_EResultOK)
            {
                sendFailures++;
                if (Time.realtimeSinceStartup >= nextSendWarn)
                {
                    nextSendWarn = Time.realtimeSinceStartup + 0.5f;
                    Debug.LogWarning("[Steam] send failed: " + sent + " (" + count + " bytes, " + networkDelivery + ", " + sendFailures + " failures so far)");
                }
            }
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (!peers.TryGetValue(clientId, out var conn)) return;
            // Linger so a refusal ("different build", "party full") reaches them before the line drops.
            SteamNetworkingSockets.CloseConnection(conn, 0, "removed by host", true);
            peers.Remove(clientId);
            connected.Remove((uint)clientId);
        }

        public override void DisconnectLocalClient()
        {
            if (toHost != HSteamNetConnection.Invalid) SteamNetworkingSockets.CloseConnection(toHost, 0, "left", true);
            toHost = HSteamNetConnection.Invalid;
            connected.Clear();
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            var conn = server ? (peers.TryGetValue(clientId, out var c) ? c : HSteamNetConnection.Invalid) : toHost;
            if (conn == HSteamNetConnection.Invalid) return 0;
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
            return SteamNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref lanes) == EResult.k_EResultOK ? (ulong)Mathf.Max(0, status.m_nPing) : 0;
        }

        public override void Shutdown()
        {
            running = false;
            if (SteamPlatform.Ready)
            {
                foreach (var conn in peers.Values) SteamNetworkingSockets.CloseConnection(conn, 0, "host closed the party", true);
                if (toHost != HSteamNetConnection.Invalid) SteamNetworkingSockets.CloseConnection(toHost, 0, "left", true);
                if (listen != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listen);
                if (group != HSteamNetPollGroup.Invalid) SteamNetworkingSockets.DestroyPollGroup(group);
            }
            peers.Clear();
            connected.Clear();
            events.Clear();
            partial.Clear();
            listen = HSteamListenSocket.Invalid;
            group = HSteamNetPollGroup.Invalid;
            toHost = HSteamNetConnection.Invalid;
            if (sendBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(sendBuffer); sendBuffer = IntPtr.Zero; sendCapacity = 0; }
        }

        private void OnDestroy()
        {
            Shutdown();
            statusChanged?.Dispose();
            statusChanged = null;
        }
    }
}
