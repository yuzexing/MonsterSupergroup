using AstralShift.HellMaiden.Player;
using AstralShift.QTI.Interactors;
using UnityEngine;

public class PlayerHitbox : Interactor
{
	[SerializeField]
	private PlayerCombatantBinding owner;

	public PlayerCombatantBinding Owner
	{
		get
		{
			ResolveOwner();
			return owner;
		}
	}

	public bool IsLocallyControlled => Owner != null && Owner.AcceptsLocalMutations;

	public void Configure(PlayerCombatantBinding playerOwner)
	{
		owner = playerOwner;
	}

	public bool TryGetOwner(out PlayerCombatantBinding playerOwner)
	{
		playerOwner = Owner;
		return playerOwner != null;
	}

	private void OnEnable()
	{
		ResolveOwner();
	}

	private void ResolveOwner()
	{
		if (owner == null)
		{
			owner = GetComponentInParent<PlayerCombatantBinding>();
		}
	}
}
