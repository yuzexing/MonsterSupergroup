#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AstralShift.DebugTools;
using AstralShift.FSM;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Interactions;
using Mirror;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class SteamLagDiagnosticsPlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] roots;
        private GameObject diagnosticRoot;
        private static IEnumerator Wait(Func<bool> condition, string phase) => EnemyDefinitionRuntimeFixture.Wait(condition, phase, 40);
        [UnityTest]
        public IEnumerator OrdinaryHostWritesVersionedBoundedSamplesAndClosesCompletely()
        {
            yield return StartHost();
            diagnosticRoot = new GameObject("Diagnostics fixture");
            var observer = diagnosticRoot.AddComponent<NetworkDiagnosticsObservation>();
            yield return new WaitForSecondsRealtime(2.2f);
            string path = observer.OutputPath;
            Assert.That(File.Exists(path), Is.True);
            UnityEngine.Object.Destroy(diagnosticRoot); yield return null;
            string text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("\"schemaVersion\":2"));
            Assert.That(text, Does.Contain("\"role\":\"host\""));
            Assert.That(text, Does.Contain("\"captureId\""));
            Assert.That(text, Does.Contain("\"frameHistogram\""));
            Assert.That(text, Does.Contain("\"areas\""));
            Assert.That(File.ReadAllText(path + ".status.json"), Does.Contain("\"complete\":true"));
            TestContext.WriteLine("DIAGNOSTICS=" + path);
        }
        [UnityTest]
        public IEnumerator DeadPlayerContactReproducesInvalidHurtRequestsWithoutChangingGameLogic()
        {
            yield return StartHost();
            var player = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            var stateField = typeof(PlayerMovement).GetField("_stateMachine", BindingFlags.Instance | BindingFlags.NonPublic);
            var deadField = typeof(PlayerMovement).GetField("Dead", BindingFlags.Instance | BindingFlags.NonPublic);
            var machine = (StateMachine)stateField.GetValue(player);
            // Keep the session running while reproducing the reported FSM state; this is a controlled
            // collision-entry experiment, not a claim to reproduce the remote three-player match.
            typeof(StateMachine).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(machine, new[] { deadField.GetValue(player) });
            var hitbox = NetworkClient.localPlayer.GetComponentInChildren<PlayerHitbox>(true);
            Assert.That(hitbox, Is.Not.Null);
            diagnosticRoot = new GameObject("Repeated contact fixture");
            var contact = diagnosticRoot.AddComponent<PlayerDamageInteraction>();
            typeof(PlayerDamageInteraction).GetField("directDamage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(contact, true);
            typeof(PlayerDamageInteraction).GetField("damage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(contact, 100);
            CombatPerformanceCounters.Enabled = true; CombatPerformanceCounters.Reset();
            int before = NetworkClient.localPlayer.GetComponent<PlayerCombatantBinding>().CurrentHealth;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) contact.DamagePlayer(hitbox);
            timer.Stop();
            var sample = CombatPerformanceCounters.ReadAndReset();
            Assert.That(sample.calls[(int)CombatPerformanceCounters.Area.DamageRequests], Is.EqualTo(100));
            Assert.That(sample.invalidDeadTransitions, Is.EqualTo(100));
            Assert.That(NetworkClient.localPlayer.GetComponent<PlayerCombatantBinding>().CurrentHealth, Is.EqualTo(before));
            TestContext.WriteLine($"DEAD_CONTACT requests=100 invalidTransitions={sample.invalidDeadTransitions} elapsedMs={timer.Elapsed.TotalMilliseconds:F3} damageMs={sample.milliseconds[1]:F3} transitionMs={sample.milliseconds[2]:F3}");
            // The assertion documents a known defect; suppressing the warnings is not a fix.
            CombatPerformanceCounters.Enabled = false;
        }
        private IEnumerator StartHost()
        {
            GameOptionsService.EnsureInitialized();
            yield return Wait(() => GameLocalization.IsReady, "localization");
            yield return null; yield return null;
            const string boot = "Assets/_Project/Scenes/Boot.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot, new LoadSceneParameters(LoadSceneMode.Single));
            roots = BootSceneFixtureObjects.Capture(boot);
            manager = (BootGameplayNetworkManager)NetworkManager.singleton;
            manager.ConfigurePreparationFlow(false);
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7987, false, out string error), Is.True, error);
            manager.StartHost();
            yield return Wait(() => NetworkClient.localPlayer != null && manager.CanBeginRun(out _), "owner ready");
            manager.BeginRun();
            yield return Wait(() => manager.IsGameplayLoaded, "gameplay");
            yield return new WaitForSecondsRealtime(.5f);
        }
        [UnityTearDown]
        public IEnumerator Stop()
        {
            CombatPerformanceCounters.Enabled = false; CombatPerformanceCounters.Reset();
            if (diagnosticRoot != null) UnityEngine.Object.Destroy(diagnosticRoot);
            if (manager != null && NetworkServer.active) manager.StopHost();
            if (manager != null) yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "cleanup");
            BootSceneFixtureObjects.Destroy(roots); yield return null; NetworkManager.ResetStatics();
        }
    }
}
#endif
