using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Owner-local presentation and input intent. The authoritative adapter supplies offers.</summary>
    [DisallowMultipleComponent]
    public sealed class ModifierSelectionController : MonoBehaviour
    {
        private Func<ulong, bool> submitSelection;
        private AstralShift.HellMaiden.Player.Attacks.WeaponBehaviour boundWeapon;

        public PlayerBuildRuntime BoundBuild { get; private set; }
        public IReadOnlyList<ModifierOffer> Offers { get; private set; } = Array.Empty<ModifierOffer>();
        public bool IsRequestPending { get; private set; }
        public bool IsPresentationReady { get; private set; }
        public string LastError { get; private set; }
        public event Action OffersChanged;
        public event Action CancelRequested;
        public event Action PresentationReady;

        public void NotifyPresentationReady()
        {
            IsPresentationReady = true;
            PresentationReady?.Invoke();
        }

        public void CancelOffer()
        {
            IsPresentationReady = false;
            CancelRequested?.Invoke();
        }

        public void Bind(PlayerBuildRuntime build)
        {
            if (ReferenceEquals(BoundBuild, build)) return;
            Unbind();
            BoundBuild = build;
            boundWeapon = build != null ? build.InitialWeapon : null;
        }

        public void Unbind()
        {
            BoundBuild = null;
            boundWeapon = null;
            ClearOffers();
        }

        public void ReceiveOffers(IReadOnlyList<ModifierOffer> offers, Func<ulong, bool> submit)
        {
            if (BoundBuild == null || !BoundBuild.IsBuildActive)
                throw new InvalidOperationException("An active owner build must be bound before presenting offers.");
            if (offers == null || offers.Count < 1 || offers.Count > 3)
                throw new ArgumentException("An upgrade offer must contain one to three choices.", nameof(offers));
            submitSelection = submit ?? throw new ArgumentNullException(nameof(submit));
            var copy = new ModifierOffer[offers.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = offers[i];
            Offers = Array.AsReadOnly(copy);
            boundWeapon = BoundBuild.InitialWeapon;
            IsRequestPending = false;
            LastError = null;
            OffersChanged?.Invoke();
        }

        public ModifierSelectionResult Select(int index) =>
            (uint)index < Offers.Count ? SelectOffer(Offers[index].OfferId) :
                ModifierSelectionResult.Failure("Invalid option index.");

        public ModifierSelectionResult SelectOffer(ulong offerId)
        {
            if (BoundBuild == null || !BoundBuild.IsBuildActive ||
                BoundBuild.InitialWeapon != boundWeapon || IsRequestPending || submitSelection == null)
                return ModifierSelectionResult.Failure("The offer has expired or a request is already pending.");
            bool found = false;
            for (int i = 0; i < Offers.Count; i++) found |= Offers[i].OfferId == offerId;
            if (!found) return ModifierSelectionResult.Failure("The option is not in the current offer.");

            IsRequestPending = true;
            OffersChanged?.Invoke();
            if (!submitSelection(offerId))
            {
                CompleteRequest("The owner connection is not ready.");
                return ModifierSelectionResult.Failure(LastError);
            }
            // Submission is not application: keep the menu until the server acknowledges it.
            return ModifierSelectionResult.Success(default);
        }

        public void CompleteRequest(string error = null)
        {
            IsRequestPending = false;
            LastError = error;
            OffersChanged?.Invoke();
        }

        public void ClearOffers()
        {
            bool changed = Offers.Count != 0 || IsRequestPending;
            Offers = Array.Empty<ModifierOffer>();
            submitSelection = null;
            IsRequestPending = false;
            LastError = null;
            if (changed) OffersChanged?.Invoke();
        }

        private void Update()
        {
            if (!ReferenceEquals(BoundBuild, null) && (BoundBuild == null ||
                !BoundBuild.IsBuildActive || BoundBuild.InitialWeapon != boundWeapon))
                ClearOffers();
        }

        private void OnDisable() => Unbind();
    }
}
