using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatInvestigationCostTests
    {
        private sealed class StoreSink : IDiagnosticSink
        {
            private readonly CombatEvidenceStore store;
            public int accepted, rejected;
            private ulong sequence;
            public StoreSink(CombatEvidenceStore store) => this.store = store;
            public bool TryWrite(DiagnosticRecord record)
            {
                record.captureId = "synthetic-capture"; record.runId = "synthetic-run"; record.round = 1;
                record.recordSequence = (++sequence).ToString(); record.monotonicTime = sequence * .25;
                record.utc = "2026-01-01T00:00:00.0000000Z";
                bool result = store.TryWrite(record);
                if (result) accepted++; else rejected++;
                return result;
            }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture) => domain;
        }

        [Test] public void SyntheticMixedInvestigationCostReportsOnAndOffThroughRealStore()
        {
            var previous = CombatEvidence.Sink;
            try
            {
                // Warm both paths and serializer contracts before the one measured pair.
                Run(false, 1); Run(true, 1);
                object off = Run(false, 24), on = Run(true, 24);
                TestContext.Out.WriteLine("INVESTIGATION_SYNTHETIC_COST " + EvidenceJson.Encode(new {
                    schemaVersion = 1, synthetic = true, paced = false, repetitions = 1, cycles = 24,
                    description = "Unpaced component micro-test: each cycle has one build begin, build snapshot, progression snapshot, selection request, selection snapshot, pickup decision and owner result. Both modes include one common baseline record.",
                    limits = "Editor process, local temporary disk, chosen small payload mix; excludes native Steam polling and clock sampling, and is not real gameplay, frame-time, weak-machine or sustained-throughput acceptance. Allocation counter covers producer thread only and is unavailable if its probe fails. Elapsed drain excludes observation export.",
                    off, on }));
            }
            finally
            {
                CombatInvestigationEvidence.Configure(EvidenceProfile.Standard, Array.Empty<string>());
                CombatEvidence.Sink = previous;
            }
        }

        private static object Run(bool enabled, int cycles)
        {
            string directory = Path.Combine(Path.GetTempPath(), "investigation-cost-" + Guid.NewGuid().ToString("N"));
            var store = new CombatEvidenceStore(directory, EvidenceStoreOptions.ForProfile(EvidenceProfile.Diagnostic, true));
            var sink = new StoreSink(store); CombatEvidence.Sink = sink;
            CombatInvestigationEvidence.Configure(EvidenceProfile.Diagnostic, new[] { "--combat-evidence-investigation=" + (enabled ? "on" : "off") });
            bool joined = false;
            try
            {
                Assert.That(sink.TryWrite(new DiagnosticRecord { stage = "synthetic.baseline", estimatedBytes = 512 }), Is.True);
                store.QueueObservation.MarkPhase(EvidenceQueuePhase.Load, Stopwatch.GetTimestamp());
                bool allocationsAvailable = AllocationCounterWorks(out string allocationStatus);
                long allocated = allocationsAvailable ? GC.GetAllocatedBytesForCurrentThread() : 0;
                long started = Stopwatch.GetTimestamp();
                for (int cycle = 0; cycle < cycles; cycle++) EmitCycle(cycle);
                double producerMilliseconds = Elapsed(started);
                long? producerAllocatedBytes = allocationsAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocated : null;
                started = Stopwatch.GetTimestamp();
                store.RequestClose(); joined = store.WaitForClose(30000);
                double drainMilliseconds = Elapsed(started);
                Assert.That(joined, Is.True, "Bounded test fixture writer did not finish.");
                Assert.That(store.Dropped, Is.Zero); Assert.That(sink.rejected, Is.Zero);
                var assessment = store.AssessShutdownCompleteness();
                Assert.That(assessment.complete, Is.True, string.Join(";", assessment.failures));
                var paths = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                long evidenceBytes = paths.Sum(path => new FileInfo(path).Length);
                var records = paths.Where(path => Path.GetFileName(path).StartsWith("events-", StringComparison.Ordinal) && path.EndsWith(".jsonl", StringComparison.Ordinal))
                    .SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
                int expected = enabled ? cycles * 7 : 0;
                Assert.That(records.Count(r => r.stage.StartsWith("investigation.", StringComparison.Ordinal)), Is.EqualTo(expected));
                Assert.That(sink.accepted, Is.EqualTo(expected + 1));
                // Decode every retained dependency too: task completion alone is not success.
                foreach (var record in records.Where(r => r.inputRef != null))
                {
                    string inputPath = Path.Combine(directory, "synthetic-run/1/sources/synthetic-capture", record.inputRef);
                    string input = Encoding.UTF8.GetString(EvidenceJson.Decompress(File.ReadAllBytes(inputPath), 16 << 20));
                    Assert.That(JToken.Parse(input), Is.Not.Null);
                }
                string observationPath = Path.Combine(directory, "cost-observation.json");
                Assert.That(store.ExportQueueObservation(observationPath), Is.True);
                var observation = JObject.Parse(File.ReadAllText(observationPath));
                Assert.That((bool)observation["countsBalanced"], Is.True);
                Assert.That(store.Memory.Used, Is.Zero);
                return new { investigationEnabled = enabled, investigationRecords = expected,
                    logicalRecords = sink.accepted, evidenceBytes, evidenceFiles = paths.Length,
                    queuedTasks = (long)observation["producerTotals"]["workEnqueued"],
                    completedTasks = (long)observation["consumerTotals"]["workCompleted"],
                    chargedBytes = (long)observation["producerTotals"]["chargedBytes"],
                    producerAllocatedBytes, allocationStatus, producerMilliseconds,
                    queuePeakBytes = store.PeakPendingBytes, drainMilliseconds, countsBalanced = true };
            }
            finally
            {
                store.RequestClose(); joined |= store.WaitForClose(30000);
                if (joined) store.QueueObservation?.ReleaseAfterStop(true, true);
                store.Dispose();
                if (joined && Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void EmitCycle(int cycle)
        {
            if (!CombatInvestigationEvidence.Enabled) return;
            uint player = 7; string operationId = (cycle + 1).ToString();
            var identity = new { participantId = "9007199254740993", avatar = player, connectionEpoch = 1,
                run = "synthetic-run", birth = "synthetic-birth" };
            CombatInvestigationEvidence.Capture("build.begin", "Started", () => new {
                entity = player, domain = "build", operationId, operation = "SyntheticEquip", perspective = "Server", identity });
            CombatInvestigationEvidence.Capture("state", "Completed", () => new {
                entity = player, domain = "build", stateRevision = operationId, operationId, perspective = "Server", identity,
                baseline = cycle == 0, continuous = false, state = new { active = true, definition = new PlayerBuildSnapshot {
                    InitialWeaponId = 1, InitialWeaponSlot = 0,
                    Weapons = Enumerable.Range(0, 4).Select(i => new PlayerBuildWeaponSnapshot { SlotIndex = i, WeaponId = (uint)(i + 1) }).ToArray(),
                    Equipment = Enumerable.Range(0, 12).Select(i => new PlayerBuildEquipmentSnapshot { SlotIndex = i / 3, EquipmentId = (uint)(i + 2), LevelIndex = cycle % 3 }).ToArray(),
                    Perks = Enumerable.Range(0, 6).Select(i => new PlayerBuildPerkSnapshot { PerkId = (uint)(i + 20) }).ToArray()
                } }, cause = new { operation = "SyntheticEquip", outcome = "Completed" } });
            CombatInvestigationEvidence.Capture("state", "Committed", () => new {
                entity = player, domain = "progression", stateRevision = operationId, perspective = "Server", identity,
                baseline = cycle == 0, continuous = false, state = new { authoritative = true, experiencePerLevel = 20,
                    snapshot = new { Level = 3, Experience = cycle * .5f, PendingRewards = new[] { 3 } } } });
            CombatInvestigationEvidence.Capture("selection.request", "Requested", () => new {
                identity, action = "Select", eventId = operationId, index = 0, optionId = "1", buildRevision = cycle });
            CombatInvestigationEvidence.Capture("state", "Published", () => new {
                entity = player, domain = "selection", stateRevision = operationId, perspective = "Server", identity,
                baseline = cycle == 0, continuous = false, state = new { selecting = true, pendingEventId = operationId,
                    offers = Enumerable.Range(0, 3).Select(i => new { ContentId = i + 1, OptionId = (i + 1).ToString(), Kind = "Equipment" }).ToArray() } });
            PickupInvestigation.Capture("decision", "Accepted", "synthetic-run", (ulong)cycle + 1, 1, 100, player,
                () => new { benefitConfirmed = false, effect = "RestoreHealth", amount = 12 }, participant: 9007199254740993UL);
            PickupInvestigation.Capture("owner_result", "Applied", "synthetic-run", (ulong)cycle + 1, 1, 100, player,
                () => new { benefitConfirmed = true, previousHealth = 70, currentHealth = 82, restoredHealth = 12 });
        }

        private static bool AllocationCounterWorks(out string status)
        {
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var probe = new byte[32768]; GC.KeepAlive(probe);
                bool available = GC.GetAllocatedBytesForCurrentThread() - before >= probe.Length;
                status = available ? "ProducerThreadCounterVerified" : "CounterDidNotObserveProbeAllocation";
                return available;
            }
            catch (Exception error) { status = error.GetType().Name; return false; }
        }
        private static double Elapsed(long started) => (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
    }
}
