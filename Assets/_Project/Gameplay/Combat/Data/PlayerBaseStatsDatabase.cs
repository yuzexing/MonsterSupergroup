using AstralShift.HellMaiden.Player;
using UnityEngine;

namespace Assets.Scripts.AstralShift.HellMaiden.Data
{
	[CreateAssetMenu(fileName = "New BaseStatsDB", menuName = "HellMaiden/Data/Player/BaseStatsDB")]
	public class PlayerBaseStatsDatabase : ScriptableObject
	{
		public PlayerStats.PlayerStatsValues values;
	}
}
