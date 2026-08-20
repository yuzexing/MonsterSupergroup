using AstralShift.HellMaiden.AI.Boss;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class BossPlayerDamageInteraction : PlayerDamageInteraction
	{
		[SerializeField]
		private bool findBossStatsWithTag;

		[SerializeField]
		[ConditionalHide("findBossStatsWithTag", true)]
		private string bossTag;

		private void OnEnable()
		{
			if (findBossStatsWithTag)
			{
				GameObject gameObject = GameObject.FindGameObjectWithTag(bossTag);
				if ((bool)gameObject)
				{
					enemyStats = gameObject.GetComponent<BossController>().stats;
				}
			}
			else
			{
				BossController bossController = Object.FindAnyObjectByType<BossController>();
				if ((bool)bossController)
				{
					enemyStats = bossController.stats;
				}
			}
		}
	}
}
