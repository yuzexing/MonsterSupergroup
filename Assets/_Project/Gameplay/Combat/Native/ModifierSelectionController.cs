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
        private Func<bool> submitBack;
        private AstralShift.HellMaiden.Player.Attacks.WeaponBehaviour boundWeapon;

        public PlayerBuildRuntime BoundBuild { get; private set; }
        public IReadOnlyList<ModifierOffer> Offers { get; private set; } = Array.Empty<ModifierOffer>();
        public bool IsRequestPending { get; private set; }
        public UpgradeSelectionStage Stage { get; private set; }
        public int EarnedLevel { get; private set; }
        public bool CanGoBack => Stage == UpgradeSelectionStage.EquipmentTarget && submitBack != null;
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

        public void ReceiveOffers(IReadOnlyList<ModifierOffer> offers, Func<ulong, bool> submit,
            UpgradeSelectionStage stage = UpgradeSelectionStage.Reward, int earnedLevel = 0, Func<bool> back = null)
        {
            if (BoundBuild == null || !BoundBuild.IsBuildActive)
                throw new InvalidOperationException("An active owner build must be bound before presenting offers.");
            if (offers == null || offers.Count < 1 || offers.Count > (stage == UpgradeSelectionStage.EquipmentTarget ? 4 : 3))
                throw new ArgumentException("An upgrade offer must contain one to three cards, or up to four Equipment targets.", nameof(offers));
            submitSelection = submit ?? throw new ArgumentNullException(nameof(submit));
            submitBack = back;
            Stage = stage;
            EarnedLevel = earnedLevel;
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

        public ModifierSelectionResult Back()
        {
            if (!CanGoBack || IsRequestPending || BoundBuild == null || !BoundBuild.IsBuildActive ||
                BoundBuild.InitialWeapon != boundWeapon)
                return ModifierSelectionResult.Failure("No active target selection to return from.");
            IsRequestPending = true;
            OffersChanged?.Invoke();
            if (submitBack()) return ModifierSelectionResult.Success(default);
            CompleteRequest("The owner connection is not ready.");
            return ModifierSelectionResult.Failure(LastError);
        }

        public void ClearOffers()
        {
            bool changed = Offers.Count != 0 || IsRequestPending;
            Offers = Array.Empty<ModifierOffer>();
            submitSelection = null;
            submitBack = null;
            Stage = UpgradeSelectionStage.Reward;
            EarnedLevel = 0;
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
