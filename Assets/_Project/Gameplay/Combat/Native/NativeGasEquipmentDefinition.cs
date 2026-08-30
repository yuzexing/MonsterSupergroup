using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS.Authoring;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [Serializable]
    public sealed class NativeGasEquipmentLevel
    {
        [SerializeField] private List<EquipmentDataModifier> modifiers =
            new List<EquipmentDataModifier>();

        public IReadOnlyList<EquipmentDataModifier> Modifiers => modifiers;

        public void Configure(IReadOnlyList<EquipmentDataModifier> values)
        {
            modifiers = values == null
                ? new List<EquipmentDataModifier>()
                : new List<EquipmentDataModifier>(values);
        }
    }

    [CreateAssetMenu(
        fileName = "NativeGasEquipment",
        menuName = "Monster Supergroup/GAS/Native HellMaiden Equipment")]
    public sealed class NativeGasEquipmentDefinition : ScriptableObject
    {
        [SerializeField] private List<NativeGasEquipmentLevel> levels =
            new List<NativeGasEquipmentLevel>();

        public int LevelCount => levels?.Count ?? 0;

        public IReadOnlyList<EquipmentDataModifier> GetModifiers(int levelIndex)
        {
            if (levels == null || (uint)levelIndex >= levels.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(levelIndex));
            }

            NativeGasEquipmentLevel level = levels[levelIndex];
            if (level == null)
            {
                throw new InvalidOperationException(
                    $"{name} has a null level at index {levelIndex}.");
            }

            return level.Modifiers;
        }

        public void Configure(IReadOnlyList<NativeGasEquipmentLevel> newLevels)
        {
            levels = newLevels == null
                ? new List<NativeGasEquipmentLevel>()
                : new List<NativeGasEquipmentLevel>(newLevels);
        }
    }
}
