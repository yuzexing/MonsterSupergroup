using System;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>One resolved dash motion, copied before permission is requested or presentation starts.</summary>
    [Serializable]
    public struct DashMotionParameters
    {
        public Vector2 StartPosition;
        public Vector2 Direction;
        public float Distance;
        public float Duration;
        public float PeakSpeed;

        public DashMotionParameters(Vector2 startPosition, Vector2 direction, float distance, float duration, float peakSpeed)
        {
            StartPosition = startPosition;
            Direction = direction;
            Distance = distance;
            Duration = duration;
            PeakSpeed = peakSpeed;
        }
    }
}
