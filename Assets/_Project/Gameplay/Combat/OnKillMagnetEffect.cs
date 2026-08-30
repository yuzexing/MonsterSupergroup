using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

public class OnKillMagnetEffect : AttackHitParticleEffect, ILootColector
{
	public float pullArea = 2f;

	private PlayerCombatantBinding _combatantBinding;

	public PlayerCombatantBinding CombatantBinding => _combatantBinding;

	public override void Init(WeaponBehaviour behaviour)
	{
		base.Init(behaviour);
		_combatantBinding = behaviour != null ? behaviour.OwnerCombatant : null;
		LootManager.Instance.RegisterLootCollector(this);
	}

	public Vector2 GetLootCollectorPosition()
	{
		return base.transform.position;
	}

	public float GetLootPullArea()
	{
		return pullArea;
	}

	protected override void OnDisable()
	{
		LootManager.Instance.UnRegisterLootCollector(this);
		base.OnDisable();
	}
}
