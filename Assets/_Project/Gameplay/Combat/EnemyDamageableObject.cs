using System;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public class EnemyDamageableObject : Interactor
	{
		public StatusBar statusBar;

		[SerializeField]
		private float maxHealth;

		private float currentHealth;

		public Action OnKilled;

		public Action OnHit;

		public bool isImmortal = true;

		[SerializeField]
		private bool mustBeFacing;

		[SerializeField]
		private Collider2D collider;

		[SerializeField]
		private bool blocksDamage;

		public float MaxHealth
		{
			get
			{
				return maxHealth;
			}
			set
			{
				maxHealth = value;
			}
		}

		public bool IsDead { get; private set; }

		public bool MustBeFacing => mustBeFacing;

		public bool BlocksDamage => blocksDamage;

		public void Awake()
		{
			statusBar?.InitializeBar(maxHealth);
			currentHealth = maxHealth;
		}

		public void DamageObject(int value)
		{
			if (!IsDead && !isImmortal)
			{
				currentHealth = Mathf.Clamp(currentHealth - (float)value, 0f, maxHealth);
				statusBar?.StatusChange(currentHealth);
				OnHit?.Invoke();
				if (currentHealth <= 0f)
				{
					IsDead = true;
					EnableCollider(enable: false);
					OnKilled?.Invoke();
				}
			}
		}

		public void ReviveObject()
		{
			IsDead = false;
			EnableCollider(enable: true);
			currentHealth = maxHealth;
		}

		public void HideHealthbar()
		{
			statusBar?.Hide();
		}

		private void EnableCollider(bool enable)
		{
			if ((bool)collider)
			{
				collider.enabled = enable;
			}
		}
	}
}
