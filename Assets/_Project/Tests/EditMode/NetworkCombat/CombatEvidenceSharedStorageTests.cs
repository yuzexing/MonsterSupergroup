using System;
using System.IO;
using System.Linq;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceSharedStorageTests
    {
        [Test] public void SharedInputSurvivesCreatorReleaseAndDeferredInputEncoding()
        {
            string directory = Path.Combine(Path.GetTempPath(), "evidence-shared-" + Guid.NewGuid().ToString("N"));
            var memory = new DiagnosticMemoryBudget();
            try
            {
                using var store = new CombatEvidenceStore(directory, new EvidenceStoreOptions { Memory = memory });
                var batch = new CombatSubmissionBatch { Results = new[] { new CombatResult { Damage = 7 } } };
                Assert.That(SharedEvidencePayload.TryCapture(batch, memory, out var shared), Is.True);
                using (shared)
                {
                    Assert.That(store.TryWrite(Record(1, "replay.input", new object[] { shared })), Is.True);
                    Assert.That(store.TryWrite(Record(2, "replay.input", new object[] { shared })), Is.True);
                }
                batch.Results[0].Damage = 999;
                store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
                var blobs = Directory.GetFiles(directory, "*.json.gz", SearchOption.AllDirectories);
                Assert.That(blobs, Has.Length.EqualTo(1), "The same frozen version has one durable dependency.");
                string payload = System.Text.Encoding.UTF8.GetString(EvidenceJson.Decompress(File.ReadAllBytes(blobs[0]), 1 << 20));
                Assert.That((int)JObject.Parse(payload)["Results"][0]["Damage"], Is.EqualTo(7));
                var records = Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories)
                    .SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
                Assert.That(records, Has.Length.EqualTo(2));
                Assert.That(JToken.FromObject(records[0].input)[0]["$evidenceRef"], Is.Not.Null);
                Assert.That(store.Dropped, Is.Zero);
                Assert.That(memory.Used, Is.Zero, "Writer completion releases both queue leases and the shared reservation.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static DiagnosticRecord Record(int sequence, string stage, object input) => new DiagnosticRecord {
            captureId = "shared", runId = "run", round = 1, recordSequence = sequence.ToString(),
            stage = stage, engine = "gateway-1", operation = "ProcessBatch", input = input, critical = true, estimatedBytes = 1024 };
    }
}
