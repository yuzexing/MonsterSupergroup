using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class PlayerAttacksPauser : MonoBehaviour, IPausable
	{
		private void Start()
		{
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
		}

		private void UnSubscribeGameEvents()
		{
			((IPausable)this).UnSubscribe();
		}

		private void OnDestroy()
		{
			UnSubscribeGameEvents();
		}
	}
}
