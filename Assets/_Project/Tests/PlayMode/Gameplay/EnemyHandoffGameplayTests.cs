using System;
using System.Collections;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class EnemyHandoffGameplayTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] roots;
        private GameObject gate;
        private NetworkEnemySimulationWorld World => NetworkEnemySimulationWorld.Instance;
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
            gate = new GameObject("Handoff test weapon gate"); gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7992, false, out var error), Is.True, error);
            manager.StartHost();
            yield return WaitFor(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline && World.HasEligiblePlayer, "host ready");
            foreach (var spawner in Object.FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None)) spawner.enabled = false;
        }

        [UnityTest]
        public IEnumerator HostFirstFrameIsAcknowledgedAndDownedTargetFreezesThenRestores()
        {
            var prefab = Object.FindFirstObjectByType<NetworkGameplayEnemySpawner>().EnemyPrefab;
            var agent = Spawn(prefab);
            yield return WaitFor(() => World.TryReadHandoff(agent.netId, out var state) && state.Completed > 0, "first frame");
            Assert.That(agent.Assignment.Host, Is.EqualTo(EnemySimulationHost.ClientPlayer));
            Assert.That(agent.Authority.Role, Is.EqualTo(EnemySimulationRole.ClientOwner));
            uint epoch = agent.Assignment.Epoch;
            Assert.That(World.RequestTargetChange(agent.netId, Owner.netId, EnemyTargetChangeReason.Forced), Is.EqualTo(EnemyTargetChangeResult.Unchanged));
            Assert.That(agent.Assignment.Epoch, Is.EqualTo(epoch));
            var combat = NetworkCombatWorld.Instance;
            combat.Gateway.Ledger.TryGetState(Owner.netId, out var alive);
            Owner.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
            yield return WaitFor(() => agent.Assignment.Host == EnemySimulationHost.Frozen, "downed target freezes");
            Assert.That(World.RequestTargetChange(agent.netId, Owner.netId, EnemyTargetChangeReason.Forced), Is.EqualTo(EnemyTargetChangeResult.InvalidTarget));
            combat.RestorePlayerState(Owner.netId, new PlayerRuntimeCheckpoint { PreviousAvatarId = Owner.netId,
                Health = new ServerEntityCheckpoint(alive, false) });
            yield return WaitFor(() => agent.Assignment.Host == EnemySimulationHost.ClientPlayer &&
                World.TryReadHandoff(agent.netId, out var state) && !state.AwaitingFirstSnapshot, "alive test checkpoint resumes");
            Assert.That(agent.Assignment.Epoch, Is.GreaterThan(epoch));
        }

        [UnityTest]
        public IEnumerator CanonicalEnemyDeathRejectsItsFirstSnapshotBeforeTheBodyReceivesTheDeathBatch()
        {
            var prefab = Object.FindFirstObjectByType<NetworkGameplayEnemySpawner>().EnemyPrefab;
            var agent = Spawn(prefab);
            uint id = agent.netId;
            Assert.That(World.TryReadHandoff(id, out var progress) && progress.AwaitingFirstSnapshot, Is.True);
            World.Registry.TryGetLatestSnapshot(id, out var before);
            var ledger = NetworkCombatWorld.Instance.Gateway.Ledger;
            Assert.That(ledger.TryGetState(id, out var life), Is.True);
            var death = ledger.ApplyServerStatusDamage(id, life.Health, 900, Owner.netId);
            Assert.That(death.Accepted && !death.State.Alive, Is.True);
            Assert.That(agent.IsCanonicalAlive, Is.True, "The runtime body has not received the canonical death batch yet.");
            var poison = before;
            poison.Sequence = 1; poison.AssignmentEpoch = agent.Assignment.Epoch;
            poison.SampleNetworkTime = NetworkTime.time; poison.Position += Vector2.right * 100;
            var endpoint = Owner.GetComponent<NetworkEnemySimulationEndpoint>();
            World.SubmitClientSnapshots(endpoint, new EnemySimulationSnapshotBatch { BatchSequence = 1, Snapshots = new[] { poison } });
            World.SubmitClientAttackPresentations(endpoint, new EnemyAttackPresentationBatch { BatchSequence = 1,
                Edges = new[] { new EnemyAttackPresentationEdge { EnemyEntityId = id, AssignmentEpoch = agent.Assignment.Epoch,
                    StateSequence = 1, Phase = EnemyAttackPresentationPhase.Active, PhaseDuration = 1, StateStartNetworkTime = NetworkTime.time,
                    Facing = Vector2.right } } });
            World.Registry.TryGetLatestSnapshot(id, out var after);
            Assert.That(after.Position, Is.EqualTo(before.Position));
            Assert.That(after.Sequence, Is.EqualTo(before.Sequence));
            Assert.That(World.Registry.TryGetLatestAttackPresentation(id, out _), Is.False);
            Assert.That(World.RequestTargetChange(id, Owner.netId, EnemyTargetChangeReason.Forced), Is.EqualTo(EnemyTargetChangeResult.EnemyDead));
            yield return WaitFor(() => !World.TryReadHandoff(id, out _), "dead in-flight handoff removed");
        }

        [UnityTest]
        public IEnumerator MeleeCheckpointRestoresEveryPhaseWithoutRestartingActionAndSeeksExpiredAttack()
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Content/NetworkCombat/NetworkEnemySkeleton.prefab");
            var agent = Spawn(prefab);
            yield return WaitFor(() => agent.ProductEnemyInitialized && agent.Authority.RunsCombatDecisions, "melee ready");
            var enemy = agent.GetComponent<EnemyController>();
            var action = new EnemyActionState { ActionId = 77, Phase = EnemyAttackPresentationPhase.Warning,
                WarningStartedAt = NetworkTime.time, WarningUntil = NetworkTime.time + 1,
                ActiveUntil = NetworkTime.time + 2, RecoveryUntil = NetworkTime.time + 3, NextAttackAt = NetworkTime.time + 4,
                Facing = Vector2.left, TargetPosition = new Vector2(-10, 4) };
            for (int phase = 0; phase < 4; phase++)
            {
                enemy.SuspendSimulationExecution();
                double sample = action.WarningStartedAt + phase + .1;
                enemy.RestoreSimulationAction(action, sample);
                var captured = enemy.CaptureSimulationAction(sample);
                Assert.That(captured.ActionId, Is.EqualTo(77));
                Assert.That(captured.Facing, Is.EqualTo(Vector2.left));
                Assert.That(captured.NextAttackAt, Is.EqualTo(action.NextAttackAt).Within(.001));
                Assert.That(enemy.CurrentAttackPresentationPhase, Is.EqualTo(action.PhaseAt(sample)));
            }
            Assert.That(enemy.StateMachine.GetState(), Is.SameAs(enemy.Moving));
#endif
            yield break;
        }

        private NetworkEnemySimulationAgent Spawn(GameObject prefab)
        {
            var instance = Object.Instantiate(prefab, Owner.transform.position + Vector3.right * 8, Quaternion.identity);
            var agent = instance.GetComponent<NetworkEnemySimulationAgent>();
            agent.ConfigureInitialServerTarget(Owner.netId);
            NetworkServer.Spawn(instance);
            return agent;
        }

        private static IEnumerator WaitFor(Func<bool> predicate, string description)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, description);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (manager != null && NetworkServer.active) manager.StopHost();
            if (manager != null) yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "unload");
            BootSceneFixtureObjects.Destroy(roots);
            if (gate != null) Object.Destroy(gate);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
