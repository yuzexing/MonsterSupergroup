using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat.Hand.Data;
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
    public sealed class WeaponAttackAdmissionPlayModeTests
    {
        private const string BootScenePath = "Assets/_Project/Scenes/Boot.unity";
        private const string GameplayScenePath = "Assets/_Project/Scenes/Gameplay.unity";
        private const uint TargetId = 0x7fff0001;
        private const int TargetHealth = 1000000;
        private readonly List<Object> fixtureObjects = new List<Object>();
        private readonly List<AttackSnapshot> snapshots = new List<AttackSnapshot>();
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;

        [UnityTest]
        public IEnumerator Host_NativeAttackCommandsValidateRootsAndFlushOutcomesBeforeRetirement()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                BootScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(BootScenePath, LoadSceneMode.Single);
#endif
            bootRoots = BootSceneFixtureObjects.Capture(BootScenePath);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp(
                "127.0.0.1", 7906, false, out string error), Is.True, error);

            // Exercise the existing weapon's native begin boundary without its audio or
            // automatic projectile Update. Definitions are cloned only in memory.
            var database = Object.FindFirstObjectByType<RuntimeDB>();
            Assert.That(database, Is.Not.Null);
            var weaponDatabase = Object.Instantiate(database.WeaponDB);
            fixtureObjects.Add(weaponDatabase);
            weaponDatabase.Configure(weaponDatabase.Weapons.Select(definition =>
            {
                var copy = Object.Instantiate(definition);
                fixtureObjects.Add(copy);
                var stats = copy.BaseStats;
                stats.speed = 1f / 30f;
                copy.ConfigureNativeGas(stats, copy.AttackTags, copy.Presentation);
                return copy;
            }).ToArray());
            database.ConfigureWeaponDatabase(weaponDatabase);
            CreateFixtureObject("Disable automatic weapon execution")
                .AddComponent<WeaponAttackAdmissionFixtureGate>();
            SceneManager.sceneLoaded += PrepareGameplay;
            manager.StartHost();

            yield return WaitUntil(() => manager.IsGameplayLoaded && NetworkClient.localPlayer != null &&
                NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive &&
                NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline,
                "Boot must create the owned Build and deliver its authoritative baseline.");

            var player = NetworkClient.localPlayer;
            var build = player.GetComponent<PlayerBuildRuntime>();
            var adapter = player.GetComponent<NetworkWeaponCombatAdapter>();
            var bridge = player.GetComponent<MirrorNetworkCombatBridge>();
            var selection = player.GetComponent<NetworkModifierSelection>();
            var weapon = build.InitialWeapon;
            var world = NetworkCombatWorld.Instance;
            build.SetWeaponExecutionEnabled(false);
            // Completion must perform the flush; the periodic bridge Update is disabled.
            bridge.enabled = false;
            Assert.That(adapter.AcceptedCooldownReportCount, Is.Zero,
                "The early fixture gate must prevent all automatic attacks before the test.");
            Assert.That(world.Gateway.Attacks.RequiresAdmission(player.netId), Is.True);
            Assert.That(selection.OwnerBuildRevision, Is.EqualTo(selection.BuildRevision));
            Assert.That(bridge.Collector.PendingResultCount, Is.Zero);

            var target = CreateFixtureObject("Canonical admission target").AddComponent<CombatantBehaviour>();
            target.Initialize(TargetHealth);
            target.ConfigureEntityId(TargetId);
            world.Gateway.Ledger.RegisterEntity(TargetId, TargetHealth,
                CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);

            // Invoke the woven Command wrapper, not its generated server body. Mirror must
            // supply the authenticated sender and preserve reliable command ordering.
            CombatEventId wrongWeapon = bridge.EventIds.Next();
            SendAttackCommand(adapter, 0, uint.MaxValue, wrongWeapon,
                selection.OwnerBuildRevision);
            yield return WaitUntil(() => adapter.RejectedAttackCount == 1,
                "A root for a weapon outside the server Build must be rejected.");
            Assert.That(adapter.LastAttackRejection, Is.EqualTo(CombatRejectionReason.SourceNotOwned));
            Assert.That(world.Gateway.Attacks.ActiveCount(player.netId), Is.Zero);

            CombatEventId staleBuild = bridge.EventIds.Next();
            SendAttackCommand(adapter, 0, weapon.ID, staleBuild,
                selection.OwnerBuildRevision + 1);
            yield return WaitUntil(() => adapter.RejectedAttackCount == 2,
                "A root claiming a different Build revision must be rejected.");
            Assert.That(adapter.LastAttackRejection, Is.EqualTo(CombatRejectionReason.StaleAttackBuild));
            Assert.That(world.Gateway.Attacks.ActiveCount(player.netId), Is.Zero);

            AttackSnapshot attack = BeginNativeAttack(weapon);
            yield return WaitUntil(() => adapter.AcceptedCooldownReportCount == 1,
                "The native begin event must reach the server admission Command.");
            Assert.That(world.Gateway.Attacks.Contains(player.netId,
                attack.Context.EventId.Value, weapon.ID), Is.True);
            Assert.That(world.Gateway.Attacks.ActiveCount(player.netId), Is.EqualTo(1));

            AttackSnapshot rapidAttack = BeginNativeAttack(weapon);
            yield return WaitUntil(() => adapter.RejectedAttackCount == 3,
                "A fresh event identity must not bypass the existing weapon's cooldown.");
            Assert.That(adapter.LastAttackRejection, Is.EqualTo(CombatRejectionReason.InvalidAttackRate));
            Assert.That(world.Gateway.Attacks.Contains(player.netId,
                rapidAttack.Context.EventId.Value, weapon.ID), Is.False);
            rapidAttack.Dispose();
            yield return null;
            yield return null;
            Assert.That(world.Gateway.Attacks.ActiveCount(player.netId), Is.EqualTo(1),
                "Completing a rejected root must not release the admitted attack.");

            long acceptedBefore = world.Gateway.Metrics.AcceptedCombatResults;
            CombatResolution first = weapon.NativeRuntime.ResolveHitDetailed(attack, target);
            CombatResolution second = weapon.NativeRuntime.ResolveHitDetailed(attack, target);
            Assert.That(first.DamageContext.RootEventId, Is.EqualTo(attack.Context.EventId));
            Assert.That(second.DamageContext.RootEventId, Is.EqualTo(attack.Context.EventId));
            Assert.That(first.DamageContext.EventId, Is.Not.EqualTo(second.DamageContext.EventId));
            Assert.That(first.ResolvedDamage.Value, Is.GreaterThan(0));
            Assert.That(second.ResolvedDamage.Value, Is.GreaterThan(0));
            Assert.That(bridge.Collector.PendingResultCount, Is.EqualTo(2));
            Assert.That(world.Gateway.Metrics.AcceptedCombatResults, Is.EqualTo(acceptedBefore));
            AssertCanonicalHealth(world, TargetHealth);

            // More than one collector batch must drain before the root is retired. This checks
            // transport batching for area/multi-hit outcomes, not one projectile's hit budget.
            const int outcomeCount = 258;
            int expectedHealth = TargetHealth - first.ResolvedDamage.Value - second.ResolvedDamage.Value;
            for (int i = 2; i < outcomeCount; i++)
                expectedHealth -= weapon.NativeRuntime.ResolveHitDetailed(attack, target).ResolvedDamage.Value;
            Assert.That(bridge.Collector.PendingResultCount, Is.EqualTo(outcomeCount));
            attack.Dispose();
            yield return WaitUntil(() => world.Gateway.Attacks.ActiveCount(player.netId) == 0 &&
                world.Gateway.Metrics.AcceptedCombatResults == acceptedBefore + outcomeCount,
                "Snapshot completion must flush all result batches before retiring their root.");
            AssertCanonicalHealth(world, expectedHealth);
            Assert.That(bridge.Collector.PendingResultCount, Is.Zero);
            Assert.That(world.Gateway.Attacks.Contains(player.netId,
                attack.Context.EventId.Value, weapon.ID), Is.False);

            long invalidRootsBefore = world.Gateway.Metrics.GetRejected(CombatRejectionReason.InvalidAttackRoot);
            using (AttackSnapshot bypass = weapon.NativeRuntime.BeginAttack())
            {
                Assert.That(world.Gateway.Attacks.Contains(player.netId,
                    bypass.Context.EventId.Value, weapon.ID), Is.False);
                weapon.NativeRuntime.ResolveHitDetailed(bypass, target);
            }
            Assert.That(bridge.Collector.PendingResultCount, Is.EqualTo(1));
            bridge.Flush();
            yield return WaitUntil(() => world.Gateway.Metrics.GetRejected(
                    CombatRejectionReason.InvalidAttackRoot) == invalidRootsBefore + 1,
                "Calling GAS BeginAttack directly must not bypass native root admission.");
            AssertCanonicalHealth(world, expectedHealth);
            Assert.That(world.Gateway.Metrics.AcceptedCombatResults, Is.EqualTo(acceptedBefore + outcomeCount));
            Assert.That(adapter.AcceptedCooldownReportCount, Is.EqualTo(1));

            uint playerId = player.netId;
            manager.StopHost();
            yield return WaitUntil(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning,
                "The real Boot stop path must complete Gameplay cleanup.");
            Assert.That(world.Gateway.Attacks.RequiresAdmission(playerId), Is.False,
                "Disconnect must remove the avatar's admission registry entry.");
        }

        private AttackSnapshot BeginNativeAttack(WeaponBehaviour weapon)
        {
            var begin = typeof(WeaponBehaviour).GetMethod("BeginNativeGasAttack",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(begin, Is.Not.Null);
            var attack = (AttackSnapshot)begin.Invoke(weapon, null);
            snapshots.Add(attack);
            return attack;
        }

        private static void SendAttackCommand(NetworkWeaponCombatAdapter adapter, int slot,
            uint weaponId, CombatEventId eventId, uint revision)
        {
            var command = typeof(NetworkWeaponCombatAdapter).GetMethod("CmdObserveWeaponAttack",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(command, Is.Not.Null);
            command.Invoke(adapter, new object[] { slot, weaponId, eventId.Value,
                NetworkTime.time, revision, null });
        }

        private static void AssertCanonicalHealth(NetworkCombatWorld world, int expected)
        {
            Assert.That(world.Gateway.Ledger.TryGetState(TargetId, out var state), Is.True);
            Assert.That(state.Health, Is.EqualTo(expected));
        }

        private static IEnumerator WaitUntil(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private GameObject CreateFixtureObject(string name)
        {
            var result = new GameObject(name);
            fixtureObjects.Add(result);
            return result;
        }

        private static void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != GameplayScenePath) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                    spawner.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= PrepareGameplay;
            foreach (AttackSnapshot snapshot in snapshots) snapshot.Dispose();
            snapshots.Clear();
            if (manager != null)
            {
                if (NetworkServer.active && NetworkClient.active) manager.StopHost();
                else if (NetworkServer.active) manager.StopServer();
                else if (NetworkClient.active) manager.StopClient();
                yield return WaitUntil(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning,
                    "Gameplay cleanup must finish before another Boot fixture starts.");
            }
            BootSceneFixtureObjects.Destroy(bootRoots);
            for (int i = fixtureObjects.Count - 1; i >= 0; i--)
                if (fixtureObjects[i] != null) Object.Destroy(fixtureObjects[i]);
            fixtureObjects.Clear();
            yield return null;
            NetworkManager.ResetStatics();
        }
    }

    // Mirror's early network callbacks finish before Update. This gate executes
    // before every weapon Update, including the first frame after owner spawn.
    [DefaultExecutionOrder(-32000)]
    internal sealed class WeaponAttackAdmissionFixtureGate : MonoBehaviour
    {
        private void Update()
        {
            if (NetworkClient.localPlayer != null)
                NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(false);
        }
    }
}
