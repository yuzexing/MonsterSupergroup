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
    public sealed class GameplayWavePlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private GameObject gate;
        private GameplayWaveRules rulesCopy;
        private NetworkGameplayEnemySpawner Spawner => Object.FindFirstObjectByType<NetworkGameplayEnemySpawner>();
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkWaveProgress Progress => NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>();

        private IEnumerator StartHost(bool legacy = false)
        {
            const string path = "Assets/_Project/Scenes/Boot.unity";
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
#endif
            bootRoots = BootSceneFixtureObjects.Capture(path);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            Assert.That(manager.TryBeginRun(out _), Is.False);
            gate = new GameObject("M5 runtime attack gate");
            gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7968, false, out string error), Is.True, error);
            if (legacy) SceneManager.sceneLoaded += ConfigureLegacy;
            manager.StartHost();
            Assert.That(manager.TryBeginRun(out _), Is.False, "An incomplete Gameplay load cannot lock the roster.");
            yield return WaitFor(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline && manager.CanBeginRun(out _));
            Assert.That(Spawner.UsesWaves, Is.EqualTo(!legacy));
            NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(Owner.netId, true);
            Owner.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(true);
        }
        [UnityTest]
        public IEnumerator FormalBoot_WaitsForStart_UsesOneSchedule_AndRendersAuthoritativeState()
        {
            yield return StartHost();
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(Spawner.CountCanonicalEnemies(), Is.Zero);
            Assert.That(Progress.Snapshot.Phase, Is.EqualTo(WavePhase.Waiting));
            Assert.That(manager.TryBeginRun(out string error), Is.True, error);
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            var before = Spawner.ServerProgress;
            Assert.That(manager.TryBeginRun(out _), Is.True);
            Assert.That(Spawner.ServerProgress.TotalSpawned, Is.EqualTo(1));
            Assert.That(Spawner.ServerProgress.Elapsed, Is.EqualTo(before.Elapsed));
            yield return WaitFor(() => Progress.Snapshot.TotalSpawned == 1);
            var hud = Object.FindFirstObjectByType<NetworkWaveHUD>();
            Assert.That(hud, Is.Not.Null);
            yield return null;
            Assert.That(hud.DisplayedSnapshot.RunId, Is.EqualTo(manager.Session.RunId));
            Assert.That(hud.GetComponent<WaveProgressHUD>().Content, Does.Contain("Wave 1"));
            var enemy = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>();
            // A local prediction must not change the server's capacity read.
            enemy.GetComponent<CombatantBehaviour>().ApplyCanonicalHealth(0, 100, 999);
            Assert.That(Spawner.CountCanonicalEnemies(), Is.EqualTo(1));
            hud.enabled = false;
            Assert.That(hud.DisplayedSnapshot.RunId, Is.Null);
            Assert.That(hud.GetComponent<WaveProgressHUD>().Content, Is.Empty);
        }
        [UnityTest]
        public IEnumerator Capacity_SkipsUntilNextOpportunity_AndCanonicalDeathReleasesSpace()
        {
            yield return StartHost();
            UseFastRules(1.5f, .2f, 2);
            Assert.That(manager.TryBeginRun(out _), Is.True);
            yield return WaitFor(() => Spawner.ServerProgress.TotalSkipped >= 1);
            Assert.That(Spawner.CountCanonicalEnemies(), Is.EqualTo(2));
            long produced = Spawner.ServerProgress.TotalSpawned;
            var enemy = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>();
            SetCanonicalHealth(enemy.netId, 0);
            Assert.That(Spawner.CountCanonicalEnemies(), Is.EqualTo(1));
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned > produced);
            Assert.That(Spawner.CountCanonicalEnemies(), Is.EqualTo(2));
            Assert.That(Spawner.ServerProgress.TotalSpawned, Is.EqualTo(produced + 1));
        }
        [UnityTest]
        public IEnumerator InvalidConfigurationBlocksStart_AndCapturedRulesIgnoreLaterChanges()
        {
            yield return StartHost();
            UseFastRules(1.5f, .2f, 2);
            Set(rulesCopy, "maximumAlive", 0);
            Assert.That(manager.TryBeginRun(out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(manager.Session.IsRosterLocked, Is.False);
            Assert.That(Spawner.CountCanonicalEnemies(), Is.Zero);
            Set(rulesCopy, "maximumAlive", 2);
            Assert.That(manager.TryBeginRun(out _), Is.True);
            Set(rulesCopy, "maximumAlive", 30);
            yield return WaitFor(() => Spawner.ServerProgress.TotalSkipped >= 2);
            Assert.That(Spawner.ServerProgress.Limit, Is.EqualTo(2));
            Assert.That(Spawner.CountCanonicalEnemies(), Is.EqualTo(2));
        }
        [UnityTest]
        public IEnumerator DeathPauses_DisableStops_AndStopRestartCreatesFreshWaitingRun()
        {
            yield return StartHost();
            UseFastRules(1.5f, .2f, 30);
            manager.BeginRun();
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            SetCanonicalHealth(Owner.netId, 0);
            yield return WaitFor(() => Spawner.ServerProgress.Phase == WavePhase.Paused);
            double paused = Spawner.ServerProgress.Elapsed;
            long total = Spawner.ServerProgress.TotalSpawned;
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(Spawner.ServerProgress.Elapsed, Is.EqualTo(paused));
            Assert.That(Spawner.ServerProgress.TotalSpawned, Is.EqualTo(total));
            Spawner.enabled = false;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(Progress.Snapshot.Phase, Is.EqualTo(WavePhase.Stopped));
            string previous = manager.Session.RunId;
            manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
            Assert.That(Object.FindObjectsByType<NetworkWaveHUD>(FindObjectsSortMode.None), Is.Empty);
            manager.StartHost();
            yield return WaitFor(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline && manager.CanBeginRun(out _));
            Assert.That(manager.Session.RunId, Is.Not.EqualTo(previous));
            Assert.That(Spawner.CountCanonicalEnemies(), Is.Zero);
            Assert.That(manager.TryBeginRun(out _), Is.True);
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            Assert.That(Spawner.ServerProgress.Wave, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator GroundCornerPlacement_KeepsEnemyBodyInsideApprovedBounds()
        {
            yield return StartHost();
            Bounds bounds = Spawner.BoundaryGround.bounds;
            Owner.transform.position = new Vector3(bounds.max.x - .5f, bounds.max.y - .5f, 0);
            manager.BeginRun();
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            var enemy = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>();
            var body = enemy.GetComponent<AstralShift.HellMaiden.AI.Enemy.EnemyController>().collider;
            Assert.That(body.bounds.min.x, Is.GreaterThanOrEqualTo(bounds.min.x));
            Assert.That(body.bounds.min.y, Is.GreaterThanOrEqualTo(bounds.min.y));
            Assert.That(body.bounds.max.x, Is.LessThanOrEqualTo(bounds.max.x));
            Assert.That(body.bounds.max.y, Is.LessThanOrEqualTo(bounds.max.y));
            Assert.That(Vector2.Distance(enemy.transform.position, Owner.transform.position), Is.EqualTo(5).Within(.15));
        }

        [UnityTest]
        public IEnumerator SelectingUpgrade_DoesNotPauseWaveClock_AndBothStagesRemainUsable()
        {
            yield return StartHost();
            UseFastRules(1.5f, .2f, 30);
            manager.BeginRun();
            var selection = Owner.GetComponent<NetworkModifierSelection>();
            var view = Owner.GetComponent<ModifierSelectionController>();
            Assert.That(selection.RequestDebugLevelUp(), Is.True);
            yield return WaitFor(() => selection.IsSelecting && view.Offers.Count > 0);
            double before = Spawner.ServerProgress.Elapsed;
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(Spawner.ServerProgress.Elapsed, Is.GreaterThan(before));
            Assert.That(Spawner.ServerProgress.Phase, Is.EqualTo(WavePhase.Running));
            Assert.That(Object.FindFirstObjectByType<WaveProgressHUD>().Content, Does.Contain("Wave"));
            Assert.That(Object.FindFirstObjectByType<CardPickMenu>().IsOpen, Is.True);
            Assert.That(view.Select(0).Succeeded, Is.True);
            yield return WaitFor(() => view.Stage == UpgradeSelectionStage.EquipmentTarget && !view.IsRequestPending);
            Assert.That(view.Select(0).Succeeded, Is.True);
            yield return WaitFor(() => !selection.IsSelecting && selection.PendingUpgradeCount == 0);
            Assert.That(selection.BuildRevision, Is.EqualTo(2));
        }
        [UnityTest]
        public IEnumerator ExplicitLegacyMode_SpawnsOneMemberEnemy_AndEnableDoesNotDuplicateIt()
        {
            yield return StartHost(legacy: true);
            yield return WaitFor(() => Spawner.SpawnedPlayerCount == 1);
            Assert.That(Spawner.CountCanonicalEnemies(), Is.EqualTo(1));
            Spawner.enabled = false;
            yield return null;
            Spawner.enabled = true;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(Spawner.CountCanonicalEnemies(), Is.EqualTo(1));
            Assert.That(Spawner.SpawnedPlayerCount, Is.EqualTo(1));
        }
        private static void ConfigureLegacy(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                    spawner.Configure(spawner.EnemyPrefab, 5);
        }
        private void UseFastRules(float duration, float interval, int limit)
        {
            rulesCopy = Object.Instantiate((GameplayWaveRules)typeof(NetworkGameplayEnemySpawner)
                .GetField("waveRules", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Spawner));
            Set(rulesCopy, "waveDuration", duration); Set(rulesCopy, "spawnInterval", interval); Set(rulesCopy, "maximumAlive", limit);
            Set(Spawner, "waveRules", rulesCopy);
        }
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        internal static void SetCanonicalHealth(uint id, int health)
        {
            var world = NetworkCombatWorld.Instance;
            var captured = world.Gateway.Ledger.CaptureEntityState(id);
            var state = captured.State; state.Health = health; state.Alive = health > 0; state.StateVersion++;
            world.RestorePlayerState(id, new PlayerRuntimeCheckpoint { PreviousAvatarId = id,
                Health = new ServerEntityCheckpoint(state, false) });
        }
        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, "Formal wave scenario timed out.");
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= ConfigureLegacy;
            if (manager != null)
            {
                if (NetworkServer.active && NetworkClient.active) manager.StopHost();
                else if (NetworkServer.active) manager.StopServer();
                yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
            }
            BootSceneFixtureObjects.Destroy(bootRoots);
            if (gate != null) Object.Destroy(gate);
            if (rulesCopy != null) Object.Destroy(rulesCopy);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
