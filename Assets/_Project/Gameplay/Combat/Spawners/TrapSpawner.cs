using AstralShift.HellMaiden.Combat.Traps;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.Combat.Spawners
{
	public abstract class TrapSpawner : SerializedProgressable
	{
		[FormerlySerializedAs("prefab")]
		public Trap trap;

		public float startingChance;

		public float pittyChance;

		public float trapCooldown;

		protected float lastTrapTime = float.NegativeInfinity;

		protected float currentChance;

		public override void Init()
		{
			startingChance = 100f / base.Duration;
			pittyChance = startingChance;
			currentChance = startingChance;
			TrySpawn();
		}

		public override void ProgressUpdate()
		{
			TrySpawn();
		}

		public virtual void TrySpawn()
		{
			if (!ProgressionManager.Instance.ReachedMaxTrapCount && !(Time.time - lastTrapTime < trapCooldown))
			{
				if (Random.Range(0f, 100f) <= currentChance)
				{
					lastTrapTime = Time.time;
					SpawnTrap();
				}
				else
				{
					currentChance += pittyChance;
				}
			}
		}

		protected abstract void SpawnTrap();
	}
}
