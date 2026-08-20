using System;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	[Serializable]
	public class BossPhase
	{
		[SerializeField]
		private float healthThreshold;

		[SerializeField]
		private float intermissionInterval = 20f;

		public float HealthThreshold
		{
			get
			{
				return healthThreshold;
			}
			set
			{
				healthThreshold = value;
			}
		}

		public float IntermissionInterval => intermissionInterval;
	}
}
