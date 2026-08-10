using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MonsterSupergroup.GAS
{
    public sealed class ModifierRegistry
    {
        private readonly Dictionary<EquipmentModifierID, EquipmentRegistration> equipmentRegistrations;
        private readonly Dictionary<PerkModifierID, PerkRegistration> perkRegistrations;
        private readonly ReadOnlyCollection<EquipmentRegistration> equipmentRegistrationList;
        private readonly ReadOnlyCollection<PerkRegistration> perkRegistrationList;

        private ModifierRegistry(
            Dictionary<EquipmentModifierID, EquipmentRegistration> equipmentRegistrations,
            Dictionary<PerkModifierID, PerkRegistration> perkRegistrations,
            EquipmentRegistration[] equipmentRegistrationList,
            PerkRegistration[] perkRegistrationList)
        {
            this.equipmentRegistrations = equipmentRegistrations;
            this.perkRegistrations = perkRegistrations;
            this.equipmentRegistrationList = Array.AsReadOnly(equipmentRegistrationList);
            this.perkRegistrationList = Array.AsReadOnly(perkRegistrationList);
        }

        public IReadOnlyList<EquipmentRegistration> EquipmentRegistrations => equipmentRegistrationList;

        public IReadOnlyList<PerkRegistration> PerkRegistrations => perkRegistrationList;

        internal RuntimeEquipmentModifier CreateEquipment(
            EquipmentModifierID id,
            EquipmentModifierParameters parameters)
        {
            if (!equipmentRegistrations.TryGetValue(id, out EquipmentRegistration registration))
            {
                throw new KeyNotFoundException($"Unknown Equipment Modifier ID: {id}.");
            }

            return registration.Create(parameters);
        }

        internal RuntimePerkModifier CreatePerk(PerkModifierID id, PerkModifierParameters parameters)
        {
            if (!perkRegistrations.TryGetValue(id, out PerkRegistration registration))
            {
                throw new KeyNotFoundException($"Unknown Perk Modifier ID: {id}.");
            }

            return registration.Create(parameters);
        }

        public sealed class Builder
        {
            private readonly Dictionary<EquipmentModifierID, EquipmentRegistration> equipmentById =
                new Dictionary<EquipmentModifierID, EquipmentRegistration>();
            private readonly Dictionary<Type, EquipmentModifierID> equipmentByType =
                new Dictionary<Type, EquipmentModifierID>();
            private readonly Dictionary<PerkModifierID, PerkRegistration> perksById =
                new Dictionary<PerkModifierID, PerkRegistration>();
            private readonly Dictionary<Type, PerkModifierID> perksByType =
                new Dictionary<Type, PerkModifierID>();

            public Builder RegisterEquipment<TModifier, TParameters>(
                EquipmentModifierID id,
                Func<TParameters, TModifier> factory)
                where TModifier : RuntimeEquipmentModifier
                where TParameters : EquipmentModifierParameters
            {
                if (factory == null)
                {
                    throw new ArgumentNullException(nameof(factory));
                }

                EnsureValidId(id, nameof(id));
                Type modifierType = typeof(TModifier);
                if (equipmentById.ContainsKey(id))
                {
                    throw new InvalidOperationException($"Equipment Modifier ID {id} is already registered.");
                }

                if (equipmentByType.ContainsKey(modifierType))
                {
                    throw new InvalidOperationException(
                        $"Equipment Modifier type {modifierType.FullName} is already registered.");
                }

                var registration = new EquipmentRegistration(
                    id,
                    modifierType,
                    typeof(TParameters),
                    parameters =>
                    {
                        if (parameters == null || parameters.GetType() != typeof(TParameters))
                        {
                            string actualType = parameters == null ? "null" : parameters.GetType().FullName;
                            throw new ArgumentException(
                                $"Equipment Modifier {id} requires parameters of exact type " +
                                $"{typeof(TParameters).FullName}, but received {actualType}.",
                                nameof(parameters));
                        }

                        TModifier modifier = factory((TParameters)parameters);
                        if (modifier == null)
                        {
                            throw new InvalidOperationException(
                                $"Factory for Equipment Modifier {id} returned null.");
                        }

                        if (!EqualityComparer<EquipmentModifierID>.Default.Equals(id, modifier.ID))
                        {
                            throw new InvalidOperationException(
                                $"Factory registered for Equipment Modifier {id} produced modifier {modifier.ID}.");
                        }

                        return modifier;
                    });

                equipmentById.Add(id, registration);
                equipmentByType.Add(modifierType, id);
                return this;
            }

            public Builder RegisterPerk<TModifier, TParameters>(
                PerkModifierID id,
                Func<TParameters, TModifier> factory)
                where TModifier : RuntimePerkModifier
                where TParameters : PerkModifierParameters
            {
                if (factory == null)
                {
                    throw new ArgumentNullException(nameof(factory));
                }

                EnsureValidId(id, nameof(id));
                Type modifierType = typeof(TModifier);
                if (perksById.ContainsKey(id))
                {
                    throw new InvalidOperationException($"Perk Modifier ID {id} is already registered.");
                }

                if (perksByType.ContainsKey(modifierType))
                {
                    throw new InvalidOperationException(
                        $"Perk Modifier type {modifierType.FullName} is already registered.");
                }

                var registration = new PerkRegistration(
                    id,
                    modifierType,
                    typeof(TParameters),
                    parameters =>
                    {
                        if (parameters == null || parameters.GetType() != typeof(TParameters))
                        {
                            string actualType = parameters == null ? "null" : parameters.GetType().FullName;
                            throw new ArgumentException(
                                $"Perk Modifier {id} requires parameters of exact type " +
                                $"{typeof(TParameters).FullName}, but received {actualType}.",
                                nameof(parameters));
                        }

                        TModifier modifier = factory((TParameters)parameters);
                        if (modifier == null)
                        {
                            throw new InvalidOperationException($"Factory for Perk Modifier {id} returned null.");
                        }

                        if (!EqualityComparer<PerkModifierID>.Default.Equals(id, modifier.ID))
                        {
                            throw new InvalidOperationException(
                                $"Factory registered for Perk Modifier {id} produced modifier {modifier.ID}.");
                        }

                        return modifier;
                    });

                perksById.Add(id, registration);
                perksByType.Add(modifierType, id);
                return this;
            }

            public ModifierRegistry Build()
            {
                var equipmentDictionary =
                    new Dictionary<EquipmentModifierID, EquipmentRegistration>(equipmentById);
                var perkDictionary = new Dictionary<PerkModifierID, PerkRegistration>(perksById);

                var equipmentList = new EquipmentRegistration[equipmentDictionary.Count];
                equipmentDictionary.Values.CopyTo(equipmentList, 0);
                Array.Sort(equipmentList, CompareEquipmentRegistration);

                var perkList = new PerkRegistration[perkDictionary.Count];
                perkDictionary.Values.CopyTo(perkList, 0);
                Array.Sort(perkList, ComparePerkRegistration);

                return new ModifierRegistry(equipmentDictionary, perkDictionary, equipmentList, perkList);
            }

            private static void EnsureValidId<TId>(TId id, string parameterName)
                where TId : struct
            {
                if (EqualityComparer<TId>.Default.Equals(id, default(TId)))
                {
                    throw new ArgumentException("Modifier ID 0 is reserved as Invalid.", parameterName);
                }
            }

            private static int CompareEquipmentRegistration(
                EquipmentRegistration left,
                EquipmentRegistration right)
            {
                return left.Id.Value.CompareTo(right.Id.Value);
            }

            private static int ComparePerkRegistration(PerkRegistration left, PerkRegistration right)
            {
                return left.Id.Value.CompareTo(right.Id.Value);
            }
        }

        public sealed class EquipmentRegistration
        {
            private readonly Func<EquipmentModifierParameters, RuntimeEquipmentModifier> factory;

            internal EquipmentRegistration(
                EquipmentModifierID id,
                Type modifierType,
                Type parametersType,
                Func<EquipmentModifierParameters, RuntimeEquipmentModifier> factory)
            {
                Id = id;
                ModifierType = modifierType;
                ParametersType = parametersType;
                this.factory = factory;
            }

            public EquipmentModifierID Id { get; }

            public Type ModifierType { get; }

            public Type ParametersType { get; }

            internal RuntimeEquipmentModifier Create(EquipmentModifierParameters parameters)
            {
                return factory(parameters);
            }
        }

        public sealed class PerkRegistration
        {
            private readonly Func<PerkModifierParameters, RuntimePerkModifier> factory;

            internal PerkRegistration(
                PerkModifierID id,
                Type modifierType,
                Type parametersType,
                Func<PerkModifierParameters, RuntimePerkModifier> factory)
            {
                Id = id;
                ModifierType = modifierType;
                ParametersType = parametersType;
                this.factory = factory;
            }

            public PerkModifierID Id { get; }

            public Type ModifierType { get; }

            public Type ParametersType { get; }

            internal RuntimePerkModifier Create(PerkModifierParameters parameters)
            {
                return factory(parameters);
            }
        }
    }
}
