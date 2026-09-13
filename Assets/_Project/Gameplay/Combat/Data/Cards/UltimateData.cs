using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "New Ultimate Data", menuName = "HellMaiden/Data/Ultimate Data")]
	public class UltimateData : ScriptableObject
	{
		[SerializeField]
		protected uint id;

        [SerializeField] private UnityEngine.Localization.LocalizedString localizedTitle = new();
        [SerializeField] private UnityEngine.Localization.LocalizedString localizedDescription = new();
        public UnityEngine.Localization.LocalizedString LocalizedTitle => localizedTitle;
        public UnityEngine.Localization.LocalizedString LocalizedDescription => localizedDescription;

		[SerializeField]
		public UltimateAttackEvents ultimateAttackEvents;

		[SerializeField]
		public UltimateAttackWeaponBehaviour ultimateAttackWeaponBehaviour;

		[Space]
		[SerializeField]
		protected AttackStats baseStats;

		public uint Id => id;


		public AttackStats BaseStats => baseStats;

        public virtual string GetTitle() => MonsterSupergroup.Gameplay.Options.GameLocalization.Resolve(localizedTitle, Id);
        public virtual string GetDescription() => MonsterSupergroup.Gameplay.Options.GameLocalization.Resolve(localizedDescription, Id);
    }
}
