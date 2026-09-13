using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Allocation-free, stable nearest selection over already qualified server avatars.</summary>
    public struct EnemyNearestTarget
    {
        public uint AvatarId { get; private set; }
        private ulong participantId;
        private float distanceSquared;

        public void Consider(uint avatar, ulong participant, Vector2 position, Vector2 enemyPosition, bool eligible)
        {
            if (!eligible || avatar == 0) return;
            float distance = (position - enemyPosition).sqrMagnitude;
            if (float.IsNaN(distance) || float.IsInfinity(distance)) return;
            if (AvatarId == 0 || distance < distanceSquared || distance == distanceSquared && participant < participantId)
            { AvatarId = avatar; participantId = participant; distanceSquared = distance; }
        }
    }
}
