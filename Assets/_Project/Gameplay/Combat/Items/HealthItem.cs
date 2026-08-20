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
			GameDirector.Instance.Player.IncreaseHealth(healthValue);
			base.Consume();
		}

		public override bool StartPlayerPull()
		{
			if (GameDirector.Instance.Player.CheckIfMaxHealth())
			{
				return false;
			}
			return base.StartPlayerPull();
		}
	}
}
