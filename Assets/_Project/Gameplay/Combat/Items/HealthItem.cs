using AstralShift.HellMaiden.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.Items
{
	public class HealthItem : WorldItem
	{
		[Header("Settings")]
		public int healthValue = 20;

		protected override void OnEnable()
		{
			_worldItemPool = PoolManager.Instance.ItemsPool.Health;
			base.OnEnable();
		}

		public override void Consume()
		{
			PullCollector?.CombatantBinding?.RestoreHealth(healthValue);
			base.Consume();
		}

		public override bool StartPlayerPull(ILootColector collector)
		{
			if (collector?.CombatantBinding == null ||
				collector.CombatantBinding.CurrentHealth ==
				collector.CombatantBinding.MaximumHealth)
			{
				return false;
			}
			return base.StartPlayerPull(collector);
		}
	}
}
