using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;

namespace AstralShift.HellMaiden.GameStats
{
	public class PlayerStatsEntry : StatEntry
	{
		public float maxLevelReached { get; set; }

		public float TotalTimeSurvived { get; set; }

		public int damageTaken { get; private set; }

		public int healthRecovered { get; private set; }

		public int totalEnemiesDefeated { get; private set; }

		public Dictionary<string, int> enemiesDefeated { get; private set; }

		public int eliteEnemiesDefeated { get; private set; }

		public int shrinesActivated { get; private set; }

		public Dictionary<uint, int> shrineActivationCount { get; private set; }

		public float totalDamageDealt { get; private set; }

		public float questsCompleted { get; private set; }

		public int ultimatesUsed { get; private set; }

		public Dictionary<int, int> scores { get; set; }

		public Dictionary<uint, int> weaponCounts { get; set; }

		public Dictionary<uint, int> equipmentCounts { get; set; }

		public int HighhestScore { get; set; }

		public event Action<int> OnShrinesActivated;

		public PlayerStatsEntry()
		{
			scores = scores ?? new Dictionary<int, int>();
			weaponCounts = weaponCounts ?? new Dictionary<uint, int>();
			equipmentCounts = equipmentCounts ?? new Dictionary<uint, int>();
			enemiesDefeated = enemiesDefeated ?? new Dictionary<string, int>();
			shrineActivationCount = shrineActivationCount ?? new Dictionary<uint, int>();
		}

		public void RegisterDamageTaken(int value)
		{
			damageTaken += value;
		}

		public void RegisterDamageDealt(float value)
		{
			totalDamageDealt += value;
		}

		public void RegisterHealthRecovered(int value)
		{
			healthRecovered += value;
		}

		public void RegisterTimeSurvived(float value)
		{
			TotalTimeSurvived += value;
		}

		public void RegisterUltimateUsed()
		{
			ultimatesUsed++;
		}

		public void RegisterDefeatedEnemy(string enemyID)
		{
			totalEnemiesDefeated++;
			if (enemiesDefeated.ContainsKey(enemyID))
			{
				enemiesDefeated[enemyID]++;
			}
			else
			{
				enemiesDefeated.Add(enemyID, 1);
			}
		}

		public void RegisterDefeatedElite()
		{
			eliteEnemiesDefeated++;
		}

		public void RegisterTempleActivated(uint shrineID)
		{
			shrinesActivated++;
			if (shrineActivationCount.ContainsKey(shrineID))
			{
				shrineActivationCount[shrineID]++;
			}
			else
			{
				shrineActivationCount.Add(shrineID, 1);
			}
			this.OnShrinesActivated?.Invoke(shrinesActivated);
		}

		public void RegisterLevelUp(int level)
		{
			maxLevelReached = level;
		}

		public void RegisterCompletedQuest()
		{
			questsCompleted++;
		}

		public void RegisterWeaponEquip(uint weaponID)
		{
			if (weaponCounts.ContainsKey(weaponID))
			{
				weaponCounts[weaponID]++;
			}
			else
			{
				weaponCounts.Add(weaponID, 1);
			}
		}

		public void RegisterEquipmentEquip(uint equipmentID)
		{
			if (equipmentCounts.ContainsKey(equipmentID))
			{
				equipmentCounts[equipmentID]++;
			}
			else
			{
				equipmentCounts.Add(equipmentID, 1);
			}
		}

		public void RegisterScore(int circle, int score)
		{
			scores.Add(circle, score);
			HighhestScore = score;
		}

		public override void CompareHighScores(StatEntry statEntry)
		{
			if (statEntry == null || !(statEntry is PlayerStatsEntry playerStatsEntry))
			{
				return;
			}
			if (damageTaken > playerStatsEntry.damageTaken)
			{
				damageTaken = playerStatsEntry.damageTaken;
			}
			if (healthRecovered < playerStatsEntry.healthRecovered)
			{
				healthRecovered = playerStatsEntry.healthRecovered;
			}
			if (TotalTimeSurvived < playerStatsEntry.TotalTimeSurvived)
			{
				TotalTimeSurvived = playerStatsEntry.TotalTimeSurvived;
			}
			if (totalEnemiesDefeated < playerStatsEntry.totalEnemiesDefeated)
			{
				totalEnemiesDefeated = playerStatsEntry.totalEnemiesDefeated;
			}
			if (eliteEnemiesDefeated < playerStatsEntry.eliteEnemiesDefeated)
			{
				eliteEnemiesDefeated = playerStatsEntry.eliteEnemiesDefeated;
			}
			if (maxLevelReached < playerStatsEntry.maxLevelReached)
			{
				maxLevelReached = playerStatsEntry.maxLevelReached;
			}
			if (shrinesActivated < playerStatsEntry.shrinesActivated)
			{
				shrinesActivated = playerStatsEntry.shrinesActivated;
			}
			if (ultimatesUsed < playerStatsEntry.ultimatesUsed)
			{
				ultimatesUsed = playerStatsEntry.ultimatesUsed;
			}
			if (scores.ContainsKey(RunStatsTracker.Instance.Circle))
			{
				if (scores[RunStatsTracker.Instance.Circle] < playerStatsEntry.scores[RunStatsTracker.Instance.Circle])
				{
					scores[RunStatsTracker.Instance.Circle] = playerStatsEntry.scores[RunStatsTracker.Instance.Circle];
				}
			}
			else
			{
				scores.Add(RunStatsTracker.Instance.Circle, playerStatsEntry.scores[RunStatsTracker.Instance.Circle]);
			}
			if (HighhestScore < playerStatsEntry.HighhestScore)
			{
				HighhestScore = playerStatsEntry.HighhestScore;
			}
		}

		public override void JoinStatsEntries(StatEntry statEntry)
		{
			if (statEntry == null || !(statEntry is PlayerStatsEntry playerStatsEntry))
			{
				return;
			}
			damageTaken += playerStatsEntry.damageTaken;
			healthRecovered += playerStatsEntry.healthRecovered;
			TotalTimeSurvived += playerStatsEntry.TotalTimeSurvived;
			totalEnemiesDefeated += playerStatsEntry.totalEnemiesDefeated;
			eliteEnemiesDefeated += playerStatsEntry.eliteEnemiesDefeated;
			maxLevelReached += playerStatsEntry.maxLevelReached;
			shrinesActivated += playerStatsEntry.shrinesActivated;
			ultimatesUsed += playerStatsEntry.ultimatesUsed;
			foreach (KeyValuePair<string, int> item in playerStatsEntry.enemiesDefeated)
			{
				if (enemiesDefeated.ContainsKey(item.Key))
				{
					enemiesDefeated[item.Key] += item.Value;
				}
				else
				{
					enemiesDefeated.Add(item.Key, item.Value);
				}
			}
			foreach (KeyValuePair<uint, int> weaponCount in playerStatsEntry.weaponCounts)
			{
				if (weaponCounts.ContainsKey(weaponCount.Key))
				{
					weaponCounts[weaponCount.Key] += weaponCount.Value;
				}
				else
				{
					weaponCounts.Add(weaponCount.Key, weaponCount.Value);
				}
			}
			foreach (KeyValuePair<uint, int> equipmentCount in playerStatsEntry.equipmentCounts)
			{
				if (equipmentCounts.ContainsKey(equipmentCount.Key))
				{
					equipmentCounts[equipmentCount.Key] += equipmentCount.Value;
				}
				else
				{
					equipmentCounts.Add(equipmentCount.Key, equipmentCount.Value);
				}
			}
			foreach (KeyValuePair<uint, int> item2 in playerStatsEntry.shrineActivationCount)
			{
				if (shrineActivationCount.ContainsKey(item2.Key))
				{
					shrineActivationCount[item2.Key] += item2.Value;
				}
				else
				{
					shrineActivationCount.Add(item2.Key, item2.Value);
				}
			}
		}

		public override void CleanEntry()
		{
			damageTaken = 0;
			healthRecovered = 0;
		}

		public void LinkPlayerEvents()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnHealthDecrease = (Action<int>)Delegate.Combine(instance.OnHealthDecrease, new Action<int>(RegisterDamageTaken));
			GameEvents instance2 = GameEvents.Instance;
			instance2.OnHealthIncrease = (Action<int>)Delegate.Combine(instance2.OnHealthIncrease, new Action<int>(RegisterHealthRecovered));
			GameEvents instance3 = GameEvents.Instance;
			instance3.OnTimeTick = (Action<float>)Delegate.Combine(instance3.OnTimeTick, new Action<float>(RegisterTimeSurvived));
			GameEvents instance4 = GameEvents.Instance;
			instance4.OnLevelIncrease = (Action<int>)Delegate.Combine(instance4.OnLevelIncrease, new Action<int>(RegisterLevelUp));
			GameEvents instance5 = GameEvents.Instance;
			instance5.UltimateUsed = (Action)Delegate.Combine(instance5.UltimateUsed, new Action(RegisterUltimateUsed));
		}

		public override void CleanLinkedEvents()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnHealthDecrease = (Action<int>)Delegate.Remove(instance.OnHealthDecrease, new Action<int>(RegisterDamageTaken));
			GameEvents instance2 = GameEvents.Instance;
			instance2.OnHealthIncrease = (Action<int>)Delegate.Remove(instance2.OnHealthIncrease, new Action<int>(RegisterHealthRecovered));
			GameEvents instance3 = GameEvents.Instance;
			instance3.OnTimeTick = (Action<float>)Delegate.Remove(instance3.OnTimeTick, new Action<float>(RegisterTimeSurvived));
			GameEvents instance4 = GameEvents.Instance;
			instance4.OnLevelIncrease = (Action<int>)Delegate.Remove(instance4.OnLevelIncrease, new Action<int>(RegisterLevelUp));
			GameEvents instance5 = GameEvents.Instance;
			instance5.UltimateUsed = (Action)Delegate.Remove(instance5.UltimateUsed, new Action(RegisterUltimateUsed));
		}
	}
}
