using System;
using System.Collections;
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
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class NetworkPlayerDashPlayModeTests
    {
        private const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private GameObject gate;

        [UnityTest]
        public IEnumerator Host_RealMovementSpendsEachChargeOnceAndRebindDoesNotRefund()
        {
            yield return StartHostFixture();
            var identity = NetworkClient.localPlayer;
            var dash = identity.GetComponent<NetworkPlayerDash>();
            var movement = identity.GetComponent<PlayerMovement>();
            var adapter = identity.GetComponent<NetworkWeaponCombatAdapter>();
            Assert.That(movement.IsLocalOwnerBound, Is.True);
            Assert.That(dash.OwnerRuntime.AvailableCharges, Is.EqualTo(2));
            Assert.That(dash.CaptureServerState().RechargeReadyAt, Is.Empty);
            int starts = 0, ends = 0;
            movement.OnDashStart += () => starts++;
            movement.OnDashEnd += () => ends++;
            movement.body.position = new Vector2(1000, 1000);
            movement.transform.position = new Vector3(1000, 1000, 0);
            movement.SetDirection(Vector2.right);
            Physics2D.SyncTransforms();
            movement.Dash();
            yield return WaitFor(() => dash.AcceptedUseCount == 1 && starts == 1 && dash.PendingOwnerUseCount == 0,
                "The buffered movement input must be confirmed once on Host.");
            Assert.That(dash.OwnerRuntime.AvailableCharges, Is.EqualTo(1));
            Assert.That(dash.CaptureServerState().RechargeReadyAt.Length, Is.EqualTo(1),
                "Host prediction and server authority must not spend the same Runtime instance twice.");
            movement.CancelDash();
            Assert.That(ends, Is.EqualTo(1));
            Assert.That(dash.OwnerRuntime.AvailableCharges, Is.EqualTo(1));
            yield return WaitFor(() => NetworkTime.time >= dash.OwnerRuntime.NextUseAt, "The original chain delay must finish.");
            movement.Dash();
            yield return WaitFor(() => dash.AcceptedUseCount == 2 && starts == 2 && dash.PendingOwnerUseCount == 0,
                "The second independent charge must be accepted.");
            movement.CancelDash();
            Assert.That(dash.OwnerRuntime.AvailableCharges, Is.Zero);
            Assert.That(dash.CaptureServerState().RechargeReadyAt.Length, Is.EqualTo(2));
            movement.Dash();
            yield return null;
            Assert.That(starts, Is.EqualTo(2), "An empty resource cannot start another animation or attack.");

            PlayerDashSnapshot spent = dash.CaptureServerState();
            dash.enabled = false;
            Assert.That(movement.OwnerDashRuntime, Is.Null);
            dash.enabled = true;
            yield return WaitFor(() => dash.HasOwnerBaseline, "Re-enabling the adapter must request the canonical resource.");
            Assert.That(dash.OwnerRuntime.AvailableCharges, Is.Zero);
            Assert.That(dash.CaptureServerState().RechargeReadyAt, Is.EqualTo(spent.RechargeReadyAt));
            yield return WaitFor(() => dash.OwnerRuntime.AvailableCharges > 0, "A spent charge must return at its absolute deadline.");
            Assert.That(starts, Is.EqualTo(2), "Old buffered input cannot replay after a new baseline.");
            Assert.That(adapter.AcceptedCooldownReportCount, Is.Zero, "Dash resources do not use ordinary weapon cooldown admission.");
            Assert.That(dash.RejectedUseCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Host_CommandRejectsInvalidMotionAndRevisionAndUsesCanonicalDuration()
        {
            yield return StartHostFixture();
            var identity = NetworkClient.localPlayer;
            var dash = identity.GetComponent<NetworkPlayerDash>();
            var movement = identity.GetComponent<PlayerMovement>();
            var bridge = identity.GetComponent<MirrorNetworkCombatBridge>();
            uint revision = identity.GetComponent<NetworkModifierSelection>().OwnerBuildRevision;
            movement.body.position = new Vector2(1000, 1000);
            movement.transform.position = new Vector3(1000, 1000, 0);
            Physics2D.SyncTransforms();
            Assert.That(movement.TryGetDashMotionParameters(Vector2.right, movement.transform.position, out var motion), Is.True);
            var invalid = motion; invalid.Duration = float.NaN;
            SendDashCommand(dash, bridge.EventIds.Next().Value, revision, invalid);
            yield return WaitFor(() => dash.RejectedUseCount == 1, "Non-finite motion must be rejected over the actual Mirror Command.");
            SendDashCommand(dash, bridge.EventIds.Next().Value, revision + 1, motion);
            yield return WaitFor(() => dash.RejectedUseCount == 2, "A stale Build cannot authorize a dash.");
            Assert.That(dash.CaptureServerState().RechargeReadyAt, Is.Empty);
            Assert.That(dash.OwnerRuntime.AvailableCharges, Is.EqualTo(2));

            ulong legalId = bridge.EventIds.Next().Value;
            var approximate = motion; approximate.Duration -= 0.009f;
            double sentAt = NetworkTime.time;
            SendDashCommand(dash, legalId, revision, approximate);
            yield return WaitFor(() => dash.AcceptedUseCount == 1, "A within-tolerance motion must be accepted.");
            PlayerDashSnapshot accepted = dash.CaptureServerState();
            Assert.That(accepted.NextUseAt, Is.GreaterThanOrEqualTo(sentAt + motion.Duration + PlayerDashRuntime.ChainDelaySeconds - 0.001d),
                "Tolerance is a reception bound, not permission to shorten the authoritative duration.");
            Assert.That(accepted.RechargeReadyAt.Length, Is.EqualTo(1));
            SendDashCommand(dash, legalId, revision, motion);
            yield return WaitFor(() => dash.RejectedUseCount == 3, "Duplicate use identity must not consume twice.");
            SendDashCommand(dash, bridge.EventIds.Next().Value, revision, motion);
            yield return WaitFor(() => dash.RejectedUseCount == 4, "A fresh ID cannot bypass the active chain delay.");
            Assert.That(dash.CaptureServerState().RechargeReadyAt, Is.EqualTo(accepted.RechargeReadyAt));
            Assert.That(dash.OwnerRuntime.AvailableCharges, Is.EqualTo(1));
        }

        private static void SendDashCommand(NetworkPlayerDash dash, ulong useId, uint revision, DashMotionParameters motion)
        {
            // Exercise the woven wrapper so Mirror supplies the authenticated sender.
            typeof(NetworkPlayerDash).GetMethod("CmdUseDash", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(dash, new object[] { useId, revision, NetworkTime.time, motion, null });
        }

        private IEnumerator StartHostFixture()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BootPath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(BootPath, LoadSceneMode.Single);
#endif
            bootRoots = BootSceneFixtureObjects.Capture(BootPath);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7951, false, out string error), Is.True, error);
            gate = new GameObject("Dash resource fixture gate");
            gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            SceneManager.sceneLoaded += PrepareGameplay;
            manager.StartHost();
            yield return WaitFor(() => manager.IsGameplayLoaded && NetworkClient.localPlayer != null &&
                NetworkClient.localPlayer.GetComponent<NetworkPlayerDash>().HasOwnerBaseline &&
                NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline,
                "Dash and Build must both receive their post-spawn Owner baselines.");
        }

        private static void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true)) spawner.enabled = false;
        }
        private static IEnumerator WaitFor(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= PrepareGameplay;
            if (manager != null)
            {
                if (NetworkServer.active && NetworkClient.active) manager.StopHost();
                else if (NetworkServer.active) manager.StopServer();
                else if (NetworkClient.active) manager.StopClient();
                yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Gameplay must unload.");
            }
            if (gate != null) Object.Destroy(gate);
            BootSceneFixtureObjects.Destroy(bootRoots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
