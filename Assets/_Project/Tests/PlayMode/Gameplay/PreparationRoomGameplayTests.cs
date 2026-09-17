using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class PreparationRoomGameplayTests
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
            manager = NetworkManager.singleton as BootGameplayNetworkManager;
            Assert.That(manager, Is.Not.Null);
            manager.ConfigurePreparationFlow(true);
        }
        [UnityTest] public IEnumerator PreparationHasNoAvatarsOrCombat_OfflineHasNoListener()
        {
            yield return Open();
            Assert.That(manager.IsGameplayLoaded, Is.False);
            Assert.That(manager.IsGameplayTransitioning, Is.False);
            Assert.That(NetworkClient.localPlayer, Is.Null);
            Assert.That(NetworkServer.spawned.Values.Any(identity => identity.GetComponent<PlayerBuildRuntime>() != null ||
                identity.GetComponent<NetworkEnemySimulationAgent>() != null), Is.False,
                "Boot may retain its shared World identity, but preparation must have no combat actors.");
            Assert.That(NetworkServer.listen, Is.False);
            Assert.That(manager.transport.ServerActive(), Is.False);
            Assert.That(manager.RoomSnapshot.Members.Length, Is.EqualTo(1));
            Assert.That(manager.RoomSnapshot.Members[0].IsHost, Is.True);
            Assert.That(manager.RoomSnapshot.Members[0].WeaponId, Is.EqualTo(2));
            Assert.That(manager.TryBeginRun(out _), Is.False);
        }
        [UnityTest] public IEnumerator AllSixWeapons_ConfigureBeforeBuild_NoDefaultLeftovers()
        {
            foreach (uint id in new uint[] { 1, 2, 3, 6, 8, 402 })
            {
                yield return Open();
                ulong participant = manager.RoomSnapshot.SelfId;
                manager.SetOwnLoadout(id);
                yield return Until(() => manager.RoomSnapshot.Members[0].WeaponId == id);
                manager.StartPreparedGame(); manager.StartPreparedGame();
                yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame);
                var avatar = NetworkClient.localPlayer;
                Assert.That(avatar.GetComponent<NetworkRunParticipant>().ParticipantId, Is.EqualTo(participant));
                var build = avatar.GetComponent<PlayerBuildRuntime>();
                Assert.That(build.InitialWeaponId, Is.EqualTo(id));
                var snapshot = build.CaptureState();
                Assert.That(snapshot.Weapons.Length, Is.EqualTo(1), "No default weapon instance should remain.");
                Assert.That(snapshot.InitialWeaponId, Is.EqualTo(id));
                Assert.That(manager.Session.IsRosterLocked && manager.Session.IsRunStarted, Is.True);
                yield return null;
                Assert.That(avatar.GetComponent<PlayerMovement>().IsRunLoadingLocked, Is.False);
                manager.LeavePreparationRoom();
                yield return Until(() => !NetworkServer.active && !manager.IsLeavingRoom && !manager.IsGameplayLoaded);
            }
        }
        [UnityTest] public IEnumerator StaleReadyAfterLoadoutChangeIsRejected()
        {
            yield return Open();
            var old = manager.RoomSnapshot;
            manager.SetOwnLoadout(6);
            yield return Until(() => manager.RoomSnapshot.Members[0].WeaponId == 6);
            NetworkClient.Send(new RequestSetReady { RunId = old.RunId, LoadoutRevision = old.Members[0].LoadoutRevision, Ready = true });
            yield return Until(() => !string.IsNullOrEmpty(manager.MenuNotice));
            Assert.That(manager.RoomSnapshot.Members[0].Ready, Is.False);
        }
        [UnityTest] public IEnumerator LoadingBarrierBlocksMovementDashAndWeaponsUntilOwnerBaselineAcknowledged()
        {
            yield return manager.EnsureMainMenu();
            var menu = UnityEngine.Object.FindFirstObjectByType<PreparationMenuView>();
            Assert.That(menu, Is.Not.Null);
            var menuCanvas = menu.GetComponentInChildren<Canvas>(true);
            var menuEvents = menu.GetComponentInChildren<UnityEngine.EventSystems.EventSystem>(true);
            yield return Open();
            manager.SetOwnLoadout(6);
            yield return Until(() => manager.RoomSnapshot.Members[0].WeaponId == 6);
            manager.StartPreparedGame();
            yield return Until(() => manager.ServerRoom?.Phase == PreparationPhase.Loading);
            GameplayReady captured = default;
            NetworkServer.RegisterHandler<GameplayReady>((connection, message) => captured = message);
            yield return Until(() => captured.AvatarId != 0);
            Assert.That(menuCanvas.gameObject.activeInHierarchy, Is.True, "Keep loading UI until all owners are ready.");
            Assert.That(menuEvents.enabled, Is.True);
            var spawner = UnityEngine.Object.FindFirstObjectByType<NetworkGameplayEnemySpawner>();
            Assert.That(spawner.CanBeginWaveRun(out string waveError), Is.True, waveError);
            var avatar = NetworkClient.localPlayer;
            var movement = avatar.GetComponent<PlayerMovement>();
            var body = avatar.GetComponent<Rigidbody2D>();
            Vector2 position = body.position;
            var dash = avatar.GetComponent<NetworkPlayerDash>();
            int accepted = dash.AcceptedUseCount;
            for (int i = 0; i < 10; i++)
            {
                movement.SetDirection(Vector2.right); movement.SetDirectionImmediate(Vector2.up); movement.Dash();
                Assert.That(avatar.GetComponent<NetworkPlayerUltimate>().RequestUse(), Is.False);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(Vector2.Distance(position, body.position), Is.LessThan(.01f));
            Assert.That(dash.AcceptedUseCount, Is.EqualTo(accepted));
            Assert.That(avatar.GetComponent<PlayerBuildRuntime>().InitialWeapon.CanAttack, Is.False);
            Assert.That(manager.Session.IsRunStarted, Is.False);
            var receive = typeof(BootGameplayNetworkManager).GetMethod("ReceiveGameplayReady", BindingFlags.Instance | BindingFlags.NonPublic);
            NetworkServer.RegisterHandler<GameplayReady>((connection, message) => receive.Invoke(manager, new object[] { connection, message }));
            NetworkClient.Send(captured);
            yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame);
            yield return null;
            Assert.That(movement.IsRunLoadingLocked, Is.False);
            Assert.That(menuCanvas.gameObject.activeInHierarchy, Is.False, "The preparation UI must hide when combat starts.");
            Assert.That(menuEvents.enabled, Is.False, "The hidden menu must release input.");
            manager.LeavePreparationRoom();
            yield return Until(() => !NetworkServer.active && !manager.IsLeavingRoom && !manager.IsGameplayLoaded);
            yield return null;
            Assert.That(menuCanvas.gameObject.activeInHierarchy, Is.True, "Leaving combat must restore the menu.");
            Assert.That(menuEvents.enabled, Is.True);
        }
        [UnityTest] public IEnumerator LoadingTimeoutAbortsAndAllowsANewRoom()
        {
            yield return Open();
            string previous = manager.RoomSnapshot.RunId;
            manager.StartPreparedGame();
            yield return Until(() => manager.ServerRoom?.Phase == PreparationPhase.Loading);
            typeof(BootGameplayNetworkManager).GetField("preparationLoadDeadline", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(manager, Time.realtimeSinceStartupAsDouble - 1);
            yield return Until(() => !NetworkServer.active && !manager.IsLeavingRoom && !manager.IsGameplayTransitioning);
            Assert.That(manager.MenuNotice, Does.Contain("120"));
            yield return Open();
            Assert.That(manager.RoomSnapshot.RunId, Is.Not.EqualTo(previous));
            Assert.That(NetworkClient.localPlayer, Is.Null);
        }
        [UnityTest] public IEnumerator MissingGameplaySceneCancelsImmediately()
        {
            yield return Open();
            manager.ConfigureGameplay("Assets/_Project/Scenes/Missing.unity", null, null);
            manager.StartPreparedGame();
            yield return Until(() => !NetworkServer.active && !manager.IsLeavingRoom);
            Assert.That(manager.MenuNotice, Does.Contain("无法加载"));
        }
        private IEnumerator Open()
        {
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing && manager.RoomSnapshot.SelfId != 0);
        }
        private static IEnumerator Until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Preparation/Gameplay transition timed out.");
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (manager != null)
            {
                manager.LeavePreparationRoom();
                yield return Until(() => !NetworkServer.active && !NetworkClient.active && !manager.IsGameplayTransitioning);
            }
            BootSceneFixtureObjects.Destroy(roots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
