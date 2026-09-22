using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace MonsterSupergroup.Gameplay.Tests
{
#if UNITY_EDITOR
    public sealed class SteamInvitationSwitchPlayModeTests
    {
        private const ulong Lobby = 109775242349881186, OtherLobby = 109775242349881187;
        private GameObject[] roots;
        private BootGameplayNetworkManager manager;
        private SteamLobbyService service;
        private readonly List<ulong> joined = new List<ulong>();
        private readonly List<ulong> abandoned = new List<ulong>();
        private double clock;

        [UnitySetUp] public IEnumerator SetUp()
        {
            const string boot = "Assets/_Project/Scenes/Boot.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot, new LoadSceneParameters(LoadSceneMode.Single));
            roots = BootSceneFixtureObjects.Capture(boot);
            manager = (BootGameplayNetworkManager)NetworkManager.singleton;
            manager.ConfigurePreparationFlow(true);
            service = manager.GetComponent<SteamLobbyService>();
            SetProperty("State", SteamLobbyState.Idle);
            joined.Clear(); abandoned.Clear(); clock = 0;
            service.ConfigureJoinForTests(lobby => {
                Assert.That(NetworkClient.active || NetworkServer.active || manager.IsLeavingRoom ||
                    manager.IsGameplayLoaded || manager.IsGameplayTransitioning, Is.False, "Never join before old session cleanup.");
                Assert.That(manager.mode, Is.EqualTo(NetworkManagerMode.Offline));
                joined.Add(lobby);
            }, () => clock, abandoned.Add);
            yield return manager.EnsureMainMenu();
        }

        [UnityTest] public IEnumerator RichPresenceAndLobbyInvitesSwitchLocalHostWithoutConfirmationAndLatestWins()
        {
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return Until(() => manager.RoomSnapshot.SelfId != 0);
            Assert.That(NetworkServer.active, Is.True);
            service.AcceptConnectionString(SteamLobbyConnection.Format(Lobby), "rich_presence");
            Assert.That(manager.IsLeavingRoom, Is.True);
            Assert.That(joined, Is.Empty);
            service.AcceptInvitation(OtherLobby);
            service.AcceptInvitation(OtherLobby);
            yield return Until(() => joined.Count > 0);
            yield return null;
            Assert.That(joined, Is.EqualTo(new[] { OtherLobby }));
            Assert.That(manager.RoomSnapshot.SelfId, Is.Zero);
            Assert.That(service.CanStartOperation, Is.True);
        }

        [UnityTest] public IEnumerator InvalidConnectionDoesNotExitRoomAndCurrentLobbyInviteDoesNotRestart()
        {
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return Until(() => manager.RoomSnapshot.SelfId != 0);
            service.AcceptConnectionString("+connect_lobby 76561198000000001", "rich_presence");
            yield return null;
            Assert.That(NetworkServer.active, Is.True);
            Assert.That(manager.IsLeavingRoom, Is.False);
            SetProperty("CurrentLobbyId", Lobby);
            service.AcceptInvitation(Lobby);
            Assert.That(manager.IsLeavingRoom, Is.False);
            Assert.That(joined, Is.Empty);
            SetProperty("CurrentLobbyId", 0ul);
        }

        [UnityTest] public IEnumerator AcceptDuringCombatWaitsForGameplaySceneAndAvatarCleanup()
        {
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return Until(() => manager.RoomSnapshot.SelfId != 0);
            manager.StartPreparedGame();
            yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame);
            Assert.That(manager.IsGameplayLoaded, Is.True);
            Assert.That(NetworkClient.localPlayer, Is.Not.Null);
            service.AcceptInvitation(Lobby);
            Assert.That(joined, Is.Empty);
            yield return Until(() => joined.Count > 0);
            Assert.That(joined, Is.EqualTo(new[] { Lobby }));
            Assert.That(NetworkClient.localPlayer, Is.Null);
            Assert.That(manager.IsGameplayLoaded, Is.False);
        }

        [UnityTest] public IEnumerator ClosedTargetStaysInMenuWithFailureInsteadOfCreatingOfflineRoom()
        {
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return Until(() => manager.RoomSnapshot.SelfId != 0);
            service.ConfigureJoinForTests(lobby => {
                joined.Add(lobby);
                SetProperty("State", SteamLobbyState.Joining);
                typeof(SteamLobbyService).GetField("pendingLobbyId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, lobby);
                typeof(SteamLobbyService).GetMethod("HandleLobbyEntered", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(service, new object[] { new LobbyEnter_t { m_ulSteamIDLobby = lobby,
                        m_EChatRoomEnterResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist }, false });
            }, () => clock, abandoned.Add);
            LogAssert.Expect(LogType.Warning, "[SteamLobby] JoinLobby failed: k_EChatRoomEnterResponseDoesntExist.");
            service.AcceptInvitation(Lobby);
            yield return Until(() => joined.Count > 0);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(service.State, Is.EqualTo(SteamLobbyState.Error));
            Assert.That(service.LastError, Does.Contain("DoesntExist"));
            Assert.That(NetworkServer.active || NetworkClient.active, Is.False);
            Assert.That(manager.RoomSnapshot.SelfId, Is.Zero);
        }

        [UnityTest] public IEnumerator TimedOutCleanupDoesNotAutoJoinAfterItEventuallyCompletes()
        {
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return Until(() => manager.RoomSnapshot.SelfId != 0);
            service.AcceptInvitation(Lobby);
            clock = 30;
            LogAssert.Expect(LogType.Warning, "[SteamInvite] stage=switch result=cleanup_timeout");
            yield return null;
            yield return Until(() => !manager.IsLeavingRoom);
            yield return null;
            Assert.That(joined, Is.Empty);
            Assert.That(manager.MenuNotice, Does.Contain("超时"));
            service.AcceptInvitation(OtherLobby);
            yield return Until(() => joined.Count > 0);
            Assert.That(joined, Is.EqualTo(new[] { OtherLobby }));
        }

        [UnityTest] public IEnumerator ExplicitLeaveCancelsQueuedInvitationInsteadOfJoiningAfterCleanup()
        {
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return Until(() => manager.RoomSnapshot.SelfId != 0);
            service.AcceptInvitation(Lobby);
            manager.LeavePreparationRoom();
            yield return Until(() => !manager.IsLeavingRoom);
            yield return null;
            Assert.That(joined, Is.Empty);
            Assert.That(service.CanStartOperation, Is.True);
        }

        [UnityTest] public IEnumerator SuccessfulLateNativeCallbackLeavesOldLobbyWithoutOverwritingNewSession()
        {
            typeof(SteamLobbyService).GetMethod("CancelPendingOperations", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(service, null);
            SetProperty("CurrentLobbyId", OtherLobby);
            typeof(SteamLobbyService).GetMethod("CompleteTrackedLobbyJoin", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(service, new object[] { 0u, new LobbyEnter_t { m_ulSteamIDLobby = Lobby,
                    m_EChatRoomEnterResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess }, false });
            Assert.That(abandoned, Is.EqualTo(new[] { Lobby }));
            Assert.That(service.CurrentLobbyId, Is.EqualTo(OtherLobby));
            Assert.That(joined, Is.Empty);
            SetProperty("CurrentLobbyId", 0ul);
            yield return null;
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            if (manager != null)
            {
                service.ConfigureJoinForTests(null);
                manager.LeavePreparationRoom();
                yield return Until(() => !NetworkServer.active && !NetworkClient.active && !manager.IsLeavingRoom);
            }
            BootSceneFixtureObjects.Destroy(roots); yield return null; NetworkManager.ResetStatics();
        }

        private void SetProperty<T>(string name, T value) => typeof(SteamLobbyService)
            .GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, value);
        private static IEnumerator Until(Func<bool> condition)
        {
            float until = Time.realtimeSinceStartup + 30;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(condition(), Is.True, "Steam invitation switch timed out.");
        }
    }
#endif
}
