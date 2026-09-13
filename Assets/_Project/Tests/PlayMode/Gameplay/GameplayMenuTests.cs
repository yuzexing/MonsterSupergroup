using System.Collections;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class GameplayMenuTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] roots;
        [UnitySetUp] public IEnumerator SetUp()
        {
            const string path = "Assets/_Project/Scenes/Boot.unity";
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
#endif
            roots = BootSceneFixtureObjects.Capture(path);
            manager = (BootGameplayNetworkManager)NetworkManager.singleton;
            manager.ConfigurePreparationFlow(true);
            yield return manager.EnsureMainMenu();
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return GameplayMenuUiScenario.Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "room");
            manager.StartPreparedGame();
            yield return GameplayMenuUiScenario.Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame, "gameplay");
            yield return null;
        }
        [UnityTest] public IEnumerator OwnerStatisticsCardsInputAndConfirmedExit()
        {
            yield return GameplayMenuUiScenario.Run(manager, null, true);
            var menu = Object.FindFirstObjectByType<NetworkGameplayMenuController>();
            menu.OpenMenu(); menu.RequestExit(); menu.ConfirmExit(); menu.ConfirmExit();
            Assert.That(menu.IsExiting, Is.True);
            yield return GameplayMenuUiScenario.Wait(() => !NetworkClient.active && !manager.IsGameplayLoaded && !manager.IsLeavingRoom, "exit");
            Assert.That(GameplayMenuInput.IsOpen, Is.False);
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return GameplayMenuUiScenario.Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "new room");
        }
        [UnityTest] public IEnumerator MenuWithoutOwnerCleansUp_WipeTakesPrecedenceOverEsc()
        {
            var menu = Object.FindFirstObjectByType<NetworkGameplayMenuController>();
            var owner = NetworkClient.localPlayer;
            // Readiness is explicit when the owner is not available, even with server avatars present.
            var missing = GameplayMenuSnapshotReader.Read(null, PreparationMenuCatalog.Load(), NetworkTime.time);
            Assert.That(missing.HealthState, Is.EqualTo(MenuDataState.Synchronizing));
            Assert.That(missing.Weapons[0].State, Is.EqualTo(MenuDataState.Synchronizing));
            var localPlayer = typeof(NetworkClient).GetProperty("localPlayer", BindingFlags.Public | BindingFlags.Static);
            try
            {
                localPlayer.SetValue(null, null);
                menu.OpenMenu(); yield return new WaitForSecondsRealtime(.25f);
                Assert.That(menu.IsOpen, Is.True, "The menu must not depend on a bound avatar.");
                Assert.That(menu.Snapshot.HealthState, Is.EqualTo(MenuDataState.Synchronizing));
                Assert.That(owner.GetComponent<PlayerMovement>().IsMenuInputBlocked, Is.False);
            }
            finally { localPlayer.SetValue(null, owner); }
            yield return null;
            Assert.That(owner.GetComponent<PlayerMovement>().IsMenuInputBlocked, Is.True);
            menu.OpenMenu(); menu.enabled = false; yield return null;
            Assert.That(owner.GetComponent<PlayerMovement>().IsMenuInputBlocked, Is.False);
            Assert.That(GameplayMenuInput.IsOpen, Is.False);
            menu.enabled = true; menu.OpenMenu();
            owner.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
            yield return GameplayMenuUiScenario.Wait(() => manager.IsRunEndScreen, "solo wipe");
            yield return null;
            Assert.That(menu.IsOpen, Is.False, "The completed run supersedes the old Esc menu.");
            menu.HandleEscape(); Assert.That(menu.IsOpen, Is.False);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (manager != null)
            {
                manager.LeavePreparationRoom();
                yield return GameplayMenuUiScenario.Wait(() => !NetworkClient.active && !NetworkServer.active && !manager.IsGameplayTransitioning, "cleanup");
            }
            BootSceneFixtureObjects.Destroy(roots); yield return null; NetworkManager.ResetStatics();
        }
    }
}
