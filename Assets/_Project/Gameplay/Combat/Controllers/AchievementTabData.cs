using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.UI.Menus.Achievement;

namespace AstralShift.HellMaiden.Controllers
{
	[Serializable]
	public class AchievementTabData
	{
		public string tabName;

		public List<AchievementData> achievements;
	}
}
