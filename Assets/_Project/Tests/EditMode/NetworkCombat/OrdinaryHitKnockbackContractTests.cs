using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;
using DamageInfo = MonsterSupergroup.GAS.DamageInfo;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class OrdinaryHitKnockbackContractTests
    {
        private KnockbackSettings source;
        private EnemyKnockbackSettings preset;
        [SetUp]
        public void SetUp()
        {
            source = ScriptableObject.CreateInstance<KnockbackSettings>();
            source.distance = .2f; source.speedMultiplier = 10; source.staggerTime = 0;
            source.speedCurve = AnimationCurve.Linear(0, 0, 1, 1);
            preset = EnemyKnockbackSettings.From(source);
        }
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(source);
        private static ulong Id(uint sequence) => CombatEventId.Compose(5, 2, sequence).Value;
        private static OrdinaryHitKnockback Metadata() => new OrdinaryHitKnockback
        { Requested = true, AssignmentEpoch = 3, HitNetworkTime = 10, Origin = Vector2.left, MultiplierSum = .5f };
        private static CombatResult Hit(uint sequence = 3) => new CombatResult
        {
            EventId = Id(sequence), RootEventId = Id(1), ParentEventId = Id(sequence - 1), Sequence = sequence, ChainDepth = 2,
            SourcePlayerId = 7, SourceEntityId = 7, TargetEntityId = 42, AbilityId = 6, Damage = 12,
            Knockback = Metadata()
        };
        private ServerCombatGateway Gateway()
        {
            var gateway = new ServerCombatGateway();
            gateway.RegisterClientIdentity(7, 5, 2);
            gateway.Ledger.RegisterSource(7, 7);
            gateway.Ledger.RegisterEntity(42, 100, CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            gateway.Attacks.RegisterPlayer(7);
            Assert.That(gateway.Attacks.Admit(7, 7, 6, 1, Id(1), preset), Is.EqualTo(CombatRejectionReason.None));
            return gateway;
        }

        [Test]
        public void DamageAcceptanceNotifiesEachDistinctHitOnceAndKeepsNormalDamageRules()
        {
            var gateway = Gateway();
            var accepted = new List<ulong>();
            gateway.CombatResultAccepted += (result, applied, now) =>
            {
                Assert.That(gateway.ProcessedEvents.IsProcessed(result.EventId, now), Is.True);
                Assert.That(applied.Accepted, Is.True);
                accepted.Add(result.EventId);
            };
            gateway.ProcessBatch(7, new CombatSubmissionBatch { BatchSequence = 1, Results = new[] { Hit(), Hit(), Hit(5), Hit(7) } }, 10);
            Assert.That(accepted, Is.EqualTo(new[] { Id(3), Id(5), Id(7) }));
            Assert.That(gateway.Ledger.TryGetState(42, out var health), Is.True);
            Assert.That(health.Health, Is.EqualTo(64));
            gateway.Attacks.Retire(7, Id(1));
            gateway.ProcessBatch(7, new CombatSubmissionBatch { BatchSequence = 2, Results = new[] { Hit(9) } }, 10);
            Assert.That(accepted.Count, Is.EqualTo(3));
        }

        [Test]
        public void CollectorAttachesOnlyToTheMatchingPendingDamageAndDrainsItWithThatResult()
        {
            var ids = new SequentialCombatEventIdSource(5, 2);
            using var collector = new ClientCombatCollector(7, ids);
            var root = CombatContext.CreateRoot(ids.Next(), 7, 7, 6);
            var hit = root.CreateChild(ids.Next(), CombatTags.Hit, 42, 1);
            var damage = hit.CreateChild(ids.Next(), CombatTags.Damage, 42, 1);
            collector.Publish(new CombatEvent(CombatEventKind.DamageResolved, damage, new DamageInfo(6, 12, false), new DamageInfo(6, 12, false)));
            Assert.That(collector.TryAttachKnockback(damage.EventId.Value, 43, Metadata()), Is.False);
            Assert.That(collector.TryAttachKnockback(damage.EventId.Value, 42, Metadata()), Is.True);
            Assert.That(collector.TryAttachKnockback(damage.EventId.Value, 42, Metadata()), Is.False);
            var result = collector.Drain(1).Results[0];
            Assert.That(result.EventId, Is.EqualTo(damage.EventId.Value));
            Assert.That(result.Knockback.Requested, Is.True);
            Assert.That(result.Damage, Is.EqualTo(12));
            Assert.That(collector.TryAttachKnockback(damage.EventId.Value, 42, Metadata()), Is.False);
        }

        [Test]
        public void InvalidKnockbackMetadataDoesNotSuppressAnOtherwiseValidDamageResult()
        {
            var gateway = Gateway();
            var hit = Hit();
            hit.Knockback.Origin.x = float.NaN;
            Assert.That(hit.Knockback.IsValid, Is.False);
            gateway.ProcessBatch(7, new CombatSubmissionBatch { BatchSequence = 1, Results = new[] { hit } }, 10);
            Assert.That(gateway.Ledger.TryGetState(42, out var health), Is.True);
            Assert.That(health.Health, Is.EqualTo(88));
        }

        [Test]
        public void RootOwnsAnAuthoredPresetCopyUntilRetirementOrDisconnect()
        {
            var gateway = Gateway();
            source.distance = 99;
            source.speedCurve = AnimationCurve.Constant(0, 1, 0);
            Assert.That(gateway.Attacks.TryGetKnockback(7, Id(1), out var copy), Is.True);
            Assert.That(copy.Distance, Is.EqualTo(.2f));
            Assert.That(copy.CurveKeys[1].Value, Is.EqualTo(1));
            gateway.Attacks.Retire(7, Id(1));
            Assert.That(gateway.Attacks.TryGetKnockback(7, Id(1), out _), Is.False);
            gateway.Attacks.Admit(7, 7, 6, 2, Id(10), preset);
            gateway.UnregisterClientIdentity(7);
            Assert.That(gateway.Attacks.TryGetKnockback(7, Id(10), out _), Is.False);
        }

        [Test]
        public void MirrorRoundTripPreservesTheOptionalResultAndCommandCause()
        {
            var writer = new NetworkWriter();
            writer.Write(Hit());
            // Mirror's WeaverPriority integer writers use varints; the estimator is a nominal unpacked budget.
            var read = new NetworkReader(writer.ToArraySegment()).Read<CombatResult>();
            Assert.That(read.Knockback.IsValid, Is.True);
            Assert.That(read.Knockback.MultiplierSum, Is.EqualTo(.5f));
            var command = Command();
            writer.Reset(); writer.Write(command);
            var decoded = new NetworkReader(writer.ToArraySegment()).Read<EnemyKnockbackCommand>();
            Assert.That(decoded.IsValid, Is.True);
            Assert.That(decoded.Kind, Is.EqualTo(EnemyKnockbackKind.OrdinaryHit));
            Assert.That(decoded.DamageEventId, Is.EqualTo(Id(3)));
            Assert.That(decoded.MultiplierSum, Is.EqualTo(.5f));
            Assert.That(decoded.Settings.Distance, Is.EqualTo(.2f));
        }

        [TestCase("missing event")]
        [TestCase("wrong epoch")]
        [TestCase("wrong source")]
        [TestCase("root event")]
        [TestCase("ability")]
        [TestCase("unknown kind")]
        [TestCase("multiplier")]
        public void InvalidOrdinaryCauseCannotBecomeAMovementCommand(string defect)
        {
            var command = Command();
            switch (defect)
            {
                case "missing event": command.DamageEventId = 0; break;
                case "wrong epoch": command.DamageEventId = CombatEventId.Compose(5, 3, 3).Value; break;
                case "wrong source": command.DamageEventId = CombatEventId.Compose(6, 2, 3).Value; break;
                case "root event": command.DamageEventId = Id(1); break;
                case "ability": command.AbilityCombatId = 0x80000000u; break;
                case "unknown kind": command.Kind = (EnemyKnockbackKind)255; break;
                case "multiplier": command.MultiplierSum = float.NaN; break;
            }
            Assert.That(command.IsValid, Is.False);
        }
        [TestCase(7.99, false)] [TestCase(8, true)] [TestCase(10.1, true)] [TestCase(10.11, false)]
        public void HitAgeRemainsBoundedAcrossBatching(double hitTime, bool valid)
        {
            var metadata = Metadata(); metadata.HitNetworkTime = hitTime;
            Assert.That(metadata.IsTimely(10), Is.EqualTo(valid));
        }
        private EnemyKnockbackCommand Command() => new EnemyKnockbackCommand
        {
            Kind = EnemyKnockbackKind.OrdinaryHit, DamageEventId = Id(3), RootEventId = Id(1), MultiplierSum = .5f,
            EnemyEntityId = 42, AssignmentEpoch = 3, SourcePlayerId = 7, AbilityCombatId = 6, CommandId = 1,
            IssuedAt = 10, Origin = Vector2.left, Settings = preset
        };
    }
}
