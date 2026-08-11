using System;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class StatusUpdateDriver : MonoBehaviour
    {
        [SerializeField] private CombatantBehaviour combatant;

        public CombatantBehaviour Combatant => combatant;

        public void Configure(CombatantBehaviour target)
        {
            combatant = target ?? throw new ArgumentNullException(nameof(target));
        }

        public void Tick(float deltaSeconds)
        {
            if (combatant == null)
            {
                throw new InvalidOperationException(
                    "StatusUpdateDriver requires a CombatantBehaviour reference. Call Configure or assign it in the Inspector.");
            }

            combatant.AdvanceStatuses(deltaSeconds);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }
    }
}
