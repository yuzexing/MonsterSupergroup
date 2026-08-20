using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[CreateAssetMenu(fileName = "New Hub Dialogue LUT", menuName = "HellMaiden/Data/Dialogues/Hub Dialogue LUT")]
	public class HubDialogueLUT : ScriptableObject
	{
		public List<DialogueLUTEntry> HighPriority;

		public List<DialogueLUTEntry> MediumPriority;

		public List<DialogueLUTEntry> LowPriority;
	}
}
