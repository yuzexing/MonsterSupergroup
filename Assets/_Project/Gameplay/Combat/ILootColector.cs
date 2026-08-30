using AstralShift.HellMaiden.Player;
using UnityEngine;

public interface ILootColector
{
	PlayerCombatantBinding CombatantBinding { get; }

	float GetLootPullArea();

	Vector2 GetLootCollectorPosition();
}
