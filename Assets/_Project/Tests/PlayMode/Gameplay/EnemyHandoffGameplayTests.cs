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

        [UnityTest]
        public IEnumerator DashCheckpointRestoresMotionOnlyInsideRemainingActiveWindow()
        {
#if UNITY_EDITOR
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Content/NetworkCombat/Limbo/Dash/ReferenceBrotchiDash.prefab");
            var agent=Spawn(prefab);
            yield return WaitFor(()=>agent.ProductEnemyInitialized&&agent.Authority.RunsCombatDecisions,"Dash ready");
            var enemy=agent.GetComponent<EnemyController>();var dash=agent.GetComponent<EnemyAttackDash>();
            Vector2 pose=enemy.rigidBody.position;
            double start=EnemySimulationClock.CombatNow;
            var action=new EnemyActionState{Dash=true,ActionId=77,Phase=EnemyAttackPresentationPhase.Warning,
                WarningStartedAt=start,WarningUntil=start+.78,ActiveUntil=start+1.209999948,RecoveryUntil=start+1.289999948,NextAttackAt=start+1.789999948,
                Facing=Vector2.right,TargetPosition=pose+Vector2.right*3,DashStart=pose-Vector2.right*2,DashEnd=pose+Vector2.right*4,
                DashLastPosition=pose,DashWarningOrigin=pose-Vector2.right*2};
            int originalMask=enemy.collider.excludeLayers;
            foreach(double age in new[]{.2,.95,1.25,2.0})
            {
                enemy.SuspendSimulationExecution();enemy.RestoreSimulationAction(action,start+age);
                var restored=enemy.CaptureSimulationAction(start+age);
                Assert.That(restored.ActionId,Is.EqualTo(77));Assert.That(restored.DashStart,Is.EqualTo(action.DashStart));
                Assert.That(restored.DashLastPosition,Is.EqualTo(pose));Assert.That(enemy.rigidBody.position,Is.EqualTo(pose),"Restoring state must not jump back to the dash origin.");
                bool active=age>.78&&age<1.21;
                agent.GetComponent<NetworkEnemyMeleeReplica>().ApplyAction(action, agent.Assignment.Epoch, start+age);
                Assert.That(dash.attackCollider.enabled&&dash.damageInteraction.enabled,Is.EqualTo(active));
                if(active){Assert.That(enemy.rigidBody.simulated,Is.True);Assert.That(enemy.rigidBody.constraints,Is.EqualTo(RigidbodyConstraints2D.FreezeRotation));Assert.That(enemy.collider.excludeLayers.value,Is.EqualTo(64));}
                enemy.SuspendSimulationExecution();
                Assert.That(dash.attackCollider.enabled&&dash.damageInteraction.enabled,Is.EqualTo(active),"A simulation lease change does not restart or close the local hit window.");
                Assert.That(enemy.collider.excludeLayers.value,Is.EqualTo(originalMask));
                dash.ReleaseLocalFrame();Assert.That(dash.attackCollider.enabled||dash.damageInteraction.enabled,Is.False);
            }
#endif
            yield break;
        }

        [UnityTest]
        public IEnumerator ReferenceRepositionAppliesPoseEvenWhenServerRemainsSimulator()
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Content/NetworkCombat/Limbo/Stage2/ReferenceSkeleton.prefab");
            var instance = Object.Instantiate(prefab, Owner.transform.position + Vector3.right * 8, Quaternion.identity);
            var agent = instance.GetComponent<NetworkEnemySimulationAgent>();
            agent.ConfigureBirth(new EnemyBirthParameters { Enabled = true, SourceEnemy = "Skeleton", Variant = 0,
                Health = 50, Damage = 50, Speed = 2, SpeedMultiplier = 1.1f, Xp = 7, Knockback = 1,
                Wind = 1, Counted = true, ResetOnReposition = true });
            agent.ConfigureInitialServerTarget(Owner.netId); NetworkServer.Spawn(instance);
            yield return WaitFor(() => agent.ProductEnemyInitialized && agent.Authority.RunsCombatDecisions, "reference ready");
            World.Registry.TryGetLatestSnapshot(agent.netId, out var pose);
            var assignment = World.Registry.AssignServerAuthoritative(agent.netId, Owner.netId);
            pose.AssignmentEpoch = assignment.Epoch;
            agent.SetServerHandoff(new EnemySimulationHandoff { Assignment = assignment,
                Checkpoint = new EnemySimulationCheckpoint { Movement = pose }, Reason = EnemyTargetChangeReason.Forced });
            Vector2 destination = (Vector2)agent.transform.position + Vector2.up * 9;
            uint epoch = agent.Assignment.Epoch;
            var reposition = typeof(NetworkEnemySimulationWorld).GetMethod("RepositionReferenceEnemy",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That((bool)reposition.Invoke(World, new object[] { agent, destination }), Is.True);
            Assert.That(Vector2.Distance(agent.transform.position, destination), Is.LessThan(.001f), "Handoff must apply the new pose, not only renew the registry epoch.");
            Assert.That(Vector2.Distance(agent.GetComponent<Rigidbody2D>().position, destination), Is.LessThan(.001f));
            Assert.That(agent.Assignment.Epoch, Is.GreaterThan(epoch));
            Assert.That(agent.Assignment.Host, Is.EqualTo(EnemySimulationHost.ServerAuthoritative));
            Assert.That(agent.ReferenceResetVersion, Is.EqualTo(1));
            Assert.That(agent.GetComponent<EnemyController>().stats.SpeedMultiplier, Is.EqualTo(1));
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
