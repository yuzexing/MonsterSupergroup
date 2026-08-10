using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonsterSupergroup.GAS;
using UnityEditor;

namespace MonsterSupergroup.GAS.Editor
{
    public enum ModifierDomain
    {
        Equipment,
        Perk
    }

    public readonly struct ModifierDescriptor
    {
        public ModifierDescriptor(ModifierDomain domain, uint id, string displayName, Type modifierType, Type parametersType)
        {
            Domain = domain;
            Id = id;
            DisplayName = displayName;
            ModifierType = modifierType;
            ParametersType = parametersType;
        }

        public ModifierDomain Domain { get; }
        public uint Id { get; }
        public string DisplayName { get; }
        public Type ModifierType { get; }
        public Type ParametersType { get; }
    }

    public static class ModifierTypeCatalog
    {
        private static IReadOnlyList<ModifierDescriptor> equipment;
        private static IReadOnlyList<ModifierDescriptor> perks;

        public static IReadOnlyList<ModifierDescriptor> Equipment => equipment ??= DiscoverEquipment();
        public static IReadOnlyList<ModifierDescriptor> Perks => perks ??= DiscoverPerks();

        public static void Refresh()
        {
            equipment = null;
            perks = null;
        }

        public static bool TryGetEquipment(uint id, out ModifierDescriptor descriptor)
        {
            return TryGet(Equipment, id, out descriptor);
        }

        public static bool TryGetPerk(uint id, out ModifierDescriptor descriptor)
        {
            return TryGet(Perks, id, out descriptor);
        }

        private static IReadOnlyList<ModifierDescriptor> DiscoverEquipment()
        {
            return Discover<EquipmentModifierTypeAttribute>(
                ModifierDomain.Equipment,
                typeof(RuntimeEquipmentModifier),
                typeof(EquipmentModifierParameters));
        }

        private static IReadOnlyList<ModifierDescriptor> DiscoverPerks()
        {
            return Discover<PerkModifierTypeAttribute>(
                ModifierDomain.Perk,
                typeof(RuntimePerkModifier),
                typeof(PerkModifierParameters));
        }

        private static IReadOnlyList<ModifierDescriptor> Discover<TAttribute>(
            ModifierDomain domain,
            Type modifierBaseType,
            Type parametersBaseType)
            where TAttribute : Attribute
        {
            var result = new List<ModifierDescriptor>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<TAttribute>())
            {
                if (type.IsAbstract || !modifierBaseType.IsAssignableFrom(type))
                {
                    continue;
                }

                CustomAttributeData attribute = type.CustomAttributes.Single(data => data.AttributeType == typeof(TAttribute));
                if (attribute.ConstructorArguments.Count != 3)
                {
                    throw new InvalidOperationException($"{typeof(TAttribute).Name} on {type.FullName} must declare id, display name and parameters type.");
                }

                uint id = Convert.ToUInt32(attribute.ConstructorArguments[0].Value);
                string displayName = (string)attribute.ConstructorArguments[1].Value;
                Type parametersType = (Type)attribute.ConstructorArguments[2].Value;
                if (parametersType == null || !parametersBaseType.IsAssignableFrom(parametersType))
                {
                    throw new InvalidOperationException($"{type.FullName} declares an invalid parameters type.");
                }

                result.Add(new ModifierDescriptor(domain, id, displayName, type, parametersType));
            }

            return result
                .OrderBy(descriptor => descriptor.Id)
                .ThenBy(descriptor => descriptor.ModifierType.FullName, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool TryGet(IReadOnlyList<ModifierDescriptor> descriptors, uint id, out ModifierDescriptor descriptor)
        {
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (descriptors[index].Id == id)
                {
                    descriptor = descriptors[index];
                    return true;
                }
            }

            descriptor = default;
            return false;
        }
    }
}
