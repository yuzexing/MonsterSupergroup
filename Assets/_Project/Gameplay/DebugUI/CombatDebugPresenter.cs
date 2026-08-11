using System;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.DebugUI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Monster Supergroup/Debug/Combat Debug Presenter")]
    public sealed class CombatDebugPresenter : MonoBehaviour
    {
        [SerializeField] private VerticalSliceCombatController controller;
        [SerializeField] private CombatantBehaviour player;
        [SerializeField] private CombatantBehaviour enemy;
        [SerializeField] private WeaponRuntimeBehaviour weapon;

        public VerticalSliceCombatController Controller => controller;
        public CombatantBehaviour Player => player;
        public CombatantBehaviour Enemy => enemy;
        public WeaponRuntimeBehaviour Weapon => weapon;

        public void Configure(
            VerticalSliceCombatController combatController,
            CombatantBehaviour playerCombatant,
            CombatantBehaviour enemyCombatant,
            WeaponRuntimeBehaviour playerWeapon)
        {
            if (combatController == null)
            {
                throw new ArgumentNullException(nameof(combatController));
            }

            if (playerCombatant == null)
            {
                throw new ArgumentNullException(nameof(playerCombatant));
            }

            if (enemyCombatant == null)
            {
                throw new ArgumentNullException(nameof(enemyCombatant));
            }

            if (playerWeapon == null)
            {
                throw new ArgumentNullException(nameof(playerWeapon));
            }

            controller = combatController;
            player = playerCombatant;
            enemy = enemyCombatant;
            weapon = playerWeapon;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            const float Width = 360f;
            GUILayout.BeginArea(new Rect(16f, 16f, Width, 420f), "GAS Vertical Slice", GUI.skin.window);

            if (controller == null || player == null || enemy == null || weapon == null)
            {
                GUILayout.Label("Debug presenter is not configured.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"Player HP: {player.CurrentHealth} / {player.MaxHealth}");
            GUILayout.Label($"Enemy HP: {enemy.CurrentHealth} / {enemy.MaxHealth}");
            GUILayout.Space(4f);
            GUILayout.Label($"Enemy direct damage taken: {enemy.DirectDamageTaken}");
            GUILayout.Label($"Enemy Burn ticks: {enemy.StatusTickCount}");
            GUILayout.Label($"Enemy Burn damage taken: {enemy.StatusDamageTaken}");
            GUILayout.Label($"Last direct hit: {controller.LastDamage.Value}" +
                            (controller.LastDamage.IsCritical ? " (Critical)" : string.Empty));
            GUILayout.Space(4f);

            if (weapon.IsInitialized && weapon.Stats != null)
            {
                GUILayout.Label($"Weapon damage: {weapon.Stats.DamageValue}");
                GUILayout.Label($"Weapon speed: {weapon.Stats.SpeedValue:0.###}");
                GUILayout.Label($"Attack interval: {controller.AttackInterval:0.###} s");
            }
            else
            {
                GUILayout.Label("Weapon is not initialized.");
            }

            GUILayout.Label($"Equipment modifier count: {weapon.ModifierCount}");
            GUILayout.Label($"Auto attack: {controller.AutoAttack}");
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Attack"))
            {
                controller.AttackOnce();
            }

            if (GUILayout.Button("Reset"))
            {
                controller.ResetEncounter();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
#endif
    }
}
