#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System.Collections;
using System.Linq;
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

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class EnemyDefinitionPlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private EnemyDefinitionRuntimeFixture fixture;
        private GameObject[] roots;
        private GameObject gate;
        [UnityTest]
        public IEnumerator DefinitionsDriveRealSpawn_ResetDeathAndRestartPreserveIdentityAndAppearance()
        {
            const string boot = "Assets/_Project/Scenes/Boot.unity";
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(boot, LoadSceneMode.Single);
#endif
            roots = BootSceneFixtureObjects.Capture(boot);
            manager = (BootGameplayNetworkManager)NetworkManager.singleton;
            manager.ConfigurePreparationFlow(true);
            fixture = new EnemyDefinitionRuntimeFixture(manager);
            gate = new GameObject("Definition fixture automatic-attack gate"); gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "preparation");
            manager.SetOwnLoadout(6);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.Members[0].WeaponId == 6, "loadout");
            manager.StartPreparedGame();
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame && fixture.PairReady(), "two definition spawns");
            yield return null;
            foreach (var agent in fixture.Agents()) fixture.Verify(agent, true);
            var first = fixture.Agents().Single(a => a.Birth.DefinitionId == fixture.Definitions[0].Id);
            var second = fixture.Agents().Single(a => a.Birth.DefinitionId == fixture.Definitions[1].Id);
            uint firstId = first.netId, secondId = second.netId;
            yield return EnemyDefinitionRuntimeFixture.Wait(() => fixture.PlaceInView(), "visible death fixture placement");
            EnemyDefinitionRuntimeFixture.Damage(secondId, 7);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => second.GetComponent<CombatantBehaviour>().CurrentHealth == 53, "canonical damage");
            NetworkCombatWorld.Instance.ResetReferenceEnemy(second);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => second.ReferenceResetVersion > 0, "reference reset");
            yield return null; fixture.Verify(second, false);
            EnemyDefinitionRuntimeFixture.Damage(firstId, 20);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => !NetworkCombatWorld.Instance.Gateway.Ledger.IsAlive(firstId), "canonical lethal damage");
            yield return EnemyDefinitionRuntimeFixture.Wait(() => first == null, "confirmed death and presentation cleanup");
            fixture.Verify(second, false);
            var connection = NetworkClient.connection; string oldRun = manager.RoomSnapshot.RunId;
            NetworkClient.localPlayer.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.GameOver, "game over");
            manager.ChooseRunEndAction(RunEndAction.Restart);
            yield return EnemyDefinitionRuntimeFixture.Wait(() => manager.RoomSnapshot.RunId != oldRun &&
                manager.RoomSnapshot.Phase == PreparationPhase.InGame && fixture.PairReady(), "new round definitions");
            yield return null;
            Assert.That(NetworkClient.connection, Is.SameAs(connection), "Restart must retain transport connection.");
            foreach (var agent in fixture.Agents())
            {
                Assert.That(agent.netId, Is.Not.EqualTo(firstId).And.Not.EqualTo(secondId));
                fixture.Verify(agent, true);
            }
            Debug.Log("[EnemyDefinitionFixture] spawn/damage/reset/death/shared appearance/restart PASS");
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (manager != null)
            {
                manager.LeavePreparationRoom();
                yield return EnemyDefinitionRuntimeFixture.Wait(() => !NetworkClient.active && !NetworkServer.active &&
                    !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "fixture cleanup");
            }
            fixture?.Dispose(); UnityEngine.Object.Destroy(gate);
            BootSceneFixtureObjects.Destroy(roots); yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
#endif
