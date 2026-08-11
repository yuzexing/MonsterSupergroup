using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.GAS.Unity;
using NUnit.Framework;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class GasUnityAdapterTests
    {
        [Test]
        public void ProgrammaticAuthoring_LoadsEquipmentAndAppliesWeaponPerks()
        {
            var factory = new RuntimeModifierFactory(GeneratedModifierRegistry.Create());
            var runtimeModifiers = new RuntimeEquipmentModifiers();
            var equipment = new[]
            {
                new EquipmentDataModifier(
                    new EquipmentModifierID(DamageStatModifier.ModifierIdValue),
                    new DamageStatModifierParameters(0.5f)),
                new EquipmentDataModifier(
                    new EquipmentModifierID(OnHitBurnModifier.ModifierIdValue),
                    new OnHitBurnModifierParameters(1f, 0.5f, 2, 0.5f))
            };
            var perks = new[]
            {
                new PerkDataModifier(
                    new PerkModifierID(WeaponSpeedPerkModifier.ModifierIdValue),
                    new WeaponSpeedPerkModifierParameters(0.25f))
            };
            var perkMultipliers = new AttackStatsMultipliers();

            try
            {
                ModifierSetRuntimeLoader.LoadEquipment(equipment, factory, runtimeModifiers);
                ModifierSetRuntimeLoader.ApplyWeaponStatPerks(perks, factory, perkMultipliers);

                Assert.That(runtimeModifiers.Count, Is.EqualTo(2));
                Assert.That(runtimeModifiers.StaticModifiers, Has.Count.EqualTo(1));
                Assert.That(runtimeModifiers.OnHitModifiers, Has.Count.EqualTo(1));
                Assert.That(perkMultipliers.speed, Is.EqualTo(0.25f).Within(0.0001f));
            }
            finally
            {
                runtimeModifiers.Clear();
            }
        }

        [Test]
        public void NullModifierSets_AreEmptyButRequiredDependenciesAreStillValidated()
        {
            var factory = new RuntimeModifierFactory(GeneratedModifierRegistry.Create());
            var runtimeModifiers = new RuntimeEquipmentModifiers();
            var perkMultipliers = new AttackStatsMultipliers();

            ModifierSetRuntimeLoader.LoadEquipment(
                (EquipmentModifierSet)null,
                factory,
                runtimeModifiers);
            ModifierSetRuntimeLoader.ApplyWeaponStatPerks(
                (PerkModifierSet)null,
                factory,
                perkMultipliers);

            Assert.That(runtimeModifiers.Count, Is.Zero);
            Assert.Throws<ArgumentNullException>(() =>
                ModifierSetRuntimeLoader.LoadEquipment(
                    (EquipmentModifierSet)null,
                    null,
                    runtimeModifiers));
            Assert.Throws<ArgumentNullException>(() =>
                ModifierSetRuntimeLoader.ApplyWeaponStatPerks(
                    (PerkModifierSet)null,
                    factory,
                    null));
        }

        [Test]
        public void ProgrammaticAuthoring_RejectsInvalidIdentityAndParameters()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EquipmentDataModifier(
                    EquipmentModifierID.Invalid,
                    new DamageStatModifierParameters(0f)));
            Assert.Throws<ArgumentNullException>(() =>
                new EquipmentDataModifier(
                    new EquipmentModifierID(DamageStatModifier.ModifierIdValue),
                    null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PerkDataModifier(
                    PerkModifierID.Invalid,
                    new WeaponSpeedPerkModifierParameters(0f)));
        }

        [Test]
        public void UnityRandomSource_RespectsTheCoreHalfOpenRangeContract()
        {
            var random = new UnityRandomSource();

            for (int index = 0; index < 1000; index++)
            {
                float value = random.Next01();
                Assert.That(value, Is.GreaterThanOrEqualTo(0f));
                Assert.That(value, Is.LessThan(1f));
            }
        }

        [Test]
        public void SeededRandomSource_ReplaysTheSameSequenceForTheSameSeed()
        {
            var first = new SeededRandomSource(12345);
            var second = new SeededRandomSource(12345);

            for (int index = 0; index < 64; index++)
            {
                float firstValue = first.Next01();
                float secondValue = second.Next01();
                Assert.That(firstValue, Is.EqualTo(secondValue));
                Assert.That(firstValue, Is.GreaterThanOrEqualTo(0f));
                Assert.That(firstValue, Is.LessThan(1f));
            }
        }

        [Test]
        public void EquipmentLoadFailure_RollsBackOnlyTheCurrentBatch()
        {
            var factory = new RuntimeModifierFactory(GeneratedModifierRegistry.Create());
            var destination = new RuntimeEquipmentModifiers();
            destination.Add(factory.CreateEquipment(
                new EquipmentModifierID(DamageStatModifier.ModifierIdValue),
                new DamageStatModifierParameters(0.1f)));
            var batch = new[]
            {
                new EquipmentDataModifier(
                    new EquipmentModifierID(OnHitBurnModifier.ModifierIdValue),
                    new OnHitBurnModifierParameters(1f, 0.5f, 2, 0.5f)),
                new EquipmentDataModifier(
                    new EquipmentModifierID(0x7FFFFFFFu),
                    new DamageStatModifierParameters(0.2f))
            };

            try
            {
                Assert.Throws<KeyNotFoundException>(() =>
                    ModifierSetRuntimeLoader.LoadEquipment(batch, factory, destination));
                Assert.That(destination.Count, Is.EqualTo(1));
                Assert.That(destination.StaticModifiers, Has.Count.EqualTo(1));
                Assert.That(destination.OnHitModifiers, Is.Empty);
            }
            finally
            {
                destination.Clear();
            }
        }

        [Test]
        public void PerkLoadFailure_RestoresTheOriginalMultiplierSnapshot()
        {
            var factory = new RuntimeModifierFactory(GeneratedModifierRegistry.Create());
            var multipliers = new AttackStatsMultipliers { speed = 0.4f, damage = 0.2f };
            var batch = new[]
            {
                new PerkDataModifier(
                    new PerkModifierID(WeaponSpeedPerkModifier.ModifierIdValue),
                    new WeaponSpeedPerkModifierParameters(0.25f)),
                new PerkDataModifier(
                    new PerkModifierID(0x7FFFFFFFu),
                    new WeaponSpeedPerkModifierParameters(0.5f))
            };

            Assert.Throws<KeyNotFoundException>(() =>
                ModifierSetRuntimeLoader.ApplyWeaponStatPerks(batch, factory, multipliers));
            Assert.That(multipliers.speed, Is.EqualTo(0.4f));
            Assert.That(multipliers.damage, Is.EqualTo(0.2f));
        }
    }
}
