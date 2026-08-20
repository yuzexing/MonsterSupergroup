using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

public class OnKillMagnetEffect : AttackHitParticleEffect, ILootColector
{
	public float pullArea = 2f;

	public override void Init(WeaponBehaviour behaviour)
	{
		base.Init(behaviour);
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
