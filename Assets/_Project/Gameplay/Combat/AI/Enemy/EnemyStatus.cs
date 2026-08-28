using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyStatus : MonoBehaviour
	{
		private EnemyStatusID _activeStatuses;

		private BaseEnemyController _controller;

		public void Init(BaseEnemyController controller)
		{
			_controller = controller;
			ClearAllStatus();
		}

		public bool HasStatus(EnemyStatusID id)
		{
			return (_activeStatuses & id) != 0;
		}

		public bool HasAnyStatus()
		{
			if (_activeStatuses != EnemyStatusID.None)
			{
				return true;
			}
			return false;
		}

		public void Apply(
			EnemyStatusID id,
			float power,
			float duration,
			float rate = 0f,
			float priority = 0f,
			LegacyDamageSource source = default)
		{
			EnemyStatusData data = new EnemyStatusData(
				power,
				duration,
				rate,
				priority,
				source);
			switch (id)
			{
			case EnemyStatusID.Slow:
				ApplySlow(data);
				break;
			case EnemyStatusID.Weaken:
				ApplyWeaken(data);
				break;
			case EnemyStatusID.Burn:
				ApplyBurn(data);
				break;
			case EnemyStatusID.Poison:
				ApplyPoison(data);
				break;
			case EnemyStatusID.Bleed:
				ApplyBleed(data);
				break;
			}
		}

		public void ConsumeStack(EnemyStatusID id)
		{
			EnemyStatusResolver.Instance.ConsumeStack(id, _controller);
		}

		private void ApplySlow(EnemyStatusData data)
		{
			EnemyStatusResolver.Instance.RegisterSlowStatus(_controller, data);
		}

		private void ApplyWeaken(EnemyStatusData data)
		{
			EnemyStatusResolver.Instance.RegisterWeakStatus(_controller, data);
		}

		private void ApplyBurn(EnemyStatusData data)
		{
			AddBit(EnemyStatusID.Burn);
			EnemyStatusResolver.Instance.RegisterBurnStatus(_controller, data);
		}

		private void ApplyPoison(EnemyStatusData data)
		{
			AddBit(EnemyStatusID.Poison);
			EnemyStatusResolver.Instance.RegisterPoisonStatus(_controller, data);
		}

		private void ApplyBleed(EnemyStatusData data)
		{
			AddBit(EnemyStatusID.Bleed);
			EnemyStatusResolver.Instance.RegisterBleedStatus(_controller, data);
		}

		public void SetSlowStat(float power)
		{
			AddBit(EnemyStatusID.Slow);
			_controller.stats.SpeedMultiplier = power;
		}

		public void RemoveSlow()
		{
			RemoveBit(EnemyStatusID.Slow);
			_controller.stats.SpeedMultiplier = 1f;
		}

		public void SetWeakStat(float power)
		{
			AddBit(EnemyStatusID.Weaken);
			_controller.stats.DamageMultiplier = power;
		}

		public void RemoveWeak()
		{
			RemoveBit(EnemyStatusID.Weaken);
			_controller.stats.DamageMultiplier = 1f;
		}

		public void RemoveBurn()
		{
			RemoveBit(EnemyStatusID.Burn);
		}

		public void RemovePoison()
		{
			RemoveBit(EnemyStatusID.Poison);
		}

		public void RemoveBleed()
		{
			RemoveBit(EnemyStatusID.Bleed);
		}

		public void ClearAllStatus()
		{
			if (HasStatus(EnemyStatusID.Slow))
			{
				EnemyStatusResolver.Instance.UnRegisterSlowStatus(_controller);
			}
			if (HasStatus(EnemyStatusID.Burn))
			{
				EnemyStatusResolver.Instance.UnRegisterBurnStatus(_controller);
			}
			if (HasStatus(EnemyStatusID.Poison))
			{
				EnemyStatusResolver.Instance.UnRegisterPoisonStatus(_controller);
			}
			if (HasStatus(EnemyStatusID.Bleed))
			{
				EnemyStatusResolver.Instance.UnRegisterBleedStatus(_controller);
			}
			if (HasStatus(EnemyStatusID.Weaken))
			{
				EnemyStatusResolver.Instance.UnRegisterWeakStatus(_controller);
			}
			_activeStatuses = EnemyStatusID.None;
		}

		private void AddBit(EnemyStatusID id)
		{
			_activeStatuses |= id;
		}

		private void RemoveBit(EnemyStatusID id)
		{
			_activeStatuses &= ~id;
		}

		public void TransferTo(BaseEnemyController targetEnemy)
		{
			if (!(targetEnemy == null) && !(targetEnemy == _controller))
			{
				targetEnemy.status._activeStatuses = _activeStatuses;
				EnemyStatusResolver.Instance.TransferStatus(_controller, targetEnemy);
				_activeStatuses = EnemyStatusID.None;
			}
		}
	}
}
