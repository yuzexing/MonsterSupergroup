using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Items;
using AstralShift.QTI.Helpers.Attributes;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "EnemyDB", menuName = "Scriptable Objects/AstralShift/EnemyDB")]
public class EnemyDatabase : ScriptableObject
{
	[Serializable]
	public class EnemyData
	{
		public string variantName;

		[Header("Loot Drop Settings")]
		public bool overrideGlobalLootSettings;

		[ConditionalHide("overrideGlobalLootSettings", true)]
		public LootSettingsData lootSettings;

		[SerializeField]
		private EnemyStats stats;

		[SerializeField]
		private float rubberbandMaxDistance = 20f;

		[SerializeField]
		private Texture2D colorLUT;

		[Obsolete]
		public Color hueColor = Color.white;

		public EnemyStats Stats => stats.Clone();

		public float RubberbandMaxDistance => rubberbandMaxDistance;

		public Texture2D ColorLUT => colorLUT;
	}

	[Serializable]
	public class EnemyDataAggregator
	{
		public string enemyName;

		public EnemyData[] enemyData;
	}

	[SerializeField]
	[HideInInspector]
	private EnemyDataAggregator[] enemies;

	private string enemyFilter = "All";

	public EnemyDataAggregator[] Enemies => enemies;

	public string[] EnemyNames
	{
		get
		{
			if (enemies == null)
			{
				return Array.Empty<string>();
			}
			return (from n in (from e in enemies
					select e.enemyName into n
					where !string.IsNullOrEmpty(n)
					select n).Distinct()
				orderby n
				select n).ToArray();
		}
	}

	private EnemyDataAggregator[] EnemiesView
	{
		get
		{
			if (enemies == null)
			{
				return Array.Empty<EnemyDataAggregator>();
			}
			if (string.IsNullOrEmpty(enemyFilter) || enemyFilter == "All")
			{
				return enemies;
			}
			return enemies.Where((EnemyDataAggregator e) => e.enemyName == enemyFilter).ToArray();
		}
		set
		{
			if (enemyFilter == "All")
			{
				enemies = value;
			}
		}
	}

	private ValueDropdownItem<string>[] GetEnemyFilterDropdown()
	{
		List<ValueDropdownItem<string>> list = EnemyNames.Select((string n) => new ValueDropdownItem<string>(n, n)).ToList();
		list.Insert(0, new ValueDropdownItem<string>("All", "All"));
		return list.ToArray();
	}

	public EnemyData GetEnemyData(string enemyName, int variantIdx)
	{
		if (enemies == null)
		{
			return null;
		}
		int num = Array.FindIndex(enemies, (EnemyDataAggregator e) => e.enemyName == enemyName);
		if (num < 0)
		{
			return null;
		}
		if (enemies[num].enemyData == null)
		{
			return null;
		}
		if (variantIdx < 0 || variantIdx >= enemies[num].enemyData.Length)
		{
			return null;
		}
		return enemies[num].enemyData[variantIdx];
	}

	public LootSettingsData GetLootSettings(string enemyName, int variantIdx)
	{
		EnemyData enemyData = GetEnemyData(enemyName, variantIdx);
		if (enemyData == null)
		{
			return null;
		}
		if ((bool)enemyData.lootSettings && enemyData.overrideGlobalLootSettings)
		{
			return enemyData.lootSettings;
		}
		return null;
	}
}
