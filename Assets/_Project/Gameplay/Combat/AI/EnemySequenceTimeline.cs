using UnityEngine;

namespace AstralShift.HellMaiden.AI
{
    // A three-strike view of the existing action checkpoint, never an independent clock.
    public static class EnemySequenceTimeline
    {
        public static EnemyActionState Resolve(EnemyActionState state, double now)
        {
            return state.Sequence ? EnemyActionTimeline.Resolve(state, now) : state;
        }

        public static double Duration(Vector3 warnings, Vector3 actives) =>
            (double)warnings.x + warnings.y + warnings.z + actives.x + actives.y + actives.z;

        public static bool HasPose(EnemyActionState state) => EnemyActionTimeline.HasPose(state);
        public static int Order(EnemyActionState state) => EnemyActionTimeline.Order(state);
    }
}
