using System;
using System.Collections.Generic;
using System.Threading;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class SharedEvidencePayloadTests
    {
        [Test] public void SubmissionSnapshotCannotBeChangedByLaterGameplayOrInspectionMutation()
        {
            var memory = new DiagnosticMemoryBudget();
            var batch = new CombatSubmissionBatch { Round = 1, BatchSequence = 7, Results = new[] { new CombatResult { Damage = 10, EventId = 5 } } };
            Assert.That(SharedEvidencePayload.TryCapture(batch, memory, out var shared), Is.True);
            using (shared)
            {
                batch.Round = 2; batch.Results[0].Damage = 99;
                var first = (CombatSubmissionBatch)shared.CopyValueForTests();
                Assert.That(first.Round, Is.EqualTo(1)); Assert.That(first.Results[0].Damage, Is.EqualTo(10));
                first.Results[0].Damage = 100;
                Assert.That(((CombatSubmissionBatch)shared.CopyValueForTests()).Results[0].Damage, Is.EqualTo(10));
            }
            Assert.That(memory.Used, Is.Zero);
        }

        [Test] public void CanonicalHitPositionChangesRequireANewSemanticVersion()
        {
            var memory = new DiagnosticMemoryBudget();
            var batch = new CanonicalWorldBatch { ServerSequence = 8, EnemyHitPresentations = new[] { new EnemyHitPresentation { Damage = 10 } } };
            Assert.That(SharedEvidencePayload.TryCapture(batch, memory, out var before), Is.True);
            using (before)
            {
                batch.EnemyHitPresentations[0].HasPosition = true; batch.EnemyHitPresentations[0].Position = UnityEngine.Vector3.one;
                Assert.That(SharedEvidencePayload.TryCapture(batch, memory, out var after), Is.True);
                using (after)
                {
                    Assert.That(((CanonicalWorldBatch)before.CopyValueForTests()).EnemyHitPresentations[0].HasPosition, Is.False);
                    Assert.That(((CanonicalWorldBatch)after.CopyValueForTests()).EnemyHitPresentations[0].Position, Is.EqualTo(UnityEngine.Vector3.one));
                }
            }
            Assert.That(memory.Used, Is.Zero);
        }

        [Test] public void MultipleQueuedReferencesChargeTheSnapshotOnceAndReleaseOnlyAfterTheLastLease()
        {
            var memory = new DiagnosticMemoryBudget();
            Assert.That(SharedEvidencePayload.TryCapture(new CombatSubmissionBatch { Results = new CombatResult[2] }, memory, out var shared), Is.True);
            long held = memory.Used;
            var first = shared.AcquireLease(); var second = shared.AcquireLease();
            Assert.That(DiagnosticPayload.Freeze(shared), Is.SameAs(shared)); Assert.That(memory.Used, Is.EqualTo(held));
            shared.Dispose(); shared.Dispose(); first.Dispose(); first.Dispose();
            Assert.That(memory.Used, Is.EqualTo(held)); Assert.That(shared.CopyValueForTests(), Is.TypeOf<CombatSubmissionBatch>());
            second.Dispose(); second.Dispose(); Assert.That(memory.Used, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => shared.AcquireLease());
        }

        [Test] public void BudgetRejectionAndPerSnapshotBoundDoNotRetainMemory()
        {
            var tiny = new DiagnosticMemoryBudget(512);
            Assert.That(SharedEvidencePayload.TryCapture(new CombatSubmissionBatch { Results = new CombatResult[1] }, tiny, out var denied), Is.False);
            Assert.That(denied, Is.Null); Assert.That(tiny.Used, Is.Zero);
            var memory = new DiagnosticMemoryBudget();
            Assert.That(SharedEvidencePayload.TryCapture(new CanonicalWorldBatch { Entities = new CanonicalEntityState[4096] }, memory, out denied), Is.False);
            Assert.That(denied, Is.Null); Assert.That(memory.Used, Is.Zero);
        }

        [Test] public void AHandleCannotBeSerializedBeforeItsDurableDependencyReferenceExists()
        {
            var memory = new DiagnosticMemoryBudget();
            Assert.That(SharedEvidencePayload.TryCapture(new CanonicalWorldBatch(), memory, out var shared), Is.True);
            using (shared) Assert.Throws<JsonSerializationException>(() => EvidenceJson.Encode(new object[] { shared }));
            Assert.That(memory.Used, Is.Zero);
        }

        [Test] public void QueueTraversalKeepsNestedReferencesAliveAndRollsBackFailedCollection()
        {
            var memory = new DiagnosticMemoryBudget();
            Assert.That(SharedEvidencePayload.TryCapture(new CanonicalWorldBatch(), memory, out var shared), Is.True);
            var leases = new List<IDisposable>();
            try
            {
                var graph = new ReplayOutResult { result = new CanonicalReceiveEvidence { incomingRound = 1, batch = shared },
                    outValues = new object[] { new object[] { shared } } };
                SharedEvidencePayload.CollectLeases(graph, leases);
                Assert.That(leases.Count, Is.EqualTo(2));
                var cycle = new object[2]; cycle[0] = shared; cycle[1] = cycle;
                Assert.Throws<InvalidOperationException>(() => SharedEvidencePayload.CollectLeases(cycle, leases));
                Assert.That(leases.Count, Is.EqualTo(2), "Failed traversal releases only its own newly acquired leases.");
                shared.Dispose(); Assert.That(memory.Used, Is.EqualTo(shared.RetainedBytes));
            }
            finally { shared.Dispose(); foreach (var lease in leases) lease.Dispose(); }
            Assert.That(memory.Used, Is.Zero);
        }

        [Test] public void ReferenceResolutionRunsBeforeEmissionAndRestoresNestedAndThreadLocalScopes()
        {
            var memory = new DiagnosticMemoryBudget();
            Assert.That(SharedEvidencePayload.TryCapture(new CanonicalWorldBatch(), memory, out var shared), Is.True);
            using (shared)
            {
                int persisted = 0; bool otherThreadRejected = false;
                using (SharedEvidencePayloadReferenceConverter.BeginResolution(value => { persisted++; return "blobs/outer.json.gz"; }))
                {
                    string encoded = EvidenceJson.Encode(new object[] { shared });
                    Assert.That(persisted, Is.EqualTo(1)); Assert.That(encoded, Does.Contain("\"$evidenceRef\":\"blobs/outer.json.gz\""));
                    using (SharedEvidencePayloadReferenceConverter.BeginResolution(value => "blobs/inner.json.gz"))
                        Assert.That(EvidenceJson.Encode(shared), Does.Contain("blobs/inner.json.gz"));
                    Assert.That(EvidenceJson.Encode(shared), Does.Contain("blobs/outer.json.gz"));
                    var thread = new Thread(() =>
                    {
                        try { EvidenceJson.Encode(shared); }
                        catch (JsonSerializationException) { otherThreadRejected = true; }
                    });
                    thread.Start(); Assert.That(thread.Join(2000), Is.True); Assert.That(otherThreadRejected, Is.True);
                }
                Assert.Throws<JsonSerializationException>(() => EvidenceJson.Encode(shared));
                using (SharedEvidencePayloadReferenceConverter.BeginResolution(value => null))
                    Assert.Throws<JsonSerializationException>(() => EvidenceJson.Encode(shared));
                using (SharedEvidencePayloadReferenceConverter.BeginResolution(value => throw new InvalidOperationException("DiskFailed")))
                    Assert.Throws<InvalidOperationException>(() => EvidenceJson.Encode(shared));
            }
            Assert.That(memory.Used, Is.Zero);
        }

        [Test] public void CachedReferenceBelongsToOneSourceAndNeverRetainsEncodedPayload()
        {
            var memory = new DiagnosticMemoryBudget();
            Assert.That(SharedEvidencePayload.TryCapture(new CombatSubmissionBatch(), memory, out var shared), Is.True);
            using (shared)
            {
                Assert.That(shared.TryGetReference("capture-a", out _), Is.False);
                shared.SetReference("capture-a", "blobs/a.json.gz");
                Assert.That(shared.TryGetReference("capture-a", out var first), Is.True); Assert.That(first, Is.EqualTo("blobs/a.json.gz"));
                Assert.That(shared.TryGetReference("capture-b", out _), Is.False);
                shared.SetReference("capture-b", "blobs/b.json.gz");
                Assert.That(shared.TryGetReference("capture-a", out _), Is.False);
                Assert.That(shared.TryGetReference("capture-b", out var second), Is.True); Assert.That(second, Is.EqualTo("blobs/b.json.gz"));
                Assert.That(memory.Used, Is.EqualTo(shared.RetainedBytes));
            }
            Assert.That(memory.Used, Is.Zero);
        }
    }
}
