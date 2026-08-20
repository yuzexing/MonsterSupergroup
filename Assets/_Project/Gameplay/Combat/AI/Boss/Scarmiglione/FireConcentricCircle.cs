using System;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss.Scarmiglione
{
	public class FireConcentricCircle : AnimatedBossAttack
	{
		public float scaleSpeed = 1f;

		public Action onEnd;

		private void FixedUpdate()
		{
			Vector3 localScale = base.transform.localScale;
			float num = scaleSpeed * Time.deltaTime;
			localScale -= Vector3.one * num;
			localScale = Vector3.Max(localScale, Vector3.zero);
			base.transform.localScale = localScale;
			if (base.transform.localScale.x < 1f)
			{
				Despawn();
			}
		}

		private void Despawn()
		{
			RunOutAnimation(onEnd);
		}
	}
}
