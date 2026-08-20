using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AstralShift.HellMaiden.Dialogue
{
	[CreateAssetMenu(fileName = "New NPC LUT", menuName = "HellMaiden/Dialogue/NPCLUT")]
	public class NPC_DS_LUT : SerializedScriptableObject
	{
		public Dictionary<PoetID, GameObject> LUT = new Dictionary<PoetID, GameObject>();
	}
}
