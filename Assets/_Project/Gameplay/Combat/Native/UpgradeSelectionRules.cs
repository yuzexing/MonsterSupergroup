using System;
using AstralShift.HellMaiden.Data;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [CreateAssetMenu(menuName = "MonsterSupergroup/Combat/Upgrade Selection Rules")]
    public sealed class UpgradeSelectionRules : ScriptableObject
    {
        [SerializeField] private int[] weaponLevels = { 4, 12, 18 };
        [SerializeField, Min(2)] private int firstPerkLevel = 7;
        [SerializeField, Min(1)] private int perkInterval = 2;
        [SerializeField] private PerkDropWeightsData perkWeights;

        public PerkDropWeightsData PerkWeights => perkWeights;

        public UpgradeRewardKind RewardAtLevel(int level)
        {
            if (weaponLevels == null || firstPerkLevel < 2 || perkInterval < 1)
                throw new InvalidOperationException("Invalid upgrade reward schedule.");
            if (Array.IndexOf(weaponLevels, level) >= 0) return UpgradeRewardKind.Weapon;
            return level >= firstPerkLevel && (level - firstPerkLevel) % perkInterval == 0
                ? UpgradeRewardKind.Perk : UpgradeRewardKind.Equipment;
        }

        public void Validate()
        {
            RewardAtLevel(2);
            var levels = new System.Collections.Generic.HashSet<int>();
            foreach (int level in weaponLevels)
                if (level < 2 || !levels.Add(level))
                    throw new InvalidOperationException("Weapon reward levels must be distinct and at least 2.");
            if (perkWeights == null) throw new InvalidOperationException("Upgrade rules require Perk drop weights.");
        }
    }
}
