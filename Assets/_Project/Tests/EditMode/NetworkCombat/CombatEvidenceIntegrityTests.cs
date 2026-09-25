using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceIntegrityTests
    {
        [TearDown] public void ResetSink() => CombatEvidence.Sink = null;

        [Test] public void FailedCaptureUsesIndependentIntegrityChannelAndRetainsIdentity()
        {
            var sink = new IntegritySink { throwWrites = true }; CombatEvidence.Sink = sink;
            var original = new DiagnosticRecord { stage = "stats.damage", engine = "output-3", role = "Owner", operation = "ApplyDamage",
                eventId = "17", rootEventId = "11", parentEventId = "15", source = 2, target = 7, stateVersion = 9,
                input = new object(), before = new object(), after = new object(), inputRef = "stale", critical = false };
            Assert.That(CombatEvidence.Write(original), Is.False);
            Assert.That(sink.writes, Is.EqualTo(1));
            var gap = sink.failures.Single();
            Assert.That(gap.stage, Is.EqualTo("evidence.gap")); Assert.That(gap.outcome, Is.EqualTo("CaptureFailed"));
            Assert.That(gap.reason, Is.EqualTo("stats.damage:InvalidOperationException"));
            Assert.That(gap.engine, Is.EqualTo("output-3")); Assert.That(gap.operation, Is.EqualTo("ApplyDamage"));
            Assert.That(gap.eventId, Is.EqualTo("17")); Assert.That(gap.rootEventId, Is.EqualTo("11")); Assert.That(gap.parentEventId, Is.EqualTo("15"));
            Assert.That(gap.source, Is.EqualTo(2)); Assert.That(gap.target, Is.EqualTo(7)); Assert.That(gap.stateVersion, Is.EqualTo(9));
            Assert.That(gap.input, Is.Null); Assert.That(gap.before, Is.Null); Assert.That(gap.after, Is.Null); Assert.That(gap.inputRef, Is.Null);
            Assert.That(gap.critical, Is.True); Assert.That(original.stage, Is.EqualTo("stats.damage"));
        }

        [Test] public void OldSinkReceivesFallbackGapAndBrokenIntegritySinkCannotThrowOrRecurse()
        {
            var old = new LegacySink(); CombatEvidence.Sink = old;
            CombatEvidence.ReportCaptureFailure(new DiagnosticRecord { stage = "owner.attack_stats" }, new ArgumentException());
            Assert.That(old.records.Single().reason, Is.EqualTo("owner.attack_stats:ArgumentException"));
            var broken = new IntegritySink { throwWrites = true, throwIntegrity = true }; CombatEvidence.Sink = broken;
            Assert.DoesNotThrow(() => CombatEvidence.Write(new DiagnosticRecord { stage = "stats.damage" }));
            Assert.That(broken.writes, Is.EqualTo(1)); Assert.That(broken.integrityCalls, Is.EqualTo(1));
        }

        [Test] public void UnknownEngineRegistrationFailureRemainsConservativeAndDoesNotSuppressLaterCalls()
        {
            var sink = new IntegritySink { throwRegistration = true }; CombatEvidence.Sink = sink;
            var engine = new object();
            using (CombatEvidence.Begin(engine, "gateway", "StopCombat", Array.Empty<object>(), _ => new object())) { }
            var gap = sink.failures.Single();
            Assert.That(gap.engine, Is.Null); Assert.That(gap.reason, Is.EqualTo("replay.engine_checkpoint:InvalidOperationException"));
            Assert.That(CombatEvidence.CurrentEngine, Is.Null);
            sink.throwRegistration = false;
            using (var operation = CombatEvidence.Begin(engine, "gateway", "StopCombat", Array.Empty<object>(), _ => new object())) operation.Complete();
            Assert.That(sink.records.Select(r => r.stage), Is.EqualTo(new[] { "replay.input", "replay.output" }));
            Assert.That(CombatEvidence.CurrentEngine, Is.Null);
        }

        [Test] public void FailedCompactInputAndCompletionRemainReportedWithoutChangingStatusAdvance()
        {
            var sink = new IntegritySink { throwAdvances = true }; CombatEvidence.Sink = sink;
            var status = new StatusController(_ => { });
            Assert.DoesNotThrow(() => status.Advance(.125f));
            Assert.That(sink.failures.Select(r => r.reason), Is.EqualTo(new[] { "replay.input:InvalidOperationException", "replay.output:InvalidOperationException" }));
            Assert.That(sink.failures.All(r => r.engine == "status-1" && r.operation == "Advance"), Is.True);
            Assert.That(CombatEvidence.CurrentEngine, Is.Null);
        }

        [Test] public void ExplicitFailureRetainsTheDamageContextAndSuppressionPreventsRecursiveEvidence()
        {
            var sink = new IntegritySink(); CombatEvidence.Sink = sink;
            var context = CombatContext.CreateRoot(CombatEventId.Compose(2, 4, 5), 2, 3, 7, CombatTags.Attack)
                .CreateChild(CombatEventId.Compose(2, 4, 6), CombatTags.Damage, 19, 11);
            CombatEvidence.ReportCaptureFailure("Owner", "owner.damage_calculation", new ArgumentException(), context, "damage-1");
            var gap = sink.failures.Single();
            Assert.That(gap.eventId, Is.EqualTo(context.EventId.Value.ToString()));
            Assert.That(gap.rootEventId, Is.EqualTo(context.RootEventId.Value.ToString()));
            Assert.That(gap.parentEventId, Is.EqualTo(context.ParentEventId.Value.ToString()));
            Assert.That(gap.source, Is.EqualTo(2)); Assert.That(gap.target, Is.EqualTo(19)); Assert.That(gap.engine, Is.EqualTo("damage-1"));
            using (CombatEvidence.Suppress()) CombatEvidence.ReportCaptureFailure("Owner", "owner.damage_calculation", new Exception(), context);
            Assert.That(sink.failures, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DetachedStatusPublishesCurrentBoundaryBeforeItsFirstIndependentCall(bool periodicCheckpoint)
        {
            using var capture = new RuntimeCapture();
            var status = new StatusController(_ => { });
            status.Advance(.25f);
            var replica = new CanonicalWorldReplica();
            replica.RegisterStatusController(4, status);
            status.Advance(.5f);
            if (periodicCheckpoint) capture.CheckpointSet();
            Assert.That(replica.UnregisterStatusController(4, status), Is.True);
            status.Clear();
            status.Clear();
            var records = capture.Close();
            var checkpoints = records.Where(r => r.stage == "replay.engine_checkpoint" && r.engine.StartsWith("status-", StringComparison.Ordinal)).ToArray();
            Assert.That(checkpoints, Has.Length.EqualTo(2), "A changed parent lifecycle needs a new boundary, while subsequent independent calls reuse it.");
            var checkpoint = checkpoints[1];
            Assert.That((double)capture.Payload(checkpoint)["state"]["time"], Is.EqualTo(.75d));
            var firstClear = records.First(r => r.engine == checkpoint.engine && r.operation == "Clear" && r.stage == "replay.input");
            Assert.That(ulong.Parse(checkpoint.recordSequence), Is.LessThan(ulong.Parse(firstClear.recordSequence)));
            if (periodicCheckpoint)
                Assert.That(capture.Payload(records.Single(r => r.stage == "replay.checkpoint"))["engines"].Any(e => (string)e["engine"] == checkpoint.engine), Is.False);
            Assert.That((bool)capture.Coverage()["complete"], Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DetachedBoundaryFailureLeavesAGapWithoutThrowingIntoTheOperation(bool exhaustBudget)
        {
            using var capture = new RuntimeCapture();
            var target = new object(); var parent = new object(); bool fail = false;
            Func<object, object> snapshot = _ => fail ? throw new InvalidOperationException("InjectedDetachedCaptureFailure") : new { value = 1 };
            string engine = capture.runtime.RegisterEngine(target, "status", snapshot);
            CombatEvidence.Bind(target, parent, "child.", "replica", _ => new { value = 2 });
            CombatEvidence.Unbind(target);
            Assert.That(SpinWait.SpinUntil(() => capture.store.PendingBytes == 0, 5000), Is.True);
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            Assert.That(capture.store.Schedule(512, () => { entered.Set(); release.Wait(5000); }), Is.True);
            Assert.That(entered.Wait(5000), Is.True);
            long reserved = 0;
            try
            {
                if (exhaustBudget)
                {
                    reserved = capture.store.Memory.Limit - capture.store.Memory.Used - (1 << 20);
                    Assert.That(capture.store.Memory.TryReserve(reserved), Is.True);
                }
                else fail = true;
                Assert.DoesNotThrow(() => { using var operation = CombatEvidence.Begin(target, "status", "AfterUnbind", Array.Empty<object>(), snapshot); operation.Complete(); });
            }
            finally { if (reserved != 0) capture.store.Memory.Release(reserved); release.Set(); }
            var records = capture.Close();
            Assert.That(records.Count(r => r.stage == "replay.engine_checkpoint" && r.engine == engine), Is.EqualTo(1));
            var coverage = capture.Coverage();
            Assert.That((bool)coverage["complete"], Is.False, "An absent detached boundary cannot leave an apparently complete source.");
            Assert.That((long)coverage["criticalDropped"], Is.GreaterThan(0));
            Assert.That(coverage["gaps"], Is.Not.Empty);
            Assert.That(records.Any(r => r.operation == "AfterUnbind" && r.stage == "replay.output" && r.outcome == "Completed"), Is.True);
        }

        [Test] public void UnchangedIndependentEngineDoesNotCaptureOnEveryCall()
        {
            using var capture = new RuntimeCapture();
            var target = new object(); int snapshots = 0;
            for (int i = 0; i != 3; ++i)
                using (var operation = CombatEvidence.Begin(target, "gateway", "Advance", Array.Empty<object>(), _ => { snapshots++; return new { value = 1 }; })) operation.Complete();
            var records = capture.Close();
            Assert.That(snapshots, Is.EqualTo(1));
            Assert.That(records.Count(r => r.stage == "replay.engine_checkpoint"), Is.EqualTo(1));
            Assert.That(records.Count(r => r.stage == "replay.input"), Is.EqualTo(3));
        }

        // An inactive component uses the production registration and store paths without booting networking or a Player.
        private sealed class RuntimeCapture : IDisposable
        {
            public readonly CombatEvidenceRuntime runtime;
            public readonly CombatEvidenceStore store;
            private readonly UnityEngine.GameObject root;
            private readonly string directory = Path.Combine(Path.GetTempPath(), "evidence-binding-" + Guid.NewGuid().ToString("N"));
            private readonly string source;
            public RuntimeCapture()
            {
                root = new UnityEngine.GameObject("evidence-binding-test"); root.SetActive(false);
                runtime = root.AddComponent<CombatEvidenceRuntime>();
                store = new CombatEvidenceStore(directory);
                Set("store", store); Set("capture", "capture"); Set("mainThread", Thread.CurrentThread.ManagedThreadId);
                source = Path.Combine(directory, "boot", "0", "sources", "capture");
                CombatEvidence.Sink = runtime;
            }
            private void Set(string name, object value) => typeof(CombatEvidenceRuntime).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(runtime, value);
            public void CheckpointSet() => typeof(CombatEvidenceRuntime).GetMethod("CaptureCheckpointSet", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime, null);
            public DiagnosticRecord[] Close()
            {
                store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
                return Directory.GetFiles(source, "events-*.jsonl").OrderBy(p => p, StringComparer.Ordinal).SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
            }
            public JObject Coverage() => JObject.Parse(File.ReadAllText(Path.Combine(source, "coverage.json")));
            public JObject Payload(DiagnosticRecord record)
            {
                using var file = File.OpenRead(Path.Combine(source, record.checkpointRef));
                using var gzip = new GZipStream(file, CompressionMode.Decompress); using var reader = new StreamReader(gzip);
                return JObject.Parse(reader.ReadToEnd());
            }
            public void Dispose()
            {
                CombatEvidence.Sink = null; Set("shuttingDown", true);
                store.Dispose(); store.WaitForClose(); UnityEngine.Object.DestroyImmediate(root);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private sealed class LegacySink : IDiagnosticSink
        {
            public readonly List<DiagnosticRecord> records = new();
            public bool TryWrite(DiagnosticRecord record) { records.Add(record); return true; }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture) => domain + "-1";
        }
        private sealed class IntegritySink : IDiagnosticSink, IDiagnosticIntegritySink, IDiagnosticAdvanceSink
        {
            public bool throwWrites, throwIntegrity, throwRegistration, throwAdvances;
            public int writes, integrityCalls;
            public readonly List<DiagnosticRecord> records = new(), failures = new();
            public bool TryWrite(DiagnosticRecord record)
            {
                writes++; if (throwWrites) throw new InvalidOperationException("injected capture failure");
                records.Add(record); return true;
            }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture)
            { if (throwRegistration) throw new InvalidOperationException("injected registration failure"); return domain + "-1"; }
            public bool TryWriteAdvance(string role, string engine, string operation, float delta, StatusReplayBoundary boundary, int phase)
            { if (throwAdvances) throw new InvalidOperationException("injected advance failure"); return true; }
            public void ReportCaptureFailure(DiagnosticRecord record)
            {
                integrityCalls++;
                if (throwIntegrity) { CombatEvidence.ReportCaptureFailure(record, new Exception()); throw new InvalidOperationException("injected integrity failure"); }
                failures.Add(record);
            }
        }
    }
}
