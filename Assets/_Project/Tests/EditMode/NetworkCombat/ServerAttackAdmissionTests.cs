using MonsterSupergroup.GAS;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class ServerAttackAdmissionTests
    {
        private const uint PlayerId = 1;
        private const uint SourceId = 10;
        private const uint EnemyId = 100;
        private const uint WeaponId = 2;
        private const ushort SourceSlot = 7;
        private const ushort Epoch = 3;

        [Test]
        public void RegisteredPlayer_CannotDamageWithoutAdmittedRoot()
        {
            var gateway = CreateGateway();
            Submit(gateway, Hit());

            AssertRejected(gateway, CombatRejectionReason.InvalidAttackRoot);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AdmittedRoot_CannotBeUsedByDifferentWeaponOrSource(bool changeWeapon)
        {
            var gateway = CreateGateway();
            Admit(gateway);
            CombatResult hit = Hit();
            if (changeWeapon) hit.AbilityId = WeaponId + 1;
            else hit.SourceEntityId = SourceId + 1;
            // Even another source owned by the same player needs its own admission.
            gateway.Ledger.RegisterSource(SourceId + 1, PlayerId);
            Submit(gateway, hit);

            AssertRejected(gateway, CombatRejectionReason.InvalidAttackRoot);
        }

        [Test]
        public void Admission_DoesNotBypassLedgerSourceOwnership()
        {
            var gateway = CreateGateway();
            gateway.Ledger.RegisterSource(SourceId, PlayerId + 1);
            Admit(gateway);
            Submit(gateway, Hit());

            AssertRejected(gateway, CombatRejectionReason.SourceNotOwned);
        }

        [Test]
        public void AnotherSender_CannotUsePlayersAdmittedRoot()
        {
            var gateway = CreateGateway();
            Admit(gateway);
            gateway.ProcessBatch(PlayerId + 1, new CombatSubmissionBatch
            {
                BatchSequence = 1, Results = new[] { Hit() }
            }, 0);

            AssertRejected(gateway, CombatRejectionReason.InvalidSender);
        }

        [TestCase("missing parent")]
        [TestCase("parent before root")]
        [TestCase("parent equals root")]
        [TestCase("parent equals result")]
        [TestCase("parent after result")]
        [TestCase("parent foreign source")]
        [TestCase("parent old epoch")]
        [TestCase("insufficient depth")]
        public void MalformedLineage_CannotDamage(string defect)
        {
            var gateway = CreateGateway();
            Admit(gateway, rootSequence: 10);
            CombatResult hit = Hit(rootSequence: 10, parentSequence: 11, resultSequence: 12);
            switch (defect)
            {
                case "missing parent": hit.ParentEventId = 0; break;
                case "parent before root": hit.ParentEventId = Event(9); break;
                case "parent equals root": hit.ParentEventId = hit.RootEventId; break;
                case "parent equals result": hit.ParentEventId = hit.EventId; break;
                case "parent after result": hit.ParentEventId = Event(13); break;
                case "parent foreign source":
                    hit.ParentEventId = CombatEventId.Compose(SourceSlot + 1, Epoch, 11).Value;
                    break;
                case "parent old epoch": hit.ParentEventId = Event(11, Epoch - 1); break;
                case "insufficient depth": hit.ChainDepth = 1; break;
            }
            Submit(gateway, hit);

            AssertRejected(gateway, CombatRejectionReason.InvalidAttackRoot);
        }

        [Test]
        public void CurrentEpochResult_CannotClaimAnOldEpochRoot()
        {
            var gateway = CreateGateway();
            Admit(gateway);
            CombatResult hit = Hit();
            hit.RootEventId = Event(1, Epoch - 1);
            Submit(gateway, hit);

            AssertRejected(gateway, CombatRejectionReason.InvalidAttackRoot);
        }

        [Test]
        public void OldEpochResult_IsRejectedEvenIfRootWasPreviouslyAdmitted()
        {
            var gateway = CreateGateway();
            Admit(gateway);
            gateway.RegisterClientIdentity(PlayerId, SourceSlot, Epoch + 1);
            Submit(gateway, Hit());

            AssertRejected(gateway, CombatRejectionReason.InvalidSequence);
        }

        [Test]
        public void OneRoot_AllowsMultipleHitsAndDerivedResultsWithDistinctEventIds()
        {
            var gateway = CreateGateway();
            Admit(gateway);
            CombatResult first = Hit();
            CombatResult second = Hit(parentSequence: 4, resultSequence: 5);
            CombatResult derived = Hit(parentSequence: 6, resultSequence: 7);
            derived.ChainDepth = 4;
            derived.BuildId = 12345;
            derived.DamageTags |= (ulong)CombatTags.Explosion;
            gateway.ProcessBatch(PlayerId, new CombatSubmissionBatch
            {
                BatchSequence = 1, Results = new[] { first, second, derived }
            }, 0);

            AssertHealth(gateway, 70);
            Assert.That(gateway.Metrics.AcceptedCombatResults, Is.EqualTo(3));
            Assert.That(gateway.Attacks.ActiveCount(PlayerId), Is.EqualTo(1));
        }

        [Test]
        public void DuplicateResult_DamagesOnlyOnceWithoutConsumingOtherHits()
        {
            var gateway = CreateGateway();
            Admit(gateway);
            CombatResult hit = Hit();
            gateway.ProcessBatch(PlayerId, new CombatSubmissionBatch
            {
                BatchSequence = 1, Results = new[] { hit, hit }
            }, 0);
            gateway.ProcessBatch(PlayerId, new CombatSubmissionBatch
            {
                BatchSequence = 2,
                Results = new[] { hit, Hit(parentSequence: 4, resultSequence: 5) }
            }, 0.1);

            AssertHealth(gateway, 80);
            Assert.That(gateway.Metrics.AcceptedCombatResults, Is.EqualTo(2));
            Assert.That(gateway.Metrics.GetRejected(CombatRejectionReason.DuplicateEvent), Is.EqualTo(2));
        }

        [TestCase(0u)]
        [TestCase(12345u)]
        public void GasBuildId_IsModifierIdentityRatherThanAdmissionRevision(uint modifierId)
        {
            var gateway = CreateGateway();
            Admit(gateway, buildRevision: 9);
            CombatResult hit = Hit();
            hit.BuildId = modifierId;
            Submit(gateway, hit);

            AssertHealth(gateway, 90);
            Assert.That(gateway.Metrics.AcceptedCombatResults, Is.EqualTo(1));
        }

        [Test]
        public void NewBuildAdmission_PreservesInflightRootUntilExplicitRetirement()
        {
            var gateway = CreateGateway();
            Admit(gateway, buildRevision: 1);
            Admit(gateway, rootSequence: 10, buildRevision: 2);
            gateway.ProcessBatch(PlayerId, new CombatSubmissionBatch
            {
                BatchSequence = 1,
                Results = new[]
                {
                    Hit(parentSequence: 11, resultSequence: 12),
                    Hit(rootSequence: 10, parentSequence: 13, resultSequence: 14)
                }
            }, 0);
            AssertHealth(gateway, 80);
            Assert.That(gateway.Attacks.Retire(PlayerId, Event(1)), Is.True);
            Assert.That(gateway.Attacks.Contains(PlayerId, Event(10), WeaponId), Is.True);
            gateway.ProcessBatch(PlayerId, new CombatSubmissionBatch
            {
                BatchSequence = 2,
                Results = new[] { Hit(parentSequence: 15, resultSequence: 16) }
            }, 1);

            AssertHealth(gateway, 80);
            Assert.That(gateway.Metrics.AcceptedCombatResults, Is.EqualTo(2));
            Assert.That(gateway.Metrics.GetRejected(CombatRejectionReason.InvalidAttackRoot), Is.EqualTo(1));
        }

        [Test]
        public void RetiredRoot_CannotBeReadmittedByReplayingItsSequence()
        {
            var registry = new ServerAttackRegistry();
            registry.RegisterPlayer(PlayerId);
            Assert.That(registry.Admit(PlayerId, SourceId, WeaponId, 1, Event(10)),
                Is.EqualTo(CombatRejectionReason.None));
            Assert.That(registry.Retire(PlayerId, Event(10)), Is.True);
            Assert.That(registry.Retire(PlayerId, Event(10)), Is.False);
            Assert.That(registry.Admit(PlayerId, SourceId, WeaponId, 1, Event(10)),
                Is.EqualTo(CombatRejectionReason.InvalidSequence));
            Assert.That(registry.Admit(PlayerId, SourceId, WeaponId, 1, Event(9)),
                Is.EqualTo(CombatRejectionReason.InvalidSequence));
            Assert.That(registry.ActiveCount(PlayerId), Is.Zero);
        }

        [Test]
        public void UnregisterIdentity_ClearsRootsAndAllowsNewEpochToStartFresh()
        {
            var gateway = CreateGateway();
            Admit(gateway, rootSequence: 100);
            // Repeated component registration does not reset an active player's roots.
            gateway.Attacks.RegisterPlayer(PlayerId);
            Assert.That(gateway.Attacks.ActiveCount(PlayerId), Is.EqualTo(1));
            gateway.UnregisterClientIdentity(PlayerId);
            Assert.That(gateway.Attacks.RequiresAdmission(PlayerId), Is.False);
            Assert.That(gateway.Attacks.ActiveCount(PlayerId), Is.Zero);

            gateway.RegisterClientIdentity(PlayerId, SourceSlot, Epoch + 1);
            gateway.Attacks.RegisterPlayer(PlayerId);
            Assert.That(gateway.Attacks.Admit(PlayerId, SourceId, WeaponId, 1, Event(1, Epoch + 1)),
                Is.EqualTo(CombatRejectionReason.None));
            Assert.That(gateway.Attacks.Contains(PlayerId, Event(100), WeaponId), Is.False);
            CombatResult newHit = Hit(epoch: Epoch + 1);
            Submit(gateway, newHit);

            AssertHealth(gateway, 90);
            Assert.That(gateway.Metrics.AcceptedCombatResults, Is.EqualTo(1));
        }

        [Test]
        public void ActiveCapacity_IsPerPlayerAndRetirementReleasesCapacity()
        {
            var registry = new ServerAttackRegistry(maximumActiveRootsPerPlayer: 1);
            registry.RegisterPlayer(PlayerId);
            registry.RegisterPlayer(PlayerId + 1);
            Assert.That(registry.Admit(PlayerId, SourceId, WeaponId, 1, Event(1)),
                Is.EqualTo(CombatRejectionReason.None));
            Assert.That(registry.Admit(PlayerId, SourceId, WeaponId, 1, Event(4)),
                Is.EqualTo(CombatRejectionReason.AttackCapacityExceeded));
            Assert.That(registry.ActiveCount(PlayerId), Is.EqualTo(1));
            Assert.That(registry.Admit(PlayerId + 1, SourceId + 1, WeaponId, 1,
                CombatEventId.Compose(SourceSlot + 1, Epoch, 1).Value),
                Is.EqualTo(CombatRejectionReason.None));
            registry.Retire(PlayerId, Event(1));
            Assert.That(registry.Admit(PlayerId, SourceId, WeaponId, 1, Event(4)),
                Is.EqualTo(CombatRejectionReason.None), "A capacity rejection must not consume the root sequence.");
            registry.UnregisterPlayer(PlayerId);
            Assert.That(registry.ActiveCount(PlayerId), Is.Zero);
            Assert.That(registry.ActiveCount(PlayerId + 1), Is.EqualTo(1));
        }

        private static ServerCombatGateway CreateGateway()
        {
            var gateway = new ServerCombatGateway();
            gateway.RegisterClientIdentity(PlayerId, SourceSlot, Epoch);
            gateway.Attacks.RegisterPlayer(PlayerId);
            gateway.Ledger.RegisterSource(SourceId, PlayerId);
            gateway.Ledger.RegisterEntity(EnemyId, 100, CombatEntityKind.Enemy,
                CombatEntityAuthority.ServerCanonical);
            return gateway;
        }

        private static void Admit(ServerCombatGateway gateway, uint rootSequence = 1,
            uint buildRevision = 1)
        {
            Assert.That(gateway.Attacks.Admit(PlayerId, SourceId, WeaponId, buildRevision,
                Event(rootSequence)), Is.EqualTo(CombatRejectionReason.None));
        }

        private static CombatResult Hit(uint rootSequence = 1, uint parentSequence = 2,
            uint resultSequence = 3, ushort epoch = Epoch) => new CombatResult
        {
            EventId = Event(resultSequence, epoch),
            RootEventId = Event(rootSequence, epoch),
            ParentEventId = Event(parentSequence, epoch),
            Sequence = resultSequence,
            ChainDepth = 2,
            SourcePlayerId = PlayerId,
            SourceEntityId = SourceId,
            TargetEntityId = EnemyId,
            AbilityId = WeaponId,
            Damage = 10,
            DamageTags = (ulong)(CombatTags.Attack | CombatTags.Projectile | CombatTags.Hit | CombatTags.Damage),
            TargetStateVersion = 1
        };

        private static ulong Event(uint sequence, ushort epoch = Epoch) =>
            CombatEventId.Compose(SourceSlot, epoch, sequence).Value;

        private static void Submit(ServerCombatGateway gateway, CombatResult hit) =>
            gateway.ProcessBatch(PlayerId, new CombatSubmissionBatch
            {
                BatchSequence = 1, Results = new[] { hit }
            }, 0);

        private static void AssertRejected(ServerCombatGateway gateway, CombatRejectionReason reason)
        {
            AssertHealth(gateway, 100);
            Assert.That(gateway.Metrics.AcceptedCombatResults, Is.Zero);
            Assert.That(gateway.Metrics.GetRejected(reason), Is.EqualTo(1));
        }

        private static void AssertHealth(ServerCombatGateway gateway, int expected)
        {
            Assert.That(gateway.Ledger.TryGetState(EnemyId, out CanonicalEntityState enemy), Is.True);
            Assert.That(enemy.Health, Is.EqualTo(expected));
        }
    }
}
