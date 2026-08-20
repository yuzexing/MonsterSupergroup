using System.Collections.Generic;
using AstralShift.HellMaiden.Dialogue;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[CreateAssetMenu(fileName = "New Poets Dialogue LUT", menuName = "HellMaiden/Data/Dialogues/Poets Dialogue LUT")]
	public class PoetsDialogueLUT : SerializedScriptableObject
	{
		public Dictionary<PoetID, HubDialogueLUT> LUT = new Dictionary<PoetID, HubDialogueLUT>();
	}
}
