using System;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Local
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatantBehaviour))]
    public sealed class LocalEnemyDeathBehaviour : MonoBehaviour
    {
        [SerializeField] private CombatantBehaviour combatant;
        [SerializeField] private LocalEnemyChase chase;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField, Min(0f)] private float destroyDelay = 0.1f;

        private bool handled;

        public void Configure(
            CombatantBehaviour targetCombatant,
            LocalEnemyChase enemyChase,
            Collider2D enemyCollider)
        {
            combatant = targetCombatant ?? throw new ArgumentNullException(nameof(targetCombatant));
            chase = enemyChase ?? throw new ArgumentNullException(nameof(enemyChase));
            bodyCollider = enemyCollider ?? throw new ArgumentNullException(nameof(enemyCollider));
        }

        private void Awake()
        {
            if (combatant == null)
            {
                combatant = GetComponent<CombatantBehaviour>();
            }
        }

        private void OnEnable()
        {
            if (combatant != null)
            {
                combatant.HealthChanged += HandleHealthChanged;
            }
        }

        private void OnDisable()
        {
            if (combatant != null)
            {
                combatant.HealthChanged -= HandleHealthChanged;
            }
        }

        private void HandleHealthChanged(int current, int maximum)
        {
            if (current > 0 || handled)
            {
                return;
            }

            handled = true;
            if (chase != null)
            {
                chase.enabled = false;
            }

            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            Destroy(gameObject, destroyDelay);
        }
    }
}
