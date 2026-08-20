using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus.Achievement.Data
{
	[CreateAssetMenu(fileName = "AchievementDB", menuName = "HellMaiden/Data/HellMaidenAchievements/AchievementDataBase")]
	public class AchievementDB : ScriptableObject
	{
		public List<AchievementData> achievements;
	}
}
