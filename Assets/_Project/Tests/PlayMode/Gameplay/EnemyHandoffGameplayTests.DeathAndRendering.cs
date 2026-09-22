using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class EnemyHandoffGameplayTests
    {
#if UNITY_EDITOR
        private NetworkEnemySimulationAgent SpawnDeathFixture() => Spawn(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Content/NetworkCombat/Limbo/Stage2/ReferenceBrotchi.prefab"));

        private void PredictFixtureDeath(NetworkEnemySimulationAgent agent)
        {
            var bridge = Owner.GetComponent<MirrorNetworkCombatBridge>();
            bridge.enabled = false;
            var execution = new LegacyCombatExecution(Owner.GetComponent<CombatRuntimeServiceProvider>().Services);
            var context = execution.BeginAttack(6, MonsterSupergroup.GAS.CombatTags.Damage);
            var enemy = agent.GetComponent<EnemyController>();
            enemy.Damage(enemy.CurrentHealth + 1, DamageType.Normal, new LegacyDamageSource(execution, context, 6));
            Assert.That(enemy.DeathRequested, Is.True);
            Assert.That(bridge.Collector.PendingEnemyDeathCount, Is.EqualTo(1));
        }
#endif

        [UnityTest]
        public IEnumerator DelayedDeathReceiptHidesFinishedBodyAndShadow_ThenDespawnsExactlyOnce()
        {
#if UNITY_EDITOR
            var agent = SpawnDeathFixture();
            yield return WaitFor(() => agent.ProductEnemyInitialized && agent.Authority.RunsNavigation, "death fixture ready");
            var enemy = agent.GetComponent<EnemyController>();
            var visuals = enemy.enemyAnimator.Renderers.ToArray();
            Assert.That(visuals.Length, Is.GreaterThanOrEqualTo(2), "Exercise the body and its shadow.");
            uint id = agent.netId;
            PredictFixtureDeath(agent);
            yield return WaitFor(() => enemy.DeathPresentationComplete, "local death animation completes before receipt");
            Assert.That(NetworkServer.spawned.ContainsKey(id), Is.True, "A visual must not delete a server-live network identity.");
            Assert.That(agent.IsCanonicalAlive, Is.True);
            Assert.That(visuals.All(r => r.forceRenderingOff || !r.enabled || !r.gameObject.activeInHierarchy), Is.True,
                "Finished local death must hide its shadow as well as its body while waiting for confirmation.");
            Assert.That(enemy.enemyAnimator.TryHurtBlinkAnimation(), Is.False, "Late hits cannot restart death flashes.");
            var bridge = Owner.GetComponent<MirrorNetworkCombatBridge>(); bridge.enabled = true; bridge.Flush();
            yield return WaitFor(() => !NetworkServer.spawned.ContainsKey(id) && !NetworkClient.spawned.ContainsKey(id), "confirmed network despawn");
            yield return null;
            Assert.That(visuals.All(r => r == null), Is.True, "The entire hierarchy, including shadow renderers, must be destroyed.");
            Assert.That(NetworkCombatWorld.Instance.Gateway.Metrics.ConfirmedKills, Is.EqualTo(1));
            Assert.That(bridge.Collector.PendingEnemyDeathCount, Is.Zero);
#endif
            yield break;
        }

        [UnityTest]
        public IEnumerator ConfirmedDeathCleansUpEvenWhenTheAnimationCoroutineIsInterrupted()
        {
#if UNITY_EDITOR
            var agent = SpawnDeathFixture();
            yield return WaitFor(() => agent.ProductEnemyInitialized && agent.Authority.RunsNavigation, "death fixture ready");
            uint id = agent.netId;
            var enemy = agent.GetComponent<EnemyController>();
            PredictFixtureDeath(agent);
            enemy.enemyAnimator.StopAllCoroutines();
            var bridge = Owner.GetComponent<MirrorNetworkCombatBridge>(); bridge.enabled = true; bridge.Flush();
            yield return WaitFor(() => !agent.IsCanonicalAlive, "canonical death");
            Assert.That(enemy.DeathPresentationComplete, Is.False, "Inject a missing presentation callback.");
            yield return new WaitForSecondsRealtime(enemy.enemyAnimator.DeadTime + 2.1f);
            Assert.That(NetworkServer.spawned.ContainsKey(id), Is.False, "Canonical death cannot retain a network object forever when presentation fails.");
            Assert.That(NetworkClient.spawned.ContainsKey(id), Is.False);
            Assert.That(NetworkCombatWorld.Instance.Gateway.Metrics.ConfirmedKills, Is.EqualTo(1));
#endif
            yield break;
        }

        [UnityTest]
        public IEnumerator RepositionUsesLatestSimulationPose_NotTheDelayedHostDisplayPose()
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Content/NetworkCombat/Limbo/Stage2/ReferenceBrotchi.prefab");
            var instance = Object.Instantiate(prefab, Owner.transform.position + Vector3.right * 8, Quaternion.identity);
            var agent = instance.GetComponent<NetworkEnemySimulationAgent>();
            agent.ConfigureBirth(new EnemyBirthParameters { Enabled = true, SourceEnemy = "Brotchi", ClipIndex = 0,
                BornAt = -100, Health = 30, Damage = 1, Speed = 1, SpeedMultiplier = 1, Knockback = 1, Wind = 1, Counted = true });
            agent.ConfigureInitialServerTarget(Owner.netId); NetworkServer.Spawn(instance);
            yield return WaitFor(() => agent.ProductEnemyInitialized && agent.Authority.RunsNavigation, "reference reposition fixture");
            agent.enabled = false;
            agent.Authority.ApplyRole(EnemySimulationRole.Replica, 900, Owner.netId, agent.Assignment.Epoch);
            var spawner = Object.FindFirstObjectByType<NetworkGameplayEnemySpawner>();
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(string field, object value) => typeof(NetworkGameplayEnemySpawner).GetField(field, fields).SetValue(spawner, value);
            var reference = new ReferenceWaveProgram(new[] { new ReferenceSpawnDefinition { SourceEnemy = "Brotchi", PrefabIndex = 0 } },
                System.Array.Empty<ReferenceBarrierDefinition>(), 1000, 1000, AnimationCurve.Linear(0, 0, 1, 1),
                1, 1, 2, 2, 30, 5, 20, 6, 1, 1, 1, true);
            var parameters = new WaveParameters(reference, new[] { prefab }, 100, 10);
            Set("world", World); Set("settings", parameters); Set("referenceRandom", new System.Random(1));
            Set("schedule", new ServerWaveSchedule(manager.Session.RunId, parameters, NetworkTime.time));
            Assert.That(manager.Session.TryGetConnection(Owner.connectionToClient.connectionId, out var member), Is.True);
            var participants = (List<RunParticipant>)typeof(NetworkGameplayEnemySpawner).GetField("activeParticipants", fields).GetValue(spawner);
            participants.Clear(); participants.Add(member);
            var view = (Bounds)typeof(NetworkGameplayEnemySpawner).GetMethod("ReferenceView", fields).Invoke(spawner, new object[] { member });
            World.Registry.TryGetLatestSnapshot(agent.netId, out var pose);
            pose.Position = view.center; pose.SampleNetworkTime = NetworkTime.time;
            World.Registry.RecordCheckpoint(new EnemySimulationCheckpoint { Movement = pose });
            agent.GetComponent<EnemySnapshotInterpolator>().ResetRenderPose((Vector2)view.center + Vector2.right * 1000);
            typeof(NetworkGameplayEnemySpawner).GetMethod("UpdateReferenceReposition", fields).Invoke(spawner, null);
            var outside = (Dictionary<uint, double>)typeof(NetworkGameplayEnemySpawner).GetField("offscreenSince", fields).GetValue(spawner);
            Assert.That(outside.ContainsKey(agent.netId), Is.False,
                "A visible simulated enemy must not enter offscreen recycling because its host display is delayed.");
#endif
            yield break;
        }

        [UnityTest]
        public IEnumerator ReplicaPositionAdvancesBetweenPhysicsTicks()
        {
#if UNITY_EDITOR
            var agent = SpawnDeathFixture();
            yield return WaitFor(() => agent.ProductEnemyInitialized && agent.Authority.RunsNavigation, "replica fixture ready");
            agent.enabled = false; // Keep the deliberately injected replica role for this rendering fixture.
            var interpolation = agent.GetComponent<EnemySnapshotInterpolator>();
            var body = agent.GetComponent<Rigidbody2D>();
            agent.Authority.ApplyRole(EnemySimulationRole.Replica, 900, Owner.netId, agent.Assignment.Epoch);
            Vector2 origin = body.position;
            double now = EnemySimulationClock.Now;
            Assert.That(interpolation.Push(new EnemySimulationSnapshot { EnemyEntityId = agent.netId,
                AssignmentEpoch = agent.Assignment.Epoch, Sequence = 500,
                SampleNetworkTime = now - .2, Position = origin, Velocity = Vector2.right * 10 }), Is.True);
            Assert.That(interpolation.Push(new EnemySimulationSnapshot { EnemyEntityId = agent.netId,
                AssignmentEpoch = agent.Assignment.Epoch, Sequence = 501,
                SampleNetworkTime = now + .8, Position = origin + Vector2.right * 10, Velocity = Vector2.right * 10 }), Is.True);
            float previousStep = Time.fixedDeltaTime;
            Time.fixedDeltaTime = 10; // No physics step during the following rendering frames.
            try
            {
                yield return null;
                float first = agent.transform.position.x;
                yield return new WaitForSecondsRealtime(.05f);
                Assert.That(agent.transform.position.x, Is.GreaterThan(first + .1f),
                    "Remote interpolation must advance at rendered-frame cadence, not only FixedUpdate.");
                Assert.That(body.interpolation, Is.EqualTo(RigidbodyInterpolation2D.None), "Do not double-interpolate remote snapshots.");
            }
            finally { Time.fixedDeltaTime = previousStep; }
#endif
            yield break;
        }
    }
}
