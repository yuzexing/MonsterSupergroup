using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceProfileTests
    {
        [TestCase(false)] [TestCase(true)]
        public void DiagnosticProfileIsExplicitAndObservationRemainsOptIn(bool observe)
        {
            string[] arguments = observe
                ? new[] { "--combat-evidence-profile=diagnostic", "--combat-evidence-observe-queue" }
                : new[] { "--combat-evidence-profile=diagnostic" };
            var diagnostic = Parse(arguments);
            Assert.That(diagnostic.Profile, Is.EqualTo(EvidenceProfile.Diagnostic));
            Assert.That(diagnostic.QueueBytes, Is.EqualTo(512 << 20));
            Assert.That(diagnostic.ReservedBytes, Is.EqualTo(4 << 20));
            Assert.That(diagnostic.Memory.Limit, Is.EqualTo(768L << 20));
            Assert.That(diagnostic.Memory.Used, Is.Zero, "The limit is not a preallocation.");
            Assert.That(diagnostic.ObservationWindowMilliseconds, Is.EqualTo(1000));
            Assert.That(diagnostic.ObserveQueue, Is.EqualTo(observe));
            Assert.That(diagnostic.DeferObservationWindows, Is.EqualTo(observe));
            foreach (var standard in new[] { Parse(null), Parse(Array.Empty<string>()), Parse(new[] { "--combat-evidence-profile=standard" }) })
            {
                Assert.That(standard.Profile, Is.EqualTo(EvidenceProfile.Standard));
                Assert.That(standard.QueueBytes, Is.EqualTo(32 << 20));
                Assert.That(standard.ReservedBytes, Is.EqualTo(4 << 20));
                Assert.That(standard.Memory.Limit, Is.EqualTo(128L << 20));
                Assert.That(standard.ObservationWindowMilliseconds, Is.EqualTo(100));
                Assert.That(standard.SessionBytes, Is.EqualTo(diagnostic.SessionBytes));
                Assert.That(standard.TotalBytes, Is.EqualTo(diagnostic.TotalBytes));
            }
        }

        [TestCase("--combat-evidence-profile=")]
        [TestCase("--combat-evidence-profile=typo")]
        public void InvalidProfileCannotSilentlyRunWithTheSmallBudget(string argument)
        {
            var error = Assert.Throws<TargetInvocationException>(() => Parse(new[] { argument }));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
        }

        [Test] public void ConflictingProfilesAreRejected()
        {
            var error = Assert.Throws<TargetInvocationException>(() => Parse(new[] {
                "--combat-evidence-profile=standard", "--combat-evidence-profile=diagnostic" }));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
        }

        [Test] public void DiagnosticAdmissionExceedsOldLimitButKeepsCriticalReserveAndHardBounds()
        {
            string directory = Temporary();
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var store = new CombatEvidenceStore(directory, EvidenceStoreOptions.ForProfile(EvidenceProfile.Diagnostic, true));
            try
            {
                Assert.That(store.Schedule(512, () => { entered.Set(); release.Wait(); }), Is.True);
                Assert.That(entered.Wait(5000), Is.True);
                // These are deliberately conservative accounting charges, not a 512 MiB test allocation.
                Assert.That(store.TryWrite(Record(1, 32 << 20)), Is.True);
                Assert.That(store.Schedule((508 << 20) - (32 << 20) - 512, () => { }), Is.True);
                Assert.That(store.PendingBytes, Is.EqualTo(508L << 20));
                Assert.That(store.TryWrite(Record(2, 512)), Is.False);
                Assert.That(store.TryWriteAdvance("capture", "run", 1, new DiagnosticAdvance {
                    sequence = 3, utcTicks = DateTime.UtcNow.Ticks, engine = "replica-1", operation = "Advance" }), Is.False);
                Assert.That(store.TryWrite(Record(4, 4 << 20, true)), Is.True);
                Assert.That(store.PendingBytes, Is.EqualTo(512L << 20));
                Assert.That(store.TryWrite(Record(5, 512, true)), Is.False);
                Assert.That(store.Memory.Used, Is.LessThanOrEqualTo(768L << 20));
                Assert.That(store.Configuration.drainPolicy, Is.EqualTo("WaitForCompletion"));
            }
            finally
            {
                release.Set(); Close(store, directory);
            }
        }

        [Test] public void DiagnosticQueueDoesNotBypassTheIndependentTotalBudget()
        {
            string directory = Temporary();
            var options = EvidenceStoreOptions.ForProfile(EvidenceProfile.Diagnostic, true);
            options.Memory = new DiagnosticMemoryBudget(26L << 20);
            var store = new CombatEvidenceStore(directory, options);
            try
            {
                Assert.That(store.TryWrite(Record(1, 2 << 20)), Is.False);
                store.RequestClose(); Assert.That(store.WaitForClose(5000), Is.True);
                string path = Path.Combine(directory, "observation.json");
                Assert.That(store.ExportQueueObservation(path), Is.True);
                Assert.That((string)JObject.Parse(File.ReadAllText(path))["firstRejection"]["guard"], Is.EqualTo("BudgetReservation"));
                Assert.That(store.PendingBytes, Is.Zero);
            }
            finally { Close(store, directory); }
        }

        [Test] public void OneSecondWindowsRetainTenMinutesWithoutIncreasingTheObservationArrays()
        {
            long origin = Stopwatch.Frequency;
            var memory = new DiagnosticMemoryBudget(EvidenceQueueObservation.ReservedBytes);
            Assert.That(EvidenceQueueObservation.TryCreate(memory, true, out var observer, origin, true, 1000), Is.True);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                observer.MarkPhase(EvidenceQueuePhase.Load, origin);
                foreach (int second in new[] { 0, 599, 600 })
                {
                    long now = origin + second * Stopwatch.Frequency;
                    observer.Attempt(EvidenceQueueEntry.TryWrite, now);
                    observer.Accepted(EvidenceQueueEntry.TryWrite, now);
                    observer.Enqueued(512, 512, now); observer.Dequeued(now);
                    observer.Completed(512, now); observer.Pending(0, now); observer.ConsumerPending(0, now);
                }
                Assert.That(observer.StopAndExport(path, true, true), Is.True);
                var report = JObject.Parse(File.ReadAllText(path));
                Assert.That((int)report["windowMilliseconds"], Is.EqualTo(1000));
                Assert.That((bool)report["overflow"], Is.False);
                Assert.That((bool)report["countsBalanced"], Is.True);
                Assert.That(report["producerWindows"].Count(), Is.EqualTo(3));
                Assert.That((long)report["accountedStorageBytes"], Is.LessThanOrEqualTo(EvidenceQueueObservation.ReservedBytes));
                Assert.That(memory.Used, Is.Zero);
            }
            finally { observer.ReleaseAfterStop(true, true); if (File.Exists(path)) File.Delete(path); }
        }

        private static EvidenceStoreOptions Parse(string[] arguments) => (EvidenceStoreOptions)typeof(CombatEvidenceRuntime)
            .GetMethod("CreateStoreOptions", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { arguments });
        private static string Temporary() => Path.Combine(Path.GetTempPath(), "evidence-profile-" + Guid.NewGuid().ToString("N"));
        private static DiagnosticRecord Record(ulong sequence, int bytes, bool critical = false) => new DiagnosticRecord {
            captureId = "capture", runId = "run", round = 1, recordSequence = sequence.ToString(),
            stage = "profile.fixture", estimatedBytes = bytes, critical = critical, utc = DateTime.UtcNow.ToString("o") };
        private static void Close(CombatEvidenceStore store, string directory)
        {
            store.RequestClose(); bool joined = store.WaitForClose(5000);
            if (joined) store.QueueObservation?.ReleaseAfterStop(true, true);
            store.Dispose();
            Assert.That(joined, Is.True);
            Assert.That(store.Memory.Used, Is.Zero);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
