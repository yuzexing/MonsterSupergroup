using System;

namespace MonsterSupergroup.GAS
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class EquipmentModifierTypeAttribute : Attribute
    {
        public EquipmentModifierTypeAttribute(uint id, string displayName, Type parametersType)
        {
            Id = id;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            ParametersType = parametersType ?? throw new ArgumentNullException(nameof(parametersType));
        }

        public uint Id { get; }

        public string DisplayName { get; }

        public Type ParametersType { get; }
    }
}
