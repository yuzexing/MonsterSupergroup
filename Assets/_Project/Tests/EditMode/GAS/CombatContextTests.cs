using System.Collections.Generic;
using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class CombatContextTests
    {
        [Test]
        public void CombatEventId_PacksSourceEpochAndSequenceWithoutLosingIdentity()
        {
            CombatEventId id = CombatEventId.Compose(17, 23, 987654u);

            Assert.That(id.IsValid, Is.True);
            Assert.That(id.SourceSlot, Is.EqualTo(17));
            Assert.That(id.ConnectionEpoch, Is.EqualTo(23));
            Assert.That(id.Sequence, Is.EqualTo(987654u));
            Assert.That(new CombatEventId(id.Value), Is.EqualTo(id));
        }

        [Test]
        public void ResolveHitDetailed_PreservesRawOverkillAndExplicitEventLineage()
        {
            var modifiers = new RuntimeEquipmentModifiers();
            var legacyLethal = new CapturingLegacyOnKillModifier(42);
            modifiers.Add(legacyLethal);
            var sink = new RecordingCombatEventSink();
            var pipeline = new CombatPipeline(
                modifiers,
                new SequenceRandom(0f),
                new SequentialCombatEventIdSource(2, 3, 100),
                sink);
            TestWeapon weapon = Weapon(damage: 10);
            AttackSnapshot attack = pipeline.BeginAttack(weapon);

            CombatResolution result = pipeline.ResolveHitDetailed(
                attack,
                new TestTarget(5));

            Assert.That(result.ResolvedDamage.Value, Is.EqualTo(10));
            Assert.That(result.PredictedAppliedDamage.Value, Is.EqualTo(5));
            Assert.That(result.IsPredictedLethal, Is.True);
            Assert.That(sink.Events.ConvertAll(item => item.Kind), Is.EqualTo(new[]
            {
                CombatEventKind.AttackStarted,
                CombatEventKind.HitResolved,
                CombatEventKind.DamageResolved,
                CombatEventKind.PredictedLethalHit
            }));
            Assert.That(sink.Events, Has.None.Matches<CombatEvent>(
                item => item.Kind == CombatEventKind.ConfirmedKill));

            CombatContext root = sink.Events[0].Context;
            CombatContext hit = sink.Events[1].Context;
            CombatContext damage = sink.Events[2].Context;
            CombatContext lethal = sink.Events[3].Context;
            Assert.That(hit.RootEventId, Is.EqualTo(root.EventId));
            Assert.That(hit.ParentEventId, Is.EqualTo(root.EventId));
            Assert.That(damage.ParentEventId, Is.EqualTo(hit.EventId));
            Assert.That(lethal.ParentEventId, Is.EqualTo(damage.EventId));
            Assert.That(lethal.ChainDepth, Is.EqualTo(3));
            Assert.That((damage.Tags & CombatTags.Damage) != 0, Is.True);
            Assert.That((lethal.Tags & CombatTags.PredictedLethalHit) != 0, Is.True);

            Assert.That(legacyLethal.Calls, Is.EqualTo(1));
            Assert.That(legacyLethal.LastArgs.DamageInfo.Value, Is.EqualTo(5));
            Assert.That(legacyLethal.LastArgs.ResolvedDamageInfo.Value, Is.EqualTo(10));
            Assert.That(legacyLethal.LastArgs.Context.BuildId, Is.EqualTo(42u));
        }

        [Test]
        public void TriggerGuard_EnforcesSelfRootTargetDepthCountAndCooldownPolicies()
        {
            CombatContext root = CombatContext.CreateRoot(
                CombatEventId.Compose(1, 1, 1),
                1,
                10,
                20);
            var trigger = new EquipmentModifierID(7);

            var guard = new CombatTriggerGuard(new CombatChainLimits(2, 2));
            Assert.That(
                guard.TryEnter(root.WithBuild(7), trigger, 100, BuildTriggerPolicy.Default, 0),
                Is.False,
                "Default policy must block a build from directly triggering itself.");

            var oncePerTarget = new BuildTriggerPolicy(false, false, true);
            Assert.That(guard.TryEnter(root, trigger, 100, oncePerTarget, 0), Is.True);
            Assert.That(guard.TryEnter(root, trigger, 100, oncePerTarget, 0), Is.False);
            Assert.That(guard.TryEnter(root, trigger, 101, oncePerTarget, 0), Is.True);
            Assert.That(
                guard.TryEnter(root, new EquipmentModifierID(8), 102, BuildTriggerPolicy.Default, 0),
                Is.False,
                "The per-root trigger budget must be bounded.");

            CombatContext depthOne = root.CreateChild(
                CombatEventId.Compose(1, 1, 2),
                CombatTags.Build);
            CombatContext depthTwo = depthOne.CreateChild(
                CombatEventId.Compose(1, 1, 3),
                CombatTags.Build);
            CombatContext depthThree = depthTwo.CreateChild(
                CombatEventId.Compose(1, 1, 4),
                CombatTags.Build);
            var depthGuard = new CombatTriggerGuard(new CombatChainLimits(2, 10));
            Assert.That(
                depthGuard.TryEnter(depthTwo, trigger, 1, BuildTriggerPolicy.Default, 0),
                Is.True);
            Assert.That(
                depthGuard.TryEnter(depthThree, new EquipmentModifierID(8), 1, BuildTriggerPolicy.Default, 0),
                Is.False);

            var cooldownGuard = new CombatTriggerGuard();
            var cooldown = new BuildTriggerPolicy(false, false, false, 1f);
            Assert.That(cooldownGuard.TryEnter(root, trigger, 100, cooldown, 5), Is.True);
            cooldownGuard.ReleaseRoot(root.RootEventId);
            CombatContext nextRoot = CombatContext.CreateRoot(
                CombatEventId.Compose(1, 1, 10),
                1,
                10,
                20);
            Assert.That(cooldownGuard.TryEnter(nextRoot, trigger, 100, cooldown, 5.5), Is.False);
            Assert.That(cooldownGuard.TryEnter(nextRoot, trigger, 100, cooldown, 6), Is.True);
        }

        [Test]
        public void CombatPipeline_ActuallyAppliesDepthAndSelfTriggerGuardToModifiers()
        {
            var selfModifiers = new RuntimeEquipmentModifiers();
            var self = new CountingOnHitModifier(90);
            selfModifiers.Add(self);
            var selfIds = new SequentialCombatEventIdSource(1, 1);
            var selfPipeline = new CombatPipeline(
                selfModifiers,
                new SequenceRandom(0f),
                selfIds);
            CombatContext selfContext = CombatContext.CreateRoot(
                selfIds.Next(), 1, 10, 77).WithBuild(90);

            selfPipeline.ResolveHitDetailed(
                selfPipeline.BeginAttack(Weapon(1), selfContext),
                new TestTarget(10));

            Assert.That(self.Calls, Is.Zero,
                "A modifier must not directly retrigger itself through a child context.");

            var deepModifiers = new RuntimeEquipmentModifiers();
            var deep = new CountingOnHitModifier(91);
            deepModifiers.Add(deep);
            var deepIds = new SequentialCombatEventIdSource(2, 1);
            var deepPipeline = new CombatPipeline(
                deepModifiers,
                new SequenceRandom(0f),
                deepIds);
            CombatContext deepContext = CombatContext.CreateRoot(
                deepIds.Next(), 1, 10, 77);
            for (int i = 0; i < 32; i++)
            {
                deepContext = deepContext.CreateChild(deepIds.Next(), CombatTags.Build);
            }

            deepPipeline.ResolveHitDetailed(
                deepPipeline.BeginAttack(Weapon(1), deepContext),
                new TestTarget(10));

            Assert.That(deep.Calls, Is.Zero,
                "Modifier gameplay must stop beyond MaxChainDepth while base damage can resolve.");
        }

        private static TestWeapon Weapon(int damage)
        {
            return new TestWeapon(77, new WeaponBehaviourStats(new AttackStats
            {
                damage = damage,
                speed = 1f,
                size = 1f,
                duration = 1f,
                projectileCount = 1,
                critMultiplier = 1f,
                knockbackDistance = 1f
            }));
        }

        private sealed class RecordingCombatEventSink : ICombatEventSink
        {
            public List<CombatEvent> Events { get; } = new List<CombatEvent>();

            public void Publish(CombatEvent combatEvent)
            {
                Events.Add(combatEvent);
            }
        }

        private sealed class CapturingLegacyOnKillModifier : OnKillModifier
        {
            public CapturingLegacyOnKillModifier(uint id)
                : base(new EquipmentModifierID(id), new TestEquipmentParameters())
            {
            }

            public int Calls { get; private set; }
            public OnKillModifierArgs LastArgs { get; private set; }

            public override float GetRollChance() => 1f;
            public override float GetRollPriority() => 0f;

            protected override void ApplyEffect(OnKillModifierArgs args)
            {
                Calls++;
                LastArgs = args;
            }
        }

        private sealed class CountingOnHitModifier : OnHitModifier
        {
            public CountingOnHitModifier(uint id)
                : base(new EquipmentModifierID(id), new TestEquipmentParameters())
            {
            }

            public int Calls { get; private set; }
            public override float GetRollChance() => 1f;
            public override float GetRollPriority() => 0f;

            protected override void ApplyEffect(OnHitModifierArgs args)
            {
                Calls++;
            }
        }
    }
}
