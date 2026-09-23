using System;
using System.IO;
using System.Linq;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using MonsterSupergroup.NetworkCombat.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatReplayBatchTests
    {
        private string directory;
        [SetUp] public void SetUp() => Directory.CreateDirectory(directory = Path.Combine(Path.GetTempPath(), "combat-replay-batch-" + Guid.NewGuid().ToString("N")));
        [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Test] public void EmptyOrUnreliableBatchCannotPass()
        {
            Assert.That(CombatReplayBatchSummary.Classify(Array.Empty<ReplayReport>()).exitCode, Is.EqualTo(3));
            Assert.That(CombatReplayBatchSummary.Classify(new[] { new ReplayReport { reason = "IncompleteOrUnsupportedEvidence" } }).exitCode, Is.EqualTo(3));
            Assert.That(CombatReplayBatchSummary.Classify(new[] { new ReplayReport { reliable = true, passed = true, executed = 0 } }).exitCode, Is.EqualTo(3));
        }

        [Test] public void UnverifiableEvidenceTakesPrecedenceOverBusinessDivergence()
        {
            var passed = new ReplayReport { reliable = true, passed = true, executed = 1 };
            var different = new ReplayReport { reliable = true, executed = 1, firstDivergence = 0 };
            Assert.That(CombatReplayBatchSummary.Classify(new[] { passed }).exitCode, Is.Zero);
            Assert.That(CombatReplayBatchSummary.Classify(new[] { passed, different }).exitCode, Is.EqualTo(2));
            var summary = CombatReplayBatchSummary.Classify(new[] { passed, different, new ReplayReport() });
            Assert.That(summary.exitCode, Is.EqualTo(3));
            Assert.That(summary.passed, Is.EqualTo(1)); Assert.That(summary.diverged, Is.EqualTo(1));
            Assert.That(summary.unreliable, Is.EqualTo(1)); Assert.That(summary.total, Is.EqualTo(3));
        }

        [Test] public void MalformedFileDoesNotHideLaterReplayResults()
        {
            string broken = Path.Combine(directory, "broken.fixture.json"); File.WriteAllText(broken, "{");
            string valid = WriteFixture("valid.fixture.json");
            var results = CombatReplayBatch.EvaluateFiles(new[] { broken, valid });
            Assert.That(results, Has.Length.EqualTo(2));
            Assert.That(results[0].report.reliable, Is.False);
            Assert.That(results[0].report.reason, Does.StartWith("FixtureReadFailed:"));
            Assert.That(results[1].report.passed, Is.True);
            Assert.That(CombatReplayBatchSummary.Classify(results.Select(r => r.report)).exitCode, Is.EqualTo(3));
        }

        [Test] public void ExplicitManifestAcceptsNamedJsonWithoutDiscoveringReports()
        {
            string fixture = WriteFixture("damage-formula-matched.json");
            File.WriteAllText(Path.Combine(directory, "replay-results.json"), "[]");
            string manifest = Path.Combine(directory, "fixtures.json");
            File.WriteAllText(manifest, "{\"fixtures\":[\"damage-formula-matched.json\"]}");
            var paths = CombatReplayBatch.ReadManifest(manifest);
            Assert.That(paths, Is.EqualTo(new[] { fixture }));
            Assert.That(CombatReplayBatch.EvaluateFiles(paths).Single().report.passed, Is.True);
            Assert.That(CombatReplayBatch.EvaluateFiles(Directory.GetFiles(directory, "*.fixture.json")), Is.Empty);
        }

        [Test] public void MissingFileAndNullFixtureAreUnverifiable()
        {
            string missing = Path.Combine(directory, "missing.json");
            string empty = Path.Combine(directory, "null.json"); File.WriteAllText(empty, "null");
            var results = CombatReplayBatch.EvaluateFiles(new[] { missing, empty });
            Assert.That(results.All(r => !r.report.reliable && !r.report.passed), Is.True);
            Assert.That(CombatReplayBatchSummary.Classify(results.Select(r => r.report)).exitCode, Is.EqualTo(3));
        }

        [Test] public void IncompleteFixtureAndCheckpointWithoutStepsCannotPassTheBatch()
        {
            string path = WriteFixture("incomplete.json");
            var fixture = EvidenceJson.Decode<ReplayFixture>(File.ReadAllText(path));
            fixture.complete = false; File.WriteAllText(path, EvidenceJson.Encode(fixture));
            string checkpointOnly = Path.Combine(directory, "checkpoint-only.json");
            fixture.complete = true; fixture.steps = Array.Empty<ReplayStep>(); File.WriteAllText(checkpointOnly, EvidenceJson.Encode(fixture));
            var results = CombatReplayBatch.EvaluateFiles(new[] { path, checkpointOnly });
            Assert.That(results[0].report.reason, Is.EqualTo("IncompleteOrUnsupportedEvidence"));
            Assert.That(results[1].report.reason, Is.EqualTo("NoReplaySteps"));
            Assert.That(results.All(r => !r.report.passed && !r.report.reliable), Is.True);
            Assert.That(CombatReplayBatchSummary.Classify(results.Select(r => r.report)).exitCode, Is.EqualTo(3));
        }

        [Test] public void EmptyManifestIsNotASuccessAndMissingEntriesAreRejected()
        {
            string manifest = Path.Combine(directory, "fixtures.json"); File.WriteAllText(manifest, "{\"fixtures\":[]}");
            Assert.That(CombatReplayBatchSummary.Classify(CombatReplayBatch.EvaluateFiles(CombatReplayBatch.ReadManifest(manifest)).Select(r => r.report)).exitCode, Is.EqualTo(3));
            File.WriteAllText(manifest, "{}");
            Assert.Throws<InvalidDataException>(() => CombatReplayBatch.ReadManifest(manifest));
        }

        private string WriteFixture(string name)
        {
            var state = new MonsterSupergroup.GAS.OutputStatisticsState();
            var input = new MonsterSupergroup.GAS.OutputStatisticInput { value = 7, metric = "ComputedDamage" };
            var fixture = new ReplayFixture { domain = "output_stats", complete = true,
                checkpoint = CombatReplayAdapter.Token(state), steps = new[] {
                    new ReplayStep { record = "capture:2", operation = "ApplyDamage", arguments = new JArray(CombatReplayAdapter.Token(input)),
                        expected = CombatReplayAdapter.Token(MonsterSupergroup.GAS.OutputStatistics.ApplyDamage(state, input)) }
                } };
            string path = Path.Combine(directory, name); File.WriteAllText(path, EvidenceJson.Encode(fixture)); return path;
        }
    }
}
