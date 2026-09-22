using System;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class SteamLobbyService
    {
        private SteamLobbyInvitations invitations;
        private string loggedPreparationRun;
        private string lastInvitationNotice;
        private void ShowInvitationNotice(string message)
        { lastInvitationNotice = message; networkManager?.ShowMenuNotice(message); }
        private SteamLobbyInvitations Invitations => invitations ??= new SteamLobbyInvitations(new NativeSteamInviteApi(),
            () => new SteamInviteContext(CurrentLobbyId, IsSteamInitialized,
                IsSteamBackendSelected && !cleanupInProgress && networkManager != null && networkManager.RoomSnapshot.Online &&
                networkManager.RoomSnapshot.Phase == PreparationPhase.Preparing &&
                (State == SteamLobbyState.Hosting || State == SteamLobbyState.Connected)), () => Time.realtimeSinceStartupAsDouble);
        public bool CanBrowseInviteFriends => Invitations.HasRoomContext;
        public SteamInviteFriendsSnapshot InviteFriendsSnapshot => Invitations.Snapshot;
        public SteamInviteFriendsSnapshot RefreshInviteFriends() => Invitations.Refresh();
        public SteamInviteResult SendLobbyInvite(ulong expectedLobbyId, ulong friendSteamId)
        {
            var result = Invitations.Send(expectedLobbyId, friendSteamId);
            Debug.Log($"[SteamInvite] stage=send lobby={expectedLobbyId} currentLobby={CurrentLobbyId} friend={friendSteamId} " +
                $"role={(isHostSession ? "host" : "member")} method=InviteUserToGame phase={networkManager?.RoomSnapshot.Phase} result={result.Code} reason={result.Message}", this);
            // A failed invite must not change the session state or trigger the creation fallback.
            ShowInvitationNotice(result.Message);
            Invitations.Refresh();
            return result;
        }
        private void ObserveInvitationLifecycle()
        {
            ObserveLobbyPresence();
            invitations?.SynchronizeContext();
            if (CurrentLobbyId == 0) { loggedPreparationRun = null; return; }
            if (networkManager == null) return;
            var room = networkManager.RoomSnapshot;
            if (room.Phase == PreparationPhase.Preparing && room.SelfId != 0 && room.RunId != loggedPreparationRun)
            {
                loggedPreparationRun = room.RunId;
                Debug.Log($"[SteamInvite] stage=preparation_snapshot lobby={CurrentLobbyId} host={HostSteamId64} " +
                    $"participant={room.SelfId} run={room.RunId} members={room.Members?.Length} revision={room.Revision}", this);
                ShowInvitationNotice("已进入准备房间，可以选择武器并准备。");
            }
        }
#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
        internal void ConfigureInvitationsForTests(ISteamInviteApi api, Func<SteamInviteContext> context, Func<double> clock)
        { invitations = new SteamLobbyInvitations(api, context, clock); }
#endif
    }
}
