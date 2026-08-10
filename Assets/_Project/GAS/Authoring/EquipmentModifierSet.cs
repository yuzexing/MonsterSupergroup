using System.Collections.Generic;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.GAS.Authoring
{
    [CreateAssetMenu(fileName = "EquipmentModifierSet", menuName = "Monster Supergroup/GAS/Equipment Modifier Set")]
    public sealed class EquipmentModifierSet : ScriptableObject
    {
        [SerializeField] private List<EquipmentDataModifier> modifiers = new List<EquipmentDataModifier>();

        public IReadOnlyList<EquipmentDataModifier> Modifiers => modifiers;

        public IEnumerable<RuntimeEquipmentModifier> CreateRuntime(RuntimeModifierFactory factory)
        {
            foreach (EquipmentDataModifier modifier in modifiers)
            {
                yield return modifier.CreateRuntime(factory);
            }
        }
    }
}
