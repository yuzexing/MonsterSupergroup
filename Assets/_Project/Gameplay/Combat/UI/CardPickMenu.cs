using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.Gameplay.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.UI
{
    /// <summary>Local presentation of an owner's server-issued offer.</summary>
    [DisallowMultipleComponent]
    public sealed class CardPickMenu : MonoBehaviour
    {
        [SerializeField] private CanvasGroup menuGroup;
        [SerializeField] private Button[] optionButtons = new Button[4];
        [SerializeField] private TMP_Text[] optionTitles = new TMP_Text[4];
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Text heading;

        private readonly ulong[] displayedOfferIds = new ulong[4];

        public ModifierSelectionController BoundSelection { get; private set; }
        public bool IsOpen { get; private set; }
        public bool IsPresentationSuppressed { get; private set; }
        private GameObject suppressedFocus;

        public void SetPresentationSuppressed(bool suppressed)
        {
            if (IsPresentationSuppressed == suppressed) return;
            if (suppressed && EventSystem.current != null)
                suppressedFocus = EventSystem.current.currentSelectedGameObject;
            IsPresentationSuppressed = suppressed;
            ApplyPresentation();
            if (!suppressed && IsOpen && EventSystem.current != null)
            {
                var previous = suppressedFocus != null ? suppressedFocus.GetComponent<Selectable>() : null;
                if (previous != null && previous.transform.IsChildOf(transform) && previous.IsActive() && previous.IsInteractable())
                    previous.Select();
                else if (!BoundSelection.IsRequestPending) optionButtons[0].Select();
            }
            if (!suppressed) suppressedFocus = null;
        }

        private void ApplyPresentation()
        {
            if (menuGroup == null) return;
            bool visible = IsOpen && !IsPresentationSuppressed;
            menuGroup.alpha = visible ? 1f : 0f;
            menuGroup.blocksRaycasts = visible;
            menuGroup.interactable = visible && BoundSelection != null && !BoundSelection.IsRequestPending;
        }

        private void OnEnable() => GameLocalization.Changed += Refresh;
        private void Awake()
        {
            optionButtons[0].onClick.AddListener(SelectFirst);
            optionButtons[1].onClick.AddListener(SelectSecond);
            optionButtons[2].onClick.AddListener(SelectThird);
            if (optionButtons.Length > 3) optionButtons[3].onClick.AddListener(SelectFourth);
            if (backButton != null) backButton.onClick.AddListener(Back);
            Hide();
        }

        public void Bind(ModifierSelectionController selection)
        {
            if (ReferenceEquals(BoundSelection, selection)) return;
            Unbind();
            BoundSelection = selection;
            if (BoundSelection == null) return;
            BoundSelection.OffersChanged += Refresh;
            BoundSelection.NotifyPresentationReady();
            Refresh();
        }

        public void Unbind()
        {
            if (!ReferenceEquals(BoundSelection, null))
            {
                BoundSelection.CancelOffer();
                BoundSelection.OffersChanged -= Refresh;
            }
            BoundSelection = null;
            Hide();
        }

        private void Refresh()
        {
            if (BoundSelection == null || BoundSelection.Offers.Count == 0)
            {
                Hide();
                return;
            }

            bool wasOpen = IsOpen;
            bool targets = BoundSelection.Stage == UpgradeSelectionStage.EquipmentTarget;
            if (GameLocalization.TMPFont != null)
            {
                if (heading != null) heading.font = GameLocalization.TMPFont;
                foreach (var label in optionTitles) if (label != null) label.font = GameLocalization.TMPFont;
            }
            if (heading != null) heading.text = MenuLocalization.Get("ui.card.heading", BoundSelection.EarnedLevel,
                targets ? MenuLocalization.Get("ui.card.choose_weapon", BoundSelection.Offers[0].DisplayName) : MenuLocalization.Get("ui.reward." + BoundSelection.Offers[0].Kind.ToString().ToLowerInvariant()));
            if (backButton != null)
            {
                var backText = backButton.GetComponentInChildren<TMP_Text>();
                if (backText != null) { backText.text = MenuLocalization.Get("返回"); if (GameLocalization.TMPFont != null) backText.font = GameLocalization.TMPFont; }
                backButton.gameObject.SetActive(BoundSelection.CanGoBack);
                backButton.interactable = !BoundSelection.IsRequestPending;
            }
            for (int i = 0; i < optionButtons.Length; i++)
            {
                bool visible = i < BoundSelection.Offers.Count;
                optionButtons[i].gameObject.SetActive(visible);
                // Center only the issued choices; the fourth button belongs exclusively to target selection.
                var rectangle = (RectTransform)optionButtons[i].transform;
                float width = BoundSelection.Offers.Count == 4 ? 270f : 320f;
                rectangle.sizeDelta = new Vector2(width, 180f);
                rectangle.anchoredPosition = new Vector2((i - (BoundSelection.Offers.Count - 1) * 0.5f) * (width + 24f), 0);
                if (!visible)
                {
                    displayedOfferIds[i] = 0;
                    optionTitles[i].text = string.Empty;
                    optionButtons[i].interactable = false;
                    continue;
                }
                ModifierOffer offer = BoundSelection.Offers[i];
                displayedOfferIds[i] = offer.OfferId;
                string description = offer.DisplayName;
                if (targets)
                {
                    var weapon = BoundSelection.BoundBuild.GetWeaponAtSlot(offer.TargetSlotIndex);
                    description = $"{weapon.WeaponData.GetTitle()}\n" +
                        (offer.LevelIndex == 0 ? MenuLocalization.Get("ui.card.add_equipment") : MenuLocalization.Get("ui.card.upgrade_level", offer.LevelIndex + 1));
                }
                else if (offer.Kind == UpgradeRewardKind.Perk)
                    description += "\n" + ContentText.Rarity(offer.Rarity);
                else if (offer.Kind == UpgradeRewardKind.Equipment)
                    description += "\n" + MenuLocalization.Get("ui.card.choose_target_next");
                string effect = offer.Kind == UpgradeRewardKind.Weapon ? offer.Weapon.GetDescription() :
                    offer.Kind == UpgradeRewardKind.Perk ? offer.Perk.GetDescription(offer.Rarity) :
                    offer.Equipment.GetDescription((uint)Mathf.Max(0, offer.LevelIndex));
                optionTitles[i].enableAutoSizing = true;
                optionTitles[i].fontSizeMin = 16; optionTitles[i].fontSizeMax = 24;
                optionTitles[i].text = $"{i + 1}. {description}\n\n{effect}";
                optionButtons[i].interactable = !BoundSelection.IsRequestPending;
            }
            IsOpen = true;
            ApplyPresentation();
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            // A reply can arrive after the overlay closed while the request was still pending.
            if (!IsPresentationSuppressed && !BoundSelection.IsRequestPending && events != null &&
                (!wasOpen || selected == null || (selected.transform.IsChildOf(transform) && !selected.activeInHierarchy)))
                events.SetSelectedGameObject(optionButtons[0].gameObject);
        }

        private void SelectFirst() => Submit(0);
        private void SelectSecond() => Submit(1);
        private void SelectThird() => Submit(2);
        private void SelectFourth() => Submit(3);
        private void Back() { if (!IsPresentationSuppressed) BoundSelection?.Back(); }

        private void Submit(int index)
        {
            if (IsPresentationSuppressed || !IsOpen || BoundSelection == null || BoundSelection.IsRequestPending) return;
            // Keep the menu until the authoritative response clears/advances the offer.
            ModifierSelectionResult result = BoundSelection.SelectOffer(displayedOfferIds[index]);
            if (!result.Succeeded)
                Debug.LogWarning($"Upgrade selection: {result.Error}", this);
        }

        private void Hide()
        {
            IsOpen = false;
            if (menuGroup != null)
            {
                menuGroup.alpha = 0f;
                menuGroup.interactable = false;
                menuGroup.blocksRaycasts = false;
            }
            if (backButton != null) backButton.gameObject.SetActive(false);
            if (heading != null) heading.text = string.Empty;
            for (int i = 0; i < optionButtons.Length; i++)
            {
                displayedOfferIds[i] = 0;
                if (optionButtons[i] != null) optionButtons[i].interactable = false;
                if (optionTitles[i] != null) optionTitles[i].text = string.Empty;
            }
            EventSystem events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null &&
                events.currentSelectedGameObject.transform.IsChildOf(transform))
                events.SetSelectedGameObject(null);
        }

        private void OnDisable() { GameLocalization.Changed -= Refresh; Unbind(); }

        private void OnDestroy()
        {
            Unbind();
            optionButtons[0].onClick.RemoveListener(SelectFirst);
            optionButtons[1].onClick.RemoveListener(SelectSecond);
            optionButtons[2].onClick.RemoveListener(SelectThird);
            if (optionButtons.Length > 3) optionButtons[3].onClick.RemoveListener(SelectFourth);
            if (backButton != null) backButton.onClick.RemoveListener(Back);
        }
    }
}
