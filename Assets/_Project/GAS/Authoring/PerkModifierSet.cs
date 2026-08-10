using System.Collections.Generic;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.GAS.Authoring
{
    [CreateAssetMenu(fileName = "PerkModifierSet", menuName = "Monster Supergroup/GAS/Perk Modifier Set")]
    public sealed class PerkModifierSet : ScriptableObject
    {
        [SerializeField] private List<PerkDataModifier> modifiers = new List<PerkDataModifier>();

        public IReadOnlyList<PerkDataModifier> Modifiers => modifiers;

        public IEnumerable<RuntimePerkModifier> CreateRuntime(RuntimeModifierFactory factory)
        {
            foreach (PerkDataModifier modifier in modifiers)
            {
                yield return modifier.CreateRuntime(factory);
            }
        }
    }
}
