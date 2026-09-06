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
            else return;

            ModifierSelectionResult result = selection.Select(index);
            if (result.Succeeded)
                Debug.Log($"[ModifierSelection] selected={index + 1} equipmentHandle={result.EquipmentHandle.Value}", this);
            else
                Debug.LogWarning($"[ModifierSelection] selection failed: {result.Error}", this);
        }

        private void LogOffers()
        {
            if (selection.Offers.Count == 0) return;
            var message = new StringBuilder("[ModifierSelection] Choose with 1 / 2 / 3:");
            for (int i = 0; i < selection.Offers.Count; i++)
            {
                ModifierOffer offer = selection.Offers[i];
                message.Append($"\n{i + 1}: {offer.Equipment.Title} cardId={offer.EquipmentId} " +
                    $"level={offer.LevelIndex + 1} offerId={offer.OfferId}");
                foreach (var application in offer.Modifiers)
                    message.Append($"\n  modifier=0x{application.ModifierIdValue:X8} " +
                        $"{application.Parameters.GetType().Name} {JsonUtility.ToJson(application.Parameters)}");
            }
            Debug.Log(message.ToString(), this);
        }
    }
}
