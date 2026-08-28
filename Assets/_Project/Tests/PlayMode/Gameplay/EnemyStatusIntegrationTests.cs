using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;
using GasEnemyStatusID = MonsterSupergroup.GAS.EnemyStatusID;
using LegacyDamageType = AstralShift.HellMaiden.Player.Attacks.DamageType;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class EnemyStatusIntegrationTests
    {
        [Test]
        public void LegacyFacade_AndCombatantShareOneStatusController()
        {
            TestRig rig = CreateRig();
            try
            {
                Assert.That(rig.Status.Runtime, Is.SameAs(rig.Combatant.StatusController));

                rig.Status.Apply(GasEnemyStatusID.Slow, 0.5f, 1f);
                Assert.That(rig.Status.HasStatus(GasEnemyStatusID.Slow), Is.True);
                Assert.That(rig.Enemy.stats.SpeedMultiplier, Is.EqualTo(0.5f));

                rig.Combatant.ApplyStatus(new StatusApplication(
                    new StatusDefinition(
                        GasEnemyStatusID.Burn,
                        StatusStackMode.HighestPriority,
                        1),
                    tickDamage: 3,
                    numberOfHits: 1,
                    hitIntervalDuration: 1f,
                    priority: 3f));
                Assert.That(rig.Status.HasStatus(GasEnemyStatusID.Burn), Is.True);
                Assert.That(rig.Status.HasAnyStatus(), Is.True);

                rig.Combatant.AdvanceStatuses(1f);
                Assert.That(rig.Status.HasAnyStatus(), Is.False);
                Assert.That(rig.Enemy.stats.SpeedMultiplier, Is.EqualTo(1f));
                Assert.That(rig.Combatant.CurrentHealth, Is.EqualTo(97));
            }
            finally
            {
                Object.DestroyImmediate(rig.Root);
            }
        }

        [Test]
        public void CanonicalReplica_DrivesLegacySlowBindingAndRemoval()
        {
            TestRig rig = CreateRig();
            try
            {
                var instanceId = new StatusInstanceId(501);
                var slow = new StatusInstance(
                    instanceId,
                    new StatusDefinition(
                        GasEnemyStatusID.Slow,
                        StatusStackMode.HighestPriority,
                        1),
                    sourcePlayerId: 2,
                    sourceEntityId: 20,
                    targetEntityId: 100,
                    stack: 1,
                    startTime: 0,
                    duration: 2,
                    executionAuthority: StatusExecutionAuthority.SourceClient,
                    version: 1,
                    tickDamage: 0,
                    totalTicks: 1,
                    completedTicks: 0,
                    tickInterval: 2,
                    priority: 1,
                    damageSourceId: 20,
                    magnitude: 0.25f);

                Assert.That(rig.Combatant.StatusController.UpsertCanonical(slow), Is.True);
                Assert.That(rig.Status.HasStatus(GasEnemyStatusID.Slow), Is.True);
                Assert.That(rig.Enemy.stats.SpeedMultiplier, Is.EqualTo(0.25f));

                Assert.That(
                    rig.Combatant.StatusController.RemoveCanonical(instanceId, 2),
                    Is.True);
                Assert.That(rig.Status.HasStatus(GasEnemyStatusID.Slow), Is.False);
                Assert.That(rig.Enemy.stats.SpeedMultiplier, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(rig.Root);
            }
        }

        [Test]
        public void LegacyApplication_PreservesSourceIdentityAuthorityAndMagnitude()
        {
            TestRig rig = CreateRig();
            try
            {
                rig.Combatant.ConfigureEntityId(100);
                var services = new CombatRuntimeServices(
                    7,
                    70,
                    new SequentialCombatEventIdSource(7, 1),
                    NullCombatEventSink.Instance);
                var execution = new LegacyCombatExecution(services);
                CombatContext context = execution.BeginAttack(9, CombatTags.Attack);
                var source = new LegacyDamageSource(
                    execution,
                    context.WithTarget(100),
                    70);

                rig.Status.Apply(
                    GasEnemyStatusID.Weaken,
                    0.6f,
                    2f,
                    source: source);

                StatusInstance instance =
                    rig.Status.Runtime.GetInstances(GasEnemyStatusID.Weaken)[0];
                Assert.That(instance.SourcePlayerId, Is.EqualTo(7));
                Assert.That(instance.SourceEntityId, Is.EqualTo(70));
                Assert.That(instance.TargetEntityId, Is.EqualTo(100));
                Assert.That(
                    instance.ExecutionAuthority,
                    Is.EqualTo(StatusExecutionAuthority.SourceClient));
                Assert.That(instance.Magnitude, Is.EqualTo(0.6f));
                Assert.That(rig.Enemy.stats.DamageMultiplier, Is.EqualTo(0.6f));
            }
            finally
            {
                Object.DestroyImmediate(rig.Root);
            }
        }

        [Test]
        public void LegacyBleed_UsesCombatantTickDamageAndConsumeSemantics()
        {
            TestRig rig = CreateRig();
            try
            {
                rig.Status.Apply(GasEnemyStatusID.Bleed, 4, 3, 1f);
                rig.Status.Apply(GasEnemyStatusID.Bleed, 4, 3, 1f);

                rig.Status.ConsumeStack(GasEnemyStatusID.Bleed);

                Assert.That(rig.Combatant.CurrentHealth, Is.EqualTo(92));
                Assert.That(rig.Combatant.StatusDamageTaken, Is.EqualTo(8));
                Assert.That(rig.Combatant.StatusTickCount, Is.EqualTo(2));
                Assert.That(rig.Status.HasStatus(GasEnemyStatusID.Bleed), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(rig.Root);
            }
        }

        [Test]
        public void LegacyTransfer_MovesStatusStateAndLegacyBindingsToTarget()
        {
            TestRig source = CreateRig();
            TestRig target = CreateRig();
            try
            {
                source.Status.Apply(GasEnemyStatusID.Slow, 0.4f, 2f);
                source.Status.Apply(GasEnemyStatusID.Bleed, 5, 2, 1f);
                source.Combatant.AdvanceStatuses(0.25f);

                source.Status.TransferTo(target.Enemy);

                Assert.That(source.Status.HasAnyStatus(), Is.False);
                Assert.That(source.Enemy.stats.SpeedMultiplier, Is.EqualTo(1f));
                Assert.That(target.Status.HasStatus(GasEnemyStatusID.Slow), Is.True);
                Assert.That(target.Status.HasStatus(GasEnemyStatusID.Bleed), Is.True);
                Assert.That(target.Enemy.stats.SpeedMultiplier, Is.EqualTo(0.4f));

                target.Combatant.AdvanceStatuses(0.74f);
                Assert.That(target.Combatant.CurrentHealth, Is.EqualTo(100));
                target.Combatant.AdvanceStatuses(0.01f);
                Assert.That(target.Combatant.CurrentHealth, Is.EqualTo(95));
            }
            finally
            {
                Object.DestroyImmediate(source.Root);
                Object.DestroyImmediate(target.Root);
            }
        }

        private static TestRig CreateRig()
        {
            var root = new GameObject("EnemyStatusIntegrationTest");
            CombatantBehaviour combatant = root.AddComponent<CombatantBehaviour>();
            root.AddComponent<EnemyCombatantBinding>();
            EnemyStatus status = root.AddComponent<EnemyStatus>();
            TestEnemyController enemy = root.AddComponent<TestEnemyController>();
            enemy.stats = new EnemyStats();
            enemy.stats.Init(new EnemyStatsValues
            {
                Health = 100,
                Damage = 10,
                Speed = 8,
                XP = 1,
                KnockBackMultiplier = 1,
                WindMultiplier = 1
            });
            enemy.status = status;
            combatant.Initialize(100);
            status.Init(enemy);
            return new TestRig(root, enemy, status, combatant);
        }

        private readonly struct TestRig
        {
            public TestRig(
                GameObject root,
                TestEnemyController enemy,
                EnemyStatus status,
                CombatantBehaviour combatant)
            {
                Root = root;
                Enemy = enemy;
                Status = status;
                Combatant = combatant;
            }

            public GameObject Root { get; }
            public TestEnemyController Enemy { get; }
            public EnemyStatus Status { get; }
            public CombatantBehaviour Combatant { get; }
        }

        private sealed class TestEnemyController : BaseEnemyController
        {
            public override bool IsDead => !IsAlive;

            public override void Init(int id)
            {
            }

            public override void Dispose()
            {
            }

            public override void Damage(
                Vector2 attackPosition,
                WeaponBehaviour weapon,
                LegacyDamageType damageType)
            {
            }

            public override void Damage(int value, LegacyDamageType damageType)
            {
            }

            public override void ApplyKnockBack(
                Vector2 attackPosition,
                WeaponBehaviour weaponBehaviour,
                bool isFatal)
            {
            }

            public override void BruteforceKnockBack(
                Vector2 attackPosition,
                KnockbackSettings settings)
            {
            }

            public override Vector2 GetHurtBoxPosition()
            {
                return transform.position;
            }
        }
    }
}
