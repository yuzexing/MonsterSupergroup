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

        public string Diagnostic { get; private set; }

        public EquipmentModifierOfferProvider(IRandomSource random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            registrations = GeneratedModifierRegistry.Create().EquipmentRegistrations;
        }

        public IReadOnlyList<ModifierOffer> Generate(PlayerBuildRuntime build)
        {
            Diagnostic = null;
            if (build == null || !build.IsBuildActive)
                throw new InvalidOperationException("Modifier selection requires an active player build.");
            EquipmentDB database = build.BuildDatabase?.EquipmentDB;
            if (database == null || database.Equipments == null)
                throw new InvalidOperationException("Modifier selection requires a configured EquipmentDB.");

            int targetSlot = FindInitialWeaponSlot(build);
            var eligible = new List<ModifierOffer>();
            var cardIds = new HashSet<uint>();
            IReadOnlyList<PlayerBuildEquipmentState> states = build.GetEquipmentStates();
            foreach (EquipmentData card in database.Equipments)
            {
                if (card == null) continue;
                if (!cardIds.Add(card.ID))
                    throw new InvalidOperationException($"EquipmentDB contains duplicate card ID {card.ID}.");
                PlayerBuildEquipmentState owned = FindEquipment(states, targetSlot, card.ID);
                int nextLevel = owned.Handle.IsValid ? owned.LevelIndex + 1 : 0;
                if (!CanOffer(card, build.InitialWeapon.WeaponData, nextLevel)) continue;
                var option = new ModifierOffer(0, card, nextLevel, targetSlot, owned.Handle);
                if (IsEligible(build, option)) eligible.Add(option);
            }

            if (eligible.Count == 0)
            {
                Diagnostic = "Player build has 0 eligible equipment upgrades; selection is deferred.";
                return Array.Empty<ModifierOffer>();
            }

            var offers = new ModifierOffer[Math.Min(OfferCount, eligible.Count)];
            for (int i = 0; i < offers.Length; i++)
            {
                int selected = SampleIndex(i, eligible.Count);
                ModifierOffer choice = eligible[selected];
                eligible[selected] = eligible[i];
                eligible[i] = choice;
                offers[i] = new ModifierOffer(nextOfferId++, choice.Equipment, choice.LevelIndex,
                    choice.TargetSlotIndex, choice.ExistingEquipmentHandle);
            }
            return Array.AsReadOnly(offers);
        }

        public bool IsEligible(PlayerBuildRuntime build, ModifierOffer offer)
        {
            if (build == null || !build.IsBuildActive || offer == null ||
                (uint)offer.TargetSlotIndex >= PlayerBuildRuntime.HandSlotCount ||
                build.GetWeaponAtSlot(offer.TargetSlotIndex) != build.InitialWeapon)
                return false;
            EquipmentDB database = build.BuildDatabase?.EquipmentDB;
            if (database?.Equipments == null ||
                Array.IndexOf(database.Equipments, offer.Equipment) < 0 ||
                !CanOffer(offer.Equipment, build.InitialWeapon.WeaponData, offer.LevelIndex))
                return false;

            IReadOnlyList<PlayerBuildEquipmentState> states = build.GetEquipmentStates();
            PlayerBuildEquipmentState owned = FindEquipment(states, offer.TargetSlotIndex, offer.EquipmentId);
            if (owned.Handle.IsValid)
                return owned.Handle.Equals(offer.ExistingEquipmentHandle) &&
                    offer.LevelIndex == owned.LevelIndex + 1;
            if (offer.ExistingEquipmentHandle.IsValid || offer.LevelIndex != 0) return false;
            int usedSlots = 0;
            for (int i = 0; i < states.Count; i++)
                if (states[i].SourceSlotIndex == offer.TargetSlotIndex) usedSlots++;
            return usedSlots < PlayerBuildRuntime.MaxEquipmentPerSlot;
        }

        private static int FindInitialWeaponSlot(PlayerBuildRuntime build)
        {
            for (int slot = 0; slot < PlayerBuildRuntime.HandSlotCount; slot++)
                if (build.GetWeaponAtSlot(slot) == build.InitialWeapon) return slot;
            throw new InvalidOperationException("Initial weapon is absent from the player build slots.");
        }

        private static PlayerBuildEquipmentState FindEquipment(
            IReadOnlyList<PlayerBuildEquipmentState> states, int slot, uint cardId)
        {
            for (int i = 0; i < states.Count; i++)
                if (states[i].SourceSlotIndex == slot && states[i].EquipmentId == cardId)
                    return states[i];
            return default;
        }

        public IReadOnlyList<ModifierOffer> Generate(EquipmentDB database, WeaponData weapon)
        {
            Diagnostic = null;
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

            if (eligible.Count == 0)
            {
                Diagnostic = $"EquipmentDB '{database.name}' has 0 eligible cards for '{weapon.name}'; " +
                    "selection is deferred.";
                return Array.Empty<ModifierOffer>();
            }

            var offers = new ModifierOffer[Math.Min(OfferCount, eligible.Count)];
            for (int i = 0; i < offers.Length; i++)
            {
                int selected = SampleIndex(i, eligible.Count);
                EquipmentData card = eligible[selected];
                eligible[selected] = eligible[i];
                eligible[i] = card;
                offers[i] = new ModifierOffer(nextOfferId++, card, 0);
            }
            return Array.AsReadOnly(offers);
        }

        private int SampleIndex(int start, int count)
        {
            float sample = random.Next01();
            if (float.IsNaN(sample) || sample < 0f || sample >= 1f)
                throw new InvalidOperationException("Offer random source must return a value in [0, 1).");
            return start + (int)(sample * (count - start));
        }

        private bool CanOffer(EquipmentData card, WeaponData weapon, int levelIndex = 0)
        {
            if (card.Levels == null || (uint)levelIndex >= card.Levels.Length || card.Levels[levelIndex] == null ||
                card.Levels[levelIndex].Modifiers.Length == 0 || card.HasDependencies) return false;

            foreach (EquipmentModifierApplication application in card.Levels[levelIndex].Modifiers)
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
