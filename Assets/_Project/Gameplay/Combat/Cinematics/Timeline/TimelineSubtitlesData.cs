using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.Cinematics.Timeline
{
	[CreateAssetMenu(fileName = "New Subtitles Data", menuName = "HellMaiden/Data/Subtitles")]
	public class TimelineSubtitlesData : ScriptableObject
	{
		public List<string> LUT;
	}
}
