using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Player;

public class UltimateItem : WorldItem
{
	protected override void OnEnable()
	{
		_worldItemPool = PoolManager.Instance.ItemsPool.UltimatePowerup;
		base.OnEnable();
		base.OnStartPlayerPull += LaunchSpellCardsTutorial;
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		base.OnStartPlayerPull -= LaunchSpellCardsTutorial;
	}

	public override void Consume()
	{
		LootManager.Instance.UltimateCurrentlySpawned = false;
		GameDirector.Instance.Player.GainUltimateCharge();
		base.Consume();
	}

	private void LaunchSpellCardsTutorial()
	{
		if (!PlayerState.IsBusy())
		{
			base.OnStartPlayerPull -= LaunchSpellCardsTutorial;
			TutorialManager.Instance.SLM.TryLaunchSpellCardsTutorial(null);
		}
	}
}
