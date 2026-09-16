using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.AI
{
    public readonly struct EnemyStrikeTiming
    {
        public readonly float Warning, Active;
        public EnemyStrikeTiming(float warning, float active) { Warning = warning; Active = active; }
    }

    /// <summary>Pure action-time evaluation. The checkpoint, not an animation callback, owns deadlines.</summary>
    public static class EnemyActionTimeline
    {
        public static EnemyActionState Begin(ulong id, double now, IReadOnlyList<EnemyStrikeTiming> strikes,
            float recovery, float cooldown, Vector2 facing, Vector2 target)
        {
            // The existing wire format supports the authored single and three-strike attacks.
            if (strikes.Count != 1 && strikes.Count != 3) throw new ArgumentOutOfRangeException(nameof(strikes));
            var state = new EnemyActionState { ActionId = id, Phase = EnemyAttackPresentationPhase.Warning,
                Sequence = strikes.Count > 1, ComboStartedAt = now, WarningStartedAt = now,
                Facing = facing, TargetPosition = target, LockedStrikeMask = 1, PoseStrikeIndex = 0 };
            double end = now;
            for (int i = 0; i < strikes.Count; i++)
            {
                state.SequenceWarnings[i] = strikes[i].Warning;
                state.SequenceActives[i] = strikes[i].Active;
                end += (double)strikes[i].Warning + strikes[i].Active;
            }
            state.WarningUntil = now + strikes[0].Warning;
            state.ActiveUntil = state.WarningUntil + strikes[0].Active;
            state.RecoveryUntil = end + recovery;
            state.NextAttackAt = state.RecoveryUntil + cooldown;
            return state;
        }

        public static EnemyActionState Resolve(EnemyActionState state, double now)
        {
            if (state.Phase == EnemyAttackPresentationPhase.Cancelled ||
                state.Phase == EnemyAttackPresentationPhase.Inactive) return state;
            now = Math.Max(now, state.StartAt(state.Phase));
            if (state.Sequence)
            {
                double start = state.ComboStartedAt;
                for (int i = 0; i < 3; i++)
                {
                    double warningEnd = start + state.SequenceWarnings[i];
                    double activeEnd = warningEnd + state.SequenceActives[i];
                    if (now < activeEnd || i == 2)
                    {
                        state.StrikeIndex = i; state.WarningStartedAt = start;
                        state.WarningUntil = warningEnd; state.ActiveUntil = activeEnd;
                        break;
                    }
                    start = activeEnd;
                }
            }
            state.Phase = now < state.WarningUntil ? EnemyAttackPresentationPhase.Warning :
                now < state.ActiveUntil ? EnemyAttackPresentationPhase.Active :
                now < state.RecoveryUntil ? EnemyAttackPresentationPhase.Recovery : EnemyAttackPresentationPhase.Inactive;
            return state;
        }

        public static bool HasPose(EnemyActionState state) => !state.Sequence ||
            state.PoseStrikeIndex == state.StrikeIndex && (state.LockedStrikeMask & (1 << state.StrikeIndex)) != 0;

        public static int Order(EnemyActionState state) => state.Phase == EnemyAttackPresentationPhase.Cancelled ? int.MaxValue :
            state.Phase == EnemyAttackPresentationPhase.Inactive ? int.MaxValue - 1 :
            state.Phase == EnemyAttackPresentationPhase.Recovery ? 6 :
            state.StrikeIndex * 2 + (state.Phase == EnemyAttackPresentationPhase.Active ? 1 : 0);
    }
}
