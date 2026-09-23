using System;
using System.IO;
using System.Reflection;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceRecoveryHistoryTests
    {
        [Test] public void LaterFailureAndRecoveryDoNotOverwriteTheFirstCertifiedWindow()
        {
            var state = new EvidenceCoverage();
            Fault(state, 2, 2); Recover(state, 4);
            string first = History(state)[0].ToString();
            Fault(state, 7, 7);
            Assert.That(History(state).Count, Is.EqualTo(2));
            Assert.That(History(state)[0].ToString(), Is.EqualTo(first));
            Assert.That((string)History(state)[1]["first"], Is.EqualTo("7"));
            Assert.That(History(state)[1]["reliableFromSequence"], Is.Null);
            Recover(state, 8);
            Assert.That(History(state)[0].ToString(), Is.EqualTo(first));
            Assert.That((string)History(state)[1]["reliableFromSequence"], Is.EqualTo("8"));
            Assert.That((long)History(state)[1]["epoch"], Is.EqualTo(state.failureEpoch));
            Assert.That(state.failureFirstSequence, Is.EqualTo("2")); Assert.That(state.failureLastSequence, Is.EqualTo("7"));
            Assert.That(state.reliableFromSequence, Is.EqualTo("8"), "Old-reader fields remain backward compatible.");
        }

        [Test] public void QueueOnlyFailuresCreateSeparateEpisodesWithoutAFailureString()
        {
            var state = new EvidenceCoverage();
            Call("Gap", state, "2", "QueueOverload"); Recover(state, 4);
            Call("Gap", state, "7", "QueueOverload");
            var history = History(state);
            Assert.That(state.failure, Is.Null); Assert.That(history.Count, Is.EqualTo(2));
            Assert.That((string)history[0]["reliableFromSequence"], Is.EqualTo("4"));
            Assert.That((string)history[1]["first"], Is.EqualTo("7")); Assert.That(history[1]["reliableFromSequence"], Is.Null);
        }

        [Test] public void OnlyACheckpointFromTheLatestFailureEpochCanCloseAnEpisode()
        {
            var state = new EvidenceCoverage(); Fault(state, 2, 2);
            SetCandidate(state, 4); Fault(state, 5, 5);
            state.flushed = "6"; Call("PublishRecovery", state);
            Assert.That(state.recoveryPending, Is.True);
            Assert.That(History(state).Count, Is.EqualTo(1)); Assert.That(History(state)[0]["reliableFromSequence"], Is.Null);
            SetCandidate(state, 7); state.flushed = "6"; Call("PublishRecovery", state);
            Assert.That(History(state)[0]["reliableFromSequence"], Is.Null, "An unflushed checkpoint is not a recovery certificate.");
            state.flushed = "7"; Call("PublishRecovery", state);
            Assert.That((string)History(state)[0]["reliableFromSequence"], Is.EqualTo("7"));
        }

        [Test] public void HistoryCapacityCoalescesTheOldestEpisodesAndKeepsRecentWindows()
        {
            var state = new EvidenceCoverage();
            for (ulong i = 0; i < 130; i++) { Fault(state, i * 3 + 1, i * 3 + 1); Recover(state, i * 3 + 2); }
            var history = History(state);
            Assert.That(history.Count, Is.EqualTo(128)); Assert.That((bool)history[0]["conservative"], Is.True);
            Assert.That((string)history[0]["first"], Is.EqualTo("1")); Assert.That((string)history[0]["last"], Is.EqualTo("7"));
            Assert.That((string)history[0]["reliableFromSequence"], Is.EqualTo("8"));
            Assert.That((string)history[127]["first"], Is.EqualTo("388")); Assert.That((string)history[127]["reliableFromSequence"], Is.EqualTo("389"));
        }

        [Test] public void LegacyUnknownFailureRemainsUnknownUntilANewDurableCheckpoint()
        {
            var state = EvidenceJson.Decode<EvidenceCoverage>("{\"failure\":\"Old unlocated failure\",\"failureEpoch\":2,\"recoveryPending\":true}");
            Fault(state, 7, 7);
            var episode = History(state)[0];
            Assert.That(episode["first"], Is.Null); Assert.That(episode["last"], Is.Null);
            Assert.That((bool)episode["conservative"], Is.True);
            Recover(state, 8); Fault(state, 10, 10);
            Assert.That((string)History(state)[0]["reliableFromSequence"], Is.EqualTo("8"));
            Assert.That((string)History(state)[1]["first"], Is.EqualTo("10"));
        }

        [Test] public void EmptyTailFailureCanRecoverAtTheFirstActualRecordWithoutRelaxingRealLosses()
        {
            var state = new EvidenceCoverage(); Fault(state, 2, 2); Recover(state, 4);
            Call("MarkTailFailure", state, (ulong)7);
            Assert.That((bool)History(state)[1]["tailMarker"], Is.True);
            Assert.That((string)History(state)[1]["first"], Is.EqualTo("7"));
            Recover(state, 7);
            Assert.That((string)History(state)[1]["reliableFromSequence"], Is.EqualTo("7"));
            var mixed = new EvidenceCoverage(); Fault(mixed, 2, 2); Call("MarkTailFailure", mixed, (ulong)7);
            Assert.That((string)History(mixed)[0]["last"], Is.EqualTo("2"), "An empty write adds no missing record to an already pending episode.");
            var replaced = new EvidenceCoverage(); Call("MarkTailFailure", replaced, (ulong)7); Fault(replaced, 7, 7);
            Assert.That((bool?)History(replaced)[0]["tailMarker"], Is.Not.True, "A real failure at the frontier must require a later checkpoint.");
        }

        [Test] public void StorePublishesBothDurableRecoveryCertificates()
        {
            string directory = Path.Combine(Path.GetTempPath(), "evidence-recovery-history-" + Guid.NewGuid().ToString("N"));
            CombatEvidenceStore store = null;
            try
            {
                store = new CombatEvidenceStore(directory);
                store.ReportCaptureFailure(Record(2));
                Assert.That(store.TryWrite(Checkpoint(4)), Is.True);
                Assert.That(SpinWait.SpinUntil(() => (string)Read(directory)?["reliableFromSequence"] == "4", 5000), Is.True);
                Assert.That(store.TryWrite(Record(5)), Is.True); Assert.That(store.TryWrite(Record(6)), Is.True);
                Assert.That(SpinWait.SpinUntil(() => (string)Read(directory)?["flushed"] == "6", 5000), Is.True);
                store.ReportCaptureFailure(Record(7));
                Assert.That(store.TryWrite(Checkpoint(8)), Is.True);
                Assert.That(SpinWait.SpinUntil(() => (string)Read(directory)?["reliableFromSequence"] == "8", 5000), Is.True);
                var history = (JArray)Read(directory)["integrityHistory"];
                Assert.That(history, Is.Not.Null); Assert.That(history.Count, Is.EqualTo(2));
                Assert.That((string)history[0]["reliableFromSequence"], Is.EqualTo("4")); Assert.That((string)history[1]["reliableFromSequence"], Is.EqualTo("8"));
            }
            finally
            {
                store?.Dispose(); if (store != null) Assert.That(store.WaitForClose(), Is.True);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static JArray History(EvidenceCoverage state)
        {
            var body = JObject.Parse(EvidenceJson.Encode(state));
            Assert.That((int?)body["integrityHistoryVersion"], Is.EqualTo(1), "The writer must distinguish complete history from legacy aggregate metadata.");
            var history = body["integrityHistory"] as JArray; Assert.That(history, Is.Not.Null); return history;
        }
        private static void Fault(EvidenceCoverage state, ulong first, ulong last) => Call("MarkFailureRange", state, first, last);
        private static void Recover(EvidenceCoverage state, ulong sequence) { SetCandidate(state, sequence); state.flushed = sequence.ToString(); Call("PublishRecovery", state); }
        private static void SetCandidate(EvidenceCoverage state, ulong sequence)
        {
            typeof(EvidenceCoverage).GetField("recoveryCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, sequence.ToString());
            typeof(EvidenceCoverage).GetField("recoveryCheckpointEpoch", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, state.failureEpoch);
        }
        private static void Call(string method, params object[] arguments) => typeof(CombatEvidenceStore).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);
        private static DiagnosticRecord Record(int sequence) => new DiagnosticRecord { captureId = "capture", runId = "run", round = 1,
            recordSequence = sequence.ToString(), role = "Process", stage = "test.record", critical = true, estimatedBytes = 4096 };
        private static DiagnosticRecord Checkpoint(int sequence)
        {
            var record = Record(sequence); record.stage = "replay.checkpoint";
            record.input = new ReplayCheckpointSet { engines = new[] { new ReplayCheckpoint { engine = "status-1", domain = "status", state = new { time = 0 } } } };
            return record;
        }
        private static JObject Read(string directory)
        {
            try { string path = Path.Combine(directory, "run", "1", "sources", "capture", "coverage.json"); return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null; }
            catch (IOException) { return null; }
        }
    }
}
