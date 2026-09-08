using System;
using MonsterSupergroup.GAS;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class PlayerCombatRestorationTests
    {
        [TestCase(37, true)]
        [TestCase(0, false)]
        public void HealthCheckpoint_RestoresFactsIntoNewIdentityWithoutTouchingOtherPlayer(
            int health, bool alive)
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 10);
            RegisterPlayer(ledger, 20);
            Assert.That(ledger.ApplyOwnerFinalReport(10, Report(10, health, 140, 9)).Accepted,
                Is.True);
            ServerEntityCheckpoint checkpoint = ledger.CaptureEntityState(10);
            ledger.UnregisterEntity(10);
            RegisterPlayer(ledger, 11);

            CanonicalEntityState restored = ledger.RestoreEntityState(11, checkpoint);

            Assert.That(restored.EntityId, Is.EqualTo(11));
            Assert.That(restored.OwnerPlayerId, Is.EqualTo(11));
            Assert.That(restored.Authority, Is.EqualTo((byte)CombatEntityAuthority.OwnerFinal));
            Assert.That(restored.Health, Is.EqualTo(health));
            Assert.That(restored.MaxHealth, Is.EqualTo(140));
            Assert.That(restored.Alive, Is.EqualTo(alive));
            Assert.That(restored.StateVersion, Is.GreaterThan(checkpoint.State.StateVersion));
            ledger.TryGetState(20, out CanonicalEntityState other);
            Assert.That(other.Health, Is.EqualTo(100));
            Assert.That(other.StateVersion, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HealthCheckpoint_SeparatesAbsoluteProtectionFromSelectionGate(bool absolute)
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 10);
            ledger.SetAbsoluteInvulnerable(10, absolute);
            ledger.SetPlayerUpgradeSelectionState(10, true);
            ServerEntityCheckpoint checkpoint = ledger.CaptureEntityState(10);
            Assert.That(checkpoint.State.AbsoluteInvulnerable, Is.True);
            Assert.That(checkpoint.AbsoluteInvulnerable, Is.EqualTo(absolute));
            RegisterPlayer(ledger, 11);
            ledger.SetPlayerUpgradeSelectionState(11, true);

            CanonicalEntityState restored = ledger.RestoreEntityState(11, checkpoint);

            Assert.That(restored.AbsoluteInvulnerable, Is.EqualTo(absolute));
            Assert.That(ledger.IsPlayerSelectingUpgrade(11), Is.False,
                "The selection runtime must restore its pending-choice gate separately.");
        }

        [Test]
        public void HealthRestore_RaisesBothBaselinesAndRejectsOldOwnerReports()
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 10);
            ledger.ApplyOwnerFinalReport(10, Report(10, 37, 100, 9));
            ServerEntityCheckpoint checkpoint = ledger.CaptureEntityState(10);
            RegisterPlayer(ledger, 11);
            ledger.ApplyOwnerFinalReport(11, Report(11, 75, 100, 30));

            CanonicalEntityState restored = ledger.RestoreEntityState(11, checkpoint);
            Assert.That(restored.StateVersion, Is.EqualTo(31));
            PlayerHealthReport oldOwner = Report(10, 100, 100, 99);
            oldOwner.EntityId = 11;
            Assert.That(ledger.ApplyOwnerFinalReport(10, oldOwner).Rejection,
                Is.EqualTo(CombatRejectionReason.WrongAuthority));
            Assert.That(ledger.ApplyOwnerFinalReport(11, Report(11, 100, 100, 30)).Rejection,
                Is.EqualTo(CombatRejectionReason.StaleOwnerReport));
            Assert.That(ledger.ApplyOwnerFinalReport(11, Report(11, 36, 100, 32)).Accepted,
                Is.True, "The existing Owner-final protocol resumes above the restored baseline.");
        }

        [Test]
        public void InvalidHealthCheckpoint_IsRejectedBeforeMutation()
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 10);
            CanonicalEntityState invalid = ledger.CaptureEntityState(10).State;
            invalid.Health = 101;
            Assert.Throws<ArgumentException>(() => ledger.RestoreEntityState(
                10, new ServerEntityCheckpoint(invalid, false)));
            ledger.TryGetState(10, out CanonicalEntityState unchanged);
            Assert.That(unchanged.Health, Is.EqualTo(100));
            Assert.That(unchanged.StateVersion, Is.EqualTo(1));
            Assert.That(ledger.TryCaptureEntityState(99, out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => ledger.CaptureEntityState(99));
        }

        [Test]
        public void StatusCapture_OnlyIncludesUnexpiredTargetInstancesAndDoesNotAdvanceRuntime()
        {
            var registry = new ServerStatusRegistry(new CombatLedger());
            registry.AddServerStatus(Status(1, 10, startTime: 10, totalTicks: 5));
            registry.AddServerStatus(Status(2, 10, startTime: 10, totalTicks: 2));
            registry.AddServerStatus(Status(3, 20, startTime: 10, totalTicks: 5));

            CanonicalStatusState[] checkpoint = registry.CaptureTarget(10, 12);

            Assert.That(checkpoint, Has.Length.EqualTo(1));
            Assert.That(checkpoint[0].InstanceId, Is.EqualTo(1));
            Assert.That(checkpoint[0].CompletedTicks, Is.EqualTo(2));
            Assert.That(checkpoint[0].StartTime, Is.EqualTo(10));
            Assert.That(checkpoint[0].Duration, Is.EqualTo(5));
            Assert.That(registry.Count, Is.EqualTo(3));
            Assert.That(registry.TryGet(new StatusInstanceId(1), out StatusInstance live), Is.True);
            Assert.That(live.CompletedTicks, Is.Zero,
                "Capturing must not steal unprocessed ticks from an avatar that is still active.");
            checkpoint[0].Stack = 1;
            Assert.That(live.Stack, Is.EqualTo(2), "The checkpoint is detached data.");
        }

        [Test]
        public void StatusRestore_SkipsAbsentTicksAndRetainsOriginalDeadlineAndDamageSchedule()
        {
            var gateway = new ServerCombatGateway();
            RegisterEnemy(gateway.Ledger, 10);
            gateway.Statuses.AddServerStatus(Status(1, 10, startTime: 10, totalTicks: 6));
            gateway.Advance(12);
            CanonicalStatusState[] checkpoint = gateway.Statuses.CaptureTarget(10, 12.25);
            var removals = gateway.Statuses.RemoveTarget(10);
            gateway.Ledger.UnregisterEntity(10);
            RegisterEnemy(gateway.Ledger, 11);

            var restored = gateway.Statuses.RestoreTarget(10, 11, checkpoint, 14.6);

            Assert.That(restored, Has.Count.EqualTo(1));
            Assert.That(restored[0].InstanceId, Is.EqualTo(checkpoint[0].InstanceId));
            Assert.That(restored[0].Version, Is.GreaterThan(removals[0].Version));
            Assert.That(restored[0].CompletedTicks, Is.EqualTo(4));
            Assert.That(restored[0].StartTime, Is.EqualTo(10));
            Assert.That(restored[0].Duration, Is.EqualTo(6));
            Assert.That(restored[0].Stack, Is.EqualTo(checkpoint[0].Stack));
            Assert.That(restored[0].Magnitude, Is.EqualTo(checkpoint[0].Magnitude));
            Assert.That(gateway.Advance(14.9).Entities, Is.Empty);
            Assert.That(gateway.Advance(15).Entities[0].Health, Is.EqualTo(93));
            CanonicalWorldBatch finalTick = gateway.Advance(16);
            Assert.That(finalTick.Entities[0].Health, Is.EqualTo(86));
            Assert.That(finalTick.Statuses[0].Removed, Is.True);
            Assert.That(gateway.Advance(17).Entities, Is.Empty);
            Assert.That(gateway.Statuses.Count, Is.Zero);
        }

        [Test]
        public void StatusRestore_DoesNotResurrectStatusesExpiredWhileAbsent()
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 11);
            var registry = new ServerStatusRegistry(ledger);
            registry.AddServerStatus(Status(1, 10, startTime: 10, totalTicks: 3));
            CanonicalStatusState[] checkpoint = registry.CaptureTarget(10, 11.5);
            registry.RemoveTarget(10);

            Assert.That(registry.RestoreTarget(10, 11, checkpoint, 13), Is.Empty);
            Assert.That(registry.Count, Is.Zero);
            Assert.That(registry.Advance(20).Ticks, Is.Empty);
        }

        [Test]
        public void StatusRestore_SelfSourceUsesNewAvatarAndKeepsDisconnectTakeover()
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 10);
            ledger.RegisterSource(10, 10);
            var registry = new ServerStatusRegistry(ledger);
            AddSourceStatus(registry, 1, source: 10, target: 10);
            CanonicalStatusState[] checkpoint = registry.CaptureTarget(10, 1.25);
            registry.HandleSourceDisconnected(10, 1.25);
            registry.RemoveTarget(10);
            ledger.UnregisterSource(10);
            ledger.UnregisterEntity(10);
            RegisterPlayer(ledger, 11);
            ledger.RegisterSource(11, 11);

            var restored = registry.RestoreTarget(10, 11, checkpoint, 2.5);
            StatusInstance instance = restored[0].ToStatusInstance();

            Assert.That(instance.ExecutionAuthority, Is.EqualTo(StatusExecutionAuthority.Server));
            Assert.That(instance.SourcePlayerId, Is.EqualTo(11));
            Assert.That(instance.SourceEntityId, Is.EqualTo(11));
            Assert.That(instance.TargetEntityId, Is.EqualTo(11));
            Assert.That(instance.DamageSourceId, Is.EqualTo(11));
            Assert.That(instance.SourceContext.SourcePlayerId, Is.EqualTo(11));
            Assert.That(instance.SourceContext.SourceEntityId, Is.EqualTo(11));
            Assert.That(instance.SourceContext.TargetEntityId, Is.EqualTo(11));
            Assert.That(instance.SourceContext.EventId.Value, Is.EqualTo(checkpoint[0].SourceEventId),
                "The original cause remains historical provenance, not a new connection event.");
            Assert.That(instance.CompletedTicks, Is.EqualTo(2));
            Assert.That(new StatusExecutionScope(false, false, 11).CanExecute(instance), Is.False);
            Assert.That(registry.Advance(2.9).Ticks, Is.Empty);
            Assert.That(registry.Advance(3).Ticks, Has.Count.EqualTo(1));
        }

        [TestCase(true, StatusExecutionAuthority.SourceClient)]
        [TestCase(false, StatusExecutionAuthority.Server)]
        public void StatusRestore_ExternalSourceKeepsAuthorityOnlyWhileStillRegistered(
            bool sourceConnected, StatusExecutionAuthority expected)
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 10);
            RegisterPlayer(ledger, 20);
            ledger.RegisterSource(20, 20);
            var registry = new ServerStatusRegistry(ledger);
            AddSourceStatus(registry, 1, source: 20, target: 10);
            CanonicalStatusState[] checkpoint = registry.CaptureTarget(10, 1.25);
            registry.RemoveTarget(10);
            if (!sourceConnected)
                ledger.UnregisterSource(20);
            RegisterPlayer(ledger, 11);

            var restored = registry.RestoreTarget(10, 11, checkpoint, 2.5);

            Assert.That(restored[0].ExecutionAuthority, Is.EqualTo((byte)expected));
            Assert.That(restored[0].SourcePlayerId, Is.EqualTo(20));
            Assert.That(restored[0].SourceEntityId, Is.EqualTo(20));
            Assert.That(restored[0].TargetEntityId, Is.EqualTo(11));
        }

        [Test]
        public void StatusRestore_PreservesTargetOwnerExecutionPolicy()
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 11);
            var registry = new ServerStatusRegistry(ledger);
            CanonicalStatusState saved = CanonicalStatusState.From(Status(1, 10, 0, 5));
            saved.ExecutionAuthority = (byte)StatusExecutionAuthority.TargetOwnerClient;

            var restored = registry.RestoreTarget(10, 11, new[] { saved }, 2.5);
            StatusInstance instance = restored[0].ToStatusInstance();

            Assert.That(instance.ExecutionAuthority,
                Is.EqualTo(StatusExecutionAuthority.TargetOwnerClient));
            Assert.That(new StatusExecutionScope(false, false, 11, 11).CanExecute(instance), Is.True);
            Assert.That(new StatusExecutionScope(false, false, 20, 11).CanExecute(instance), Is.False);
            Assert.That(registry.Advance(3).Ticks, Is.Empty);
        }

        [Test]
        public void StatusRestore_RepeatedLiveBindingDoesNotRewindTickProgress()
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 11);
            var registry = new ServerStatusRegistry(ledger);
            var checkpoint = new[] { CanonicalStatusState.From(Status(1, 10, 0, 5)) };
            var first = registry.RestoreTarget(10, 11, checkpoint, 1.5);
            Assert.That(registry.Advance(2).Ticks, Has.Count.EqualTo(1));

            var repeated = registry.RestoreTarget(10, 11, checkpoint, 2.5);

            Assert.That(repeated[0].Version, Is.EqualTo(first[0].Version));
            Assert.That(repeated[0].CompletedTicks, Is.EqualTo(2));
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.Advance(2.5).Ticks, Is.Empty);
        }

        [Test]
        public void InvalidStatusCheckpoint_DoesNotPartiallyRestoreOrMoveAnotherPlayersStatus()
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 11);
            var registry = new ServerStatusRegistry(ledger);
            registry.AddServerStatus(Status(3, 20, 0, 5));
            CanonicalStatusState valid = CanonicalStatusState.From(Status(1, 10, 0, 5));
            CanonicalStatusState wrongPlayer = CanonicalStatusState.From(Status(2, 20, 0, 5));

            Assert.Throws<ArgumentException>(() => registry.RestoreTarget(
                10, 11, new[] { valid, wrongPlayer }, 1));
            Assert.That(registry.GetForTarget(11), Is.Empty);
            Assert.That(registry.GetForTarget(20), Has.Count.EqualTo(1));
            Assert.Throws<ArgumentException>(() => registry.RestoreTarget(
                10, 11, new[] { valid, valid }, 1));
            Assert.That(registry.Count, Is.EqualTo(1));
        }

        [Test]
        public void StatusRestore_RequiresOldAvatarDetachment()
        {
            var ledger = new CombatLedger();
            RegisterPlayer(ledger, 11);
            var registry = new ServerStatusRegistry(ledger);
            registry.AddServerStatus(Status(1, 10, 0, 5));
            CanonicalStatusState[] checkpoint = registry.CaptureTarget(10, 1);

            Assert.Throws<InvalidOperationException>(() =>
                registry.RestoreTarget(10, 11, checkpoint, 2));
            Assert.That(registry.GetForTarget(10), Has.Count.EqualTo(1));
            Assert.That(registry.GetForTarget(11), Is.Empty);
        }

        private static void RegisterPlayer(CombatLedger ledger, uint id)
        {
            ledger.RegisterEntity(id, 100, CombatEntityKind.Player,
                CombatEntityAuthority.OwnerFinal, id);
        }

        private static void RegisterEnemy(CombatLedger ledger, uint id)
        {
            ledger.RegisterEntity(id, 100, CombatEntityKind.Enemy,
                CombatEntityAuthority.ServerCanonical);
        }

        private static PlayerHealthReport Report(uint player, int health, int maxHealth, uint version)
        {
            return new PlayerHealthReport
            {
                EventId = version,
                Sequence = version,
                PlayerId = player,
                EntityId = player,
                Health = health,
                MaxHealth = maxHealth,
                Alive = health > 0,
                StateVersion = version
            };
        }

        private static StatusInstance Status(ulong id, uint target, double startTime, int totalTicks)
        {
            return new StatusInstance(new StatusInstanceId(id),
                new StatusDefinition(EnemyStatusID.Poison, StatusStackMode.Add, 3),
                20, 20, target, 2, startTime, totalTicks, StatusExecutionAuthority.Server,
                7, 7, totalTicks, 0, 1, 0.5f, 20, magnitude: 0.25f);
        }

        private static void AddSourceStatus(
            ServerStatusRegistry registry, ulong id, uint source, uint target)
        {
            StatusMutationResult applied = registry.Apply(source, new StatusMutation
            {
                EventId = id,
                RootEventId = id,
                Sequence = (uint)id,
                Kind = StatusMutationKind.ApplyOrRefresh,
                InstanceId = id,
                DefinitionId = (uint)EnemyStatusID.Poison,
                StackMode = (byte)StatusStackMode.Add,
                MaxStacks = 3,
                SourcePlayerId = source,
                SourceEntityId = source,
                TargetEntityId = target,
                StackDelta = 2,
                StartTime = 0,
                Duration = 5,
                ExecutionAuthority = (byte)StatusExecutionAuthority.SourceClient,
                TickDamage = 7,
                TotalTicks = 5,
                TickInterval = 1,
                DamageSourceId = source,
                Magnitude = 0.25f
            }, 0);
            Assert.That(applied.Accepted, Is.True);
        }
    }
}
