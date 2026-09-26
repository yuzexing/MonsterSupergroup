#if !DISABLESTEAMWORKS
using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public class NextServer : NextCommon, IServer
    {
        private event Action<int,string> OnConnectedWithAddress;
        private event Action<int, ArraySegment<byte>, int> OnReceivedData;
        private readonly List<HSteamNetConnection> receiveConnections = new List<HSteamNetConnection>();
        private readonly List<HSteamNetConnection> flushConnections = new List<HSteamNetConnection>();
        private event Action<int> OnDisconnected;
        private event Action<int, TransportError, string> OnReceivedError;

        private BidirectionalDictionary<HSteamNetConnection, int> connToMirrorID;
        private BidirectionalDictionary<CSteamID, int> steamIDToMirrorID;
        private int maxConnections;
        private int nextConnectionID;

        private HSteamListenSocket listenSocket;

        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;

        private static NextServer server;
        private NextServer(int maxConnections)
        {
            this.maxConnections = maxConnections;
            connToMirrorID = new BidirectionalDictionary<HSteamNetConnection, int>();
            steamIDToMirrorID = new BidirectionalDictionary<CSteamID, int>();
            nextConnectionID = 1;
#if UNITY_SERVER
            c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.CreateGameServer(OnConnectionStatusChanged);
#else
            c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
#endif
        }

        public static NextServer CreateServer(FizzySteamworks transport, int maxConnections)
        {
            server = new NextServer(maxConnections);

            server.OnConnectedWithAddress += (id,addres) => transport.OnServerConnectedWithAddress.Invoke(id,addres);
            server.OnDisconnected += (id) => transport.OnServerDisconnected.Invoke(id);
            server.OnReceivedData += (id, data, ch) => transport.OnServerDataReceived.Invoke(id, data, ch);
            server.OnReceivedError += (id, error, reason) => transport.OnServerError.Invoke(id, error, reason);

            try
            {
#if UNITY_SERVER
                SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
#else
                SteamNetworkingUtils.InitRelayNetworkAccess();
#endif
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            server.Host();

            return server;
        }

        private void Host()
        {
            SteamNetworkingConfigValue_t[] options = new SteamNetworkingConfigValue_t[] { };
#if UNITY_SERVER
            listenSocket = SteamGameServerNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
#else
            listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
#endif
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            SteamTransportDiagnostics.RecordConnection(param.m_hConn.m_HSteamNetConnection,
                connToMirrorID.TryGetValue(param.m_hConn, out int knownConnection) ? knownConnection : -1,
                "Server", param.m_eOldState.ToString(), param.m_info.m_eState.ToString(), param.m_info.m_eEndReason, param.m_info.m_identityRemote.GetSteamID64().ToString());
            ulong clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                if (connToMirrorID.Count >= maxConnections)
                {
                    Debug.Log($"Incoming connection {clientSteamID} would exceed max connection count. Rejecting.");
#if UNITY_SERVER
                    SteamGameServerNetworkingSockets.CloseConnection(param.m_hConn, 0, "Max Connection Count", false);
#else
                    SteamNetworkingSockets.CloseConnection(param.m_hConn, 0, "Max Connection Count", false);
#endif
                    return;
                }

                EResult res;

#if UNITY_SERVER
                if ((res = SteamGameServerNetworkingSockets.AcceptConnection(param.m_hConn)) == EResult.k_EResultOK)
#else
                if ((res = SteamNetworkingSockets.AcceptConnection(param.m_hConn)) == EResult.k_EResultOK)
#endif
                {
                    Debug.Log($"Accepting connection {clientSteamID}");
                }
                else
                {
                    Debug.Log($"Connection {clientSteamID} could not be accepted: {res}");
                }
            }
            else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                int connectionId = nextConnectionID++;
                SteamEvidenceLane.Connect(connectionId, param.m_hConn);
                connToMirrorID.Add(param.m_hConn, connectionId);
                SteamTransportDiagnostics.RecordConnection(param.m_hConn.m_HSteamNetConnection, connectionId, "Server", "Connected", "MirrorConnectionAssigned", 0);
                steamIDToMirrorID.Add(param.m_info.m_identityRemote.GetSteamID(), connectionId);
                OnConnectedWithAddress?.Invoke(connectionId,server.ServerGetClientAddress(connectionId));
                Debug.Log($"Client with SteamID {clientSteamID} connected. Assigning connection id {connectionId}");
            }
            else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer || param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                if (connToMirrorID.TryGetValue(param.m_hConn, out int connId))
                {
                    InternalDisconnect(connId, param.m_hConn);
                }
            }
            else
            {
                Debug.Log($"Connection {clientSteamID} state changed: {param.m_info.m_eState}");
            }
        }

        private void InternalDisconnect(int connId, HSteamNetConnection socket)
        {
            SteamTransportDiagnostics.RecordConnection(socket.m_HSteamNetConnection, connId, "Server", "Active", "ClosedLocally", 0);
            SteamEvidenceLane.Disconnect(connId);
            OnDisconnected?.Invoke(connId);
#if UNITY_SERVER
            SteamGameServerNetworkingSockets.CloseConnection(socket, 0, "Graceful disconnect", false);
#else
            SteamNetworkingSockets.CloseConnection(socket, 0, "Graceful disconnect", false);
#endif
            connToMirrorID.Remove(connId);
            steamIDToMirrorID.Remove(connId);
            Debug.Log($"Client with ConnectionID {connId} disconnected.");
        }

        public void Disconnect(int connectionId)
        {
            if (connToMirrorID.TryGetValue(connectionId, out HSteamNetConnection conn))
            {
                SteamTransportDiagnostics.RecordConnection(conn.m_HSteamNetConnection, connectionId, "Server", "Active", "ClosedLocally", 0);
                Debug.Log($"Connection id {connectionId} disconnected.");
#if UNITY_SERVER
                SteamGameServerNetworkingSockets.CloseConnection(conn, 0, "Disconnected by server", false);
#else
                SteamNetworkingSockets.CloseConnection(conn, 0, "Disconnected by server", false);
#endif
                steamIDToMirrorID.Remove(connectionId);
                connToMirrorID.Remove(connectionId);
                SteamEvidenceLane.Disconnect(connectionId);
                OnDisconnected?.Invoke(connectionId);
            }
            else
            {
                Debug.LogWarning("Trying to disconnect unknown connection id: " + connectionId);
            }
        }

        public void FlushData()
        {
            flushConnections.Clear(); flushConnections.AddRange(connToMirrorID.FirstTypes);
            foreach (HSteamNetConnection conn in flushConnections)
            {
#if UNITY_SERVER
                SteamGameServerNetworkingSockets.FlushMessagesOnConnection(conn);
#else
                SteamNetworkingSockets.FlushMessagesOnConnection(conn);
#endif
            }
        }

        public void ReceiveData()
        {
            receiveConnections.Clear(); receiveConnections.AddRange(connToMirrorID.FirstTypes);
            foreach (HSteamNetConnection conn in receiveConnections)
            {
                if (connToMirrorID.TryGetValue(conn, out int connId))
                {
                    IntPtr[] ptrs = messagePointers;
                    int messageCount = 0;
                    long pollStart = SteamTransportDiagnostics.BeginReceivePoll();
                    try
                    {

#if UNITY_SERVER
                    if ((messageCount = SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(conn, ptrs, MAX_MESSAGES)) > 0)
#else
                    if ((messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(conn, ptrs, MAX_MESSAGES)) > 0)
#endif
                    {
                        try
                        {
                            for (int i = 0; i < messageCount; i++)
                            {
                                if (!connToMirrorID.TryGetValue(conn, out _)) break;
                                IntPtr pointer = ptrs[i]; ptrs[i] = IntPtr.Zero;
                                (ArraySegment<byte> data, int ch) = ProcessMessage(pointer);
                                if (data.Array == null) continue;
                                try { if (ch == 2) SteamEvidenceLane.Deliver(connId, data); else OnReceivedData?.Invoke(connId, data, ch); }
                                finally { ReturnMessage(data); }
                            }
                        }
                        finally { ReleasePendingMessages(messageCount); }
                    }
                    }
                    finally { SteamTransportDiagnostics.EndReceivePoll(conn.m_HSteamNetConnection, messageCount, MAX_MESSAGES, pollStart); }
                }
            }
        }

        public void Send(int connectionId, byte[] data, int channelId)
            => Send(connectionId, new ArraySegment<byte>(data), channelId);

        public void ReadConnectionDiagnostics(List<SteamConnectionSample> samples, List<SteamConnectionInvestigationSample> investigation = null)
        {
            foreach (var conn in connToMirrorID.FirstTypes)
                if (connToMirrorID.TryGetValue(conn, out int id) && SteamTransportDiagnostics.TrySample(conn, id, out var sample, investigation))
                    samples.Add(sample);
        }

        public void Send(int connectionId, ArraySegment<byte> data, int channelId)
        {
            if (connToMirrorID.TryGetValue(connectionId, out HSteamNetConnection conn))
            {
                EResult res = SendSocket(conn, data, channelId);

                if (res == EResult.k_EResultNoConnection || res == EResult.k_EResultInvalidParam)
                {
                    Debug.Log($"Connection to {connectionId} was lost.");
                    InternalDisconnect(connectionId, conn);
                }
            }
            else
            {
                Debug.LogError("Trying to send on an unknown connection: " + connectionId);
                OnReceivedError?.Invoke(connectionId, TransportError.Unexpected, "ERROR Unknown Connection");
            }
        }

        public string ServerGetClientAddress(int connectionId)
        {
            if (steamIDToMirrorID.TryGetValue(connectionId, out CSteamID steamId))
            {
                return steamId.ToString();
            }
            else
            {
                Debug.LogError("Trying to get info on an unknown connection: " + connectionId);
                OnReceivedError?.Invoke(connectionId, TransportError.Unexpected, "ERROR Unknown Connection");
                return string.Empty;
            }
        }

        public void Shutdown()
        {
            foreach (var connection in connToMirrorID.FirstTypes) if (connToMirrorID.TryGetValue(connection, out int id))
            {
                SteamTransportDiagnostics.RecordConnection(connection.m_HSteamNetConnection, id, "Server", "Active", "Shutdown", 0);
                SteamEvidenceLane.Disconnect(id);
            }
#if UNITY_SERVER
            SteamGameServerNetworkingSockets.CloseListenSocket(listenSocket);
#else
            SteamNetworkingSockets.CloseListenSocket(listenSocket);
#endif

            c_onConnectionChange?.Dispose();
            c_onConnectionChange = null;
        }
    }
}
#endif // !DISABLESTEAMWORKS
