using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Items
{
	[Serializable]
	[CreateAssetMenu(fileName = "NewLootSettings", menuName = "Divina/Loot Settings")]
	public class LootSettingsData : ScriptableObject
	{
		[Serializable]
		public class ItemSettingsOverride
		{
			[Range(0f, 100f)]
			public int numberOfItems = 1;

			public float HealthWeight = 1f;

			public float MagnetWeight = 1f;
		}

		[SerializeField]
		public bool isXPMandatory;

		[SerializeField]
		[Range(0f, 1f)]
		public float XPWeight = 1f;

		[SerializeField]
		[Range(0f, 1f)]
		public float ItemsWeight = 1f;

		public ItemSettingsOverride ItemSettings;

		public bool alwaysDrops = true;

		public bool dropChest;
	}
}
