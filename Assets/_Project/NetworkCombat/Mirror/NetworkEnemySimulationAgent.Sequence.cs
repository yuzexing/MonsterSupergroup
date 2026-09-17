using System;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationAgent
    {
        private ulong cancelledSequenceActionId;
        private ulong broadcastSequenceInterruptId;
        internal bool TryMarkSequenceInterruptBroadcast(ulong actionId)
        { if(actionId==0||broadcastSequenceInterruptId==actionId)return false;broadcastSequenceInterruptId=actionId;return true; }
        internal bool ValidateSequenceAction(EnemyActionState action)
        {
            if (!(new EnemySimulationRuntimeState{Action=action}).IsFinite) return false;
            if (action.WarningStep.Enabled && (enemyController == null ||
                enemyController.GetComponent<EnemyWarningStep>() is not { enabled: true })) return false;
            if (action.ActionId == cancelledSequenceActionId && action.ActionId != 0 && action.Phase != EnemyAttackPresentationPhase.Cancelled && action.Phase != EnemyAttackPresentationPhase.Inactive) return false;
            var timed = enemyController != null ? enemyController.attackScript : null;
            if (timed != null && timed.SupportsSharedTimeline && timed is not SequenceEnemyAttack && action.ActionId != 0)
            {
                timed.enemyAnimator = enemyController.enemyAnimator;
                var strike = timed.TimelineStrikes[0];
                if (action.Sequence || Math.Abs(action.WarningUntil-action.WarningStartedAt-strike.Warning)>.001 ||
                    Math.Abs(action.ActiveUntil-action.WarningUntil-strike.Active)>.001 ||
                    Math.Abs(action.RecoveryUntil-action.ActiveUntil-timed.RecoveryTime)>.001) return false;
                if (action.Phase != EnemyAttackPresentationPhase.Cancelled && action.Phase != EnemyAttackPresentationPhase.Inactive &&
                    Math.Abs(action.NextAttackAt-action.RecoveryUntil-enemyController.attackCooldown)>.001) return false;
            }
            if (enemyController == null || enemyController.attackScript is not SequenceEnemyAttack sequence) return !action.Sequence;
            if (action.ActionId == 0) return !action.Sequence;
            if (!action.Sequence || action.Dash || action.Explosion || enemyController.enemyAnimator is not MultipleAttackAnimator animator) return false;
            if (action.ActionId == cancelledSequenceActionId && action.Phase != EnemyAttackPresentationPhase.Cancelled && action.Phase != EnemyAttackPresentationPhase.Inactive) return false;
            if ((action.SequenceWarnings-animator.SequenceWarnings).sqrMagnitude>.00000001f ||
                (action.SequenceActives-animator.SequenceActives).sqrMagnitude>.00000001f) return false;
            sequence.enemyAnimator=animator;
            double start=action.ComboStartedAt;
            for(int i=0;i<action.StrikeIndex;i++)start+=(double)action.SequenceWarnings[i]+action.SequenceActives[i];
            if(Math.Abs(action.WarningStartedAt-start)>.00001||
                Math.Abs(action.WarningUntil-start-action.SequenceWarnings[action.StrikeIndex])>.00001||
                Math.Abs(action.ActiveUntil-action.WarningUntil-action.SequenceActives[action.StrikeIndex])>.00001)return false;
            if(action.Phase!=EnemyAttackPresentationPhase.Cancelled&&action.Phase!=EnemyAttackPresentationPhase.Inactive&&
                Math.Abs(action.NextAttackAt-action.RecoveryUntil-enemyController.attackCooldown)>.00001)return false;
            return Math.Abs(action.RecoveryUntil-action.ComboStartedAt-EnemySequenceTimeline.Duration(action.SequenceWarnings,action.SequenceActives)-sequence.RecoveryTime)<.00001;
        }
        internal void RememberSequenceCancellation(EnemyActionState action)
        { if(action.Phase==EnemyAttackPresentationPhase.Cancelled)cancelledSequenceActionId=action.ActionId; }

        internal void ApplyAcceptedSequenceInterrupt(ulong actionId)
        {
            if(actionId==0||!IsCanonicalAlive||CurrentActionId!=actionId||enemyController?.attackScript?.SupportsSharedTimeline!=true)return;
            cancelledSequenceActionId=actionId;
            GetComponent<NetworkEnemyMeleeReplica>()?.CancelActionPresentation(actionId);
        }
    }
}
