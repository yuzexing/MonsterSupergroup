using System;

namespace MonsterSupergroup.GAS
{
    public sealed class RuntimeModifierFactory
    {
        private readonly ModifierRegistry registry;

        public RuntimeModifierFactory(ModifierRegistry registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public RuntimeEquipmentModifier CreateEquipment(
            EquipmentModifierID id,
            EquipmentModifierParameters parameters)
        {
            return registry.CreateEquipment(id, parameters);
        }

        public RuntimePerkModifier CreatePerk(PerkModifierID id, PerkModifierParameters parameters)
        {
            return registry.CreatePerk(id, parameters);
        }
    }
}
