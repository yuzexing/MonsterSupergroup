using System;
using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat.Content
{
    [CreateAssetMenu(menuName = "MonsterSupergroup/Enemies/Stats")]
    public sealed class EnemyStatsDefinition : ScriptableObject
    {
        [SerializeField] private EnemyStatsValues baseStats = new EnemyStatsValues
        { Health = 50, Damage = 10, Speed = 3, XP = 5, KnockBackMultiplier = 1, WindMultiplier = 1 };

        // Never expose the mutable authored values to a spawned enemy.
        public EnemyStatsValues Capture()
        {
            if (baseStats == null || baseStats.Health < 1 || baseStats.Damage < 0 ||
                !Valid(baseStats.Speed) || !Valid(baseStats.XP) || !Valid(baseStats.StunTime) ||
                !Valid(baseStats.KnockBackMultiplier) || !Valid(baseStats.WindMultiplier))
                throw new ArgumentException("Invalid enemy base stats: " + name);
            return baseStats.Clone();
        }
        private static bool Valid(float value) => value >= 0 && !float.IsNaN(value) && !float.IsInfinity(value);
#if UNITY_EDITOR
        public void SetAuthoringValues(EnemyStatsValues value) => baseStats = value?.Clone() ?? throw new ArgumentNullException(nameof(value));
#endif
    }
}
