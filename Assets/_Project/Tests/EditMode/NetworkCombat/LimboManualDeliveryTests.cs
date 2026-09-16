using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LimboManualDeliveryTests
    {
        [Test] public void SelectionIntervalStopsWhenAvatarIsRemovedAndKeepsLastRunSnapshot()
        {
            var go = new GameObject("manual-observation-test");
            var observer = go.AddComponent<LimboReferenceLaunch>();
            var stream = new MemoryStream();
            var log = new LimboObservationLog(stream, () => 0);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(string name, object value) => typeof(LimboReferenceLaunch).GetField(name, flags).SetValue(observer, value);
            void Call(string name, params object[] values) => typeof(LimboReferenceLaunch).GetMethod(name, flags).Invoke(observer, values);
            try
            {
                Set("audit", log); Set("wasSelecting", true); Set("selectStarted", Time.realtimeSinceStartupAsDouble - 10);
                Set("deliveryRun", "exited-run"); Set("lastDeliveryProgress", new WaveProgressSnapshot { RunId = "exited-run", Elapsed = 125.5 });
                Call("ObserveDelivery");
                Assert.That(typeof(LimboReferenceLaunch).GetField("wasSelecting", flags).GetValue(observer), Is.False);
                Call("FinishDeliveryObservation", "process-exit");
                log.Flush();
                string[] lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n');
                Assert.That(lines.Count(l => l.Contains("\"kind\":\"selection-interrupted\"")), Is.EqualTo(1));
                Assert.That(lines.First(l => l.Contains("\"kind\":\"selection-interrupted\"")), Does.Contain("local-avatar-unavailable"));
                Assert.That(lines.First(l => l.Contains("\"kind\":\"run-ended\"")), Does.Contain("125.5"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); log.Dispose(); }
        }

        [Test] public void RepeatedHealthBaselineIsNotRecordedAsEffectiveHealthChange()
        {
            var go = new GameObject("manual-health-test");
            var observer = go.AddComponent<LimboReferenceLaunch>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(LimboReferenceLaunch).GetField("previousHealth", flags).SetValue(observer, 500);
                typeof(LimboReferenceLaunch).GetField("previousMaximum", flags).SetValue(observer, 500);
                Assert.DoesNotThrow(() => typeof(LimboReferenceLaunch).GetMethod("RecordHealth", flags).Invoke(observer, new object[] {500, 500}));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        private static string[] Manual => new[] { "--limbo-role=host", "--limbo-manual=true", "--limbo-profile=full", "--limbo-log-detail=light",
            "--limbo-wait-for=1", "--limbo-windowed=true", "--limbo-output=C:/Manual Play/Runs/one" };

        [Test] public void ManualFullAllowsOnlyExplicitPassiveOptions() => Assert.That(LimboManualOptions.Validate(Manual), Is.Null);
        [TestCase("--limbo-autowalk=true")]
        [TestCase("--limbo-art-observe=true")]
        [TestCase("--limbo-full-case=busy")]
        [TestCase("--limbo-unknown-helper=true")]
        public void ManualRejectsAuxiliaryOptions(string option) => Assert.That(LimboManualOptions.Validate(Manual.Append(option).ToArray()), Is.Not.Null);
        [TestCase("--limbo-profile=full", "--limbo-profile=full-validation")]
        [TestCase("--limbo-log-detail=light", "--limbo-log-detail=detailed")]
        [TestCase("--limbo-wait-for=1", "--limbo-wait-for=4")]
        public void ManualRejectsIncompatibleDefaults(string from, string to) => Assert.That(LimboManualOptions.Validate(Manual.Select(a => a == from ? to : a).ToArray()), Is.Not.Null);
        [Test] public void ManualRejectsDuplicateRole() => Assert.That(LimboManualOptions.Validate(Manual.Append("--limbo-role=client").ToArray()), Is.Not.Null);
        [Test] public void OldTechnicalEntryStillAllowsItsExplicitInputs() => Assert.That(LimboManualOptions.Validate(new[] {
            "--limbo-role=host", "--limbo-profile=full-validation", "--limbo-autowalk=true" }), Is.Null);
        [Test] public void InvalidLogModeIsNeverSilentlyAccepted() => Assert.That(LimboManualOptions.Validate(new[] { "--limbo-log-detail=ligth" }), Is.Not.Null);

        [Test] public void BufferedEventsFlushAtUnscaledDeadlineAndWhileNoEventsArrive()
        {
            double clock = 10; var stream = new MemoryStream();
            using var log = new LimboObservationLog(stream, () => clock);
            log.WriteLine("birth"); Assert.That(stream.Length, Is.Zero);
            clock = 10.99; log.FlushIfDue(); Assert.That(stream.Length, Is.Zero);
            clock = 11; LimboObservationLog.FlushDue();
            Assert.That(System.Threading.SpinWait.SpinUntil(() => stream.Length > 0, 2000), Is.True);
            log.Flush(); Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Does.Contain("birth"));
            long firstLength = stream.Length;
            log.WriteLine("death"); clock = 12; LimboObservationLog.FlushDue();
            Assert.That(System.Threading.SpinWait.SpinUntil(() => stream.Length > firstLength, 2000), Is.True);
            log.Flush(); Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Does.Contain("death"));
        }
        [Test] public void EndFlushAndDisposePreserveTailWithoutWaitingOneSecond()
        {
            var stream = new MemoryStream(); int before = LimboObservationLog.OpenCount;
            var log = new LimboObservationLog(stream, () => 0);
            log.WriteLine("failed-not-completed"); LimboObservationLog.FlushAll();
            Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Does.Contain("failed-not-completed"));
            log.WriteLine("process-closed"); log.Dispose(); log.Dispose();
            Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Does.Contain("process-closed"));
            Assert.That(LimboObservationLog.OpenCount, Is.EqualTo(before));
            Assert.Throws<ObjectDisposedException>(() => log.WriteLine("late"));
        }
        [Test] public void BoundedBufferWritesThroughWithoutAnUnboundedQueue()
        {
            var stream = new MemoryStream();
            using var log = new LimboObservationLog(stream, () => 0);
            for (int i = 0; i < 100; i++) log.WriteLine(new string('x', 1024));
            Assert.That(LimboObservationLog.PendingBytes, Is.LessThanOrEqualTo(LimboObservationLog.MaximumPendingBytes));
            log.Flush();
            Assert.That(stream.Length, Is.EqualTo(100 * (1024 + Environment.NewLine.Length)), "Explicit drain preserves every queued event.");
        }
        [Test] public void ExistingRunEvidenceCannotBeTruncated()
        {
            string path = Path.Combine(Path.GetTempPath(), "limbo-log-" + Guid.NewGuid() + ".jsonl");
            try
            {
                File.WriteAllText(path, "old evidence");
                Assert.Throws<IOException>(() => new LimboObservationLog(path));
                Assert.That(File.ReadAllText(path), Is.EqualTo("old evidence"));
            }
            finally { File.Delete(path); }
        }
    }
}
