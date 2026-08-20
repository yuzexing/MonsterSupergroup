using System;
using UnityEngine;

namespace AstralShift.HellMaiden.CameraFX
{
	public class UIPoetDeathEffect : FullscreenEffect
	{
		[SerializeField]
		private Animator animator;

		public override void Trigger()
		{
			animator.Play("PoetDeath");
		}

		public override void Enable()
		{
			throw new NotImplementedException();
		}

		public override void Disable()
		{
			throw new NotImplementedException();
		}
	}
}
