using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class RuntimeEquipmentModifiersTests
    {
        [Test]
        public void Add_ClassifiesEveryExecutionStage()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new TestStaticModifier(1));
            modifiers.Add(new TestDynamicModifier(2));
            modifiers.Add(new TestOnDamageModifier(3));
            modifiers.Add(new TestOnHitModifier(4));
            modifiers.Add(new TestOnKillModifier(5));

            Assert.That(modifiers.StaticModifiers.Count, Is.EqualTo(1));
            Assert.That(modifiers.DynamicModifiers.Count, Is.EqualTo(1));
            Assert.That(modifiers.DynamicOnDamageModifiers.Count, Is.EqualTo(1));
            Assert.That(modifiers.OnHitModifiers.Count, Is.EqualTo(1));
            Assert.That(modifiers.PredictedLethalHitModifiers.Count, Is.EqualTo(1));
            Assert.That(modifiers.OnKillModifiers.Count, Is.EqualTo(1));
            Assert.That(modifiers.Count, Is.EqualTo(5));
        }

        [Test]
        public void Add_SortsByStageThenRollPriorityThenInsertionOrder()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            var late = new TestOnHitModifier(1, rollPriority: 1f, sortPriority: 2);
            var lowerRoll = new TestOnHitModifier(2, rollPriority: 1f, sortPriority: 1);
            var higherRollFirst = new TestOnHitModifier(3, rollPriority: 3f, sortPriority: 1);
            var higherRollSecond = new TestOnHitModifier(4, rollPriority: 3f, sortPriority: 1);

            modifiers.Add(late);
            modifiers.Add(lowerRoll);
            modifiers.Add(higherRollFirst);
            modifiers.Add(higherRollSecond);

            Assert.That(modifiers.OnHitModifiers[0], Is.SameAs(higherRollFirst));
            Assert.That(modifiers.OnHitModifiers[1], Is.SameAs(higherRollSecond));
            Assert.That(modifiers.OnHitModifiers[2], Is.SameAs(lowerRoll));
            Assert.That(modifiers.OnHitModifiers[3], Is.SameAs(late));
        }

        [Test]
        public void Add_SortsOnKillByStageThenRollPriorityThenInsertionOrder()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            var late = new TestOnKillModifier(1, rollPriority: 1f, sortPriority: 2);
            var lowerRoll = new TestOnKillModifier(2, rollPriority: 1f, sortPriority: 1);
            var higherRollFirst = new TestOnKillModifier(3, rollPriority: 3f, sortPriority: 1);
            var higherRollSecond = new TestOnKillModifier(4, rollPriority: 3f, sortPriority: 1);

            modifiers.Add(late);
            modifiers.Add(lowerRoll);
            modifiers.Add(higherRollFirst);
            modifiers.Add(higherRollSecond);

            Assert.That(modifiers.OnKillModifiers[0], Is.SameAs(higherRollFirst));
            Assert.That(modifiers.OnKillModifiers[1], Is.SameAs(higherRollSecond));
            Assert.That(modifiers.OnKillModifiers[2], Is.SameAs(lowerRoll));
            Assert.That(modifiers.OnKillModifiers[3], Is.SameAs(late));
        }

        [Test]
        public void StandardStages_SortByPriorityThenInsertionOrder()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            var late = new TestStaticModifier(1, priority: 3);
            var firstTie = new TestStaticModifier(2, priority: 1);
            var secondTie = new TestStaticModifier(3, priority: 1);

            modifiers.Add(late);
            modifiers.Add(firstTie);
            modifiers.Add(secondTie);

            Assert.That(modifiers.StaticModifiers[0], Is.SameAs(firstTie));
            Assert.That(modifiers.StaticModifiers[1], Is.SameAs(secondTie));
            Assert.That(modifiers.StaticModifiers[2], Is.SameAs(late));
        }

        [Test]
        public void Remove_UsesHandleToDistinguishInstancesWithSameId()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            var first = new TestStaticModifier(7);
            var second = new TestStaticModifier(7);
            ModifierHandle firstHandle = modifiers.Add(first);
            ModifierHandle secondHandle = modifiers.Add(second);

            Assert.That(firstHandle, Is.Not.EqualTo(secondHandle));
            Assert.That(modifiers.Remove(firstHandle), Is.True);
            Assert.That(modifiers.StaticModifiers, Has.Count.EqualTo(1));
            Assert.That(modifiers.StaticModifiers[0], Is.SameAs(second));
            Assert.That(modifiers.Remove(firstHandle), Is.False);
            Assert.That(modifiers.Remove(secondHandle), Is.True);
        }

        [Test]
        public void Clear_RemovesAllStagesAndHandles()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            ModifierHandle handle = modifiers.Add(new TestStaticModifier(1));
            modifiers.Add(new TestOnHitModifier(2));

            modifiers.Clear();

            Assert.That(modifiers.HasModifiers, Is.False);
            Assert.That(modifiers.StaticModifiers, Is.Empty);
            Assert.That(modifiers.OnHitModifiers, Is.Empty);
            Assert.That(modifiers.Remove(handle), Is.False);
        }

        [Test]
        public void Container_DisposesOwnedInstancesExactlyOnceOnRemoveOrClear()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            var removed = new DisposableStaticModifier(1);
            var cleared = new DisposableStaticModifier(2);
            ModifierHandle removedHandle = modifiers.Add(removed);
            modifiers.Add(cleared);

            Assert.That(modifiers.Remove(removedHandle), Is.True);
            Assert.That(removed.DisposeCalls, Is.EqualTo(1));
            Assert.That(cleared.DisposeCalls, Is.Zero);

            modifiers.Clear();
            Assert.That(removed.DisposeCalls, Is.EqualTo(1));
            Assert.That(cleared.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public void Add_RejectsTheSameOwnedInstanceTwice()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            var modifier = new DisposableStaticModifier(1);
            modifiers.Add(modifier);

            Assert.Throws<InvalidOperationException>(() => modifiers.Add(modifier));

            modifiers.Clear();
            Assert.That(modifier.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public void StageLists_AreReadOnlyViews()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new TestStaticModifier(1));

            Assert.That(modifiers.StaticModifiers, Is.Not.InstanceOf<List<StaticStatModifier>>());
        }

        [Test]
        public void Add_RejectsNullAndUnsupportedStages()
        {
            var modifiers = new RuntimeEquipmentModifiers();

            Assert.Throws<ArgumentNullException>(() => modifiers.Add(null));
            Assert.Throws<ArgumentException>(() => modifiers.Add(new UnsupportedModifier(1)));
        }
    }
}
