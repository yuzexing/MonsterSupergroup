using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AstralShift.HellMaiden.Items
{
	[CreateAssetMenu(fileName = "XPColorTresholds", menuName = "HellMaiden/Data/XPColorTresholds")]
	public class XPColorTresholds : ScriptableObject
	{
		public List<XPColorTreshold> XPColorTresholdsList;

		public void Initialize()
		{
			XPColorTresholdsList = XPColorTresholdsList.OrderBy((XPColorTreshold x) => x.value).ToList();
		}

		public XPGem GetGemByValue(float value)
		{
			int index = 0;
			for (int i = 0; i < XPColorTresholdsList.Count; i++)
			{
				if (value >= (float)XPColorTresholdsList[i].value)
				{
					index = i;
				}
			}
			return XPColorTresholdsList[index].xpPrefab;
		}
	}
}
