using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Unity;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    public sealed class ModifierSelectionController : MonoBehaviour
    {
        private static readonly IReadOnlyList<ModifierOffer> Empty = Array.Empty<ModifierOffer>();
        private EquipmentModifierOfferProvider provider;
        private WeaponBehaviour offeredWeapon;
        private int lastOfferedWeaponId;

        public PlayerBuildRuntime BoundBuild { get; private set; }
        public IReadOnlyList<ModifierOffer> Offers { get; private set; } = Empty;
        public event Action OffersChanged;

        public void Initialize(IRandomSource random)
        {
            if (provider != null) throw new InvalidOperationException("Offer provider is already initialized.");
            provider = new EquipmentModifierOfferProvider(random);
        }

        public void Bind(PlayerBuildRuntime build)
        {
            if (ReferenceEquals(BoundBuild, build)) return;
            Unbind();
            BoundBuild = build;
            RefreshBuild();
        }

        public void Unbind()
        {
            BoundBuild = null;
            offeredWeapon = null;
            ClearOffers();
        }

        private void Update() => RefreshBuild();
        private void OnDisable() => Unbind();

        private void RefreshBuild()
        {
            WeaponBehaviour current = BoundBuild != null && BoundBuild.IsBuildActive
                ? BoundBuild.InitialWeapon : null;
            if (ReferenceEquals(current, offeredWeapon)) return;

            offeredWeapon = current;
            ClearOffers();
            if (current == null) return;

            // Keep only a scalar token across Unbind: rebinding the same build
            // cannot grant another round, and no old player reference is retained.
            int weaponId = current.GetInstanceID();
            if (weaponId == lastOfferedWeaponId) return;
            lastOfferedWeaponId = weaponId;

            if (provider == null) Initialize(new UnityRandomSource());
            try
            {
                Offers = provider.Generate(BoundBuild.BuildDatabase?.EquipmentDB, current.WeaponData);
            }
            catch (Exception exception)
            {
                // Remember this build even on failure: do not retry/log every frame.
                Debug.LogError($"Modifier selection: {exception.Message}", this);
                return;
            }
            OffersChanged?.Invoke();
        }

        public ModifierSelectionResult Select(int index)
        {
            // Capture before refreshing so a delayed input cannot select a new build's offer.
            ModifierOffer offer = (uint)index < Offers.Count ? Offers[index] : null;
            return SelectCurrent(offer);
        }

        public ModifierSelectionResult SelectOffer(ulong offerId)
        {
            ModifierOffer offer = null;
            for (int i = 0; i < Offers.Count; i++)
                if (Offers[i].OfferId == offerId) { offer = Offers[i]; break; }
            return SelectCurrent(offer);
        }

        private ModifierSelectionResult SelectCurrent(ModifierOffer offer)
        {
            RefreshBuild();
            if (offer == null || BoundBuild == null || offeredWeapon == null || !Contains(offer))
                return ModifierSelectionResult.Failure("The selection is invalid or its player build has expired.");

            PlayerBuildEquipmentHandle handle;
            try
            {
                handle = BoundBuild.AddEquipment(offeredWeapon, offer.Equipment, offer.LevelIndex);
            }
            catch (Exception exception)
            {
                return ModifierSelectionResult.Failure(exception.Message);
            }
            ClearOffers();
            return ModifierSelectionResult.Success(handle);
        }

        private bool Contains(ModifierOffer offer)
        {
            for (int i = 0; i < Offers.Count; i++)
                if (ReferenceEquals(Offers[i], offer)) return true;
            return false;
        }

        private void ClearOffers()
        {
            if (Offers.Count == 0) return;
            Offers = Empty;
            OffersChanged?.Invoke();
        }
    }
}
