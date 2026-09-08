using System;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class EquipmentUpgradeRuntimeTests
    {
        private GameObject poolObject, playerObject, databaseObject;
        private PlayerBuildRuntime build;
        private EquipmentDB equipmentDB;
        private RuntimeDB database;

        [SetUp]
        public void SetUp()
        {
            poolObject = new GameObject("Equipment Upgrade Test Pool");
            poolObject.AddComponent<PoolManager>().Init();
            playerObject = new GameObject("Equipment Upgrade Test Player");
            playerObject.SetActive(false);
            var player = playerObject.AddComponent<PlayerMovement>();
            var attacks = new GameObject("Attacks");
            attacks.transform.SetParent(playerObject.transform, false);
            player.AttacksParent = attacks.transform;
            build = playerObject.AddComponent<PlayerBuildRuntime>();
            build.Initialize(player, new FixedRandom(0.99f));
            databaseObject = new GameObject("Equipment Upgrade RuntimeDB");
            database = databaseObject.AddComponent<RuntimeDB>();
            database.ConfigureWeaponDatabase(Asset<WeaponDB>(
                "Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasWeaponDB.asset"));
            equipmentDB = UnityEngine.Object.Instantiate(Asset<EquipmentDB>(
                "Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasEquipmentDB.asset"));
            typeof(RuntimeDB).GetField("_equipmentDB", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(database, equipmentDB);
            build.StartInitialBuild(database);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(playerObject);
            UnityEngine.Object.DestroyImmediate(databaseObject);
            UnityEngine.Object.DestroyImmediate(equipmentDB);
            UnityEngine.Object.DestroyImmediate(poolObject);
            PoolManager.Instance = null;
        }

        [Test]
        public void Upgrade_ReplacesAbsoluteLevelAndPreservesCapacityAndApplicationOrder()
        {
            var damage = equipmentDB.Equipments.Single(card => card.ID == 2u);
            var first = build.AddEquipment(0, damage, 0);
            var speed = build.AddEquipment(0, equipmentDB.Equipments[1], 0);
            Assert.That(build.InitialWeapon.DamageValue, Is.EqualTo(20));
            var upgraded = build.UpgradeEquipment(first, 1);
            Assert.That(upgraded.IsValid, Is.True);
            Assert.That(upgraded, Is.Not.EqualTo(first));
            Assert.That(build.EquipmentCount, Is.EqualTo(2));
            Assert.That(build.InitialWeapon.NativeRuntime.ModifierCount, Is.EqualTo(2));
            Assert.That(build.InitialWeapon.DamageValue, Is.EqualTo(23));
            Assert.That(build.GetEquipmentStates().Select(state => state.Handle),
                Is.EqualTo(new[] { speed, upgraded }));
            Assert.That(build.RemoveEquipment(first), Is.False);
            Assert.Throws<ArgumentException>(() => build.UpgradeEquipment(first, 1));
            using (var attack = build.InitialWeapon.NativeRuntime.BeginAttack())
            {
                var target = new Target();
                var resolution = build.InitialWeapon.NativeRuntime.ResolveHitDetailed(attack, target);
                Assert.That(resolution.ResolvedDamage.Value, Is.EqualTo(23));
                Assert.That(target.Health, Is.EqualTo(77));
            }
            var maximum = build.UpgradeEquipment(upgraded, 2);
            Assert.That(build.InitialWeapon.DamageValue, Is.EqualTo(30));
            Assert.Throws<ArgumentOutOfRangeException>(() => build.UpgradeEquipment(maximum, 3));
            Assert.That(build.RemoveEquipment(maximum), Is.True);
            Assert.That(build.InitialWeapon.DamageValue, Is.EqualTo(15));
        }

        [Test]
        public void Upgrade_FailedReplacementLeavesOriginalHandleLevelAndModifiersIntact()
        {
            EquipmentData invalid = UnityEngine.Object.Instantiate(equipmentDB.Equipments[0]);
            try
            {
                var handle = build.AddEquipment(0, invalid, 0);
                var unknown = new EquipmentModifierApplication();
                unknown.Configure(new EquipmentDataModifier(
                    new EquipmentModifierID(0x0100FFFFu), new DamageStatModifierParameters(0.5f)),
                    "invalid", false, null);
                invalid.Levels[1].ConfigureNative(new[] { invalid.Levels[1].Modifiers[0], unknown });
                Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                    () => build.UpgradeEquipment(handle, 1));
                Assert.That(build.EquipmentCount, Is.EqualTo(1));
                Assert.That(build.GetEquipmentStates()[0].Handle, Is.EqualTo(handle));
                Assert.That(build.GetEquipmentStates()[0].LevelIndex, Is.Zero);
                Assert.That(build.InitialWeapon.NativeRuntime.ModifierCount, Is.EqualTo(1));
                Assert.That(build.InitialWeapon.DamageValue, Is.EqualTo(20));
            }
            finally { UnityEngine.Object.DestroyImmediate(invalid); }
        }

        [Test]
        public void Provider_ConsecutiveOffersUseOwnedNextLevelAndRespectFullSlots()
        {
            var provider = new EquipmentModifierOfferProvider(new FixedRandom(0f));
            var first = provider.Generate(build);
            Assert.That(first.Count, Is.EqualTo(3));
            Assert.That(first.Select(option => option.EquipmentId).Distinct().Count(), Is.EqualTo(3));
            var added = build.AddEquipment(0, first[0].Equipment, 0);
            Assert.That(provider.IsEligible(build, first[0]), Is.False, "An old new-card offer cannot stack duplicates.");
            var next = provider.Generate(build);
            var upgrade = next.Single(option => option.EquipmentId == first[0].EquipmentId);
            Assert.That(upgrade.LevelIndex, Is.EqualTo(1));
            Assert.That(upgrade.ExistingEquipmentHandle, Is.EqualTo(added));
            Assert.That(provider.IsEligible(build, upgrade), Is.True);
            var second = build.AddEquipment(0, first[1].Equipment, 0);
            var third = build.AddEquipment(0, first[2].Equipment, 0);
            var full = provider.Generate(build);
            Assert.That(full.Count, Is.EqualTo(3));
            Assert.That(full.All(option => option.ExistingEquipmentHandle.IsValid && option.LevelIndex == 1), Is.True);
            build.UpgradeEquipment(added, 1);
            Assert.That(provider.IsEligible(build, upgrade), Is.False, "The previous-level handle has been consumed.");
            Assert.That(build.EquipmentCount, Is.EqualTo(3));
            Assert.That(build.GetEquipmentStates().Select(state => state.Handle), Does.Contain(second));
            Assert.That(build.GetEquipmentStates().Select(state => state.Handle), Does.Contain(third));
        }

        [Test]
        public void Provider_ExhaustedPoolReturnsNoOfferRatherThanInvalidOrDuplicateChoices()
        {
            var provider = new EquipmentModifierOfferProvider(new FixedRandom(0f));
            var first = provider.Generate(build);
            foreach (var offer in first) build.AddEquipment(0, offer.Equipment, offer.Equipment.Levels.Length - 1);
            Assert.That(provider.Generate(build), Is.Empty);
            Assert.That(provider.Diagnostic, Does.Contain("0 eligible"));
            Assert.That(build.EquipmentCount, Is.EqualTo(3));
            Assert.That(build.InitialWeapon.NativeRuntime.ModifierCount, Is.EqualTo(3));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void Provider_PartiallyExhaustedBuildOffersOnlyRemainingEligibleUpgrades(int remaining)
        {
            var provider = new EquipmentModifierOfferProvider(new FixedRandom(0.99f));
            var initial = provider.Generate(build);
            for (int i = 0; i < initial.Count; i++)
                build.AddEquipment(0, initial[i].Equipment,
                    i < remaining ? 0 : initial[i].Equipment.Levels.Length - 1);
            var previousStates = build.GetEquipmentStates().ToArray();
            int previousModifierCount = build.InitialWeapon.NativeRuntime.ModifierCount;

            var offers = provider.Generate(build);

            Assert.That(offers.Count, Is.EqualTo(remaining));
            Assert.That(offers.Select(offer => offer.EquipmentId),
                Is.EquivalentTo(initial.Take(remaining).Select(offer => offer.EquipmentId)));
            Assert.That(offers.Select(offer => offer.OfferId).Distinct().Count(), Is.EqualTo(remaining));
            Assert.That(offers.All(offer => offer.ExistingEquipmentHandle.IsValid &&
                offer.LevelIndex == 1 && provider.IsEligible(build, offer)), Is.True);
            Assert.That(provider.Diagnostic, Is.Null);
            Assert.That(build.GetEquipmentStates().Select(state => state.Handle),
                Is.EqualTo(previousStates.Select(state => state.Handle)), "Generating a smaller offer cannot mutate Build.");
            Assert.That(build.InitialWeapon.NativeRuntime.ModifierCount, Is.EqualTo(previousModifierCount));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Provider_DatabaseEntryPointReturnsAvailableDistinctCardsUpToThree(int available)
        {
            var provider = new EquipmentModifierOfferProvider(new FixedRandom(0f));
            var eligible = provider.Generate(build).Select(offer => offer.Equipment).ToArray();
            equipmentDB.Equipments = eligible.Take(available).ToArray();

            var offers = provider.Generate(equipmentDB, build.InitialWeapon.WeaponData);

            Assert.That(offers.Count, Is.EqualTo(available));
            Assert.That(offers.Select(offer => offer.Equipment), Is.EquivalentTo(equipmentDB.Equipments));
            Assert.That(offers.All(offer => offer.LevelIndex == 0 &&
                !offer.ExistingEquipmentHandle.IsValid), Is.True);
            Assert.That(build.EquipmentCount, Is.Zero);
            if (available == 0) Assert.That(provider.Diagnostic, Does.Contain("0 eligible"));
            else Assert.That(provider.Diagnostic, Is.Null);
        }

        [Test]
        public void WeaponExecutionTogglePreservesRuntimeForCanonicalRemoteBuildsAndNewWeapons()
        {
            build.SetWeaponExecutionEnabled(false);
            Assert.That(build.InitialWeapon.enabled, Is.False);
            Assert.That(build.InitialWeapon.NativeRuntime.IsInitialized, Is.True);
            var additional = build.EquipWeaponAtSlot(1, build.InitialWeapon.WeaponData);
            Assert.That(additional.enabled, Is.False);
            Assert.That(build.GetWeaponAtSlot(1), Is.SameAs(additional));
            Assert.That(additional.NativeRuntime.IsInitialized, Is.True);
            build.SetWeaponExecutionEnabled(true);
            Assert.That(build.InitialWeapon.enabled, Is.True);
            Assert.That(additional.enabled, Is.True);
            build.ClearBuild();
            Assert.That(build.GetEquipmentStates(), Is.Empty);
            Assert.That(build.GetWeaponAtSlot(0), Is.Null);
            Assert.That(build.GetWeaponAtSlot(1), Is.Null);
        }

        private static T Asset<T>(string path) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            var result = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(result, Is.Not.Null, path);
            return result;
#else
            throw new NotSupportedException("Existing content integration requires the Editor.");
#endif
        }

        private sealed class FixedRandom : IRandomSource
        {
            private readonly float value;
            public FixedRandom(float value) { this.value = value; }
            public float Next01() => value;
        }

        private sealed class Target : ICombatTarget
        {
            public int Health { get; private set; } = 100;
            public bool IsAlive => Health > 0;
            public DamageInfo ReceiveDamage(DamageInfo damage)
            {
                int accepted = Math.Min(Health, damage.Value);
                Health -= accepted;
                return new DamageInfo(damage.Id, accepted, damage.IsCritical);
            }
            public StatusApplicationResult ApplyStatus(StatusApplication application) =>
                StatusApplicationResult.Rejected;
        }
    }
}
