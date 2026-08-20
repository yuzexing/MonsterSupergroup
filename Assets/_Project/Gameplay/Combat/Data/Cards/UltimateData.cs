using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "New Ultimate Data", menuName = "HellMaiden/Data/Ultimate Data")]
	public class UltimateData : ScriptableObject
	{
		[SerializeField]
		protected uint id;

		[SerializeField]
		protected string title;

		[TextArea]
		[SerializeField]
		protected string description;

		[SerializeField]
		protected bool hasLocalization;

		[SerializeField]
		protected string titleKey;

		[SerializeField]
		protected string descriptionKey;

		[SerializeField]
		public UltimateAttackEvents ultimateAttackEvents;

		[SerializeField]
		public UltimateAttackWeaponBehaviour ultimateAttackWeaponBehaviour;

		[Space]
		[SerializeField]
		protected AttackStats baseStats;

		public uint Id => id;

		public bool HasLocalization => hasLocalization;

		public AttackStats BaseStats => baseStats;

		public virtual string GetTitle()
		{
			if (HasLocalization)
			{
				string term = titleKey;
				LocalizationMediator.GetTranslation(ref term);
				return term;
			}
			return title;
		}

		public virtual string GetDescription()
		{
			if (HasLocalization)
			{
				string term = descriptionKey;
				LocalizationMediator.GetTranslation(ref term);
				return term;
			}
			return description;
		}
	}
}
