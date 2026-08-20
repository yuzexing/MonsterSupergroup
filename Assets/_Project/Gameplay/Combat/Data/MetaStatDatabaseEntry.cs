using System;
using AstralShift.HellMaiden.Data;
using UnityEngine;

namespace Assets.Scripts.AstralShift.HellMaiden.Data
{
	[Serializable]
	public class MetaStatDatabaseEntry
	{
		public enum MetaStatDatabaseEntryType
		{
			ADD = 0,
			MUL = 1
		}

		[Serializable]
		public struct MetaStatDatabaseEntryLevel
		{
			public float increaseAmmount;

			public float cost;

			public bool hasLockVerification;

			public AchievementManager.AchievementID achievementID;
		}

		public string name;

		public string description;

		public MetaStatDatabaseEntryLevel[] levels;

		public MetaStatDatabaseEntryType type;

		public Sprite icon;

		public MetaColor color;
	}
}
