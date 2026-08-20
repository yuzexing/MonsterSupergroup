using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "New Equipment Card Visual Data", menuName = "HellMaiden/Data/Cards/Visuals/Equipment Card Visual Data")]
	public class EquipmentCardVisualData : CardVisualData
	{
		[SerializeField]
		protected List<CardVisualLayerGroup> illustrationsPerLevel;

		public List<CardVisualLayerGroup> IllustrationsPerLevel => illustrationsPerLevel;
	}
}
