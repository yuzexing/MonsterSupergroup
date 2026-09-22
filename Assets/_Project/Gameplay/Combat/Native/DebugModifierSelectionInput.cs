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
            if (!MonsterSupergroup.Builds.BuildFeatures.DevelopmentToolsAllowed || (!Application.isEditor && !Debug.isDebugBuild))
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
