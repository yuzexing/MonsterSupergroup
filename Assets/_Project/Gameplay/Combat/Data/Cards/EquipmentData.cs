using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	[CreateAssetMenu(fileName = "New Equipment Data", menuName = "HellMaiden/Data/Cards/Equipment Data")]
	public class EquipmentData : CardData
	{
		[Header("Equipment Settings")]
		public EquipmentCardType cardType;

		public ModifierFlags usedStatsModifiers;

		[SerializeField]
		protected EquipmentLevelModifiersData[] levelModifiersData;

        public EquipmentLevelModifiersData[] Levels => levelModifiersData;
        public override string GetDescription() => GetDescription(0u);
        public string GetDescription(uint levelIndex)
        {
            if (Levels == null || levelIndex >= Levels.Length) return MonsterSupergroup.Gameplay.Options.GameLocalization.Menu("ui.content.unavailable");
            var level = Levels[levelIndex];
            var text = level.LocalizedDescription != null && !level.LocalizedDescription.IsEmpty ? level.LocalizedDescription : localizedDescription;
            return MonsterSupergroup.Gameplay.Options.GameLocalization.Resolve(text, ID,
                MonsterSupergroup.Gameplay.Options.ContentText.Arguments(level.Modifiers));
        }
    }
}
