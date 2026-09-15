using UnityEngine;

namespace AstralShift.HellMaiden.AI
{
    // A three-strike view of the existing action checkpoint, never an independent clock.
    public static class EnemySequenceTimeline
    {
        public static EnemyActionState Resolve(EnemyActionState state, double now)
        {
            if (!state.Sequence) return state;
            now = System.Math.Max(now, state.StartAt(state.Phase));
            double start = state.ComboStartedAt;
            for (int i = 0; i < 3; i++)
            {
                double warningEnd = start + state.SequenceWarnings[i];
                double activeEnd = warningEnd + state.SequenceActives[i];
                if (now < activeEnd || i == 2)
                {
                    state.StrikeIndex = i;
                    state.WarningStartedAt = start; state.WarningUntil = warningEnd; state.ActiveUntil = activeEnd;
                    if (state.Phase != EnemyAttackPresentationPhase.Cancelled && state.Phase != EnemyAttackPresentationPhase.Inactive)
                        state.Phase = now < warningEnd ? EnemyAttackPresentationPhase.Warning :
                            now < activeEnd ? EnemyAttackPresentationPhase.Active :
                            now < state.RecoveryUntil ? EnemyAttackPresentationPhase.Recovery : EnemyAttackPresentationPhase.Inactive;
                    return state;
                }
                start = activeEnd;
            }
            return state;
        }

        public static double Duration(Vector3 warnings, Vector3 actives) =>
            (double)warnings.x + warnings.y + warnings.z + actives.x + actives.y + actives.z;

        public static bool HasPose(EnemyActionState state) => state.PoseStrikeIndex == state.StrikeIndex &&
            (state.LockedStrikeMask & (1 << state.StrikeIndex)) != 0;
        public static int Order(EnemyActionState state) => state.Phase == EnemyAttackPresentationPhase.Cancelled ? 8 :
            state.Phase == EnemyAttackPresentationPhase.Inactive ? 7 : state.Phase == EnemyAttackPresentationPhase.Recovery ? 6 :
            state.StrikeIndex*2+(state.Phase==EnemyAttackPresentationPhase.Active?1:0);
    }
}
