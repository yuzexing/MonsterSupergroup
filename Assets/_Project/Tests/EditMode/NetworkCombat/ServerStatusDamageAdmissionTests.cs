using System;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class ServerStatusDamageAdmissionTests
    {
        private const uint PlayerId = 1;
        private const uint SourceId = 10;
        private const uint EnemyId = 100;
        private const uint WeaponId = 77;
        private const ushort SourceSlot = 7;
        private const ushort Epoch = 3;

        [TestCase(false)]
        [TestCase(true)]
        public void RealOnHitBurn_TicksAfterWeaponRootRetires_WithPredictedOrCanonicalParent(bool canonicalEcho)
        {
            using (var run = new BurnRun())
            {
                CanonicalWorldBatch accepted = run.ApplyInitial();
                StatusMutation mutation = run.Initial.StatusMutations[0];
                Assert.That(mutation.BuildId, Is.EqualTo(OnHitBurnModifier.ModifierIdValue));
                Assert.That(mutation.RootEventId, Is.EqualTo(run.RootId));
                if (canonicalEcho)
                    Assert.That(run.Target.StatusController.UpsertCanonical(accepted.Statuses[0].ToStatusInstance()), Is.True);
                run.Retire();

                CombatResult tick = run.NextTicks(1f)[0];
                Assert.That(tick.ParentEventId, Is.EqualTo(canonicalEcho ? mutation.EventId : mutation.ParentEventId));
                Assert.That(tick.ChainDepth, Is.EqualTo(canonicalEcho ? mutation.ChainDepth + 1 : mutation.ChainDepth));
                run.SubmitTicks(1d, tick);

                run.AssertHealth(85);
                Assert.That(run.Gateway.Metrics.AcceptedCombatResults, Is.EqualTo(2));
                Assert.That(run.Gateway.Attacks.ActiveCount(PlayerId), Is.Zero);
            }
        }

        [Test]
        public void RealOnHitBurn_ApplicationAndFirstTickInSameBatch_AuthorizesTickAfterMutation()
        {
            using (var run = new BurnRun(drainInitial: false))
            {
                run.Target.AdvanceStatuses(1f);
                CombatSubmissionBatch outgoing = run.Collector.Drain(1);
                Assert.That(outgoing.Results, Has.Length.EqualTo(2));
                Assert.That(outgoing.StatusMutations, Has.Length.EqualTo(1));

                run.Gateway.ProcessBatch(PlayerId, outgoing, 1d);

                run.AssertHealth(85);
                Assert.That(run.Gateway.Metrics.AcceptedStatusMutations, Is.EqualTo(1));
                Assert.That(run.Gateway.Metrics.AcceptedCombatResults, Is.EqualTo(2));
            }
        }

        [Test]
        public void FinalTickAfterCanonicalStatusExpiry_IsAcceptedWithinDeliveryGrace()
        {
            using (var run = new BurnRun())
            {
                run.ApplyInitial();
                run.Retire();
                run.SubmitTicks(2d, run.NextTicks(2f));
                run.Gateway.Advance(3.2d);
                Assert.That(run.Gateway.Statuses.Count, Is.Zero);
                run.AssertHealth(80);

                run.SubmitTicks(3.5d, run.NextTicks(1f));

                run.AssertHealth(75);
                Assert.That(run.Gateway.Metrics.AcceptedCombatResults, Is.EqualTo(4));
            }
        }

        [Test]
        public void UnusedTickAfterDeliveryGrace_CannotDamage()
        {
            using (var run = new BurnRun())
            {
                run.ApplyInitial();
                run.Retire();
                run.Gateway.Advance(5.01d);
                run.SubmitTicks(5.01d, run.NextTicks(1f));

                run.AssertHealth(90);
                Assert.That(run.Gateway.Metrics.GetRejected(CombatRejectionReason.InvalidStatus), Is.EqualTo(1));
            }
        }

        [TestCase("damage")]
        [TestCase("root")]
        [TestCase("target")]
        [TestCase("parent")]
        [TestCase("ability")]
        [TestCase("modifier")]
        [TestCase("depth")]
        [TestCase("source")]
        [TestCase("nonperiodic")]
        public void ReceiptDoesNotAuthorizeDifferentOutcome_AndRejectionDoesNotConsumeValidTick(string defect)
        {
            using (var run = new BurnRun(ticks: 1))
            {
                run.ApplyInitial();
                run.Retire();
                CombatResult valid = run.NextTicks(1f)[0];
                CombatResult invalid = run.WithNewEvent(valid);
                switch (defect)
                {
                    case "damage": invalid.Damage++; break;
                    case "root": invalid.RootEventId = run.Event(999); break;
                    case "target": invalid.TargetEntityId = EnemyId + 1; break;
                    case "parent": invalid.ParentEventId = run.RootId; break;
                    case "ability": invalid.AbilityId++; break;
                    case "modifier": invalid.BuildId++; break;
                    case "depth": invalid.ChainDepth++; break;
                    case "source": invalid.SourceEntityId = SourceId + 1; break;
                    case "nonperiodic": invalid.DamageTags &= ~(ulong)CombatTags.Periodic; break;
                }
                run.Gateway.Ledger.RegisterEntity(EnemyId + 1, 100, CombatEntityKind.Enemy,
                    CombatEntityAuthority.ServerCanonical);
                run.Gateway.Ledger.RegisterSource(SourceId + 1, PlayerId);

                run.SubmitTicks(1d, invalid, valid);

                run.AssertHealth(85);
                Assert.That(run.Gateway.Metrics.AcceptedCombatResults, Is.EqualTo(2));
                CombatRejectionReason reason = defect == "nonperiodic"
                    ? CombatRejectionReason.InvalidAttackRoot : CombatRejectionReason.InvalidStatus;
                Assert.That(run.Gateway.Metrics.GetRejected(reason), Is.EqualTo(1));
                Assert.That(run.Gateway.Ledger.TryGetState(EnemyId + 1, out CanonicalEntityState other), Is.True);
                Assert.That(other.Health, Is.EqualTo(100));
            }
        }

        [Test]
        public void PredictedAndCanonicalParentAliases_ShareOneTickBudget()
        {
            using (var run = new BurnRun(ticks: 1))
            {
                run.ApplyInitial();
                run.Retire();
                CombatResult predicted = run.NextTicks(1f)[0];
                CombatResult canonical = run.WithNewEvent(predicted);
                canonical.ParentEventId = run.Initial.StatusMutations[0].EventId;
                canonical.ChainDepth++;

                run.SubmitTicks(1d, predicted, canonical);

                run.AssertHealth(85);
                Assert.That(run.Gateway.Metrics.GetRejected(CombatRejectionReason.InvalidStatus), Is.EqualTo(1));
            }
        }

        [Test]
        public void DuplicateTickEvent_DoesNotSpendBudgetAgain_AndExtraUniqueTickIsRejected()
        {
            using (var run = new BurnRun(ticks: 2))
            {
                run.ApplyInitial();
                run.Retire();
                CombatResult first = run.NextTicks(1f)[0];
                run.SubmitTicks(1d, first, first);
                CombatResult second = run.NextTicks(1f)[0];
                run.SubmitTicks(2d, second, run.WithNewEvent(second));

                run.AssertHealth(80);
                Assert.That(run.Gateway.Metrics.AcceptedCombatResults, Is.EqualTo(3));
                Assert.That(run.Gateway.Metrics.GetRejected(CombatRejectionReason.DuplicateEvent), Is.EqualTo(1));
                Assert.That(run.Gateway.Metrics.GetRejected(CombatRejectionReason.InvalidStatus), Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StatusWithoutAdmittedWeaponRoot_CannotMintTickReceipt(bool eraseAbility)
        {
            using (var run = new BurnRun(admitRoot: false))
            {
                StatusMutation mutation = run.Initial.StatusMutations[0];
                CombatResult tick = run.NextTicks(1f)[0];
                if (eraseAbility)
                {
                    mutation.AbilityId = 0;
                    mutation.RootEventId = 0;
                    tick.AbilityId = 0;
                    tick.RootEventId = mutation.EventId;
                }
                run.Gateway.ProcessBatch(PlayerId, new CombatSubmissionBatch
                {
                    BatchSequence = 1,
                    StatusMutations = new[] { mutation },
                    Results = new[] { tick }
                }, 1d);

                run.AssertHealth(100);
                Assert.That(run.Gateway.Metrics.AcceptedStatusMutations, Is.Zero);
                Assert.That(run.Gateway.Metrics.AcceptedCombatResults, Is.Zero);
                Assert.That(run.Gateway.StatusDamageAdmissions.Validate(tick, 1d),
                    Is.EqualTo(CombatRejectionReason.InvalidStatus));
            }
        }

        [Test]
        public void InvalidStatusApplication_CannotAuthorizeItsPeriodicOutcome()
        {
            using (var run = new BurnRun())
            {
                StatusMutation mutation = run.Initial.StatusMutations[0];
                mutation.TickInterval = 0;
                CombatResult tick = run.NextTicks(1f)[0];

                run.Gateway.ProcessBatch(PlayerId, new CombatSubmissionBatch
                {
                    BatchSequence = 1,
                    StatusMutations = new[] { mutation },
                    Results = new[] { tick }
                }, 1d);

                run.AssertHealth(100);
                Assert.That(run.Gateway.Metrics.AcceptedStatusMutations, Is.Zero);
                Assert.That(run.Gateway.Metrics.AcceptedCombatResults, Is.Zero);
                Assert.That(run.Gateway.Metrics.GetRejected(CombatRejectionReason.InvalidStatus), Is.EqualTo(2));
            }
        }

        [Test]
        public void SourceDisconnectTakeover_ImmediatelyRevokesOwnerTickReceipt()
        {
            using (var run = new BurnRun())
            {
                run.ApplyInitial();
                run.Retire();
                CombatResult pending = run.NextTicks(1f)[0];

                CanonicalWorldBatch takeover = run.Gateway.HandleSourceDisconnected(PlayerId, 1d);

                Assert.That(takeover.Statuses, Has.Length.EqualTo(1));
                Assert.That(takeover.Statuses[0].ExecutionAuthority,
                    Is.EqualTo((byte)StatusExecutionAuthority.Server));
                Assert.That(run.Gateway.StatusDamageAdmissions.Validate(pending, 1d),
                    Is.EqualTo(CombatRejectionReason.InvalidStatus));
                run.SubmitTicks(1d, pending);
                run.AssertHealth(90);
                run.Gateway.Advance(2d);
                run.AssertHealth(85);
            }
        }

        [Test]
        public void Disconnect_ClearsTickReceiptsBeforeNewPlayerRegistration()
        {
            using (var run = new BurnRun())
            {
                run.ApplyInitial();
                run.Retire();
                CombatResult pending = run.NextTicks(1f)[0];
                Assert.That(run.Gateway.StatusDamageAdmissions.Validate(pending, 1d),
                    Is.EqualTo(CombatRejectionReason.None));

                run.Gateway.UnregisterClientIdentity(PlayerId);

                Assert.That(run.Gateway.StatusDamageAdmissions.Validate(pending, 1d),
                    Is.EqualTo(CombatRejectionReason.InvalidStatus));
                run.Gateway.RegisterClientIdentity(PlayerId, SourceSlot, Epoch + 1);
                run.Gateway.Attacks.RegisterPlayer(PlayerId);
                run.SubmitTicks(1d, pending);
                run.AssertHealth(90);
                Assert.That(run.Gateway.Metrics.GetRejected(CombatRejectionReason.InvalidSequence), Is.EqualTo(1));
            }
        }

        private sealed class BurnRun : IDisposable
        {
            private readonly GameObject targetObject;
            private readonly SequentialCombatEventIdSource ids = new SequentialCombatEventIdSource(SourceSlot, Epoch);
            private uint batchSequence = 1;

            public BurnRun(int ticks = 3, bool admitRoot = true, bool drainInitial = true)
            {
                Gateway = new ServerCombatGateway();
                Gateway.RegisterClientIdentity(PlayerId, SourceSlot, Epoch);
                Gateway.Attacks.RegisterPlayer(PlayerId);
                Gateway.Ledger.RegisterSource(SourceId, PlayerId);
                Gateway.Ledger.RegisterEntity(EnemyId, 100, CombatEntityKind.Enemy,
                    CombatEntityAuthority.ServerCanonical);
                Collector = new ClientCombatCollector(PlayerId, ids);
                targetObject = new GameObject("Source-client burn admission target");
                Target = targetObject.AddComponent<CombatantBehaviour>();
                Target.Initialize(100);
                Target.ConfigureEntityId(EnemyId);
                Target.ConfigureStatusExecution(new StatusExecutionScope(false, false, PlayerId));
                Target.ConfigureStatusCombatEvents(ids, Collector);
                Collector.Observe(Target.StatusController);
                var modifiers = new RuntimeEquipmentModifiers();
                modifiers.Add(new OnHitBurnModifier(new OnHitBurnModifierParameters(1f, 0.5f, ticks, 1f)));
                var pipeline = new CombatPipeline(modifiers, new FixedRandom(), ids, Collector);
                using (AttackSnapshot attack = pipeline.BeginAttack(new Weapon()))
                {
                    RootId = attack.Context.RootEventId.Value;
                    if (admitRoot)
                        Assert.That(Gateway.Attacks.Admit(PlayerId, SourceId, WeaponId, 1, RootId),
                            Is.EqualTo(CombatRejectionReason.None));
                    pipeline.ResolveHitDetailed(attack, Target);
                }
                Assert.That(Target.CurrentHealth, Is.EqualTo(90));
                Assert.That(Target.StatusController.Has(EnemyStatusID.Burn), Is.True);
                if (drainInitial)
                {
                    Initial = Collector.Drain(1);
                    Assert.That(Initial.Results, Has.Length.EqualTo(1));
                    Assert.That(Initial.StatusMutations, Has.Length.EqualTo(1));
                    Assert.That(Initial.StatusMutations[0].TickDamage, Is.EqualTo(5));
                }
            }

            public ServerCombatGateway Gateway { get; }
            public ClientCombatCollector Collector { get; }
            public CombatantBehaviour Target { get; }
            public CombatSubmissionBatch Initial { get; }
            public ulong RootId { get; }
            public ulong Event(uint sequence) => CombatEventId.Compose(SourceSlot, Epoch, sequence).Value;

            public CanonicalWorldBatch ApplyInitial() => Gateway.ProcessBatch(PlayerId, Initial, 0d);
            public void Retire() => Assert.That(Gateway.Attacks.Retire(PlayerId, RootId), Is.True);

            public CombatResult[] NextTicks(float elapsed)
            {
                Target.AdvanceStatuses(elapsed);
                // Canonical status expiry/removal is independent of admitting damage outcomes.
                return Collector.Drain(++batchSequence).Results;
            }

            public void SubmitTicks(double time, params CombatResult[] results) =>
                Gateway.ProcessBatch(PlayerId, new CombatSubmissionBatch
                { BatchSequence = ++batchSequence, Results = results }, time);

            public CombatResult WithNewEvent(CombatResult result)
            {
                CombatEventId next = ids.Next();
                result.EventId = next.Value;
                result.Sequence = next.Sequence;
                return result;
            }

            public void AssertHealth(int expected)
            {
                Assert.That(Gateway.Ledger.TryGetState(EnemyId, out CanonicalEntityState state), Is.True);
                Assert.That(state.Health, Is.EqualTo(expected));
            }

            public void Dispose()
            {
                Collector.Dispose();
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        private sealed class FixedRandom : IRandomSource
        {
            public float Next01() => 0f;
        }

        private sealed class Weapon : IWeaponRuntime, ICombatContextSource
        {
            public uint CombatId => WeaponId;
            public uint SourcePlayerId => PlayerId;
            public uint SourceEntityId => SourceId;
            public WeaponBehaviourStats Stats { get; } = new WeaponBehaviourStats(new AttackStats
            { damage = 10, speed = 1, size = 1, duration = 1, projectileCount = 1, critMultiplier = 1 });
        }
    }
}
