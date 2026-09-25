using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidencePayloadPlacementTests
    {
        [TestCase("movement.submit", 16384, false)]
        [TestCase("movement.submit", 16385, true)]
        [TestCase("movement.receive", 4096, false)]
        [TestCase("movement.receive", 4097, true)]
        [TestCase("replay.input", 8192, true)]
        [TestCase("replay.output", 8192, true)]
        [TestCase("owner.attack_stats", 64, true)]
        [TestCase("replay.checkpoint", 64, true)]
        [TestCase("replay.engine_checkpoint", 64, true)]
        public void OnlyMovementSubmissionHasLargerUtf8InlineLimit(string stage, int bytes, bool external)
        {
            string directory = TemporaryDirectory();
            var memory = new DiagnosticMemoryBudget();
            JObject input = SizedPayload(bytes);
            try
            {
                using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { Memory = memory });
                Assert.That(store.TryWrite(Record(1, stage, input)), Is.True);
                Close(store);
                DiagnosticRecord record = ReadRecords(directory).Single();
                string reference = record.inputRef ?? record.checkpointRef;
                Assert.That(reference != null, Is.EqualTo(external));
                if (external)
                {
                    Assert.That(record.input, Is.Null);
                    Assert.That(reference, Does.StartWith(stage == "replay.checkpoint" || stage == "replay.engine_checkpoint" ? "checkpoints/" : "inputs/"));
                }
                Assert.That(JToken.DeepEquals(ReadInput(directory, record), input), Is.True);
                Assert.That(Directory.GetFiles(directory, "*.json.gz", SearchOption.AllDirectories).Length, Is.EqualTo(external ? 1 : 0));
                Assert.That(store.Dropped, Is.Zero);
                Assert.That(store.LastFailure, Is.Null);
                Assert.That(memory.Used, Is.Zero);
            }
            finally { DeleteDirectory(directory); }
        }

        [Test] public void MovementBoundaryUsesUtf8BytesAndNotCharacterCount()
        {
            string directory = TemporaryDirectory();
            var input = new JObject { ["value"] = new string('界', 6000) };
            Assert.That(EvidenceJson.Encode(input).Length, Is.LessThan(16384));
            Assert.That(Encoding.UTF8.GetByteCount(EvidenceJson.Encode(input)), Is.GreaterThan(16384));
            try
            {
                using var store = new CombatEvidenceStore(directory);
                Assert.That(store.TryWrite(Record(1, "movement.submit", input)), Is.True);
                Close(store);
                var record = ReadRecords(directory).Single();
                Assert.That(record.inputRef, Is.Not.Null);
                Assert.That(JToken.DeepEquals(ReadInput(directory, record), input), Is.True);
                Assert.That(store.LastFailure, Is.Null);
            }
            finally { DeleteDirectory(directory); }
        }

        [Test] public void SharedDependenciesStayExternalAndExistBeforeEventDurableFlush()
        {
            string directory = TemporaryDirectory();
            var memory = new DiagnosticMemoryBudget();
            var storage = new ReferenceCheckingStorage();
            try
            {
                using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { Memory = memory, Storage = storage });
                Assert.That(SharedEvidencePayload.TryCapture(new CombatSubmissionBatch { Results = new[] { new CombatResult { Damage = 7 } } }, memory, out var shared), Is.True);
                using (shared)
                {
                    Assert.That(store.TryWrite(Record(1, "movement.submit", new object[] { shared, new string('x', 8000) })), Is.True);
                    Assert.That(store.TryWrite(Record(2, "movement.submit", new object[] { shared, new string('x', 8000) })), Is.True);
                }
                Assert.That(store.TryWrite(Record(3, "movement.submit", SizedPayload(16385))), Is.True);
                Assert.That(store.TryWrite(Record(4, "replay.engine_checkpoint", new { state = 7 })), Is.True);
                Close(store);
                var records = ReadRecords(directory);
                Assert.That(records, Has.Length.EqualTo(4));
                Assert.That(records[0].inputRef, Is.Null);
                Assert.That(records[1].inputRef, Is.Null);
                var first = JToken.FromObject(records[0].input)[0];
                var second = JToken.FromObject(records[1].input)[0];
                Assert.That((string)first["$evidenceRef"], Is.Not.Null);
                Assert.That(JToken.DeepEquals(first, second), Is.True, "One shared version retains one external dependency.");
                Assert.That(Directory.GetFiles(directory, "*.json.gz", SearchOption.AllDirectories), Has.Length.EqualTo(3));
                Assert.That(storage.CheckedReferences, Is.GreaterThanOrEqualTo(4));
                Assert.That(store.LastFailure, Is.Null, "No event flush may precede a readable, complete referenced blob.");
                Assert.That(store.Dropped, Is.Zero);
                Assert.That(memory.Used, Is.Zero, "Inline placement does not leak queue/shared reservations.");
            }
            finally { DeleteDirectory(directory); }
        }

        [Test] public void InlineMovementKeepsQueueAdmissionBoundAndReleasesBudgetAfterDrain()
        {
            string directory = TemporaryDirectory();
            var memory = new DiagnosticMemoryBudget();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            try
            {
                using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions {
                    Memory = memory, QueueBytes = 96 << 10, ReservedBytes = 0 });
                Assert.That(store.Schedule(512, () => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); }), Is.True);
                Assert.That(entered.Wait(5000), Is.True);
                Assert.That(store.TryWrite(Record(1, "movement.submit", SizedPayload(16384))), Is.True);
                Assert.That(store.TryWrite(Record(2, "movement.submit", SizedPayload(16384))), Is.False);
                Assert.That(store.PendingBytes, Is.LessThanOrEqualTo(96 << 10));
                Assert.That(memory.Used, Is.LessThanOrEqualTo(memory.Limit));
                release.Set(); Close(store);
                Assert.That(ReadRecords(directory).Select(record => record.recordSequence), Is.EqualTo(new[] { "1" }));
                Assert.That(store.Dropped, Is.EqualTo(1));
                Assert.That(memory.Used, Is.Zero);
                var coverage = JObject.Parse(File.ReadAllText(Path.Combine(directory, "run/1/sources/capture/coverage.json")));
                Assert.That((bool)coverage["complete"], Is.False, "Changing placement must not hide a rejected record.");
            }
            finally { release.Set(); DeleteDirectory(directory); }
        }

        [Test] public void ExplicitInputReferenceSurvivesReopenWithoutAnotherBlob()
        {
            string directory = TemporaryDirectory();
            var memory = new DiagnosticMemoryBudget();
            JObject input = SizedPayload(8192);
            try
            {
                using (var original = new CombatEvidenceStore(directory, new EvidenceStoreOptions { Memory = memory }))
                {
                    Assert.That(original.TryWrite(Record(1, "replay.input", input)), Is.True);
                    Close(original); Assert.That(original.LastFailure, Is.Null);
                }
                string reference = ReadRecords(directory).Single().inputRef;
                Assert.That(reference, Is.Not.Null);
                using (var reopened = new CombatEvidenceStore(directory, new EvidenceStoreOptions { Memory = memory }))
                {
                    var record = Record(2, "movement.submit", null); record.inputRef = reference;
                    Assert.That(reopened.TryWrite(record), Is.True);
                    Close(reopened); Assert.That(reopened.LastFailure, Is.Null);
                }
                var records = ReadRecords(directory);
                Assert.That(records, Has.Length.EqualTo(2));
                Assert.That(records[1].input, Is.Null);
                Assert.That(records[1].inputRef, Is.EqualTo(reference));
                Assert.That(JToken.DeepEquals(ReadInput(directory, records[1]), input), Is.True);
                Assert.That(Directory.GetFiles(directory, "*.json.gz", SearchOption.AllDirectories), Has.Length.EqualTo(1));
                Assert.That(memory.Used, Is.Zero);
            }
            finally { DeleteDirectory(directory); }
        }

        private static JObject SizedPayload(int bytes)
        {
            var value = new JObject { ["value"] = "" };
            int overhead = Encoding.UTF8.GetByteCount(EvidenceJson.Encode(value));
            value["value"] = new string('x', bytes - overhead);
            Assert.That(Encoding.UTF8.GetByteCount(EvidenceJson.Encode(value)), Is.EqualTo(bytes));
            return value;
        }
        private static DiagnosticRecord Record(int sequence, string stage, object input) => new DiagnosticRecord {
            captureId = "capture", runId = "run", round = 1, recordSequence = sequence.ToString(),
            stage = stage, input = input, critical = true, estimatedBytes = 64 << 10 };
        private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "evidence-placement-" + Guid.NewGuid().ToString("N"));
        private static void DeleteDirectory(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
        private static void Close(CombatEvidenceStore store) { store.Dispose(); Assert.That(store.WaitForClose(), Is.True); }
        private static DiagnosticRecord[] ReadRecords(string directory) => Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal).SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
        private static JToken ReadInput(string directory, DiagnosticRecord record)
        {
            string reference = record.inputRef ?? record.checkpointRef;
            if (reference == null) return JToken.FromObject(record.input);
            string path = Path.Combine(directory, "run/1/sources/capture", reference);
            return JToken.Parse(Encoding.UTF8.GetString(EvidenceJson.Decompress(File.ReadAllBytes(path), 16 << 20)));
        }
        private sealed class ReferenceCheckingStorage : IEvidenceStorage
        {
            private readonly FileEvidenceStorage real = new();
            public int CheckedReferences;
            public Stream OpenAppend(string path) => real.OpenAppend(path);
            public void Flush(Stream stream, bool durable)
            {
                if (durable && stream is FileStream events)
                    foreach (var record in ReadLinesShared(events.Name).SelectMany(EvidenceBlocks.Decode))
                    {
                        var refs = new HashSet<string>();
                        if (record.inputRef != null) refs.Add(record.inputRef);
                        if (record.checkpointRef != null) refs.Add(record.checkpointRef);
                        if (record.input != null && JToken.FromObject(record.input) is JContainer container)
                            foreach (var token in container.DescendantsAndSelf().OfType<JObject>())
                                if (token["$evidenceRef"] != null) refs.Add((string)token["$evidenceRef"]);
                        foreach (string reference in refs)
                        {
                            string path = Path.Combine(Path.GetDirectoryName(events.Name), reference);
                            byte[] raw = EvidenceJson.Decompress(File.ReadAllBytes(path), 16 << 20);
                            if (Path.GetFileName(path) != EvidenceJson.Hash(raw) + ".json.gz") throw new InvalidDataException("Referenced payload not complete before event flush.");
                            CheckedReferences++;
                        }
                    }
                real.Flush(stream, durable);
            }
            private static IEnumerable<string> ReadLinesShared(string path)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                string line; while ((line = reader.ReadLine()) != null) yield return line;
            }
        }
    }
}
