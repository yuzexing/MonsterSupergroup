using MonsterSupergroup.GAS;
using NUnit.Framework;
using AstralShift.HellMaiden.Player;
using Mirror;
using UnityEngine;
using System.Reflection;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class UpgradeSelectionCombatGateTests
    {
        [Test]
        public void SelectingPlayerTransformDiscardsIncomingAndBufferedOwnerMovement()
        {
            var obj = new GameObject("Locked network transform");
            obj.SetActive(false);
            try
            {
                var player = obj.AddComponent<PlayerMovement>();
                player.SetUpgradeSelectionLocked(true);
                obj.AddComponent<NetworkIdentity>();
                var transformSync = obj.AddComponent<NetworkPlayerTransformReliable>();
                typeof(NetworkPlayerTransformReliable).GetMethod("Awake",
                    BindingFlags.NonPublic | BindingFlags.Instance).Invoke(transformSync, null);
                typeof(NetworkPlayerTransformReliable).GetMethod("OnClientToServerSync",
                    BindingFlags.NonPublic | BindingFlags.Instance).Invoke(transformSync,
                    new object[] { (Vector3?)Vector3.one * 100, (Quaternion?)Quaternion.identity, null });
                Assert.That(transformSync.serverSnapshots, Is.Empty);
                transformSync.serverSnapshots.Add(1, new TransformSnapshot(
                    1, 1, Vector3.one * 50, Quaternion.identity, Vector3.one));
                typeof(NetworkPlayerTransformReliable).GetMethod("UpdateServer",
                    BindingFlags.NonPublic | BindingFlags.Instance).Invoke(transformSync, null);
                Assert.That(transformSync.serverSnapshots, Is.Empty);
                Assert.That(obj.transform.position, Is.EqualTo(Vector3.zero));
            }
            finally { Object.DestroyImmediate(obj); }
        }

        [Test]
        public void SelectingPlayerCannotSubmitDamageOrStatus_OtherPlayerContinues()
        {
            var gateway = CreateGateway();
            gateway.Ledger.SetPlayerUpgradeSelectionState(1, true);
            gateway.ProcessBatch(1, new CombatSubmissionBatch
            {
                BatchSequence = 1,
                Results = new[] { Hit(1, 1) },
                StatusMutations = new[] { Poison(2) }
            }, 0);
            Assert.That(gateway.Ledger.TryGetState(100, out var enemy), Is.True);
            Assert.That(enemy.Health, Is.EqualTo(100));
            Assert.That(gateway.Statuses.GetAllStates(), Is.Empty);
            Assert.That(gateway.Metrics.GetRejected(
                CombatRejectionReason.SourceSelectingUpgrade), Is.EqualTo(2));

            gateway.ProcessBatch(2, new CombatSubmissionBatch
            {
                BatchSequence = 1, Results = new[] { Hit(3, 2) }
            }, 0);
            gateway.Ledger.TryGetState(100, out enemy);
            Assert.That(enemy.Health, Is.EqualTo(90));

            gateway.Ledger.SetPlayerUpgradeSelectionState(1, false);
            gateway.ProcessBatch(1, new CombatSubmissionBatch
            {
                BatchSequence = 2,
                Results = new[] { Hit(1, 1), Hit(4, 1) },
                StatusMutations = new[] { Poison(2) }
            }, 1);
            gateway.Ledger.TryGetState(100, out enemy);
            Assert.That(enemy.Health, Is.EqualTo(80), "Rejected events cannot be replayed after unlock.");
            Assert.That(gateway.Statuses.GetAllStates(), Is.Empty);
        }

        [Test]
        public void OwnerDamageReportDuringSelectionIsCorrectedAndNormalDamageResumes()
        {
            var gateway = CreateGateway();
            gateway.Ledger.SetPlayerUpgradeSelectionState(1, true);
            var report = new PlayerHealthReport
            {
                EventId = 10, Sequence = 10, PlayerId = 1, EntityId = 1,
                Health = 20, MaxHealth = 100, Alive = true, StateVersion = 10
            };
            CanonicalWorldBatch correction = gateway.ProcessBatch(1,
                new CombatSubmissionBatch { BatchSequence = 1, PlayerHealthReports = new[] { report } }, 0);
            Assert.That(correction.Entities, Has.Length.EqualTo(1));
            Assert.That(correction.Entities[0].Health, Is.EqualTo(100));
            Assert.That(correction.Entities[0].StateVersion, Is.GreaterThan(report.StateVersion));
            Assert.That(correction.Entities[0].AbsoluteInvulnerable, Is.True);
            Assert.That(gateway.Metrics.AcceptedPlayerReports, Is.Zero);

            gateway.Ledger.SetPlayerUpgradeSelectionState(1, false);
            report.EventId = report.Sequence = 11;
            report.StateVersion = 20;
            CanonicalWorldBatch accepted = gateway.ProcessBatch(1,
                new CombatSubmissionBatch { BatchSequence = 2, PlayerHealthReports = new[] { report } }, 1);
            Assert.That(accepted.Entities[0].Health, Is.EqualTo(20));
            Assert.That(accepted.Entities[0].AbsoluteInvulnerable, Is.False);
        }

        [Test]
        public void SelectionStateIsIndependentIdempotentAndPreservesOtherImmunity()
        {
            var gateway = CreateGateway();
            var ledger = gateway.Ledger;
            Assert.That(ledger.SetPlayerUpgradeSelectionState(999, true), Is.False);
            ledger.SetAbsoluteInvulnerable(1, true);
            ledger.SetPlayerUpgradeSelectionState(1, true);
            ledger.SetPlayerUpgradeSelectionState(2, true);
            ledger.TryGetState(1, out var before);
            ledger.SetPlayerUpgradeSelectionState(1, true);
            ledger.TryGetState(1, out var same);
            Assert.That(same.StateVersion, Is.EqualTo(before.StateVersion));
            ledger.SetPlayerUpgradeSelectionState(1, false);
            ledger.TryGetState(1, out var after);
            Assert.That(after.AbsoluteInvulnerable, Is.True);
            Assert.That(ledger.IsPlayerSelectingUpgrade(1), Is.False);
            Assert.That(ledger.IsPlayerSelectingUpgrade(2), Is.True);
            ledger.UnregisterEntity(2);
            Assert.That(ledger.IsPlayerSelectingUpgrade(2), Is.False);
        }

        private static ServerCombatGateway CreateGateway()
        {
            var gateway = new ServerCombatGateway();
            for (uint id = 1; id <= 2; id++)
            {
                gateway.Ledger.RegisterSource(id, id);
                gateway.Ledger.RegisterEntity(id, 100, CombatEntityKind.Player,
                    CombatEntityAuthority.OwnerFinal, id);
            }
            gateway.Ledger.RegisterEntity(100, 100, CombatEntityKind.Enemy,
                CombatEntityAuthority.ServerCanonical);
            return gateway;
        }

        private static CombatResult Hit(ulong eventId, uint playerId) => new CombatResult
        {
            EventId = eventId, Sequence = (uint)eventId,
            SourcePlayerId = playerId, SourceEntityId = playerId,
            TargetEntityId = 100, Damage = 10
        };

        private static StatusMutation Poison(ulong eventId) => new StatusMutation
        {
            EventId = eventId, Sequence = (uint)eventId, InstanceId = 500,
            Kind = StatusMutationKind.ApplyOrRefresh,
            SourcePlayerId = 1, SourceEntityId = 1, TargetEntityId = 100,
            DefinitionId = (uint)EnemyStatusID.Poison,
            StackMode = (byte)StatusStackMode.Add, MaxStacks = 20, StackDelta = 1,
            Duration = 3, TickInterval = 1, TotalTicks = 3, TickDamage = 2,
            ExecutionAuthority = (byte)StatusExecutionAuthority.SourceClient
        };
    }
}
