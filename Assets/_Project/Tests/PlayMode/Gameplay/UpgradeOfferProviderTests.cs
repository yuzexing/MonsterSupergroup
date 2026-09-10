#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class UpgradeOfferProviderTests
    {
        private const string Content = "Assets/_Project/Content/HellMaiden/NativeGAS/";
        private readonly List<Object> objects = new List<Object>();
        private RuntimeDB database;
        private PlayerBuildRuntime build;
        private UpgradeSelectionRules rules;
        private UpgradeOfferProvider provider;
        private EquipmentData damage;

        [SetUp]
        public void SetUp()
        {
            Create("M4 pool").AddComponent<PoolManager>().Init();
            database = Create("M4 definitions").AddComponent<RuntimeDB>();
            database.ConfigureWeaponDatabase(Asset<WeaponDB>(Content + "NativeGasWeaponDB.asset"));
            Set(database, "_equipmentDB", Asset<EquipmentDB>(Content + "NativeGasEquipmentDB.asset"));
            Set(database, "_perkDB", Asset<PerkDB>(Content + "NativeGasPerkDB.asset"));
            rules = Asset<UpgradeSelectionRules>("Assets/_Project/Content/NetworkCombat/GameplayUpgradeSelectionRules.asset");
            damage = Asset<EquipmentData>("Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset");
            var playerObject = Create("M4 build");
            playerObject.SetActive(false);
            var player = playerObject.AddComponent<PlayerMovement>();
            var attacks = new GameObject("Attacks");
            attacks.transform.SetParent(playerObject.transform);
            player.AttacksParent = attacks.transform;
            build = playerObject.AddComponent<PlayerBuildRuntime>();
            build.Initialize(player, new FixedRandom());
            Set(build, "initialWeaponId", 6u);
            build.StartInitialBuild(database);
            provider = new UpgradeOfferProvider(new FixedRandom());
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            PoolManager.Instance = null;
        }

        [TestCase(2, UpgradeRewardKind.Equipment)]
        [TestCase(4, UpgradeRewardKind.Weapon)]
        [TestCase(6, UpgradeRewardKind.Equipment)]
        [TestCase(7, UpgradeRewardKind.Perk)]
        [TestCase(9, UpgradeRewardKind.Perk)]
        [TestCase(12, UpgradeRewardKind.Weapon)]
        [TestCase(18, UpgradeRewardKind.Weapon)]
        [TestCase(51, UpgradeRewardKind.Perk)]
        public void AuthoredScheduleMatchesConfirmedLevels(int level, UpgradeRewardKind expected)
        {
            rules.Validate();
            Assert.That(rules.RewardAtLevel(level), Is.EqualTo(expected));
        }

        [Test]
        public void ScheduleIsConfigurableAndWeaponWinsAnOverlap()
        {
            var copy = Clone(rules);
            Set(copy, "weaponLevels", new[] { 5, 9 });
            Set(copy, "firstPerkLevel", 5);
            Set(copy, "perkInterval", 3);
            Assert.That(copy.RewardAtLevel(5), Is.EqualTo(UpgradeRewardKind.Weapon));
            Assert.That(copy.RewardAtLevel(8), Is.EqualTo(UpgradeRewardKind.Perk));
            Assert.That(copy.RewardAtLevel(4), Is.EqualTo(UpgradeRewardKind.Equipment));
        }

        [Test]
        public void WeaponsExcludeOwnedAndSampleWithoutReplacement_ThenFreezeFullHandConversion()
        {
            var reward = Reward(4, UpgradeRewardKind.Weapon);
            var offers = provider.Generate(build, ref reward, rules);
            Assert.That(offers.Count, Is.EqualTo(3));
            Assert.That(offers.All(o => o.Kind == UpgradeRewardKind.Weapon && o.Weapon.ID != 6), Is.True);
            Assert.That(offers.Select(o => o.ContentId).Distinct().Count(), Is.EqualTo(3));
            foreach (var offer in offers) build.EquipWeapon(offer.Weapon);
            var converted = provider.Generate(build, ref reward, rules);
            Assert.That(build.WeaponCount, Is.EqualTo(4));
            Assert.That(reward.Kind, Is.EqualTo(UpgradeRewardKind.Equipment));
            Assert.That(converted.All(o => o.Kind == UpgradeRewardKind.Equipment && o.TargetSlotIndex == -1), Is.True);
            build.StartInitialBuild(database);
            Assert.That(provider.Generate(build, ref reward, rules).All(o => o.Kind == UpgradeRewardKind.Equipment), Is.True);
        }

        [TestCase(1u)] [TestCase(2u)] [TestCase(3u)] [TestCase(6u)] [TestCase(8u)] [TestCase(402u)]
        public void AllSixNativeWeaponsEquipThroughExistingBuild(uint id)
        {
            var weapon = database.WeaponDB.Weapons.Single(w => w.ID == id);
            var equipped = build.EquipWeapon(weapon);
            Assert.That(equipped.NativeRuntime, Is.Not.Null);
            using (var snapshot = equipped.NativeRuntime.BeginAttack(weapon.AttackTags))
                Assert.That(snapshot.Stats.Damage, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void NoUnownedWeaponConvertsEvenWithFreeSlots_ZeroWeightWeaponIsNeverOffered()
        {
            var weapons = Clone(database.WeaponDB);
            var disabled = Clone(database.WeaponDB.Weapons.Single(w => w.ID == 2));
            disabled.poolWeight = 0;
            weapons.Configure(new[] { build.InitialWeapon.WeaponData, disabled });
            database.ConfigureWeaponDatabase(weapons);
            var reward = Reward(4, UpgradeRewardKind.Weapon);
            Assert.That(provider.Generate(build, ref reward, rules).All(o => o.Kind == UpgradeRewardKind.Equipment), Is.True);
            Assert.That(reward.Kind, Is.EqualTo(UpgradeRewardKind.Equipment));
            Assert.That(build.WeaponCount, Is.EqualTo(1));
        }

        [Test]
        public void EquipmentTargetsAllFourSlots_UpgradeInFullSlotAndExcludeMaximumLevel()
        {
            for (int slot = 1; slot < 4; slot++) build.EquipWeaponAtSlot(slot, build.InitialWeapon.WeaponData);
            var targets = provider.GetEquipmentTargets(build, damage);
            Assert.That(targets.Select(t => t.TargetSlotIndex), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(build.EquipmentCount, Is.Zero);
            var handle = build.AddEquipment(2, damage, 0);
            foreach (var card in database.EquipmentDB.Equipments.Where(c => c != damage &&
                provider.GetEquipmentTargets(build, c).Any(t => t.TargetSlotIndex == 2)).Take(2))
                build.AddEquipment(2, card, 0);
            Assert.That(build.GetEquipmentStates().Count(e => e.SourceSlotIndex == 2), Is.EqualTo(3));
            var upgrade = provider.GetEquipmentTargets(build, damage).Single(t => t.TargetSlotIndex == 2);
            Assert.That(upgrade.LevelIndex, Is.EqualTo(1));
            Assert.That(upgrade.ExistingEquipmentHandle, Is.EqualTo(handle));
            for (int level = 1; level < damage.Levels.Length; level++) handle = build.UpgradeEquipment(handle, level);
            Assert.That(provider.GetEquipmentTargets(build, damage).Any(t => t.TargetSlotIndex == 2), Is.False);
            Assert.That(provider.GetEquipmentTargets(build, damage).Count, Is.EqualTo(3));
        }

        [Test]
        public void PerkThreeBronzeThenSilverAddsTwentyTwoPercent_AndNewWeaponsInheritIt()
        {
            var speed = database.PerkDB.Perks.Single(p => p.ID == 3);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(UpgradeOfferProvider.TryGetNextPerk(build.CaptureState().Perks, speed, out var rarity, out int level), Is.True);
                Assert.That(level, Is.EqualTo(i + 1));
                Assert.That(rarity, Is.EqualTo(i < 3 ? PerkRarity.Bronze : PerkRarity.Silver));
                build.AddPerk(speed, rarity);
            }
            using (var attack = build.InitialWeapon.NativeRuntime.BeginAttack(build.InitialWeapon.WeaponData.AttackTags))
                Assert.That(attack.Stats.SpeedMultiplierSum, Is.EqualTo(0.22f).Within(0.0001f));
            var added = build.EquipWeapon(database.WeaponDB.Weapons.Single(w => w.ID == 2));
            using (var attack = added.NativeRuntime.BeginAttack(added.WeaponData.AttackTags))
                Assert.That(attack.Stats.SpeedMultiplierSum, Is.EqualTo(0.22f).Within(0.0001f));
            var saved = build.CaptureState();
            build.ReconcileState(database, saved);
            build.ReconcileState(database, saved);
            Assert.That(build.PerkCount, Is.EqualTo(4));
            Assert.That(UpgradeOfferProvider.TryGetNextPerk(saved.Perks, speed, out var next, out int nextLevel), Is.True);
            Assert.That(next, Is.EqualTo(PerkRarity.Silver));
            Assert.That(nextLevel, Is.EqualTo(5));
        }

        [Test]
        public void CrystalIsOneAcquisition_AndSilverOnlyStartDoesNotInventBronze()
        {
            var projectile = database.PerkDB.Perks.Single(p => p.ID == 28);
            Assert.That(UpgradeOfferProvider.TryGetNextPerk(Array.Empty<PlayerBuildPerkSnapshot>(), projectile, out var rarity, out int level), Is.True);
            Assert.That(rarity, Is.EqualTo(PerkRarity.Crystal));
            Assert.That(level, Is.EqualTo(1));
            build.AddPerk(projectile, rarity);
            Assert.That(UpgradeOfferProvider.TryGetNextPerk(build.CaptureState().Perks, projectile, out _, out _), Is.False);
            var crit = database.PerkDB.Perks.Single(p => p.ID == 18);
            Assert.That(UpgradeOfferProvider.TryGetNextPerk(build.CaptureState().Perks, crit, out rarity, out _), Is.True);
            Assert.That(rarity, Is.EqualTo(PerkRarity.Silver));
        }

        [Test]
        public void ReferenceWeightThresholdsInterpolateTierAndRarityIndependently()
        {
            var at35 = UpgradeOfferProvider.InterpolateWeights(rules.PerkWeights, 35);
            Assert.That(at35.Select(t => t.Weight), Is.EqualTo(new[] { 63.5f, 27f, 7.995f, 1.505f }).Within(0.0001f));
            Assert.That(at35[1].RarityWeights.Single(r => r.Rarity == PerkRarity.Silver).Weight, Is.EqualTo(95.75f).Within(.0001f));
            var after = UpgradeOfferProvider.InterpolateWeights(rules.PerkWeights, 100);
            Assert.That(after.Select(t => t.Weight), Is.EqualTo(new[] { 47f, 35f, 15f, 3f }));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(7)]
        public void PerkFinitePoolShowsActualCountAndNeverDuplicatesAnId(int count)
        {
            var perks = Clone(database.PerkDB);
            perks.Perks = perks.Perks.Take(count).ToArray();
            Set(database, "_perkDB", perks);
            var reward = Reward(7, UpgradeRewardKind.Perk);
            var offers = provider.Generate(build, ref reward, rules);
            Assert.That(offers.Count, Is.EqualTo(Math.Min(3, count)));
            Assert.That(offers.Select(o => o.ContentId).Distinct().Count(), Is.EqualTo(offers.Count));
            Assert.That(offers.All(o => provider.IsEligible(build, o)), Is.True);
            Assert.That(reward.Kind, Is.EqualTo(UpgradeRewardKind.Perk));
        }

        [Test]
        public void ExhaustedPerksYieldNoReward_AndKeepTheirCategory()
        {
            foreach (var data in database.PerkDB.Perks)
                while (UpgradeOfferProvider.TryGetNextPerk(build.CaptureState().Perks, data, out var rarity, out _))
                    build.AddPerk(data, rarity);
            var reward = Reward(100, UpgradeRewardKind.Perk);
            Assert.That(provider.Generate(build, ref reward, rules), Is.Empty);
            Assert.That(reward.Kind, Is.EqualTo(UpgradeRewardKind.Perk));
        }

        private static PendingUpgradeReward Reward(int level, UpgradeRewardKind kind) => new PendingUpgradeReward { EarnedLevel = level, Kind = kind };
        private GameObject Create(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        private T Clone<T>(T value) where T : Object { var copy = Object.Instantiate(value); objects.Add(copy); return copy; }
        private static T Asset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private sealed class FixedRandom : IRandomSource { public float Next01() => 0.37f; }
    }
}
#endif
