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
        [SerializeField] private Button[] optionButtons = new Button[3];
        [SerializeField] private TMP_Text[] optionTitles = new TMP_Text[3];

        private readonly ulong[] displayedOfferIds = new ulong[3];

        public ModifierSelectionController BoundSelection { get; private set; }
        public bool IsOpen { get; private set; }

        private void Awake()
        {
            optionButtons[0].onClick.AddListener(SelectFirst);
            optionButtons[1].onClick.AddListener(SelectSecond);
            optionButtons[2].onClick.AddListener(SelectThird);
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
            for (int i = 0; i < 3; i++)
            {
                bool visible = i < BoundSelection.Offers.Count;
                optionButtons[i].gameObject.SetActive(visible);
                if (!visible)
                {
                    displayedOfferIds[i] = 0;
                    optionTitles[i].text = string.Empty;
                    optionButtons[i].interactable = false;
                    continue;
                }
                ModifierOffer offer = BoundSelection.Offers[i];
                displayedOfferIds[i] = offer.OfferId;
                optionTitles[i].text = offer.DisplayName;
                optionButtons[i].interactable = !BoundSelection.IsRequestPending;
            }
            IsOpen = true;
            menuGroup.alpha = 1f;
            menuGroup.blocksRaycasts = true;
            menuGroup.interactable = !BoundSelection.IsRequestPending;
            if (!wasOpen && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(optionButtons[0].gameObject);
        }

        private void SelectFirst() => Submit(0);
        private void SelectSecond() => Submit(1);
        private void SelectThird() => Submit(2);

        private void Submit(int index)
        {
            if (!IsOpen || BoundSelection == null || BoundSelection.IsRequestPending) return;
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
            for (int i = 0; i < 3; i++)
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

        private void OnDisable() => Unbind();

        private void OnDestroy()
        {
            Unbind();
            optionButtons[0].onClick.RemoveListener(SelectFirst);
            optionButtons[1].onClick.RemoveListener(SelectSecond);
            optionButtons[2].onClick.RemoveListener(SelectThird);
        }
    }
}
