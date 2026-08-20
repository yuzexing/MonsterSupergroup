using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class UltimateDamageAttack : BasePlayerAttack, IPausable
	{
		public Animator animator;

		public override void Attack()
		{
			_onStart?.Invoke();
			if (animator != null)
			{
				animator.Rebind();
				animator.Update(0f);
			}
		}

		public override void Dispose()
		{
		}

		public void onAttackAnimationEnd()
		{
			_onEnd?.Invoke();
		}

		private void Start()
		{
			SubscribeGameEvents();
		}

		private void OnDestroy()
		{
			UnSubscribeGameEvents();
		}

		private void SubscribeGameEvents()
		{
			((IPausable)this).Subscribe();
		}

		private void UnSubscribeGameEvents()
		{
			((IPausable)this).UnSubscribe();
		}

		public void OnPausePausables()
		{
			if (animator != null)
			{
				animator.updateMode = AnimatorUpdateMode.Normal;
			}
		}

		public void OnResumePausables()
		{
			if (animator != null)
			{
				animator.updateMode = AnimatorUpdateMode.UnscaledTime;
			}
		}

		public void OnGamePause()
		{
			if (animator != null)
			{
				animator.updateMode = AnimatorUpdateMode.Normal;
			}
		}

		public void OnGameResume()
		{
			if (animator != null)
			{
				animator.updateMode = AnimatorUpdateMode.UnscaledTime;
			}
		}
	}
}
