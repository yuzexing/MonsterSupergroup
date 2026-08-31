using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using AstralShift.QTI.Triggers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class EnemySimulationTests
    {
        [Test]
        public void Authority_NetworkManagedEnemyStartsFrozenAndExposesRoleCapabilities()
        {
            var gameObject = new GameObject("Enemy Simulation Authority Test");
            try
            {
                EnemySimulationAuthority authority =
                    gameObject.AddComponent<EnemySimulationAuthority>();
                authority.ConfigureNetworkManaged();

                Assert.That(authority.Role, Is.EqualTo(EnemySimulationRole.Frozen));
                Assert.That(authority.RunsNavigation, Is.False);
                Assert.That(authority.RunsCombatDecisions, Is.False);

                authority.ApplyRole(EnemySimulationRole.ClientOwner, 10u, 10u, 1u);
                Assert.That(authority.RunsNavigation, Is.True);
                Assert.That(authority.RunsCombatDecisions, Is.False);
                Assert.That(authority.RunsRubberBand, Is.True);

                authority.SetCombatDecisionSimulationEnabled(true);
                Assert.That(authority.RunsCombatDecisions, Is.True);

                authority.ApplyRole(EnemySimulationRole.Replica, 10u, 10u, 1u);
                Assert.That(authority.RunsNavigation, Is.False);
                Assert.That(authority.RunsCombatDecisions, Is.False);
                Assert.That(authority.ConsumesSnapshots, Is.True);

                authority.ApplyRole(EnemySimulationRole.ServerFallback, 0u, 20u, 2u);
                Assert.That(authority.RunsNavigation, Is.True);
                Assert.That(authority.RunsRubberBand, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Authority_BossModeRunsOnlyInServerAuthoritativeRole()
        {
            var gameObject = new GameObject("Boss Simulation Authority Test");
            try
            {
                EnemySimulationAuthority authority =
                    gameObject.AddComponent<EnemySimulationAuthority>();
                authority.ConfigureNetworkManaged(
                    EnemySimulationMode.BossServer,
                    enableCombatDecisions: true);

                Assert.That(
                    authority.SimulationMode,
                    Is.EqualTo(EnemySimulationMode.BossServer));
                authority.ApplyRole(
                    EnemySimulationRole.ServerAuthoritative,
                    0u,
                    10u,
                    1u);
                Assert.That(authority.RunsNavigation, Is.True);
                Assert.That(authority.RunsCombatDecisions, Is.True);
                Assert.That(authority.ConsumesSnapshots, Is.False);

                authority.ApplyRole(
                    EnemySimulationRole.Replica,
                    0u,
                    10u,
                    1u);
                Assert.That(authority.RunsNavigation, Is.False);
                Assert.That(authority.RunsCombatDecisions, Is.False);
                Assert.That(authority.ConsumesSnapshots, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Registry_AcceptsOnlyCurrentOwnerEpochAndIncreasingSequence()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 1d);
            EnemySimulationAssignment assignment =
                registry.AssignClientOwner(100u, 10u, 10u);

            EnemySimulationSnapshot snapshot = Snapshot(100u, assignment.Epoch, 1u);
            Assert.That(
                registry.TryAcceptClientSnapshot(10u, snapshot),
                Is.EqualTo(EnemySnapshotRejectionReason.None));
            Assert.That(
                registry.TryAcceptClientSnapshot(10u, snapshot),
                Is.EqualTo(EnemySnapshotRejectionReason.StaleSequence));

            snapshot.Sequence = 2u;
            Assert.That(
                registry.TryAcceptClientSnapshot(20u, snapshot),
                Is.EqualTo(EnemySnapshotRejectionReason.WrongOwner));

            snapshot.AssignmentEpoch++;
            Assert.That(
                registry.TryAcceptClientSnapshot(10u, snapshot),
                Is.EqualTo(EnemySnapshotRejectionReason.WrongEpoch));
        }

        [Test]
        public void Registry_AssignmentChangeInvalidatesOldOwnerSnapshots()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 1d);
            EnemySimulationAssignment owner =
                registry.AssignClientOwner(100u, 10u, 10u);
            EnemySimulationSnapshot accepted = Snapshot(100u, owner.Epoch, 4u);
            accepted.Position = new Vector2(4f, 2f);
            Assert.That(
                registry.TryAcceptClientSnapshot(10u, accepted),
                Is.EqualTo(EnemySnapshotRejectionReason.None));

            EnemySimulationAssignment fallback =
                registry.AssignServerFallback(100u, 20u);

            Assert.That(fallback.Epoch, Is.Not.EqualTo(owner.Epoch));
            Assert.That(fallback.SimulationOwnerPlayerId, Is.Zero);
            Assert.That(fallback.AggroTargetPlayerId, Is.EqualTo(20u));
            Assert.That(
                registry.TryAcceptClientSnapshot(10u, accepted),
                Is.EqualTo(EnemySnapshotRejectionReason.WrongHost));
            Assert.That(
                registry.TryGetLatestSnapshot(100u, out EnemySimulationSnapshot latest),
                Is.True);
            Assert.That(latest.Position, Is.EqualTo(new Vector2(4f, 2f)));
        }

        [Test]
        public void Registry_ServerAuthoritativeEnemyRejectsClientAndAcceptsServerSnapshot()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 1d);
            EnemySimulationAssignment assignment =
                registry.AssignServerAuthoritative(100u, 10u);
            EnemySimulationSnapshot snapshot = Snapshot(100u, assignment.Epoch, 1u);

            Assert.That(assignment.Host, Is.EqualTo(EnemySimulationHost.ServerAuthoritative));
            Assert.That(assignment.SimulationOwnerPlayerId, Is.Zero);
            Assert.That(assignment.AggroTargetPlayerId, Is.EqualTo(10u));
            Assert.That(
                registry.TryAcceptClientSnapshot(10u, snapshot),
                Is.EqualTo(EnemySnapshotRejectionReason.WrongHost));

            Assert.DoesNotThrow(() => registry.RecordServerSnapshot(snapshot));
            Assert.That(
                registry.TryGetLatestSnapshot(100u, out EnemySimulationSnapshot latest),
                Is.True);
            Assert.That(latest.Sequence, Is.EqualTo(1u));
        }

        [Test]
        public void Registry_ReportsAllEnemiesOwnedByDisconnectedPlayer()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 0d);
            registry.RegisterEnemy(101u, Vector2.zero, 0d);
            registry.RegisterEnemy(102u, Vector2.zero, 0d);
            registry.AssignClientOwner(100u, 10u, 10u);
            registry.AssignClientOwner(101u, 20u, 20u);
            registry.AssignClientOwner(102u, 10u, 10u);
            var results = new List<uint>();

            registry.GetEnemiesOwnedBy(10u, results);

            CollectionAssert.AreEquivalent(new[] { 100u, 102u }, results);
        }

        [Test]
        public void Registry_ReportsClientOwnedAndFallbackEnemiesDependingOnPlayer()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 0d);
            registry.RegisterEnemy(101u, Vector2.zero, 0d);
            registry.RegisterEnemy(102u, Vector2.zero, 0d);
            registry.AssignClientOwner(100u, 10u, 10u);
            registry.AssignServerFallback(101u, 10u);
            registry.AssignClientOwner(102u, 20u, 20u);
            var results = new List<uint>();

            registry.GetEnemiesDependingOnPlayer(10u, results);

            CollectionAssert.AreEquivalent(new[] { 100u, 101u }, results);
        }

        [Test]
        public void Registry_RejectsNonFiniteSnapshots()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 0d);
            EnemySimulationAssignment assignment =
                registry.AssignClientOwner(100u, 10u, 10u);
            EnemySimulationSnapshot snapshot = Snapshot(100u, assignment.Epoch, 1u);
            snapshot.Position.x = float.NaN;

            Assert.That(
                registry.TryAcceptClientSnapshot(10u, snapshot),
                Is.EqualTo(EnemySnapshotRejectionReason.InvalidValue));
        }

        [Test]
        public void Registry_RejectsRegressedSampleTimeEvenWhenSequenceIncreases()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 0d);
            EnemySimulationAssignment assignment =
                registry.AssignClientOwner(100u, 10u, 10u);
            EnemySimulationSnapshot first = Snapshot(100u, assignment.Epoch, 1u);
            first.SampleNetworkTime = 10d;
            EnemySimulationSnapshot regressed = Snapshot(100u, assignment.Epoch, 2u);
            regressed.SampleNetworkTime = 9d;

            Assert.That(
                registry.TryAcceptClientSnapshot(10u, first),
                Is.EqualTo(EnemySnapshotRejectionReason.None));
            Assert.That(
                registry.TryAcceptClientSnapshot(10u, regressed),
                Is.EqualTo(EnemySnapshotRejectionReason.StaleTimestamp));
        }

        [Test]
        public void Registry_OrdersSnapshotsPerEnemyInsteadOfAcrossBatches()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 0d);
            registry.RegisterEnemy(101u, Vector2.zero, 0d);
            EnemySimulationAssignment firstAssignment =
                registry.AssignClientOwner(100u, 10u, 10u);
            EnemySimulationAssignment secondAssignment =
                registry.AssignClientOwner(101u, 10u, 10u);

            Assert.That(
                registry.TryAcceptClientSnapshot(
                    10u,
                    Snapshot(101u, secondAssignment.Epoch, 2u)),
                Is.EqualTo(EnemySnapshotRejectionReason.None));
            Assert.That(
                registry.TryAcceptClientSnapshot(
                    10u,
                    Snapshot(100u, firstAssignment.Epoch, 1u)),
                Is.EqualTo(EnemySnapshotRejectionReason.None),
                "Out-of-order datagrams can carry different Enemies and must not be dropped globally.");
        }

        [Test]
        public void Registry_AcceptsAttackPresentationOnlyFromCurrentOwnerAndEpoch()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 0d);
            EnemySimulationAssignment assignment =
                registry.AssignClientOwner(100u, 10u, 10u);
            EnemyAttackPresentationEdge edge = AttackEdge(
                100u,
                assignment.Epoch,
                1u,
                EnemyAttackPresentationPhase.Warning);

            Assert.That(
                registry.TryAcceptClientAttackPresentation(20u, edge),
                Is.EqualTo(EnemyAttackPresentationRejectionReason.WrongOwner));
            Assert.That(
                registry.TryAcceptClientAttackPresentation(10u, edge),
                Is.EqualTo(EnemyAttackPresentationRejectionReason.None));
            Assert.That(
                registry.TryAcceptClientAttackPresentation(10u, edge),
                Is.EqualTo(EnemyAttackPresentationRejectionReason.StaleSequence));

            edge.StateSequence = 2u;
            edge.AssignmentEpoch++;
            Assert.That(
                registry.TryAcceptClientAttackPresentation(10u, edge),
                Is.EqualTo(EnemyAttackPresentationRejectionReason.WrongEpoch));
        }

        [Test]
        public void Registry_CachesLatestReliableAttackEdgeAndClearsItOnAssignmentChange()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 0d);
            EnemySimulationAssignment assignment =
                registry.AssignClientOwner(100u, 10u, 10u);
            EnemyAttackPresentationEdge warning = AttackEdge(
                100u,
                assignment.Epoch,
                1u,
                EnemyAttackPresentationPhase.Warning);
            EnemyAttackPresentationEdge active = AttackEdge(
                100u,
                assignment.Epoch,
                2u,
                EnemyAttackPresentationPhase.Active);
            active.StateStartNetworkTime = warning.StateStartNetworkTime;

            Assert.That(
                registry.TryAcceptClientAttackPresentation(10u, warning),
                Is.EqualTo(EnemyAttackPresentationRejectionReason.None));
            Assert.That(
                registry.TryAcceptClientAttackPresentation(10u, active),
                Is.EqualTo(EnemyAttackPresentationRejectionReason.None),
                "Two phase transitions may legitimately occur during one network frame.");
            Assert.That(
                registry.TryGetLatestAttackPresentation(100u, out var latest),
                Is.True);
            Assert.That(latest.Phase, Is.EqualTo(EnemyAttackPresentationPhase.Active));
            Assert.That(latest.StateSequence, Is.EqualTo(2u));

            registry.AssignServerFallback(100u, 10u);
            Assert.That(
                registry.TryGetLatestAttackPresentation(100u, out _),
                Is.False,
                "An old Owner edge must not be replayed under a new assignment epoch.");
        }

        [Test]
        public void Registry_RejectsInvalidAttackPresentationAndAcceptsServerFallbackEdge()
        {
            var registry = new ServerEnemySimulationRegistry();
            registry.RegisterEnemy(100u, Vector2.zero, 0d);
            EnemySimulationAssignment owner =
                registry.AssignClientOwner(100u, 10u, 10u);
            EnemyAttackPresentationEdge invalid = AttackEdge(
                100u,
                owner.Epoch,
                1u,
                EnemyAttackPresentationPhase.Warning);
            invalid.Facing.x = float.NaN;
            Assert.That(
                registry.TryAcceptClientAttackPresentation(10u, invalid),
                Is.EqualTo(EnemyAttackPresentationRejectionReason.InvalidValue));

            EnemySimulationAssignment fallback =
                registry.AssignServerFallback(100u, 10u);
            EnemyAttackPresentationEdge serverEdge = AttackEdge(
                100u,
                fallback.Epoch,
                1u,
                EnemyAttackPresentationPhase.Inactive);
            Assert.DoesNotThrow(() =>
                registry.RecordServerAttackPresentation(serverEdge));
            Assert.That(
                registry.TryAcceptClientAttackPresentation(10u, serverEdge),
                Is.EqualTo(EnemyAttackPresentationRejectionReason.WrongHost));
        }

        [Test]
        public void AttackPresentationEdge_ReconstructsElapsedTimeFromNetworkClock()
        {
            EnemyAttackPresentationEdge edge = AttackEdge(
                100u,
                2u,
                1u,
                EnemyAttackPresentationPhase.Active);
            edge.StateStartNetworkTime = 10d;

            Assert.That(edge.ElapsedAt(10.25d), Is.EqualTo(0.25d).Within(0.0001d));
            Assert.That(edge.RemainingAt(10.25d), Is.EqualTo(0.75d).Within(0.0001d));
            Assert.That(edge.IsExpiredAt(11d), Is.True);
            Assert.That(edge.ElapsedAt(9d), Is.Zero,
                "Clock skew must not produce a negative presentation phase.");
        }

        [Test]
        public void SnapshotBuffer_InterpolatesAndRejectsStalePackets()
        {
            var buffer = new EnemySnapshotBuffer();
            EnemySimulationSnapshot first = Snapshot(100u, 3u, 1u);
            first.SampleNetworkTime = 10d;
            first.Position = Vector2.zero;
            EnemySimulationSnapshot second = Snapshot(100u, 3u, 2u);
            second.SampleNetworkTime = 11d;
            second.Position = new Vector2(10f, 0f);

            Assert.That(buffer.Push(first), Is.True);
            Assert.That(buffer.Push(second), Is.True);
            Assert.That(buffer.Push(first), Is.False);
            Assert.That(
                buffer.TrySample(10.5d, 0.1d, out Vector2 position, out _),
                Is.True);
            Assert.That(position.x, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void SnapshotBuffer_DiscontinuityClearsOldInterpolationHistory()
        {
            var buffer = new EnemySnapshotBuffer();
            EnemySimulationSnapshot first = Snapshot(100u, 3u, 1u);
            first.SampleNetworkTime = 10d;
            first.Position = Vector2.zero;
            EnemySimulationSnapshot teleport = Snapshot(100u, 3u, 2u);
            teleport.SampleNetworkTime = 10.1d;
            teleport.Position = new Vector2(100f, 0f);
            teleport.Flags = EnemySimulationSnapshotFlags.Discontinuity;

            buffer.Push(first);
            buffer.Push(teleport);

            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(
                buffer.TrySample(10.1d, 0d, out Vector2 position, out _),
                Is.True);
            Assert.That(position, Is.EqualTo(teleport.Position));
        }

        [Test]
        public void SnapshotBuffer_NewEpochClearsOldHistoryAndAllowsSequenceRestart()
        {
            var buffer = new EnemySnapshotBuffer();
            EnemySimulationSnapshot oldEpoch = Snapshot(100u, 3u, 50u);
            oldEpoch.SampleNetworkTime = 10d;
            EnemySimulationSnapshot newEpoch = Snapshot(100u, 4u, 1u);
            newEpoch.SampleNetworkTime = 11d;
            newEpoch.Position = new Vector2(25f, 0f);

            Assert.That(buffer.Push(oldEpoch), Is.True);
            Assert.That(buffer.Push(newEpoch), Is.True);
            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(
                buffer.TrySample(11d, 0d, out Vector2 position, out _),
                Is.True);
            Assert.That(position, Is.EqualTo(newEpoch.Position));
        }

        [Test]
        public void SnapshotBuffer_BoundsExtrapolationThenFreezesAtLastEstimate()
        {
            var buffer = new EnemySnapshotBuffer();
            EnemySimulationSnapshot snapshot = Snapshot(100u, 3u, 1u);
            snapshot.SampleNetworkTime = 10d;
            snapshot.Position = Vector2.zero;
            snapshot.Velocity = new Vector2(10f, 0f);
            buffer.Push(snapshot);

            Assert.That(
                buffer.TrySample(20d, 0.1d, out Vector2 position, out _),
                Is.True);
            Assert.That(position.x, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void SequenceComparison_AcceptsUnsignedWrapAndRejectsOldValues()
        {
            Assert.That(
                EnemySimulationSequence.IsNewer(uint.MaxValue, uint.MaxValue - 1u),
                Is.True);
            Assert.That(EnemySimulationSequence.IsNewer(1u, uint.MaxValue), Is.True);
            Assert.That(EnemySimulationSequence.IsNewer(uint.MaxValue, 1u), Is.False);
            Assert.That(EnemySimulationSequence.IsNewer(1u, 1u), Is.False);
        }

        [Test]
        public void ProcessValidationOptions_ParseExplicitHostAndDefaults()
        {
            bool parsed = NetworkEnemyProcessValidationBootstrap.TryParseOptions(
                new[]
                {
                    "Game.exe",
                    "--enemy-sim-role=host",
                    "--enemy-sim-port=8123",
                    "--enemy-sim-timeout=45"
                },
                out EnemySimulationValidationOptions options,
                out string error);

            Assert.That(parsed, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(options.Role, Is.EqualTo(EnemySimulationValidationRole.Host));
            Assert.That(options.Address, Is.EqualTo("127.0.0.1"));
            Assert.That(options.Port, Is.EqualTo(8123));
            Assert.That(options.TimeoutSeconds, Is.EqualTo(45f));
        }

        [Test]
        public void ProcessValidationOptions_RejectInvalidRoleAndStayInertWithoutRole()
        {
            Assert.That(
                NetworkEnemyProcessValidationBootstrap.TryParseOptions(
                    new[] { "Game.exe" },
                    out _,
                    out string missingRoleError),
                Is.False);
            Assert.That(missingRoleError, Is.Null);

            Assert.That(
                NetworkEnemyProcessValidationBootstrap.TryParseOptions(
                    new[] { "Game.exe", "--enemy-sim-role=server" },
                    out _,
                    out string invalidRoleError),
                Is.False);
            Assert.That(invalidRoleError, Does.Contain("Unknown"));
        }

        [Test]
        public void ProductNetworkPrefab_UsesMovementOnlyWithLocalContactDamage()
        {
            const string path =
                "Assets/_Project/Content/NetworkCombat/NetworkEnemyBase.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null);
            EnemyController controller = prefab.GetComponent<EnemyController>();
            NetworkEnemySimulationAgent agent =
                prefab.GetComponent<NetworkEnemySimulationAgent>();
            PlayerDamageInteraction contactDamage =
                prefab.GetComponentInChildren<PlayerDamageInteraction>(true);
            InteractionTrigger contactTrigger = contactDamage != null
                ? contactDamage.GetComponent<InteractionTrigger>()
                : null;
            Collider2D contactCollider = contactDamage != null
                ? contactDamage.GetComponent<Collider2D>()
                : null;

            Assert.That(controller, Is.Not.Null);
            Assert.That(agent, Is.Not.Null);
            Assert.That(agent.ProductMovementOnly, Is.True,
                "NetworkEnemyBase must not run the unsynchronized product attack FSM.");
            Assert.That(controller.alwaysAttacking, Is.True);
            Assert.That(controller.hasAttackAnimation, Is.False);
            Assert.That(contactDamage, Is.Not.Null);
            Assert.That(contactTrigger, Is.Not.Null);
            Assert.That(contactTrigger.interaction, Is.SameAs(contactDamage));
            Assert.That(contactCollider, Is.Not.Null);
            Assert.That(contactCollider.isTrigger, Is.True);
            Assert.That(contactDamage.gameObject.layer,
                Is.EqualTo(LayerMask.NameToLayer("EnemyAttack")));
        }

        [Test]
        public void SkeletonNetworkPrefab_RestoresMeleeAndAnimationReferences()
        {
            const string path =
                "Assets/_Project/Content/NetworkCombat/NetworkEnemySkeleton.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null);
            EnemyController controller = prefab.GetComponent<EnemyController>();
            EnemyAttackMelee melee = prefab.GetComponent<EnemyAttackMelee>();
            NetworkEnemySimulationAgent agent =
                prefab.GetComponent<NetworkEnemySimulationAgent>();
            NetworkEnemyMeleeReplica replica =
                prefab.GetComponent<NetworkEnemyMeleeReplica>();

            Assert.That(controller, Is.Not.Null);
            Assert.That(melee, Is.Not.Null);
            Assert.That(melee.enabled, Is.False,
                "Attack scripts must remain dormant until a simulation role is assigned.");
            Assert.That(melee.attackPrefab, Is.Not.Null);
            Assert.That(melee.attackPrefab.damageInteraction,
                Is.TypeOf<PlayerDamageInteraction>());
            Assert.That(melee.attackPrefab.attackWarning, Is.Not.Null);
            var damageTrigger = melee.attackPrefab.damageInteraction.GetComponent<
                AstralShift.QTI.Triggers.Physics2D.StepOn2DTrigger>();
            Assert.That(damageTrigger, Is.Not.Null);
            Assert.That(damageTrigger.layerMask.value,
                Is.EqualTo(1 << LayerMask.NameToLayer("PlayerHitbox")),
                "Replica melee collision filtering must target the local PlayerHitbox layer.");
            Assert.That(agent, Is.Not.Null);
            Assert.That(agent.ProductMovementOnly, Is.False);
            Assert.That(replica, Is.Not.Null);
            Assert.That(controller.usesPathfinding, Is.False,
                "This attack-validation prefab deliberately uses DefaultMovement " +
                "until the sandbox has a scanned A* graph.");

            EnemyAnimator animator = controller.enemyAnimator;
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.animancer, Is.Not.Null);
            Assert.That(animator.MoveLeftDown?.Clip, Is.Not.Null);
            Assert.That(animator.MoveRightDown?.Clip, Is.Not.Null);
            Assert.That(animator.AttackWarningLeftDown?.Clip, Is.Not.Null);
            Assert.That(animator.AttackWarningRightDown?.Clip, Is.Not.Null);
            Assert.That(animator.AttackLeftDown?.Clip, Is.Not.Null);
            Assert.That(animator.AttackRightDown?.Clip, Is.Not.Null);
            Assert.That(animator.RecoveryLeftDown?.Clip, Is.Not.Null);
            Assert.That(animator.RecoveryRightDown?.Clip, Is.Not.Null);

            Assert.That(
                prefab.GetComponentsInChildren<Component>(true),
                Has.None.Null,
                "The migrated prefab must not contain a Missing Script.");
            Assert.That(
                prefab.GetComponents<MonoBehaviour>(),
                Has.None.Matches<MonoBehaviour>(component =>
                    component != null &&
                    component.GetType().Name == "NetworkTransformReliable"));
        }

        private static EnemySimulationSnapshot Snapshot(
            uint enemyId,
            uint epoch,
            uint sequence)
        {
            return new EnemySimulationSnapshot
            {
                EnemyEntityId = enemyId,
                AssignmentEpoch = epoch,
                Sequence = sequence,
                SampleNetworkTime = sequence,
                Position = new Vector2(sequence, sequence),
                Velocity = Vector2.right,
                Facing = Vector2.right
            };
        }

        private static EnemyAttackPresentationEdge AttackEdge(
            uint enemyId,
            uint epoch,
            uint sequence,
            EnemyAttackPresentationPhase phase)
        {
            return new EnemyAttackPresentationEdge
            {
                EnemyEntityId = enemyId,
                AssignmentEpoch = epoch,
                StateSequence = sequence,
                StateStartNetworkTime = sequence,
                PhaseDuration = 1f,
                Phase = phase,
                Facing = Vector2.right
            };
        }
    }
}
