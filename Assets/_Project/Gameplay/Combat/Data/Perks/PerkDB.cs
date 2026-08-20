using UnityEngine;

namespace AstralShift.HellMaiden.Data.Perks
{
	[CreateAssetMenu(fileName = "PerkDB", menuName = "Scriptable Objects/PerkDB")]
	public class PerkDB : ScriptableObject
	{
		public PerkData[] Perks;

		[Space]
		[SerializeField]
		private PerkTemplateLUT visualsTemplateLut;

		public PerkTemplateLUT VisualsTemplateLUT => visualsTemplateLut;
	}
}
