using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class ModifierSelectionTests
    {
        private const string Content = "Assets/_Project/Content/HellMaiden/NativeGAS/";
        private GameObject poolObject, playerObject, databaseObject, selectionObject;
        private PlayerBuildRuntime build;
        private RuntimeDB runtimeDB;
        private EquipmentDB equipmentDB;
        private WeaponData weaponData;
        private ModifierSelectionController selection;

        [SetUp]
        public void SetUp()
        {
            poolObject = new GameObject("Selection Test Pool");
            poolObject.AddComponent<PoolManager>().Init();
            playerObject = new GameObject("Selection Test Player");
            playerObject.SetActive(false);
            PlayerMovement player = playerObject.AddComponent<PlayerMovement>();
            var attacks = new GameObject("Attacks");
            attacks.transform.SetParent(playerObject.transform, false);
            player.AttacksParent = attacks.transform;
            build = playerObject.AddComponent<PlayerBuildRuntime>();
            build.Initialize(player, new FixedRandom(0.99f));

            equipmentDB = UnityEngine.Object.Instantiate(Asset<EquipmentDB>(Content + "NativeGasEquipmentDB.asset"));
            WeaponDB weaponDB = Asset<WeaponDB>(Content + "NativeGasWeaponDB.asset");
            weaponData = weaponDB.Weapons[0];
            databaseObject = new GameObject("Selection Test RuntimeDB");
            runtimeDB = databaseObject.AddComponent<RuntimeDB>();
            runtimeDB.ConfigureWeaponDatabase(weaponDB);
            typeof(RuntimeDB).GetField("_equipmentDB", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(runtimeDB, equipmentDB);
            build.StartInitialBuild(runtimeDB);
            selectionObject = new GameObject("Selection Test Controller");
            selection = selectionObject.AddComponent<ModifierSelectionController>();
            selection.Initialize(new FixedRandom(0f));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(selectionObject);
            UnityEngine.Object.DestroyImmediate(playerObject);
            UnityEngine.Object.DestroyImmediate(databaseObject);
            UnityEngine.Object.DestroyImmediate(equipmentDB);
            UnityEngine.Object.DestroyImmediate(poolObject);
            PoolManager.Instance = null;
        }

        [Test]
        public void Provider_UsesNativeDefinitionsAndDeterministicDistinctEligibleCards()
        {
            var a = new EquipmentModifierOfferProvider(new SeededRandom(71));
            var b = new EquipmentModifierOfferProvider(new SeededRandom(71));
            var first = a.Generate(equipmentDB, weaponData);
            var second = b.Generate(equipmentDB, weaponData);
            Assert.That(first.Select(offer => offer.EquipmentId),
                Is.EqualTo(second.Select(offer => offer.EquipmentId)));
            Assert.That(first.Select(offer => offer.EquipmentId).Distinct().Count(), Is.EqualTo(3));
            foreach (ModifierOffer offer in first)
            {
                Assert.That(offer.LevelIndex, Is.Zero);
                Assert.That(equipmentDB.Equipments, Does.Contain(offer.Equipment));
                foreach (EquipmentModifierApplication application in offer.Modifiers)
                {
                    Assert.That(application.ModifierIdValue, Is.InRange(0x01000001u, 0x01000008u));
                    Assert.That(application.Parameters, Is.Not.Null);
                    Assert.That(weaponData.Supports(application.ModifierId), Is.True);
                }
            }
            Assert.That(build.EquipmentCount, Is.Zero);
            Assert.That(build.InitialWeapon.NativeRuntime.ModifierCount, Is.Zero);
        }

        [Test]
        public void Provider_RejectsIncompleteUnknownAndWrongParameterDefinitionsWithoutInstantiation()
        {
            EquipmentData invalid = UnityEngine.Object.Instantiate(equipmentDB.Equipments[0]);
            try
            {
                equipmentDB.Equipments = new[] { invalid, equipmentDB.Equipments[1], equipmentDB.Equipments[2] };
                var provider = new EquipmentModifierOfferProvider(new FixedRandom(0f));
                EquipmentModifierApplication application = invalid.Levels[0].Modifiers[0];
                foreach (EquipmentDataModifier definition in new[] {
                    new EquipmentDataModifier(),
                    new EquipmentDataModifier(new EquipmentModifierID(0x0100FFFFu), new DamageStatModifierParameters(0.3f)),
                    new EquipmentDataModifier(new EquipmentModifierID(DamageStatModifier.ModifierIdValue), new SpeedStatModifierParameters(0.3f)) })
                {
                    application.Configure(definition, "test", false, null);
                    Assert.Throws<InvalidOperationException>(() => provider.Generate(equipmentDB, weaponData));
                }
                Assert.That(build.InitialWeapon.NativeRuntime.ModifierCount, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(invalid); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SelectIndex_AppliesExactlyTheChosenCardOnce(int index)
        {
            selection.Bind(build);
            ModifierOffer offer = selection.Offers[index];
            var result = selection.Select(index);
            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(result.EquipmentHandle.IsValid, Is.True);
            Assert.That(build.EquipmentCount, Is.EqualTo(1));
            Assert.That(build.InitialWeapon.NativeRuntime.RuntimeModifiers.StaticModifiers
                .Select(modifier => modifier.ID.Value),
                Is.EqualTo(offer.Modifiers.Select(application => application.ModifierIdValue)));
            Assert.That(selection.Offers, Is.Empty);
            Assert.That(selection.Select(index).Succeeded, Is.False);
            Assert.That(selection.SelectOffer(offer.OfferId).Succeeded, Is.False);
            Assert.That(build.EquipmentCount, Is.EqualTo(1));
        }

        [Test]
        public void SelectOfferId_ChangesActualPipelineDamageAndHandleRemovesIt()
        {
            selection.Bind(build);
            ModifierOffer damage = selection.Offers.Single(offer => offer.EquipmentId == 2u);
            Assert.That(build.InitialWeapon.DamageValue, Is.EqualTo(15));
            ModifierSelectionResult result = selection.SelectOffer(damage.OfferId);
            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(build.InitialWeapon.DamageValue, Is.EqualTo(20));
            using (AttackSnapshot attack = build.InitialWeapon.NativeRuntime.BeginAttack(weaponData.AttackTags))
            {
                var target = new Target();
                CombatResolution resolution = build.InitialWeapon.NativeRuntime.ResolveHitDetailed(attack, target);
                Assert.That(resolution.ResolvedDamage.Value, Is.EqualTo(20));
                Assert.That(target.Health, Is.EqualTo(80));
            }
            selection.Unbind();
            Assert.That(build.EquipmentCount, Is.EqualTo(1), "Unbind must not remove applied equipment.");
            Assert.That(build.RemoveEquipment(result.EquipmentHandle), Is.True);
            Assert.That(build.InitialWeapon.DamageValue, Is.EqualTo(15));
        }

        [Test]
        public void CompositeKnockbackCard_AppliesBothNativeModifiers()
        {
            equipmentDB.Equipments = new[] { equipmentDB.Equipments.Last(), equipmentDB.Equipments[0], equipmentDB.Equipments[1] };
            selection.Bind(build);
            var offer = selection.Offers[0];
            float beforeSpeed = build.InitialWeapon.SpeedValue;
            Assert.That(offer.Modifiers.Count, Is.EqualTo(2));
            Assert.That(selection.SelectOffer(offer.OfferId).Succeeded, Is.True);
            Assert.That(build.InitialWeapon.NativeRuntime.ModifierCount, Is.EqualTo(2));
            Assert.That(build.InitialWeapon.NativeRuntime.RuntimeModifiers.StaticModifiers
                .Select(modifier => modifier.ID.Value), Is.EquivalentTo(new[] {
                    KnockbackStatModifier.ModifierIdValue, SpeedStatModifier.ModifierIdValue }));
            Assert.That(build.InitialWeapon.SpeedValue, Is.GreaterThan(beforeSpeed));
            using (AttackSnapshot attack = build.InitialWeapon.NativeRuntime.BeginAttack())
                Assert.That(attack.Stats.KnockbackDistance, Is.EqualTo(2f));
        }

        [Test]
        public void InvalidIndexAndFullSlot_DoNotConsumeOffersOrLeakModifiers()
        {
            selection.Bind(build);
            var offers = selection.Offers;
            Assert.That(selection.Select(-1).Succeeded, Is.False);
            Assert.That(selection.Select(3).Succeeded, Is.False);
            for (int i = 0; i < PlayerBuildRuntime.MaxEquipmentPerSlot; i++)
                build.AddEquipment(build.InitialWeapon, equipmentDB.Equipments[0], 0);
            Assert.That(selection.Select(0).Succeeded, Is.False);
            Assert.That(selection.Offers, Is.SameAs(offers));
            Assert.That(build.EquipmentCount, Is.EqualTo(3));
            Assert.That(build.InitialWeapon.NativeRuntime.ModifierCount, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator RebuildUnbindDisableAndDestroy_ExpireOldOffers()
        {
            selection.Bind(build);
            ulong oldId = selection.Offers[0].OfferId;
            build.StartInitialBuild(runtimeDB);
            Assert.That(selection.SelectOffer(oldId).Succeeded, Is.False);
            Assert.That(build.EquipmentCount, Is.Zero);
            Assert.That(selection.Offers.Count, Is.EqualTo(3));
            Assert.That(selection.Offers.Any(offer => offer.OfferId == oldId), Is.False);

            var result = selection.Select(0);
            Assert.That(result.Succeeded, Is.True);
            yield return null;
            Assert.That(selection.Offers, Is.Empty, "A consumed build must not regenerate.");
            selection.Unbind();
            selection.Bind(build);
            Assert.That(selection.Offers, Is.Empty, "Rebinding the same build must not grant a second round.");
            build.ClearBuild();
            yield return null;
            build.StartInitialBuild(runtimeDB);
            yield return null;
            Assert.That(selection.Offers.Count, Is.EqualTo(3));

            selection.enabled = false;
            Assert.That(selection.BoundBuild, Is.Null);
            Assert.That(selection.Offers, Is.Empty);
            Assert.That(selection.Select(0).Succeeded, Is.False);
            selection.enabled = true;
            selection.Bind(build);
            UnityEngine.Object.DestroyImmediate(playerObject);
            yield return null;
            Assert.That(selection.Offers, Is.Empty);
        }

        [Test]
        public void StopClient_WithoutStopAuthority_UnbindsAndClearsTheBuild()
        {
            var bootstrap = playerObject.AddComponent<NetworkPlayerBootstrap>();
            typeof(NetworkPlayerBootstrap).GetField("playerBuildRuntime", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(bootstrap, build);
            typeof(NetworkPlayerBootstrap).GetField("modifierSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(bootstrap, selection);
            selection.Bind(build);
            Assert.That(selection.Select(0).Succeeded, Is.True);
            bootstrap.OnStopClient();
            Assert.That(selection.BoundBuild, Is.Null);
            Assert.That(selection.Offers, Is.Empty);
            Assert.That(build.IsBuildActive, Is.False);
            Assert.That(build.EquipmentCount, Is.Zero);
            // Both callbacks can arrive; cleanup must remain idempotent.
            Assert.DoesNotThrow(() => bootstrap.OnStopAuthority());
        }

        [Test]
        public void TooFewOffers_ReportsOnceAndKeepsSelectionEmpty()
        {
            equipmentDB.Equipments = equipmentDB.Equipments.Take(2).ToArray();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Modifier selection:.*2 eligible cards"));
            selection.Bind(build);
            Assert.That(selection.Offers, Is.Empty);
            selection.Bind(build);
            Assert.That(selection.Select(0).Succeeded, Is.False);
            Assert.That(build.EquipmentCount, Is.Zero);
        }

        private static T Asset<T>(string path) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            T asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
#else
            throw new NotSupportedException("Asset integration tests require the Editor.");
#endif
        }

        private sealed class FixedRandom : IRandomSource
        {
            private readonly float value;
            public FixedRandom(float value) => this.value = value;
            public float Next01() => value;
        }

        private sealed class SeededRandom : IRandomSource
        {
            private readonly System.Random random;
            public SeededRandom(int seed) => random = new System.Random(seed);
            public float Next01() => (float)random.NextDouble();
        }

        private sealed class Target : ICombatTarget
        {
            public int Health { get; private set; } = 100;
            public bool IsAlive => Health > 0;
            public DamageInfo ReceiveDamage(DamageInfo damage) { Health -= damage.Value; return damage; }
            public StatusApplicationResult ApplyStatus(StatusApplication application) => StatusApplicationResult.Rejected;
        }
    }
}
