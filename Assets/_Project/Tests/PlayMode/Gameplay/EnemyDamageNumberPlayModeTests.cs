using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using DamageNumbersPro;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;
using LegacyDamageType = AstralShift.HellMaiden.Player.Attacks.DamageType;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class EnemyDamageNumberPlayModeTests
    {
        private const string Boot = "Assets/_Project/Scenes/Boot.unity";
        private BootGameplayNetworkManager manager;
        private GameObject[] roots;
        private GameObject gate;
        private readonly List<EnemyHitPresentation> presented = new List<EnemyHitPresentation>();

        [UnityTest]
        public IEnumerator ProductEnemiesShowResolvedNumbersIndependentlyOfFlashAndSurviveLethalDespawn()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Boot, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(Boot, LoadSceneMode.Single);
#endif
            roots = BootSceneFixtureObjects.Capture(Boot);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            manager.ConfigurePreparationFlow(false);
            gate = new GameObject("Damage number attack gate");
            gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            SceneManager.sceneLoaded += Prepare;
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7987, false, out var error), Is.True, error);
            manager.StartHost();
            yield return Wait(() => manager.IsGameplayLoaded && NetworkClient.localPlayer != null);
            manager.BeginRun();
            var world = NetworkCombatWorld.Instance;
            world.EnemyDamageNumberPresented += presented.Add;
            var pool = PoolManager.Instance;
            Assert.That(pool, Is.Not.Null);
#if UNITY_EDITOR
            foreach (string prefabName in new[] { "NetworkEnemyBase", "NetworkEnemySkeleton" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Content/NetworkCombat/{prefabName}.prefab");
                var root = Object.Instantiate(prefab, new Vector3(20, 20), Quaternion.identity);
                var agent = root.GetComponent<NetworkEnemySimulationAgent>();
                agent.ConfigureRuntimeMinimumHealthOverride(10000);
                agent.ConfigureInitialServerTarget(NetworkClient.localPlayer.netId);
                NetworkServer.Spawn(root);
                yield return Wait(() => agent.ProductEnemyInitialized);
                var enemy = root.GetComponent<EnemyController>();
                Assert.That(enemy.StateMachine == null, Is.EqualTo(agent.ProductMovementOnly));
                enemy.enemyAnimator.enabled = false;
                int start = presented.Count;
                ulong eventId = (ulong)(10000 + start * 100);
                uint target = agent.netId;
                world.PresentPredictedEnemyHit(Damage(eventId, target, 50, 8));
                Assert.That(presented.Count, Is.EqualTo(start + 1), "Disabled flash cannot suppress numbers.");
                Assert.That(presented.Last().Damage, Is.EqualTo(50));
                var popup = Popups().Single(n => n.gameObject.activeSelf);
                Assert.That(popup.number, Is.EqualTo(50));
                Assert.That(popup.transform.IsChildOf(root.transform), Is.False);
                Assert.That(popup.followedTarget, Is.EqualTo(enemy.damagePosition));
                Confirm(world, Hit(eventId, target, 2));
                Confirm(world, Hit(eventId, target, 2));
                Assert.That(presented.Count, Is.EqualTo(start + 1));
                pool.NetworkDamageNumbersEnabled = false;
                world.PresentPredictedEnemyHit(Damage(eventId + 1, target, 50, 8));
                pool.NetworkDamageNumbersEnabled = true;
                Confirm(world, Hit(eventId + 1, target, 3));
                Assert.That(presented.Count, Is.EqualTo(start + 2), "Unplayed prediction can recover on confirmation.");
                Confirm(world, Hit(eventId + 2, target, 4), Hit(eventId + 3, target, 5));
                Assert.That(presented.Count, Is.EqualTo(start + 4));
                Confirm(world, Hit(eventId + 4, target, 4));
                Assert.That(presented.Count, Is.EqualTo(start + 4));
                world.PresentPredictedEnemyHit(Damage(eventId + 5, target, 50, 0));
                Assert.That(presented.Count, Is.EqualTo(start + 4));
                Vector3 position = enemy.damagePosition.position;
                // Exercise the real Broadcast -> server death -> queued Host receipt ordering.
                world.Gateway.Ledger.TryGetState(target, out var lowHealth);
                lowHealth.Health = 8; lowHealth.StateVersion = 7;
                world.Gateway.Ledger.RestoreEntityState(target, new ServerEntityCheckpoint(lowHealth, false));
                world.Gateway.Ledger.RegisterSource(900009, 900009);
                var death = world.Gateway.ProcessBatch(900009, new CombatSubmissionBatch
                {
                    BatchSequence = (uint)(start + 1), Results = new[] { new CombatResult
                    {
                        EventId = eventId + 50, RootEventId = eventId + 50, Sequence = (uint)(eventId + 50),
                        SourcePlayerId = 900009, SourceEntityId = 900009, TargetEntityId = target,
                        Damage = 50, DamageSourceId = 6, DamageTags = (ulong)CombatTags.Damage
                    } }
                }, NetworkTime.time);
                Assert.That(death.Entities.Single().Health, Is.Zero);
                typeof(NetworkCombatWorld).GetMethod("Broadcast", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(world, new object[] { death });
                Assert.That(NetworkServer.spawned.ContainsKey(target), Is.False, "Death must immediately despawn the enemy.");
                yield return Wait(() => presented.Count == start + 5);
                Assert.That(presented.Last().Damage, Is.EqualTo(50));
                Assert.That(presented.Last().Position, Is.EqualTo(position));
                yield return null;
                var echo = Hit(eventId, target, 10); echo.HasPosition = true; echo.Position = position;
                Confirm(world, echo);
                Assert.That(presented.Count, Is.EqualTo(start + 5), "Despawn must retain predicted event receipt.");
                var lethal = Hit(eventId + 6, target, 11); lethal.HasPosition = true; lethal.Position = position;
                Confirm(world, lethal);
                Assert.That(presented.Count, Is.EqualTo(start + 6), "Remote lethal number must survive absent target.");
                Assert.That(Popups().Any(n => n.gameObject.activeSelf), Is.True);
                pool.ClearNetworkDamageNumbers();
                yield return null;
                Assert.That(Popups(), Is.Empty);
            }
#endif
            int beforeStop = presented.Count;
            world.OnStopClient();
            world.OnStartClient();
            var reused = Hit(10000, 100, 2); reused.HasPosition = true;
            Confirm(world, reused);
            Assert.That(presented.Count, Is.EqualTo(beforeStop + 1), "New world clears old event IDs.");
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator OriginalPrefabsRenderCombineByIdentityAndReusePools()
        {
            var palette = AssetDatabase.LoadAssetAtPath<DamageColorsSO>("Assets/_Project/Content/DamageNumbers/DamageColorsSO.asset");
            gate = new GameObject("Damage number resource fixture");
            var pool = gate.AddComponent<PoolManager>(); pool.Init();
            typeof(PoolManager).GetField("damageColors", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pool, palette);
            var camera = gate.AddComponent<Camera>(); camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -10); camera.tag = "MainCamera";
            var styles = new[] { palette.normalDamage.normal, palette.normalDamage.critical, palette.fireDamage.normal,
                palette.poisonDamage.normal, palette.bleedDamage.normal, palette.lightningDamage.normal };
            foreach (var style in styles)
            {
                Assert.That(style, Is.TypeOf<DamageNumberMesh>());
                Assert.That(style.enableCombination && style.enablePooling, Is.True);
                foreach (var component in style.GetComponentsInChildren<MonoBehaviour>(true)) Assert.That(component, Is.Not.Null);
                var text = style.GetComponentInChildren<TMP_Text>(true);
                Assert.That(text.font, Is.Not.Null);
                Assert.That(text.fontSharedMaterial, Is.Not.Null);
            }
            Assert.That(pool.TrySpawnNetworkDamageNumber(Vector3.zero, null, 50, LegacyDamageType.Normal, false, 1, 6, 10), Is.True);
            yield return null;
            Assert.That(pool.TrySpawnNetworkDamageNumber(Vector3.zero, null, 12, LegacyDamageType.Normal, false, 1, 6, 10), Is.True);
            yield return new WaitForSeconds(.2f);
            Assert.That(Popups().Any(n => n.gameObject.activeSelf && n.number == 62), Is.True, "Original DNP combination sums exact damage.");
            Assert.That(pool.TrySpawnNetworkDamageNumber(Vector3.zero, null, 10, LegacyDamageType.Normal, false, 2, 6, 10), Is.True);
            Assert.That(pool.TrySpawnNetworkDamageNumber(Vector3.zero, null, 11, LegacyDamageType.Fire, false, 1, 6, 10), Is.True);
            Assert.That(pool.TrySpawnNetworkDamageNumber(Vector3.zero, null, 12, LegacyDamageType.Normal, true, 1, 6, 10), Is.True);
            Assert.That(pool.TrySpawnNetworkDamageNumber(Vector3.zero, null, 13, LegacyDamageType.Normal, false, 1, 6, 11), Is.True);
            yield return new WaitForSeconds(.1f);
            Assert.That(Popups().Where(n => n.gameObject.activeSelf).Select(n => n.spamGroup).Distinct().Count(), Is.EqualTo(5));
            var used = new HashSet<int>(Popups().Select(n => n.GetInstanceID()));
            yield return new WaitForSeconds(2);
            pool.TrySpawnNetworkDamageNumber(Vector3.zero, null, 20, LegacyDamageType.Normal, false, 1, 6, 10);
            Assert.That(Popups().Where(n => n.gameObject.activeSelf).Any(n => used.Contains(n.GetInstanceID())), Is.True);
            pool.ClearNetworkDamageNumbers();
            yield return null;
            Assert.That(Popups(), Is.Empty);
        }
#endif
        private static DamageNumber[] Popups() => Object.FindObjectsByType<DamageNumber>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        private static CombatEvent Damage(ulong id, uint target, int resolved, int applied) => new CombatEvent(
            CombatEventKind.DamageResolved,
            new CombatContext(new CombatEventId(id), new CombatEventId(id), default, (uint)id, 0, 1, 1, target, 6, 0, CombatTags.Damage, 1),
            new DamageInfo(6, resolved, false), new DamageInfo(6, applied, false));
        private static EnemyHitPresentation Hit(ulong id, uint target, uint version) => new EnemyHitPresentation
        { DamageEventId = id, TargetEntityId = target, TargetStateVersion = version, Damage = 50, SourcePlayerId = 1, DamageSourceId = 6 };
        private static void Confirm(NetworkCombatWorld world, params EnemyHitPresentation[] hits) =>
            typeof(NetworkCombatWorld).GetMethod("ApplyCanonical", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(world, new object[] { new CanonicalWorldBatch { EnemyHitPresentations = hits } });
        private static IEnumerator Wait(Func<bool> condition)
        {
            float end = Time.realtimeSinceStartup + 25;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(condition(), Is.True, "Gameplay initialization/shutdown timed out.");
        }
        private void Prepare(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (var spawner in Object.FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None))
            { spawner.Configure(spawner.EnemyPrefab, 5); spawner.enabled = false; }
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= Prepare;
            if (NetworkCombatWorld.Instance != null) NetworkCombatWorld.Instance.EnemyDamageNumberPresented -= presented.Add;
            if (manager != null) { manager.StopHost(); yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning); }
            Object.Destroy(gate);
            BootSceneFixtureObjects.Destroy(roots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
