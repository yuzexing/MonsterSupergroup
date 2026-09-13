using System;
using System.Linq;
using AstralShift.HellMaiden.Data.Perks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlayerDebugSnapshotTests
    {
        [Test]
        public void DashReadingAtFutureTimesDoesNotCompleteRechargeOrExposeLiveDeadlines()
        {
            var runtime = new PlayerDashRuntime(2);
            Assert.That(runtime.TryConsume(10, 0.2, 5), Is.True);
            var before = runtime.ReadSnapshot();
            for (int i = 0; i < 10; i++)
            {
                var snapshot = runtime.ReadSnapshot();
                Assert.That(PlayerDebugSnapshotReader.DashText(snapshot, 20), Does.Contain("2/2"));
                Assert.That(runtime.AvailableCharges, Is.EqualTo(1), "UI must not complete the live recharge.");
                snapshot.RechargeReadyAt[0] = 0;
            }
            Assert.That(runtime.ReadSnapshot().RechargeReadyAt, Is.EqualTo(before.RechargeReadyAt));
            Assert.That(runtime.NextUseAt, Is.EqualTo(before.NextUseAt));
            Assert.That(runtime.Refresh(20), Is.True, "The gameplay update still owns the pending recharge.");
        }

        [Test]
        public void ServerReadsLedgerWhileClientShowsEffectiveAndConfirmedValuesWithoutMutation()
        {
            var worldObject = new GameObject("Debug reader world");
            var avatarObject = new GameObject("Debug reader player");
            try
            {
                var world = worldObject.AddComponent<NetworkCombatWorld>();
                var avatar = avatarObject.AddComponent<NetworkIdentity>();
                var combatant = avatarObject.AddComponent<CombatantBehaviour>();
                combatant.Initialize(150);
                combatant.ReceiveDamage(new DamageInfo(1, 40, false));
                var run = new RunSession();
                Assert.That(run.TryConnect("debug-player", 1, out var participant, out _), Is.True);
                run.AttachAvatar(1, 77, 1);
                world.Gateway.Ledger.RegisterEntity(77, 100, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 77);
                var status = Status(77);
                world.Gateway.Statuses.AddServerStatus(status.ToStatusInstance());
                world.Replica.Apply(new CanonicalWorldBatch { Entities = new[] {
                    new CanonicalEntityState { EntityId = 77, Kind = (byte)CombatEntityKind.Player, Health = 80,
                        MaxHealth = 120, Alive = true, StateVersion = 3 } }, Statuses = new[] { status } });
                combatant.StatusController.UpsertCanonical(status.ToStatusInstance());
                combatant.StatusController.ApplyPredictedStackDelta(new StatusInstanceId(status.InstanceId), -2);
                double beforeTime = combatant.StatusController.CurrentTime;
                var server = PlayerDebugSnapshotReader.ReadLive(avatar, participant, world, true, 0, 12);
                var client = PlayerDebugSnapshotReader.ReadLive(avatar, participant, world, false, 0, 12);
                Assert.That(server.Source, Is.EqualTo(PlayerDebugSource.ServerRecord));
                Assert.That(server.Summary, Does.Contain($"P{participant.Id} | ParticipantId: {participant.Id}").And.Contain("Avatar netId: 77"));
                Assert.That(client.Summary, Does.Contain($"P{participant.Id} | ParticipantId: {participant.Id}").And.Contain("Avatar netId: 77"));
                Assert.That(server.Canonical.Value.Health, Is.EqualTo(100));
                Assert.That(server.Details, Does.Contain("OwnerFinal"));
                Assert.That(client.Canonical.Value.Health, Is.EqualTo(80));
                Assert.That(client.LocalHealth, Is.EqualTo(110));
                Assert.That(client.Summary, Does.Contain("Burn x0 (confirmed 2)"));
                Assert.That(client.Details, Does.Contain("instance 99"));
                Assert.That(client.Details, Does.Contain("DOT 0/5"));
                Assert.That(client.Progression, Is.Null);
                Assert.That(combatant.CurrentHealth, Is.EqualTo(110));
                Assert.That(combatant.StatusController.GetStackCount(EnemyStatusID.Burn), Is.Zero);
                Assert.That(combatant.StatusController.CurrentTime, Is.EqualTo(beforeTime));
                Assert.That(world.Gateway.Statuses.GetForTarget(77).Single().CompletedTicks, Is.Zero);
                world.Replica.Clear();
                var missing = PlayerDebugSnapshotReader.ReadLive(avatar, participant, world, false, 0, 20);
                Assert.That(missing.Canonical, Is.Null);
                Assert.That(missing.Details, Does.Contain("Confirmed HP: unavailable"));
            }
            finally { Object.DestroyImmediate(avatarObject); Object.DestroyImmediate(worldObject); }
        }

        [Test]
        public void OfflineCheckpointUsesFrozenTimesStableParticipantAndUnknownDefinitionIds()
        {
            var run = new RunSession();
            run.TryConnect("returning-player", 3, out var participant, out _);
            run.AttachAvatar(3, 88, 2);
            var checkpoint = new PlayerRuntimeCheckpoint
            {
                PreviousAvatarId = 88, CapturedAt = 12, LifeState = RunPlayerLifeState.Downed,
                Health = new ServerEntityCheckpoint(new CanonicalEntityState { EntityId = 88, Health = 0,
                    MaxHealth = 150, Alive = false, StateVersion = 5 }, false),
                Progression = new PlayerProgressionSnapshot { Level = 3, Experience = 7, BuildRevision = 4 },
                Ultimate = new PlayerUltimateSnapshot { ActiveUntil = 15, InvulnerableUntil = 14 },
                Dash = new PlayerDashSnapshot { MaxCharges = 2, RechargeReadyAt = new[] { 16d } },
                Build = new PlayerBuildSnapshot { InitialWeaponId = 6, InitialWeaponSlot = 0,
                    Weapons = new[] { new PlayerBuildWeaponSnapshot { SlotIndex = 0, WeaponId = 6 } },
                    Equipment = new[] { new PlayerBuildEquipmentSnapshot { SlotIndex = 0, EquipmentId = 7, LevelIndex = 2 } },
                    Perks = new[] { new PlayerBuildPerkSnapshot { PerkId = 8, Rarity = (PerkRarity)0 } } }
            };
            run.Disconnect(3, checkpoint);
            var first = PlayerDebugSnapshotReader.ReadOffline(participant, null, 29);
            var second = PlayerDebugSnapshotReader.ReadOffline(participant, null, 29);
            Assert.That(first.Source, Is.EqualTo(PlayerDebugSource.OfflineCheckpoint));
            Assert.That(first.Summary, Does.Contain($"P{participant.Id} | ParticipantId: {participant.Id}").And.Contain("Previous Avatar netId: 88"));
            Assert.That(first.Summary, Does.Contain("Downed").And.Contain("offline checkpoint at 12s"));
            Assert.That(first.Details, Does.Contain("Active 3s").And.Contain("Invulnerable 2s"));
            Assert.That(first.Details, Does.Contain("unknown weapon #6").And.Contain("unknown equipment #7 Lv 3").And.Contain("unknown perk #8"));
            Assert.That(first.Details, Does.Contain("Slot 4: empty"));
            Assert.That(first.Details, Is.EqualTo(second.Details));
            run.TryConnect("returning-player", 4, out var restored, out _);
            run.AttachAvatar(4, 99, 3);
            Assert.That(restored.Id, Is.EqualTo(first.ParticipantId));
            Assert.That(checkpoint.Dash.Value.RechargeReadyAt, Is.EqualTo(new[] { 16d }));
        }

        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        [TestCase(320, 240)]
        public void PanelFitsBottomLeftAndNeverExceedsSixtyPercentHeight(int width, int height)
        {
            var area = NetworkPlayerDebugPanel.CalculateArea(width, height, true);
            Assert.That(area.xMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(area.yMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(area.xMax, Is.LessThanOrEqualTo(width));
            Assert.That(area.yMax, Is.LessThanOrEqualTo(height));
            Assert.That(area.width, Is.LessThanOrEqualTo(520));
            Assert.That(area.height, Is.LessThanOrEqualTo(height * .6f));
        }

        [Test]
        public void UltimateActiveHasPriorityOverAnAlreadyGrantedNextCharge()
        {
            var state = new PlayerUltimateSnapshot { HasCharge = true, ActiveUntil = 15, InvulnerableUntil = 13 };
            Assert.That(PlayerDebugSnapshotReader.UltimatePhase(state, 14), Is.EqualTo("Active"));
            Assert.That(PlayerDebugSnapshotReader.UltimatePhase(state, 15), Is.EqualTo("Ready"));
            state.HasCharge = false;
            Assert.That(PlayerDebugSnapshotReader.UltimatePhase(state, 15), Is.EqualTo("Empty"));
        }

        private static CanonicalStatusState Status(uint target) => new CanonicalStatusState
        {
            ApplicationRevision = 1,
            InstanceId = 99, DefinitionId = (uint)EnemyStatusID.Burn, StackMode = (byte)StatusStackMode.Add,
            MaxStacks = 10, Stack = 2, TargetEntityId = target, SourceEntityId = 77, SourcePlayerId = 77,
            StartTime = 10, Duration = 10, Version = 1, TickDamage = 1, TotalTicks = 5, TickInterval = 2,
            ExecutionAuthority = (byte)StatusExecutionAuthority.Server
        };
    }
}
