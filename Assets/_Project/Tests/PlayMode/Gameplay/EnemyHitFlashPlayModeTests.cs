using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.GAS;
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
    public sealed class EnemyHitFlashPlayModeTests
    {
        private const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private GameObject enemyPrefab, gate;
        private readonly List<EnemyHitPresentation> played = new List<EnemyHitPresentation>();

        [UnityTest]
        public IEnumerator SameFrameCheckpointDoesNotInterruptLaterEnemySnapshots()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BootPath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(BootPath, LoadSceneMode.Single);
#endif
            bootRoots = BootSceneFixtureObjects.Capture(BootPath);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            gate = new GameObject("Snapshot fixture attack gate");
            gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            SceneManager.sceneLoaded += PrepareGameplay;
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7987, false, out var error), Is.True, error);
            manager.StartHost();
            yield return WaitFor(() => manager.IsGameplayLoaded && NetworkClient.localPlayer != null, "Gameplay owner");
            manager.BeginRun();
            var agents = new NetworkEnemySimulationAgent[2];
            for (int i = 0; i < agents.Length; i++)
            {
                var root = Object.Instantiate(enemyPrefab, new Vector3(20 + i * 5, 20, 0), Quaternion.identity);
                agents[i] = root.GetComponent<NetworkEnemySimulationAgent>();
                agents[i].ConfigureRuntimeMinimumHealthOverride(10000);
                agents[i].ConfigureInitialServerTarget(NetworkClient.localPlayer.netId);
                NetworkServer.Spawn(root);
            }
            yield return WaitFor(() => agents[0].ProductEnemyInitialized && agents[1].ProductEnemyInitialized, "Two product enemies");
            var world = NetworkEnemySimulationWorld.Instance;
            double now = NetworkTime.time;
            foreach (var agent in agents)
            {
                agent.SetServerAssignment(world.Registry.AssignServerAuthoritative(agent.netId, NetworkClient.localPlayer.netId));
                Assert.That(agent.TryCaptureSnapshot(now - .01d, out var baseline), Is.True);
                world.Registry.RecordServerSnapshot(baseline);
            }
            world.Registry.TryGetLatestSnapshot(agents[0].netId, out var checkpoint);
            checkpoint.SampleNetworkTime = now;
            checkpoint.Sequence = 0;
            world.Registry.RecordCheckpoint(new EnemySimulationCheckpoint { Movement = checkpoint });
            typeof(NetworkEnemySimulationWorld).GetField("nextServerSnapshotTime", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(world, 0d);
            Assert.DoesNotThrow(() => typeof(NetworkEnemySimulationWorld).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world, null));
            world.Registry.TryGetLatestSnapshot(agents[0].netId, out var first);
            world.Registry.TryGetLatestSnapshot(agents[1].netId, out var second);
            Assert.That(first.Sequence, Is.Zero, "The equal-time reliable checkpoint remains the handoff seed.");
            Assert.That(second.SampleNetworkTime, Is.EqualTo(now));
            Assert.That(second.Sequence, Is.EqualTo(2), "A previous enemy must not abort this enemy's movement broadcast.");
            foreach (var agent in agents) NetworkServer.Destroy(agent.gameObject);
            Debug.Log("[SnapshotDotFix] two-enemy same-frame checkpoint/movement broadcast PASS");
        }

        [UnityTest]
        public IEnumerator ProductEnemyPresentsPredictionAndRemoteDamageOnceWithoutStartingCombatFsm()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BootPath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(BootPath, LoadSceneMode.Single);
#endif
            bootRoots = BootSceneFixtureObjects.Capture(BootPath);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            gate = new GameObject("Enemy flash fixture attack gate");
            gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            SceneManager.sceneLoaded += PrepareGameplay;
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7987, false, out var error), Is.True, error);
            manager.StartHost();
            yield return WaitFor(() => manager.IsGameplayLoaded && NetworkClient.localPlayer != null, "Gameplay owner");
            manager.BeginRun();
            var owner = NetworkClient.localPlayer;
            var root = Object.Instantiate(enemyPrefab, new Vector3(20, 20, 0), Quaternion.identity);
            var agent = root.GetComponent<NetworkEnemySimulationAgent>();
            agent.ConfigureRuntimeMinimumHealthOverride(10000);
            agent.ConfigureInitialServerTarget(owner.netId);
            NetworkServer.Spawn(root);
            yield return WaitFor(() => agent.ProductEnemyInitialized, "Product initialization");
            var enemy = root.GetComponent<EnemyController>();
            var world = NetworkCombatWorld.Instance;
            world.EnemyHitPresented += played.Add;
            Assert.That(agent.ProductMovementOnly, Is.True);
            Assert.That(enemy.StateMachine, Is.Null);
            var renderer = enemy.GetComponentInChildren<SpriteRenderer>();
            var block = new MaterialPropertyBlock();
            int blend = Shader.PropertyToID("_HitEffectBlend"), color = Shader.PropertyToID("_HitEffectColor");
            var bridge = owner.GetComponent<MirrorNetworkCombatBridge>();
            // This fixture injects presentation events without applying local damage.
            // Suppress automatic submission so the now-valid outcome cannot change HP.
            bridge.enabled = false;
            ulong id = bridge.EventIds.Next().Value;
            int hp = enemy.CurrentHealth;
            // Collector publication exercises the same synchronous bridge used by direct and status GAS damage.
            bridge.Collector.Publish(Damage(id, owner.netId, agent.netId, 10));
            Assert.That(played.Count, Is.EqualTo(1));
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetFloat(blend), Is.EqualTo(1));
            Assert.That(block.GetColor(color), Is.EqualTo(Color.white));
            Confirm(world, Hit(id, agent.netId, 2));
            Confirm(world, Hit(id, agent.netId, 2));
            Assert.That(played.Count, Is.EqualTo(1), "Prediction and repeated server echoes share one receipt.");
            bool sawBlack = false, sawSecondWhite = false;
            float end = Time.realtimeSinceStartup + 1;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                renderer.GetPropertyBlock(block);
                if (block.GetFloat(blend) == 0) break;
                if (block.GetColor(color) == Color.black) sawBlack = true;
                if (sawBlack && block.GetColor(color) == Color.white) sawSecondWhite = true;
            }
            Assert.That(sawBlack && sawSecondWhite, Is.True, "Actual shader block must alternate black and white.");
            Assert.That(block.GetFloat(blend), Is.Zero);

            // Remote damage remains visual, regardless of simulation role; each same-batch hit survives HP coalescing.
            agent.Authority.ApplyRole(EnemySimulationRole.Replica, 900, owner.netId, agent.Assignment.Epoch);
            Confirm(world, Hit(9001, agent.netId, 3), Hit(9002, agent.netId, 4));
            Assert.That(played.Count, Is.EqualTo(3));
            Confirm(world, Hit(9001, agent.netId, 3), Hit(9002, agent.netId, 4));
            Assert.That(played.Count, Is.EqualTo(3));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(hp), "Presentation must not apply damage or knockback.");
            Assert.That(enemy.IsNetworkKnockbackActive, Is.False);
            Assert.That(enemy.StateMachine, Is.Null);
            world.PresentPredictedEnemyHit(Damage(9003, owner.netId, agent.netId, 0));
            Assert.That(played.Count, Is.EqualTo(3));
            Confirm(world, Hit(9003, agent.netId, 5));
            Assert.That(played.Count, Is.EqualTo(4), "An unplayed prediction must not suppress a valid confirmation.");

            // Failed playback has no receipt: a later playable confirmation can still supply feedback.
            enemy.enemyAnimator.enabled = false;
            world.PresentPredictedEnemyHit(Damage(9004, owner.netId, agent.netId, 10));
            enemy.enemyAnimator.enabled = true;
            Confirm(world, Hit(9004, agent.netId, 6));
            Assert.That(played.Count, Is.EqualTo(5));
            world.PresentPredictedEnemyHit(Damage(9005, owner.netId, 999999, 10));
            Assert.That(played.Count, Is.EqualTo(5));

            SetInitialized(agent, false);
            Confirm(world, Hit(9006, agent.netId, 7), Hit(9007, agent.netId, 8));
            Assert.That(played.Count, Is.EqualTo(5));
            SetInitialized(agent, true);
            world.TryPresentPendingEnemyHit(agent.netId);
            Assert.That(played.Count, Is.EqualTo(6));
            Assert.That(played[5].DamageEventId, Is.EqualTo(9007));
            SetInitialized(agent, false);
            Confirm(world, Hit(9008, agent.netId, 9));
            // Prevent the agent's normal Update from undoing this deliberate initialization wait.
            agent.enabled = false;
            yield return new WaitForSecondsRealtime(.6f);
            SetInitialized(agent, true);
            world.TryPresentPendingEnemyHit(agent.netId);
            Assert.That(played.Count, Is.EqualTo(6), "Expired initialization feedback is dropped.");
            agent.enabled = true;

            // Snapshot restore contains no effects, and its newer version rejects earlier damage edges.
            SetInitialized(agent, false);
            world.PresentPredictedEnemyHit(Damage(9009, owner.netId, agent.netId, 10));
            Confirm(world, Hit(9009, agent.netId, 10));
            Apply(world, new CanonicalWorldBatch { Entities = new[] { new CanonicalEntityState
            {
                EntityId = agent.netId, Kind = (byte)CombatEntityKind.Enemy, Health = hp,
                MaxHealth = hp, Alive = true, StateVersion = 20
            } } });
            SetInitialized(agent, true);
            world.TryPresentPendingEnemyHit(agent.netId);
            Assert.That(played.Count, Is.EqualTo(6), "A newer snapshot invalidates queued old damage presentation.");
            Confirm(world, Hit(9010, agent.netId, 10));
            Assert.That(played.Count, Is.EqualTo(6));
            world.ForgetEnemyHitPresentation(agent.netId);
            world.Replica.ForgetEntity(agent.netId);
            Confirm(world, Hit(9001, agent.netId, 2));
            Assert.That(played.Count, Is.EqualTo(7), "A new entity lifetime must not inherit old receipts.");

            // The preserved void API still works for standalone/full-FSM callers.
            enemy.enemyAnimator.HurtBlinkAnimation();
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetFloat(blend), Is.EqualTo(1));
            var fullRoot = Object.Instantiate(enemyPrefab, new Vector3(25, 25, 0), Quaternion.identity);
            var full = fullRoot.GetComponent<NetworkEnemySimulationAgent>();
            full.ConfigureProductSimulation(false);
            full.ConfigureInitialServerTarget(owner.netId);
            NetworkServer.Spawn(fullRoot);
            yield return WaitFor(() => full.ProductEnemyInitialized, "Full FSM initialization");
            var fullEnemy = fullRoot.GetComponent<EnemyController>();
            Assert.That(fullEnemy.StateMachine, Is.Not.Null);
            Assert.That(fullEnemy.enemyAnimator.TryHurtBlinkAnimation(), Is.True);
            var fullRenderer = fullEnemy.GetComponentInChildren<SpriteRenderer>();
            fullRenderer.GetPropertyBlock(block);
            Assert.That(block.GetFloat(blend), Is.EqualTo(1));
            typeof(EnemySimulationAuthority).GetField("networkManaged", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(full.Authority, false);
            fullEnemy.enemyAnimator.ResetHurtBlinkColor();
            fullEnemy.Damage(1, AstralShift.HellMaiden.Player.Attacks.DamageType.Normal);
            fullRenderer.GetPropertyBlock(block);
            Assert.That(block.GetFloat(blend), Is.EqualTo(1), "Standalone damage retains the legacy flash path.");
            NetworkServer.Destroy(fullRoot);
            uint retired = agent.netId;
            NetworkServer.Destroy(root);
            yield return null;
            Confirm(world, Hit(9999, retired, 21));
            Assert.That(played.Count, Is.EqualTo(7));
            Debug.Log("[EnemyHitFlash] product shader/prediction/echo/remote/FSM/pending/expiry/snapshot/lifecycle PASS");
        }

        private static void SetInitialized(NetworkEnemySimulationAgent agent, bool value) =>
            typeof(NetworkEnemySimulationAgent).GetField("productEnemyInitialized", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(agent, value);
        private static EnemyHitPresentation Hit(ulong id, uint target, uint version) =>
            new EnemyHitPresentation { DamageEventId = id, TargetEntityId = target, TargetStateVersion = version };
        private static void Confirm(NetworkCombatWorld world, params EnemyHitPresentation[] hits) =>
            Apply(world, new CanonicalWorldBatch { EnemyHitPresentations = hits });
        private static void Apply(NetworkCombatWorld world, CanonicalWorldBatch batch) =>
            typeof(NetworkCombatWorld).GetMethod("ApplyCanonical", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world, new object[] { batch });
        private static CombatEvent Damage(ulong id, uint source, uint target, int predicted) => new CombatEvent(
            CombatEventKind.DamageResolved,
            new CombatContext(new CombatEventId(id), new CombatEventId(id), CombatEventId.None,
                (uint)id, 0, source, source, target, 6, 0, CombatTags.Damage, 1),
            new DamageInfo(source, 10, false), new DamageInfo(source, predicted, false));
        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                { enemyPrefab = spawner.EnemyPrefab; spawner.Configure(spawner.EnemyPrefab, 5); spawner.enabled = false; }
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
            if (NetworkCombatWorld.Instance != null) NetworkCombatWorld.Instance.EnemyHitPresented -= played.Add;
            if (manager != null)
            {
                manager.StopHost();
                yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Gameplay unload");
            }
            Object.Destroy(gate);
            BootSceneFixtureObjects.Destroy(bootRoots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
