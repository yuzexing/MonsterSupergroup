using System;
using System.Collections.Generic;
using Mirror;
using Steamworks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class SteamLobbyService
    {
        private readonly SteamLobbyJoinQueue invitationQueue = new SteamLobbyJoinQueue();
        private readonly List<CallResult<LobbyCreated_t>> pendingCreates = new List<CallResult<LobbyCreated_t>>();
        private readonly List<CallResult<LobbyEnter_t>> pendingJoins = new List<CallResult<LobbyEnter_t>>();
        private uint lobbyOperationGeneration;
        private Callback<GameRichPresenceJoinRequested_t> richPresenceJoinRequested;
        private Callback<NewUrlLaunchParameters_t> launchParametersChanged;
        private Callback<LobbyInvite_t> lobbyInviteReceived;
        private SteamLobbyPresence lobbyPresence;
        private double nextPresenceRead;

        private bool CanCompleteLobbyCleanup => (IsSteamInitialized || HasJoinTestDriver) &&
            (State == SteamLobbyState.Idle || State == SteamLobbyState.Error) &&
            CurrentLobbyId == 0 && !NetworkServer.active && !NetworkClient.active &&
            !cleanupInProgress && !waitingForClientDisconnect && pendingCreates.Count == 0 && pendingJoins.Count == 0 &&
            networkManager != null && networkManager.mode == NetworkManagerMode.Offline &&
            !networkManager.IsGameplayLoaded && !networkManager.IsGameplayTransitioning &&
            !networkManager.IsLeavingRoom && !networkManager.IsLocalRoomConnecting;

        public void AcceptInvitation(ulong lobbyId) => RequestInvitationJoin(lobbyId, "lobby", 0);

        internal void CancelInvitationJoin()
        {
            invitationQueue.Clear();
            startupInvitation = 0;
        }

        internal void RequestInvitationJoin(ulong lobbyId, string source, ulong friend)
        {
            if (!SteamLobbyConnection.IsValidLobby(lobbyId) || applicationQuitting ||
                (!IsSteamInitialized && !HasJoinTestDriver) || networkManager == null)
            { Debug.Log($"[SteamInvite] stage=accept source={source} lobby={lobbyId} result=invalid_or_unavailable", this); return; }
            if ((invitationQueue.Target == 0 && !networkManager.IsLeavingRoom &&
                (lobbyId == CurrentLobbyId || lobbyId == pendingLobbyId)) || !invitationQueue.Request(lobbyId, JoinClock))
            { Debug.Log($"[SteamInvite] stage=accept source={source} lobby={lobbyId} result=duplicate", this); return; }

            startupInvitation = 0;
            Debug.Log($"[SteamInvite] stage=accept source={source} lobby={lobbyId} friend={friend} currentLobby={CurrentLobbyId} result=queued phase={State}", this);
            ClearLobbyPresence();
            // Keep native callbacks alive to leave any lobby created/entered after cancellation.
            CancelPendingOperations();
            if (State == SteamLobbyState.Refreshing) SetState(SteamLobbyState.Idle, string.Empty);
            if (!CanCompleteLobbyCleanup && !networkManager.IsLeavingRoom) networkManager.BeginLeavingPreparationRoom();
            ShowInvitationNotice("正在离开当前会话并加入好友房间…");
        }

        internal void AcceptConnectionString(string connection, string source, ulong friend = 0)
        {
            ulong lobby = SteamLobbyConnection.Parse(connection);
            Debug.Log($"[SteamInvite] stage=join_requested source={source} lobby={lobby} friend={friend} phase={State}", this);
            RequestInvitationJoin(lobby, source, friend);
        }

        private void TickInvitationJoin()
        {
            ulong lobby = invitationQueue.Take(CanCompleteLobbyCleanup, JoinClock, out bool timedOut);
            if (timedOut)
            {
                Debug.LogWarning("[SteamInvite] stage=switch result=cleanup_timeout", this);
                ShowInvitationNotice("退出原房间超时，已取消自动加入；请等待清理完成后重试。");
                return;
            }
            if (lobby == 0) return;
#if UNITY_EDITOR
            if (joinTestDriver != null)
            {
                networkManager.BeginConnectionAttempt();
                if (networkManager.CheckBuildForConnection()) joinTestDriver(lobby);
                return;
            }
#endif
            JoinLobby(lobby);
        }

        private void ReadSteamLaunchInvitation(bool startup)
        {
            SteamApps.GetLaunchCommandLine(out string commandLine, 4096);
            ulong lobby = startup ? SteamLobbyConnection.ParseStartup(commandLine, Environment.GetCommandLineArgs())
                : SteamLobbyConnection.Parse(commandLine);
            if (startup) startupInvitation = lobby;
            else RequestInvitationJoin(lobby, "launch_parameters", 0);
        }

        private void TrackLobbyCreation(SteamAPICall_t call)
        {
            uint generation = lobbyOperationGeneration;
            CallResult<LobbyCreated_t> callback = null;
            callback = CallResult<LobbyCreated_t>.Create((result, failure) => {
                pendingCreates.Remove(callback); callback.Dispose();
                if (generation != lobbyOperationGeneration)
                {
                    if (!failure && result.m_eResult == EResult.k_EResultOK) LeaveAbandonedLobby(result.m_ulSteamIDLobby, true);
                    return;
                }
                HandleLobbyCreated(result, failure);
            });
            pendingCreates.Add(callback); callback.Set(call);
        }

        private void TrackLobbyJoin(SteamAPICall_t call)
        {
            uint generation = lobbyOperationGeneration;
            CallResult<LobbyEnter_t> callback = null;
            callback = CallResult<LobbyEnter_t>.Create((result, failure) => {
                pendingJoins.Remove(callback); callback.Dispose();
                CompleteTrackedLobbyJoin(generation, result, failure);
            });
            pendingJoins.Add(callback); callback.Set(call);
        }

        private void CompleteTrackedLobbyJoin(uint generation, LobbyEnter_t result, bool failure)
        {
            if (generation != lobbyOperationGeneration)
            {
                if (!failure && result.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                    LeaveAbandonedLobby(result.m_ulSteamIDLobby, false);
                return;
            }
            HandleLobbyEntered(result, failure);
        }

        private void LeaveAbandonedLobby(ulong lobby, bool created)
        {
            if (lobby == 0 || lobby == CurrentLobbyId) return;
            Debug.Log($"[SteamInvite] stage=stale_callback lobby={lobby} created={created} result=leave", this);
#if UNITY_EDITOR
            if (leaveLobbyTestDriver != null) { leaveLobbyTestDriver(lobby); return; }
#endif
            if (!IsSteamInitialized) return;
            if (created) TryCloseLobby(new CSteamID(lobby));
            SteamMatchmaking.LeaveLobby(new CSteamID(lobby));
        }

        private void ObserveLobbyPresence()
        {
            if (!IsSteamInitialized) return;
            double now = Time.realtimeSinceStartupAsDouble;
            bool eligible = invitationQueue.Target == 0 && CanBrowseInviteFriends && !networkManager.IsLeavingRoom &&
                NetworkClient.isConnected && NetworkClient.connection != null && NetworkClient.connection.isAuthenticated &&
                networkManager.RoomSnapshot.SelfId != 0;
            if (!eligible) { ClearLobbyPresence(); return; }
            if (now < nextPresenceRead) return;
            nextPresenceRead = now + 1;
            try
            {
                var api = new NativeSteamInviteApi();
                var lobby = api.ReadLobby(CurrentLobbyId);
                bool member = Array.IndexOf(lobby.Members, api.LocalUser) >= 0;
                string connect = SteamLobbyPresence.ConnectionFor(CurrentLobbyId, api.LoggedOn, lobby.Preparing,
                    member && lobby.ValidHost, lobby.Members.Length, networkManager.RoomSnapshot.Members?.Length ?? 0,
                    Math.Min(lobby.Limit, PreparationRoom.Capacity));
                Presence.Update(connect, now);
            }
            catch (Exception error)
            {
                ClearLobbyPresence();
                Debug.Log($"[SteamInvite] stage=presence_read result=failed type={error.GetType().Name}", this);
            }
        }

        private SteamLobbyPresence Presence => lobbyPresence ??= new SteamLobbyPresence(
            connection => SteamFriends.SetRichPresence("connect", connection), message => Debug.Log("[SteamInvite] " + message, this));
        private void ClearLobbyPresence()
        {
            if (IsSteamInitialized) Presence.Update(string.Empty, Time.realtimeSinceStartupAsDouble);
        }

        private bool HasJoinTestDriver
        {
            get {
#if UNITY_EDITOR
                return joinTestDriver != null;
#else
                return false;
#endif
            }
        }
        private double JoinClock =>
#if UNITY_EDITOR
            joinTestClock != null ? joinTestClock() :
#endif
            Time.realtimeSinceStartupAsDouble;
#if UNITY_EDITOR
        private Action<ulong> joinTestDriver, leaveLobbyTestDriver;
        private Func<double> joinTestClock;
        internal void ConfigureJoinForTests(Action<ulong> join, Func<double> clock = null, Action<ulong> leave = null)
        { joinTestDriver = join; joinTestClock = clock; leaveLobbyTestDriver = leave; }
#endif
    }
}
