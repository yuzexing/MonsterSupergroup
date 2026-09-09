using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Helpers.Attributes;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

public class PlayerAttackOvertimeHitBoxProgressionScaler : CustomProgressionScaler
{
	[SerializeField]
	private PlayerAttackOvertimeHitBox hitbox;

	[SerializeField]
	private float defaultHitInterval;

	[SerializeField]
	private float scallingFactor;

	[ReadOnly]
	public float currentHitInterval;

	public bool clampMin;

	[ConditionalHide("clampMin", true)]
	public float valueMin;

	public bool clampMax;

	[ConditionalHide("clampMax", true)]
	public float valueMax;

	private float _percentageMultiplier;

	public override void Apply(float percentageMultiplier)
	{
		if (!(hitbox == null))
		{
			_percentageMultiplier = percentageMultiplier;
			float value = defaultHitInterval / (1f + _percentageMultiplier * scallingFactor);
			value = Mathf.Clamp(value, clampMin ? valueMin : 0.01f, clampMax ? valueMax : float.PositiveInfinity);
			currentHitInterval = value;
			hitbox.SetHitInterval(currentHitInterval);
		}
	}

	public override void SetDefaults()
	{
		hitbox.SetHitInterval(defaultHitInterval);
	}
}
