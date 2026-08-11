using System;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.GAS.Authoring
{
    [Serializable]
    public sealed class EquipmentDataModifier
    {
        [SerializeField] private uint modifierId;
        [SerializeReference] private EquipmentModifierParameters parameters;

        public EquipmentDataModifier()
        {
        }

        public EquipmentDataModifier(
            EquipmentModifierID modifierId,
            EquipmentModifierParameters parameters)
        {
            if (!modifierId.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(modifierId), "Modifier ID must be non-zero.");
            }

            this.modifierId = modifierId.Value;
            this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        public uint ModifierIdValue => modifierId;
        public EquipmentModifierID ModifierId => new EquipmentModifierID(modifierId);
        public EquipmentModifierParameters Parameters => parameters;

        public RuntimeEquipmentModifier CreateRuntime(RuntimeModifierFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return factory.CreateEquipment(ModifierId, parameters);
        }
    }
}
