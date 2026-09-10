using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
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
    public sealed class UpgradeSelectionNetworkTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private GameObject gate;
        private UpgradeSelectionRules rulesCopy;
        private AstralShift.HellMaiden.Data.Perks.PerkDB perkCopy;
        private NetworkIdentity Player => NetworkClient.localPlayer;
        private PlayerBuildRuntime Build => Player.GetComponent<PlayerBuildRuntime>();
        private NetworkModifierSelection Authority => Player.GetComponent<NetworkModifierSelection>();
        private ModifierSelectionController View => Player.GetComponent<ModifierSelectionController>();

        [UnityTest]
        public IEnumerator BulkXpQueuesEachEarnedLevel_EquipmentHasTwoPhases_BackAndOldRequestsCannotApply()
        {
            yield return StartHost();
            uint baseline = Authority.BuildRevision;
            Authority.ServerGrantExperience(Authority.ExperiencePerLevel * 6);
            yield return WaitFor(() => View.Offers.Count > 0);
            var state = Authority.CaptureProgression();
            Assert.That(state.Rewards.Select(r => r.EarnedLevel), Is.EqualTo(new[] { 2, 3, 4, 5, 6, 7 }));
            Assert.That(state.Rewards.Select(r => r.Kind), Is.EqualTo(new[] {
                UpgradeRewardKind.Equipment, UpgradeRewardKind.Equipment, UpgradeRewardKind.Weapon,
                UpgradeRewardKind.Equipment, UpgradeRewardKind.Equipment, UpgradeRewardKind.Perk }));
            Assert.That(View.EarnedLevel, Is.EqualTo(2));
            var original = View.Offers.Select(o => o.ContentId).ToArray();
            ulong old = Authority.PendingEventId;
            Assert.That(View.Select(0).Succeeded, Is.True);
            Assert.That(View.Select(0).Succeeded, Is.False);
            yield return WaitFor(() => View.Stage == UpgradeSelectionStage.EquipmentTarget);
            Assert.That(Build.EquipmentCount, Is.Zero);
            Assert.That(Authority.BuildRevision, Is.EqualTo(baseline));
            Assert.That(Authority.PendingUpgradeCount, Is.EqualTo(6));
            Assert.That(View.Offers.Count, Is.EqualTo(1), "A single target still needs confirmation.");
            Assert.That(Authority.ServerSelect(Player.connectionToClient, old, 0, out _), Is.False);
            ulong targetEvent = Authority.PendingEventId;
            Assert.That(View.Back().Succeeded, Is.True);
            yield return WaitFor(() => View.Stage == UpgradeSelectionStage.Reward && !View.IsRequestPending);
            Assert.That(View.Offers.Select(o => o.ContentId), Is.EqualTo(original));
            Assert.That(Authority.ServerBack(Player.connectionToClient, targetEvent, out _), Is.False);
            Assert.That(Authority.ServerSelect(Player.connectionToClient, targetEvent, 0, out _), Is.False);
            Assert.That(View.Select(0).Succeeded, Is.True);
            yield return WaitFor(() => View.Stage == UpgradeSelectionStage.EquipmentTarget);
            ulong final = Authority.PendingEventId;
            Assert.That(View.Select(0).Succeeded, Is.True);
            yield return WaitFor(() => Authority.BuildRevision == baseline + 1 && View.EarnedLevel == 3);
            Assert.That(Build.EquipmentCount, Is.EqualTo(1));
            Assert.That(Authority.ServerSelect(Player.connectionToClient, final, 0, out _), Is.False);
            Assert.That(Authority.PendingUpgradeCount, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator FourTargets_AreSelectableByIndexAndButton_AndExtraEquipmentDoesNotAdvanceLevel()
        {
            yield return StartHost();
            for (int i = 1; i < 4; i++) Build.EquipWeaponAtSlot(i, Build.InitialWeapon.WeaponData);
            Authority.ServerQueueUpgrades();
            yield return WaitFor(() => View.Offers.Count > 0);
            uint card = View.Offers[0].ContentId;
            View.Select(0);
            yield return WaitFor(() => View.Offers.Count == 4);
            var menu = UnityEngine.Object.FindFirstObjectByType<CardPickMenu>();
            var fourth = menu.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b => b.name == "Option3");
            Assert.That(fourth.gameObject.activeSelf, Is.True);
            fourth.onClick.Invoke();
            yield return WaitFor(() => Authority.PendingUpgradeCount == 0);
            Assert.That(Build.GetEquipmentStates().Single().SourceSlotIndex, Is.EqualTo(3));
            Assert.That(Build.GetEquipmentStates().Single().EquipmentId, Is.EqualTo(card));
            Assert.That(Authority.Level, Is.EqualTo(1));
            Assert.That(Build.EquipmentCount, Is.EqualTo(1));
        }

        [TestCase(UpgradeRewardKind.Weapon)]
        [TestCase(UpgradeRewardKind.Equipment)]
        [TestCase(UpgradeRewardKind.Perk)]
        public void TypedOfferContractRoundTripsAllFields(UpgradeRewardKind kind)
        {
            var message = new UpgradeOptionMessage { OptionId = ulong.MaxValue - 2, Kind = kind, ContentId = 402,
                SlotIndex = 3, LevelIndex = 2, PerkLevel = 7, Rarity = AstralShift.HellMaiden.Data.Perks.PerkRarity.Gold };
            using (var writer = NetworkWriterPool.Get())
            {
                writer.Write(message);
                using (var reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    var copy = reader.Read<UpgradeOptionMessage>();
                    Assert.That(copy.OptionId, Is.EqualTo(message.OptionId));
                    Assert.That(copy.Kind, Is.EqualTo(kind));
                    Assert.That(copy.ContentId, Is.EqualTo(402));
                    Assert.That(copy.SlotIndex, Is.EqualTo(3));
                    Assert.That(copy.LevelIndex, Is.EqualTo(2));
                    Assert.That(copy.PerkLevel, Is.EqualTo(7));
                    Assert.That(copy.Rarity, Is.EqualTo(message.Rarity));
                }
            }
        }

        [UnityTest]
        public IEnumerator EmptyPerkPoolKeepsRewardAndUnlocksUntilCandidatesBecomeAvailable()
        {
            yield return StartHost();
            var rulesField = typeof(NetworkModifierSelection).GetField("selectionRules", BindingFlags.Instance | BindingFlags.NonPublic);
            rulesCopy = UnityEngine.Object.Instantiate((UpgradeSelectionRules)rulesField.GetValue(Authority));
            typeof(UpgradeSelectionRules).GetField("firstPerkLevel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(rulesCopy, 2);
            rulesField.SetValue(Authority, rulesCopy);
            var db = Build.BuildDatabase;
            var original = db.PerkDB;
            perkCopy = UnityEngine.Object.Instantiate(original);
            perkCopy.Perks = Array.Empty<AstralShift.HellMaiden.Data.Perks.PerkData>();
            var perkField = db.GetType().GetField("_perkDB", BindingFlags.Instance | BindingFlags.NonPublic);
            perkField.SetValue(db, perkCopy);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no legal candidates; reward retained and player unlocked"));
            Authority.ServerGrantExperience(Authority.ExperiencePerLevel);
            yield return null;
            Assert.That(Authority.PendingUpgradeCount, Is.EqualTo(1));
            Assert.That(Authority.PendingEventId, Is.Zero);
            Assert.That(Authority.IsSelecting, Is.False);
            Assert.That(Authority.CaptureProgression().Rewards.Single().Kind, Is.EqualTo(UpgradeRewardKind.Perk));
            perkField.SetValue(db, original);
            yield return WaitFor(() => View.Offers.Count > 0);
            Assert.That(View.Offers.All(o => o.Kind == UpgradeRewardKind.Perk), Is.True);
            Assert.That(View.EarnedLevel, Is.EqualTo(2));
        }

        private IEnumerator StartHost()
        {
            const string path = "Assets/_Project/Scenes/Boot.unity";
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
#endif
            bootRoots = BootSceneFixtureObjects.Capture(path);
            gate = new GameObject("M4 selection fixture attack gate");
            gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            manager = UnityEngine.Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7974, false, out string error), Is.True, error);
            SceneManager.sceneLoaded += Prepare;
            manager.StartHost();
            yield return WaitFor(() => Player != null && Authority.HasOwnerBaseline && View.IsPresentationReady);
        }
        private static void Prepare(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true)) spawner.enabled = false;
        }
        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            float until = Time.realtimeSinceStartup + 20;
            while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(predicate(), Is.True, "Formal Boot selection did not reach the expected state.");
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= Prepare;
            if (manager != null)
            {
                if (NetworkServer.active && NetworkClient.active) manager.StopHost();
                yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
            }
            BootSceneFixtureObjects.Destroy(bootRoots);
            if (gate != null) UnityEngine.Object.Destroy(gate);
            if (rulesCopy != null) UnityEngine.Object.Destroy(rulesCopy);
            if (perkCopy != null) UnityEngine.Object.Destroy(perkCopy);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
