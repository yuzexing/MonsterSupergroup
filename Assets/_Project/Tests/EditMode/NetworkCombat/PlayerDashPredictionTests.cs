using System;
using System.Linq;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class PlayerDashPredictionTests
    {
        [Test]
        public void PredictionRequiresABaselineAndRejectsMalformedDuplicateAndOlderUseIds()
        {
            var prediction = new PlayerDashPrediction();
            Assert.That(prediction.TryPredict(Id(1), 0d, 0.2f, 2f), Is.False);
            Assert.That(prediction.ApplyBaseline(1, 0, Snapshot(3), 0d), Is.True);
            Assert.That(prediction.TryPredict(0, 0d, 0.2f, 2f), Is.False);
            Assert.That(prediction.TryPredict((3UL << 48) | (7UL << 32), 0d, 0.2f, 2f), Is.False);
            Assert.That(prediction.TryPredict(Id(4), 0d, 0.2f, 2f), Is.True);
            Assert.That(prediction.TryPredict(Id(4), 0.4d, 0.2f, 2f), Is.False);
            Assert.That(prediction.TryPredict(Id(3), 0.4d, 0.2f, 2f), Is.False);
            Assert.That(prediction.PendingCount, Is.EqualTo(1));
            Assert.That(prediction.Runtime.AvailableCharges, Is.EqualTo(2));
            prediction.ApplyBaseline(2, Id(4), Snapshot(3, 0.35d, 2d), 0.4d);
            Assert.That(prediction.TryPredict(Id(4), 3d, 0.2f, 2f), Is.False, "Acknowledgement does not erase replay protection.");
        }

        [Test]
        public void AcknowledgingAAfterPredictingBDoesNotRefundB()
        {
            PlayerDashPrediction prediction = Create(2);
            prediction.TryPredict(Id(1), 0d, 0.2f, 3f);
            prediction.TryPredict(Id(2), 0.4d, 0.2f, 3f);
            Assert.That(prediction.ApplyBaseline(2, Id(1), Snapshot(2, 0.35d, 3d), 0.6d), Is.True);
            Assert.That(prediction.PendingCount, Is.EqualTo(1));
            Assert.That(prediction.Runtime.AvailableCharges, Is.Zero);
            Assert.That(prediction.Runtime.Capture(0.6d).RechargeReadyAt, Is.EqualTo(new[] { 3d, 3.4d }));
            Assert.That(prediction.Runtime.NextUseAt, Is.EqualTo(0.75d).Within(0.000001d));
        }

        [Test]
        public void RejectingAReplaysBOverTheUnspentServerBaseline()
        {
            PlayerDashPrediction prediction = Create(2);
            prediction.TryPredict(Id(1), 0d, 0.2f, 3f);
            prediction.TryPredict(Id(2), 0.4d, 0.2f, 3f);
            prediction.ApplyBaseline(2, Id(1), Snapshot(2), 0.6d);
            Assert.That(prediction.PendingCount, Is.EqualTo(1));
            Assert.That(prediction.Runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(prediction.Runtime.Capture(0.6d).RechargeReadyAt, Is.EqualTo(new[] { 3.4d }));
            Assert.That(prediction.Runtime.NextUseAt, Is.EqualTo(0.75d).Within(0.000001d));
        }

        [Test]
        public void CapacityDowngradeRetainsPendingBWhenCanonicalAAlreadyOccupiesEverySlot()
        {
            PlayerDashPrediction prediction = Create(2);
            prediction.TryPredict(Id(1), 0d, 0.2f, 3f);
            prediction.TryPredict(Id(2), 0.5d, 0.2f, 5f);
            prediction.ApplyBaseline(2, Id(1), Snapshot(1, 0.35d, 3d), 0.6d);
            Assert.That(prediction.PendingCount, Is.EqualTo(1));
            Assert.That(prediction.Runtime.MaxCharges, Is.EqualTo(1));
            Assert.That(prediction.Runtime.Capture(0.6d).RechargeReadyAt, Is.EqualTo(new[] { 3d, 5.5d }));
            prediction.Runtime.Refresh(3.1d);
            Assert.That(prediction.Runtime.AvailableCharges, Is.Zero,
                "B remains reserved after A recharges even though replay exceeded the downgraded capacity.");
            prediction.ApplyBaseline(3, Id(1), Snapshot(3, 0.35d, 3d), 3.1d);
            Assert.That(prediction.Runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(prediction.Runtime.Capture(3.1d).RechargeReadyAt, Is.EqualTo(new[] { 5.5d }));
        }

        [Test]
        public void NewerAcknowledgementConsumesItsPrefixAndOlderOrDuplicateBaselinesCannotRewindIt()
        {
            PlayerDashPrediction prediction = Create(2);
            prediction.TryPredict(Id(1), 0d, 0.2f, 3f);
            prediction.TryPredict(Id(2), 0.4d, 0.2f, 3f);
            prediction.ApplyBaseline(3, Id(2), Snapshot(2, 0.75d, 3d, 3.4d), 0.6d);
            Assert.That(prediction.PendingCount, Is.Zero);
            Assert.That(prediction.ApplyBaseline(2, Id(1), Snapshot(2), 0.7d), Is.False);
            Assert.That(prediction.ApplyBaseline(3, Id(2), Snapshot(2), 0.7d), Is.False);
            Assert.That(prediction.Runtime.AvailableCharges, Is.Zero);
            Assert.That(prediction.Runtime.NextUseAt, Is.EqualTo(0.75d));
        }

        [Test]
        public void SeveralPendingUsesRemainReservedAcrossPartialAcknowledgements()
        {
            PlayerDashPrediction prediction = Create(4);
            for (uint sequence = 1; sequence <= 4; sequence++)
                Assert.That(prediction.TryPredict(Id(sequence), (sequence - 1) * 0.4d, 0.2f, 10f), Is.True);
            prediction.ApplyBaseline(2, Id(1), Snapshot(4, 0.35d, 10d), 1.3d);
            Assert.That(prediction.PendingCount, Is.EqualTo(3));
            Assert.That(prediction.Runtime.AvailableCharges, Is.Zero);
            prediction.ApplyBaseline(3, Id(3), Snapshot(4, 1.15d, 10d, 10.4d, 10.8d), 1.4d);
            Assert.That(prediction.PendingCount, Is.EqualTo(1));
            Assert.That(prediction.Runtime.AvailableCharges, Is.Zero);
            double[] remaining = prediction.Runtime.Capture(1.4d).RechargeReadyAt;
            Assert.That(remaining.Length, Is.EqualTo(4));
            Assert.That(remaining[3], Is.EqualTo(11.2d).Within(0.000001d));
        }

        [Test]
        public void ReceiptJitterMovesPendingUseForwardWithoutShorteningItsRecharge()
        {
            PlayerDashPrediction prediction = Create(2);
            prediction.TryPredict(Id(1), 0d, 0.2f, 3f);
            prediction.TryPredict(Id(2), 0.4d, 0.2f, 3f);
            prediction.ApplyBaseline(2, Id(1), Snapshot(2, 0.45d, 3.1d), 0.6d);
            double[] deadlines = prediction.Runtime.Capture(0.6d).RechargeReadyAt;
            Assert.That(deadlines, Is.EqualTo(new[] { 3.1d, 3.45d }));
            Assert.That(prediction.Runtime.NextUseAt, Is.EqualTo(0.8d).Within(0.000001d));
        }

        [Test]
        public void ExpiredPendingRechargeCompletesOfflineWithoutLosingItsUnacknowledgedIdentity()
        {
            PlayerDashPrediction prediction = Create(2);
            prediction.TryPredict(Id(1), 0d, 0.2f, 1f);
            prediction.TryPredict(Id(2), 0.4d, 0.2f, 1f);
            prediction.ApplyBaseline(2, Id(1), Snapshot(2, 0.35d, 1d), 5d);
            Assert.That(prediction.PendingCount, Is.EqualTo(1));
            Assert.That(prediction.Runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(prediction.Runtime.Capture(5d).RechargeReadyAt, Is.Empty);
            prediction.ApplyBaseline(3, Id(2), Snapshot(2, 0.75d, 1d, 1.4d), 6d);
            Assert.That(prediction.PendingCount, Is.Zero);
            Assert.That(prediction.Runtime.AvailableCharges, Is.EqualTo(2));
        }

        [Test]
        public void InvalidNewBaselineLeavesPendingAndRevisionUsableForTheNextValidDelivery()
        {
            PlayerDashPrediction prediction = Create(2);
            prediction.TryPredict(Id(1), 0d, 0.2f, 5f);
            PlayerDashSnapshot before = prediction.Runtime.Capture(0d);
            Assert.Throws<ArgumentOutOfRangeException>(() => prediction.ApplyBaseline(2, Id(1), Snapshot(-1), 0d));
            Assert.Throws<ArgumentOutOfRangeException>(() => prediction.ApplyBaseline(2, Id(1), Snapshot(2, 0d, double.NaN), 0d));
            Assert.That(prediction.PendingCount, Is.EqualTo(1));
            Assert.That(prediction.Runtime.Capture(0d).RechargeReadyAt, Is.EqualTo(before.RechargeReadyAt));
            Assert.That(prediction.ApplyBaseline(2, Id(1), Snapshot(2, 0.35d, 5d), 0d), Is.True);
            Assert.That(prediction.PendingCount, Is.Zero);
        }

        [Test]
        public void BaselineArraysRemainCallerOwnedAndDoNotAliasThePredictionRuntime()
        {
            PlayerDashPrediction prediction = Create(2);
            prediction.TryPredict(Id(1), 0d, 0.2f, 3f);
            prediction.TryPredict(Id(2), 0.4d, 0.2f, 3f);
            PlayerDashSnapshot transport = Snapshot(2, 0.35d, 3d);
            prediction.ApplyBaseline(2, Id(1), transport, 0.6d);
            Assert.That(transport.RechargeReadyAt.Length, Is.EqualTo(1));
            transport.RechargeReadyAt[0] = 0d;
            Assert.That(prediction.Runtime.Capture(0.6d).RechargeReadyAt, Is.EqualTo(new[] { 3d, 3.4d }));
        }

        [Test]
        public void RevisionWrapAndZeroRevisionFollowMonotonicNetworkOrdering()
        {
            var prediction = new PlayerDashPrediction();
            Assert.That(prediction.ApplyBaseline(0, 0, Snapshot(2), 0d), Is.False);
            Assert.That(prediction.ApplyBaseline(uint.MaxValue, 0, Snapshot(2), 0d), Is.True);
            Assert.That(prediction.ApplyBaseline(1, 0, Snapshot(3), 0d), Is.True);
            Assert.That(prediction.ApplyBaseline(uint.MaxValue, 0, Snapshot(9), 0d), Is.False);
            Assert.That(prediction.Runtime.MaxCharges, Is.EqualTo(3));
        }

        [Test]
        public void HostOwnerPredictionAndServerConsumptionRemainSeparateInstances()
        {
            PlayerDashPrediction owner = Create(2);
            var server = new ServerDashUseRegistry();
            server.Runtime.ConfigureCapacity(2);
            Assert.That(owner.TryPredict(Id(1), 0d, 0.2f, 3f), Is.True);
            Assert.That(server.Runtime.AvailableCharges, Is.EqualTo(2));
            Assert.That(server.TryConsume(Id(1), 1, 0d, 0.2f, 3f, new uint[4]), Is.True);
            Assert.That(owner.Runtime, Is.Not.SameAs(server.Runtime));
            owner.ApplyBaseline(2, Id(1), server.Runtime.Capture(0d), 0d);
            Assert.That(owner.Runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(server.Runtime.AvailableCharges, Is.EqualTo(1));
            Assert.That(owner.PendingCount, Is.Zero);
        }

        [Test]
        public void PendingInputLimitRejectsBeforeSpendingAnotherCharge()
        {
            PlayerDashPrediction prediction = Create(100);
            for (uint sequence = 1; sequence <= 64; sequence++)
                Assert.That(prediction.TryPredict(Id(sequence), sequence * 0.2d, 0f, 1000f), Is.True);
            Assert.That(prediction.PendingCount, Is.EqualTo(64));
            Assert.That(prediction.Runtime.AvailableCharges, Is.EqualTo(36));
            Assert.That(prediction.TryPredict(Id(65), 13d, 0f, 1000f), Is.False);
            Assert.That(prediction.Runtime.AvailableCharges, Is.EqualTo(36));
            double[] acknowledgedDeadlines = Enumerable.Range(1, 32).Select(sequence => sequence * 0.2d + 1000d).ToArray();
            prediction.ApplyBaseline(2, Id(32), Snapshot(100, 6.55d, acknowledgedDeadlines), 13d);
            Assert.That(prediction.PendingCount, Is.EqualTo(32));
            Assert.That(prediction.Runtime.AvailableCharges, Is.EqualTo(36));
            Assert.That(prediction.TryPredict(Id(65), 13d, 0f, 1000f), Is.True);
        }

        private static PlayerDashPrediction Create(int capacity)
        {
            var prediction = new PlayerDashPrediction();
            prediction.ApplyBaseline(1, 0, Snapshot(capacity), 0d);
            return prediction;
        }

        private static PlayerDashSnapshot Snapshot(int capacity, double nextUseAt = 0d, params double[] deadlines) =>
            new PlayerDashSnapshot { MaxCharges = capacity, NextUseAt = nextUseAt, RechargeReadyAt = deadlines };
        private static ulong Id(uint sequence) => CombatEventId.Compose(3, 7, sequence).Value;
    }
}
