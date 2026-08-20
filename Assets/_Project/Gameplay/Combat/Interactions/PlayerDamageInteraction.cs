using System.Collections;
using System.Collections.Generic;
using AstralShift.DebugTools;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class PlayerDamageInteraction : Interaction
	{
		[SerializeField]
		protected bool directDamage;

		[SerializeField]
		protected int damage = 1;

		[SerializeField]
		protected float stunTime;

		public EnemyStats enemyStats;

		public DamageType damageType;

		private Coroutine _collisionCheckCoroutine;

		private bool _hasCollidedWithPlayer;

		private readonly List<Transform> _collidedTransforms = new List<Transform>();

		private PlayerMovement _player;

		public static TimedCollection<int> damageablesToIgnore = new TimedCollection<int>();

		private void Awake()
		{
			_player = GameDirector.Instance.Player;
		}

		public override void Interact(IInteractor interactor)
		{
			if (damageablesToIgnore.Contains(GetInstanceID()))
			{
				base.Interact(interactor);
				OnEnd();
				return;
			}
			base.Interact(interactor);
			EnemyDamageableObject component2;
			if (interactor.Transform.TryGetComponent<PlayerHitbox>(out var _))
			{
				_hasCollidedWithPlayer = true;
				if (_collisionCheckCoroutine == null)
				{
					_collisionCheckCoroutine = StartCoroutine(VerifyCollisionsRoutine());
				}
			}
			else if (interactor.Transform.TryGetComponent<EnemyDamageableObject>(out component2) && !component2.IsDead)
			{
				if (component2.BlocksDamage)
				{
					_collidedTransforms.Add(interactor.Transform);
					if (_collisionCheckCoroutine == null)
					{
						_collisionCheckCoroutine = StartCoroutine(VerifyCollisionsRoutine());
					}
				}
				else
				{
					DamageObject(component2);
				}
			}
			OnEnd();
		}

		private void OnDisable()
		{
			if (_collisionCheckCoroutine != null)
			{
				VerifyCollisions();
				_collisionCheckCoroutine = null;
			}
		}

		public void DamagePlayer()
		{
			if (directDamage)
			{
				if (enemyStats != null)
				{
					int num = damage;
					damage = (int)((float)damage * enemyStats.DamageMultiplier);
					_player.Damage(damage, damageType);
					damage = num;
				}
				else
				{
					_player.Damage(damage, damageType);
				}
				if (stunTime != 0f)
				{
					_player.Stun(stunTime);
				}
			}
			else if (enemyStats == null)
			{
				DBL.Log(DBL.Module.EnemyAttacks, "EnemyStats is null! Can't damage.", 2);
			}
			else
			{
				_player.Damage(enemyStats.Damage, damageType);
				if (enemyStats.StunTime != 0f)
				{
					_player.Stun(enemyStats.StunTime);
				}
			}
		}

		private void DamageObject(EnemyDamageableObject enemyDamageableObject)
		{
			enemyDamageableObject.DamageObject(directDamage ? damage : enemyStats.Damage);
		}

		private IEnumerator VerifyCollisionsRoutine()
		{
			yield return new WaitForEndOfFrame();
			VerifyCollisions();
		}

		private void VerifyCollisions()
		{
			float maxDot;
			Transform closestCollidedTransform = GetClosestCollidedTransform(out maxDot);
			EnemyDamageableObject component2;
			if (_hasCollidedWithPlayer)
			{
				EnemyDamageableObject component;
				if (maxDot < 0f)
				{
					DamagePlayer();
				}
				else if ((bool)closestCollidedTransform && closestCollidedTransform.TryGetComponent<EnemyDamageableObject>(out component))
				{
					DamageObject(component);
				}
			}
			else if (maxDot >= 0f && (bool)closestCollidedTransform && closestCollidedTransform.TryGetComponent<EnemyDamageableObject>(out component2))
			{
				DamageObject(component2);
				damageablesToIgnore.Add(GetInstanceID());
			}
			_collidedTransforms.Clear();
			_collisionCheckCoroutine = null;
			_hasCollidedWithPlayer = false;
		}

		private Transform GetClosestCollidedTransform(out float maxDot)
		{
			Transform result = null;
			maxDot = -1f;
			Vector2 origin = GameDirector.Instance.Player.transform.position;
			Vector2 damagePosition = base.transform.position;
			foreach (Transform collidedTransform in _collidedTransforms)
			{
				float num = DotProductDamageAndObject(origin, damagePosition, collidedTransform.position);
				if (num > maxDot)
				{
					maxDot = num;
					result = collidedTransform;
				}
			}
			return result;
		}

		private float DotProductDamageAndObject(Vector2 origin, Vector2 damagePosition, Vector2 objectPosition)
		{
			Vector2 normalized = (objectPosition - origin).normalized;
			return Vector2.Dot((damagePosition - origin).normalized, normalized);
		}
	}
}
