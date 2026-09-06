using MonsterSupergroup.Gameplay.Combat;
using TMPro;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.UI
{
    [DisallowMultipleComponent]
    public sealed class PlayerHealthHUD : MonoBehaviour
    {
        [SerializeField] private OverflowBar healthBar;
        [SerializeField] private TMP_Text currentHealthText;
        [SerializeField] private TMP_Text maxHealthText;

        public CombatantBehaviour BoundCombatant { get; private set; }

        public void Bind(CombatantBehaviour combatant)
        {
            Unbind();
            BoundCombatant = combatant;
            if (combatant == null)
            {
                return;
            }

            if (isActiveAndEnabled)
            {
                Subscribe();
            }
            RenderImmediate(combatant.CurrentHealth, combatant.MaxHealth);
        }

        public void Unbind()
        {
            Unsubscribe();
            BoundCombatant = null;
            ClearDisplay();
        }

        private void OnEnable()
        {
            if (BoundCombatant != null)
            {
                Subscribe();
                RenderImmediate(BoundCombatant.CurrentHealth, BoundCombatant.MaxHealth);
            }
            else
            {
                ClearDisplay();
            }
        }

        private void Subscribe()
        {
            BoundCombatant.HealthChanged -= Render;
            BoundCombatant.HealthChanged += Render;
        }

        private void Unsubscribe()
        {
            // Unity's destroyed-object null comparison must not skip removing
            // a managed event handler from a former player instance.
            if (!ReferenceEquals(BoundCombatant, null))
            {
                BoundCombatant.HealthChanged -= Render;
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearDisplay();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Render(int current, int maximum)
        {
            healthBar.SetMaxValue(maximum);
            healthBar.StatusChange(current);
            RenderNumbers(current, maximum);
        }

        private void RenderImmediate(int current, int maximum)
        {
            healthBar.SetValueImmediate(current, maximum);
            RenderNumbers(current, maximum);
        }

        private void RenderNumbers(int current, int maximum)
        {
            currentHealthText.text = current.ToString();
            maxHealthText.text = maximum.ToString();
        }

        private void ClearDisplay()
        {
            if (healthBar != null)
            {
                healthBar.ClearPresentation();
            }
            if (currentHealthText != null)
            {
                currentHealthText.text = string.Empty;
            }
            if (maxHealthText != null)
            {
                maxHealthText.text = string.Empty;
            }
        }
    }
}
