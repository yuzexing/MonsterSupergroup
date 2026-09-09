using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class UltimateProtectionTests
    {
        [Test]
        public void UltimateExpiryDoesNotRemoveUpgradeOrIndependentProtection()
        {
            var ledger = new CombatLedger();
            ledger.RegisterEntity(1, 100, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 1);
            ledger.SetPlayerUpgradeSelectionState(1, true);
            ledger.SetPlayerUltimateInvulnerable(1, true);
            ledger.SetPlayerUltimateInvulnerable(1, false);
            Assert.That(ledger.TryGetState(1, out var choosing), Is.True);
            Assert.That(choosing.AbsoluteInvulnerable, Is.True);
            ledger.SetAbsoluteInvulnerable(1, true);
            ledger.SetPlayerUpgradeSelectionState(1, false);
            ledger.SetPlayerUltimateInvulnerable(1, true);
            ledger.SetPlayerUltimateInvulnerable(1, false);
            ledger.TryGetState(1, out var independent);
            Assert.That(independent.AbsoluteInvulnerable, Is.True);
            ledger.SetAbsoluteInvulnerable(1, false);
            ledger.TryGetState(1, out var expired);
            Assert.That(expired.AbsoluteInvulnerable, Is.False);
        }

        [Test]
        public void HealthCheckpointDoesNotTurnTemporaryUltimateProtectionIntoPermanentProtection()
        {
            var ledger = new CombatLedger();
            ledger.RegisterEntity(1, 100, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 1);
            ledger.SetPlayerUltimateInvulnerable(1, true);
            var checkpoint = ledger.CaptureEntityState(1);
            Assert.That(checkpoint.State.AbsoluteInvulnerable, Is.True);
            Assert.That(checkpoint.AbsoluteInvulnerable, Is.False);
            ledger.RegisterEntity(2, 100, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 2);
            var restored = ledger.RestoreEntityState(2, checkpoint);
            Assert.That(restored.AbsoluteInvulnerable, Is.False);
            // The ability checkpoint can independently reapply only its still-live absolute deadline.
            ledger.SetPlayerUltimateInvulnerable(2, true);
            ledger.TryGetState(2, out restored);
            Assert.That(restored.AbsoluteInvulnerable, Is.True);
            ledger.SetPlayerUltimateInvulnerable(2, false);
            ledger.TryGetState(2, out restored);
            Assert.That(restored.AbsoluteInvulnerable, Is.False);
        }
    }
}
