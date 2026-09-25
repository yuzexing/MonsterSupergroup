using System;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json.Linq;
using System.Reflection;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceMainObservationTests
    {
        [Test] public void MainFrameObservationExposesOriginalFrameAndSequenceBoundaries()
        {
            var observer = typeof(EvidenceQueueObservation);
            Assert.That(observer.GetMethod("BeginMainFrame", new[] { typeof(int), typeof(long), typeof(ulong) }), Is.Not.Null,
                "The current queue/service timeline cannot attribute measured main-thread tail frames.");
            Assert.That(observer.GetMethod("EndMainFrame", new[] { typeof(long), typeof(ulong) }), Is.Not.Null,
                "Retained frames must preserve their original end timestamp and produced-sequence boundary.");
            Assert.That(observer.GetMethod("BeginMainStage", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
            Assert.That(observer.GetMethod("EndMainStage", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
        }

        [Test] public void MainFrameDetailsHaveFixedCapacityWithinTheExistingReservation()
        {
            var observer = typeof(EvidenceQueueObservation);
            foreach (var pair in new[] {
                (name: "MainFrameCapacity", value: 36),
                (name: "MainStageCount", value: 16),
                (name: "MainStackDepth", value: 16) })
            {
                var field = observer.GetField(pair.name, BindingFlags.Public | BindingFlags.Static);
                Assert.That(field, Is.Not.Null, pair.name + " must be an explicit bounded contract.");
                Assert.That((int)field.GetRawConstantValue(), Is.EqualTo(pair.value), pair.name);
            }
            Assert.That(observer.GetProperty("MainStorageBytes", BindingFlags.Public | BindingFlags.Static), Is.Not.Null,
                "The new frame headers, stage values, stack and metadata must be charged inside the existing reservation.");
            Assert.That(EvidenceQueueObservation.ReservedBytes, Is.EqualTo(512 << 10));
            Assert.That(EvidenceQueueObservation.WindowCount, Is.EqualTo(1024));
        }

        [Test] public void MainTimingBridgeBelongsToGasAndUsesAValueScopeWithoutEngineDependencies()
        {
            var gas = typeof(CombatEvidence).Assembly;
            var bridge = gas.GetType("MonsterSupergroup.GAS.DiagnosticMainTiming");
            Assert.That(bridge, Is.Not.Null,
                "Diagnostic wrappers need a thread-isolated timing bridge without a GAS dependency on NetworkCombat or Unity.");
            var stage = gas.GetType("MonsterSupergroup.GAS.DiagnosticMainStage");
            var observer = gas.GetType("MonsterSupergroup.GAS.IDiagnosticMainTiming");
            var scope = gas.GetType("MonsterSupergroup.GAS.DiagnosticMainScope");
            Assert.That(stage, Is.Not.Null); Assert.That(stage.IsEnum, Is.True);
            Assert.That(Enum.GetNames(stage).Length, Is.EqualTo(16));
            Assert.That(observer, Is.Not.Null); Assert.That(observer.IsInterface, Is.True);
            Assert.That(scope, Is.Not.Null); Assert.That(scope.IsValueType, Is.True);
            Assert.That(bridge.GetMethod("Measure", new[] { stage })?.ReturnType, Is.EqualTo(scope));
            Assert.That(observer.GetMethod("BeginMainStage", new[] { stage, typeof(long) }), Is.Not.Null);
            Assert.That(observer.GetMethod("EndMainStage", new[] { stage, typeof(long) }), Is.Not.Null);
            Assert.That(gas.GetReferencedAssemblies().Select(reference => reference.Name)
                .Any(name => name.StartsWith("Unity", StringComparison.Ordinal) || name.Contains("NetworkCombat")), Is.False);
        }

        [Test] public void ConcreteMainLayoutAndExistingWindowsAndServicesFitTheSameReservation()
        {
            var type = typeof(EvidenceQueueObservation);
            long windows = (long)(Marshal.SizeOf(type.GetNestedType("ProducerWindow", BindingFlags.NonPublic)) +
                Marshal.SizeOf(type.GetNestedType("ConsumerWindow", BindingFlags.NonPublic))) * EvidenceQueueObservation.WindowCount;
            Assert.That(Marshal.SizeOf(type.GetNestedType("MainFrameRecord", BindingFlags.NonPublic)), Is.EqualTo(48));
            Assert.That(Marshal.SizeOf(type.GetNestedType("MainStageValue", BindingFlags.NonPublic)), Is.EqualTo(24));
            Assert.That(Marshal.SizeOf(type.GetNestedType("MainStackEntry", BindingFlags.NonPublic)), Is.EqualTo(24));
            Assert.That(EvidenceQueueObservation.MainStorageBytes, Is.EqualTo(19792));
            long total = windows + (64 << 10) + EvidenceQueueObservation.ServiceStorageBytes + EvidenceQueueObservation.MainStorageBytes + EvidenceQueueObservation.DeferredWindowMetadataBytes;
            Assert.That(total, Is.LessThanOrEqualTo(EvidenceQueueObservation.ReservedBytes));
            Assert.That(EvidenceQueueObservation.AccountedStorageBytes, Is.EqualTo(total));
            TestContext.WriteLine("Actual main layout={0}; existing and new total={1}; reservation={2}",
                EvidenceQueueObservation.MainStorageBytes, total, EvidenceQueueObservation.ReservedBytes);
        }

        [Test] public void NestedMainStagesAreExclusiveAndWorkloadResidualIsExplicitlyNonDiagnostic()
        {
            var observer = CreateMain(); observer.BeginMainFrame(0, 100, 9);
            observer.BeginMainStage(DiagnosticMainStage.WorkloadStep, 110);
            observer.BeginMainStage(DiagnosticMainStage.GasWrapper, 120);
            observer.BeginMainStage(DiagnosticMainStage.Capture, 130);
            observer.EndMainStage(DiagnosticMainStage.Capture, 150);
            observer.EndMainStage(DiagnosticMainStage.GasWrapper, 160);
            observer.EndMainStage(DiagnosticMainStage.WorkloadStep, 180);
            observer.EndMainFrame(200, 19);
            var result = ExportMain(observer); var frame = result["frames"].Single();
            Assert.That((bool)result["complete"], Is.True);
            Assert.That((long)frame["wallTicks"], Is.EqualTo(100));
            Assert.That((ulong)frame["sequenceBefore"], Is.EqualTo(9)); Assert.That((ulong)frame["sequenceAfter"], Is.EqualTo(19));
            Assert.That((long)MainStage(frame, "WorkloadStep")["inclusiveTicks"], Is.EqualTo(70));
            Assert.That((long)MainStage(frame, "WorkloadStep")["exclusiveTicks"], Is.EqualTo(30));
            Assert.That((string)MainStage(frame, "WorkloadStep")["exclusiveClassification"], Is.EqualTo("NonDiagnosticResidual"));
            Assert.That((long)MainStage(frame, "GasWrapper")["exclusiveTicks"], Is.EqualTo(20));
            Assert.That((long)MainStage(frame, "Capture")["exclusiveTicks"], Is.EqualTo(20));
            Assert.That((int)MainStage(frame, "Capture")["callCount"], Is.EqualTo(1));
            Assert.That((long)result["stageCallCount"], Is.EqualTo(3));
            Assert.That((long)frame["wallResidualTicks"], Is.EqualTo(30));
        }

        [Test] public void LongestThirtySixFramesRetainOriginalSequenceBoundariesAndEarlierTies()
        {
            var observer = CreateMain();
            for (int frame = 71; frame >= 0; frame--)
            {
                long start = 1000 + (72 - frame) * 1000;
                observer.BeginMainFrame(frame, start, (ulong)(frame * 2));
                observer.EndMainFrame(start + 100, (ulong)(frame * 2 + 1));
            }
            var result = ExportMain(observer);
            CollectionAssert.AreEqual(Enumerable.Range(0, 36), result["frames"].Select(frame => (int)frame["frame"]));
            Assert.That((int)result["frameCount"], Is.EqualTo(72)); Assert.That((int)result["unretainedFrames"], Is.EqualTo(36));
            Assert.That((long)result["overflowEvents"], Is.Zero); Assert.That((bool)result["complete"], Is.True);
            Assert.That((ulong)result["frames"][35]["sequenceBefore"], Is.EqualTo(70));
        }

        [Test] public void TailSelectionKeepsTheActualLongestFramesWithoutCurrentScratchSlot()
        {
            var observer = CreateMain();
            for (int frame = 0; frame < 80; frame++)
            { observer.BeginMainFrame(frame, frame * 1000, (ulong)frame); observer.EndMainFrame(frame * 1000 + frame, (ulong)frame + 1); }
            var result = ExportMain(observer);
            CollectionAssert.AreEqual(Enumerable.Range(44, 36), result["frames"].Select(frame => (int)frame["frame"]));
            Assert.That(result["frames"].Count(), Is.EqualTo(36));
            Assert.That((int)result["unretainedFrames"], Is.EqualTo(44));
        }

        [Test] public void ZeroDurationStagesArePresentWhileAbsentStagesStayAbsent()
        {
            var observer = CreateMain(); observer.BeginMainFrame(0, 100, 0);
            observer.BeginMainStage(DiagnosticMainStage.Wake, 110); observer.EndMainStage(DiagnosticMainStage.Wake, 110);
            observer.EndMainFrame(120, 0); var frame = ExportMain(observer)["frames"].Single();
            Assert.That((long)MainStage(frame, "Wake")["maxTicks"], Is.Zero);
            Assert.That(frame["stages"].Count(), Is.EqualTo(1)); Assert.That((bool)frame["stagesComplete"], Is.True);
        }

        [Test] public void StackOverflowAndReversedBoundariesCannotClaimACompleteFrame()
        {
            var observer = CreateMain(); observer.BeginMainFrame(0, 100, 20);
            for (int i = 0; i < EvidenceQueueObservation.MainStackDepth + 1; i++) observer.BeginMainStage(DiagnosticMainStage.GasWrapper, 110);
            for (int i = 0; i < EvidenceQueueObservation.MainStackDepth + 1; i++) observer.EndMainStage(DiagnosticMainStage.GasWrapper, 120);
            observer.EndMainFrame(130, 19); var result = ExportMain(observer); var frame = result["frames"].Single();
            Assert.That((bool)result["complete"], Is.False); Assert.That((bool)frame["sequenceComplete"], Is.False);
            Assert.That((bool)frame["stagesComplete"], Is.False); Assert.That(frame["wallResidualTicks"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That((long)result["overflowEvents"], Is.GreaterThan(0));
            Assert.That((int)MainStage(frame, "GasWrapper")["callCount"], Is.EqualTo(EvidenceQueueObservation.MainStackDepth + 1));
        }

        [Test] public void MismatchedStageDisposalAndReversedWallStayUnknown()
        {
            var observer = CreateMain(); observer.BeginMainFrame(0, 100, 1);
            observer.BeginMainStage(DiagnosticMainStage.Capture, 110); observer.EndMainStage(DiagnosticMainStage.Freeze, 120);
            observer.EndMainFrame(90, 1); var result = ExportMain(observer); var frame = result["frames"].Single();
            Assert.That((bool)result["complete"], Is.False); Assert.That(frame["wallTicks"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(frame["wallResidualTicks"].Type, Is.EqualTo(JTokenType.Null));
        }

        [Test] public void ExceptionUnwindingClosesScopesAndRestoresTheAmbientFrame()
        {
            var observer = CreateMain(); long started = EvidenceQueueObservation.Now;
            observer.BeginMainFrame(0, started, 0);
            try { using var scope = DiagnosticMainTiming.Measure(DiagnosticMainStage.Capture); throw new InvalidOperationException("business"); }
            catch (InvalidOperationException) { }
            finally { observer.EndMainFrame(EvidenceQueueObservation.Now, 1); }
            var sentinel = new ThrowingMainTiming(); Assert.That(DiagnosticMainTiming.TryAttach(sentinel), Is.True);
            try
            {
                Assert.DoesNotThrow(() => { using var ignored = DiagnosticMainTiming.Measure(DiagnosticMainStage.Capture); });
                Assert.That(DiagnosticMainTiming.HasFault(sentinel), Is.True);
            }
            finally { DiagnosticMainTiming.Detach(sentinel); }
            var result = ExportMain(observer); Assert.That((bool)result["complete"], Is.True);
            Assert.That(MainStage(result["frames"].Single(), "Capture"), Is.Not.Null);
        }

        [Test] public void WorkerThreadCannotAddStagesToTheMainAmbientFrame()
        {
            var observer = CreateMain(); observer.BeginMainFrame(0, EvidenceQueueObservation.Now, 0);
            Exception failure = null;
            var worker = new Thread(() => { try { using var ignored = DiagnosticMainTiming.Measure(DiagnosticMainStage.Capture); }
                catch (Exception error) { failure = error; } }) { IsBackground = true };
            worker.Start(); Assert.That(worker.Join(2000), Is.True); Assert.That(failure, Is.Null);
            observer.EndMainFrame(EvidenceQueueObservation.Now, 0); var result = ExportMain(observer);
            Assert.That(result["frames"].Single()["stages"].Count(), Is.Zero); Assert.That((bool)result["complete"], Is.True);
        }

        [Test] public void ActiveFramePreventsExportOrReleaseAndPostReleaseCallsAreSafe()
        {
            var memory = new DiagnosticMemoryBudget(); Assert.That(EvidenceQueueObservation.TryCreate(memory, true, out var observer, 1), Is.True);
            observer.BeginMainFrame(0, 100, 0);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Assert.That(observer.StopAndExport(path, true, true), Is.False); Assert.That(File.Exists(path), Is.False);
                Assert.That(observer.ReleaseAfterStop(true, true), Is.False);
                Assert.That(memory.Used, Is.EqualTo(EvidenceQueueObservation.ReservedBytes));
                observer.EndMainFrame(200, 0); Assert.That(observer.StopAndExport(path, true, true), Is.True);
                Assert.That(observer.ReleaseAfterStop(true, true), Is.False);
                Assert.That(memory.Used, Is.Zero);
                Assert.DoesNotThrow(() => { observer.BeginMainFrame(1, 300, 0); observer.BeginMainStage(DiagnosticMainStage.Capture, 301);
                    observer.EndMainStage(DiagnosticMainStage.Capture, 302); observer.EndMainFrame(303, 1); });
            }
            finally { observer.EndMainFrame(200, 0); observer.ReleaseAfterStop(true, true); if (File.Exists(path)) File.Delete(path); }
        }

        [Test] public void DuplicateFrameBeginCannotOverwriteCurrentIdentityOrClaimComplete()
        {
            var observer = CreateMain(); observer.BeginMainFrame(5, 100, 10); observer.BeginMainFrame(6, 110, 20);
            observer.EndMainFrame(200, 30); var result = ExportMain(observer);
            Assert.That((int)result["frames"].Single()["frame"], Is.EqualTo(5)); Assert.That((bool)result["complete"], Is.False);
            Assert.That((long)result["lifecycleErrors"], Is.GreaterThan(0));
        }

        [Test] public void DisabledAndUncapturedObserversDoNotPretendMainFramesWereMeasured()
        {
            var memory = new DiagnosticMemoryBudget();
            Assert.That(EvidenceQueueObservation.TryCreate(memory, false, out var disabled), Is.False);
            Assert.That(memory.Used, Is.Zero);
            Assert.That(disabled, Is.Null); Assert.DoesNotThrow(() => { using var ignored = DiagnosticMainTiming.Measure(DiagnosticMainStage.Capture); });
            var result = ExportMain(CreateMain()); Assert.That((string)result["status"], Is.EqualTo("NotCaptured"));
            Assert.That((bool)result["complete"], Is.False); Assert.That((int)result["frameCount"], Is.Zero);
        }

        [Test] public void ASecondObserverCannotReplaceTheFirstActiveAmbientFrame()
        {
            var first = CreateMain(); var second = CreateMain();
            first.BeginMainFrame(1, EvidenceQueueObservation.Now, 0);
            second.BeginMainFrame(2, EvidenceQueueObservation.Now, 0);
            using (DiagnosticMainTiming.Measure(DiagnosticMainStage.Capture)) { }
            first.EndMainFrame(EvidenceQueueObservation.Now, 1);
            var result = ExportMain(first); var skipped = ExportMain(second);
            Assert.That((bool)result["complete"], Is.True); Assert.That(MainStage(result["frames"].Single(), "Capture"), Is.Not.Null);
            Assert.That((bool)skipped["complete"], Is.False); Assert.That((long)skipped["lifecycleErrors"], Is.EqualTo(1));
        }

        [Test] public void ForeignDirectCallsAreRejectedWithoutClosingOrChangingTheActiveFrame()
        {
            var observer = CreateMain(); observer.BeginMainFrame(0, 100, 0);
            Exception failure = null;
            var worker = new Thread(() => {
                try { observer.BeginMainStage(DiagnosticMainStage.Capture, 110); observer.EndMainStage(DiagnosticMainStage.Capture, 120);
                    observer.EndMainFrame(130, 7); }
                catch (Exception error) { failure = error; }
            }) { IsBackground = true };
            worker.Start(); Assert.That(worker.Join(2000), Is.True); Assert.That(failure, Is.Null);
            observer.EndMainFrame(140, 1); var result = ExportMain(observer); var frame = result["frames"].Single();
            Assert.That((long)result["threadViolations"], Is.EqualTo(3)); Assert.That((bool)result["complete"], Is.False);
            Assert.That((long)frame["endTicks"], Is.EqualTo(140)); Assert.That((ulong)frame["sequenceAfter"], Is.EqualTo(1));
            Assert.That(frame["stages"].Count(), Is.Zero);
        }

        [Test] public void BridgeCatchesAnEndCallbackFailureWithoutChangingTheBusinessException()
        {
            var observer = new EndThrowingMainTiming(); Assert.That(DiagnosticMainTiming.TryAttach(observer), Is.True);
            try
            {
                var error = Assert.Throws<ArgumentException>(() => {
                    using var scope = DiagnosticMainTiming.Measure(DiagnosticMainStage.Capture);
                    throw new ArgumentException("business failure");
                });
                Assert.That(error.Message, Is.EqualTo("business failure"));
                Assert.That(DiagnosticMainTiming.HasFault(observer), Is.True);
            }
            finally { DiagnosticMainTiming.Detach(observer); }
        }

        [Test] public void AnUnclosedStageMakesTheFrameIncompleteAndStillDetachesTheAmbientBridge()
        {
            var observer = CreateMain(); observer.BeginMainFrame(0, 100, 0);
            observer.BeginMainStage(DiagnosticMainStage.Capture, 110); observer.EndMainFrame(120, 1);
            var sentinel = new ThrowingMainTiming(); Assert.That(DiagnosticMainTiming.TryAttach(sentinel), Is.True);
            DiagnosticMainTiming.Detach(sentinel);
            var result = ExportMain(observer); Assert.That((bool)result["complete"], Is.False);
            Assert.That(result["frames"].Single()["wallResidualTicks"].Type, Is.EqualTo(JTokenType.Null));
        }

        private sealed class EndThrowingMainTiming : IDiagnosticMainTiming
        {
            public void BeginMainStage(DiagnosticMainStage stage, long ticks) { }
            public void EndMainStage(DiagnosticMainStage stage, long ticks) => throw new InvalidOperationException("diagnostic");
        }

        private sealed class ThrowingMainTiming : IDiagnosticMainTiming
        {
            public void BeginMainStage(DiagnosticMainStage stage, long ticks) => throw new InvalidOperationException("diagnostic");
            public void EndMainStage(DiagnosticMainStage stage, long ticks) => throw new InvalidOperationException("diagnostic");
        }
        private static EvidenceQueueObservation CreateMain()
        { Assert.That(EvidenceQueueObservation.TryCreate(new DiagnosticMemoryBudget(), true, out var observer, 1), Is.True); return observer; }
        private static JToken MainStage(JToken frame, string stage) => frame["stages"].Single(value => (string)value["stage"] == stage);
        private static JToken ExportMain(EvidenceQueueObservation observer)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try { Assert.That(observer.StopAndExport(path, true, true), Is.True); return JObject.Parse(File.ReadAllText(path))["mainFrames"]; }
            finally { observer.ReleaseAfterStop(true, true); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
