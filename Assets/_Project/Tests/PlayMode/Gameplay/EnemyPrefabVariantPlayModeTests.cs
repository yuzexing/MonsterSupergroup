using System;
using System.Collections;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
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
    public sealed partial class EnemyPrefabVariantPlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] roots;
        private GameObject gate;
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
            gate = new GameObject("Variant test weapon gate");
            gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            manager.ConfigurePreparationFlow(false);
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7986, false, out var error), Is.True, error);
            manager.StartHost();
            yield return WaitFor(() => Owner != null && manager.IsGameplayLoaded && NetworkEnemySimulationWorld.Instance.HasEligiblePlayer);
            foreach (var spawner in Object.FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None)) spawner.enabled = false;
        }

        private EnemyController Spawn(string name, bool? movementOnly = null)
        {
            var container = new GameObject("inactive spawn configuration");
            container.SetActive(false);
            var root = Object.Instantiate(manager.spawnPrefabs.First(p => p.name == name), container.transform);
            root.transform.position = Owner.transform.position + new Vector3(15, 10, 0);
            var agent = root.GetComponent<NetworkEnemySimulationAgent>();
            if (movementOnly.HasValue) agent.ConfigureProductSimulation(movementOnly.Value);
            root.transform.SetParent(null);
            Object.Destroy(container);
            agent.ConfigureInitialServerTarget(Owner.netId);
            NetworkServer.Spawn(root);
            return root.GetComponent<EnemyController>();
        }

        [UnityTest]
        public IEnumerator ContactOnlyAndEmptyActiveSlotKeepLifecycleAndDamageReception()
        {
            var contactOnly = Spawn("NetworkEnemyBase");
            var fullWithoutAttack = Spawn("NetworkEnemyBase", false);
            yield return WaitFor(() => Ready(contactOnly) && Ready(fullWithoutAttack));
            Assert.That(contactOnly.StateMachine, Is.Null);
            Assert.That(fullWithoutAttack.StateMachine, Is.Not.Null);
            Assert.That(contactOnly.GetComponent<EnemyContactDamage>().IsActive, Is.True);
            fullWithoutAttack.GetComponent<EnemyContactDamage>().SetContactEnabled(false);
            Assert.That(fullWithoutAttack.GetComponent<EnemyContactDamage>().IsActive, Is.False);
            Assert.That(fullWithoutAttack.hurtBox.GetComponent<Collider2D>().enabled, Is.True);
            fullWithoutAttack.Attack();
            int hp = fullWithoutAttack.CurrentHealth;
            fullWithoutAttack.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, 7, false));
            Assert.That(fullWithoutAttack.CurrentHealth, Is.EqualTo(hp - 7));
            Assert.That(contactOnly.CurrentHealth, Is.EqualTo(100));
            Assert.DoesNotThrow(() => fullWithoutAttack.Kill(instant: true, dropXp: false));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ContactToggleDiscardsPendingContactsAndCoexistsWithRealMelee()
        {
            var enemy = Spawn("NetworkEnemySkeleton");
            yield return WaitFor(() => Ready(enemy));
            var contact = enemy.GetComponent<EnemyContactDamage>();
            var player = Owner.GetComponent<PlayerMovement>();
            var playerBinding = Owner.GetComponent<PlayerCombatantBinding>();
            var hitbox = Owner.GetComponentInChildren<PlayerHitbox>();
            yield return WaitFor(() => !player.IsInvulnerable);
            int hp = playerBinding.CurrentHealth;
            Assert.That(contact.IsActive, Is.False);
            contact.SetContactEnabled(true);
            Assert.That(contact.IsActive, Is.True);
            contact.DamageInteraction.Interact(hitbox);
            contact.SetContactEnabled(false);
            yield return null; yield return null;
            Assert.That(playerBinding.CurrentHealth, Is.EqualTo(hp), "Disabling contact must discard deferred damage.");

            contact.SetContactEnabled(true);
            var extra = new GameObject("second hitbox for the same player");
            extra.transform.SetParent(Owner.transform, false);
            var duplicate = extra.AddComponent<PlayerHitbox>();
            duplicate.Configure(playerBinding);
            int changes = 0;
            Action<int, int> changed = (current, maximum) => { if (current < hp) changes++; };
            playerBinding.Combatant.HealthChanged += changed;
            contact.DamageInteraction.Interact(hitbox);
            contact.DamageInteraction.Interact(hitbox);
            contact.DamageInteraction.Interact(duplicate);
            yield return null; yield return null;
            playerBinding.Combatant.HealthChanged -= changed;
            Object.Destroy(extra);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(playerBinding.CurrentHealth, Is.LessThan(hp));
            int afterContact = playerBinding.CurrentHealth;
            // Independent attack mechanisms still share the player's existing invulnerability.
            contact.DamageInteraction.Interact(hitbox);
            yield return null; yield return null;
            Assert.That(playerBinding.CurrentHealth, Is.EqualTo(afterContact));
            yield return WaitFor(() => !player.IsInvulnerable);

            enemy.Attack();
            yield return WaitFor(() => enemy.CurrentAttackPresentationPhase == AstralShift.HellMaiden.AI.EnemyAttackPresentationPhase.Active);
            var attack = enemy.GetComponentsInChildren<EnemyAttackPrefab>(true).Single(a => a.damageInteraction != null && a.damageInteraction.gameObject.activeInHierarchy);
            Assert.That(contact.IsActive, Is.True, "Melee must not turn off the independent contact capability.");
            attack.damageInteraction.Interact(hitbox);
            yield return null; yield return null;
            Assert.That(playerBinding.CurrentHealth, Is.LessThan(afterContact));
            contact.SetContactEnabled(false);
            Assert.That(enemy.hurtBox.GetComponent<Collider2D>().enabled, Is.True);
            Assert.That(enemy.attackScript, Is.SameAs(enemy.GetComponent<EnemyAttackMelee>()));
            enemy.Kill(instant: true, dropXp: false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisableAndDeathDiscardPendingContactDamage()
        {
            var player = Owner.GetComponent<PlayerMovement>();
            var binding = Owner.GetComponent<PlayerCombatantBinding>();
            var hitbox = Owner.GetComponentInChildren<PlayerHitbox>();
            yield return WaitFor(() => !player.IsInvulnerable);
            foreach (bool kill in new[] { false, true })
            {
                var enemy = Spawn("NetworkEnemySkeleton");
                yield return WaitFor(() => Ready(enemy));
                var contact = enemy.GetComponent<EnemyContactDamage>();
                contact.SetContactEnabled(true);
                int hp = binding.CurrentHealth;
                contact.DamageInteraction.Interact(hitbox);
                if (kill) enemy.Kill(instant: true, dropXp: false);
                else enemy.gameObject.SetActive(false);
                yield return null; yield return null;
                Assert.That(binding.CurrentHealth, Is.EqualTo(hp), kill ? "Death flushed pending contact." : "Disable flushed pending contact.");
                if (enemy != null) NetworkServer.Destroy(enemy.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator SkeletonAndExampleHaveIndependentHealthTargetsCooldownsAndStatuses()
        {
            var a = Spawn("NetworkEnemySkeleton");
            var b = Spawn("NetworkEnemySkeletonExample");
            yield return WaitFor(() => Ready(a) && Ready(b));
            Assert.That(a.CurrentHealth, Is.EqualTo(2000));
            Assert.That(b.CurrentHealth, Is.EqualTo(2400));
            Assert.That(a.stats, Is.Not.SameAs(b.stats));
            Assert.That(a.StateMachine, Is.Not.SameAs(b.StateMachine));
            Assert.That(a.status.Runtime, Is.Not.SameAs(b.status.Runtime));
            a.lastAttackTime = Time.time;
            a.Target = null;
            a.status.Apply(EnemyStatusID.Slow, .5f, 2f);
            a.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, 7, false));
            Assert.That(a.CurrentHealth, Is.EqualTo(1993));
            Assert.That(b.CurrentHealth, Is.EqualTo(2400));
            Assert.That(b.Target, Is.Not.Null);
            Assert.That(b.lastAttackTime, Is.Not.EqualTo(a.lastAttackTime));
            Assert.That(b.status.HasAnyStatus(), Is.False);
            Assert.That(b.stats.SpeedMultiplier, Is.EqualTo(1));
            NetworkServer.Destroy(a.gameObject); NetworkServer.Destroy(b.gameObject);
            yield return null;
        }

        private static bool Ready(EnemyController enemy) => enemy.GetComponent<NetworkEnemySimulationAgent>().ProductEnemyInitialized;
        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, "Variant fixture timed out.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (manager != null && NetworkServer.active) manager.StopHost();
            if (manager != null) yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
            BootSceneFixtureObjects.Destroy(roots);
            if (gate != null) Object.Destroy(gate);
            yield return null;
        }
    }
}
