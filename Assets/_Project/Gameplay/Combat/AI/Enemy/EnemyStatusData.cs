using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public struct EnemyStatusData
	{
		public float power;

		public float startTime;

		public float currentDuration;

		public float totalDuration;

		public float hitInterval;

		public float priority;

		public LegacyDamageSource source;

		public EnemyStatusData(
			float power,
			float duration,
			float hitInterval = 0f,
			float priority = 0f,
			LegacyDamageSource source = default)
		{
			this.power = power;
			startTime = Time.time;
			currentDuration = 0f;
			totalDuration = duration;
			this.hitInterval = hitInterval;
			this.priority = priority;
			this.source = source;
		}
	}
}
