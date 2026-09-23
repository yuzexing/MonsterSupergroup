using System;
using System.Linq;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class StatusBoundaryCacheTests
    {
        [Test] public void UnchangedBoundaryCaptureReusesFrozenValuesWithoutAllocating()
        {
            var controller = new StatusController(_ => { });
            var expected = controller.CaptureReplayBoundary();
            controller.CaptureReplayBoundary();
            bool same = true;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) same &= ReferenceEquals(expected, controller.CaptureReplayBoundary());
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(same, Is.True); Assert.That(allocated, Is.Zero);
        }

        [Test] public void AllocatingStatusIdCreatesNewBoundaryWithoutChangingQueuedPreviousValue()
        {
            var ids = new SequentialStatusInstanceIdSource(2, 3, 7);
            var controller = new StatusController(_ => { }, ids, new StatusExecutionScope(true, false, 2));
            var first = controller.CaptureReplayBoundary();
            Assert.That(ids.Next().Value, Is.EqualTo(CombatEventId.Compose(2, 3, 7).Value));
            var next = controller.CaptureReplayBoundary();
            Assert.That(next, Is.Not.SameAs(first)); Assert.That(first.ids.next, Is.EqualTo(7)); Assert.That(next.ids.next, Is.EqualTo(8));
            Assert.That(next.ids.slot, Is.EqualTo(2)); Assert.That(next.ids.epoch, Is.EqualTo(3));
        }

        [Test] public void SharedCombatIdAllocationInvalidatesCacheEvenWithoutStatusMutation()
        {
            var ids = new SequentialCombatEventIdSource(4, 5, 20);
            var controller = new StatusController(_ => { }, new CombatEventStatusInstanceIdSource(ids), new StatusExecutionScope(false, true, 4));
            var first = controller.CaptureReplayBoundary();
            // A different actor can allocate from the shared source between this controller's Advances.
            ids.Next(); ids.Next();
            var next = controller.CaptureReplayBoundary();
            Assert.That(next, Is.Not.SameAs(first)); Assert.That(first.ids.next, Is.EqualTo(20)); Assert.That(next.ids.next, Is.EqualTo(22));
            var restored = new StatusController(_ => { }); restored.RestoreReplayBoundary(next);
            Assert.That(EvidenceJson.Encode(restored.CaptureReplayBoundary()), Is.EqualTo(EvidenceJson.Encode(next)));
            Assert.That(ids.Next().Sequence, Is.EqualTo(22), "Capture and restore never advance the live source.");
        }

        [Test] public void EpochOwnerAndPolicyChangesAreCapturedAndUnsupportedPolicyStaysUnsupported()
        {
            var controller = new StatusController(_ => { }, new SequentialStatusInstanceIdSource(1, 1, 5), new StatusExecutionScope(false, false, 1, 1));
            var owner = controller.CaptureReplayBoundary();
            controller.SetExecutionPolicy(new StatusExecutionScope(false, false, 1, 2));
            var handoff = controller.CaptureReplayBoundary();
            Assert.That(handoff.targetOwner, Is.EqualTo(2)); Assert.That(owner.targetOwner, Is.EqualTo(1));
            controller.SetInstanceIdSource(new SequentialStatusInstanceIdSource(1, 2, 5));
            var reconnect = controller.CaptureReplayBoundary();
            Assert.That(reconnect.ids.epoch, Is.EqualTo(2)); Assert.That(handoff.ids.epoch, Is.EqualTo(1));
            controller.SetExecutionPolicy(new StatusExecutionScope(false, false, 0, 0));
            Assert.That(controller.CaptureReplayBoundary().supported, Is.True);
            controller.SetExecutionPolicy(new UnsupportedPolicy());
            var unsupported = controller.CaptureReplayBoundary(); Assert.That(unsupported.supported, Is.False);
            Assert.Throws<InvalidOperationException>(() => controller.RestoreReplayBoundary(unsupported));
            controller.SetExecutionPolicy(new StatusExecutionScope(false, false, 0, 0));
            controller.SetInstanceIdSource(new UnsupportedIdSource());
            var unknownIds = controller.CaptureReplayBoundary();
            Assert.That(unknownIds.supported, Is.False); Assert.That(unknownIds.ids, Is.Null);
            controller.SetInstanceIdSource(new SequentialStatusInstanceIdSource(1, 2, 5));
            var knownIds = controller.CaptureReplayBoundary();
            Assert.That(knownIds.supported, Is.True); Assert.That(knownIds.ids.next, Is.EqualTo(5));
            controller.SetInstanceIdSource(new CombatEventStatusInstanceIdSource(new SequentialCombatEventIdSource(1, 2, 5)));
            var combatIds = controller.CaptureReplayBoundary();
            Assert.That(combatIds.eventIds, Is.True); Assert.That(knownIds.eventIds, Is.False);
            Assert.That(combatIds, Is.Not.SameAs(knownIds));
        }

        [Test] public void CachedBoundaryAndNestedSequenceAreReadOnlyButJsonAndBinaryRecoveryRemainWritable()
        {
            var boundary = new StatusController(_ => { }).CaptureReplayBoundary();
            Assert.Throws<InvalidOperationException>(() => boundary.ids.next = 999);
            Assert.Throws<InvalidOperationException>(() => boundary.localPlayer = 999);
            Assert.Throws<InvalidOperationException>(() => boundary.ids = new EventSequenceState());
            var decoded = EvidenceJson.Decode<StatusReplayBoundary>(EvidenceJson.Encode(boundary));
            decoded.ids.next = 123; decoded.localPlayer = 7;
            Assert.That(boundary.ids.next, Is.EqualTo(1)); Assert.That(boundary.localPlayer, Is.Zero);
            var controller = new StatusController(_ => { }); controller.RestoreReplayBoundary(decoded);
            Assert.That(controller.CaptureReplayBoundary().ids.next, Is.EqualTo(123));
            // Existing codec and initializer construction remain compatible: only captured instances are frozen.
            var writable = new StatusReplayBoundary { supported = true, ids = new EventSequenceState { slot = 3, epoch = 4, next = 5 } };
            writable.ids.next = 6; Assert.That(writable.ids.next, Is.EqualTo(6));
            var entry = new DiagnosticAdvance { sequence = 1, role = "status", engine = "status-1", operation = "Advance", phase = 0,
                delta = 1f / 144, utcTicks = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc).Ticks, boundary = boundary };
            string block = EvidenceBlocks.EncodeAdvances("capture", "run", 1, new[] { entry }, 1);
            var fromBinary = (StatusReplayBoundary)EvidenceBlocks.Decode(block).Single().before;
            Assert.That(EvidenceJson.Encode(fromBinary), Is.EqualTo(EvidenceJson.Encode(boundary)));
            fromBinary.ids.next = 456;
            Assert.That(boundary.ids.next, Is.EqualTo(1));
        }

        private sealed class UnsupportedPolicy : IStatusExecutionPolicy { public bool CanExecute(StatusInstance instance) => true; }
        private sealed class UnsupportedIdSource : IStatusInstanceIdSource { public StatusInstanceId Next() => default; }
    }
}
