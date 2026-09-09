using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class PlayerAttacksPauser : MonoBehaviour, IPausable
	{
		private bool subscribed;
		private void Start()
		{
			// Native network weapons are gated by their owning Build. A process-wide
			// pause callback must never create or operate the old singleton Hand.
			if (GetComponentInParent<MonsterSupergroup.Gameplay.Combat.PlayerBuildRuntime>() != null) return;
			SubscribeGameEvents();
		}

		public void OnPausePausables()
		{
			PlayerHand.Instance.DeactivateWeapons();
		}

		public void OnResumePausables()
		{
			PlayerHand.Instance.ActivateWeapons();
		}

		private void SubscribeGameEvents()
		{
			((IPausable)this).Subscribe();
			subscribed = true;
		}

		private void UnSubscribeGameEvents()
		{
			if (!subscribed) return;
			((IPausable)this).UnSubscribe();
			subscribed = false;
		}

		private void OnDestroy()
		{
			UnSubscribeGameEvents();
		}
	}
}
