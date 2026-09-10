using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class ExperienceRulesTests
    {
        private static ExperienceParameters Rules()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameplayExperienceRules>("Assets/_Project/Content/NetworkCombat/GameplayExperienceRules.asset");
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.TryCapture(out var rules, out string error), Is.True, error);
            return rules;
        }
        [TestCase(1, 19)] [TestCase(2, 39)] [TestCase(3, 60)] [TestCase(4, 81)]
        [TestCase(7, 171)] [TestCase(12, 305)] [TestCase(18, 374)] [TestCase(50, 596)]
        [TestCase(99, 999)] [TestCase(100, 1000)] [TestCase(1000, 1000)]
        public void AuthoredCurveThresholds(int level, int expected) => Assert.That(Rules().Threshold(level), Is.EqualTo(expected));
        [Test]
        public void LargeGrantPreservesAllLevelsAndFractionalRemainder_AndDebugThresholdIsExactlyOneLevel()
        {
            var rules = Rules();
            float total = Enumerable.Range(1, 110).Sum(rules.Threshold);
            Assert.That(rules.TryAdvance(1, .5f, total, out int level, out float xp), Is.True);
            Assert.That(level, Is.EqualTo(111)); Assert.That(xp, Is.EqualTo(.5f));
            for (int i = 1; i < 150; i++)
            {
                Assert.That(rules.TryAdvance(i, .5f, rules.Threshold(i), out level, out xp), Is.True);
                Assert.That(level, Is.EqualTo(i + 1)); Assert.That(xp, Is.EqualTo(.5f));
            }
            Assert.That(rules.TryAdvance(1, 0, float.NaN, out _, out _), Is.False);
            Assert.That(rules.TryAdvance(int.MaxValue, 0, 1000, out _, out _), Is.False);
        }
        [Test]
        public void OneGlobalAccumulator_FirstFourThenAlternate_DeduplicatesDeathsNotSharedAttackCauses()
        {
            var schedule = new ExperienceDropSchedule(Rules());
            var results = Enumerable.Range(1, 10).Select(i => schedule.ConsumeDeath((uint)i, 2, 2, out _)).ToArray();
            Assert.That(results, Is.EqualTo(new[] { true, true, true, true, false, true, false, true, false, true }));
            float before = schedule.Accumulator;
            Assert.That(schedule.ConsumeDeath(1, 2, 2, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("duplicate-death")); Assert.That(schedule.Accumulator, Is.EqualTo(before));
            var fresh = new ExperienceDropSchedule(Rules());
            Assert.That(fresh.ConsumeDeath(1, 2, 0, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("nonpositive-xp")); Assert.That(fresh.Accumulator, Is.EqualTo(1.5f));
            Assert.That(fresh.ConsumeDeath(2, 2, 2, out _), Is.True);
        }
        [Test]
        public void CapturedCurveIgnoresAssetMutation_AndInvalidConfigurationFails()
        {
            var curve = AnimationCurve.Linear(0, 0, 1, 1);
            var rules = new ExperienceParameters(curve, 2, .5f);
            curve.keys = AnimationCurve.Linear(0, 0, 1, 2).keys;
            Assert.That(rules.Threshold(50), Is.EqualTo(500));
            Assert.Throws<ArgumentException>(() => new ExperienceParameters(null, 2, .5f));
            Assert.Throws<ArgumentException>(() => new ExperienceParameters(AnimationCurve.Constant(0, 1, 0), 2, .5f));
            Assert.Throws<ArgumentException>(() => new ExperienceParameters(curve, float.NaN, .5f));
        }
    }
}
