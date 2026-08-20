using System;
using Animancer;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class Enemy3DAnimator : EnemyAnimator
	{
		[SerializeField]
		private ClipTransition moving;

		[SerializeField]
		private ClipTransition death;

		[SerializeField]
		private Transform model;

		public float offset = 90f;

		private Transform target;

		public override void Init(EnemyController controller)
		{
			_controller = controller;
			target = controller.Target;
		}

		public override void Movement(float x, float y)
		{
			ResumeAnimator();
			Vector2 vector = target.position - base.transform.position;
			vector.Normalize();
			float y2 = Mathf.Atan2(vector.x, vector.y) * 57.29578f + offset;
			model.localRotation = Quaternion.Euler(0f, y2, 0f);
		}

		public override void AttackWarning(float x, float y)
		{
		}

		public override void Attack(float x, float y)
		{
		}

		public override void Hurt(float x, float y)
		{
		}

		public override AnimancerState Dead(float x, float y)
		{
			PauseAnimator();
			return animancer.Layers[0].Play(death, 0f);
		}

		public override void Recovery(float x, float y)
		{
		}

		public override void ResetHurtBlinkColor()
		{
		}

		public override void DeathAnimation(Vector2 direction, Action onEnd)
		{
			onEnd?.Invoke();
		}
	}
}
