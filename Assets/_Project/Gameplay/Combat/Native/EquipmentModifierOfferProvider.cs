using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Cards;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.Gameplay.Combat
{
    public sealed class EquipmentModifierOfferProvider
    {
        public const int OfferCount = 3;
        private readonly IRandomSource random;
        private readonly IReadOnlyList<ModifierRegistry.EquipmentRegistration> registrations;
        private ulong nextOfferId = 1;

        public EquipmentModifierOfferProvider(IRandomSource random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            registrations = GeneratedModifierRegistry.Create().EquipmentRegistrations;
        }

        public IReadOnlyList<ModifierOffer> Generate(EquipmentDB database, WeaponData weapon)
        {
            if (database == null || database.Equipments == null)
                throw new InvalidOperationException("Modifier selection requires a configured EquipmentDB.");
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));

            var eligible = new List<EquipmentData>();
            var cardIds = new HashSet<uint>();
            foreach (EquipmentData card in database.Equipments)
            {
                if (card == null) continue;
                if (!cardIds.Add(card.ID))
                    throw new InvalidOperationException($"EquipmentDB contains duplicate card ID {card.ID}.");
                if (CanOffer(card, weapon)) eligible.Add(card);
            }

            if (eligible.Count < OfferCount)
                throw new InvalidOperationException(
                    $"EquipmentDB '{database.name}' has {eligible.Count} eligible cards for '{weapon.name}'; " +
                    $"modifier selection requires {OfferCount} distinct cards.");

            var offers = new ModifierOffer[OfferCount];
            for (int i = 0; i < offers.Length; i++)
            {
                float sample = random.Next01();
                if (float.IsNaN(sample) || sample < 0f || sample >= 1f)
                    throw new InvalidOperationException("Offer random source must return a value in [0, 1).");
                int selected = i + (int)(sample * (eligible.Count - i));
                EquipmentData card = eligible[selected];
                eligible[selected] = eligible[i];
                eligible[i] = card;
                offers[i] = new ModifierOffer(nextOfferId++, card, 0);
            }
            return Array.AsReadOnly(offers);
        }

        private bool CanOffer(EquipmentData card, WeaponData weapon)
        {
            if (card.Levels == null || card.Levels.Length == 0 || card.Levels[0] == null ||
                card.Levels[0].Modifiers.Length == 0 || card.HasDependencies) return false;

            foreach (EquipmentModifierApplication application in card.Levels[0].Modifiers)
            {
                // This offer pool covers the migrated, self-slot numeric cards only.
                // Multi-slot/dependency offers need their own eligibility rules later.
                if (application?.Modifier == null || application.Parameters == null ||
                    application.HasMultiSlotConfig || !weapon.Supports(application.ModifierId)) return false;

                bool registered = false;
                foreach (ModifierRegistry.EquipmentRegistration registration in registrations)
                {
                    if (registration.Id.Value == application.ModifierIdValue &&
                        registration.ParametersType == application.Parameters.GetType())
                    {
                        registered = true;
                        break;
                    }
                }
                if (!registered) return false;
            }
            return true;
        }
    }
}
