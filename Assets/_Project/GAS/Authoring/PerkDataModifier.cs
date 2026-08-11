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

        public PerkDataModifier()
        {
        }

        public PerkDataModifier(
            PerkModifierID modifierId,
            PerkModifierParameters parameters)
        {
            if (!modifierId.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(modifierId), "Modifier ID must be non-zero.");
            }

            this.modifierId = modifierId.Value;
            this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

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
