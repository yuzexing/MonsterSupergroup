using System;
using System.Collections.Generic;
using Mirror;
using Mirror.FizzySteam;
using Steamworks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum SteamLobbyState : byte
    {
        Disabled = 0,
        Initializing = 1,
        Idle = 2,
        Creating = 3,
        Refreshing = 4,
        Joining = 5,
        ConnectingClient = 6,
        Hosting = 7,
        Connected = 8,
        Leaving = 9,
        Error = 10
    }

    [DefaultExecutionOrder(-30000)]
    [DisallowMultipleComponent]
    public sealed class SteamLobbyService : MonoBehaviour
    {
        public const uint DevelopmentAppId = 480u;

        private const int SearchResultLimit = 50;
        private static SteamLobbyService instance;
        private static bool everInitialized;

        [SerializeField] private NetworkBackendBootstrap backendBootstrap;
        [SerializeField] private BootGameplayNetworkManager networkManager;
        [SerializeField] private FizzySteamworks fizzyTransport;

        private readonly List<SteamLobbySummary> lobbies =
            new List<SteamLobbySummary>();

        private CallResult<LobbyCreated_t> lobbyCreatedResult;
        private CallResult<LobbyMatchList_t> lobbyListResult;
        private CallResult<LobbyEnter_t> lobbyEnterResult;
        private Callback<LobbyChatUpdate_t> lobbyChatUpdate;
        private Callback<LobbyDataUpdate_t> lobbyDataUpdate;
        private Callback<LobbyKicked_t> lobbyKicked;
        private Callback<SteamServersDisconnected_t> steamDisconnected;
        private Callback<SteamServersConnected_t> steamConnected;

        private ulong pendingLobbyId;
        private bool ownsSteamApi;
        private bool validationBypass;
        private bool isHostSession;
        private bool networkObserved;
        private bool cleanupInProgress;
        private bool applicationQuitting;
        private bool waitingForClientDisconnect;
        private bool cleanupEndsInError;
        private string cleanupCompletionError;

        public event Action Changed;

        public bool IsSteamInitialized { get; private set; }
        public SteamLobbyState State { get; private set; } =
            SteamLobbyState.Initializing;
        public string LastError { get; private set; } = string.Empty;
        public ulong CurrentLobbyId { get; private set; }
        public ulong HostSteamId64 { get; private set; }
        public IReadOnlyList<SteamLobbySummary> Lobbies => lobbies;
        public bool IsHostSession => isHostSession;
        public bool IsValidationBypass => validationBypass;
        public bool IsSteamBackendSelected => backendBootstrap != null &&
            backendBootstrap.IsInitialized &&
            backendBootstrap.Selection.Backend == NetworkBackendKind.Steam &&
            backendBootstrap.Selection.Purpose ==
                NetworkRuntimePurpose.Interactive;
        public NetworkBackendBootstrap ConfiguredBackendBootstrap =>
            backendBootstrap;
        public BootGameplayNetworkManager ConfiguredNetworkManager => networkManager;
        public FizzySteamworks ConfiguredFizzyTransport => fizzyTransport;

        public bool CanStartOperation =>
            IsSteamInitialized &&
            (State == SteamLobbyState.Idle || State == SteamLobbyState.Error) &&
            CurrentLobbyId == 0ul &&
            !NetworkServer.active &&
            !NetworkClient.active &&
            !cleanupInProgress &&
            !waitingForClientDisconnect &&
            networkManager != null &&
            !networkManager.IsGameplayLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            everInitialized = false;
        }

        public void Configure(
            NetworkBackendBootstrap bootstrap,
            BootGameplayNetworkManager manager,
            FizzySteamworks transport)
        {
            backendBootstrap = bootstrap != null
                ? bootstrap
                : throw new ArgumentNullException(nameof(bootstrap));
            networkManager = manager != null
                ? manager
                : throw new ArgumentNullException(nameof(manager));
            fizzyTransport = transport != null
                ? transport
                : throw new ArgumentNullException(nameof(transport));
            fizzyTransport.enabled = false;
        }

        public void Configure(
            BootGameplayNetworkManager manager,
            FizzySteamworks transport)
        {
            backendBootstrap = manager != null
                ? manager.GetComponent<NetworkBackendBootstrap>()
                : null;
            networkManager = manager != null
                ? manager
                : throw new ArgumentNullException(nameof(manager));
            fizzyTransport = transport != null
                ? transport
                : throw new ArgumentNullException(nameof(transport));
            fizzyTransport.enabled = false;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            ResolveReferences();
            if (fizzyTransport != null)
            {
                fizzyTransport.enabled = false;
            }

            if (backendBootstrap == null || !backendBootstrap.IsInitialized)
            {
                string[] arguments = Environment.GetCommandLineArgs();
                validationBypass = HasValidationRoleArgument(arguments) ||
                    HasArgument(arguments, "-runTests");
                if (validationBypass)
                {
                    State = SteamLobbyState.Disabled;
                    LastError =
                        "Steam Lobby is disabled for automated validation/tests.";
                    NotifyChanged();
                    return;
                }

                SetError("NetworkBackendBootstrap is not configured.");
                return;
            }

            validationBypass = backendBootstrap.Selection.Purpose !=
                NetworkRuntimePurpose.Interactive;
            if (!IsSteamBackendSelected)
            {
                State = SteamLobbyState.Disabled;
                LastError = validationBypass
                    ? "Steam Lobby is disabled for automated validation/tests."
                    : "Steam Lobby is disabled while the KCP backend is selected.";
                NotifyChanged();
                return;
            }

            TryInitializeSteam();
        }

        private void Update()
        {
            if (IsSteamInitialized)
            {
                SteamAPI.RunCallbacks();
            }

            CompleteCleanupWhenGameplayUnloaded();
            ObserveMirrorLifecycle();
        }

        private void OnApplicationQuit()
        {
            applicationQuitting = true;
            CleanupInternal(string.Empty, true);
            ShutdownSteam();
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            if (!applicationQuitting)
            {
                CleanupInternal(string.Empty, true);
                ShutdownSteam();
            }

            instance = null;
        }

        public bool TryInitializeSteam()
        {
            if (validationBypass || !IsSteamBackendSelected)
            {
                return false;
            }
            if (IsSteamInitialized)
            {
                return true;
            }
            if (!ResolveReferences())
            {
                SetError("Steam Lobby references are not configured.");
                return false;
            }
            if (everInitialized)
            {
                SetError("Steam API is already owned by another SteamLobbyService.");
                return false;
            }

            SetState(SteamLobbyState.Initializing, string.Empty);
            try
            {
                if (SteamAPI.RestartAppIfNecessary(
                        new AppId_t(DevelopmentAppId)))
                {
                    SetError("Steam requested that the application restart.");
                    Application.Quit();
                    return false;
                }

                if (!Packsize.Test())
                {
                    SetError("Steamworks.NET Packsize check failed.");
                    return false;
                }
                if (!DllCheck.Test())
                {
                    SetError("Steamworks.NET DLL check failed.");
                    return false;
                }
                if (!SteamAPI.Init())
                {
                    SetError(
                        "SteamAPI.Init failed. Start Steam and sign in before retrying.");
                    return false;
                }

                ownsSteamApi = true;
                everInitialized = true;
                if (SteamUtils.GetAppID().m_AppId != DevelopmentAppId)
                {
                    ShutdownSteam();
                    SetError(
                        $"Steam AppID mismatch; expected {DevelopmentAppId}.");
                    return false;
                }

                CreateSteamCallbacks();
                IsSteamInitialized = true;
                SetState(SteamLobbyState.Idle, string.Empty);
                Debug.Log(
                    $"[SteamLobby] Steam initialized with AppID " +
                    $"{DevelopmentAppId}.",
                    this);
                return true;
            }
            catch (DllNotFoundException exception)
            {
                ShutdownSteam();
                SetError($"Steam native library is unavailable: {exception.Message}");
                return false;
            }
            catch (Exception exception)
            {
                ShutdownSteam();
                SetError($"Steam initialization failed: {exception.Message}");
                return false;
            }
        }

        public void CreateLobby()
        {
            if (!TryBeginOperation(SteamLobbyState.Creating))
            {
                return;
            }

            try
            {
                SteamAPICall_t call = SteamMatchmaking.CreateLobby(
                    ELobbyType.k_ELobbyTypePublic,
                    networkManager.maxConnections);
                if (call == SteamAPICall_t.Invalid)
                {
                    FailOperation("Steam returned an invalid CreateLobby call.");
                    return;
                }

                lobbyCreatedResult.Set(call);
            }
            catch (Exception exception)
            {
                FailOperation($"CreateLobby failed: {exception.Message}");
            }
        }

        public void RequestLobbyList()
        {
            if (!TryBeginOperation(SteamLobbyState.Refreshing))
            {
                return;
            }

            lobbies.Clear();
            try
            {
                SteamMatchmaking.AddRequestLobbyListStringFilter(
                    SteamLobbyMetadata.GameKey,
                    SteamLobbyMetadata.GameValue,
                    ELobbyComparison.k_ELobbyComparisonEqual);
                SteamMatchmaking.AddRequestLobbyListStringFilter(
                    SteamLobbyMetadata.ProtocolKey,
                    SteamLobbyMetadata.ProtocolValue,
                    ELobbyComparison.k_ELobbyComparisonEqual);
                SteamMatchmaking.AddRequestLobbyListStringFilter(
                    SteamLobbyMetadata.StateKey,
                    SteamLobbyMetadata.ReadyState,
                    ELobbyComparison.k_ELobbyComparisonEqual);
                SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
                SteamMatchmaking.AddRequestLobbyListDistanceFilter(
                    ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
                SteamMatchmaking.AddRequestLobbyListResultCountFilter(
                    SearchResultLimit);

                SteamAPICall_t call = SteamMatchmaking.RequestLobbyList();
                if (call == SteamAPICall_t.Invalid)
                {
                    FailOperation(
                        "Steam returned an invalid RequestLobbyList call.");
                    return;
                }

                lobbyListResult.Set(call);
            }
            catch (Exception exception)
            {
                FailOperation($"RequestLobbyList failed: {exception.Message}");
            }
        }

        public void JoinLobby(ulong lobbyId)
        {
            if (lobbyId == 0ul)
            {
                RejectOperation("LobbyID is invalid.");
                return;
            }
            if (!TryBeginOperation(SteamLobbyState.Joining))
            {
                return;
            }

            pendingLobbyId = lobbyId;
            try
            {
                SteamAPICall_t call = SteamMatchmaking.JoinLobby(
                    new CSteamID(lobbyId));
                if (call == SteamAPICall_t.Invalid)
                {
                    pendingLobbyId = 0ul;
                    FailOperation("Steam returned an invalid JoinLobby call.");
                    return;
                }

                lobbyEnterResult.Set(call);
            }
            catch (Exception exception)
            {
                pendingLobbyId = 0ul;
                FailOperation($"JoinLobby failed: {exception.Message}");
            }
        }

        public void LeaveAndStop()
        {
            CleanupInternal(string.Empty, true);
        }

        private void HandleLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (State != SteamLobbyState.Creating)
            {
                return;
            }
            if (ioFailure || result.m_eResult != EResult.k_EResultOK ||
                result.m_ulSteamIDLobby == 0ul)
            {
                FailOperation(
                    ioFailure
                        ? "Steam IO failure while creating the Lobby."
                        : $"CreateLobby failed: {result.m_eResult}.");
                return;
            }

            CurrentLobbyId = result.m_ulSteamIDLobby;
            HostSteamId64 = SteamUser.GetSteamID().m_SteamID;
            isHostSession = true;
            CSteamID lobby = new CSteamID(CurrentLobbyId);
            if (!SteamLobbyMetadata.IsValidHostSteamId(HostSteamId64))
            {
                CleanupInternal("Local SteamID64 is invalid.", true);
                return;
            }

            string lobbyName = $"{SteamFriends.GetPersonaName()}'s Lobby";
            bool metadataWritten = SteamMatchmaking.SetLobbyJoinable(lobby, false);
            metadataWritten &= SteamMatchmaking.SetLobbyData(
                lobby,
                SteamLobbyMetadata.GameKey,
                SteamLobbyMetadata.GameValue);
            metadataWritten &= SteamMatchmaking.SetLobbyData(
                lobby,
                SteamLobbyMetadata.ProtocolKey,
                SteamLobbyMetadata.ProtocolValue);
            metadataWritten &= SteamMatchmaking.SetLobbyData(
                lobby,
                SteamLobbyMetadata.StateKey,
                SteamLobbyMetadata.StartingState);
            metadataWritten &= SteamMatchmaking.SetLobbyData(
                lobby,
                SteamLobbyMetadata.HostSteamIdKey,
                HostSteamId64.ToString());
            metadataWritten &= SteamMatchmaking.SetLobbyData(
                lobby,
                SteamLobbyMetadata.NameKey,
                lobbyName);
            if (!metadataWritten)
            {
                CleanupInternal("Failed to publish Lobby metadata.", true);
                return;
            }
            if (!ActivateFizzy(out string transportError))
            {
                CleanupInternal(transportError, true);
                return;
            }

            try
            {
                networkManager.StartHost();
            }
            catch (Exception exception)
            {
                CleanupInternal(
                    $"Mirror StartHost failed: {exception.Message}",
                    true);
                return;
            }

            if (!NetworkServer.active || !fizzyTransport.ServerActive())
            {
                CleanupInternal(
                    "Mirror Host/Fizzy server did not become active.",
                    true);
                return;
            }

            networkObserved = true;
            bool readyPublished = SteamMatchmaking.SetLobbyData(
                lobby,
                SteamLobbyMetadata.StateKey,
                SteamLobbyMetadata.ReadyState);
            readyPublished &= SteamMatchmaking.SetLobbyJoinable(lobby, true);
            if (!readyPublished)
            {
                CleanupInternal("Failed to mark the Lobby ready.", true);
                return;
            }

            SetState(SteamLobbyState.Hosting, string.Empty);
            Debug.Log(
                $"[SteamLobby] Hosting Lobby {CurrentLobbyId} as " +
                $"{HostSteamId64}.",
                this);
        }

        private void HandleLobbyList(LobbyMatchList_t result, bool ioFailure)
        {
            if (State != SteamLobbyState.Refreshing)
            {
                return;
            }
            if (ioFailure)
            {
                FailOperation("Steam IO failure while requesting Lobby list.");
                return;
            }

            lobbies.Clear();
            uint count = Math.Min(
                result.m_nLobbiesMatching,
                (uint)SearchResultLimit);
            for (uint index = 0u; index < count; index++)
            {
                CSteamID lobby = SteamMatchmaking.GetLobbyByIndex((int)index);
                if (SteamLobbyMetadata.TryCreateSummary(
                        lobby.m_SteamID,
                        SteamMatchmaking.GetLobbyData(
                            lobby,
                            SteamLobbyMetadata.GameKey),
                        SteamMatchmaking.GetLobbyData(
                            lobby,
                            SteamLobbyMetadata.ProtocolKey),
                        SteamMatchmaking.GetLobbyData(
                            lobby,
                            SteamLobbyMetadata.StateKey),
                        SteamMatchmaking.GetLobbyData(
                            lobby,
                            SteamLobbyMetadata.HostSteamIdKey),
                        SteamMatchmaking.GetLobbyData(
                            lobby,
                            SteamLobbyMetadata.NameKey),
                        SteamMatchmaking.GetNumLobbyMembers(lobby),
                        SteamMatchmaking.GetLobbyMemberLimit(lobby),
                        out SteamLobbySummary summary))
                {
                    lobbies.Add(summary);
                }
            }

            SetState(SteamLobbyState.Idle, string.Empty);
        }

        private void HandleLobbyEntered(LobbyEnter_t result, bool ioFailure)
        {
            if (State != SteamLobbyState.Joining)
            {
                return;
            }

            ulong requestedLobbyId = pendingLobbyId;
            pendingLobbyId = 0ul;
            EChatRoomEnterResponse response =
                (EChatRoomEnterResponse)result.m_EChatRoomEnterResponse;
            if (ioFailure ||
                response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess ||
                result.m_ulSteamIDLobby == 0ul)
            {
                FailOperation(
                    ioFailure
                        ? "Steam IO failure while joining the Lobby."
                        : $"JoinLobby failed: {response}.");
                return;
            }

            if (result.m_ulSteamIDLobby != requestedLobbyId)
            {
                CurrentLobbyId = result.m_ulSteamIDLobby;
                CleanupInternal(
                    "Steam entered a different Lobby than requested.",
                    true);
                return;
            }

            CurrentLobbyId = result.m_ulSteamIDLobby;
            isHostSession = false;
            CSteamID lobby = new CSteamID(CurrentLobbyId);
            if (!TryReadReadyHost(lobby, out ulong hostSteamId, out string error))
            {
                CleanupInternal(error, true);
                return;
            }

            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (!owner.IsValid() || !owner.BIndividualAccount() ||
                owner.m_SteamID != hostSteamId)
            {
                CleanupInternal(
                    "Lobby owner does not match host_steam_id metadata.",
                    true);
                return;
            }

            HostSteamId64 = hostSteamId;
            if (!ActivateFizzy(out string transportError))
            {
                CleanupInternal(transportError, true);
                return;
            }

            networkManager.networkAddress = HostSteamId64.ToString();
            try
            {
                networkManager.StartClient();
            }
            catch (Exception exception)
            {
                CleanupInternal(
                    $"Mirror StartClient failed: {exception.Message}",
                    true);
                return;
            }

            if (!NetworkClient.active)
            {
                CleanupInternal("Mirror Client did not start connecting.", true);
                return;
            }

            networkObserved = true;
            SetState(SteamLobbyState.ConnectingClient, string.Empty);
            Debug.Log(
                $"[SteamLobby] Joining Lobby {CurrentLobbyId}; " +
                $"Fizzy target is {HostSteamId64}.",
                this);
        }

        private bool TryReadReadyHost(
            CSteamID lobby,
            out ulong hostSteamId,
            out string error)
        {
            return SteamLobbyMetadata.TryGetReadyHostSteamId(
                SteamMatchmaking.GetLobbyData(
                    lobby,
                    SteamLobbyMetadata.GameKey),
                SteamMatchmaking.GetLobbyData(
                    lobby,
                    SteamLobbyMetadata.ProtocolKey),
                SteamMatchmaking.GetLobbyData(
                    lobby,
                    SteamLobbyMetadata.StateKey),
                SteamMatchmaking.GetLobbyData(
                    lobby,
                    SteamLobbyMetadata.HostSteamIdKey),
                out hostSteamId,
                out error);
        }

        private void HandleLobbyChatUpdate(LobbyChatUpdate_t update)
        {
            if (update.m_ulSteamIDLobby == CurrentLobbyId && !isHostSession)
            {
                ValidateCurrentLobbyOwner();
            }
        }

        private void HandleLobbyDataUpdate(LobbyDataUpdate_t update)
        {
            if (update.m_ulSteamIDLobby != CurrentLobbyId || isHostSession)
            {
                return;
            }
            if (update.m_bSuccess == 0)
            {
                CleanupInternal("The current Lobby was removed.", true, false);
                return;
            }

            CSteamID lobby = new CSteamID(CurrentLobbyId);
            string state = SteamMatchmaking.GetLobbyData(
                lobby,
                SteamLobbyMetadata.StateKey);
            if (!string.Equals(
                    state,
                    SteamLobbyMetadata.ReadyState,
                    StringComparison.Ordinal))
            {
                CleanupInternal(
                    "The Lobby host closed the session.",
                    true,
                    false);
                return;
            }

            ValidateCurrentLobbyOwner();
        }

        private void HandleLobbyKicked(LobbyKicked_t kicked)
        {
            if (kicked.m_ulSteamIDLobby == CurrentLobbyId)
            {
                CleanupInternal(
                    "The local user was removed from the Lobby.",
                    true,
                    false);
            }
        }

        private void HandleSteamDisconnected(SteamServersDisconnected_t result)
        {
            string message = $"Steam backend disconnected: {result.m_eResult}.";
            if (State == SteamLobbyState.Leaving)
            {
                cleanupCompletionError = message;
                cleanupEndsInError = true;
                LastError = message;
                NotifyChanged();
                return;
            }

            if (CurrentLobbyId != 0ul || NetworkServer.active ||
                NetworkClient.active)
            {
                CleanupInternal(message, true);
            }
            else
            {
                CancelPendingOperations();
                SetError(message);
            }
        }

        private void HandleSteamConnected(SteamServersConnected_t _)
        {
            if (IsSteamInitialized && State == SteamLobbyState.Error &&
                CurrentLobbyId == 0ul &&
                !NetworkServer.active && !NetworkClient.active)
            {
                SetState(SteamLobbyState.Idle, string.Empty);
            }
        }

        private void ValidateCurrentLobbyOwner()
        {
            if (CurrentLobbyId == 0ul || HostSteamId64 == 0ul)
            {
                return;
            }

            CSteamID owner = SteamMatchmaking.GetLobbyOwner(
                new CSteamID(CurrentLobbyId));
            if (owner.m_SteamID != HostSteamId64)
            {
                CleanupInternal(
                    "Lobby owner changed; host migration is not supported.",
                    true,
                    false);
            }
        }

        private void ObserveMirrorLifecycle()
        {
            if (cleanupInProgress || CurrentLobbyId == 0ul || !networkObserved)
            {
                return;
            }

            if (isHostSession)
            {
                if (!NetworkServer.active)
                {
                    CleanupInternal(
                        "Mirror Host stopped unexpectedly.",
                        true,
                        false);
                }
                return;
            }

            if (!NetworkClient.active)
            {
                CleanupInternal(
                    "Mirror Client disconnected.",
                    true,
                    false);
                return;
            }
            if (State == SteamLobbyState.ConnectingClient &&
                NetworkClient.isConnected)
            {
                SetState(SteamLobbyState.Connected, string.Empty);
            }
        }

        private bool ActivateFizzy(out string error)
        {
            error = null;
            if (!IsSteamInitialized || networkManager == null ||
                fizzyTransport == null)
            {
                error = "Steam/Fizzy is not initialized.";
                return false;
            }

            if (backendBootstrap == null ||
                backendBootstrap.ConfiguredSteamTransport != fizzyTransport)
            {
                error = "Steam/Fizzy backend wiring is inconsistent.";
                return false;
            }
            if (!backendBootstrap.TryPrepareSteam(out error))
            {
                return false;
            }

            return true;
        }

        private bool TryBeginOperation(SteamLobbyState operationState)
        {
            if (!CanStartOperation)
            {
                RejectOperation(
                    IsSteamInitialized
                        ? "Another Lobby or Mirror operation is already active."
                        : "Steam is not initialized. Retry Steam initialization first.");
                return false;
            }

            CancelPendingOperations();
            SetState(operationState, string.Empty);
            return true;
        }

        private void FailOperation(string message)
        {
            CancelPendingOperations();
            SetError(message);
        }

        private void CleanupInternal(
            string error,
            bool leaveLobby,
            bool endInError = true)
        {
            if (cleanupInProgress ||
                (State == SteamLobbyState.Leaving && CurrentLobbyId == 0ul))
            {
                return;
            }

            cleanupInProgress = true;
            SetState(SteamLobbyState.Leaving, string.Empty);
            CancelPendingOperations();

            ulong lobbyId = CurrentLobbyId;
            bool wasHost = isHostSession;
            bool clientStopMayCompleteAsynchronously = !wasHost &&
                networkObserved &&
                networkManager != null &&
                networkManager.mode == NetworkManagerMode.ClientOnly &&
                !NetworkClient.isConnected;
            if (wasHost && lobbyId != 0ul && IsSteamInitialized)
            {
                TryCloseLobby(new CSteamID(lobbyId));
            }

            try
            {
                if (networkManager != null)
                {
                    if (NetworkServer.active && NetworkClient.active)
                    {
                        networkManager.StopHost();
                    }
                    else if (NetworkServer.active)
                    {
                        networkManager.StopServer();
                    }
                    else if (NetworkClient.active)
                    {
                        networkManager.StopClient();
                    }
                }
            }
            catch (Exception exception)
            {
                endInError = true;
                if (string.IsNullOrEmpty(error))
                {
                    error = $"Mirror cleanup failed: {exception.Message}";
                }
            }

            if (fizzyTransport != null)
            {
                try
                {
                    fizzyTransport.Shutdown();
                }
                catch (Exception exception)
                {
                    endInError = true;
                    if (string.IsNullOrEmpty(error))
                    {
                        error = $"Fizzy cleanup failed: {exception.Message}";
                    }
                }
                fizzyTransport.enabled = false;
            }

            if (leaveLobby && lobbyId != 0ul && IsSteamInitialized)
            {
                try
                {
                    SteamMatchmaking.LeaveLobby(new CSteamID(lobbyId));
                }
                catch (Exception exception)
                {
                    endInError = true;
                    if (string.IsNullOrEmpty(error))
                    {
                        error = $"LeaveLobby failed: {exception.Message}";
                    }
                }
            }

            CurrentLobbyId = 0ul;
            HostSteamId64 = 0ul;
            isHostSession = false;
            networkObserved = false;
            lobbies.Clear();
            if (networkManager != null)
            {
                networkManager.networkAddress = string.Empty;
                if (backendBootstrap != null)
                {
                    backendBootstrap.RestoreIdleTransport();
                }
            }

            cleanupCompletionError = error ?? string.Empty;
            cleanupEndsInError = endInError &&
                !string.IsNullOrEmpty(cleanupCompletionError);
            waitingForClientDisconnect =
                clientStopMayCompleteAsynchronously &&
                networkManager != null &&
                networkManager.mode != NetworkManagerMode.Offline;
            cleanupInProgress = false;
            if (applicationQuitting || networkManager == null ||
                (!waitingForClientDisconnect &&
                 !networkManager.IsGameplayLoaded))
            {
                CompleteCleanupState();
            }
        }

        private void TryCloseLobby(CSteamID lobby)
        {
            try
            {
                SteamMatchmaking.SetLobbyJoinable(lobby, false);
                SteamMatchmaking.SetLobbyData(
                    lobby,
                    SteamLobbyMetadata.StateKey,
                    SteamLobbyMetadata.ClosedState);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[SteamLobby] Unable to close Lobby metadata: " +
                    $"{exception.Message}",
                    this);
            }
        }

        private void CompleteCleanupWhenGameplayUnloaded()
        {
            if (State == SteamLobbyState.Leaving && !cleanupInProgress &&
                CurrentLobbyId == 0ul &&
                (!waitingForClientDisconnect || networkManager == null ||
                 networkManager.mode == NetworkManagerMode.Offline) &&
                (networkManager == null || !networkManager.IsGameplayLoaded))
            {
                CompleteCleanupState();
            }
        }

        private void CompleteCleanupState()
        {
            backendBootstrap?.RestoreIdleTransport();
            string error = cleanupCompletionError;
            cleanupCompletionError = string.Empty;
            waitingForClientDisconnect = false;
            bool endInError = cleanupEndsInError;
            cleanupEndsInError = false;
            SetState(
                endInError ? SteamLobbyState.Error : SteamLobbyState.Idle,
                error);
        }

        private void CreateSteamCallbacks()
        {
            lobbyCreatedResult = CallResult<LobbyCreated_t>.Create(
                HandleLobbyCreated);
            lobbyListResult = CallResult<LobbyMatchList_t>.Create(
                HandleLobbyList);
            lobbyEnterResult = CallResult<LobbyEnter_t>.Create(
                HandleLobbyEntered);
            lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(
                HandleLobbyChatUpdate);
            lobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(
                HandleLobbyDataUpdate);
            lobbyKicked = Callback<LobbyKicked_t>.Create(HandleLobbyKicked);
            steamDisconnected = Callback<SteamServersDisconnected_t>.Create(
                HandleSteamDisconnected);
            steamConnected = Callback<SteamServersConnected_t>.Create(
                HandleSteamConnected);
        }

        private void CancelPendingOperations()
        {
            lobbyCreatedResult?.Cancel();
            lobbyListResult?.Cancel();
            lobbyEnterResult?.Cancel();
            pendingLobbyId = 0ul;
        }

        private void DisposeSteamCallbacks()
        {
            lobbyCreatedResult?.Dispose();
            lobbyCreatedResult = null;
            lobbyListResult?.Dispose();
            lobbyListResult = null;
            lobbyEnterResult?.Dispose();
            lobbyEnterResult = null;
            lobbyChatUpdate?.Dispose();
            lobbyChatUpdate = null;
            lobbyDataUpdate?.Dispose();
            lobbyDataUpdate = null;
            lobbyKicked?.Dispose();
            lobbyKicked = null;
            steamDisconnected?.Dispose();
            steamDisconnected = null;
            steamConnected?.Dispose();
            steamConnected = null;
        }

        private void ShutdownSteam()
        {
            DisposeSteamCallbacks();
            IsSteamInitialized = false;
            if (!ownsSteamApi)
            {
                return;
            }

            SteamAPI.Shutdown();
            ownsSteamApi = false;
            everInitialized = false;
        }

        private bool ResolveReferences()
        {
            if (backendBootstrap == null)
            {
                backendBootstrap = GetComponent<NetworkBackendBootstrap>();
            }
            if (networkManager == null)
            {
                networkManager = GetComponent<BootGameplayNetworkManager>();
            }
            if (fizzyTransport == null)
            {
                fizzyTransport = GetComponent<FizzySteamworks>();
            }

            return backendBootstrap != null && networkManager != null &&
                fizzyTransport != null;
        }

        private void SetError(string message)
        {
            SetState(SteamLobbyState.Error, message);
            Debug.LogWarning($"[SteamLobby] {message}", this);
        }

        private void RejectOperation(string message)
        {
            LastError = message ?? string.Empty;
            NotifyChanged();
            Debug.LogWarning($"[SteamLobby] {LastError}", this);
        }

        private void SetState(SteamLobbyState state, string error)
        {
            State = state;
            LastError = error ?? string.Empty;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            Changed?.Invoke();
        }

        private static bool HasValidationRoleArgument(string[] arguments)
        {
            if (arguments == null)
            {
                return false;
            }

            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].StartsWith(
                        NetworkBackendBootstrap.ValidationRolePrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasArgument(string[] arguments, string expected)
        {
            if (arguments == null)
            {
                return false;
            }

            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(
                        arguments[i],
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
