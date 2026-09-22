using AstralShift.DebugTools;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class NetworkDiagnosticsWindowTests
    {
        [Test]
        public void LongFramesAreBoundedButAllFramesContributeToStatistics()
        {
            var window = new NetworkDiagnosticsWindow();
            for (int i = 0; i < 20; i++) Assert.That(window.Add(150), Is.EqualTo(i < 8));
            Assert.That(window.Add(100), Is.False);
            window.Sort();
            Assert.That(window.Count, Is.EqualTo(21));
            Assert.That(window.LongFrames, Is.EqualTo(20));
            Assert.That(window.LongDetails, Is.EqualTo(8));
            Assert.That(window.Percentile(.95), Is.EqualTo(150));
            Assert.That(window.Histogram[5], Is.EqualTo(20));
            window.Reset();
            Assert.That(window.Count, Is.Zero);
            Assert.That(window.Sum, Is.Zero);
            Assert.That(window.LongFrames, Is.Zero);
            Assert.That(window.Histogram, Is.All.Zero);
        }
        [Test]
        public void FrameOverflowIsExplicitWithoutLosingMeanAndMaximum()
        {
            var window = new NetworkDiagnosticsWindow();
            for (int i = 0; i < 5000; i++) window.Add(2);
            window.Add(400);
            Assert.That(window.Overflow, Is.EqualTo(905));
            Assert.That(window.Maximum, Is.EqualTo(400));
            Assert.That(window.Mean, Is.EqualTo(10400d / 5001).Within(.0001));
        }
        [Test]
        public void DisabledScopesDoNotCountAndEnabledScopesResetExactlyOnce()
        {
            bool prior = CombatPerformanceCounters.Enabled;
            try
            {
                CombatPerformanceCounters.Reset(); CombatPerformanceCounters.Enabled = false;
                using (CombatPerformanceCounters.Measure(CombatPerformanceCounters.Area.DamageRequests)) { CombatPerformanceCounters.DeadDamageRequest(); }
                Assert.That(CombatPerformanceCounters.ReadAndReset().calls, Is.All.Zero);
                CombatPerformanceCounters.Enabled = true;
                using (CombatPerformanceCounters.Measure(CombatPerformanceCounters.Area.DamageRequests)) { CombatPerformanceCounters.DeadDamageRequest(); }
                var sample = CombatPerformanceCounters.ReadAndReset();
                Assert.That(sample.calls[1], Is.EqualTo(1));
                Assert.That(sample.deadDamageRequests, Is.EqualTo(1));
                Assert.That(sample.milliseconds[1], Is.GreaterThanOrEqualTo(0));
                Assert.That(CombatPerformanceCounters.ReadAndReset().deadDamageRequests, Is.Zero);
            }
            finally { CombatPerformanceCounters.Reset(); CombatPerformanceCounters.Enabled = prior; }
        }
    }
}
