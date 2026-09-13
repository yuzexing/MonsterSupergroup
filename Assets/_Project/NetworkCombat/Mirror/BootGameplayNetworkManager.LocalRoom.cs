using System;
using System.Linq;
using System.Net.Sockets;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum LocalPreparationOperation : byte { None, Creating, Joining }

    public sealed partial class BootGameplayNetworkManager
    {
        public static bool LocalPreparationAvailable => Application.isEditor || Debug.isDebugBuild;
        public LocalPreparationOperation LocalRoomOperation { get; private set; }
        public bool IsLocalRoomConnecting => LocalRoomOperation != LocalPreparationOperation.None;
        public ushort LocalRoomPort { get; private set; } = NetworkBackendBootstrap.DefaultKcpPort;
        private double localRoomDeadline;
        private NetworkConnectionToServer localRoomConnection;
        public bool IsKcpPreparationRoom => RoomSnapshot.Online &&
            GetComponent<NetworkBackendBootstrap>()?.Selection.Backend == NetworkBackendKind.Kcp;
        public bool CanStartLocalRoom => LocalPreparationAvailable && !IsLocalRoomConnecting &&
            !NetworkServer.active && !NetworkClient.active && mode == NetworkManagerMode.Offline &&
            !IsLeavingRoom && !IsGameplayLoaded && !IsGameplayTransitioning && !HasSteamRoomOperation;
        private bool HasSteamRoomOperation
        {
            get
            {
                var steam = GetComponent<SteamLobbyService>();
                return awaitingSteamRoom || (steam != null && (steam.CurrentLobbyId != 0 ||
                    steam.State == SteamLobbyState.Creating || steam.State == SteamLobbyState.Joining ||
                    steam.State == SteamLobbyState.ConnectingClient || steam.State == SteamLobbyState.Leaving));
            }
        }

        public bool TryCreateLocalPreparationRoom(ushort port, out string error) => StartLocalRoom(port, true, out error);
        public bool TryJoinLocalPreparationRoom(ushort port, out string error) => StartLocalRoom(port, false, out error);

        private bool StartLocalRoom(ushort port, bool host, out string error)
        {
            error = null;
            if (!LocalPreparationAvailable) { error = "本地联机入口仅在开发环境可用。"; return false; }
            if (port == 0) { error = "端口必须是 1 至 65535 的整数。"; return false; }
            if (!CanStartLocalRoom) { error = "已有连接操作或会话尚未清理，请稍候。"; return false; }
            var backend = GetComponent<NetworkBackendBootstrap>();
            if (backend == null) { error = "本地网络组件未就绪。"; return false; }
            if (!backend.TryPrepareKcp(NetworkBackendBootstrap.DefaultKcpAddress, port, false, out error)) return false;
            UsePreparationRoom = true;
            LocalRoomPort = port;
            LocalRoomOperation = host ? LocalPreparationOperation.Creating : LocalPreparationOperation.Joining;
            localRoomDeadline = Time.realtimeSinceStartupAsDouble + 15;
            ShowMenuNotice(string.Empty);
            try
            {
                if (host) { NetworkServer.listen = true; StartHost(); }
                else StartClient();
                localRoomConnection = NetworkClient.connection;
                if (!NetworkClient.active || (host && (!NetworkServer.active || !transport.ServerActive())))
                    throw new InvalidOperationException("KCP did not start.");
                Debug.Log($"[LocalRoom] operation={LocalRoomOperation} endpoint=127.0.0.1:{port}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[LocalRoom] KCP startup failed: " + exception);
                error = exception is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? "端口已被占用，请加入已有主机或更换端口。" : "ui.connection.transport_error";
                // Mirror sets Host mode before binding the UDP socket. Complete the already
                // initialized server without a listener so its public StopServer can undo setup.
                if (mode == NetworkManagerMode.Host && !NetworkServer.active)
                {
                    NetworkServer.listen = false;
                    NetworkServer.Listen(maxConnections);
                    StopServer();
                }
                backend.ShutdownActiveTransport();
                FailLocalRoom(error);
                return false;
            }
        }

        private void UpdateLocalRoomOperation()
        {
            if (!IsLocalRoomConnecting) return;
            if (localRoomConnection != null && !ReferenceEquals(localRoomConnection, NetworkClient.connection))
            { FailLocalRoom(string.IsNullOrEmpty(MenuNotice) ? "本地连接已断开，请重试。" : MenuNotice); return; }
            if (NetworkClient.isConnected && RoomSnapshot.SelfId != 0 && RoomSnapshot.Members != null &&
                RoomSnapshot.Members.Any(m => m.ParticipantId == RoomSnapshot.SelfId))
            {
                Debug.Log($"[LocalRoom] ready endpoint=127.0.0.1:{LocalRoomPort} participant={RoomSnapshot.SelfId} phase={RoomSnapshot.Phase}");
                ClearLocalRoomOperation(); ShowMenuNotice(string.Empty); return;
            }
            if (Time.realtimeSinceStartupAsDouble >= localRoomDeadline)
                FailLocalRoom(string.IsNullOrEmpty(MenuNotice) ? "本地连接超时，请确认主机已创建且端口一致。" : MenuNotice);
        }

        public void CancelLocalRoomConnection()
        {
            if (!IsLocalRoomConnecting) return;
            ClearLocalRoomOperation();
            ShowMenuNotice("已取消本地连接。");
            StartCoroutine(LeaveRoomRoutine(false));
        }
        private void FailLocalRoom(string error)
        {
            ClearLocalRoomOperation(); ShowMenuNotice(error);
            StartCoroutine(LeaveRoomRoutine(false));
        }
        private void ClearLocalRoomOperation()
        {
            LocalRoomOperation = LocalPreparationOperation.None;
            localRoomConnection = null; localRoomDeadline = 0;
        }
    }
}
