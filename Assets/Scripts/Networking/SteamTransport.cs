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
            toHost = SteamNetworkingSockets.ConnectP2P(ref who, 0, 0, null);
            return toHost != HSteamNetConnection.Invalid;
        }

        private void Begin()
        {
            events.Clear();
            peers.Clear();
            connected.Clear();
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
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    MarkConnected(conn);
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    Debug.Log("[Steam] connection " + id + " closed: " + s.m_info.m_eEndReason + " " + s.m_info.m_szEndDebug);
                    SteamNetworkingSockets.CloseConnection(conn, 0, null, false);
                    bool was = connected.Remove(id);
                    if (server) { if (was) { peers.Remove(id); Queue(NetworkEvent.Disconnect, id); } }
                    // A guest whose host vanished, or who never got through: either way NGO hears "disconnected".
                    else if (conn == toHost) { toHost = HSteamNetConnection.Invalid; Queue(NetworkEvent.Disconnect, ServerClientId); }
                    break;
            }
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
                var bytes = new byte[msg.m_cbSize];
                Marshal.Copy(msg.m_pData, bytes, 0, msg.m_cbSize);
                // A message can beat its own "connected" callback by a frame: count the sender in first.
                if (!connected.Contains(msg.m_conn.m_HSteamNetConnection)) MarkConnected(msg.m_conn);
                Queue(NetworkEvent.Data, server ? msg.m_conn.m_HSteamNetConnection : ServerClientId, new ArraySegment<byte>(bytes));
                SteamNetworkingMessage_t.Release(inbox[i]);
            }
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            HSteamNetConnection conn;
            if (server) { if (!peers.TryGetValue(clientId, out conn)) return; }
            else conn = toHost;
            if (conn == HSteamNetConnection.Invalid || payload.Count == 0) return;
            if (payload.Count > sendCapacity)
            {
                if (sendBuffer != IntPtr.Zero) Marshal.FreeHGlobal(sendBuffer);
                sendCapacity = Mathf.NextPowerOfTwo(payload.Count);
                sendBuffer = Marshal.AllocHGlobal(sendCapacity);
            }
            Marshal.Copy(payload.Array, payload.Offset, sendBuffer, payload.Count);
            int flags = networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced
                ? Constants.k_nSteamNetworkingSend_Unreliable | Constants.k_nSteamNetworkingSend_NoNagle
                : Constants.k_nSteamNetworkingSend_Reliable | Constants.k_nSteamNetworkingSend_NoNagle;
            SteamNetworkingSockets.SendMessageToConnection(conn, sendBuffer, (uint)payload.Count, flags, out _);
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
