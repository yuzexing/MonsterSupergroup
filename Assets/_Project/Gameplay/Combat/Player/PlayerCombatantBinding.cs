using AstralShift.HellMaiden.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.Player
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(CombatantBehaviour))]
	public sealed class PlayerCombatantBinding : MonoBehaviour
	{
		[SerializeField]
		private CombatantBehaviour combatant;

		[SerializeField]
		private PlayerMovement playerMovement;

		[SerializeField]
		private bool acceptsLocalMutations = true;

		private bool _combatantSubscribed;

		private bool _statsSubscribed;

		private bool _suppressLegacyEvents;

		private bool _suppressHealthDeltaEvents;

		private bool _legacyMirrorInitialized;

		private int _lastHealth;

		private int _lastMaximumHealth;

		public CombatantBehaviour Combatant => combatant;

		public PlayerMovement PlayerMovement => playerMovement;

		public PlayerStats PlayerStats => playerMovement != null
			? playerMovement.PlayerStats
			: null;

		public bool AcceptsLocalMutations => acceptsLocalMutations;

		public int CurrentHealth => combatant != null ? combatant.CurrentHealth : 0;

		public int MaximumHealth => combatant != null ? combatant.MaxHealth : 0;

		public bool IsAlive => combatant != null && combatant.IsAlive;

		private void Awake()
		{
			ResolveReferences();
			Subscribe();
			SyncLegacyMirror(emitLegacyEvents: false);
		}

		public void Configure(
			PlayerMovement movement,
			CombatantBehaviour playerCombatant)
		{
			Unsubscribe();
			playerMovement = movement != null
				? movement
				: throw new System.ArgumentNullException(nameof(movement));
			combatant = playerCombatant != null
				? playerCombatant
				: throw new System.ArgumentNullException(nameof(playerCombatant));
			Subscribe();
			SyncLegacyMirror(emitLegacyEvents: false);
		}

		public void InitializeFromPlayerStats()
		{
			ResolveReferences();
			Subscribe();
			if (PlayerStats == null || PlayerStats.MaxHP < 1)
			{
				throw new System.InvalidOperationException(
					"PlayerCombatantBinding requires initialized PlayerStats with positive MaxHP.");
			}

			_suppressLegacyEvents = true;
			try
			{
				combatant.Initialize(PlayerStats.MaxHP);
			}
			finally
			{
				_suppressLegacyEvents = false;
			}

			SyncLegacyMirror(emitLegacyEvents: false);
		}

		public void SetLocalMutationAuthority(bool value)
		{
			acceptsLocalMutations = value;
		}

		public int ApplyDamage(int value)
		{
			if (!acceptsLocalMutations || value <= 0 || combatant == null)
			{
				return 0;
			}

			return combatant.ReceiveDamage(new DamageInfo(0u, value, false)).Value;
		}

		public int RestoreHealth(int value)
		{
			if (!acceptsLocalMutations || value <= 0 || combatant == null)
			{
				return 0;
			}

			return combatant.RestoreHealth(value);
		}

		public bool SetMaximumHealthPreservingMissingHealth(int maximumHealth)
		{
			return acceptsLocalMutations && combatant != null &&
				combatant.SetMaximumHealthPreservingMissingHealth(maximumHealth);
		}

		private void ResolveReferences()
		{
			if (combatant == null)
			{
				combatant = GetComponent<CombatantBehaviour>();
			}

			if (playerMovement == null)
			{
				playerMovement = GetComponent<PlayerMovement>();
			}
		}

		private void Subscribe()
		{
			if (combatant != null && !_combatantSubscribed)
			{
				combatant.HealthChanged += HandleHealthChanged;
				_combatantSubscribed = true;
			}

			if (PlayerStats != null && !_statsSubscribed)
			{
				PlayerStats.MaximumHealthChanged += HandleMaximumHealthChanged;
				_statsSubscribed = true;
			}
		}

		private void Unsubscribe()
		{
			if (_combatantSubscribed && combatant != null)
			{
				combatant.HealthChanged -= HandleHealthChanged;
			}

			if (_statsSubscribed && PlayerStats != null)
			{
				PlayerStats.MaximumHealthChanged -= HandleMaximumHealthChanged;
			}

			_combatantSubscribed = false;
			_statsSubscribed = false;
		}

		private void HandleMaximumHealthChanged(int maximumHealth)
		{
			_suppressHealthDeltaEvents = true;
			try
			{
				SetMaximumHealthPreservingMissingHealth(maximumHealth);
			}
			finally
			{
				_suppressHealthDeltaEvents = false;
			}
		}

		private void HandleHealthChanged(int currentHealth, int maximumHealth)
		{
			SyncLegacyMirror(
				acceptsLocalMutations && !_suppressLegacyEvents,
				currentHealth,
				maximumHealth);
		}

		private void SyncLegacyMirror(bool emitLegacyEvents)
		{
			if (combatant == null)
			{
				return;
			}

			SyncLegacyMirror(
				emitLegacyEvents,
				combatant.CurrentHealth,
				combatant.MaxHealth);
		}

		private void SyncLegacyMirror(
			bool emitLegacyEvents,
			int currentHealth,
			int maximumHealth)
		{
			PlayerStats stats = PlayerStats;
			if (stats == null)
			{
				return;
			}

			int previousHealth = _legacyMirrorInitialized
				? _lastHealth
				: currentHealth;
			int previousMaximumHealth = _legacyMirrorInitialized
				? _lastMaximumHealth
				: maximumHealth;

			stats.currentStats.HP = currentHealth;
			stats.currentStats.maxHP = maximumHealth;
			_lastHealth = currentHealth;
			_lastMaximumHealth = maximumHealth;
			_legacyMirrorInitialized = true;

			if (!emitLegacyEvents)
			{
				return;
			}

			int delta = currentHealth - previousHealth;
			if (!_suppressHealthDeltaEvents)
			{
				if (delta > 0)
				{
					GameEvents.Instance?.OnHealthIncrease?.Invoke(delta);
				}
				else if (delta < 0)
				{
					GameEvents.Instance?.OnHealthDecrease?.Invoke(-delta);
				}
			}

			if (maximumHealth != previousMaximumHealth)
			{
				GameEvents.Instance?.OnMaxHealthUpdate?.Invoke(maximumHealth);
			}

			if (delta != 0 || maximumHealth != previousMaximumHealth)
			{
				GameEvents.Instance?.OnHealthUpdate?.Invoke(currentHealth);
			}
		}

		private void OnDestroy()
		{
			Unsubscribe();
		}
	}
}
