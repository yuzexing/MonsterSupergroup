using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.UI
{
    [DisallowMultipleComponent]
    public sealed class CombatHUDController : MonoBehaviour
    {
        [SerializeField] private CanvasGroup hudGroup;
        [SerializeField] private PlayerHealthHUD healthHUD;

        public CombatantBehaviour BoundCombatant => healthHUD != null
            ? healthHUD.BoundCombatant
            : null;

        public void Bind(CombatantBehaviour combatant)
        {
            Unbind();
            if (combatant == null)
            {
                return;
            }

            healthHUD.Bind(combatant);
            Show();
        }

        public void Unbind()
        {
            if (healthHUD != null)
            {
                healthHUD.Unbind();
            }
            Hide();
        }

        public void Show()
        {
            SetVisible(BoundCombatant != null);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (hudGroup != null)
            {
                hudGroup.alpha = visible ? 1f : 0f;
                // The combat HUD is display-only. Menus are sibling UI flows.
                hudGroup.interactable = false;
                hudGroup.blocksRaycasts = false;
            }
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
