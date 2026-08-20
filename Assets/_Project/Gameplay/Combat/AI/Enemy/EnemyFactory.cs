using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.GameStats;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public static class EnemyFactory
	{
		public static EnemyController CreateEnemy(EnemySpawnParams args)
		{
			EnemyController orCreate = args.Pool.GetOrCreate();
			EnemyDatabase.EnemyData enemyData = ProgressionManager.Instance.enemyDatabase.GetEnemyData(args.Prefab.selectedName, args.VariantIdx);
			orCreate.stats = enemyData.Stats;
			orCreate.lootSettings = ProgressionManager.Instance.enemyDatabase.GetLootSettings(args.Prefab.selectedName, args.VariantIdx);
			orCreate.overrideGlobalLootSettings = orCreate.lootSettings;
			float xPModifier = ProgressionManager.Instance.GetXPModifier();
			orCreate.stats.BaseXP *= xPModifier;
			orCreate.Target = args.AttackTarget;
			orCreate.EnemyPool = args.Pool;
			orCreate.allowRubberband = args.AllowRubberBand;
			orCreate.enemyAnimator.Recolor(enemyData.ColorLUT);
			orCreate.RubberbandMaxDistance = enemyData.RubberbandMaxDistance;
			orCreate.endTime = (args.RubberbandKillsEnemiesOnClipEnd ? args.EndTime : 0f);
			orCreate.transform.position = args.SpawnPosition;
			int id = args.ID;
			if (args.ID == 0)
			{
				id = GenerateId(args.Prefab.selectedName + args.VariantIdx);
			}
			orCreate.Init(id);
			orCreate.stats.SpeedMultiplier = Random.Range(args.SpeedMultiplierRange.x, args.SpeedMultiplierRange.y);
			if (enemyData.hueColor != Color.white)
			{
				orCreate.enemyAnimator.Recolor(enemyData.hueColor);
			}
			if (args.OnKill != null)
			{
				orCreate.OnKill += args.OnKill;
			}
			if (orCreate.isElite)
			{
				orCreate.OnKill += RegisterEliteEnemyDeath;
			}
			return orCreate;
		}

		private static void RegisterEliteEnemyDeath()
		{
			RunStatsTracker.Instance?.PlayerStatsEntry.RegisterDefeatedElite();
		}

		public static int GenerateId(string enemyName)
		{
			if (string.IsNullOrEmpty(enemyName))
			{
				return 0;
			}
			uint num = 2166136261u;
			foreach (ushort num2 in enemyName)
			{
				num ^= (byte)(num2 & 0xFF);
				num *= 16777619;
				num ^= (byte)(num2 >> 8);
				num *= 16777619;
			}
			int num3 = (int)num;
			if (num3 == -1)
			{
				return -3;
			}
			return num3;
		}
	}
}
