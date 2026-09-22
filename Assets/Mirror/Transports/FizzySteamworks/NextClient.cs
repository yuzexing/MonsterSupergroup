#if !DISABLESTEAMWORKS
using Steamworks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public class NextClient : NextCommon, IClient
    {
        public bool Connected { get; private set; }
        public bool Error { get; private set; }

        private TimeSpan ConnectionTimeout;

        private event Action<ArraySegment<byte>, int> OnReceivedData;
        private event Action OnConnected;
        private event Action OnDisconnected;
        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;

        private CancellationTokenSource cancelToken;
        private TaskCompletionSource<Task> connectedComplete;
        private CSteamID hostSteamID = CSteamID.Nil;
        private HSteamNetConnection HostConnection;
        private readonly Queue<(ArraySegment<byte> Data, int Channel)> BufferedData = new Queue<(ArraySegment<byte>, int)>();

        private NextClient(FizzySteamworks transport)
        {
            ConnectionTimeout = TimeSpan.FromSeconds(Math.Max(1, transport.Timeout));
        }

        public static NextClient CreateClient(FizzySteamworks transport, string host)
        {
            NextClient c = new NextClient(transport);

            c.OnConnected += () => transport.OnClientConnected.Invoke();
            c.OnDisconnected += () => transport.OnClientDisconnected.Invoke();
            c.OnReceivedData += (data, ch) => transport.OnClientDataReceived.Invoke(data, ch);

            try
            {
#if UNITY_SERVER
                SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
#else
                SteamNetworkingUtils.InitRelayNetworkAccess();
#endif
                c.Connect(host);
            }
            catch (FormatException)
            {
                Debug.LogError($"Connection string was not in the right format. Did you enter a SteamId?");
                c.Error = true;
                c.OnConnectionFailed();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected exception: {ex.Message}");
                c.Error = true;
                c.OnConnectionFailed();
            }

            return c;
        }

        private async void Connect(string host)
        {
            cancelToken = new CancellationTokenSource();
            c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            try
            {
                hostSteamID = new CSteamID(UInt64.Parse(host));
                connectedComplete = new TaskCompletionSource<Task>();
                OnConnected += SetConnectedComplete;

                SteamNetworkingIdentity smi = new SteamNetworkingIdentity();
                smi.SetSteamID(hostSteamID);

                SteamNetworkingConfigValue_t[] options = new SteamNetworkingConfigValue_t[] { };
                HostConnection = SteamNetworkingSockets.ConnectP2P(ref smi, 0, options.Length, options);

                Task connectedCompleteTask = connectedComplete.Task;
                Task timeOutTask = Task.Delay(ConnectionTimeout, cancelToken.Token);

                if (await Task.WhenAny(connectedCompleteTask, timeOutTask) != connectedCompleteTask)
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        Debug.LogError($"The connection attempt was cancelled.");
                    }
                    else if (timeOutTask.IsCompleted)
                    {
                        Debug.LogError($"Connection to {host} timed out.");
                    }

                    OnConnected -= SetConnectedComplete;
                    OnConnectionFailed();
                }

                OnConnected -= SetConnectedComplete;
            }
            catch (FormatException)
            {
                Debug.LogError($"Connection string was not in the right format. Did you enter a SteamId?");
                Error = true;
                OnConnectionFailed();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected exception: {ex.Message}");
                Error = true;
                OnConnectionFailed();
            }
            finally
            {
                if (Error)
                {
                    Debug.LogError("Connection failed.");
                    OnConnectionFailed();
                }
            }
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            ulong clientSteamID = param.m_info.m_identityRemote.GetSteamID64();
            if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            {
                Connected = true;
                OnConnected.Invoke();
                Debug.Log("Connection established.");

                if (BufferedData.Count > 0)
                {
                    Debug.Log($"{BufferedData.Count} received before connection was established. Processing now.");
                    while (BufferedData.Count > 0)
                    {
                        var message = BufferedData.Dequeue();
                        Deliver(message.Data, message.Channel);
                    }
                }
            }
            else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer || param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                Debug.Log($"Connection was closed by peer, {param.m_info.m_szEndDebug}");
                Disconnect();
            }
            else
            {
                Debug.Log($"Connection state changed: {param.m_info.m_eState.ToString()} - {param.m_info.m_szEndDebug}");
            }
        }

        public void Disconnect()
        {
            cancelToken?.Cancel();
            Dispose();

            if (Connected)
            {
                InternalDisconnect();
            }

            if (HostConnection.m_HSteamNetConnection != 0)
            {
                Debug.Log("Sending Disconnect message");
                SteamNetworkingSockets.CloseConnection(HostConnection, 0, "Graceful disconnect", false);
                HostConnection.m_HSteamNetConnection = 0;
            }
        }

        protected void Dispose()
        {
            while (BufferedData.Count > 0) ReturnMessage(BufferedData.Dequeue().Data);
            if (c_onConnectionChange != null)
            {
                c_onConnectionChange.Dispose();
                c_onConnectionChange = null;
            }
        }

        private void InternalDisconnect()
        {
            Connected = false;
            OnDisconnected.Invoke();
            Debug.Log("Disconnected.");
            SteamNetworkingSockets.CloseConnection(HostConnection, 0, "Disconnected", false);
        }

        public void ReceiveData()
        {
            if (HostConnection.m_HSteamNetConnection == 0) return;
            IntPtr[] ptrs = messagePointers;
            int messageCount;

            if ((messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(HostConnection, ptrs, MAX_MESSAGES)) > 0)
            {
                try
                {
                    for (int i = 0; i < messageCount; i++)
                    {
                        // A delivery callback may disconnect and dispose the deferred queue.
                        if (HostConnection.m_HSteamNetConnection == 0) return;
                        IntPtr pointer = ptrs[i]; ptrs[i] = IntPtr.Zero;
                        (ArraySegment<byte> data, int ch) = ProcessMessage(pointer);
                        if (data.Array == null) continue;
                        if (Connected)
                        {
                            Deliver(data, ch);
                        }
                        else
                        {
                            BufferedData.Enqueue((data, ch));
                        }
                    }
                }
                finally { ReleasePendingMessages(messageCount); }
            }
        }

        public void Send(byte[] data, int channelId)
            => Send(new ArraySegment<byte>(data), channelId);

        private void Deliver(ArraySegment<byte> data, int channel)
        {
            try { OnReceivedData?.Invoke(data, channel); }
            finally { ReturnMessage(data); }
        }

        public void ReadConnectionDiagnostics(List<SteamConnectionSample> samples)
        {
            if (Connected && SteamTransportDiagnostics.TrySample(HostConnection, 0, out var sample)) samples.Add(sample);
        }

        public void Send(ArraySegment<byte> data, int channelId)
        {
            try
            {
                EResult res = SendSocket(HostConnection, data, channelId);

                if (res == EResult.k_EResultNoConnection || res == EResult.k_EResultInvalidParam)
                {
                    Debug.Log($"Connection to server was lost.");
                    InternalDisconnect();
                }
                else if (res != EResult.k_EResultOK)
                {
                    Debug.LogError($"Could not send: {res.ToString()}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"SteamNetworking exception during Send: {ex.Message}");
                InternalDisconnect();
            }
        }

        private void SetConnectedComplete() => connectedComplete.SetResult(connectedComplete.Task);
        private void OnConnectionFailed() => OnDisconnected.Invoke();
        public void FlushData()
        {
            SteamNetworkingSockets.FlushMessagesOnConnection(HostConnection);
        }
    }
}
#endif // !DISABLESTEAMWORKS
