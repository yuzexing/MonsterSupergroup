using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.AI;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class GameplayWavePlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private GameObject gate;
        private GameplayWaveRules rulesCopy;
        private readonly List<Object> timelineAssets = new List<Object>();
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
        public IEnumerator CanonicalDeath_StopRestartWithSelection_DoesNotPoisonReusedEnemyId()
        {
            yield return StartHost();
            var persistentWorld = NetworkCombatWorld.Instance;
            manager.BeginRun();
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            uint previousEnemyId = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>().netId;
            SetCanonicalHealth(previousEnemyId, 0);
            yield return WaitFor(() => !NetworkServer.spawned.ContainsKey(previousEnemyId));
            yield return new WaitForSecondsRealtime(.2f); // Host consumes the queued death after local despawn.
            manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
            Assert.That(NetworkCombatWorld.Instance, Is.SameAs(persistentWorld), "Boot's scene World must survive Stop.");
            manager.StartHost();
            yield return WaitFor(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline && manager.CanBeginRun(out _));
            var selection = Owner.GetComponent<NetworkModifierSelection>();
            Assert.That(selection.RequestDebugLevelUp(), Is.True);
            yield return WaitFor(() => selection.IsSelecting);
            manager.BeginRun();
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            var enemy = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>();
            Assert.That(enemy.netId, Is.EqualTo(previousEnemyId));
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(persistentWorld.Gateway.Ledger.TryGetState(enemy.netId, out var server), Is.True);
            Assert.That(persistentWorld.Replica.TryGetEntity(enemy.netId, out var replica), Is.True);
            Assert.That(server.Alive, Is.True);
            Assert.That(replica.Health, Is.EqualTo(server.Health), "Previous run's canonical death blocked this run's baseline.");
            Assert.That(replica.StateVersion, Is.EqualTo(server.StateVersion));
            Assert.That(enemy.GetComponent<CombatantBehaviour>().CurrentHealth, Is.EqualTo(server.Health));
            Assert.That(selection.IsSelecting, Is.True);
        }

        [UnityTest, Category("CacheLifecycleReproduction")]
        public IEnumerator RemovedStatusVersions_StopRestart_DoesNotRetainPreviousRunRecords()
        {
            yield return StartHost();
            var world = NetworkCombatWorld.Instance;
            string oldRun = manager.Session.RunId;
            manager.BeginRun();
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            var enemy = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>();
            uint enemyId = enemy.netId;
            var ids = Owner.GetComponent<MirrorNetworkCombatBridge>().EventIds;
            // Seed valid server status records to isolate their lifetime from weapon admission.
            // Removal still runs through the real canonical-death/despawn callbacks.
            for (int i = 0; i < 64; i++)
            {
                CombatEventId id = ids.Next();
                var result = world.Gateway.Statuses.Apply(Owner.netId, new StatusMutation
                {
                    ApplicationRevision = 1,
                    EventId = id.Value, RootEventId = id.Value, Sequence = id.Sequence,
                    InstanceId = id.Value, Kind = StatusMutationKind.ApplyOrRefresh,
                    SourcePlayerId = Owner.netId, SourceEntityId = Owner.netId, TargetEntityId = enemyId,
                    DefinitionId = (uint)EnemyStatusID.Poison, StackMode = (byte)StatusStackMode.Add,
                    MaxStacks = 1, StackDelta = 1, Duration = 60, TickInterval = 60, TotalTicks = 1,
                    ExecutionAuthority = (byte)StatusExecutionAuthority.SourceClient
                }, NetworkTime.time);
                Assert.That(result.Accepted, Is.True, result.Rejection.ToString());
            }
            Assert.That(world.Gateway.Statuses.Count, Is.EqualTo(64));
            SetCanonicalHealth(enemyId, 0);
            yield return WaitFor(() => !NetworkServer.spawned.ContainsKey(enemyId));
            Assert.That(world.Gateway.Statuses.Count, Is.Zero);
            int removed = CountStatusRemovalVersions(world.Gateway);
            Assert.That(removed, Is.Zero, "Retiring an Enemy must release its status history immediately.");
            var previousGateway = world.Gateway;
            manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
            Assert.That(NetworkCombatWorld.Instance, Is.SameAs(world));
            int afterStop = CountStatusRemovalVersions(world.Gateway);
            Assert.That(afterStop, Is.Zero);
            manager.StartHost();
            yield return WaitFor(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline && manager.CanBeginRun(out _));
            Assert.That(manager.Session.RunId, Is.Not.EqualTo(oldRun));
            Assert.That(world.Gateway, Is.Not.SameAs(previousGateway));
            int afterRestart = CountStatusRemovalVersions(world.Gateway);
            Debug.Log($"[CacheLifecycle] status removed={removed} afterStop={afterStop} afterRestart={afterRestart} active={world.Gateway.Statuses.Count}");
            Assert.That(afterRestart, Is.Zero, "A new Boot server run retained the previous run's status removal versions.");
        }

        [UnityTest, Category("CacheLifecycleReproduction")]
        public IEnumerator HostDeaths_DespawnedRecordsAreReleasedAfterNotifications()
        {
            yield return StartHost();
            UseFastRules(1.5f, .2f, 30);
            var world = NetworkCombatWorld.Instance;
            var despawned = new List<uint>();
            manager.BeginRun();
            int firstSample = 0;
            for (int i = 0; i < 12; i++)
            {
                yield return WaitFor(() => Object.FindFirstObjectByType<NetworkEnemySimulationAgent>() != null);
                uint id = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>().netId;
                despawned.Add(id);
                SetCanonicalHealth(id, 0);
                yield return WaitFor(() => !NetworkServer.spawned.ContainsKey(id) && !NetworkClient.spawned.ContainsKey(id));
                yield return new WaitForSecondsRealtime(.1f); // Consume the actual queued Host RPC.
                if (i == 5) firstSample = CountRetainedDeaths(world.Replica, despawned);
            }
            Spawner.enabled = false;
            int secondSample = CountRetainedDeaths(world.Replica, despawned);
            yield return new WaitForSecondsRealtime(3f); // Longer than the Debug panel's death-row lifetime.
            int afterIdle = CountRetainedDeaths(world.Replica, despawned);
            Assert.That(despawned.All(id => !NetworkServer.spawned.ContainsKey(id) && !NetworkClient.spawned.ContainsKey(id)), Is.True);
            Debug.Log($"[CacheLifecycle] host deaths=12 retainedAfter6={firstSample} retainedAfter12={secondSample} retainedAfterIdle={afterIdle}");
            Assert.That(firstSample, Is.Zero);
            Assert.That(secondSample, Is.Zero);
            Assert.That(afterIdle, Is.Zero);
            manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
            Assert.That(CountRetainedDeaths(world.Replica, despawned), Is.Zero, "The existing session cleanup must still work.");
        }

        [UnityTest, Category("CacheLifecycleReproduction")]
        public IEnumerator PendingAttackEdge_StopRestart_ClearsWaitingRecordsAndAcceptsFreshSequence()
        {
            yield return StartHost();
            var world = NetworkEnemySimulationWorld.Instance;
            manager.BeginRun();
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            var enemy = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>();
            uint previousId = enemy.netId;
            uint previousEpoch = enemy.Assignment.Epoch;
            var oldEdge = new EnemyAttackPresentationEdge
            {
                EnemyEntityId = previousId, AssignmentEpoch = previousEpoch, StateSequence = 10,
                StateStartNetworkTime = NetworkTime.time, PhaseDuration = 1,
                Phase = EnemyAttackPresentationPhase.Warning, Facing = Vector2.left
            };
            // Contract-level fault injection: EnemyBase does not produce phase edges in
            // movement-only mode. Submit a valid edge through server admission and the real
            // reliable RPC, then despawn before Host consumes it. Do not write the cache.
            world.SubmitClientAttackPresentations(Owner.GetComponent<NetworkEnemySimulationEndpoint>(),
                new EnemyAttackPresentationBatch { BatchSequence = 1, Edges = new[] { oldEdge } });
            Assert.That(world.Registry.TryGetLatestAttackPresentation(previousId, out var admitted), Is.True);
            Assert.That(admitted.StateSequence, Is.EqualTo(10));
            SetCanonicalHealth(previousId, 0);
            yield return WaitFor(() => !NetworkServer.spawned.ContainsKey(previousId));
            yield return new WaitForSecondsRealtime(.2f);
            int beforeStop = world.PendingClientAttackPresentationCount;
            manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
            Assert.That(NetworkEnemySimulationWorld.Instance, Is.SameAs(world));
            int afterStop = world.PendingClientAttackPresentationCount;
            manager.StartHost();
            yield return WaitFor(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline && manager.CanBeginRun(out _));
            manager.BeginRun();
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            enemy = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>();
            Assert.That(enemy.netId, Is.EqualTo(previousId));
            Assert.That(enemy.Assignment.Epoch, Is.EqualTo(previousEpoch));
            yield return new WaitForSecondsRealtime(.2f);
            uint sequenceAtSpawn = enemy.HasLatestAttackPresentation ? enemy.LatestAttackPresentation.StateSequence : 0;
            var fresh = oldEdge;
            fresh.StateSequence = 1;
            fresh.StateStartNetworkTime = NetworkTime.time;
            fresh.Phase = EnemyAttackPresentationPhase.Inactive;
            world.SubmitClientAttackPresentations(Owner.GetComponent<NetworkEnemySimulationEndpoint>(),
                new EnemyAttackPresentationBatch { BatchSequence = 1, Edges = new[] { fresh } });
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(world.Registry.TryGetLatestAttackPresentation(enemy.netId, out var current), Is.True);
            Assert.That(current.StateSequence, Is.EqualTo(1), "The new server run must accept its first phase edge.");
            Debug.Log($"[CacheLifecycle] attack enemy={enemy.netId} epoch={enemy.Assignment.Epoch} pendingBeforeStop={beforeStop} pendingAfterStop={afterStop} sequenceAtSpawn={sequenceAtSpawn} serverSequence={current.StateSequence} clientSequence={enemy.LatestAttackPresentation.StateSequence}");
            Assert.That(enemy.LatestAttackPresentation.StateSequence, Is.EqualTo(current.StateSequence),
                "An old run's pending attack edge suppressed the new enemy's first accepted edge.");
            Assert.That(afterStop, Is.Zero, "Stop retained an old run's pending attack presentation.");
        }

        private static int CountStatusRemovalVersions(ServerCombatGateway gateway) =>
            gateway.Statuses.RemovalHistoryCount;

        private static int CountRetainedDeaths(CanonicalWorldReplica replica, IEnumerable<uint> ids) =>
            ids.Count(id => replica.TryGetEntity(id, out var state) && !state.Alive);

        [UnityTest, Category("CacheLifecycleReproduction")]
        public IEnumerator ClientWaitingCallbacks_ClearBothQueuesWithoutClearingHostRegistrations()
        {
            yield return StartHost();
            manager.BeginRun();
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            var world = NetworkEnemySimulationWorld.Instance;
            var enemy = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>();
            var command = new EnemyKnockbackCommand
            {
                EnemyEntityId = uint.MaxValue, AssignmentEpoch = 1, SourcePlayerId = Owner.netId,
                AbilityCombatId = 0x80000001, RootEventId = Owner.GetComponent<MirrorNetworkCombatBridge>().EventIds.Next().Value,
                CommandId = 1, IssuedAt = NetworkTime.time,
                Settings = new EnemyKnockbackSettings { Distance = .2f, SpeedMultiplier = 10,
                    CurveKeys = new[] { new EnemyKnockbackCurveKey(), new EnemyKnockbackCurveKey { Time = 1, Value = 1 } } }
            };
            Assert.That(command.IsValid, Is.True);
            for (int i = 0; i < 2; i++)
            {
                ApplyClientAttackEdge(world, new EnemyAttackPresentationEdge { EnemyEntityId = uint.MaxValue,
                    AssignmentEpoch = 1, StateSequence = 1, Phase = EnemyAttackPresentationPhase.Warning,
                    PhaseDuration = 1, StateStartNetworkTime = NetworkTime.time, Facing = Vector2.right });
                typeof(NetworkEnemySimulationWorld).GetMethod("ReceiveKnockback", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(world, new object[] { command, Owner.netId });
                Assert.That(world.PendingClientAttackPresentationCount, Is.EqualTo(1));
                Assert.That(world.PendingClientKnockbackCount, Is.EqualTo(1));
                if (i == 0) world.OnStopClient(); else world.OnStartClient();
                Assert.That(world.PendingClientAttackPresentationCount, Is.Zero);
                Assert.That(world.PendingClientKnockbackCount, Is.Zero);
                Assert.That(world.Registry.Count, Is.EqualTo(1));
                Assert.That(world.HasEligiblePlayer, Is.True);
                var snapshots = new List<EnemySimulationSnapshot>();
                world.CollectClientOwnedSnapshots(Owner.netId, NetworkTime.time, snapshots);
                Assert.That(snapshots.Count, Is.EqualTo(1), "The Host's shared Enemy map must survive client queue cleanup.");
                Assert.That(snapshots[0].EnemyEntityId, Is.EqualTo(enemy.netId));
            }
        }

        [UnityTest, Category("CacheLifecycleReproduction")]
        public IEnumerator WaitingAttackEdges_DiscardSkippedEpochAndPreservePreRegistrationDelivery()
        {
            yield return StartHost();
            manager.BeginRun();
            yield return WaitFor(() => Spawner.ServerProgress.TotalSpawned == 1);
            var world = NetworkEnemySimulationWorld.Instance;
            var enemy = Object.FindFirstObjectByType<NetworkEnemySimulationAgent>();
            var edge = new EnemyAttackPresentationEdge { EnemyEntityId = enemy.netId,
                AssignmentEpoch = enemy.Assignment.Epoch + 1, StateSequence = 10,
                Phase = EnemyAttackPresentationPhase.Warning, PhaseDuration = 1,
                StateStartNetworkTime = NetworkTime.time, Facing = Vector2.left };
            ApplyClientAttackEdge(world, edge);
            Assert.That(world.PendingClientAttackPresentationCount, Is.EqualTo(1));
            world.Registry.Freeze(enemy.netId);
            enemy.SetServerAssignment(world.Registry.AssignClientOwner(enemy.netId, Owner.netId, Owner.netId));
            Assert.That(enemy.Assignment.Epoch, Is.GreaterThan(edge.AssignmentEpoch));
            Assert.That(world.PendingClientAttackPresentationCount, Is.Zero);
            edge.AssignmentEpoch = enemy.Assignment.Epoch;
            edge.StateSequence = 1;
            world.UnregisterClientEnemy(enemy); // Existing late-join registration-order fixture pattern.
            ApplyClientAttackEdge(world, edge);
            Assert.That(world.PendingClientAttackPresentationCount, Is.EqualTo(1));
            world.RegisterClientEnemy(enemy);
            Assert.That(world.PendingClientAttackPresentationCount, Is.Zero);
            Assert.That(enemy.LatestAttackPresentation.StateSequence, Is.EqualTo(1));
        }

        private static void ApplyClientAttackEdge(NetworkEnemySimulationWorld world, EnemyAttackPresentationEdge edge) =>
            typeof(NetworkEnemySimulationWorld).GetMethod("ApplyAttackPresentations", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(world, new object[] { new EnemyAttackPresentationBatch { Edges = new[] { edge } } });

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
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>(); timelineAssets.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration = duration;
            AddFixtureSpawn(timeline, Spawner.EnemyPrefab, 0, interval * 6, 6);
            Set(rulesCopy, "timeline", timeline); Set(rulesCopy, "waveDuration", duration); Set(rulesCopy, "maximumAlive", limit);
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
            foreach (var asset in timelineAssets) if (asset != null) Object.Destroy(asset);
            timelineAssets.Clear();
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
