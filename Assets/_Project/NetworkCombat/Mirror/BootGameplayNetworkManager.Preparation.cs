using System;
using System.Collections;
using System.Linq;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class BootGameplayNetworkManager
    {
        public const string MainMenuScene = "Assets/_Project/Scenes/MainMenu.unity";
        public bool UsePreparationRoom { get; private set; } = true;
        public PreparationRoom ServerRoom { get; private set; }
        public PreparationRoomSnapshot RoomSnapshot { get; private set; }
        public string MenuNotice { get; private set; } = string.Empty;
        public bool IsLeavingRoom { get; private set; }
        public bool IsLoadingLocked => UsePreparationRoom &&
            (NetworkServer.active ? ServerRoom?.Phase != PreparationPhase.InGame : RoomSnapshot.Phase != PreparationPhase.InGame);
        public event Action PreparationChanged;
        private double preparationLoadDeadline;
        private uint readyAvatarSent;
        private bool menuLoadStarted;
        private bool roomStopping;
        private bool awaitingSteamRoom;

        private void InitializeMenuFlow()
        {
            var backend = GetComponent<NetworkBackendBootstrap>();
            // Existing process probes and the Unity runner are explicit validation entry points.
            // Interactive builds, including non-Development builds, always use preparation.
            var purpose = backend != null ? backend.Selection.Purpose : NetworkBackendBootstrap.ResolveSelection(
                Environment.GetCommandLineArgs(), Application.isEditor, false, NetworkBackendKind.Steam).Purpose;
            UsePreparationRoom = purpose == NetworkRuntimePurpose.Interactive;
        }

        public void ConfigurePreparationFlow(bool enabled)
        {
            if (NetworkServer.active || NetworkClient.active) throw new InvalidOperationException("Choose the entry flow before connecting.");
            UsePreparationRoom = enabled;
        }

        public override void Start()
        {
            base.Start();
            if (UsePreparationRoom && !GameplayRuntimeEnvironment.IsDedicatedServer) StartCoroutine(EnsureMainMenu());
        }

        public IEnumerator EnsureMainMenu()
        {
            if (menuLoadStarted)
            {
                // Concurrent callers must observe the completed load, not a false ready state.
                while (menuLoadStarted) yield return null;
                yield break;
            }
            menuLoadStarted = true;
            var scene = SceneManager.GetSceneByPath(MainMenuScene);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (!Application.CanStreamedLevelBeLoaded(MainMenuScene)) { menuLoadStarted = false; yield break; }
                yield return SceneManager.LoadSceneAsync(MainMenuScene, LoadSceneMode.Additive);
                scene = SceneManager.GetSceneByPath(MainMenuScene);
            }
            if (scene.IsValid() && scene.isLoaded) PreparationMenuView.Install(scene, this);
            if (bootCamera != null) bootCamera.enabled = false;
            if (bootAudioListener != null) bootAudioListener.enabled = false;
            menuLoadStarted = false;
        }

        public void ShowMenuNotice(string message)
        {
            if (MonsterSupergroup.Builds.BuildCompatibility.TryDecode(message, out _, out _, out _, out _)) { SetConnectionNotice(message); return; }
            if (connectionNotice.Priority > 0) return;
            MenuNotice = message ?? string.Empty;
            PreparationChanged?.Invoke();
        }

        public void CreatePreparationRoom()
        {
            if (NetworkClient.active || NetworkServer.active || IsGameplayTransitioning || IsLeavingRoom || IsLocalRoomConnecting) return;
            BeginConnectionAttempt();
            if (!CheckBuildForConnection()) return;
            var steam = GetComponent<SteamLobbyService>();
            if (steam != null && steam.IsSteamInitialized)
            {
                awaitingSteamRoom = true;
                steam.CreateLobby();
                return;
            }
            if (!TryStartOfflineRoom(out string error)) ShowMenuNotice(error);
        }

        public bool TryStartOfflineRoom(out string error)
        {
            error = null;
            if (!RuntimeBuildAvailable(out error)) return false;
            if (IsGameplayTransitioning || IsLeavingRoom) { error = "正在清理上一次游戏，请稍候。"; return false; }
            var backend = GetComponent<NetworkBackendBootstrap>();
            if (backend == null || !backend.TryPrepareLocalHost(out error)) return false;
            UsePreparationRoom = true;
            NetworkServer.listen = false;
            StartHost();
            return true;
        }

        private void StartPreparationServer()
        {
            roomStopping = false; ResetRunEndFlow();
            // Brief unauthenticated connections need space to receive a full-room reason.
            // The room itself still admits exactly four participants.
            NetworkServer.maxConnections = PreparationRoom.Capacity + 4;
            ServerRoom = new PreparationRoom(Session.RunId, gameplayScene, NetworkServer.listen,
                PreparationMenuCatalog.Load().AllowedWeaponIds);
            NetworkServer.RegisterHandler<RequestSetLoadout>(ReceiveLoadout);
            NetworkServer.RegisterHandler<RequestSetReady>(ReceiveReady);
            NetworkServer.RegisterHandler<RequestStartGame>(ReceiveStart);
            NetworkServer.RegisterHandler<GameplayReady>(ReceiveGameplayReady);
            NetworkServer.RegisterHandler<RequestRunEndAction>(ReceiveRunEndAction);
            NetworkServer.RegisterHandler<RunCleanupReady>(ReceiveCleanupReady);
            Debug.Log($"[Preparation] Open run={Session.RunId} online={NetworkServer.listen}");
        }

        internal bool AdmitPreparation(NetworkConnectionToClient connection, RunParticipant participant, string displayName, out string error)
        {
            error = null;
            if (!UsePreparationRoom) return true;
            if (ServerRoom == null) { error = "房间尚未就绪。"; return false; }
            if (ServerRoom.Phase == PreparationPhase.Loading || ServerRoom.Phase == PreparationPhase.Transitioning) { error = "房间正在加载，请稍后重连。"; return false; }
            if (ServerRoom.Phase == PreparationPhase.InGame || ServerRoom.Phase == PreparationPhase.GameOver) return true; // RunSession already checked the sealed roster.
            return ServerRoom.Join(participant.Id, displayName, connection is LocalConnectionToClient, out error);
        }

        private void RegisterPreparationClient()
        {
            uint noticeAttempt = ConnectionAttempt;
            readyAvatarSent = 0; clientCleanupRound = 0;
            NetworkClient.RegisterHandler<RunCleanupRequest>(ReceiveCleanupRequest);
            NetworkClient.RegisterHandler<PreparationRoomSnapshot>(snapshot =>
            {
                if (noticeAttempt != ConnectionAttempt) return;
                if (!UsePreparationRoom) UsePreparationRoom = true;
                if (snapshot.Round < RoomSnapshot.Round ||
                    (snapshot.Round == RoomSnapshot.Round && RoomSnapshot.Revision > snapshot.Revision)) return;
                if (snapshot.Round > RoomSnapshot.Round) { MenuNotice = string.Empty; readyAvatarSent = 0; }
                RoomSnapshot = snapshot;
                if (snapshot.Phase == PreparationPhase.GameOver) NetworkEnemySimulationWorld.Instance?.StopClientRun();
                PreparationChanged?.Invoke();
            });
            NetworkClient.RegisterHandler<PreparationNotice>(notice =>
            {
                if (noticeAttempt != ConnectionAttempt) return;
                if (notice.Closing) SetConnectionNotice(notice.Reason == "房主已结束会话。" ? "ui.connection.host_closed" : notice.Reason, 80, noticeAttempt);
                else ShowMenuNotice(notice.Reason);
                if (notice.Closing && !NetworkServer.active) StartCoroutine(LeaveRoomRoutine(false));
            });
        }

        public void SetOwnLoadout(uint weaponId)
        {
            ShowMenuNotice(string.Empty);
            if (NetworkClient.isConnected) NetworkClient.Send(new RequestSetLoadout {
                RunId = RoomSnapshot.RunId, CharacterId = PreparationRoom.Character, WeaponId = weaponId });
        }
        public void SetOwnReady(bool ready)
        {
            ShowMenuNotice(string.Empty);
            var self = RoomSnapshot.Members?.FirstOrDefault(m => m.ParticipantId == RoomSnapshot.SelfId) ?? default;
            if (NetworkClient.isConnected) NetworkClient.Send(new RequestSetReady {
                RunId = RoomSnapshot.RunId, LoadoutRevision = self.LoadoutRevision, Ready = ready });
        }
        public void StartPreparedGame()
        {
            ShowMenuNotice(string.Empty);
            if (NetworkClient.isConnected) NetworkClient.Send(new RequestStartGame {
                RunId = RoomSnapshot.RunId, Revision = RoomSnapshot.Revision });
        }

        private bool RequestActor(NetworkConnectionToClient connection, string runId, out ulong id)
        {
            id = 0;
            if (!UsePreparationRoom || ServerRoom == null || runId != Session.RunId ||
                !Session.TryGetConnection(connection.connectionId, out var participant)) return false;
            id = participant.Id; return true;
        }
        private void ReceiveLoadout(NetworkConnectionToClient connection, RequestSetLoadout request)
        {
            if (!RequestActor(connection, request.RunId, out ulong id)) return;
            if (!ServerRoom.SetLoadout(id, request.CharacterId, request.WeaponId, out string error)) Notify(connection, error);
            PublishRoom();
        }
        private void ReceiveReady(NetworkConnectionToClient connection, RequestSetReady request)
        {
            if (!RequestActor(connection, request.RunId, out ulong id)) return;
            if (!ServerRoom.SetReady(id, request.LoadoutRevision, request.Ready, out string error)) Notify(connection, error);
            PublishRoom();
        }
        private void ReceiveStart(NetworkConnectionToClient connection, RequestStartGame request)
        {
            if (!RequestActor(connection, request.RunId, out ulong id)) return;
            var before = ServerRoom.Phase;
            if (!ServerRoom.Start(id, request.Revision, out string error)) { Notify(connection, error); return; }
            if (before != PreparationPhase.Preparing) return;
            LaunchGameplay();
        }

        private void LaunchGameplay()
        {
            Session.SealRoster(ServerRoom.Launch.Members.Select(m => m.ParticipantId));
            preparationLoadDeadline = Time.realtimeSinceStartupAsDouble + 120;
            PublishRoom();
            if (!Application.CanStreamedLevelBeLoaded(gameplayScene))
            { AbortPreparation("战斗场景无法加载，本次开局已取消。"); return; }
            StartCoroutine(ServerLoadGameplay(serverSceneGeneration));
            foreach (var member in NetworkServer.connections.Values.ToArray())
            {
                if (!member.isAuthenticated) { member.Disconnect(); continue; }
                if (member is LocalConnectionToClient) QueuePlayerCreation(member);
                else
                {
                    remoteGameplayLoadRequests.Add(member.connectionId);
                    NetworkServer.SetClientNotReady(member);
                    StartCoroutine(SendGameplaySceneWhenReady(member));
                }
            }
            Debug.Log($"[Preparation] Locked roster and loading run={Session.RunId} members={ServerRoom.Count}");
        }
        private void ReceiveGameplayReady(NetworkConnectionToClient connection, GameplayReady ready)
        {
            if (!RequestActor(connection, ready.RunId, out ulong id) || ServerRoom.Phase != PreparationPhase.Loading ||
                connection.identity == null || connection.identity.netId != ready.AvatarId || !connection.isReady) return;
            var build = connection.identity.GetComponent<PlayerBuildRuntime>();
            var world = NetworkCombatWorld.Instance;
            if (build == null || !build.IsBuildActive || build.InitialWeaponId != ready.WeaponId ||
                ServerRoom.Launch.WeaponFor(id) != ready.WeaponId || world == null ||
                !world.Gateway.Ledger.TryGetState(ready.AvatarId, out _)) return;
            if (ServerRoom.MarkGameplayReady(id)) PublishRoom();
        }
        private void PublishRoom()
        {
            if (ServerRoom == null || !NetworkServer.active) return;
            foreach (var connection in NetworkServer.connections.Values)
                if (connection.isAuthenticated && Session.TryGetConnection(connection.connectionId, out var participant))
                    connection.Send(ServerRoom.Snapshot(participant.Id));
            GetComponent<SteamLobbyService>()?.PublishPreparationPhase(ServerRoom.Phase);
        }
        private static void Notify(NetworkConnectionToClient connection, string error) =>
            connection.Send(new PreparationNotice { Reason = error });

        public override void Update()
        {
            base.Update();
            if (!UsePreparationRoom) return;
            UpdateLocalRoomOperation();
            if (awaitingSteamRoom)
            {
                var steam = GetComponent<SteamLobbyService>();
                if (steam != null && steam.State == SteamLobbyState.Hosting) awaitingSteamRoom = false;
                else if (steam != null && steam.State == SteamLobbyState.Error && !NetworkClient.active &&
                    !NetworkServer.active && !IsGameplayTransitioning && !IsLeavingRoom)
                {
                    awaitingSteamRoom = false;
                    if (TryStartOfflineRoom(out string error)) ShowMenuNotice("Steam 联机暂不可用，已进入本地单人房间。");
                    else ShowMenuNotice(error);
                }
            }
            if (NetworkServer.active && ServerRoom?.Phase == PreparationPhase.Loading && !roomStopping)
            {
                if (Time.realtimeSinceStartupAsDouble >= preparationLoadDeadline)
                    AbortPreparation("加载超过 120 秒，本次开局已取消。请重新创建房间。");
                else if (ServerRoom.AllGameplayReady)
                {
                    if (TryBeginRun(out string error))
                    {
                        ShowMenuNotice(string.Empty);
                        ServerRoom.BeginCombat();
                        PublishRoom();
                        Debug.Log($"[Preparation] Combat started run={Session.RunId}");
                    }
                    else if (MenuNotice != error)
                    {
                        ShowMenuNotice(error);
                        Debug.LogWarning($"[Preparation] Cannot begin combat: {error}", this);
                    }
                }
            }
            UpdateRunEnd();
            TrySendGameplayReady();
        }

        private void TrySendGameplayReady()
        {
            if (!NetworkClient.isConnected || RoomSnapshot.Phase != PreparationPhase.Loading || !IsGameplayLoaded) return;
            if (MonsterSupergroup.Gameplay.Combat.GameplayMapContext.Active is { IsReady: false }) return;
            var avatar = NetworkClient.localPlayer;
            if (avatar == null || !avatar.isOwned || avatar.netId == readyAvatarSent) return;
            var build = avatar.GetComponent<PlayerBuildRuntime>();
            var participant = avatar.GetComponent<NetworkRunParticipant>();
            var bootstrap = avatar.GetComponent<NetworkPlayerBootstrap>();
            var world = NetworkCombatWorld.Instance;
            if (participant == null || participant.RunId != RoomSnapshot.RunId || participant.ParticipantId != RoomSnapshot.SelfId ||
                build == null || !build.IsBuildActive || build.InitialWeaponId != participant.InitialWeaponId ||
                bootstrap == null || !bootstrap.IsLocalOwnerBound ||
                !avatar.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                !avatar.GetComponent<NetworkPlayerDash>().HasOwnerBaseline ||
                !avatar.GetComponent<NetworkPlayerUltimate>().TryReadDebugState(false, out _) ||
                world == null || !world.Replica.TryGetEntity(avatar.netId, out _)) return;
            readyAvatarSent = avatar.netId;
            NetworkClient.Send(new GameplayReady { RunId = participant.RunId, AvatarId = avatar.netId, WeaponId = build.InitialWeaponId });
        }

        private void PreparationDisconnected(NetworkConnectionToClient connection)
        {
            if (!UsePreparationRoom || roomStopping || ServerRoom == null ||
                !Session.TryGetConnection(connection.connectionId, out var participant)) return;
            if (ServerRoom.Phase == PreparationPhase.Loading)
                AbortPreparation("有玩家在加载期间断开连接，本次开局已取消。");
            else if (ServerRoom.Phase == PreparationPhase.Preparing)
            {
                ServerRoom.Leave(participant.Id);
                // Publish next frame, after Mirror has removed the disconnected connection.
                StartCoroutine(PublishAfterDisconnect());
            }
        }
        private IEnumerator PublishAfterDisconnect() { yield return null; PublishRoom(); }
        private void AbortPreparation(string reason)
        {
            if (roomStopping) return;
            ShowMenuNotice(reason);
            StartCoroutine(LeaveRoomRoutine(true));
        }
        public void LeavePreparationRoom()
        {
            GetComponent<SteamLobbyService>()?.CancelInvitationJoin();
            BeginLeavingPreparationRoom();
        }
        internal void BeginLeavingPreparationRoom()
        {
            connectionNotice.LeaveLocally();
            ShowMenuNotice(string.Empty);
            StartCoroutine(LeaveRoomRoutine(true));
        }
        private IEnumerator LeaveRoomRoutine(bool announce)
        {
            if (IsLeavingRoom) yield break;
            ClearLocalRoomOperation();
            awaitingSteamRoom = false;
            IsLeavingRoom = true; roomStopping = true;
            if (announce && NetworkServer.active)
            {
                string reason = string.IsNullOrEmpty(MenuNotice) ? "ui.connection.host_closed" : MenuNotice;
                foreach (var connection in NetworkServer.connections.Values)
                    if (connection.isAuthenticated && !(connection is LocalConnectionToClient))
                        connection.Send(new PreparationNotice { Reason = reason, Closing = true });
                // Give reliable notices a network update before transport shutdown.
                yield return new WaitForSecondsRealtime(0.15f);
            }
            var steam = GetComponent<SteamLobbyService>();
            if (steam != null && (steam.CurrentLobbyId != 0 || steam.State == SteamLobbyState.Creating ||
                steam.State == SteamLobbyState.Joining || steam.State == SteamLobbyState.ConnectingClient)) steam.LeaveAndStop();
            else if (NetworkServer.active && NetworkClient.active) StopHost();
            else if (NetworkClient.active) StopClient();
            else if (NetworkServer.active) StopServer();
            while (NetworkClient.active || NetworkServer.active || IsGameplayTransitioning || IsGameplayLoaded) yield return null;
            GetComponent<NetworkBackendBootstrap>()?.ShutdownActiveTransport();
            RoomSnapshot = default;
            IsLeavingRoom = false;
            PreparationChanged?.Invoke();
        }
        private void StopPreparationServer()
        {
            roomStopping = true;
            NetworkServer.UnregisterHandler<RequestSetLoadout>();
            NetworkServer.UnregisterHandler<RequestSetReady>();
            NetworkServer.UnregisterHandler<RequestStartGame>();
            NetworkServer.UnregisterHandler<GameplayReady>();
            NetworkServer.UnregisterHandler<RequestRunEndAction>();
            NetworkServer.UnregisterHandler<RunCleanupReady>();
            ResetRunEndFlow();
            ServerRoom = null;
        }
        private void StopPreparationClient()
        {
            NetworkClient.UnregisterHandler<PreparationRoomSnapshot>();
            NetworkClient.UnregisterHandler<PreparationNotice>();
            NetworkClient.UnregisterHandler<RunCleanupRequest>();
            clientCleanupRound = 0;
            RoomSnapshot = default; readyAvatarSent = 0;
            PreparationChanged?.Invoke();
        }
        public override void OnClientDisconnect()
        {
            if (!IsLeavingRoom && !connectionNotice.Intentional)
                SetConnectionNotice(connectionNotice.Connected ? "ui.connection.host_lost" : "ui.connection.connect_failed", 50);
            if (IsLocalRoomConnecting)
                FailLocalRoom(string.IsNullOrEmpty(MenuNotice) ? "本地连接已断开，请确认主机与端口。" : MenuNotice);
            if (UsePreparationRoom && !IsLeavingRoom && string.IsNullOrEmpty(MenuNotice))
                ShowMenuNotice("与房主的连接已断开，房主可能已结束会话。");
            base.OnClientDisconnect();
        }
        public override void OnClientError(TransportError error, string reason)
        {
            Debug.LogWarning($"[ConnectionNotice] transport={error} attempt={ConnectionAttempt} detail={reason}");
            if (IsLeavingRoom || connectionNotice.Intentional) return;
            if (IsLocalRoomConnecting)
            {
                SetConnectionNotice("本地连接失败，请确认主机已创建且端口一致。", 10);
            }
            else SetConnectionNotice("ui.connection.transport_error", 10);
            base.OnClientError(error, reason);
        }
        public void QuitFromMenu()
        {
            GetComponent<SteamLobbyService>()?.CancelInvitationJoin();
            StartCoroutine(QuitAfterCleanup());
        }
        private IEnumerator QuitAfterCleanup()
        {
            connectionNotice.LeaveLocally();
            yield return LeaveRoomRoutine(true);
            while (IsLeavingRoom) yield return null;
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
