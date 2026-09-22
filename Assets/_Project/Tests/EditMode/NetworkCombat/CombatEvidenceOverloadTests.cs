using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceOverloadTests
    {
        private string directory;
        [SetUp] public void SetUp() => Directory.CreateDirectory(directory = Path.Combine(Path.GetTempPath(), "evidence-load-" + Guid.NewGuid().ToString("N")));
        [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Test] public void AbsentPayloadDoesNotConsumeSixKiBOfQueueBudget()
        {
            Assert.That(CombatEvidenceRuntime.RetainedBytes(null), Is.Zero);
            Assert.That(CombatEvidenceRuntime.RetainedBytes(new object[] { 1f / 144 }), Is.LessThan(256));
        }

        [Test] public void MoreThan128SeparatedGapsRetainAConservativeRangeForEveryLoss()
        {
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { QueueBytes = 8192, ReservedBytes = 4096 });
            for (int i = 1; i <= 400; i += 2)
                Assert.That(store.TryWrite(Record(i, 1, true, 16000)), Is.False);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
            var coverage = EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(Directory.GetFiles(directory, "coverage.json", SearchOption.AllDirectories).Single()));
            for (ulong i = 1; i <= 400; i += 2)
                Assert.That(coverage.gaps.Any(g => ulong.Parse(g.first) <= i && ulong.Parse(g.last) >= i), Is.True, "Missing failure evidence for " + i);
            Assert.That(coverage.gaps.Count, Is.LessThanOrEqualTo(128));
            Assert.That(coverage.complete, Is.False);
        }

        [Test] public void FiftyControllersKeepExactInputsWithAtLeast80PercentLessDiskData()
        {
            // Same dominant operation shape as the incident: interleaved controller Advance input/output.
            using var store = new CombatEvidenceStore(directory);
            long uncompressed = 0; int sequence = 0;
            for (int frame = 0; frame < 80; frame++)
                for (int controller = 1; controller <= 50; controller++)
                    for (int phase = 0; phase < 2; phase++)
                    {
                        var record = Record(++sequence, controller, phase == 0, 1024);
                        record.frame = frame; record.monotonicTime = frame / 144d;
                        record.input = phase == 0 ? new object[] { frame % 3 == 0 ? 0.007f : 1f / 144 } : null;
                        uncompressed += Encoding.UTF8.GetByteCount(EvidenceJson.Encode(record)) + 1;
                        Assert.That(store.TryWrite(record), Is.True);
                    }
            store.Dispose(); Assert.That(store.WaitForClose(30000), Is.True);
            long disk = Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
            TestContext.WriteLine("records=" + sequence + " plainBytes=" + uncompressed + " storedBytes=" + disk);
            Assert.That(store.Dropped, Is.Zero);
            Assert.That(disk, Is.LessThanOrEqualTo(uncompressed / 5));
            var decoded = Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories).OrderBy(p => p)
                .SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
            Assert.That(decoded.Length, Is.EqualTo(sequence));
            for (int i = 0; i < decoded.Length; i++)
            {
                Assert.That(decoded[i].recordSequence, Is.EqualTo((i + 1).ToString()));
                Assert.That(decoded[i].stage, Is.EqualTo(i % 2 == 0 ? "replay.input" : "replay.output"));
                if (i % 2 == 0)
                    Assert.That(Newtonsoft.Json.Linq.JArray.FromObject(decoded[i].input)[0].Value<float>(),
                        Is.EqualTo((i / 100) % 3 == 0 ? 0.007f : 1f / 144));
            }
        }

        [Test] public void TypedAdvancesPreserveInterleavingFailuresAndExactFloatingPointSteps()
        {
            using var store = new CombatEvidenceStore(directory);
            long ticks = new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc).Ticks;
            var begin = new DiagnosticAdvance { sequence = 1, role = "status", engine = "status-1", operation = "Advance", phase = 0,
                delta = 0.006944445f, utcTicks = ticks, frame = 2, monotonic = 1.25, boundary = new StatusReplayBoundary { supported = true } };
            Assert.That(store.TryWriteAdvance("capture", "run", 1, begin), Is.True);
            var decision = Record(2, 1, false, 1024); decision.captureId = "capture"; decision.stage = "status.tick";
            Assert.That(store.TryWrite(decision), Is.True);
            var end = begin; end.sequence = 3; end.phase = 2; end.boundary = null; end.utcTicks++; end.monotonic += .0001;
            Assert.That(store.TryWriteAdvance("capture", "run", 1, end), Is.True);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
            var records = Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories).SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
            Assert.That(records.Select(r => r.recordSequence), Is.EqualTo(new[] { "1", "2", "3" }));
            Assert.That(JArray.FromObject(records[0].input)[0].Value<float>(), Is.EqualTo(begin.delta));
            Assert.That(records[1].stage, Is.EqualTo("status.tick"));
            Assert.That(records[2].outcome, Is.EqualTo("Aborted"));
            Assert.That(records[2].monotonicTime, Is.EqualTo(end.monotonic));
            Assert.That(store.Memory.Used, Is.Zero);
        }

        [Test] public void InjectedFlushFailureHasAConservativeGapAndFollowingRecordsRemainReadable()
        {
            var storage = new FailingFlushStorage();
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { Storage = storage });
            Assert.That(store.TryWrite(Record(1, 1, true, 1024)), Is.True);
            using var processed = new System.Threading.ManualResetEventSlim();
            Assert.That(store.Schedule(1024, processed.Set), Is.True);
            Assert.That(processed.Wait(5000), Is.True);
            Assert.That(System.Threading.SpinWait.SpinUntil(() => store.LastFailure != null, 3000), Is.True);
            storage.fail = false;
            Assert.That(store.TryWrite(Record(2, 1, true, 1024)), Is.True);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
            var coverage = EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(Directory.GetFiles(directory, "coverage.json", SearchOption.AllDirectories).Single()));
            Assert.That(coverage.complete, Is.False);
            Assert.That(coverage.gaps.Any(g => ulong.Parse(g.first) <= 1 && ulong.Parse(g.last) >= 1), Is.True);
            Assert.That(Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories).SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode)
                .Any(r => r.recordSequence == "2"), Is.True);
            Assert.That(store.Memory.Used, Is.Zero);
        }
        private sealed class FailingFlushStorage : IEvidenceStorage
        {
            private readonly FileEvidenceStorage real = new();
            public volatile bool fail = true;
            public Stream OpenAppend(string path) => real.OpenAppend(path);
            public void Flush(Stream stream, bool durable) { if (fail) throw new IOException("InjectedFlushFailure"); real.Flush(stream, durable); }
        }

        [Test] public void FlushFailureCoversSuccessfulRecordsBetweenEarlierQueueGaps()
        {
            using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { QueueBytes = 8192, ReservedBytes = 4096, Storage = new FailingFlushStorage() });
            using var entered = new System.Threading.ManualResetEventSlim(); using var release = new System.Threading.ManualResetEventSlim();
            try
            {
                Assert.That(store.Schedule(512, () => { entered.Set(); release.Wait(5000); }), Is.True);
                Assert.That(entered.Wait(5000), Is.True);
                Assert.That(store.TryWrite(Record(1, 1, true, 16000)), Is.False);
                Assert.That(store.TryWrite(Record(2, 1, true, 1024)), Is.True);
                Assert.That(store.TryWrite(Record(3, 1, true, 16000)), Is.False);
                Assert.That(store.TryWrite(Record(4, 1, true, 1024)), Is.True);
                release.Set(); store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
                var coverage = EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(Directory.GetFiles(directory, "coverage.json", SearchOption.AllDirectories).Single()));
                TestContext.WriteLine(EvidenceJson.Encode(coverage));
                for (ulong i = 1; i <= 4; i++) Assert.That(coverage.gaps.Any(g => ulong.Parse(g.first) <= i && ulong.Parse(g.last) >= i), Is.True, "Unreported failed sequence " + i);
                Assert.That(coverage.complete, Is.False);
            }
            finally { release.Set(); }
        }

        [Test] public void NestedEncodingFieldIsOrdinaryEvidenceAndUnsupportedVersionsAreRejected()
        {
            var record = Record(1, 1, true, 1024); record.input = new { encoding = "plain" };
            Assert.That(EvidenceBlocks.Decode(EvidenceJson.Encode(record)).Single().recordSequence, Is.EqualTo("1"));
            record.schemaVersion = 99;
            Assert.Throws<InvalidDataException>(() => EvidenceBlocks.Decode(EvidenceJson.Encode(record)).ToArray());
        }

        [Test] public void RepeatedKnockbackDefinitionsUseOneVerifiedSharedInput()
        {
            using var store = new CombatEvidenceStore(directory);
            var settings = new EnemyKnockbackSettings { Distance = 2, SpeedMultiplier = 1,
                CurveKeys = new[] { new EnemyKnockbackCurveKey { Time = 0, Value = 1 }, new EnemyKnockbackCurveKey { Time = 1 } } };
            for (int i = 1; i <= 20; i++)
            {
                var record = Record(i, 1, true, 2048); record.stage = "movement.input";
                record.input = new { entity = i, runtime = new { KnockbackSettings = settings } };
                Assert.That(store.TryWrite(record), Is.True);
            }
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
            var blobs = Directory.GetFiles(directory, "*.json.gz", SearchOption.AllDirectories);
            Assert.That(blobs.Length, Is.EqualTo(1));
            byte[] raw = EvidenceJson.Decompress(File.ReadAllBytes(blobs[0]), 4096);
            Assert.That(Path.GetFileName(blobs[0]), Is.EqualTo(EvidenceJson.Hash(raw) + ".json.gz"));
            var records = Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories).SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
            Assert.That(records.Length, Is.EqualTo(20));
            foreach (var record in records) Assert.That(JObject.FromObject(record.input)["runtime"]["KnockbackSettings"]["$evidenceRef"].Value<string>(),
                Is.EqualTo("inputs/" + Path.GetFileName(blobs[0])));
        }

        [Test] public void BinaryAdvancesPreserveLargeSequencesAndExactNumberBits()
        {
            var entries = new DiagnosticAdvance[6];
            for (int i = 0; i < entries.Length; i++) entries[i] = new DiagnosticAdvance {
                sequence = ulong.MaxValue - 5 + (ulong)i, utcTicks = new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc).Ticks + i,
                role = "status", engine = "status-1", operation = "Advance", frame = 144, fixedStep = 50,
                phase = i == 5 ? 2 : i % 2, delta = i % 2 == 0 ? (i == 2 ? .006944445f : .12345679f) : 0,
                monotonic = 12345.125 + i / 700d, network = 17.31415926,
                boundary = i % 2 == 0 ? new StatusReplayBoundary { supported = true, executeAll = true,
                    ids = new EventSequenceState { slot = 2, epoch = 3, next = uint.MaxValue } } : null };
            string encoded = EvidenceBlocks.EncodeAdvances("capture", "run", 1, entries, entries.Length);
            var decoded = EvidenceBlocks.Decode(encoded).ToArray();
            for (int i = 0; i < entries.Length; i++)
                Assert.That(EvidenceJson.Encode(decoded[i]), Is.EqualTo(EvidenceJson.Encode(entries[i].Expand("capture", "run", 1))));
            string sample = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_SAMPLE_DIRECTORY");
            if (!string.IsNullOrEmpty(sample))
            {
                Directory.CreateDirectory(sample);
                File.WriteAllText(Path.Combine(sample, "advance-binary.jsonl"), encoded + "\n");
                File.WriteAllText(Path.Combine(sample, "advance-expanded.json"), EvidenceJson.Encode(decoded));
            }
        }

        [Test] public void FiveHundredEntityCheckpointEstimateFitsReservedBudget()
        {
            var authority = new ServerEnemySimulationRegistry(); var replica = new CanonicalWorldReplica(); var ledger = new CombatLedger();
            for (uint id = 1; id <= 500; id++)
            {
                authority.RegisterEnemy(id, UnityEngine.Vector2.zero, 0);
                ledger.RegisterEntity(id, 100, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
                replica.RegisterStatusController(id, new StatusController(_ => { }));
            }
            var checkpoint = new ReplayCheckpointSet { engines = new[] {
                new ReplayCheckpoint { domain = "authority", engine = "authority", state = authority.CaptureReplayState() },
                new ReplayCheckpoint { domain = "replica", engine = "replica", state = replica.CaptureReplayState() },
                new ReplayCheckpoint { domain = "ledger", engine = "ledger", state = ledger.CaptureReplayState() } } };
            Assert.That(CombatEvidenceRuntime.RetainedBytes(checkpoint), Is.LessThan(8 << 20));
            using var store = new CombatEvidenceStore(directory);
            var record = Record(1, 1, true, 8 << 20); record.stage = "replay.checkpoint";
            Assert.That(store.TryWrite(record, false, () => checkpoint), Is.True);
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
            Assert.That(store.LastFailure, Is.Null);
        }

        [Test] public void ReplicatedBlocksAppendAtTheAcknowledgedOffsetAndDuplicateAckIsIdempotent()
        {
            using var store = new CombatEvidenceStore(directory);
            string path = "remote/1/sources/capture/events-00000000000000000001.jsonl";
            var random = new Random(903); byte[] expected = new byte[100000]; random.NextBytes(expected);
            int offset = 0;
            while (offset < expected.Length)
            {
                byte[] block = expected.Skip(offset).Take(32768).ToArray();
                long acknowledged = store.ImportBlock(path, offset, block, EvidenceJson.Hash(block), false, expected.Length);
                Assert.That(acknowledged, Is.EqualTo(offset + block.Length));
                Assert.That(store.ImportBlock(path, offset, block, EvidenceJson.Hash(block), false, expected.Length), Is.EqualTo(acknowledged));
                offset += block.Length;
            }
            Assert.That(File.ReadAllBytes(store.Resolve(path)), Is.EqualTo(expected));
            store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
        }

        [Test] public void DeepDiagnosticRootCanPersistAndReadLongHashedPayloadPaths()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows long-path regression.");
            using var store = new CombatEvidenceStore(Path.Combine(directory, new string('d', 130)));
            try
            {
                var record = Record(1, 1, true, 32000); record.input = new string('p', 6000);
                Assert.That(store.TryWrite(record), Is.True);
                store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
                Assert.That(store.LastFailure, Is.Null);
                string blob = Directory.GetFiles(store.Root, "*.json.gz", SearchOption.AllDirectories).Single();
                Assert.That(blob.Length, Is.GreaterThan(260));
                Assert.That(EvidenceJson.Decompress(File.ReadAllBytes(blob), 32000).Length, Is.GreaterThan(6000));
            }
            finally { store.Dispose(); store.WaitForClose(); if (Directory.Exists(store.Root)) Directory.Delete(store.Root, true); }
        }

        private static DiagnosticRecord Record(int sequence, int controller, bool input, int bytes) => new DiagnosticRecord {
            captureId = "load", runId = "run", round = 1, recordSequence = sequence.ToString(), role = "replica", engine = "replica-1",
            stage = input ? "replay.input" : "replay.output", operation = "controller." + controller + ".Advance",
            outcome = input ? null : "Completed", utc = "2026-09-22T07:38:47.8403752Z", estimatedBytes = bytes, critical = input };
    }
}
