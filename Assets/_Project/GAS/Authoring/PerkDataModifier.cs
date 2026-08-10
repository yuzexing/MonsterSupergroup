using System;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.GAS.Authoring
{
    [Serializable]
    public sealed class PerkDataModifier
    {
        [SerializeField] private uint modifierId;
        [SerializeReference] private PerkModifierParameters parameters;

        public uint ModifierIdValue => modifierId;
        public PerkModifierID ModifierId => new PerkModifierID(modifierId);
        public PerkModifierParameters Parameters => parameters;

        public RuntimePerkModifier CreateRuntime(RuntimeModifierFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return factory.CreatePerk(ModifierId, parameters);
        }
    }
}
