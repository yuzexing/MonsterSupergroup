using AstralShift.HellMaiden.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.Items
{
	public class MagnetItem : WorldItem
	{
		[Header("Settings")]
		public float duration = 1f;

		protected override void OnEnable()
		{
			_worldItemPool = PoolManager.Instance.ItemsPool.Magnet;
			base.OnEnable();
		}

		public override void Consume()
		{
			GameEvents.Instance.StartMagnet(duration);
			base.Consume();
		}
	}
}
