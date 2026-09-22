using System;
using System.Collections.Generic;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class ClientFinalEnemyTests
    {
        private static ulong Id(uint sequence, ushort player = 1) => CombatEventId.Compose(player, 1, sequence).Value;
        private static ServerCombatGateway Gateway(int health = 100)
        {
            var gateway = new ServerCombatGateway();
            for (ushort player = 1; player <= 2; player++)
            {
                gateway.RegisterClientIdentity(player, player, 1);
                gateway.Attacks.RegisterPlayer(player);
                gateway.Ledger.RegisterSource(player, player);
                gateway.Ledger.RegisterEntity(player, 100, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, player);
            }
            gateway.Ledger.RegisterEntity(100, health, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            return gateway;
        }
        private static CombatResult Damage(int damage = 10, uint sequence = 3, ushort player = 1) => new CombatResult
        {
            EventId = Id(sequence, player), Sequence = sequence, RootEventId = Id(1, player), ParentEventId = Id(2, player), ChainDepth = 2,
            SourcePlayerId = player, SourceEntityId = player, TargetEntityId = 100, AbilityId = 77, Damage = damage
        };
        private static EnemyDeathReport Death(ushort player = 1) => new EnemyDeathReport
        {
            EventId = Id(4, player), CauseEventId = Id(3, player), Sequence = 4,
            SourcePlayerId = player, SourceEntityId = player, TargetEntityId = 100
        };

        [TestCase(false)] [TestCase(true)]
        public void DelayedOwnerOutcomeIsFinalEvenAfterSelectionAndMissingAttackMetadata(bool periodic)
        {
            var gateway = Gateway();
            gateway.Ledger.SetPlayerUpgradeSelectionState(1, true);
            gateway.Ledger.SetAbsoluteInvulnerable(100, true);
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, 77, Id(1), 0, 3, 1,
                default, out _), Is.False, "The obsolete admission deadline must not veto damage.");
            var hit = Damage();
            if (periodic) hit.DamageTags = (ulong)(CombatTags.Status | CombatTags.Periodic);
            var batch = gateway.ProcessBatch(1, new CombatSubmissionBatch
            { BatchSequence = 1, Results = new[] { hit }, EnemyDeathReports = new[] { Death() } }, 3, out var receipts);
            Assert.That(batch.Entities[0].Health, Is.Zero);
            Assert.That(batch.ConfirmedKills, Has.Length.EqualTo(1));
            Assert.That(receipts, Has.Length.EqualTo(1));
            Assert.That(receipts[0].Kill.CauseEventId, Is.EqualTo(hit.EventId));
            Assert.That(gateway.Metrics.AcceptedCombatResults, Is.EqualTo(1));
        }

        [Test]
        public void HighDamageIsAcceptedAndConcurrentRepeatedDeathsConfirmOneKillAfterDespawn()
        {
            var gateway = Gateway();
            int consequences = 0;
            gateway.ConfirmedKillProduced += _ => consequences++;
            gateway.ProcessBatch(1, new CombatSubmissionBatch
            { BatchSequence = 1, Results = new[] { Damage(int.MaxValue) } }, 0);
            gateway.UnregisterEntity(100);
            for (ushort player = 1; player <= 2; player++)
            {
                var result = gateway.ProcessBatch(player, new CombatSubmissionBatch
                { BatchSequence = 2, EnemyDeathReports = new[] { Death(player), Death(player) } }, 300, out var receipts);
                Assert.That(receipts, Has.Length.EqualTo(2));
                Assert.That(receipts[0].Kill.KillerPlayerId, Is.EqualTo(1));
                Assert.That(result.ConfirmedKills, Is.Empty);
            }
            Assert.That(consequences, Is.EqualTo(1));
            Assert.That(gateway.Metrics.ConfirmedKills, Is.EqualTo(1));
        }

        [Test]
        public void DeathBroadcastAndReceiptRaiseOneClientConfirmationEvenAfterTargetDespawn()
        {
            var replica = new CanonicalWorldReplica();
            var kill = new ConfirmedKill { TargetEntityId = 100, TargetStateVersion = 2, KillerPlayerId = 1, CauseEventId = Id(3) };
            int confirmations = 0;
            replica.KillConfirmed += _ => confirmations++;
            replica.Apply(new CanonicalWorldBatch { ConfirmedKills = new[] { kill } });
            replica.Apply(new CanonicalWorldBatch { ConfirmedKills = new[] { kill, kill } });
            Assert.That(confirmations, Is.EqualTo(1));
            replica.Clear();
            replica.KillConfirmed += _ => confirmations++;
            replica.Apply(new CanonicalWorldBatch { ConfirmedKills = new[] { kill } });
            Assert.That(confirmations, Is.EqualTo(2), "A new match may reuse object identities.");
        }

        [Test]
        public void DeathForRetiredLivingEnemyIsAcknowledgedWithoutAwardingAKill()
        {
            var gateway = Gateway();
            gateway.UnregisterEntity(100);
            gateway.ProcessBatch(1, new CombatSubmissionBatch
            { BatchSequence = 1, EnemyDeathReports = new[] { Death() } }, 0, out var receipts);
            Assert.That(receipts, Has.Length.EqualTo(1));
            Assert.That(receipts[0].Kill.TargetEntityId, Is.Zero);
            Assert.That(gateway.Metrics.ConfirmedKills, Is.Zero);
        }

        [TestCase("round")] [TestCase("source")] [TestCase("epoch")] [TestCase("player target")]
        public void DeathCannotCrossIdentityRoundOrPlayerHealthBoundary(string defect)
        {
            var gateway = Gateway(); gateway.Round = 2;
            var death = Death();
            uint round = 2;
            if (defect == "round") round = 1;
            if (defect == "source") death.SourceEntityId = 2;
            if (defect == "epoch") death.EventId = CombatEventId.Compose(1, 2, 4).Value;
            if (defect == "player target") death.TargetEntityId = 2;
            gateway.ProcessBatch(1, new CombatSubmissionBatch
            { Round = round, BatchSequence = 1, EnemyDeathReports = new[] { death } }, 0, out var receipts);
            Assert.That(receipts, Is.Empty);
            Assert.That(gateway.Ledger.IsAlive(100), Is.True);
            Assert.That(gateway.Ledger.IsAlive(2), Is.True);
        }

        private static CombatContext Context(uint sequence, uint target, CombatTags tags, uint parent) => new CombatContext(
            new CombatEventId(Id(sequence)), new CombatEventId(Id(1)), new CombatEventId(Id(parent)),
            sequence, 2, 1, 1, target, 77, 0, tags, 1);

        [Test]
        public void LargeAreaAttackDrainsEveryDeathAfterItsDamageAndRetriesUntilReceipt()
        {
            var gateway = Gateway();
            using var collector = new ClientCombatCollector(1, new SequentialCombatEventIdSource(1, 1));
            for (uint i = 0; i < 600; i++)
            {
                uint target = 100 + i, sequence = 3 + i * 2;
                gateway.Ledger.RegisterEntity(target, 100, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
                collector.Publish(new CombatEvent(CombatEventKind.DamageResolved,
                    Context(sequence, target, CombatTags.Damage, 2), new DamageInfo(77, 10, false)));
                collector.Publish(new CombatEvent(CombatEventKind.PredictedLethalHit,
                    Context(sequence + 1, target, CombatTags.PredictedLethalHit, sequence), new DamageInfo(77, 10, false)));
            }
            var delivered = new HashSet<ulong>();
            uint batchSequence = 0;
            while (collector.PendingResultCount > 0 || collector.HasDueDeaths(0))
            {
                var outgoing = collector.Drain(++batchSequence);
                foreach (var damage in outgoing.Results) delivered.Add(damage.EventId);
                foreach (var death in outgoing.EnemyDeathReports) Assert.That(delivered.Contains(death.CauseEventId), Is.True);
                gateway.ProcessBatch(1, outgoing, 0, out _); // Intentionally lose receipts.
            }
            Assert.That(collector.PendingEnemyDeathCount, Is.EqualTo(600));
            Assert.That(collector.HasDueDeaths(.99), Is.False);
            Assert.That(gateway.Metrics.ConfirmedKills, Is.EqualTo(600));
            for (int second = 1; second <= 5; second++)
            {
                int retries = 0;
                while (collector.HasDueDeaths(second))
                {
                    gateway.ProcessBatch(1, collector.Drain(++batchSequence, now: second), second, out var receipts);
                    retries += receipts.Length;
                    foreach (var receipt in receipts) Assert.That(collector.AcknowledgeDeath(receipt, second), Is.True);
                }
                Assert.That(retries, Is.LessThanOrEqualTo(ClientCombatCollector.MaximumDeathRetriesPerSecond));
            }
            Assert.That(collector.PendingEnemyDeathCount, Is.Zero);
            Assert.That(gateway.Metrics.ConfirmedKills, Is.EqualTo(600));
        }

        [TestCase(false)] [TestCase(true)]
        public void PositiveCanonicalEchoCannotUndoClientFinalEnemyDeathButPlayerCorrectionStillWorks(bool enemy)
        {
            var obj = new GameObject("Client final health test");
            try
            {
                var combatant = obj.AddComponent<CombatantBehaviour>();
                combatant.Initialize(100); combatant.ConfigureEntityId(100);
                combatant.ConfigureKillConfirmation(true); combatant.ConfigureClientFinalDeath(enemy);
                combatant.ReceiveDamage(new DamageInfo(77, 100, false));
                combatant.ReceivePredictedLethalHit(new PredictedLethalHit(
                    Context(4, 100, CombatTags.PredictedLethalHit, 3), new DamageInfo(77, 100, false), new DamageInfo(77, 100, false)));
                combatant.ApplyCanonicalHealth(90, 100, 2);
                Assert.That(combatant.CurrentHealth, Is.EqualTo(enemy ? 0 : 90));
                Assert.That(combatant.StateVersion, Is.EqualTo(2));
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }
    }
}
