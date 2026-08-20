using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "EquipmentDB", menuName = "Scriptable Objects/EquipmentDB")]
	public class EquipmentDB : ScriptableObject
	{
		public EquipmentData[] Equipments;

		[SerializeField]
		private EquipmentVisualsTemplateLUT visualsTemplatesLUT;

		public EquipmentVisualsTemplateLUT VisualsTemplateLUT => visualsTemplatesLUT;
	}
}
