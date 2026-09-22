using AstralShift.HellMaiden.AI;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationAgent
    {
        private uint appliedHandoffEpoch;
        private bool restoringHandoff;
        private EnemySimulationSnapshot pendingFutureSnapshot;
        private bool hasPendingFutureSnapshot;
        public EnemySimulationHandoff Handoff => handoff;
        public uint AppliedHandoffEpoch => appliedHandoffEpoch;
        public float LastHandoffCorrection { get; private set; }
        public int AcceptedRemoteSnapshotCount { get; private set; }
        public ulong CurrentActionId => authority != null && authority.RunsNavigation && enemyController != null
            ? enemyController.CaptureSimulationAction(EnemySimulationClock.CombatNow).ActionId
            : hasLatestAttackPresentation ? latestAttackPresentation.Checkpoint.Movement.Runtime.Action.ActionId
            : handoff.Checkpoint.Movement.Runtime.Action.ActionId;

        [Server]
        public void SetServerHandoff(EnemySimulationHandoff value)
        {
            handoff = value;
            ApplyHandoff(value);
        }

        private void HandleHandoffChanged(EnemySimulationHandoff previous, EnemySimulationHandoff current) => ApplyHandoff(current);

        private void ApplyHandoff(EnemySimulationHandoff current)
        {
            if (current.Assignment.Epoch == 0 || current.Assignment.EnemyEntityId != netId ||
                (appliedHandoffEpoch != 0 && !EnemySimulationSequence.IsNewer(current.Assignment.Epoch, appliedHandoffEpoch))) return;
            if (MonsterSupergroup.GAS.CombatEvidence.Enabled) MonsterSupergroup.GAS.CombatEvidence.Event(isServer ? "Server" : "Replica", "authority.handoff", "Applying", current.Reason.ToString(),
                target: netId, input: current, before: new { assignment, position = transform.position, appliedHandoffEpoch });
            Vector2 previousPosition = transform.position;
            bool previouslyLocal = authority != null && authority.RunsNavigation;
            bool keepServerAction = current.Reason != EnemyTargetChangeReason.ReferenceReposition && isServer && appliedHandoffEpoch != 0 &&
                assignment.Host == EnemySimulationHost.ServerAuthoritative && current.Assignment.Host == EnemySimulationHost.ServerAuthoritative;
            assignment = current.Assignment;
            if (targetState.Revision != 0) assignment.AggroTargetPlayerId = targetState.AggroPlayerId;
            resolvedTarget = ResolveSimulationTarget();
            restoringHandoff = true;
            if (!keepServerAction)
            {
                enemyController?.SuspendSimulationExecution();
                ReleaseNetworkKnockbackPreset();
                authority.ApplyRole(EnemySimulationRole.Frozen, 0, assignment.AggroTargetPlayerId, assignment.Epoch);
            }
            TryInitializeProductEnemy();
            if (assignment.Host != EnemySimulationHost.Frozen && (resolvedTarget == null || !ProductEnemyInitialized))
            { restoringHandoff = false; return; }
            snapshotSequence = 0; sequenceEpoch = assignment.Epoch;
            ResetAttackPresentationForAssignment(assignment.Epoch);
            if (!keepServerAction) interpolator.ClearSnapshots();
            else if (activeKnockbackEpoch != 0) activeKnockbackEpoch = assignment.Epoch;
            var pose = current.Checkpoint.Movement;
            if (!keepServerAction)
            {
                LastHandoffCorrection = Vector2.Distance(transform.position, pose.Position);
                interpolator.ResetRenderPose(pose.Position);
            }
            appliedHandoffEpoch = assignment.Epoch;
            ApplyAssignment(assignment);
            if (authority.RunsNavigation && !keepServerAction)
            {
                interpolator.ResetRenderPose(pose.Position);
                body.linearVelocity = pose.Velocity;
                if (pose.Facing.sqrMagnitude > .0001f) enemyController?.Movement?.SetFacingDirection(pose.Facing);
                var restoredAction = pose.Runtime.Action;
                if(restoredAction.ActionId != 0 && restoredAction.ActionId == cancelledSequenceActionId)restoredAction.Phase=EnemyAttackPresentationPhase.Cancelled;
                enemyController?.RestoreSimulationAction(restoredAction, EnemySimulationClock.CombatNow);
                RestoreSimulationKnockback(pose.Runtime, pose.SampleNetworkTime);
                authority.MarkDiscontinuity();
            }
            restoringHandoff = false;
            NetworkEnemySimulationWorld.Instance?.RecordMotionCorrection(this, current, previouslyLocal, previousPosition);
            if (authority.ConsumesSnapshots)
            {
                // A handoff baseline is not a producer's first movement packet.
                pose.AssignmentEpoch = assignment.Epoch; pose.Sequence = 0;
                interpolator.Push(pose);
                var action = pose.Runtime.Action;
                var phase = action.PhaseAt(EnemySimulationClock.CombatNow);
                ReceiveRemoteAttackPresentation(new EnemyAttackPresentationEdge
                {
                    EnemyEntityId = netId, AssignmentEpoch = assignment.Epoch, StateSequence = 1,
                    Phase = phase, Facing = action.Facing,
                    StateStartNetworkTime = action.StartAt(phase),
                    PhaseDuration = phase == EnemyAttackPresentationPhase.Warning || phase == EnemyAttackPresentationPhase.Active ||
                        phase == EnemyAttackPresentationPhase.Recovery ? (float)(action.EndAt(phase) - action.StartAt(phase)) : 0,
                    Checkpoint = current.Checkpoint
                });
                // The synthetic baseline must not consume the first real message's sequence.
                receivedAttackStateSequence = 0;
            }
            if (MonsterSupergroup.GAS.CombatEvidence.Enabled) MonsterSupergroup.GAS.CombatEvidence.Event(isServer ? "Server" : "Replica", "authority.handoff", "Applied", current.Reason.ToString(),
                target: netId, input: current, before: new { position = previousPosition }, after: new { assignment, position = transform.position, appliedHandoffEpoch, keepServerAction });
            QueueAssignmentAttackPresentationBaseline();
            if (hasPendingFutureSnapshot && pendingFutureSnapshot.AssignmentEpoch == assignment.Epoch)
            {
                var pending = pendingFutureSnapshot; hasPendingFutureSnapshot = false;
                ReceiveRemoteSnapshot(pending);
            }
            else if (hasPendingFutureSnapshot && !EnemySimulationSequence.IsNewer(pendingFutureSnapshot.AssignmentEpoch, assignment.Epoch))
                hasPendingFutureSnapshot = false;
            if (NetworkClient.active) NetworkEnemySimulationWorld.Instance?.TryApplyPendingAttackPresentation(this);
        }

        private EnemySimulationRuntimeState CaptureSimulationRuntime(double now)
        {
            var motion = enemyController != null ? enemyController.CaptureSimulationKnockback() : default;
            return new EnemySimulationRuntimeState
            {
                Action = enemyController != null ? enemyController.CaptureSimulationAction(EnemySimulationClock.CombatNow) : default,
                Knockback = motion,
                KnockbackSettings = motion.Active ? activeKnockbackSettings : default,
                KnockbackCommandId = activeKnockbackCommandId, KnockbackDamageEventId = activeKnockbackDamageEventId,
                LastHandledKnockbackId = knockbackHistory.LastCommandId,
                PredictedKnockbacks = predictedKnockbacks.Capture(now)
            };
        }

        internal EnemySimulationCheckpoint CaptureCurrentCheckpoint() => new EnemySimulationCheckpoint
        {
            Movement = new EnemySimulationSnapshot
            {
                EnemyEntityId = netId, AssignmentEpoch = assignment.Epoch,
                SampleNetworkTime = EnemySimulationClock.Now, Position = body != null ? body.position : (Vector2)transform.position,
                Velocity = body != null ? body.linearVelocity : Vector2.zero,
                Facing = enemyController != null ? enemyController.FacingDirection : Vector2.right,
                Runtime = CaptureSimulationRuntime(EnemySimulationClock.Now)
            }
        };

        private void RestoreSimulationKnockback(EnemySimulationRuntimeState state, double sampleTime)
        {
            knockbackHistory.RestoreHandled(state.LastHandledKnockbackId);
            predictedKnockbacks.Restore(state.PredictedKnockbacks, EnemySimulationClock.Now);
            foreach (var receipt in predictedKnockbacks.Capture(EnemySimulationClock.Now))
                ordinaryHitHistory.MarkProcessed(receipt.DamageEventId, EnemySimulationClock.Now);
            if (state.KnockbackDamageEventId != 0) ordinaryHitHistory.MarkProcessed(state.KnockbackDamageEventId, EnemySimulationClock.Now);
            if (!state.Knockback.Active || !state.KnockbackSettings.IsValid || enemyController == null) return;
            var preset = state.KnockbackSettings.CreateRuntimePreset();
            if (!enemyController.RestoreSimulationKnockback(state.Knockback, preset, (float)(EnemySimulationClock.Now - sampleTime)))
            { Destroy(preset); return; }
            activeKnockbackPreset = preset; activeKnockbackEpoch = assignment.Epoch;
            activeKnockbackSettings = state.KnockbackSettings;
            activeKnockbackCommandId = state.KnockbackCommandId; activeKnockbackDamageEventId = state.KnockbackDamageEventId;
        }
    }
}
