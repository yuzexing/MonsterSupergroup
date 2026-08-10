using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class ModifierRegistryTests
    {
        [Test]
        public void StableIds_AreNonZeroAndPreserveTheirValue()
        {
            var equipment = new EquipmentModifierID(0x01020304u);
            var perk = new PerkModifierID(0x05060708u);

            Assert.That(equipment.IsValid, Is.True);
            Assert.That(equipment.Value, Is.EqualTo(0x01020304u));
            Assert.That(perk.IsValid, Is.True);
            Assert.That(perk.Value, Is.EqualTo(0x05060708u));
            Assert.That(EquipmentModifierID.Invalid.IsValid, Is.False);
            Assert.That(PerkModifierID.Invalid.IsValid, Is.False);
        }

        [Test]
        public void Builder_RejectsInvalidDuplicateIdAndDuplicateType()
        {
            var builder = new ModifierRegistry.Builder();
            Assert.Throws<ArgumentException>(() =>
                builder.RegisterEquipment<TestStaticModifier, TestEquipmentParameters>(
                    EquipmentModifierID.Invalid,
                    parameters => new TestStaticModifier(1, parameters.value)));

            builder.RegisterEquipment<TestStaticModifier, TestEquipmentParameters>(
                new EquipmentModifierID(1),
                parameters => new TestStaticModifier(1, parameters.value));

            Assert.Throws<InvalidOperationException>(() =>
                builder.RegisterEquipment<OtherStaticModifier, OtherEquipmentParameters>(
                    new EquipmentModifierID(1),
                    _ => new OtherStaticModifier(1)));
            Assert.Throws<InvalidOperationException>(() =>
                builder.RegisterEquipment<TestStaticModifier, TestEquipmentParameters>(
                    new EquipmentModifierID(2),
                    parameters => new TestStaticModifier(2, parameters.value)));
        }

        [Test]
        public void Build_OrdersRegistrationsByStableId()
        {
            ModifierRegistry registry = new ModifierRegistry.Builder()
                .RegisterEquipment<TestStaticModifier, TestEquipmentParameters>(
                    new EquipmentModifierID(9),
                    parameters => new TestStaticModifier(9, parameters.value))
                .RegisterEquipment<OtherStaticModifier, OtherEquipmentParameters>(
                    new EquipmentModifierID(2),
                    _ => new OtherStaticModifier(2))
                .Build();

            Assert.That(registry.EquipmentRegistrations[0].Id.Value, Is.EqualTo(2));
            Assert.That(registry.EquipmentRegistrations[1].Id.Value, Is.EqualTo(9));
            Assert.That(
                registry.EquipmentRegistrations,
                Is.Not.InstanceOf<ModifierRegistry.EquipmentRegistration[]>());
        }

        [Test]
        public void Factory_CreatesRegisteredTypesAndRejectsUnknownOrWrongParameters()
        {
            ModifierRegistry registry = new ModifierRegistry.Builder()
                .RegisterEquipment<TestStaticModifier, TestEquipmentParameters>(
                    new EquipmentModifierID(1),
                    parameters => new TestStaticModifier(1, parameters.value))
                .RegisterPerk<TestPerkModifier, TestPerkParameters>(
                    new PerkModifierID(2),
                    parameters => new TestPerkModifier(2, parameters.value))
                .Build();
            var factory = new RuntimeModifierFactory(registry);

            RuntimeEquipmentModifier equipment = factory.CreateEquipment(
                new EquipmentModifierID(1),
                new TestEquipmentParameters { value = 0.5f });
            RuntimePerkModifier perk = factory.CreatePerk(
                new PerkModifierID(2),
                new TestPerkParameters { value = 0.25f });

            Assert.That(equipment, Is.TypeOf<TestStaticModifier>());
            Assert.That(perk, Is.TypeOf<TestPerkModifier>());
            Assert.Throws<KeyNotFoundException>(() =>
                factory.CreateEquipment(new EquipmentModifierID(99), new TestEquipmentParameters()));
            Assert.Throws<ArgumentException>(() =>
                factory.CreateEquipment(new EquipmentModifierID(1), new OtherEquipmentParameters()));
            Assert.Throws<ArgumentException>(() =>
                factory.CreateEquipment(new EquipmentModifierID(1), null));
        }

        [Test]
        public void GeneratedRegistry_CreatesAllFirstSliceModifiers()
        {
            var factory = new RuntimeModifierFactory(GeneratedModifierRegistry.Create());

            Assert.That(
                factory.CreateEquipment(
                    new EquipmentModifierID(DamageStatModifier.ModifierIdValue),
                    new DamageStatModifierParameters(0.2f)),
                Is.TypeOf<DamageStatModifier>());
            Assert.That(
                factory.CreateEquipment(
                    new EquipmentModifierID(OnHitBurnModifier.ModifierIdValue),
                    new OnHitBurnModifierParameters(1f, 0.5f, 3, 1f)),
                Is.TypeOf<OnHitBurnModifier>());
            Assert.That(
                factory.CreatePerk(
                    new PerkModifierID(WeaponSpeedPerkModifier.ModifierIdValue),
                    new WeaponSpeedPerkModifierParameters(0.15f)),
                Is.TypeOf<WeaponSpeedPerkModifier>());
        }
    }
}
