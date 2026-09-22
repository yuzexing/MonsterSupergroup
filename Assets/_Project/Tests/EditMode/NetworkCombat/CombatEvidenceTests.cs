using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceTests
    {
        private string directory;
        [SetUp] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "combat-evidence-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
        [TearDown] public void TearDown() { CombatEvidence.Sink = null; if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private static T RoundTrip<T>(T value) => EvidenceJson.Decode<T>(EvidenceJson.Encode(value));
        [Test] public void QueuedEvidenceCannotBeChangedByLaterPresentationPositionCapture()
        {
            var batch = new CanonicalWorldBatch { EnemyHitPresentations = new[] { new EnemyHitPresentation { Damage = 10 } } };
            var queued = (GatewayReplayOutput)DiagnosticPayload.Freeze((object)new GatewayReplayOutput { batch = batch });
            batch.EnemyHitPresentations[0].HasPosition = true; batch.EnemyHitPresentations[0].Position = UnityEngine.Vector2.one;
            Assert.That(queued.batch.EnemyHitPresentations[0].HasPosition, Is.False);
            Assert.That(queued.batch.EnemyHitPresentations[0].Position, Is.EqualTo(UnityEngine.Vector3.zero));
        }
        private static ServerCombatGateway Gateway()
        {
            var gateway = new ServerCombatGateway();
            gateway.RegisterClientIdentity(1, 1, 1); gateway.Ledger.RegisterSource(1, 1);
            gateway.Ledger.RegisterEntity(100, 100, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            return gateway;
        }
        private static CombatSubmissionBatch Batch(uint sequence) => new CombatSubmissionBatch { BatchSequence = sequence, Results = new[] {
            new CombatResult { EventId = CombatEventId.Compose(1, 1, 1).Value, Sequence = 1, SourcePlayerId = 1, SourceEntityId = 1, TargetEntityId = 100, Damage = 10 } } };

        [Test] public void GatewayCheckpointRetainsDedupeAndBatchWatermarks()
        {
            var gateway = Gateway(); gateway.ProcessBatch(1, Batch(1), 0);
            var state = RoundTrip(gateway.CaptureReplayState());
            var restored = ServerCombatGateway.RestoreReplayState(state);
            Assert.That(EvidenceJson.Encode(restored.CaptureReplayState()), Is.EqualTo(EvidenceJson.Encode(state)));
            var duplicateEvent = restored.ProcessBatch(1, Batch(2), 3);
            Assert.That(duplicateEvent.Entities, Is.Empty);
            Assert.That(restored.Metrics.GetRejected(CombatRejectionReason.DuplicateEvent), Is.EqualTo(1));
            restored.ProcessBatch(1, Batch(2), 3);
            Assert.That(restored.Metrics.RejectedBatches, Is.Zero, "Repeated batches intentionally reach event deduplication.");
            Assert.That(restored.Metrics.GetRejected(CombatRejectionReason.DuplicateEvent), Is.EqualTo(2));
            Assert.That(restored.Ledger.TryGetState(100, out var enemy), Is.True); Assert.That(enemy.Health, Is.EqualTo(90));
        }
        [Test] public void FixtureRunsActualGatewayAndFindsFirstDivergence()
        {
            var gateway = Gateway();
            var fixture = new ReplayFixture { complete = true, domain = "gateway", checkpoint = CombatReplayAdapter.Token(gateway.CaptureReplayState()) };
            var batch = gateway.ProcessBatch(1, Batch(1), 3, out var receipts);
            fixture.steps = new[] { new ReplayStep { record = "capture:1", operation = "ProcessBatch", arguments = JArray.Parse(EvidenceJson.Encode(new object[] { 1u, Batch(1), 3d })),
                expected = CombatReplayAdapter.Token(new GatewayReplayOutput { batch = batch, receipts = receipts }), expectedState = CombatReplayAdapter.Token(gateway.CaptureReplayState()) } };
            var result = CombatReplay.Run(RoundTrip(fixture));
            Assert.That(result.passed, Is.True, EvidenceJson.Encode(result));
            Assert.That(CombatReplay.Run(fixture).finalState.ToString(), Is.EqualTo(result.finalState.ToString()));
            fixture.steps[0].expected = new JObject(); result = CombatReplay.Run(fixture);
            Assert.That(result.firstDivergence, Is.Zero); Assert.That(result.reliable, Is.True);
            Assert.That(CombatReplay.Minimize(fixture).steps, Has.Length.EqualTo(1));
        }
        [Test] public void ReplicaRoundTripPreservesOlderVersionRejection()
        {
            var replica = new CanonicalWorldReplica();
            var state = new CanonicalEntityState { EntityId = 100, Health = 40, MaxHealth = 100, Alive = true, StateVersion = 5 };
            replica.Apply(new CanonicalWorldBatch { Entities = new[] { state } });
            replica = CanonicalWorldReplica.RestoreReplayState(RoundTrip(replica.CaptureReplayState()));
            state.Health = 90; state.StateVersion = 4;
            replica.Apply(new CanonicalWorldBatch { Entities = new[] { state } });
            replica.TryGetEntity(100, out var current); Assert.That(current.Health, Is.EqualTo(40));
        }
        [Test] public void CaptureIsPassiveAndUnsignedIdsAreStrings()
        {
            var gateway = Gateway(); string first = EvidenceJson.Encode(gateway.CaptureReplayState());
            Assert.That(EvidenceJson.Encode(gateway.CaptureReplayState()), Is.EqualTo(first));
            Assert.That(EvidenceJson.Encode(new { id = ulong.MaxValue }), Does.Contain("\"18446744073709551615\""));
            var adapter = new CombatReplayAdapter("gateway"); adapter.RestoreReplayState(JToken.Parse(first));
            Assert.Throws<InvalidOperationException>(() => adapter.Execute("GetType", new JArray(), null));
        }
        [Test] public void StatusCheckpointPreservesExecutedTicksAndReplicaControllerState()
        {
            var originalTicks = new List<StatusTick>(); var controller = new StatusController(originalTicks.Add);
            controller.Apply(new StatusApplication(new StatusDefinition(EnemyStatusID.Burn, StatusStackMode.HighestPriority, 1),
                7, 3, 1, 1, sourcePlayerId: 1, sourceEntityId: 1, targetEntityId: 100));
            controller.Advance(1.25f);
            Assert.That(originalTicks.Count, Is.EqualTo(1));
            var state = RoundTrip(controller.CaptureReplayState());
            var ticks = new List<StatusTick>(); var restored = StatusController.RestoreReplayState(state, ticks.Add);
            Assert.That(EvidenceJson.Encode(restored.CaptureReplayState()), Is.EqualTo(EvidenceJson.Encode(state)));
            restored.Advance(.25f); Assert.That(ticks, Is.Empty);
            restored.Advance(.5f); Assert.That(ticks, Has.Count.EqualTo(1)); Assert.That(ticks[0].Damage.Value, Is.EqualTo(7));
            var replica = new CanonicalWorldReplica(); replica.RegisterStatusController(100, restored);
            var copy = CanonicalWorldReplica.RestoreReplayState(RoundTrip(replica.CaptureReplayState()));
            Assert.That(EvidenceJson.Encode(copy.CaptureReplayState()), Is.EqualTo(EvidenceJson.Encode(replica.CaptureReplayState())));
        }
        [Test] public void AuthorityCheckpointRejectsOldOwnerAfterHandoff()
        {
            var registry = new ServerEnemySimulationRegistry(); registry.RegisterEnemy(100, UnityEngine.Vector2.zero, 0);
            var first = registry.AssignClientOwner(100, 1, 1);
            registry.AssignClientOwner(100, 2, 2);
            var state = RoundTrip(registry.CaptureReplayState()); registry = ServerEnemySimulationRegistry.RestoreReplayState(state);
            var reason = registry.TryAcceptClientSnapshot(1, new EnemySimulationSnapshot { EnemyEntityId = 100, AssignmentEpoch = first.Epoch, Sequence = 1 });
            Assert.That(reason, Is.EqualTo(EnemySnapshotRejectionReason.WrongOwner));
            Assert.That(EvidenceJson.Encode(registry.CaptureReplayState()), Is.EqualTo(EvidenceJson.Encode(state)));
        }
        [Test] public void ThreePeersRelayAndResumeAfterDisconnectAndLostAcknowledgements()
        {
            var hub = new FakeHub();
            var stores = Enumerable.Range(0, 3).Select(i => new CombatEvidenceStore(Path.Combine(directory, "peer" + i))).ToArray();
            var transports = Enumerable.Range(0, 3).Select(i => new FakeTransport(hub, i)).ToArray();
            var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
            var replication = Enumerable.Range(0, 3).Select(i => new DiagnosticReplicator(stores[i], transports[i], ids[i])).ToArray();
            try
            {
                for (int peer = 0; peer < 3; peer++)
                {
                    var record = Record(1); record.captureId = ids[peer]; record.input = new string((char)('a' + peer), 90000);
                    record.estimatedBytes = 200000; Assert.That(stores[peer].TryWrite(record), Is.True);
                }
                for (int frame = 0; frame < 600; frame++)
                {
                    hub.connected = frame < 40 || frame >= 100;
                    hub.dropEvery = frame < 200 ? 5 : 0;
                    for (int peer = 0; peer < 3; peer++) replication[peer].Tick(frame * .1, "test");
                    Thread.Sleep(5);
                }
                foreach (var r in replication) r.Dispose();
                foreach (var store in stores) { store.Dispose(); store.WaitForClose(); }
                for (int peer = 0; peer < 3; peer++)
                {
                    var files = Directory.GetFiles(stores[peer].Root, "events-*.jsonl", SearchOption.AllDirectories);
                    Assert.That(files, Has.Length.EqualTo(3), EvidenceJson.Encode(replication.Select(r => r.LastFailure).ToArray()));
                    Assert.That(files.Sum(f => File.ReadAllLines(f).Length), Is.EqualTo(3));
                    Assert.That(Directory.GetFiles(stores[peer].Root, "*.json.gz", SearchOption.AllDirectories), Has.Length.EqualTo(3));
                }
            }
            finally { foreach (var r in replication) r.Dispose(); foreach (var s in stores) { s.Dispose(); s.WaitForClose(); } }
        }
        [Test] public void NewRunDoesNotGetStuckBehindThePreviousRunCatalog()
        {
            var hub = new FakeHub();
            var stores = Enumerable.Range(0, 3).Select(i => new CombatEvidenceStore(Path.Combine(directory, "peer" + i))).ToArray();
            var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
            var replication = Enumerable.Range(0, 3).Select(i => new DiagnosticReplicator(stores[i], new FakeTransport(hub, i), ids[i])).ToArray();
            try
            {
                using var ready = new ManualResetEventSlim();
                Assert.That(stores[0].Schedule(4096, () => {
                    string source = stores[0].Resolve("test/1/sources/" + ids[0]); Directory.CreateDirectory(source);
                    for (int i = 0; i < 100; i++) File.WriteAllText(Path.Combine(source, "metadata-" + i.ToString("D3") + ".json"), "{}");
                    ready.Set();
                }), Is.True);
                Assert.That(ready.Wait(5000), Is.True);
                for (int frame = 0; frame < 620; frame++)
                {
                    if (frame == 20)
                        for (int peer = 0; peer < 3; peer++)
                        { var record = Record(1); record.runId = "next"; record.captureId = ids[peer]; Assert.That(stores[peer].TryWrite(record), Is.True); }
                    foreach (var r in replication) r.Tick(frame * .1, frame < 20 ? "test" : "next");
                    Thread.Sleep(5);
                }
                foreach (var r in replication) r.Dispose();
                foreach (var store in stores) { store.Dispose(); store.WaitForClose(); }
                foreach (var store in stores)
                    Assert.That(Directory.GetFiles(Path.Combine(store.Root, "next"), "events-*.jsonl", SearchOption.AllDirectories), Has.Length.EqualTo(3));
            }
            finally { foreach (var r in replication) r.Dispose(); foreach (var store in stores) { store.Dispose(); store.WaitForClose(); } }
        }
        private sealed class FakeHub
        {
            public readonly Dictionary<int, FakeTransport> transports = new();
            public bool connected = true; public int dropEvery, sent;
        }
        private sealed class FakeTransport : IDiagnosticReplicationTransport
        {
            private readonly FakeHub hub; private readonly int id;
            public FakeTransport(FakeHub hub, int id) { this.hub = hub; this.id = id; hub.transports[id] = this; }
            public int[] Peers => !hub.connected ? Array.Empty<int>() : id == 0 ? new[] { 1, 2 } : new[] { 0 };
            public bool IsHost => id == 0;
            public event Action<int, ArraySegment<byte>> Received;
            public bool Send(int peer, byte[] packet)
            {
                if (!hub.connected) return false;
                if (hub.dropEvery != 0 && ++hub.sent % hub.dropEvery == 0) return true;
                hub.transports[peer].Received?.Invoke(id, new ArraySegment<byte>(packet)); return true;
            }
        }
        [Test] public void DiskRoundTripsAndRejectsPathEscapeAndCorruption()
        {
            var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { SegmentBytes = 1024 });
            for (int i = 1; i <= 20; i++) Assert.That(store.TryWrite(Record(i)), Is.True);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
            Assert.That(Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories).Length, Is.GreaterThan(1));
            Assert.That(Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories).Sum(f => File.ReadAllLines(f).Length), Is.EqualTo(20));
            var coverage = EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(Directory.GetFiles(directory, "coverage.json", SearchOption.AllDirectories)[0]));
            Assert.That(coverage.complete, Is.True); Assert.That(coverage.flushed, Is.EqualTo("20"));
            Assert.Throws<InvalidDataException>(() => store.Resolve("../escape"));
            Assert.Throws<InvalidDataException>(() => store.ImportBlock("remote/0/sources/capture/events.jsonl", 0, new byte[] { 1 }, "wrong", false));
        }
        [Test] public void QueueOverloadIsBoundedAndLeavesGap()
        {
            var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { QueueBytes = 8192, ReservedBytes = 4096 });
            var oversized = Record(1); oversized.estimatedBytes = 16000;
            Assert.That(store.TryWrite(oversized), Is.False);
            var critical = Record(2); critical.critical = true;
            Assert.That(store.TryWrite(critical), Is.True);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
            var health = EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(Directory.GetFiles(directory, "coverage.json", SearchOption.AllDirectories)[0]));
            Assert.That(health.complete, Is.False); Assert.That(health.gaps[0].reason, Is.EqualTo("QueueOverload")); Assert.That(store.PendingBytes, Is.Zero);
        }
        [Test] public void AllRecordsRejectedStillWritesCoverageAndCodecHasABound()
        {
            var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { QueueBytes = 8192, ReservedBytes = 4096 });
            var oversized = Record(1); oversized.estimatedBytes = 16000;
            Assert.That(store.TryWrite(oversized), Is.False);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
            var coverage = EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(Directory.GetFiles(directory, "coverage.json", SearchOption.AllDirectories).Single()));
            Assert.That(coverage.complete, Is.False); Assert.That(coverage.produced, Is.EqualTo("1"));
            Assert.That(coverage.written, Is.EqualTo("0")); Assert.That(coverage.gaps.Single().reason, Is.EqualTo("QueueOverload"));
            Assert.Throws<InvalidDataException>(() => EvidenceJson.EncodeBounded(new string('x', 1000), 100));
        }
        [Test] public void RestartTrimsTruncatedTailAndMarksUnknownRange()
        {
            var store = new CombatEvidenceStore(directory); store.TryWrite(Record(1)); store.Dispose(); store.WaitForClose();
            string file = Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories)[0]; File.AppendAllText(file, "{\"truncated\":");
            store = new CombatEvidenceStore(directory); store.Dispose(); store.WaitForClose();
            Assert.That(File.ReadAllLines(file), Has.Length.EqualTo(1));
            Assert.That(Directory.GetFiles(directory, "recovery.json", SearchOption.AllDirectories), Has.Length.EqualTo(1));
        }
        [Test] public void SerializationFailureDoesNotStopFollowingRecords()
        {
            var store = new CombatEvidenceStore(directory);
            var cyclic = new Dictionary<string, object>(); cyclic["cycle"] = cyclic;
            var broken = Record(1); broken.input = cyclic;
            store.TryWrite(broken); store.TryWrite(Record(2)); store.Dispose(); store.WaitForClose();
            var health = EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(Directory.GetFiles(directory, "coverage.json", SearchOption.AllDirectories)[0]));
            Assert.That(health.complete, Is.False); Assert.That(health.gaps[0].reason, Is.EqualTo("WriteFailure")); Assert.That(health.written, Is.EqualTo("2"));
        }
        [Test] public void DiskWriteFailureRecoversAndPreservesTheMissingRange()
        {
            string blocked = Path.Combine(directory, "blocked"); File.WriteAllText(blocked, "This file prevents creation of the log directory.");
            var store = new CombatEvidenceStore(blocked);
            try
            {
                Assert.That(store.TryWrite(Record(1)), Is.True);
                using var processed = new ManualResetEventSlim();
                Assert.That(store.Schedule(4096, processed.Set), Is.True);
                Assert.That(processed.Wait(5000), Is.True, "A disk fault must not stop queue consumption.");
                Assert.That(store.LastFailure, Is.Not.Null);
                File.Delete(blocked); Directory.CreateDirectory(blocked);
                Assert.That(store.TryWrite(Record(2)), Is.True);
                store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
                var health = EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(Directory.GetFiles(blocked, "coverage.json", SearchOption.AllDirectories).Single()));
                Assert.That(health.complete, Is.False); Assert.That(health.written, Is.EqualTo("2"));
                Assert.That(health.gaps.Single().first, Is.EqualTo("1")); Assert.That(health.gaps.Single().reason, Is.EqualTo("WriteFailure"));
            }
            finally { store.Dispose(); store.WaitForClose(); }
        }
        [Test] public void RetentionKeepsLatestCheckpointAndHasExplicitBoundary()
        {
            var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { SessionBytes = 100000, TotalBytes = 200000, SegmentBytes = 4000 });
            for (int i = 1; i <= 40; i++)
            {
                var record = Record(i); record.input = new string('x', 2500); record.estimatedBytes = 3000;
                if (i == 20) { record.stage = "replay.checkpoint"; record.input = new ReplayCheckpointSet { engines = Array.Empty<ReplayCheckpoint>() }; }
                store.TryWrite(record);
            }
            store.Dispose(); store.WaitForClose();
            Assert.That(Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length), Is.LessThanOrEqualTo(100000));
            Assert.That(Directory.GetFiles(directory, "retention.json", SearchOption.AllDirectories), Has.Length.EqualTo(1));
            var remaining = Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories).SelectMany(File.ReadAllLines).ToArray();
            Assert.That(remaining.Any(l => l.Contains("replay.checkpoint")), Is.True);
        }
        [Test] public void EvidenceDoesNotChangeCombatAndProducesQueryableSample()
        {
            var expected = Gateway(); var expectedBatch = expected.ProcessBatch(1, Batch(1), 3);
            var gateway = Gateway(); var sink = new MemorySink(); CombatEvidence.Sink = sink;
            var actual = gateway.ProcessBatch(1, Batch(1), 3);
            gateway.ProcessBatch(1, Batch(2), 3);
            var replica = new CanonicalWorldReplica(); replica.Apply(actual);
            CombatEvidence.Sink = null;
            Assert.That(EvidenceJson.Encode(actual), Is.EqualTo(EvidenceJson.Encode(expectedBatch)));
            Assert.That(sink.records.Any(r => r.stage == "gateway.decision" && r.reason == "DuplicateEvent"), Is.True);
            Assert.That(sink.records.Any(r => r.stage == "gateway.canonical_link"), Is.True);
            string sample = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_SAMPLE_DIRECTORY");
            if (string.IsNullOrEmpty(sample)) sample = Path.Combine(directory, "sample");
            var store = new CombatEvidenceStore(sample);
            foreach (var record in sink.records) store.TryWrite(record);
            store.Dispose(); store.WaitForClose();
        }
        private sealed class MemorySink : IDiagnosticSink
        {
            private readonly Dictionary<object, string> engines = new();
            public readonly List<DiagnosticRecord> records = new();
            public bool TryWrite(DiagnosticRecord record)
            {
                record.captureId = "0123456789abcdef0123456789abcdef"; record.runId = "sample"; record.round = 1;
                record.recordSequence = (records.Count + 1).ToString(); record.utc = DateTime.UtcNow.ToString("o");
                records.Add(EvidenceJson.Decode<DiagnosticRecord>(EvidenceJson.Encode(record))); return true;
            }
            public string RegisterEngine(object target, string domain, Func<object, object> capture)
            {
                if (engines.TryGetValue(target, out var engine)) return engine;
                engine = domain + "-" + (engines.Count + 1); engines.Add(target, engine);
                TryWrite(new DiagnosticRecord { stage = "replay.engine_checkpoint", engine = engine,
                    input = new ReplayCheckpoint { domain = domain, engine = engine, state = capture(target) } });
                return engine;
            }
        }
        private static DiagnosticRecord Record(int sequence) => new DiagnosticRecord { captureId = "0123456789abcdef0123456789abcdef", runId = "test", round = 1,
            recordSequence = sequence.ToString(), role = "Server", stage = "test", input = new { value = sequence } };
    }
}
