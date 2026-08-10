using System.Collections.Generic;
using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class StatusControllerTests
    {
        private static readonly StatusDefinition Burn =
            new StatusDefinition(EnemyStatusID.Burn, StatusStackMode.HighestPriority, 1);

        [Test]
        public void Apply_RejectsDefaultApplicationWithoutMutatingState()
        {
            var controller = new StatusController(_ => { });

            Assert.Throws<System.ArgumentException>(() => controller.Apply(default));
            Assert.That(controller.Count, Is.Zero);
            Assert.That(controller.Has(EnemyStatusID.None), Is.False);
        }

        [Test]
        public void Definition_RejectsUnknownStackMode()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new StatusDefinition(EnemyStatusID.Burn, (StatusStackMode)99, 1));
        }

        [Test]
        public void HighestPriority_RejectsLowerRefreshesEqualAndReplacesHigher()
        {
            var controller = new StatusController(_ => { });

            Assert.That(controller.Apply(Application(Burn, priority: 10f)), Is.EqualTo(StatusApplicationResult.Added));
            Assert.That(controller.Apply(Application(Burn, priority: 9f)), Is.EqualTo(StatusApplicationResult.Rejected));
            Assert.That(controller.Apply(Application(Burn, priority: 10f)), Is.EqualTo(StatusApplicationResult.Refreshed));
            Assert.That(controller.Apply(Application(Burn, priority: 11f)), Is.EqualTo(StatusApplicationResult.Replaced));
            Assert.That(controller.GetStackCount(EnemyStatusID.Burn), Is.EqualTo(1));
        }

        [Test]
        public void HighestPriority_EqualPriorityRefreshRestartsLifecycle()
        {
            var ticks = new List<StatusTick>();
            var controller = new StatusController(ticks.Add);
            controller.Apply(Application(Burn, damage: 1, hits: 1, interval: 1f, priority: 10f));
            controller.Advance(0.75f);

            Assert.That(
                controller.Apply(Application(Burn, damage: 2, hits: 1, interval: 1f, priority: 10f)),
                Is.EqualTo(StatusApplicationResult.Refreshed));
            controller.Advance(0.5f);
            Assert.That(ticks, Is.Empty);

            controller.Advance(0.5f);
            Assert.That(ticks, Has.Count.EqualTo(1));
            Assert.That(ticks[0].Damage.Value, Is.EqualTo(2));
        }

        [Test]
        public void HighestPriority_HigherPriorityReplacementRestartsLifecycle()
        {
            var ticks = new List<StatusTick>();
            var controller = new StatusController(ticks.Add);
            controller.Apply(Application(Burn, damage: 1, hits: 1, interval: 1f, priority: 10f));
            controller.Advance(0.75f);

            Assert.That(
                controller.Apply(Application(Burn, damage: 3, hits: 1, interval: 1f, priority: 11f)),
                Is.EqualTo(StatusApplicationResult.Replaced));
            controller.Advance(0.5f);
            Assert.That(ticks, Is.Empty);

            controller.Advance(0.5f);
            Assert.That(ticks, Has.Count.EqualTo(1));
            Assert.That(ticks[0].Damage.Value, Is.EqualTo(3));
        }

        [Test]
        public void Add_StopsAtMaximumStackCount()
        {
            var definition = new StatusDefinition(EnemyStatusID.Burn, StatusStackMode.Add, 2);
            var controller = new StatusController(_ => { });

            Assert.That(controller.Apply(Application(definition)), Is.EqualTo(StatusApplicationResult.Added));
            Assert.That(controller.Apply(Application(definition)), Is.EqualTo(StatusApplicationResult.Added));
            Assert.That(controller.Apply(Application(definition)), Is.EqualTo(StatusApplicationResult.Rejected));
            Assert.That(controller.Count, Is.EqualTo(2));
        }

        [Test]
        public void Replace_RestartsStatusWithOneStack()
        {
            var definition = new StatusDefinition(EnemyStatusID.Burn, StatusStackMode.Replace, 3);
            var ticks = new List<StatusTick>();
            var controller = new StatusController(ticks.Add);

            controller.Apply(Application(definition, hits: 1, interval: 1f));
            controller.Advance(0.75f);
            Assert.That(controller.Apply(Application(definition, hits: 2, interval: 1f)), Is.EqualTo(StatusApplicationResult.Replaced));
            controller.Advance(0.5f);

            Assert.That(ticks, Is.Empty);
            Assert.That(controller.Count, Is.EqualTo(1));
        }

        [Test]
        public void Advance_LargeDeltaDispatchesEveryTickAndExpires()
        {
            var ticks = new List<StatusTick>();
            var controller = new StatusController(ticks.Add);
            controller.Apply(Application(Burn, damage: 7, hits: 3, interval: 0.5f, sourceId: 42));

            controller.Advance(2f);

            Assert.That(ticks.Count, Is.EqualTo(3));
            Assert.That(ticks[0].TickIndex, Is.EqualTo(1));
            Assert.That(ticks[2].TickIndex, Is.EqualTo(3));
            Assert.That(ticks[2].IsFinalTick, Is.True);
            Assert.That(ticks[0].Damage, Is.EqualTo(new DamageInfo(42, 7, false)));
            Assert.That(controller.Has(EnemyStatusID.Burn), Is.False);
        }

        [Test]
        public void Advance_ManySmallStepsMatchesOneLargeStep()
        {
            var smallTicks = new List<StatusTick>();
            var largeTicks = new List<StatusTick>();
            var small = new StatusController(smallTicks.Add);
            var large = new StatusController(largeTicks.Add);
            StatusApplication application = Application(Burn, hits: 4, interval: 0.25f);
            small.Apply(application);
            large.Apply(application);

            for (int i = 0; i < 10; i++)
            {
                small.Advance(0.1f);
            }

            large.Advance(1f);

            Assert.That(smallTicks, Is.EqualTo(largeTicks));
            Assert.That(small.Count, Is.EqualTo(large.Count));
        }

        [Test]
        public void ConsumeAndClear_RemoveOnlyRequestedStatuses()
        {
            var definition = new StatusDefinition(EnemyStatusID.Burn, StatusStackMode.Add, 2);
            var controller = new StatusController(_ => { });
            controller.Apply(Application(definition));
            controller.Apply(Application(definition));

            Assert.That(controller.Consume(EnemyStatusID.Burn), Is.True);
            Assert.That(controller.GetStackCount(EnemyStatusID.Burn), Is.EqualTo(1));
            Assert.That(controller.Clear(EnemyStatusID.Burn), Is.True);
            Assert.That(controller.Consume(EnemyStatusID.Burn), Is.False);

            controller.Apply(Application(definition));
            controller.Clear();
            Assert.That(controller.Count, Is.Zero);
        }

        private static StatusApplication Application(
            StatusDefinition definition,
            int damage = 1,
            int hits = 2,
            float interval = 1f,
            float priority = 1f,
            uint sourceId = 0)
        {
            return new StatusApplication(definition, damage, hits, interval, priority, sourceId);
        }
    }
}
