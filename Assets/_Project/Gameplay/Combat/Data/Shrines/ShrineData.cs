using System.Collections.Generic;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Shrines
{
	[CreateAssetMenu(fileName = "New Shrine Data", menuName = "HellMaiden/Data/Shrines/Shrine Data")]
	public class ShrineData : ScriptableObject
	{
		public uint ID;

		[SerializeField]
		private List<PerkDataModifier> modifiers = new List<PerkDataModifier>();

		public bool permanent;

		[ConditionalHide("permanent", false)]
		public float duration;

		[TextArea]
		public string pickupText;

		[SerializeField]
		private Sprite hudIcon;

		public Animator BuffSpherePrefab;

		public IReadOnlyList<PerkDataModifier> Modifiers => modifiers;

		public Sprite GetIconSprite()
		{
			return hudIcon;
		}
	}
}
