using System;
using System.Linq;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class SummonPresentationContractTests
    {
        private const uint Source = 17, Weapon = 402;
        private const ulong Pet = 0x0011000300000001UL, OtherPet = 0x0011000300000002UL;
        private const ulong Root = 0x0011000300000010UL;

        [Test]
        public void CanApplyStateDoesNotReserveIdentitySlotOrConsumeAnExistingPhase()
        {
            var history = new SummonPresentationHistory();
            Assert.That(history.CanApplyState(State()), Is.True);
            Assert.That(history.CanApplyState(State()), Is.True);
            Assert.That(history.PetCount, Is.Zero);
            Assert.That(history.CaptureStates(), Is.Empty);
            Assert.That(history.TryApplyState(State()), Is.True);
            var next = State(Pet, 0, 2);
            Assert.That(history.CanApplyState(next), Is.True);
            Assert.That(history.CanApplyState(next), Is.True);
            Assert.That(history.TryGetState(Pet, out var current), Is.True);
            Assert.That(current.State.PhaseSequence, Is.EqualTo(1));
            Assert.That(history.TryApplyState(next), Is.True);
            Assert.That(history.CanApplyState(next), Is.False);
        }

        [Test]
        public void SameDefinitionInTwoSlotsHasIndependentPetsAndPoseStreams()
        {
            var history = new SummonPresentationHistory();
            Assert.That(history.TryApplyState(State()), Is.True);
            Assert.That(history.TryApplyState(State(OtherPet, 1)), Is.True);
            Assert.That(history.PetCount, Is.EqualTo(2));
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 1, 11d, new Vector3(1, 2, 3))), Is.True);
            Assert.That(history.TryApplyPose(Pose(OtherPet, 1, 1, 11d, new Vector3(4, 5, 6))), Is.True);
            Assert.That(history.TryGetState(Pet, out var first), Is.True);
            Assert.That(history.TryGetState(OtherPet, out var second), Is.True);
            Assert.That(first.State.Pose.Position, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(second.State.Pose.Position, Is.EqualTo(new Vector3(4, 5, 6)));
            Assert.That(history.TryTerminate(End(Pet)), Is.True);
            Assert.That(history.PetCount, Is.EqualTo(1));
            Assert.That(history.TryApplyPose(Pose(OtherPet, 1, 2, 12d)), Is.True);
        }

        [Test]
        public void LiveSlotAndPetIdentityCannotBeReusedByAnotherSlotWeaponOrOwner()
        {
            var history = new SummonPresentationHistory();
            Assert.That(history.TryApplyState(State()), Is.True);
            Assert.That(history.TryApplyState(State(OtherPet)), Is.False);
            Assert.That(history.TryApplyState(State(Pet, 1, 2)), Is.False);
            var wrong = State(Pet, 0, 2); wrong.State.WeaponId++;
            Assert.That(history.TryApplyState(wrong), Is.False);
            wrong = State(Pet, 0, 2); wrong.SourcePlayerId++;
            Assert.That(history.TryApplyState(wrong), Is.False);
            Assert.That(history.TryApplyState(State(Pet, 0, 2)), Is.True);
            Assert.That(history.PetCount, Is.EqualTo(1));
        }

        [Test]
        public void TerminatedPetCannotReplayIntoAnotherSlotButIndependentPetOrderIsLegal()
        {
            var history = new SummonPresentationHistory();
            Assert.That(history.TryApplyState(State()), Is.True);
            Assert.That(history.TryTerminate(End()), Is.True);
            Assert.That(history.TryApplyState(State()), Is.False);
            Assert.That(history.TryApplyState(State(Pet, 1)), Is.False,
                "A retired lifecycle identity cannot become a different slot's new pet.");
            Assert.That(history.TryApplyState(State(OtherPet, 1)), Is.True);

            history.Clear();
            Assert.That(history.TryApplyState(State(OtherPet, 1)), Is.True);
            Assert.That(history.TryApplyState(State(Pet, 0)), Is.True,
                "Independent slots may arrive out of global sequence order.");
        }

        [Test]
        public void InvalidNewPetDoesNotReserveSlotOrConsumeItsSequence()
        {
            var history = new SummonPresentationHistory();
            var invalid = State();
            invalid.State.Stats.Duration = float.NaN;
            Assert.That(history.TryApplyState(invalid), Is.False);
            Assert.That(history.TryApplyState(State()), Is.True);
            var second = State(OtherPet, 1); second.State.PetId &= 0xffffffff00000000UL;
            Assert.That(history.TryApplyState(second), Is.False, "Sequence zero is not a valid lifecycle.");
            Assert.That(history.TryApplyState(State(OtherPet, 1)), Is.True);
        }

        [Test]
        public void NewPhaseRejectsDuplicatesOlderSequencesAndEarlierPhaseTime()
        {
            var history = new SummonPresentationHistory();
            var first = State(Pet, 0, 10);
            Assert.That(history.TryApplyState(first), Is.True);
            Assert.That(history.TryApplyState(first), Is.False);
            Assert.That(history.TryApplyState(State(Pet, 0, 9)), Is.False);
            var olderTime = State(Pet, 0, 11); olderTime.EventNetworkTime = 9d;
            Assert.That(history.TryApplyState(olderTime), Is.False);
            var valid = State(Pet, 0, 11); valid.EventNetworkTime = 11d;
            Assert.That(history.TryApplyState(valid), Is.True);
            Assert.That(history.TryGetState(Pet, out var latest), Is.True);
            Assert.That(latest.State.PhaseSequence, Is.EqualTo(11));
        }

        [Test]
        public void SequenceWrapKeepsPhaseAndPoseMonotonic()
        {
            var history = new SummonPresentationHistory();
            Assert.That(history.TryApplyState(State(Pet, 0, uint.MaxValue)), Is.True);
            Assert.That(history.TryApplyState(State(Pet, 0, 1)), Is.True);
            Assert.That(history.TryApplyState(State(Pet, 0, uint.MaxValue)), Is.False);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 1, 11d)), Is.True);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 0x7fffffff, 12d)), Is.True);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 0xfffffffe, 13d)), Is.True);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, uint.MaxValue, 14d)), Is.True);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 1, 15d)), Is.True);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, uint.MaxValue, 16d)), Is.False);
        }

        [Test]
        public void PoseRequiresCurrentPhaseOwnerWeaponAndIncreasingTimestamp()
        {
            var history = new SummonPresentationHistory();
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 1, 11d)), Is.False);
            Assert.That(history.TryApplyState(State()), Is.True);
            var sample = Pose(Pet, 1, 1, 11d);
            var wrong = sample; wrong.SourcePlayerId++;
            Assert.That(history.TryApplyPose(wrong), Is.False);
            wrong = sample; wrong.Sample.WeaponId++;
            Assert.That(history.TryApplyPose(wrong), Is.False);
            wrong = sample; wrong.Sample.PhaseSequence++;
            Assert.That(history.TryApplyPose(wrong), Is.False);
            wrong = sample; wrong.Sample.PetId++;
            Assert.That(history.TryApplyPose(wrong), Is.False);
            Assert.That(history.TryApplyPose(sample), Is.True);
            Assert.That(history.TryApplyPose(sample), Is.False);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 2, 10.5d)), Is.False);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 2, 11d)), Is.True);
            Assert.That(history.TryApplyState(State(Pet, 0, 2, 12d)), Is.True);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 99, 13d)), Is.False);
            Assert.That(history.TryApplyPose(Pose(Pet, 2, 1, 13d)), Is.True);
        }

        [Test]
        public void CaptureUpdatesPositionButKeepsPhaseTimeAnchorAndReturnsIndependentCopies()
        {
            var history = new SummonPresentationHistory();
            var start = State(); start.State.Phase = SummonPhase.Birth;
            start.State.PhaseElapsedSeconds = 0.5f;
            Assert.That(history.TryApplyState(start), Is.True);
            Vector3 latestPosition = new Vector3(4f, 5f, 6f);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 1, 12d, latestPosition)), Is.True);
            var captured = history.CaptureStates();
            Assert.That(captured.Length, Is.EqualTo(1));
            Assert.That(captured[0].State.Pose.Position, Is.EqualTo(latestPosition));
            Assert.That(captured[0].EventNetworkTime, Is.EqualTo(10d),
                "A spectator must compute Birth age from the phase edge, not the last pose sample.");
            Assert.That(captured[0].State.PhaseElapsedSeconds, Is.EqualTo(0.5f));
            captured[0].State.Pose.Position = Vector3.zero;
            captured[0].State.PhaseSequence = 999;
            Assert.That(history.TryGetState(Pet, out var untouched), Is.True);
            Assert.That(untouched.State.Pose.Position, Is.EqualTo(latestPosition));
            Assert.That(untouched.State.PhaseSequence, Is.EqualTo(1));
        }

        [Test]
        public void SameAttackRootKeepsItsFrozenStatsAndElementAcrossPhases()
        {
            var history = new SummonPresentationHistory();
            var entering = State();
            entering.State.Phase = SummonPhase.AttackEnter;
            entering.State.AttackEventId = Root;
            Assert.That(history.TryApplyState(entering), Is.True);
            var main = entering;
            main.State.PhaseSequence = 2;
            main.State.Phase = SummonPhase.AttackMain;
            main.EventNetworkTime++;
            var changed = main; changed.State.Stats.Duration += 5f;
            Assert.That(history.TryApplyState(changed), Is.False);
            changed = main; changed.State.Element = AttackElement.Fire;
            Assert.That(history.TryApplyState(changed), Is.False);
            Assert.That(history.TryApplyState(main), Is.True, "Rejected edits cannot consume the phase ordinal.");
            var exit = main;
            exit.State.PhaseSequence = 3;
            exit.State.Phase = SummonPhase.AttackExit;
            exit.EventNetworkTime++;
            Assert.That(history.TryApplyState(exit), Is.True);
            var idle = exit;
            idle.State.PhaseSequence = 4;
            idle.State.Phase = SummonPhase.Positioning;
            idle.State.AttackEventId = 0;
            idle.State.Stats.Duration += 5f;
            idle.EventNetworkTime++;
            Assert.That(history.TryApplyState(idle), Is.True, "Idle preview may reflect the new Build after the old root ends.");
        }

        [Test]
        public void TerminationIsIdempotentAndCannotAffectAnotherOwnerWeaponOrPet()
        {
            var history = new SummonPresentationHistory();
            Assert.That(history.TryTerminate(End()), Is.False);
            Assert.That(history.TryApplyState(State()), Is.True);
            var wrong = End(); wrong.SourcePlayerId++;
            Assert.That(history.TryTerminate(wrong), Is.False);
            wrong = End(); wrong.WeaponId++;
            Assert.That(history.TryTerminate(wrong), Is.False);
            Assert.That(history.TryTerminate(End(OtherPet)), Is.False);
            Assert.That(history.TryTerminate(End()), Is.True);
            Assert.That(history.TryTerminate(End()), Is.False);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 1, 11d)), Is.False);
            Assert.That(history.CaptureStates(), Is.Empty);
        }

        [Test]
        public void DisconnectClearRemovesViewsAndResetsTheOldEpochTombstones()
        {
            var history = new SummonPresentationHistory();
            Assert.That(history.TryApplyState(State()), Is.True);
            Assert.That(history.TryApplyState(State(OtherPet, 1)), Is.True);
            history.Clear();
            Assert.That(history.PetCount, Is.Zero);
            Assert.That(history.CaptureStates(), Is.Empty);
            Assert.That(history.TryGetState(Pet, out _), Is.False);
            Assert.That(history.TryApplyPose(Pose(Pet, 1, 1, 11d)), Is.False);
            Assert.That(history.TryApplyState(State()), Is.True,
                "The adapter rejects obsolete epochs; a cleared history starts a new session scope.");
        }

        [Test]
        public void ContractsRejectMalformedGeometryTimesAndContradictoryAttackIdentity()
        {
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var state = State(); state.State.PhaseElapsedSeconds = invalid;
                Assert.That(state.IsValid, Is.False);
                state = State(); state.State.Pose.Position.x = invalid;
                Assert.That(state.IsValid, Is.False);
                state = State(); state.State.Pose.RotationPivotEuler.y = invalid;
                Assert.That(state.IsValid, Is.False);
                state = State(); state.State.Pose.IsoLocalPosition.z = invalid;
                Assert.That(state.IsValid, Is.False);
                state = State(); state.State.Stats.SizeMultiplierSum = invalid;
                Assert.That(state.IsValid, Is.False);
                var pose = Pose(Pet, 1, 1, 11d); pose.Sample.Pose.MoveAnimationSpeed = invalid;
                Assert.That(pose.IsValid, Is.False);
            }
            foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var state = State(); state.EventNetworkTime = invalid;
                Assert.That(state.IsValid, Is.False);
                var pose = Pose(Pet, 1, 1, invalid);
                Assert.That(pose.IsValid, Is.False);
                var end = End(); end.EventNetworkTime = invalid;
                Assert.That(end.IsValid, Is.False);
            }
            var edge = State(); edge.State.AttackEventId = Root;
            Assert.That(edge.IsValid, Is.False);
            edge = State(); edge.State.Phase = SummonPhase.AttackEnter;
            Assert.That(edge.IsValid, Is.False);
            edge.State.AttackEventId = Root;
            Assert.That(edge.IsValid, Is.True);
            edge.State.Phase = (SummonPhase)255;
            Assert.That(edge.IsValid, Is.False);
            edge = State(); edge.State.Element = (AttackElement)(-1);
            Assert.That(edge.IsValid, Is.False);
            edge = State(); edge.SlotIndex = 4;
            Assert.That(edge.IsValid, Is.False);
            edge = State(); edge.SlotIndex = -1;
            Assert.That(edge.IsValid, Is.False);
        }

        [Test]
        public void MirrorRoundTripPreservesNestedPhaseStatsPoseAndTerminationIdentity()
        {
            NetworkSummonPresentationState state = State();
            state.State.Phase = SummonPhase.AttackMain;
            state.State.AttackEventId = Root;
            state.State.PhaseElapsedSeconds = 0.45f;
            state.State.Element = AttackElement.Poison;
            state.State.Stats.DurationMultiplierSum = 1.3f;
            var pose = Pose(Pet, 1, 34, 11.75d, new Vector3(2f, 7f, -0.3f));
            pose.Sample.Pose.RotationPivotEuler = new Vector3(-45f, 123f, 0f);
            pose.Sample.Pose.IsoLocalPosition = new Vector3(0.2f, 4.5f, 0f);
            var end = End();
            var writer = new NetworkWriter();
            writer.Write(state);
            writer.Write(pose);
            writer.Write(end);
            var reader = new NetworkReader(writer.ToArraySegment());
            var copiedState = reader.Read<NetworkSummonPresentationState>();
            var copiedPose = reader.Read<NetworkSummonPresentationPose>();
            var copiedEnd = reader.Read<NetworkSummonPresentationTermination>();
            Assert.That(copiedState, Is.EqualTo(state));
            Assert.That(copiedPose, Is.EqualTo(pose));
            Assert.That(copiedEnd, Is.EqualTo(end));
            Assert.That(copiedEnd.EventNetworkTime, Is.EqualTo(12.25d));
            Assert.That(copiedState.IsValid && copiedPose.IsValid && copiedEnd.IsValid, Is.True);
            Assert.That(reader.Remaining, Is.Zero);
        }

        private static NetworkSummonPresentationState State(ulong pet = Pet, int slot = 0, uint phase = 1, double time = 10d) =>
            new NetworkSummonPresentationState
            {
                SourcePlayerId = Source, SlotIndex = slot, EventNetworkTime = time,
                State = new SummonPresentationState
                {
                    WeaponId = Weapon, PetId = pet, PhaseSequence = phase, Phase = SummonPhase.Cocoon,
                    Stats = new ProjectilePresentationStats { Duration = 1f, ProjectileCount = 1, BaseProjectileCount = 1 },
                    Pose = new SummonPose { Position = new Vector3(1f, 2f, 0f), MoveAnimationSpeed = 1f }
                }
            };

        private static NetworkSummonPresentationPose Pose(ulong pet, uint phase, uint sequence, double time, Vector3 position = default) =>
            new NetworkSummonPresentationPose
            {
                SourcePlayerId = Source, EventNetworkTime = time,
                Sample = new SummonPresentationPose
                {
                    WeaponId = Weapon, PetId = pet, PhaseSequence = phase, PoseSequence = sequence,
                    Pose = new SummonPose { Position = position, MoveAnimationSpeed = 1f }
                }
            };

        private static NetworkSummonPresentationTermination End(ulong pet = Pet) =>
            new NetworkSummonPresentationTermination { SourcePlayerId = Source, WeaponId = Weapon, PetId = pet, EventNetworkTime = 12.25d };
    }

    public sealed class PlayerSummonMaturitySnapshotTests
    {
        [Test]
        public void ValidDeadlineAllowsIndependentSlotsAndAlreadyMatureOfflineState()
        {
            foreach (int slot in Enumerable.Range(0, 4))
            {
                var snapshot = Snapshot(slot, 0d);
                Assert.That(snapshot.IsValid, Is.True);
                snapshot.MaturityAt = 1234.56789d;
                Assert.That(snapshot.IsValid, Is.True);
            }
            Assert.That(Snapshot(-1, 1d).IsValid, Is.False);
            Assert.That(Snapshot(4, 1d).IsValid, Is.False);
            var invalid = Snapshot(0, 1d); invalid.WeaponId = 0;
            Assert.That(invalid.IsValid, Is.False);
            foreach (double invalidTime in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.That(Snapshot(0, invalidTime).IsValid, Is.False);
        }

        [Test]
        public void MirrorPreservesAbsoluteMaturityDeadlineAndSlotWithoutPetOrAttackRuntime()
        {
            var snapshots = new[] { Snapshot(0, 0d), Snapshot(3, 123456.789012345d) };
            var writer = new NetworkWriter();
            writer.Write(snapshots);
            var reader = new NetworkReader(writer.ToArraySegment());
            var copy = reader.Read<PlayerSummonMaturitySnapshot[]>();
            Assert.That(copy, Is.EqualTo(snapshots));
            Assert.That(reader.Remaining, Is.Zero);
            copy[0].MaturityAt = 500d;
            Assert.That(snapshots[0].MaturityAt, Is.Zero);
            var fields = typeof(PlayerSummonMaturitySnapshot).GetFields();
            Assert.That(fields.Select(field => field.Name), Is.EquivalentTo(new[] { "SlotIndex", "WeaponId", "MaturityAt" }));
        }

        private static PlayerSummonMaturitySnapshot Snapshot(int slot, double time) =>
            new PlayerSummonMaturitySnapshot { SlotIndex = slot, WeaponId = 402, MaturityAt = time };
    }
}
