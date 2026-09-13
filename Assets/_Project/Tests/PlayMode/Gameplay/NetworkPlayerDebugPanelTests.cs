using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class NetworkPlayerDebugPanelTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] roots;
        private GameObject gate;
        private NetworkPlayerDebugPanel Panel => Object.FindFirstObjectByType<NetworkPlayerDebugPanel>();
        private NetworkIdentity Owner => NetworkClient.localPlayer;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            const string boot = "Assets/_Project/Scenes/Boot.unity";
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(boot, LoadSceneMode.Single);
#endif
            roots = BootSceneFixtureObjects.Capture(boot);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            gate = new GameObject("Player debug test attack gate"); gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7986, false, out string error), Is.True, error);
            manager.StartHost();
            yield return WaitFor(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline &&
                Panel != null && Panel.Rows.Count == 1, "Player Debug ready");
        }

        [UnityTest]
        public IEnumerator HostRosterUsesSessionAndOfflineCheckpointAndCleansAcrossWorldChanges()
        {
            var panel = Panel;
            Assert.That(panel.Rows[0].IsSelf, Is.True);
            Assert.That(panel.Rows[0].Source, Is.EqualTo(PlayerDebugSource.ServerRecord));
            Assert.That(manager.Session.TryConnect("debug-offline", 300, out var offline, out _), Is.True);
            manager.Session.AttachAvatar(300, 991, 3);
            manager.Session.Disconnect(300, new PlayerRuntimeCheckpoint
            {
                CapturedAt = 10, PreviousAvatarId = 991,
                Health = new ServerEntityCheckpoint(new CanonicalEntityState { Health = 0, MaxHealth = 100 }, false),
                Ultimate = new PlayerUltimateSnapshot { ActiveUntil = 20 }
            });
            Assert.That(manager.Session.TryConnect("debug-loading", 301, out var loading, out _), Is.True);
            yield return WaitFor(() => panel.Rows.Count == 3, "All session participants visible");
            Assert.That(panel.Rows.Select(r => r.ParticipantId), Is.Ordered);
            var offlineRow = panel.Rows.Single(r => r.ParticipantId == offline.Id);
            Assert.That(offlineRow.Details, Does.Contain("Active 10s"));
            Assert.That(panel.Rows.Single(r => r.ParticipantId == loading.Id).Canonical, Is.Null);
            panel.SelectParticipant(offline.Id);
            panel.SetExpanded(false);
            yield return new WaitForSecondsRealtime(.3f);
            panel.SetExpanded(true);
            Assert.That(panel.SelectedParticipant, Is.EqualTo(offline.Id));
            Assert.That(panel.Rows.Single(r => r.ParticipantId == offline.Id).Details, Is.EqualTo(offlineRow.Details));
            var world = NetworkCombatWorld.Instance;
            world.enabled = false;
            yield return WaitFor(() => panel.Rows.Count == 0, "World disabled");
            world.enabled = true;
            yield return WaitFor(() => panel.Rows.Count == 3, "World restored");
            panel.enabled = false;
            Assert.That(panel.Rows, Is.Empty);
            panel.enabled = true;
            yield return WaitFor(() => panel.Rows.Count == 3, "Panel reenabled");
            manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Unload");
            Assert.That(panel == null, Is.True);
        }

        [UnityTest]
        public IEnumerator OwnerDiagnosticsArriveForQueuedRewardsWithoutOffersAndSelectionPreservesExpandedPreference()
        {
            var selection = Owner.GetComponent<NetworkModifierSelection>();
            yield return WaitFor(() => selection.TryReadDebugState(false, out _), "Initial Owner diagnostic baseline");
            typeof(NetworkModifierSelection).GetField("ownerCanSelect", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(selection, false);
            selection.ServerQueueUpgrades(2);
            Assert.That(selection.PendingEventId, Is.Zero);
            yield return WaitFor(() => selection.TryReadDebugState(false, out var data) && data.PendingUpgradeCount == 2,
                "Queue without published offers must still be visible to Owner");
            Assert.That(selection.TryReadDebugState(true, out var server), Is.True);
            Assert.That(selection.TryReadDebugState(false, out var owner), Is.True);
            Assert.That(owner.Equals(server), Is.True);
            var panel = Panel;
            panel.SelectParticipant(panel.Rows[0].ParticipantId);
            ulong selected = panel.SelectedParticipant;
            typeof(NetworkModifierSelection).GetField("ownerCanSelect", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(selection, true);
            yield return WaitFor(() => Object.FindFirstObjectByType<CardPickMenu>().IsOpen, "Selection opens");
            Assert.That(panel.AreDetailsVisible, Is.False);
            Assert.That(panel.Expanded, Is.True);
            selection.ServerCancelPending();
            yield return WaitFor(() => panel.AreDetailsVisible && selection.TryReadDebugState(false, out var state) && state.PendingUpgradeCount == 0,
                "Closing selection restores details");
            Assert.That(panel.SelectedParticipant, Is.EqualTo(selected));
        }

        private static IEnumerator WaitFor(Func<bool> predicate, string message)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, message);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (manager != null && NetworkServer.active) manager.StopHost();
            if (manager != null) yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "teardown");
            BootSceneFixtureObjects.Destroy(roots);
            if (gate != null) Object.Destroy(gate);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
