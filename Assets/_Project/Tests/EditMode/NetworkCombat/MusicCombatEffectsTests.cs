using System;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class MusicCombatEffectsTests
    {
        private static ServerCombatGateway CreateWorld()
        {
            var gateway = new ServerCombatGateway();
            gateway.Ledger.RegisterEntity(10, 100, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 10);
            gateway.Ledger.RegisterSource(10, 10);
            foreach (uint id in new uint[] { 20, 21, 22, 23 })
                gateway.Ledger.RegisterEntity(id, 100, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            return gateway;
        }

        [Test]
        public void LightningUsesCanonicalDamageAndDeduplicatesTheWholeBeatAndEachTarget()
        {
            var gateway = CreateWorld();
            ulong root = gateway.NextServerEventId();
            var result = gateway.ProcessMusicDamage(10, 10, new uint[] { 20, 20, 21, 22 }, 20, root, 1, out var batch);
            Assert.That(result.Length, Is.EqualTo(3));
            Assert.That(batch.Entities.Length, Is.EqualTo(3));
            Assert.That(batch.EnemyHitPresentations.Length, Is.EqualTo(3));
            foreach (var state in batch.Entities) Assert.That(state.Health, Is.EqualTo(80));
            foreach (var hit in batch.EnemyHitPresentations)
            {
                Assert.That(hit.PresentationDamageType, Is.EqualTo((byte)DamageType.Lightning));
                Assert.That(hit.SourcePlayerId, Is.EqualTo(10));
                Assert.That(hit.DamageEventId, Is.Not.EqualTo(root));
            }
            Assert.That(gateway.ProcessMusicDamage(10, 10, new uint[] { 23 }, 20, root, 1.1, out _), Is.Empty,
                "A repeated beat cannot hit a newly selected random target.");
            Assert.That(gateway.Ledger.TryGetState(23, out var untouched));
            Assert.That(untouched.Health, Is.EqualTo(100));
        }

        [Test]
        public void FinaleProducesOneKillReceiptForDropsAndCannotKillAgain()
        {
            var gateway = CreateWorld();
            gateway.Ledger.RegisterEntity(20, 50, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            int notifications = 0;
            gateway.ConfirmedKillProduced += _ => notifications++;
            ulong root = gateway.NextServerEventId();
            var applied = gateway.ProcessMusicDamage(10, 10, new uint[] { 20, 21, 22, 23 }, 60, root, 2, out var batch);
            Assert.That(applied.Length, Is.EqualTo(4));
            Assert.That(batch.ConfirmedKills.Length, Is.EqualTo(1));
            Assert.That(batch.ConfirmedKills[0].KillerPlayerId, Is.EqualTo(10));
            Assert.That(notifications, Is.EqualTo(1));
            gateway.ProcessMusicDamage(10, 10, new uint[] { 20 }, 60, gateway.NextServerEventId(), 3, out var next);
            Assert.That(next.ConfirmedKills, Is.Empty);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(gateway.Metrics.ConfirmedKills, Is.EqualTo(1));
        }

        [Test]
        public void ImmuneDeadMissingAndNonEnemyTargetsReceiveNoDamage()
        {
            var gateway = CreateWorld();
            gateway.Ledger.SetAbsoluteInvulnerable(20, true);
            gateway.Ledger.ApplyServerStatusDamage(21, 100, gateway.NextServerEventId(), 10);
            var result = gateway.ProcessMusicDamage(10, 10, new uint[] { 20, 21, 999, 10, 22 }, 20,
                gateway.NextServerEventId(), 1, out _);
            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0].State.EntityId, Is.EqualTo(22));
            Assert.That(gateway.Ledger.TryGetState(10, out var player));
            Assert.That(player.Health, Is.EqualTo(100));
        }

        [Test]
        public void InvalidSourceDeadOwnerAndStoppedCombatCannotAdmitEffects()
        {
            var gateway = CreateWorld();
            Assert.That(gateway.TryAdmitMusicEffect(10, 999, gateway.NextServerEventId(), 1), Is.False);
            Assert.That(gateway.TryAdmitMusicEffect(10, 10, CombatEventId.Compose(12, 1, 1).Value, 1), Is.False);
            gateway.Ledger.SetPlayerUpgradeSelectionState(10, true);
            Assert.That(gateway.TryAdmitMusicEffect(10, 10, gateway.NextServerEventId(), 1), Is.False);
            gateway.Ledger.SetPlayerUpgradeSelectionState(10, false);
            gateway.Ledger.RegisterEntity(10, 100, CombatEntityKind.Player, CombatEntityAuthority.ServerCanonical);
            gateway.Ledger.ApplyServerStatusDamage(10, 100, gateway.NextServerEventId(), 0);
            Assert.That(gateway.TryAdmitMusicEffect(10, 10, gateway.NextServerEventId(), 1), Is.False);
            gateway = CreateWorld();
            gateway.StopCombat();
            Assert.That(gateway.TryAdmitMusicEffect(10, 10, gateway.NextServerEventId(), 1), Is.False);
        }

        [Test]
        public void EmptyEffectStillConsumesItsAdmissionWithoutRerolling()
        {
            var gateway = CreateWorld();
            ulong root = gateway.NextServerEventId();
            Assert.That(gateway.ProcessMusicDamage(10, 10, Array.Empty<uint>(), 20, root, 1, out _), Is.Empty);
            Assert.That(gateway.ProcessMusicDamage(10, 10, new uint[] { 20 }, 20, root, 2, out _), Is.Empty);
        }

        [Test]
        public void MusicKnockbackRequiresServerRootAndPreservesConfiguredBaseDistance()
        {
            var command = new EnemyKnockbackCommand
            {
                Kind = EnemyKnockbackKind.Music, AbilityCombatId = ServerCombatGateway.MusicCombatId,
                RootEventId = CombatEventId.Compose(ushort.MaxValue, 1, 1).Value,
                EnemyEntityId = 20, AssignmentEpoch = 1, SourcePlayerId = 10, CommandId = 1,
                IssuedAt = 1, MultiplierSum = 0,
                Settings = new EnemyKnockbackSettings
                {
                    Distance = 2, SpeedMultiplier = 4, Direction = Vector2.zero,
                    PreWrapMode = (int)WrapMode.ClampForever, PostWrapMode = (int)WrapMode.ClampForever,
                    CurveKeys = new[] { EnemyKnockbackCurveKey.From(new Keyframe(0, 0)),
                        EnemyKnockbackCurveKey.From(new Keyframe(1, 1)) }
                }
            };
            Assert.That(command.IsValid);
            Assert.That(command.Settings.Distance * (1 + command.MultiplierSum), Is.EqualTo(2));
            command.RootEventId = CombatEventId.Compose(12, 1, 1).Value;
            Assert.That(command.IsValid, Is.False);
            command.RootEventId = CombatEventId.Compose(ushort.MaxValue, 1, 2).Value;
            command.MultiplierSum = 1;
            Assert.That(command.IsValid, Is.False);
        }
    }
}
