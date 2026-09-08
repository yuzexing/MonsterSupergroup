using System.Reflection;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using DamageInfo = MonsterSupergroup.GAS.DamageInfo;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class UpgradeSelectionLockTests
    {
        [Test]
        public void PlayerLockStopsInputPhysicsAndAttacks_UnlockPreservesOtherInvulnerability()
        {
            var owner = new GameObject("Selecting player");
            owner.SetActive(false);
            try
            {
                var combatant = owner.AddComponent<CombatantBehaviour>();
                combatant.Initialize(100);
                var player = owner.AddComponent<PlayerMovement>();
                player.Awake();
                player.CombatantBinding.Configure(player, combatant);
                player.body = owner.AddComponent<Rigidbody2D>();
                player.body.constraints = RigidbodyConstraints2D.FreezeRotation;
                var weapon = owner.AddComponent<ProjectileAttackBehaviour>();
                weapon.ConfigureOwner(player);
                player.SetInvulnerable(true);
                player.SetDirection(Vector2.right);
                player.body.linearVelocity = Vector2.right * 5;

                float worldTimeScale = Time.timeScale;
                player.SetUpgradeSelectionLocked(true);
                player.SetUpgradeSelectionLocked(true);
                player.SetDirection(Vector2.one);
                player.SetDirectionImmediate(Vector2.one);
                player.Dash();
                player.UltimateAction();
                Assert.DoesNotThrow(() => weapon.Attack(), "Locked attacks cannot reach uninitialized GAS.");
                Assert.That(player.CurrentInputDirection, Is.EqualTo(Vector2.zero));
                Assert.That(player.body.linearVelocity, Is.EqualTo(Vector2.zero));
                Assert.That(player.body.constraints & RigidbodyConstraints2D.FreezePosition,
                    Is.EqualTo(RigidbodyConstraints2D.FreezePosition));
                Assert.That(weapon.CanAttack, Is.False);
                Assert.That(combatant.ReceiveDamage(new DamageInfo(1, 40, false)).Value, Is.Zero);
                Assert.That(Time.timeScale, Is.EqualTo(worldTimeScale));

                player.SetUpgradeSelectionLocked(false);
                player.SetUpgradeSelectionLocked(false);
                Assert.That(weapon.CanAttack, Is.True);
                Assert.That(player.body.constraints, Is.EqualTo(RigidbodyConstraints2D.FreezeRotation));
                Assert.That(player.IsInvulnerable, Is.True, "The pre-existing immunity remains owned by its original source.");
                player.SetInvulnerable(false);
                Assert.That(player.IsInvulnerable, Is.False);
                player.SetDirection(Vector2.up);
                Assert.That(player.CurrentInputDirection, Is.EqualTo(Vector2.up));
                Assert.That(combatant.ReceiveDamage(new DamageInfo(1, 40, false)).Value, Is.EqualTo(40));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void SelectionImmunityBlocksStatusDamageWhileOtherCombatantsContinue()
        {
            var a = new GameObject("Selecting combatant");
            var b = new GameObject("Other combatant");
            try
            {
                var first = a.AddComponent<CombatantBehaviour>();
                var second = b.AddComponent<CombatantBehaviour>();
                first.Initialize(100);
                second.Initialize(100);
                first.SetUpgradeSelectionInvulnerable(true);
                var burn = new StatusApplication(OnHitBurnModifier.BurnDefinition, 2, 3, 0.1f, 6f, 7);
                first.ApplyStatus(burn);
                second.ApplyStatus(burn);
                first.AdvanceStatuses(0.3f);
                second.AdvanceStatuses(0.3f);
                Assert.That(first.CurrentHealth, Is.EqualTo(100));
                Assert.That(first.StatusTickCount, Is.EqualTo(3), "Status time continues during selection.");
                Assert.That(second.CurrentHealth, Is.EqualTo(94));
                first.SetCanonicalInvulnerable(true);
                first.SetUpgradeSelectionInvulnerable(false);
                Assert.That(first.ReceiveDamage(new DamageInfo(1, 5, false)).Value, Is.Zero);
                first.SetCanonicalInvulnerable(false);
                Assert.That(first.ReceiveDamage(new DamageInfo(1, 5, false)).Value, Is.EqualTo(5));
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
    }
}
