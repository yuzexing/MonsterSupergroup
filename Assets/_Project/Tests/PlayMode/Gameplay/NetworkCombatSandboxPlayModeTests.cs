using System.Collections;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using AstralShift.HellMaiden.Player;
using AstralShift.QTI.Triggers;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Local;
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
    public sealed class NetworkCombatSandboxPlayModeTests
    {
        private const string SandboxScenePath =
            "Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity";

        [UnityTest]
        public IEnumerator Host_StartsOwnerPlayerAndOneHundredTwentyCanonicalEnemies()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                SandboxScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(SandboxScenePath, LoadSceneMode.Single);
#endif
            NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();
            Assert.That(manager, Is.Not.Null);

            try
            {
                manager.StartHost();
                float deadline = Time.realtimeSinceStartup + 8f;
                while ((NetworkClient.localPlayer == null ||
                        Object.FindObjectsByType<NetworkEnemyServerDriver>(
                            FindObjectsSortMode.None).Length < 120 ||
                        Object.FindObjectsByType<NetworkEnemySimulationAgent>(
                            FindObjectsSortMode.None).Count(agent =>
                                agent.Assignment.Host ==
                                    EnemySimulationHost.ClientPlayer) < 120) &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                NetworkEnemyServerDriver[] enemies =
                    Object.FindObjectsByType<NetworkEnemyServerDriver>(
                        FindObjectsSortMode.None);
                NetworkEnemySimulationAgent[] simulationAgents =
                    Object.FindObjectsByType<NetworkEnemySimulationAgent>(
                        FindObjectsSortMode.None);
                PlayerLoader ownerLoader = NetworkClient.localPlayer != null
                    ? NetworkClient.localPlayer.GetComponent<PlayerLoader>()
                    : null;
                PlayerCombatantBinding ownerCombatant = NetworkClient.localPlayer != null
                    ? NetworkClient.localPlayer.GetComponent<PlayerCombatantBinding>()
                    : null;
                PlayerMovement ownerPlayerMovement = NetworkClient.localPlayer != null
                    ? NetworkClient.localPlayer.GetComponent<PlayerMovement>()
                    : null;
                NetworkCombatWorld world = NetworkCombatWorld.Instance;
                NetworkEnemySimulationWorld simulationWorld =
                    NetworkEnemySimulationWorld.Instance;

                Assert.That(NetworkServer.active, Is.True);
                Assert.That(NetworkClient.isConnected, Is.True);
                Assert.That(NetworkClient.localPlayer, Is.Not.Null);
                Assert.That(ownerLoader, Is.Not.Null);
                Assert.That(ownerLoader.IsLoaded, Is.True);
                Assert.That(ownerCombatant, Is.Not.Null);
                Assert.That(ownerPlayerMovement, Is.Not.Null);
                Assert.That(ownerPlayerMovement.CombatantBinding, Is.SameAs(ownerCombatant));
                Assert.That(ownerCombatant.AcceptsLocalMutations, Is.True);
                Assert.That(enemies, Has.Length.EqualTo(120));
                Assert.That(simulationAgents, Has.Length.EqualTo(120));
                Assert.That(world, Is.Not.Null);
                Assert.That(simulationWorld, Is.Not.Null);
                Assert.That(simulationWorld.Registry.Count, Is.EqualTo(120));
                Assert.That(world.Gateway.Ledger.EntityCount, Is.EqualTo(121));
                Assert.That(
                    enemies.All(enemy =>
                        enemy.GetComponent<NetworkCombatantAdapter>() != null),
                    Is.True);
                uint ownerPlayerId = NetworkClient.localPlayer.netId;
                Assert.That(
                    simulationAgents.All(agent =>
                        agent.Assignment.Host == EnemySimulationHost.ClientPlayer &&
                        agent.Assignment.SimulationOwnerPlayerId == ownerPlayerId &&
                        agent.Assignment.AggroTargetPlayerId == ownerPlayerId &&
                        agent.Authority.Role == EnemySimulationRole.ClientOwner),
                    Is.True);
                Assert.That(
                    simulationAgents.All(agent =>
                        agent.GetComponents<NetworkBehaviour>().All(component =>
                            component.GetType().Name != "NetworkTransformReliable")),
                    Is.True);

                float snapshotDeadline = Time.realtimeSinceStartup + 3f;
                bool allSnapshotsAccepted = false;
                while (!allSnapshotsAccepted &&
                       Time.realtimeSinceStartup < snapshotDeadline)
                {
                    allSnapshotsAccepted = simulationAgents.All(agent =>
                        simulationWorld.Registry.TryGetLatestSnapshot(
                            agent.netId,
                            out EnemySimulationSnapshot snapshot) &&
                        snapshot.Sequence > 0u &&
                        snapshot.AssignmentEpoch == agent.Assignment.Epoch);
                    yield return null;
                }
                Assert.That(allSnapshotsAccepted, Is.True,
                    "The owner Client must batch Enemy snapshots back to the Server ledger.");

                int initialHealth = ownerCombatant.CurrentHealth;
                ownerPlayerMovement.DecreaseHealth(10);
                int expectedDamagedHealth = initialHealth - 10;
                Assert.That(ownerCombatant.CurrentHealth, Is.EqualTo(expectedDamagedHealth));
                uint playerEntityId = NetworkClient.localPlayer.netId;
                float damageReportDeadline = Time.realtimeSinceStartup + 3f;
                CanonicalEntityState playerState = default;
                while ((!world.Gateway.Ledger.TryGetState(playerEntityId, out playerState) ||
                        playerState.Health != expectedDamagedHealth) &&
                       Time.realtimeSinceStartup < damageReportDeadline)
                {
                    yield return null;
                }

                Assert.That(playerState.Health, Is.EqualTo(expectedDamagedHealth));
                Assert.That(ownerCombatant.CurrentHealth, Is.EqualTo(expectedDamagedHealth));

                ownerPlayerMovement.IncreaseHealth(5);
                int expectedHealedHealth = expectedDamagedHealth + 5;
                Assert.That(ownerCombatant.CurrentHealth, Is.EqualTo(expectedHealedHealth));
                float healReportDeadline = Time.realtimeSinceStartup + 3f;
                while ((!world.Gateway.Ledger.TryGetState(playerEntityId, out playerState) ||
                        playerState.Health != expectedHealedHealth) &&
                       Time.realtimeSinceStartup < healReportDeadline)
                {
                    yield return null;
                }

                Assert.That(playerState.Health, Is.EqualTo(expectedHealedHealth));
                Assert.That(ownerCombatant.CurrentHealth, Is.EqualTo(expectedHealedHealth));
            }
            finally
            {
                if (manager != null && (NetworkServer.active || NetworkClient.active))
                {
                    manager.StopHost();
                }
            }

            float shutdownDeadline = Time.realtimeSinceStartup + 3f;
            while ((NetworkServer.active || NetworkClient.active) &&
                   Time.realtimeSinceStartup < shutdownDeadline)
            {
                yield return null;
            }

            Assert.That(NetworkServer.active, Is.False);
            Assert.That(NetworkClient.active, Is.False);
        }

        [UnityTest]
        public IEnumerator ProductEnemyBase_ClientOwnerRunsNavigationWithoutServerTransform()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                SandboxScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(SandboxScenePath, LoadSceneMode.Single);
#endif
            NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();
            Assert.That(manager, Is.Not.Null);
            GameObject spawnedEnemy = null;
            GameObject remoteEndpointObject = null;

            try
            {
                manager.StartHost();
                float playerDeadline = Time.realtimeSinceStartup + 5f;
                while (NetworkClient.localPlayer == null &&
                       Time.realtimeSinceStartup < playerDeadline)
                {
                    yield return null;
                }

                Assert.That(NetworkClient.localPlayer, Is.Not.Null);
                PlayerLoader playerLoader =
                    NetworkClient.localPlayer.GetComponent<PlayerLoader>();
                float loadDeadline = Time.realtimeSinceStartup + 3f;
                while ((playerLoader == null || !playerLoader.IsLoaded) &&
                       Time.realtimeSinceStartup < loadDeadline)
                {
                    yield return null;
                }
                Assert.That(playerLoader, Is.Not.Null);
                Assert.That(playerLoader.IsLoaded, Is.True);
                NetworkClient.localPlayer.GetComponent<PlayerHandBehaviour>()
                    ?.DeactivateWeapons();
                GameObject productPrefab = manager.spawnPrefabs.SingleOrDefault(
                    prefab => prefab != null && prefab.name == "NetworkEnemyBase");
                Assert.That(productPrefab, Is.Not.Null);

                Vector3 spawnPosition =
                    NetworkClient.localPlayer.transform.position + Vector3.right * 5f;
                spawnedEnemy = Object.Instantiate(
                    productPrefab,
                    spawnPosition,
                    Quaternion.identity);
                NetworkServer.Spawn(spawnedEnemy);

                EnemyController controller = spawnedEnemy.GetComponent<EnemyController>();
                NetworkEnemySimulationAgent agent =
                    spawnedEnemy.GetComponent<NetworkEnemySimulationAgent>();
                float initDeadline = Time.realtimeSinceStartup + 5f;
                while ((!agent.ProductEnemyInitialized ||
                        agent.Authority.Role != EnemySimulationRole.ClientOwner) &&
                       Time.realtimeSinceStartup < initDeadline)
                {
                    yield return null;
                }

                Assert.That(agent.ProductEnemyInitialized, Is.True);
                Assert.That(controller.StateMachine, Is.Null,
                    "Movement phase must not silently run the unsynchronized attack FSM.");
                Assert.That(controller.Target, Is.Not.Null);
                Assert.That(agent.Authority.Role, Is.EqualTo(EnemySimulationRole.ClientOwner));
                Assert.That(agent.Authority.RunsNavigation, Is.True);
                Assert.That(agent.Authority.RunsCombatDecisions, Is.False);
                Assert.That(controller.attackScript.enabled, Is.False,
                    "Movement-only network Enemies must not run standalone " +
                    "EnemyAttack lifecycle methods outside EnemyAIManager.");
                Assert.That(
                    spawnedEnemy.GetComponents<NetworkBehaviour>().All(component =>
                        component.GetType().Name != "NetworkTransformReliable"),
                    Is.True);

                PlayerDamageInteraction contactDamage =
                    spawnedEnemy.GetComponentInChildren<PlayerDamageInteraction>(true);
                InteractionTrigger contactTrigger =
                    contactDamage != null
                        ? contactDamage.GetComponent<InteractionTrigger>()
                        : null;
                PlayerHitbox ownerHitbox =
                    NetworkClient.localPlayer.GetComponentInChildren<PlayerHitbox>(true);
                PlayerCombatantBinding ownerBinding =
                    NetworkClient.localPlayer.GetComponent<PlayerCombatantBinding>();
                Assert.That(contactDamage, Is.Not.Null);
                Assert.That(contactDamage.gameObject.activeInHierarchy, Is.True,
                    "Continuous contact damage must run on every observing Client.");
                Assert.That(contactDamage.enemyStats, Is.SameAs(controller.stats));
                Assert.That(contactTrigger, Is.Not.Null);
                Assert.That(contactTrigger.interaction, Is.SameAs(contactDamage));
                Assert.That(ownerHitbox, Is.Not.Null);
                Assert.That(ownerHitbox.IsLocallyControlled, Is.True);

                controller.stats.Damage = 5;
                int healthBeforeContact = ownerBinding.CurrentHealth;
                int minimumObservedHealth = healthBeforeContact;
                void CaptureHealth(int current, int maximum)
                {
                    minimumObservedHealth = Mathf.Min(minimumObservedHealth, current);
                }
                ownerBinding.Combatant.HealthChanged += CaptureHealth;
                Rigidbody2D enemyBody = spawnedEnemy.GetComponent<Rigidbody2D>();
                enemyBody.position = ownerHitbox.transform.position;
                spawnedEnemy.transform.position = ownerHitbox.transform.position;
                Physics2D.SyncTransforms();
                float damageDeadline = Time.realtimeSinceStartup + 2f;
                while (ownerBinding.CurrentHealth == healthBeforeContact &&
                       Time.realtimeSinceStartup < damageDeadline)
                {
                    // QTI batches Stay collisions until WaitForEndOfFrame before
                    // resolving the local Player hit. Advancing only fixed steps can
                    // leave that batch pending until the interaction is disabled.
                    yield return null;
                }
                ownerBinding.Combatant.HealthChanged -= CaptureHealth;
                enemyBody.position = ownerHitbox.transform.position + Vector3.right * 10f;
                spawnedEnemy.transform.position = enemyBody.position;
                Physics2D.SyncTransforms();
                int expectedContactHealth = healthBeforeContact - 5;
                Assert.That(
                    minimumObservedHealth,
                    Is.EqualTo(expectedContactHealth),
                    "Enemy contact Collider/QTI is judged locally by the owning Player Client.");

                NetworkCombatWorld combatWorld = NetworkCombatWorld.Instance;
                float reportDeadline = Time.realtimeSinceStartup + 2f;
                CanonicalEntityState ownerState = default;
                while ((!combatWorld.Gateway.Ledger.TryGetState(
                            NetworkClient.localPlayer.netId,
                            out ownerState) ||
                        ownerState.Health != expectedContactHealth) &&
                       Time.realtimeSinceStartup < reportDeadline)
                {
                    yield return null;
                }
                Assert.That(ownerBinding.CurrentHealth, Is.EqualTo(expectedContactHealth));
                Assert.That(ownerState.Health, Is.EqualTo(expectedContactHealth),
                    "Owner-final PlayerHealthReport must converge contact damage to Server state.");

                Vector2 initialPosition = spawnedEnemy.transform.position;
                float movementDeadline = Time.realtimeSinceStartup + 2f;
                while (((Vector2)spawnedEnemy.transform.position - initialPosition)
                           .sqrMagnitude < 0.01f &&
                       Time.realtimeSinceStartup < movementDeadline)
                {
                    yield return null;
                }

                Assert.That(
                    ((Vector2)spawnedEnemy.transform.position - initialPosition)
                        .sqrMagnitude,
                    Is.GreaterThanOrEqualTo(0.01f));

                remoteEndpointObject = new GameObject("Product Enemy Remote Owner");
                remoteEndpointObject.SetActive(false);
                remoteEndpointObject.transform.position = new Vector3(50f, 50f, 0f);
                remoteEndpointObject.AddComponent<NetworkIdentity>();
                remoteEndpointObject.AddComponent<NetworkEnemySimulationEndpoint>();
                remoteEndpointObject.SetActive(true);
                NetworkServer.Spawn(remoteEndpointObject);
                NetworkEnemySimulationEndpoint remoteEndpoint =
                    remoteEndpointObject.GetComponent<NetworkEnemySimulationEndpoint>();
                NetworkEnemySimulationWorld simulationWorld =
                    NetworkEnemySimulationWorld.Instance;
                EnemySimulationAssignment replicaAssignment =
                    simulationWorld.Registry.AssignClientOwner(
                        agent.netId,
                        remoteEndpoint.PlayerEntityId,
                        remoteEndpoint.PlayerEntityId);
                agent.SetServerAssignment(replicaAssignment);
                float replicaDeadline = Time.realtimeSinceStartup + 2f;
                while (agent.Authority.Role != EnemySimulationRole.Replica &&
                       Time.realtimeSinceStartup < replicaDeadline)
                {
                    yield return null;
                }

                Assert.That(agent.Authority.Role, Is.EqualTo(EnemySimulationRole.Replica));
                Assert.That(agent.Authority.RunsNavigation, Is.False);
                Assert.That(
                    spawnedEnemy.GetComponent<Rigidbody2D>().bodyType,
                    Is.EqualTo(RigidbodyType2D.Kinematic));
                Vector2 replicaWaitingPosition = spawnedEnemy.transform.position;
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(
                    ((Vector2)spawnedEnemy.transform.position - replicaWaitingPosition)
                        .sqrMagnitude,
                    Is.LessThan(0.0001f),
                    "The product EnemyController must stop writing Transform as a Replica.");

                Vector2 relayedPosition = replicaWaitingPosition + Vector2.right * 3f;
                simulationWorld.SubmitClientSnapshots(
                    remoteEndpoint,
                    new EnemySimulationSnapshotBatch
                    {
                        BatchSequence = 1u,
                        Snapshots = new[]
                        {
                            new EnemySimulationSnapshot
                            {
                                EnemyEntityId = agent.netId,
                                AssignmentEpoch = replicaAssignment.Epoch,
                                Sequence = 1u,
                                SampleNetworkTime = NetworkTime.time,
                                Position = relayedPosition,
                                Facing = Vector2.right,
                                Flags = EnemySimulationSnapshotFlags.Discontinuity
                            }
                        }
                    });
                float relayDeadline = Time.realtimeSinceStartup + 1f;
                while (((Vector2)spawnedEnemy.transform.position - relayedPosition)
                           .sqrMagnitude >= 0.0001f &&
                       Time.realtimeSinceStartup < relayDeadline)
                {
                    yield return null;
                }
                Assert.That(
                    ((Vector2)spawnedEnemy.transform.position - relayedPosition)
                        .sqrMagnitude,
                    Is.LessThan(0.0001f));
            }
            finally
            {
                if (spawnedEnemy != null && NetworkServer.active)
                {
                    NetworkServer.Destroy(spawnedEnemy);
                }
                if (remoteEndpointObject != null && NetworkServer.active)
                {
                    NetworkServer.Destroy(remoteEndpointObject);
                }
                if (manager != null && (NetworkServer.active || NetworkClient.active))
                {
                    manager.StopHost();
                }
            }

            float shutdownDeadline = Time.realtimeSinceStartup + 3f;
            while ((NetworkServer.active || NetworkClient.active) &&
                   Time.realtimeSinceStartup < shutdownDeadline)
            {
                yield return null;
            }

            Assert.That(NetworkServer.active, Is.False);
            Assert.That(NetworkClient.active, Is.False);
        }

        [UnityTest]
        public IEnumerator SkeletonMelee_ReplicaDamagesLocalPlayerButExpiredActiveDoesNot()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                SandboxScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(SandboxScenePath, LoadSceneMode.Single);
#endif
            NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();
            Assert.That(manager, Is.Not.Null);
            GameObject spawnedEnemy = null;
            GameObject remoteEndpointObject = null;

            try
            {
                manager.StartHost();
                float playerDeadline = Time.realtimeSinceStartup + 6f;
                while (NetworkClient.localPlayer == null &&
                       Time.realtimeSinceStartup < playerDeadline)
                {
                    yield return null;
                }
                Assert.That(NetworkClient.localPlayer, Is.Not.Null);

                PlayerLoader playerLoader =
                    NetworkClient.localPlayer.GetComponent<PlayerLoader>();
                float loadDeadline = Time.realtimeSinceStartup + 3f;
                while ((playerLoader == null || !playerLoader.IsLoaded) &&
                       Time.realtimeSinceStartup < loadDeadline)
                {
                    yield return null;
                }
                Assert.That(playerLoader, Is.Not.Null);
                Assert.That(playerLoader.IsLoaded, Is.True);
                NetworkClient.localPlayer.GetComponent<PlayerHandBehaviour>()
                    ?.DeactivateWeapons();

                GameObject skeletonPrefab = manager.spawnPrefabs.SingleOrDefault(
                    prefab => prefab != null &&
                        prefab.name == "NetworkEnemySkeleton");
                Assert.That(skeletonPrefab, Is.Not.Null);

                remoteEndpointObject = new GameObject("Skeleton Remote Owner");
                remoteEndpointObject.SetActive(false);
                remoteEndpointObject.AddComponent<NetworkIdentity>();
                remoteEndpointObject.AddComponent<NetworkEnemySimulationEndpoint>();
                remoteEndpointObject.SetActive(true);
                NetworkServer.Spawn(remoteEndpointObject);
                NetworkEnemySimulationEndpoint remoteEndpoint =
                    remoteEndpointObject.GetComponent<NetworkEnemySimulationEndpoint>();
                float endpointDeadline = Time.realtimeSinceStartup + 2f;
                while (remoteEndpoint.netId == 0u &&
                       Time.realtimeSinceStartup < endpointDeadline)
                {
                    yield return null;
                }
                Assert.That(remoteEndpoint.netId, Is.Not.Zero);

                PlayerHitbox localHitbox = NetworkClient.localPlayer
                    .GetComponentInChildren<PlayerHitbox>(true);
                PlayerCombatantBinding localCombatant = NetworkClient.localPlayer
                    .GetComponent<PlayerCombatantBinding>();
                PlayerMovement localMovement = NetworkClient.localPlayer
                    .GetComponent<PlayerMovement>();
                Assert.That(localHitbox, Is.Not.Null);
                Assert.That(localHitbox.IsLocallyControlled, Is.True);
                Assert.That(localCombatant, Is.Not.Null);
                Assert.That(localMovement, Is.Not.Null);

                Vector3 safePosition = localHitbox.transform.position +
                    Vector3.right * 8f;
                spawnedEnemy = Object.Instantiate(
                    skeletonPrefab,
                    safePosition,
                    Quaternion.identity);
                NetworkServer.Spawn(spawnedEnemy);

                NetworkEnemySimulationAgent agent =
                    spawnedEnemy.GetComponent<NetworkEnemySimulationAgent>();
                EnemyController controller =
                    spawnedEnemy.GetComponent<EnemyController>();
                EnemyAttackMelee melee =
                    spawnedEnemy.GetComponent<EnemyAttackMelee>();
                NetworkEnemyMeleeReplica replica =
                    spawnedEnemy.GetComponent<NetworkEnemyMeleeReplica>();
                NetworkEnemySimulationWorld world =
                    NetworkEnemySimulationWorld.Instance;
                Assert.That(agent, Is.Not.Null);
                Assert.That(controller, Is.Not.Null);
                Assert.That(melee, Is.Not.Null);
                Assert.That(replica, Is.Not.Null);
                Assert.That(world, Is.Not.Null);

                float initializationDeadline = Time.realtimeSinceStartup + 5f;
                while (!agent.ProductEnemyInitialized &&
                       Time.realtimeSinceStartup < initializationDeadline)
                {
                    yield return null;
                }
                Assert.That(agent.ProductEnemyInitialized, Is.True);

                EnemySimulationAssignment replicaAssignment =
                    world.Registry.AssignClientOwner(
                        agent.netId,
                        remoteEndpoint.PlayerEntityId,
                        remoteEndpoint.PlayerEntityId);
                agent.SetServerAssignment(replicaAssignment);
                float replicaDeadline = Time.realtimeSinceStartup + 2f;
                while (agent.Authority.Role != EnemySimulationRole.Replica &&
                       Time.realtimeSinceStartup < replicaDeadline)
                {
                    yield return null;
                }
                Assert.That(agent.Authority.Role,
                    Is.EqualTo(EnemySimulationRole.Replica));
                Assert.That(melee.enabled, Is.False);

                controller.stats.Damage = 5;
                Rigidbody2D enemyBody = spawnedEnemy.GetComponent<Rigidbody2D>();
                enemyBody.position = localHitbox.transform.position;
                spawnedEnemy.transform.position = localHitbox.transform.position;
                Physics2D.SyncTransforms();

                int healthBeforeReplicaHit = localCombatant.CurrentHealth;
                int minimumReplicaHealth = healthBeforeReplicaHit;
                void CaptureReplicaHealth(int current, int maximum)
                {
                    minimumReplicaHealth = Mathf.Min(minimumReplicaHealth, current);
                }
                localCombatant.Combatant.HealthChanged += CaptureReplicaHealth;

                double activeStart = NetworkTime.time;
                var warning = new EnemyAttackPresentationEdge
                {
                    EnemyEntityId = agent.netId,
                    AssignmentEpoch = replicaAssignment.Epoch,
                    StateSequence = 1u,
                    StateStartNetworkTime = activeStart,
                    PhaseDuration = 0.2f,
                    Phase = EnemyAttackPresentationPhase.Warning,
                    Facing = Vector2.right
                };
                var active = warning;
                active.StateSequence = 2u;
                active.Phase = EnemyAttackPresentationPhase.Active;
                active.PhaseDuration = 0.5f;
                world.SubmitClientAttackPresentations(
                    remoteEndpoint,
                    new EnemyAttackPresentationBatch
                    {
                        BatchSequence = 1u,
                        Edges = new[] { warning, active }
                    });

                float damageDeadline = Time.realtimeSinceStartup + 2f;
                while (minimumReplicaHealth == healthBeforeReplicaHit &&
                       Time.realtimeSinceStartup < damageDeadline)
                {
                    yield return null;
                }
                localCombatant.Combatant.HealthChanged -= CaptureReplicaHealth;
                int expectedReplicaHealth = healthBeforeReplicaHit - 5;
                Assert.That(minimumReplicaHealth, Is.EqualTo(expectedReplicaHealth),
                    "Replica DamageArea must judge only the local Player hit.");
                Assert.That(replica.LastAppliedSequence, Is.EqualTo(2u));
                Assert.That(replica.LastAppliedAssignmentEpoch,
                    Is.EqualTo(replicaAssignment.Epoch));
                Assert.That(replica.LastAppliedPhase,
                    Is.EqualTo(EnemyAttackPresentationPhase.Active));
                Assert.That(agent.LatestAttackPresentation.Phase,
                    Is.EqualTo(EnemyAttackPresentationPhase.Active));

                NetworkCombatWorld combatWorld = NetworkCombatWorld.Instance;
                CanonicalEntityState canonicalPlayer = default;
                float reportDeadline = Time.realtimeSinceStartup + 2f;
                while ((!combatWorld.Gateway.Ledger.TryGetState(
                            NetworkClient.localPlayer.netId,
                            out canonicalPlayer) ||
                        canonicalPlayer.Health != expectedReplicaHealth) &&
                       Time.realtimeSinceStartup < reportDeadline)
                {
                    yield return null;
                }
                Assert.That(canonicalPlayer.Health, Is.EqualTo(expectedReplicaHealth),
                    "Owner-Final PlayerHealthReport must converge the local hit.");

                var recovery = active;
                recovery.StateSequence = 3u;
                recovery.StateStartNetworkTime = NetworkTime.time;
                recovery.Phase = EnemyAttackPresentationPhase.Recovery;
                recovery.PhaseDuration = 0.1f;
                world.SubmitClientAttackPresentations(
                    remoteEndpoint,
                    new EnemyAttackPresentationBatch
                    {
                        BatchSequence = 2u,
                        Edges = new[] { recovery }
                    });
                yield return null;
                Assert.That(replica.DamageWindowActive, Is.False);
                Assert.That(replica.HasReplicaAttackInstance, Is.False);

                localMovement.IncreaseHealth(5);
                float healDeadline = Time.realtimeSinceStartup + 2f;
                while (localCombatant.CurrentHealth != healthBeforeReplicaHit &&
                       Time.realtimeSinceStartup < healDeadline)
                {
                    yield return null;
                }
                Assert.That(localCombatant.CurrentHealth,
                    Is.EqualTo(healthBeforeReplicaHit));

                // A new epoch reproduces a Late Join whose first cached phase is
                // an already-expired Active edge. Visual replay is allowed; damage
                // compensation is explicitly forbidden.
                replicaAssignment = world.Registry.AssignClientOwner(
                    agent.netId,
                    remoteEndpoint.PlayerEntityId,
                    remoteEndpoint.PlayerEntityId);
                agent.SetServerAssignment(replicaAssignment);
                var expiredActive = active;
                expiredActive.AssignmentEpoch = replicaAssignment.Epoch;
                expiredActive.StateSequence = 1u;
                expiredActive.StateStartNetworkTime = NetworkTime.time - 1d;
                expiredActive.PhaseDuration = 0.1f;
                world.SubmitClientAttackPresentations(
                    remoteEndpoint,
                    new EnemyAttackPresentationBatch
                    {
                        BatchSequence = 3u,
                        Edges = new[] { expiredActive }
                    });
                yield return null;
                Assert.That(replica.LastAppliedSequence, Is.EqualTo(1u));
                Assert.That(replica.LastAppliedAssignmentEpoch,
                    Is.EqualTo(replicaAssignment.Epoch));
                Assert.That(replica.LastAppliedPhase,
                    Is.EqualTo(EnemyAttackPresentationPhase.Active));
                Assert.That(replica.HasReplicaAttackInstance, Is.True,
                    "Expired Active still restores its presentation object.");
                Assert.That(replica.DamageWindowActive, Is.False,
                    "Expired Active must never open a compensating damage window.");
                int healthBeforeExpiredWindow = localCombatant.CurrentHealth;
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(localCombatant.CurrentHealth,
                    Is.EqualTo(healthBeforeExpiredWindow));

                var expiredRecovery = expiredActive;
                expiredRecovery.StateSequence = 2u;
                expiredRecovery.StateStartNetworkTime = NetworkTime.time;
                expiredRecovery.Phase = EnemyAttackPresentationPhase.Recovery;
                world.SubmitClientAttackPresentations(
                    remoteEndpoint,
                    new EnemyAttackPresentationBatch
                    {
                        BatchSequence = 4u,
                        Edges = new[] { expiredRecovery }
                    });
                yield return null;

                enemyBody.position = safePosition;
                spawnedEnemy.transform.position = safePosition;
                EnemySimulationAssignment localOwnerAssignment =
                    world.Registry.AssignClientOwner(
                        agent.netId,
                        NetworkClient.localPlayer.netId,
                        NetworkClient.localPlayer.netId);
                agent.SetServerAssignment(localOwnerAssignment);
                float ownerDeadline = Time.realtimeSinceStartup + 2f;
                while ((agent.Authority.Role != EnemySimulationRole.ClientOwner ||
                        !melee.enabled) &&
                       Time.realtimeSinceStartup < ownerDeadline)
                {
                    yield return null;
                }
                Assert.That(agent.Authority.Role,
                    Is.EqualTo(EnemySimulationRole.ClientOwner));
                Assert.That(melee.enabled, Is.True,
                    "The SimulationOwner must run the original EnemyAttackMelee.");

                enemyBody.position = localHitbox.transform.position + Vector3.right;
                spawnedEnemy.transform.position = enemyBody.position;
                Physics2D.SyncTransforms();
                int healthBeforeOwnerAttack = localCombatant.CurrentHealth;
                int minimumOwnerHealth = healthBeforeOwnerAttack;
                void CaptureOwnerHealth(int current, int maximum)
                {
                    minimumOwnerHealth = Mathf.Min(minimumOwnerHealth, current);
                }
                localCombatant.Combatant.HealthChanged += CaptureOwnerHealth;
                float ownerAttackDeadline = Time.realtimeSinceStartup + 6f;
                while (minimumOwnerHealth == healthBeforeOwnerAttack &&
                       Time.realtimeSinceStartup < ownerAttackDeadline)
                {
                    yield return null;
                }
                localCombatant.Combatant.HealthChanged -= CaptureOwnerHealth;
                Assert.That(minimumOwnerHealth,
                    Is.EqualTo(healthBeforeOwnerAttack - 5),
                    "The local SimulationOwner must retain the original melee " +
                    "attack gameplay path.");
                Assert.That(
                    world.Registry.TryGetLatestAttackPresentation(
                        agent.netId,
                        out EnemyAttackPresentationEdge ownerEdge),
                    Is.True,
                    "Owner attack phases must reach the Server reliable cache.");
                Assert.That(ownerEdge.StateSequence, Is.GreaterThan(0u));
            }
            finally
            {
                if (spawnedEnemy != null && NetworkServer.active)
                {
                    NetworkServer.Destroy(spawnedEnemy);
                }
                if (remoteEndpointObject != null && NetworkServer.active)
                {
                    NetworkServer.Destroy(remoteEndpointObject);
                }
                if (manager != null && (NetworkServer.active || NetworkClient.active))
                {
                    manager.StopHost();
                }
            }

            float shutdownDeadline = Time.realtimeSinceStartup + 3f;
            while ((NetworkServer.active || NetworkClient.active) &&
                   Time.realtimeSinceStartup < shutdownDeadline)
            {
                yield return null;
            }
            Assert.That(NetworkServer.active, Is.False);
            Assert.That(NetworkClient.active, Is.False);
        }

        [UnityTest]
        public IEnumerator Host_ObserverRelayAndServerFallbackConverge()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                SandboxScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(SandboxScenePath, LoadSceneMode.Single);
#endif
            NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();
            Assert.That(manager, Is.Not.Null);
            GameObject remotePlayer = null;

            try
            {
                manager.StartHost();
                float spawnDeadline = Time.realtimeSinceStartup + 8f;
                while ((NetworkClient.localPlayer == null ||
                        Object.FindObjectsByType<NetworkEnemySimulationAgent>(
                            FindObjectsSortMode.None).Length < 120) &&
                       Time.realtimeSinceStartup < spawnDeadline)
                {
                    yield return null;
                }

                NetworkEnemySimulationWorld world =
                    NetworkEnemySimulationWorld.Instance;
                NetworkEnemySimulationAgent[] agents =
                    Object.FindObjectsByType<NetworkEnemySimulationAgent>(
                        FindObjectsSortMode.None);
                NetworkEnemySimulationEndpoint localEndpoint =
                    NetworkClient.localPlayer.GetComponent<NetworkEnemySimulationEndpoint>();
                Assert.That(world, Is.Not.Null);
                Assert.That(localEndpoint, Is.Not.Null);
                Assert.That(agents.Length, Is.GreaterThanOrEqualTo(3));

                remotePlayer = new GameObject("Remote Simulation Endpoint");
                remotePlayer.SetActive(false);
                remotePlayer.transform.position = new Vector3(100f, 100f, 0f);
                remotePlayer.AddComponent<NetworkIdentity>();
                remotePlayer.AddComponent<NetworkEnemySimulationEndpoint>();
                remotePlayer.SetActive(true);
                NetworkServer.Spawn(remotePlayer);
                NetworkEnemySimulationEndpoint remoteEndpoint =
                    remotePlayer.GetComponent<NetworkEnemySimulationEndpoint>();
                float remoteDeadline = Time.realtimeSinceStartup + 3f;
                while ((remoteEndpoint.netId == 0u ||
                        !NetworkClient.spawned.ContainsKey(remoteEndpoint.netId)) &&
                       Time.realtimeSinceStartup < remoteDeadline)
                {
                    yield return null;
                }

                Assert.That(remoteEndpoint.netId, Is.Not.Zero);
                Assert.That(remoteEndpoint.isOwned, Is.False);

                NetworkEnemySimulationAgent observerAgent = agents[0];
                EnemySimulationAssignment observerAssignment =
                    world.Registry.AssignClientOwner(
                        observerAgent.netId,
                        remoteEndpoint.PlayerEntityId,
                        remoteEndpoint.PlayerEntityId);
                observerAgent.SetServerAssignment(observerAssignment);
                float roleDeadline = Time.realtimeSinceStartup + 2f;
                while (observerAgent.Authority.Role != EnemySimulationRole.Replica &&
                       Time.realtimeSinceStartup < roleDeadline)
                {
                    yield return null;
                }

                Assert.That(
                    observerAgent.Authority.Role,
                    Is.EqualTo(EnemySimulationRole.Replica));
                Assert.That(observerAgent.Authority.RunsNavigation, Is.False);
                Assert.That(
                    observerAgent.GetComponent<LocalEnemyChase>().enabled,
                    Is.False);

                Vector2 firstRelayPosition =
                    (Vector2)observerAgent.transform.position + Vector2.right * 4f;
                var firstSnapshot = new EnemySimulationSnapshot
                {
                    EnemyEntityId = observerAgent.netId,
                    AssignmentEpoch = observerAssignment.Epoch,
                    Sequence = 1u,
                    SampleNetworkTime = NetworkTime.time,
                    Position = firstRelayPosition,
                    Velocity = Vector2.right,
                    Facing = Vector2.right,
                    Flags = EnemySimulationSnapshotFlags.Discontinuity
                };
                world.SubmitClientSnapshots(
                    remoteEndpoint,
                    new EnemySimulationSnapshotBatch
                    {
                        BatchSequence = 100u,
                        Snapshots = new[] { firstSnapshot }
                    });
                float relayDeadline = Time.realtimeSinceStartup + 2f;
                while (((Vector2)observerAgent.transform.position - firstRelayPosition)
                           .sqrMagnitude >= 0.0001f &&
                       Time.realtimeSinceStartup < relayDeadline)
                {
                    yield return null;
                }

                Assert.That(
                    world.Registry.TryGetLatestSnapshot(
                        observerAgent.netId,
                        out EnemySimulationSnapshot acceptedFirst),
                    Is.True);
                Assert.That(acceptedFirst.Sequence, Is.EqualTo(1u));
                Assert.That(
                    ((Vector2)observerAgent.transform.position - firstRelayPosition)
                        .sqrMagnitude,
                    Is.LessThan(0.0001f));

                int receivedAttackEdges = 0;
                observerAgent.AttackPresentationChanged += _ => receivedAttackEdges++;
                double sharedPhaseTime = NetworkTime.time;
                var warningEdge = new EnemyAttackPresentationEdge
                {
                    EnemyEntityId = observerAgent.netId,
                    AssignmentEpoch = observerAssignment.Epoch,
                    StateSequence = 1u,
                    StateStartNetworkTime = sharedPhaseTime,
                    PhaseDuration = 1f,
                    Phase = EnemyAttackPresentationPhase.Warning,
                    Facing = Vector2.left
                };
                var activeEdge = warningEdge;
                activeEdge.StateSequence = 2u;
                activeEdge.Phase = EnemyAttackPresentationPhase.Active;
                world.SubmitClientAttackPresentations(
                    remoteEndpoint,
                    new EnemyAttackPresentationBatch
                    {
                        BatchSequence = 1u,
                        Edges = new[] { warningEdge, activeEdge }
                    });
                float attackRelayDeadline = Time.realtimeSinceStartup + 2f;
                while ((!observerAgent.HasLatestAttackPresentation ||
                        observerAgent.LatestAttackPresentation.StateSequence != 2u) &&
                       Time.realtimeSinceStartup < attackRelayDeadline)
                {
                    yield return null;
                }

                Assert.That(
                    world.Registry.TryGetLatestAttackPresentation(
                        observerAgent.netId,
                        out EnemyAttackPresentationEdge acceptedAttack),
                    Is.True);
                Assert.That(acceptedAttack.Phase,
                    Is.EqualTo(EnemyAttackPresentationPhase.Active));
                Assert.That(observerAgent.LatestAttackPresentation.Phase,
                    Is.EqualTo(EnemyAttackPresentationPhase.Active));
                Assert.That(receivedAttackEdges, Is.EqualTo(2),
                    "Reliable presentation edges must preserve same-frame phase order.");

                var duplicateAttack = activeEdge;
                duplicateAttack.Phase = EnemyAttackPresentationPhase.Recovery;
                world.SubmitClientAttackPresentations(
                    remoteEndpoint,
                    new EnemyAttackPresentationBatch
                    {
                        BatchSequence = 2u,
                        Edges = new[] { duplicateAttack }
                    });
                yield return null;
                Assert.That(
                    world.Registry.TryGetLatestAttackPresentation(
                        observerAgent.netId,
                        out EnemyAttackPresentationEdge afterDuplicateAttack),
                    Is.True);
                Assert.That(afterDuplicateAttack.Phase,
                    Is.EqualTo(EnemyAttackPresentationPhase.Active));
                Assert.That(receivedAttackEdges, Is.EqualTo(2));

                // Reproduce the Late Join ordering window where the reliable
                // cached edge arrives before this Enemy has registered locally.
                var lateJoinRecovery = activeEdge;
                lateJoinRecovery.StateSequence = 3u;
                lateJoinRecovery.StateStartNetworkTime = NetworkTime.time;
                lateJoinRecovery.Phase = EnemyAttackPresentationPhase.Recovery;
                MethodInfo applyAttackPresentations =
                    typeof(NetworkEnemySimulationWorld).GetMethod(
                        "ApplyAttackPresentations",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(applyAttackPresentations, Is.Not.Null);
                world.UnregisterClientEnemy(observerAgent);
                applyAttackPresentations.Invoke(
                    world,
                    new object[]
                    {
                        new EnemyAttackPresentationBatch
                        {
                            Edges = new[] { lateJoinRecovery }
                        }
                    });
                Assert.That(world.PendingClientAttackPresentationCount,
                    Is.EqualTo(1));
                world.RegisterClientEnemy(observerAgent);
                Assert.That(world.PendingClientAttackPresentationCount, Is.Zero);
                Assert.That(observerAgent.LatestAttackPresentation.Phase,
                    Is.EqualTo(EnemyAttackPresentationPhase.Recovery));
                Assert.That(receivedAttackEdges, Is.EqualTo(3));

                NetworkEnemySimulationAgent reorderedAgent = agents[1];
                EnemySimulationAssignment reorderedAssignment =
                    world.Registry.AssignClientOwner(
                        reorderedAgent.netId,
                        remoteEndpoint.PlayerEntityId,
                        remoteEndpoint.PlayerEntityId);
                reorderedAgent.SetServerAssignment(reorderedAssignment);
                yield return null;
                var reorderedSnapshot = new EnemySimulationSnapshot
                {
                    EnemyEntityId = reorderedAgent.netId,
                    AssignmentEpoch = reorderedAssignment.Epoch,
                    Sequence = 1u,
                    SampleNetworkTime = NetworkTime.time,
                    Position = reorderedAgent.transform.position,
                    Velocity = Vector2.zero,
                    Facing = Vector2.right
                };
                world.SubmitClientSnapshots(
                    remoteEndpoint,
                    new EnemySimulationSnapshotBatch
                    {
                        // This older datagram carries a different Enemy and must
                        // survive reordering after batch 100.
                        BatchSequence = 99u,
                        Snapshots = new[] { reorderedSnapshot }
                    });
                Assert.That(
                    world.Registry.TryGetLatestSnapshot(
                        reorderedAgent.netId,
                        out EnemySimulationSnapshot acceptedReordered),
                    Is.True);
                Assert.That(acceptedReordered.Sequence, Is.EqualTo(1u));

                Vector2 secondRelayPosition = firstRelayPosition + Vector2.right * 2f;
                EnemySimulationSnapshot secondSnapshot = firstSnapshot;
                secondSnapshot.Sequence = 2u;
                secondSnapshot.SampleNetworkTime += 0.05d;
                secondSnapshot.Position = secondRelayPosition;
                world.SubmitClientSnapshots(
                    remoteEndpoint,
                    new EnemySimulationSnapshotBatch
                    {
                        BatchSequence = 101u,
                        Snapshots = new[] { secondSnapshot }
                    });
                yield return null;

                Assert.That(
                    world.Registry.TryGetLatestSnapshot(
                        observerAgent.netId,
                        out EnemySimulationSnapshot acceptedSecond),
                    Is.True);
                Assert.That(acceptedSecond.Sequence, Is.EqualTo(2u));
                Assert.That(acceptedSecond.Position, Is.EqualTo(secondRelayPosition));

                EnemySimulationSnapshot duplicate = secondSnapshot;
                duplicate.Position += Vector2.right * 50f;
                duplicate.SampleNetworkTime += 1d;
                world.SubmitClientSnapshots(
                    remoteEndpoint,
                    new EnemySimulationSnapshotBatch
                    {
                        BatchSequence = 102u,
                        Snapshots = new[] { duplicate }
                    });
                yield return null;
                Assert.That(
                    world.Registry.TryGetLatestSnapshot(
                        observerAgent.netId,
                        out EnemySimulationSnapshot afterDuplicate),
                    Is.True);
                Assert.That(afterDuplicate.Position, Is.EqualTo(secondRelayPosition));

                CombatantBehaviour observerCombatant =
                    observerAgent.GetComponent<CombatantBehaviour>();
                observerCombatant.ReceiveDamage(new MonsterSupergroup.GAS.DamageInfo(
                    1u,
                    observerCombatant.CurrentHealth,
                    false));
                Assert.That(observerAgent.IsCanonicalAlive, Is.False);
                Vector2 deathPosition = observerAgent.transform.position;
                EnemySimulationSnapshot afterPredictedDeath = secondSnapshot;
                afterPredictedDeath.Sequence = 3u;
                afterPredictedDeath.SampleNetworkTime += 2d;
                afterPredictedDeath.Position += Vector2.right * 100f;
                observerAgent.ReceiveRemoteSnapshot(afterPredictedDeath);
                Assert.That(
                    observerAgent.GetComponent<EnemySnapshotInterpolator>()
                        .BufferedSnapshotCount,
                    Is.Zero,
                    "Predicted death must reject later movement snapshots until canonical reconciliation.");
                Assert.That(
                    ((Vector2)observerAgent.transform.position - deathPosition)
                        .sqrMagnitude,
                    Is.LessThan(0.0001f),
                    "Predicted death must stop later movement snapshots from moving the body.");

                NetworkEnemySimulationAgent fallbackAgent = agents[2];
                Vector2 fallbackStart = fallbackAgent.transform.position;
                world.UnregisterPlayer(localEndpoint);
                float fallbackDeadline = Time.realtimeSinceStartup + 3f;
                while ((fallbackAgent.Assignment.Host !=
                            EnemySimulationHost.ServerFallback ||
                        fallbackAgent.Authority.Role !=
                            EnemySimulationRole.ServerFallback ||
                        ((Vector2)fallbackAgent.transform.position - fallbackStart)
                            .sqrMagnitude < 0.01f) &&
                       Time.realtimeSinceStartup < fallbackDeadline)
                {
                    yield return null;
                }

                Assert.That(
                    fallbackAgent.Assignment.Host,
                    Is.EqualTo(EnemySimulationHost.ServerFallback));
                Assert.That(
                    fallbackAgent.Assignment.AggroTargetPlayerId,
                    Is.EqualTo(remoteEndpoint.PlayerEntityId));
                Assert.That(
                    fallbackAgent.Authority.Role,
                    Is.EqualTo(EnemySimulationRole.ServerFallback));
                Assert.That(fallbackAgent.Authority.RunsNavigation, Is.True);
                Assert.That(fallbackAgent.Authority.RunsRubberBand, Is.False);
                Assert.That(
                    ((Vector2)fallbackAgent.transform.position - fallbackStart)
                        .sqrMagnitude,
                    Is.GreaterThanOrEqualTo(0.01f));
                float fallbackSnapshotDeadline = Time.realtimeSinceStartup + 2f;
                EnemySimulationSnapshot fallbackSnapshot = default;
                while ((!world.Registry.TryGetLatestSnapshot(
                            fallbackAgent.netId,
                            out fallbackSnapshot) ||
                        fallbackSnapshot.Sequence == 0u ||
                        fallbackSnapshot.AssignmentEpoch !=
                            fallbackAgent.Assignment.Epoch) &&
                       Time.realtimeSinceStartup < fallbackSnapshotDeadline)
                {
                    yield return null;
                }
                Assert.That(
                    world.Registry.TryGetLatestSnapshot(
                        fallbackAgent.netId,
                        out fallbackSnapshot),
                    Is.True);
                Assert.That(
                    fallbackSnapshot.AssignmentEpoch,
                    Is.EqualTo(fallbackAgent.Assignment.Epoch));
                Assert.That(fallbackSnapshot.Sequence, Is.GreaterThan(0u));

                world.UnregisterPlayer(remoteEndpoint);
                yield return null;
                Assert.That(
                    fallbackAgent.Assignment.Host,
                    Is.EqualTo(EnemySimulationHost.Frozen),
                    "A second disconnect must also handle ServerFallback targets.");
                Assert.That(
                    fallbackAgent.Authority.Role,
                    Is.EqualTo(EnemySimulationRole.Frozen));
            }
            finally
            {
                if (remotePlayer != null && NetworkServer.active)
                {
                    NetworkServer.Destroy(remotePlayer);
                }
                if (manager != null && (NetworkServer.active || NetworkClient.active))
                {
                    manager.StopHost();
                }
            }

            float shutdownDeadline = Time.realtimeSinceStartup + 3f;
            while ((NetworkServer.active || NetworkClient.active) &&
                   Time.realtimeSinceStartup < shutdownDeadline)
            {
                yield return null;
            }

            Assert.That(NetworkServer.active, Is.False);
            Assert.That(NetworkClient.active, Is.False);
        }

        [UnityTest]
        public IEnumerator DedicatedServer_ClientOwnedEnemiesWaitThenFallbackRuns()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                SandboxScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(SandboxScenePath, LoadSceneMode.Single);
#endif
            NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();
            Assert.That(manager, Is.Not.Null);
            GameObject ownerEndpointObject = null;
            GameObject fallbackTargetObject = null;

            try
            {
                manager.StartServer();
                Assert.That(NetworkServer.active, Is.True);
                Assert.That(NetworkClient.active, Is.False);

                ownerEndpointObject = new GameObject("Server Test Client Owner");
                ownerEndpointObject.SetActive(false);
                ownerEndpointObject.transform.position = Vector3.zero;
                ownerEndpointObject.AddComponent<NetworkIdentity>();
                ownerEndpointObject.AddComponent<NetworkEnemySimulationEndpoint>();
                ownerEndpointObject.SetActive(true);
                NetworkServer.Spawn(ownerEndpointObject);
                NetworkEnemySimulationEndpoint ownerEndpoint =
                    ownerEndpointObject.GetComponent<NetworkEnemySimulationEndpoint>();

                float spawnDeadline = Time.realtimeSinceStartup + 8f;
                while (Object.FindObjectsByType<NetworkEnemySimulationAgent>(
                           FindObjectsSortMode.None).Length < 120 &&
                       Time.realtimeSinceStartup < spawnDeadline)
                {
                    yield return null;
                }

                NetworkEnemySimulationWorld world =
                    NetworkEnemySimulationWorld.Instance;
                NetworkEnemySimulationAgent[] agents =
                    Object.FindObjectsByType<NetworkEnemySimulationAgent>(
                        FindObjectsSortMode.None);
                Assert.That(world, Is.Not.Null);
                Assert.That(agents, Has.Length.EqualTo(120));
                Assert.That(
                    agents.All(agent =>
                        agent.Assignment.Host == EnemySimulationHost.ClientPlayer &&
                        agent.Assignment.SimulationOwnerPlayerId ==
                            ownerEndpoint.PlayerEntityId &&
                        agent.Authority.Role == EnemySimulationRole.Replica &&
                        !agent.Authority.RunsNavigation),
                    Is.True,
                    "A Dedicated Server must not also run a Client-owned Enemy.");

                NetworkEnemySimulationAgent fallbackAgent = agents[0];
                Vector2 waitingPosition = fallbackAgent.transform.position;
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(
                    ((Vector2)fallbackAgent.transform.position - waitingPosition)
                        .sqrMagnitude,
                    Is.LessThan(0.0001f));

                fallbackTargetObject = new GameObject("Server Fallback Target");
                fallbackTargetObject.SetActive(false);
                fallbackTargetObject.transform.position =
                    new Vector3(100f, 100f, 0f);
                fallbackTargetObject.AddComponent<NetworkIdentity>();
                fallbackTargetObject.AddComponent<NetworkEnemySimulationEndpoint>();
                fallbackTargetObject.SetActive(true);
                NetworkServer.Spawn(fallbackTargetObject);
                NetworkEnemySimulationEndpoint fallbackTarget =
                    fallbackTargetObject.GetComponent<NetworkEnemySimulationEndpoint>();

                world.UnregisterPlayer(ownerEndpoint);
                float fallbackDeadline = Time.realtimeSinceStartup + 3f;
                while ((fallbackAgent.Authority.Role !=
                            EnemySimulationRole.ServerFallback ||
                        ((Vector2)fallbackAgent.transform.position - waitingPosition)
                            .sqrMagnitude < 0.01f) &&
                       Time.realtimeSinceStartup < fallbackDeadline)
                {
                    yield return null;
                }

                Assert.That(
                    fallbackAgent.Assignment.Host,
                    Is.EqualTo(EnemySimulationHost.ServerFallback));
                Assert.That(
                    fallbackAgent.Assignment.AggroTargetPlayerId,
                    Is.EqualTo(fallbackTarget.PlayerEntityId));
                Assert.That(
                    fallbackAgent.Authority.Role,
                    Is.EqualTo(EnemySimulationRole.ServerFallback));
                Assert.That(
                    ((Vector2)fallbackAgent.transform.position - waitingPosition)
                        .sqrMagnitude,
                    Is.GreaterThanOrEqualTo(0.01f));
                float snapshotDeadline = Time.realtimeSinceStartup + 2f;
                EnemySimulationSnapshot fallbackSnapshot = default;
                while ((!world.Registry.TryGetLatestSnapshot(
                            fallbackAgent.netId,
                            out fallbackSnapshot) ||
                        fallbackSnapshot.Sequence == 0u ||
                        fallbackSnapshot.AssignmentEpoch !=
                            fallbackAgent.Assignment.Epoch) &&
                       Time.realtimeSinceStartup < snapshotDeadline)
                {
                    yield return null;
                }
                Assert.That(
                    world.Registry.TryGetLatestSnapshot(
                        fallbackAgent.netId,
                        out fallbackSnapshot),
                    Is.True);
                Assert.That(fallbackSnapshot.Sequence, Is.GreaterThan(0u));
                Assert.That(
                    fallbackSnapshot.AssignmentEpoch,
                    Is.EqualTo(fallbackAgent.Assignment.Epoch));
            }
            finally
            {
                if (ownerEndpointObject != null && NetworkServer.active)
                {
                    NetworkServer.Destroy(ownerEndpointObject);
                }
                if (fallbackTargetObject != null && NetworkServer.active)
                {
                    NetworkServer.Destroy(fallbackTargetObject);
                }
                if (manager != null && NetworkServer.active)
                {
                    manager.StopServer();
                }
            }

            float shutdownDeadline = Time.realtimeSinceStartup + 3f;
            while (NetworkServer.active &&
                   Time.realtimeSinceStartup < shutdownDeadline)
            {
                yield return null;
            }
            Assert.That(NetworkServer.active, Is.False);
            Assert.That(NetworkClient.active, Is.False);
        }

        [UnityTest]
        public IEnumerator DedicatedServer_BossModeNeverBecomesClientSimulation()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                SandboxScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(SandboxScenePath, LoadSceneMode.Single);
#endif
            NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();
            Assert.That(manager, Is.Not.Null);
            GameObject endpointObject = null;
            GameObject bossObject = null;

            try
            {
                manager.StartServer();
                NetworkEnemySandboxSpawner spawner =
                    Object.FindFirstObjectByType<NetworkEnemySandboxSpawner>();
                Assert.That(spawner, Is.Not.Null);
                GameObject lightweightEnemy = manager.spawnPrefabs.Single(
                    prefab => prefab != null && prefab.name == "NetworkEnemy");
                spawner.Configure(lightweightEnemy, 0, 1, 1f, Vector2.zero);

                endpointObject = new GameObject("Boss Target Endpoint");
                endpointObject.SetActive(false);
                endpointObject.AddComponent<NetworkIdentity>();
                endpointObject.AddComponent<NetworkEnemySimulationEndpoint>();
                endpointObject.SetActive(true);
                NetworkServer.Spawn(endpointObject);
                NetworkEnemySimulationEndpoint endpoint =
                    endpointObject.GetComponent<NetworkEnemySimulationEndpoint>();

                bossObject = new GameObject("Server Authoritative Boss Probe");
                bossObject.SetActive(false);
                bossObject.AddComponent<NetworkIdentity>();
                bossObject.AddComponent<Rigidbody2D>();
                EnemySimulationAuthority bossAuthority =
                    bossObject.AddComponent<EnemySimulationAuthority>();
                bossAuthority.ConfigureNetworkManaged(
                    EnemySimulationMode.BossServer,
                    enableCombatDecisions: true);
                bossObject.AddComponent<EnemySnapshotInterpolator>();
                NetworkEnemySimulationAgent bossAgent =
                    bossObject.AddComponent<NetworkEnemySimulationAgent>();
                bossObject.SetActive(true);
                NetworkServer.Spawn(bossObject);

                float assignmentDeadline = Time.realtimeSinceStartup + 2f;
                while ((bossAgent.Assignment.Host !=
                            EnemySimulationHost.ServerAuthoritative ||
                        bossAgent.Authority.Role !=
                            EnemySimulationRole.ServerAuthoritative) &&
                       Time.realtimeSinceStartup < assignmentDeadline)
                {
                    yield return null;
                }

                Assert.That(
                    bossAgent.SimulationMode,
                    Is.EqualTo(EnemySimulationMode.BossServer));
                Assert.That(
                    bossAgent.Assignment.Host,
                    Is.EqualTo(EnemySimulationHost.ServerAuthoritative));
                Assert.That(bossAgent.Assignment.SimulationOwnerPlayerId, Is.Zero);
                Assert.That(
                    bossAgent.Assignment.AggroTargetPlayerId,
                    Is.EqualTo(endpoint.PlayerEntityId));
                Assert.That(
                    bossAgent.Authority.Role,
                    Is.EqualTo(EnemySimulationRole.ServerAuthoritative));
                Assert.That(bossAgent.Authority.RunsNavigation, Is.True);
                Assert.That(bossAgent.Authority.RunsCombatDecisions, Is.True);

                NetworkEnemySimulationWorld world =
                    NetworkEnemySimulationWorld.Instance;
                float snapshotDeadline = Time.realtimeSinceStartup + 2f;
                EnemySimulationSnapshot snapshot = default;
                while ((!world.Registry.TryGetLatestSnapshot(
                            bossAgent.netId,
                            out snapshot) ||
                        snapshot.Sequence == 0u) &&
                       Time.realtimeSinceStartup < snapshotDeadline)
                {
                    yield return null;
                }
                Assert.That(snapshot.Sequence, Is.GreaterThan(0u));
                Assert.That(
                    world.Registry.TryAcceptClientSnapshot(
                        endpoint.PlayerEntityId,
                        snapshot),
                    Is.EqualTo(EnemySnapshotRejectionReason.WrongHost));
            }
            finally
            {
                if (bossObject != null && NetworkServer.active)
                {
                    NetworkServer.Destroy(bossObject);
                }
                if (endpointObject != null && NetworkServer.active)
                {
                    NetworkServer.Destroy(endpointObject);
                }
                if (manager != null && NetworkServer.active)
                {
                    manager.StopServer();
                }
            }

            float shutdownDeadline = Time.realtimeSinceStartup + 3f;
            while (NetworkServer.active &&
                   Time.realtimeSinceStartup < shutdownDeadline)
            {
                yield return null;
            }
            Assert.That(NetworkServer.active, Is.False);
            Assert.That(NetworkClient.active, Is.False);
        }
    }
}
