using System;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	/// <summary>
	/// Stateless bridge between the legacy Enemy controller and the migrated combat state.
	/// It owns no health, death, version, or pending-kill state.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(CombatantBehaviour))]
	public sealed class EnemyCombatantBinding : MonoBehaviour
	{
		[SerializeField]
		private CombatantBehaviour combatant;

		public CombatantBehaviour Combatant
		{
			get
			{
				if (combatant == null)
				{
					combatant = GetComponent<CombatantBehaviour>();
				}

				return combatant;
			}
		}

		public int CurrentHealth => Combatant.CurrentHealth;

		public int MaxHealth => Combatant.MaxHealth;

		public bool IsAlive => Combatant.IsAlive;

		public void InitializeFromStats(EnemyStats stats)
		{
			if (stats == null)
			{
				throw new ArgumentNullException(nameof(stats));
			}

			Combatant.Initialize(stats.EffectiveMaxHealth);
		}

		private void Reset()
		{
			combatant = GetComponent<CombatantBehaviour>();
		}
	}
}
