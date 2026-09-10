using System;
using System.Collections;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
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
    public sealed class OrdinaryHitKnockbackPlayModeTests
    {
        private const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private GameObject enemyPrefab, gate, fixtureAssets;

        [UnityTest]
        public IEnumerator SingleCirclingOrbRepeatedPhysicsHitsMoveTheRealProductEnemyUnderOneRoot()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BootPath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(BootPath, LoadSceneMode.Single);
#endif
            bootRoots = BootSceneFixtureObjects.Capture(BootPath);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            var database = Object.FindFirstObjectByType<RuntimeDB>();
            fixtureAssets = new GameObject("Ordinary knockback test asset copies");
            fixtureAssets.SetActive(false);
            var weapons = Object.Instantiate(database.WeaponDB);
            weapons.Configure(weapons.Weapons.Select(definition =>
            {
                if (definition.ID != 6) return definition;
                var copy = Object.Instantiate(definition);
                var weaponCopy = Object.Instantiate((CirclingAttackBehaviour)definition.WeaponPrefab, fixtureAssets.transform);
                weaponCopy.attackPrefab = Object.Instantiate(weaponCopy.attackPrefab, fixtureAssets.transform);
                // Exported FMOD banks are unavailable. Silence only this fixture's copied sounds.
                foreach (string name in new[] { "startSound", "loopSound", "endSound", "hitSound" })
                {
                    var field = typeof(AnimatedAttack).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    field.SetValue(weaponCopy.attackPrefab, Activator.CreateInstance(field.FieldType));
                }
                foreach (var component in weaponCopy.attackPrefab.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component != null && component.GetType().Namespace == "FMODUnity") Object.DestroyImmediate(component);
                copy.WeaponPrefab = weaponCopy;
                var stats = copy.BaseStats;
                stats.projectileCount = 1;
                stats.critRate = 0;
                copy.ConfigureNativeGas(stats, copy.AttackTags, copy.Presentation);
                return copy;
            }).ToArray());
            database.ConfigureWeaponDatabase(weapons);
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7961, false, out string error), Is.True, error);
            gate = new GameObject("Ordinary knockback fixture attack timing");
            gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            SceneManager.sceneLoaded += PrepareGameplay;
            manager.StartHost();
            yield return WaitFor(() => manager.IsGameplayLoaded && NetworkClient.localPlayer != null &&
                NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline, "Owner baseline");
            manager.BeginRun();
            var owner = NetworkClient.localPlayer;
            var player = owner.GetComponent<PlayerMovement>();
            var build = owner.GetComponent<PlayerBuildRuntime>();
            var circling = build.InitialWeapon as CirclingAttackBehaviour;
            Assert.That(circling, Is.Not.Null);
            Assert.That(circling.ProjectileCountValue, Is.EqualTo(1));
            circling.baseSpeed = 0; // Keep the single real orb in contact; do not replace its hitbox/GAS path.
            var instance = Object.Instantiate(enemyPrefab, new Vector3(12, 10, 0), Quaternion.identity);
            SceneManager.MoveGameObjectToScene(instance, player.gameObject.scene);
            var agent = instance.GetComponent<NetworkEnemySimulationAgent>();
            agent.ConfigureRuntimeMinimumHealthOverride(10000);
            agent.ConfigureInitialServerTarget(owner.netId);
            NetworkServer.Spawn(instance);
            yield return WaitFor(() => agent.ProductEnemyInitialized && agent.Authority.RunsNavigation, "Assigned product enemy");
            var enemy = instance.GetComponent<EnemyController>();
            enemy.Movement.StopMovement();
            Assert.That(enemy.StateMachine, Is.Null);
            var body = instance.GetComponent<Rigidbody2D>();
            body.gravityScale = 0; // Isolate hit displacement from the prefab's unrelated gravity while navigation is locked.
            body.linearVelocity = Vector2.zero;
            Vector2 before = body.position;
            var bridge = owner.GetComponent<MirrorNetworkCombatBridge>();
            bridge.Trace.Clear();
            int initialHealth = enemy.CurrentHealth;
            int immediateHits = 0;
            enemy.NativeHitKnockbackRequested += (hit, result) =>
            {
                Assert.That(agent.AppliedOrdinaryKnockbackCount, Is.EqualTo(++immediateHits), "The local simulator must consume the hit in the same call, before any Mirror flush.");
                Assert.That(enemy.IsNetworkKnockbackActive, Is.True);
            };
            float duration = circling.GetAttackSequenceDuration();
            KeepOrbInContact();
            circling.Attack();
            float until = Time.time + duration + .1f;
            while (Time.time < until)
            {
                KeepOrbInContact();
                yield return new WaitForFixedUpdate();
            }
            yield return new WaitForSeconds(.3f);
            var hits = bridge.Trace.Snapshot().Where(e => e.Kind == CombatTraceKind.DamageResolved && e.TargetEntityId == agent.netId).ToArray();
            var gateway = NetworkCombatWorld.Instance.Gateway;
            Assert.That(gateway.Ledger.TryGetState(agent.netId, out var canonical), Is.True);
            Debug.Log($"[M3OrdinaryBaseline] hits={hits.Length} events={string.Join(",", hits.Select(e => e.EventId.Value))} roots={hits.Select(e => e.RootEventId).Distinct().Count()} localHP={enemy.CurrentHealth} canonicalHP={canonical.Health} displacement={Vector2.Distance(before, body.position):F5} oldFSM={enemy.StateMachine != null} rejectedRoot={gateway.Metrics.GetRejected(CombatRejectionReason.InvalidAttackRoot)} rejectedRate={gateway.Metrics.GetRejected(CombatRejectionReason.InvalidAttackRate)}");
            Assert.That(hits.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(hits.Select(e => e.EventId).Distinct().Count(), Is.EqualTo(hits.Length));
            Assert.That(hits.Select(e => e.RootEventId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(canonical.Health, Is.EqualTo(initialHealth - hits.Sum(e => e.Damage)));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(canonical.Health));
            Assert.That(agent.RequestedOrdinaryKnockbackCount, Is.EqualTo(hits.Length));
            Assert.That(agent.AppliedOrdinaryKnockbackCount, Is.EqualTo(hits.Length), "Server echoes must not add a second impulse.");
            Assert.That(NetworkEnemySimulationWorld.Instance.RoutedOrdinaryKnockbackCount, Is.EqualTo(hits.Length));
            Assert.That(Vector2.Distance(before, body.position), Is.GreaterThan(.1f), "Ordinary hits must leave the old-FSM early return and move only the assigned simulator.");
            Assert.That(enemy.StateMachine, Is.Null);
            yield return VerifyCommandReplayAndHandoff(agent, circling, bridge, hits[0].RootEventId.Value);

            void KeepOrbInContact()
            {
                // Follow the displacement so overtime hits genuinely keep touching the same enemy.
                Vector2 position = enemy.hurtBox.GetPosition() - Vector2.right * (circling.baseRadius * circling.SizeValue + .15f);
                player.SetDirection(Vector2.zero);
                player.body.position = position;
                player.transform.position = position;
                Physics2D.SyncTransforms();
            }
        }

        private static IEnumerator VerifyCommandReplayAndHandoff(NetworkEnemySimulationAgent agent, CirclingAttackBehaviour weapon,
            MirrorNetworkCombatBridge bridge, ulong root)
        {
            var controller = agent.GetComponent<EnemyController>();
            var body = agent.GetComponent<Rigidbody2D>();
            var world = NetworkEnemySimulationWorld.Instance;
            var apply = typeof(NetworkEnemySimulationAgent).GetMethod("TryApplyKnockback",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var preset = EnemyKnockbackSettings.From(weapon.KnockbackSettings);
            int hp = controller.CurrentHealth;
            ulong serial = 1000;
            EnemyKnockbackCommand Command() => new EnemyKnockbackCommand
            {
                Kind = EnemyKnockbackKind.OrdinaryHit, EnemyEntityId = agent.netId, SourcePlayerId = bridge.OwnerPlayerId,
                AbilityCombatId = 6, RootEventId = root, DamageEventId = bridge.EventIds.Next().Value,
                AssignmentEpoch = agent.Assignment.Epoch, CommandId = ++serial, IssuedAt = NetworkTime.time,
                Origin = body.position + Vector2.left, Settings = preset
            };
            bool Apply(EnemyKnockbackCommand command, bool server = false) => (bool)apply.Invoke(agent,
                new object[] { command, server ? 0u : bridge.OwnerPlayerId, server });

            var first = Command();
            Assert.That(Apply(first), Is.True);
            Assert.That(Apply(first), Is.False, "Duplicate reliable command.");
            var overlap = Command();
            Assert.That(Apply(overlap), Is.False, "Source rule ignores overlapping impulses.");
            controller.CancelNetworkKnockback();
            overlap.CommandId = ++serial;
            Assert.That(Apply(overlap), Is.False, "Ignored hit cannot replay under a different command ID after recovery.");
            var immune = Command();
            float resistance = controller.stats.KnockBackMultiplier;
            controller.stats.KnockBackMultiplier = 0;
            Assert.That(Apply(immune), Is.False);
            controller.stats.KnockBackMultiplier = resistance;
            immune.CommandId = ++serial;
            Assert.That(Apply(immune), Is.False, "An ignored immune hit is consumed as well.");
            var inFlight = Command();
            Assert.That(Apply(inFlight), Is.True);
            yield return new WaitForFixedUpdate();
            world.Registry.TryGetLatestSnapshot(agent.netId, out var last);
            agent.SetServerAssignment(world.Registry.AssignServerFallback(agent.netId, bridge.OwnerPlayerId));
            Assert.That(agent.HasActiveNetworkKnockback, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            inFlight.CommandId = ++serial;
            Assert.That(Apply(inFlight, true), Is.False, "Old epoch cannot be replayed by the new simulator.");
            var takeover = Command();
            Assert.That(Apply(takeover, true), Is.True);
            agent.SetServerAssignment(world.Registry.Freeze(agent.netId));
            Assert.That(agent.HasActiveNetworkKnockback, Is.False);
            Assert.That(Apply(Command(), true), Is.False, "Frozen enemy does not move.");
            agent.SetServerAssignment(world.Registry.AssignServerAuthoritative(agent.netId, bridge.OwnerPlayerId));
            var expired = Command(); expired.IssuedAt -= 3;
            Assert.That(Apply(expired, true), Is.False);
            Assert.That(Apply(Command(), true), Is.True);
            agent.enabled = false;
            Assert.That(agent.HasActiveNetworkKnockback, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(controller.CurrentHealth, Is.EqualTo(hp), "Command delivery itself must never apply damage.");
            Debug.Log("[M3Ordinary] duplicate/overlap/immune/old-epoch/fallback/frozen/server-authoritative/expiry/disable checks passed.");
        }

        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                { enemyPrefab = spawner.EnemyPrefab; spawner.enabled = false; }
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
                manager.StopHost();
                yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Gameplay unload");
            }
            Object.Destroy(gate);
            Object.Destroy(fixtureAssets);
            BootSceneFixtureObjects.Destroy(bootRoots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
