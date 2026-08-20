using AstralShift.HellMaiden.Common;
using AstralShift.HellMaiden.Interactions;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackDash : EnemyAttackMelee
	{
		public float distance;

		protected Vector2 startPoint;

		protected Vector2 endPoint;

		public Vector2 lastPosition;

		public AnimationCurve movementCurve;

		public Rigidbody2D rb;

		public Collider2D attackCollider;

		public PlayerDamageInteraction damageInteraction;

		public LayerMask dashExclusionLayerMask;

		public bool returnToInitialDashPosition;

		[ConditionalHide("returnToInitialDashPosition", true)]
		public AnimationCurve dashBacktCurve;

		public Transform sprite;

		public bool homingTarget;

		protected LayerMask defaultLayerMask;

		protected Collider2D collider;

		protected Vector2 _direction;

		protected Vector2 attackStartPosition;

		protected bool _returning;

		private void Start()
		{
			if (damageInteraction != null)
			{
				damageInteraction.enemyStats = base.controller.stats;
			}
			collider = base.controller.collider;
			defaultLayerMask = collider.excludeLayers;
		}

		public override void AttackWarningEnter()
		{
			base.AttackWarningEnter();
			if (base.controller.direction == Direction.Right)
			{
				base.controller.direction = Direction.Left;
			}
			else if (base.controller.direction == Direction.Left)
			{
				base.controller.direction = Direction.Right;
			}
			startPoint = rb.position;
			_direction = (Vector2)base.Target.position - startPoint;
			_direction.Normalize();
			_direction *= distance;
			endPoint = startPoint + _direction;
		}

		public override void AttackWarningTick()
		{
			base.AttackWarningTick();
			if (homingTarget)
			{
				_direction = base.Target.position - _attack.transform.position;
				float z = Mathf.Atan2(_direction.y, _direction.x) * 57.29578f;
				Quaternion to = Quaternion.Euler(0f, 0f, z);
				_attack.transform.rotation = Quaternion.RotateTowards(_attack.transform.rotation, to, 50f * Time.deltaTime);
			}
		}

		public override void AttackEnter()
		{
			rb.constraints = RigidbodyConstraints2D.FreezeRotation;
			lastPosition = rb.position;
			if ((bool)attackCollider)
			{
				attackCollider.enabled = true;
			}
			collider.excludeLayers = dashExclusionLayerMask;
			attackStartPosition = base.transform.position;
			base.AttackEnter();
		}

		public override void AttackTick()
		{
			float num = Time.time - _attackStartTime;
			if ((bool)_warning)
			{
				_warning.transform.position = attackStartPosition;
			}
			if (num < base.AttackTime)
			{
				float time = num / base.AttackTime;
				Vector2 vector;
				if (_returning && returnToInitialDashPosition)
				{
					float t = dashBacktCurve.Evaluate(time);
					vector = Vector2.Lerp(endPoint, startPoint, t);
				}
				else
				{
					float t = movementCurve.Evaluate(time);
					vector = Vector2.Lerp(startPoint, endPoint, t);
				}
				Vector2 linearVelocity = (vector - lastPosition) / Time.deltaTime;
				if (Time.deltaTime != 0f)
				{
					rb.linearVelocity = linearVelocity;
				}
				lastPosition = vector;
			}
			else
			{
				rb.linearVelocity = Vector2.zero;
				if (!_returning && returnToInitialDashPosition)
				{
					_returning = true;
					_attackStartTime = Time.time;
				}
			}
			base.AttackTick();
		}

		public override void AttackExit()
		{
			base.AttackExit();
			_returning = false;
			collider.excludeLayers = defaultLayerMask;
			if ((bool)attackCollider)
			{
				attackCollider.enabled = false;
			}
		}
	}
}
