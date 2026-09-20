using MonsterSupergroup.GAS;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class PickupReceiptTests
    {
        [Test]
        public void ReceiptAndHealthCommitInOneGatewayTurn_DuplicateCannotCommitTwice()
        {
            var gateway = new ServerCombatGateway();
            gateway.RegisterClientIdentity(10, 1, 1);
            gateway.Ledger.RegisterEntity(10, 500, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 10);
            var ids = new SequentialCombatEventIdSource(1, 1);
            var evt = ids.Next();
            var report = new PlayerHealthReport { EventId = evt.Value, Sequence = evt.Sequence, PlayerId = 10,
                EntityId = 10, Health = 200, MaxHealth = 500, Alive = true, StateVersion = 2 };
            gateway.ProcessBatch(10, new CombatSubmissionBatch { BatchSequence = 1, PlayerHealthReports = new[] { report } }, 1);
            int commits = 0;
            gateway.ValidatePickupReceipt = (_, r) => r.PickupDropId == 12 && r.PickupClaimVersion == 3 && r.PickupRound == 1;
            gateway.PlayerHealthReportAccepted += r =>
            {
                gateway.Ledger.TryGetState(10, out var state);
                Assert.That(state.Health, Is.EqualTo(400), "Ledger health must be visible in the same receipt callback.");
                commits++;
            };
            evt = ids.Next(); report.EventId = evt.Value; report.Sequence = evt.Sequence; report.StateVersion = 3;
            report.Health = 400; report.PickupDropId = 12; report.PickupClaimVersion = 3; report.PickupRestoredHealth = 200;
            gateway.ProcessBatch(10, new CombatSubmissionBatch { BatchSequence = 2, PlayerHealthReports = new[] { report } }, 2);
            gateway.Ledger.TryGetState(10, out var rejected);
            Assert.That(rejected.Health, Is.EqualTo(200)); Assert.That(commits, Is.Zero);
            report.PickupRound = 1;
            gateway.ProcessBatch(10, new CombatSubmissionBatch { BatchSequence = 3, PlayerHealthReports = new[] { report } }, 3);
            gateway.ProcessBatch(10, new CombatSubmissionBatch { BatchSequence = 4, PlayerHealthReports = new[] { report } }, 4);
            Assert.That(commits, Is.EqualTo(1));
        }
    }
}
