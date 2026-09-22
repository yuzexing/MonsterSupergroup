#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class UpgradeOfferDeferralPlayModeTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private GameObject attackGate;
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkModifierSelection Upgrades => Owner.GetComponent<NetworkModifierSelection>();
        private ModifierSelectionController View => Owner.GetComponent<ModifierSelectionController>();
        private PlayerMovement Player => Owner.GetComponent<PlayerMovement>();

        [UnityTest]
        public IEnumerator EarnedLevelsRemainUnlockedAndQueueNormally_UntilMatchingReleaseAutomaticallyOpensOffers()
        {
            yield return StartHost();
            uint revision = Upgrades.BuildRevision;
            Assert.That(Upgrades.ServerDeferOffers(101), Is.True);
            float firstGrant = Enumerable.Range(1, 3).Sum(Upgrades.ExperienceRequiredAtLevel) + .5f;
            Assert.That(Upgrades.TryGrantExperience(firstGrant), Is.True);
            for (int i = 0; i < 3; i++) Assert.That(Upgrades.TryGrantExperience(Upgrades.ExperiencePerLevel), Is.True);
            yield return null;
            Assert.That(Upgrades.Level, Is.EqualTo(7));
            Assert.That(Upgrades.Experience, Is.EqualTo(.5f));
            Assert.That(Upgrades.PendingUpgradeCount, Is.EqualTo(6));
            Assert.That(Upgrades.CaptureProgression().Rewards.Select(r => r.EarnedLevel),
                Is.EqualTo(Enumerable.Range(2, 6)));
            Assert.That(Upgrades.CaptureProgression().Rewards.Select(r => r.Kind), Is.EqualTo(new[] {
                UpgradeRewardKind.Equipment, UpgradeRewardKind.Equipment, UpgradeRewardKind.Weapon,
                UpgradeRewardKind.Equipment, UpgradeRewardKind.Equipment, UpgradeRewardKind.Perk }));
            AssertNoSelection();
            Assert.That(Player.CombatantBinding.ApplyDamage(1), Is.GreaterThan(0),
                "Waiting rewards must not grant upgrade-selection invulnerability.");
            Assert.That(Upgrades.BuildRevision, Is.EqualTo(revision));

            Upgrades.ServerResumeOffers(101);
            yield return Wait(() => View.Offers.Count > 0, "automatic first offer after release");
            Assert.That(Upgrades.OffersDeferred, Is.False);
            Assert.That(Upgrades.IsSelecting, Is.True);
            Assert.That(Player.IsUpgradeSelectionLocked, Is.True);
            var earned = new List<int>();
            double deadline = Time.realtimeSinceStartupAsDouble + 15;
            while (Upgrades.PendingUpgradeCount > 0 && Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (View.Offers.Count > 0 && !View.IsRequestPending)
                {
                    if (earned.Count == 0 || earned[earned.Count - 1] != View.EarnedLevel)
                        earned.Add(View.EarnedLevel);
                    Assert.That(View.Select(0).Succeeded, Is.True);
                }
                yield return null;
            }
            Assert.That(earned, Is.EqualTo(Enumerable.Range(2, 6)));
            Assert.That(Upgrades.PendingUpgradeCount, Is.Zero);
            Assert.That(Upgrades.BuildRevision, Is.EqualTo(revision + 6));
            Assert.That(Upgrades.Level, Is.EqualTo(7));
            Assert.That(Upgrades.Experience, Is.EqualTo(.5f));
            Upgrades.ServerResumeOffers(101);
            yield return null;
            AssertNoSelection();
            Assert.That(Upgrades.BuildRevision, Is.EqualTo(revision + 6), "Duplicate release cannot spend another reward.");
        }

        [UnityTest]
        public IEnumerator ZeroAndStaleTokensCannotReplaceOrReleaseAnotherDeferral_AndOpenOffersRejectDeferral()
        {
            yield return StartHost();
            Assert.That(Upgrades.ServerDeferOffers(0), Is.False);
            Assert.That(Upgrades.ServerDeferOffers(201), Is.True);
            Assert.That(Upgrades.ServerDeferOffers(202), Is.False);
            Upgrades.ServerResumeOffers(0);
            Upgrades.ServerResumeOffers(202);
            Assert.That(Upgrades.OffersDeferred, Is.True);
            Upgrades.ServerResumeOffers(201);
            Assert.That(Upgrades.OffersDeferred, Is.False);
            Assert.That(Upgrades.ServerDeferOffers(202), Is.True);
            Upgrades.ServerResumeOffers(201);
            Assert.That(Upgrades.TryGrantExperience(Upgrades.ExperiencePerLevel), Is.True);
            yield return null;
            Assert.That(Upgrades.OffersDeferred, Is.True);
            AssertNoSelection();
            Upgrades.ServerResumeOffers(202);
            yield return Wait(() => View.Offers.Count > 0, "offer after current token release");
            ulong offered = Upgrades.PendingEventId;
            Assert.That(Upgrades.ServerDeferOffers(203), Is.False);
            Assert.That(Upgrades.PendingEventId, Is.EqualTo(offered));
            Assert.That(View.Select(0).Succeeded, Is.True);
            yield return Wait(() => View.Stage == UpgradeSelectionStage.EquipmentTarget, "equipment target selection");
            Assert.That(Upgrades.ServerDeferOffers(203), Is.False);
            Assert.That(Upgrades.PendingUpgradeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DisabledSelectionReleasesDeferralButRetainsRewards_ThenReenableOpensThem()
        {
            yield return StartHost();
            Assert.That(Upgrades.ServerDeferOffers(301), Is.True);
            Assert.That(Upgrades.TryGrantExperience(Upgrades.ExperiencePerLevel + .5f), Is.True);
            Upgrades.enabled = false;
            yield return null;
            Assert.That(Upgrades.OffersDeferred, Is.False);
            Assert.That(Upgrades.PendingUpgradeCount, Is.EqualTo(1));
            Assert.That(Upgrades.Level, Is.EqualTo(2));
            Assert.That(Upgrades.Experience, Is.EqualTo(.5f));
            AssertNoSelection();
            Upgrades.enabled = true;
            yield return Wait(() => Upgrades.HasOwnerBaseline && View.Offers.Count > 0, "retained reward after reenable");
            Assert.That(View.EarnedLevel, Is.EqualTo(2));
            Assert.That(Upgrades.PendingUpgradeCount, Is.EqualTo(1));
            Upgrades.ServerResumeOffers(301);
            Assert.That(View.Offers.Count, Is.GreaterThan(0), "Stale cast completion cannot close a resumed menu.");
        }

        [UnityTest]
        public IEnumerator DeathReleasesDeferralAndRetainsEarnedRewards_WithoutOpeningAnOfferForTheDeadPlayer()
        {
            yield return StartHost();
            Assert.That(Upgrades.ServerDeferOffers(401), Is.True);
            float grant = Enumerable.Range(1, 3).Sum(Upgrades.ExperienceRequiredAtLevel) + .5f;
            Assert.That(Upgrades.TryGrantExperience(grant), Is.True);
            Assert.That(Player.CombatantBinding.ApplyDamage(int.MaxValue), Is.GreaterThan(0));
            yield return Wait(() => !NetworkCombatWorld.Instance.Gateway.Ledger.IsAlive(Owner.netId),
                "owner lethal health report reaches canonical ledger");
            yield return Wait(() => !Upgrades.OffersDeferred, "death releases presentation deferral");
            Assert.That(Upgrades.Level, Is.EqualTo(4));
            Assert.That(Upgrades.Experience, Is.EqualTo(.5f));
            Assert.That(Upgrades.PendingUpgradeCount, Is.EqualTo(3));
            Assert.That(Upgrades.CaptureProgression().Rewards.Select(r => r.EarnedLevel),
                Is.EqualTo(new[] { 2, 3, 4 }));
            Upgrades.ServerResumeOffers(401);
            yield return null;
            AssertNoSelection();
            Assert.That(Upgrades.ServerDeferOffers(402), Is.False);
        }

        private void AssertNoSelection()
        {
            Assert.That(Upgrades.IsSelecting, Is.False);
            Assert.That(Upgrades.PendingEventId, Is.Zero);
            Assert.That(Upgrades.ServerOffers, Is.Empty);
            Assert.That(View.Offers, Is.Empty);
            Assert.That(Player.IsUpgradeSelectionLocked, Is.False);
        }

        private IEnumerator StartHost()
        {
            const string boot = "Assets/_Project/Scenes/Boot.unity";
            if (!Application.isBatchMode) UnityEditor.EditorApplication.ExecuteMenuItem("Window/General/Game");
            GameOptionsService.EnsureInitialized();
            yield return Wait(() => GameLocalization.IsReady, "localization startup");
            yield return null;
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot, new LoadSceneParameters(LoadSceneMode.Single));
            bootRoots = BootSceneFixtureObjects.Capture(boot);
            manager = UnityEngine.Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            // Direct Host keeps the death test inside the same run; normal room-wide game-over
            // intentionally clears rewards at round end and is covered by run lifecycle tests.
            manager.ConfigurePreparationFlow(false);
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7974, false,
                out string error), Is.True, error);
            attackGate = new GameObject("Upgrade deferral isolated-test attack gate");
            attackGate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            SceneManager.sceneLoaded += DisableEnemySpawning;
            manager.StartHost();
            yield return Wait(() => Owner != null && Upgrades.HasOwnerBaseline && View.IsPresentationReady,
                "owner progression and presentation baselines");
        }

        private static void DisableEnemySpawning(Scene scene, LoadSceneMode mode)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                    spawner.enabled = false;
        }

        private static IEnumerator Wait(Func<bool> condition, string stage) =>
            EnemyDefinitionRuntimeFixture.Wait(condition, stage);

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded -= DisableEnemySpawning;
            if (manager != null)
            {
                if (NetworkServer.active && NetworkClient.active) manager.StopHost();
                yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "deferral test cleanup");
            }
            if (attackGate != null) UnityEngine.Object.Destroy(attackGate);
            BootSceneFixtureObjects.Destroy(bootRoots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
#endif
