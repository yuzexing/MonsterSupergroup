using System.Text;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModifierSelectionController))]
    public sealed class DebugModifierSelectionInput : MonoBehaviour
    {
        [SerializeField] private ModifierSelectionController selection;

        private void OnEnable()
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
            {
                enabled = false;
                return;
            }
            if (selection == null) selection = GetComponent<ModifierSelectionController>();
            selection.OffersChanged += LogOffers;
            LogOffers();
        }

        private void OnDisable()
        {
            if (selection != null) selection.OffersChanged -= LogOffers;
        }

        private void Update()
        {
            if (!Application.isFocused || selection == null || selection.Offers.Count == 0) return;
            int index;
            if (Input.GetKeyDown(KeyCode.Alpha1)) index = 0;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) index = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) index = 2;
            else if (selection.Stage == UpgradeSelectionStage.EquipmentTarget && Input.GetKeyDown(KeyCode.Alpha4)) index = 3;
            else return;

            ModifierSelectionResult result = selection.Select(index);
            if (result.Succeeded)
                Debug.Log($"[ModifierSelection] request submitted for option {index + 1}", this);
            else
                Debug.LogWarning($"[ModifierSelection] selection failed: {result.Error}", this);
        }

        private void LogOffers()
        {
            if (selection.Offers.Count == 0) return;
            var message = new StringBuilder($"[ModifierSelection] level={selection.EarnedLevel} stage={selection.Stage}; choose 1..{selection.Offers.Count}:");
            for (int i = 0; i < selection.Offers.Count; i++)
            {
                ModifierOffer offer = selection.Offers[i];
                message.Append($"\n{i + 1}: {offer.DisplayName} kind={offer.Kind} contentId={offer.ContentId} " +
                    $"level={offer.LevelIndex + 1} perkLevel={offer.PerkLevel} rarity={offer.Rarity} slot={offer.TargetSlotIndex} offerId={offer.OfferId}");
                foreach (var application in offer.Modifiers)
                    message.Append($"\n  modifier=0x{application.ModifierIdValue:X8} " +
                        $"{application.Parameters.GetType().Name} {JsonUtility.ToJson(application.Parameters)}");
            }
            Debug.Log(message.ToString(), this);
        }
    }
}
