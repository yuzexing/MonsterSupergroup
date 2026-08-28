using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using UnityEngine;
using GasEnemyStatusID = MonsterSupergroup.GAS.EnemyStatusID;
using LegacyDamageType = AstralShift.HellMaiden.Player.Attacks.DamageType;

namespace AstralShift.HellMaiden.AI.Enemy
{
	/// <summary>
	/// Compatibility facade for HellMaiden-authored status calls. StatusController is
	/// the only runtime state store; this component only maps the legacy API to GAS and
	/// binds legacy stat/presentation effects to StatusController changes.
	/// </summary>
	public class EnemyStatus : MonoBehaviour
	{
		private BaseEnemyController _controller;

		private CombatantBehaviour _combatant;

		private StatusController _runtime;

		private bool _ownsRuntime;

		public StatusController Runtime
		{
			get
			{
				if (_runtime == null)
				{
					throw new InvalidOperationException("EnemyStatus must be initialized before use.");
				}

				return _runtime;
			}
		}

		public void Init(BaseEnemyController controller)
		{
			if (controller == null)
			{
				throw new ArgumentNullException(nameof(controller));
			}

			Unsubscribe();
			_controller = controller;
			EnemyCombatantBinding binding = controller.GetComponent<EnemyCombatantBinding>();
			if (binding != null)
			{
				_combatant = binding.Combatant;
				_runtime = _combatant.StatusController;
				_ownsRuntime = false;
			}
			else
			{
				_combatant = null;
				_runtime = new StatusController(ReceiveFallbackStatusTick);
				_ownsRuntime = true;
			}

			_runtime.Changed += HandleStatusChanged;
			ClearAllStatus();
		}

		public bool HasStatus(GasEnemyStatusID id)
		{
			return _runtime != null && _runtime.Has(id);
		}

		public bool HasAnyStatus()
		{
			return _runtime != null && _runtime.Count > 0;
		}

		public StatusApplicationResult Apply(
			GasEnemyStatusID id,
			float power,
			float duration,
			float rate = 0f,
			float priority = 0f,
			LegacyDamageSource source = default)
		{
			if (float.IsNaN(power) || float.IsInfinity(power))
			{
				throw new ArgumentOutOfRangeException(nameof(power));
			}

			if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f)
			{
				throw new ArgumentOutOfRangeException(nameof(duration));
			}

			bool periodic = IsPeriodic(id);
			if (periodic &&
				(float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0f))
			{
				throw new ArgumentOutOfRangeException(nameof(rate));
			}

			CombatTags tags = GetTags(id);
			CombatContext context = source.IsValid
				? source.Context.WithTags(source.Tags | tags)
				: default;
			int numberOfHits = periodic ? Mathf.Max(1, Mathf.CeilToInt(duration)) : 1;
			float interval = periodic ? rate : duration;
			int tickDamage = periodic ? Mathf.Max(0, (int)power) : 0;
			return Runtime.Apply(new StatusApplication(
				GetDefinition(id),
				tickDamage,
				numberOfHits,
				interval,
				priority,
				source.DamageSourceId,
				sourcePlayerId: context.IsValid ? context.SourcePlayerId : 0u,
				sourceEntityId: context.IsValid ? context.SourceEntityId : 0u,
				targetEntityId: GetTargetEntityId(context),
				executionAuthority: source.IsValid
					? StatusExecutionAuthority.SourceClient
					: StatusExecutionAuthority.Server,
				sourceContext: context,
				magnitude: power));
		}

		public void ConsumeStack(GasEnemyStatusID id)
		{
			Runtime.ConsumeAll(id, IsPeriodic(id));
		}

		public void ClearAllStatus()
		{
			_runtime?.Clear();
			ResetLegacyStats();
			EnemyStatusResolver.Instance?.HideAll(_controller);
		}

		public void TransferTo(BaseEnemyController targetEnemy)
		{
			if (targetEnemy == null || targetEnemy == _controller)
			{
				return;
			}

			if (targetEnemy.status == null)
			{
				throw new InvalidOperationException("Target enemy requires an EnemyStatus facade.");
			}

			_runtime.TransferTo(
				targetEnemy.status.Runtime,
				targetEnemy.status.GetTargetEntityId(default));
		}

		private void Update()
		{
			if (_ownsRuntime && _runtime != null)
			{
				_runtime.Advance(Time.deltaTime);
			}
		}

		private void HandleStatusChanged(StatusChange change)
		{
			RefreshLegacyBinding(change.Instance.DefinitionId);
		}

		private void RefreshLegacyBinding(GasEnemyStatusID id)
		{
			IReadOnlyList<StatusInstance> instances = Runtime.GetInstances(id);
			if (instances.Count == 0)
			{
				if (id == GasEnemyStatusID.Slow && _controller?.stats != null)
				{
					_controller.stats.SpeedMultiplier = 1f;
				}
				else if (id == GasEnemyStatusID.Weaken && _controller?.stats != null)
				{
					_controller.stats.DamageMultiplier = 1f;
				}

				EnemyStatusResolver.Instance?.HideStatus(_controller, id);
				return;
			}

			StatusInstance effective = instances[0];
			for (int i = 1; i < instances.Count; i++)
			{
				if (instances[i].Priority >= effective.Priority)
				{
					effective = instances[i];
				}
			}

			if (id == GasEnemyStatusID.Slow && _controller?.stats != null)
			{
				_controller.stats.SpeedMultiplier = effective.Magnitude;
			}
			else if (id == GasEnemyStatusID.Weaken && _controller?.stats != null)
			{
				_controller.stats.DamageMultiplier = effective.Magnitude;
			}

			EnemyStatusResolver.Instance?.ShowStatus(
				_controller,
				id,
				effective.Duration);
		}

		private void ReceiveFallbackStatusTick(StatusTick tick)
		{
			if (_controller == null || !_controller.IsAlive || tick.Damage.Value <= 0)
			{
				return;
			}

			_controller.Damage(tick.Damage.Value, GetDamageType(tick.StatusId));
		}

		private uint GetTargetEntityId(CombatContext context)
		{
			if (_combatant != null && _combatant.EntityId != 0u)
			{
				return _combatant.EntityId;
			}

			return context.IsValid ? context.TargetEntityId : 0u;
		}

		private void ResetLegacyStats()
		{
			if (_controller?.stats == null)
			{
				return;
			}

			_controller.stats.SpeedMultiplier = 1f;
			_controller.stats.DamageMultiplier = 1f;
		}

		private void Unsubscribe()
		{
			if (_runtime != null)
			{
				_runtime.Changed -= HandleStatusChanged;
			}
		}

		private void OnDestroy()
		{
			Unsubscribe();
			if (_ownsRuntime)
			{
				_runtime?.Clear();
			}
			_runtime = null;
			_combatant = null;
			_controller = null;
		}

		private static bool IsPeriodic(GasEnemyStatusID id)
		{
			return id == GasEnemyStatusID.Burn ||
				id == GasEnemyStatusID.Poison ||
				id == GasEnemyStatusID.Bleed;
		}

		private static CombatTags GetTags(GasEnemyStatusID id)
		{
			CombatTags tags = CombatTags.Status;
			if (IsPeriodic(id))
			{
				tags |= CombatTags.Periodic;
			}

			if (id == GasEnemyStatusID.Burn)
			{
				tags |= CombatTags.Burn | CombatTags.Fire;
			}
			else if (id == GasEnemyStatusID.Poison)
			{
				tags |= CombatTags.Poison;
			}

			return tags;
		}

		private static StatusDefinition GetDefinition(GasEnemyStatusID id)
		{
			switch (id)
			{
			case GasEnemyStatusID.Slow:
			case GasEnemyStatusID.Burn:
			case GasEnemyStatusID.Poison:
			case GasEnemyStatusID.Weaken:
			case GasEnemyStatusID.Fragile:
				return new StatusDefinition(id, StatusStackMode.HighestPriority, 1);
			case GasEnemyStatusID.Bleed:
				return new StatusDefinition(id, StatusStackMode.Add, 10);
			case GasEnemyStatusID.Stun:
				return new StatusDefinition(id, StatusStackMode.Replace, 1);
			default:
				throw new ArgumentOutOfRangeException(nameof(id), id, "Unsupported enemy status.");
			}
		}

		private static LegacyDamageType GetDamageType(GasEnemyStatusID id)
		{
			switch (id)
			{
			case GasEnemyStatusID.Burn:
				return LegacyDamageType.Fire;
			case GasEnemyStatusID.Poison:
				return LegacyDamageType.Poison;
			case GasEnemyStatusID.Bleed:
				return LegacyDamageType.Bleed;
			default:
				return LegacyDamageType.Normal;
			}
		}
	}
}
