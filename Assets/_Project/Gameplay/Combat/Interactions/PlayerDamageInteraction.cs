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

		private readonly List<PlayerHitbox> _collidedPlayerHitboxes = new List<PlayerHitbox>();

		private readonly List<Transform> _collidedTransforms = new List<Transform>();

		public static TimedCollection<int> damageablesToIgnore = new TimedCollection<int>();

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
			if (interactor.Transform.TryGetComponent<PlayerHitbox>(out var playerHitbox))
			{
				if (playerHitbox.IsLocallyControlled &&
					!_collidedPlayerHitboxes.Contains(playerHitbox))
				{
					_collidedPlayerHitboxes.Add(playerHitbox);
					if (_collisionCheckCoroutine == null)
					{
						_collisionCheckCoroutine = StartCoroutine(VerifyCollisionsRoutine());
					}
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

		public void DamagePlayer(PlayerHitbox playerHitbox)
		{
			if (playerHitbox == null ||
				!playerHitbox.TryGetOwner(out PlayerCombatantBinding binding) ||
				!binding.AcceptsLocalMutations || binding.PlayerMovement == null)
			{
				return;
			}

			PlayerMovement player = binding.PlayerMovement;
			if (directDamage)
			{
				int resolvedDamage = damage;
				if (enemyStats != null)
				{
					resolvedDamage = (int)((float)resolvedDamage * enemyStats.DamageMultiplier);
				}
				player.Damage(resolvedDamage, damageType);
				if (stunTime != 0f)
				{
					player.Stun(stunTime);
				}
			}
			else if (enemyStats == null)
			{
				DBL.Log(DBL.Module.EnemyAttacks, "EnemyStats is null! Can't damage.", 2);
			}
			else
			{
				player.Damage(enemyStats.Damage, damageType);
				if (enemyStats.StunTime != 0f)
				{
					player.Stun(enemyStats.StunTime);
				}
			}
		}

		private void DamageObject(EnemyDamageableObject enemyDamageableObject)
		{
			enemyDamageableObject.DamageObject(directDamage ? damage : enemyStats.Damage);
		}

		private IEnumerator VerifyCollisionsRoutine()
		{
			// Defer one logic frame so all collision callbacks from the current
			// physics step are collected before choosing the damage target. Unlike
			// WaitForEndOfFrame, this also progresses in headless/batchmode clients.
			yield return null;
			VerifyCollisions();
		}

		private void VerifyCollisions()
		{
			var damagedObjects = new HashSet<EnemyDamageableObject>();
			bool processedLocalPlayer = false;
			for (int i = 0; i < _collidedPlayerHitboxes.Count; i++)
			{
				PlayerHitbox playerHitbox = _collidedPlayerHitboxes[i];
				if (playerHitbox == null ||
					!playerHitbox.TryGetOwner(out PlayerCombatantBinding binding) ||
					!binding.AcceptsLocalMutations || binding.PlayerMovement == null)
				{
					continue;
				}

				processedLocalPlayer = true;
				Transform closestCollidedTransform = GetClosestCollidedTransform(
					binding.PlayerMovement.transform.position,
					out float maxDot);
				if (maxDot < 0f)
				{
					DamagePlayer(playerHitbox);
				}
				else if ((bool)closestCollidedTransform &&
					closestCollidedTransform.TryGetComponent(out EnemyDamageableObject damageable) &&
					damagedObjects.Add(damageable))
				{
					DamageObject(damageable);
				}
			}

			if (!processedLocalPlayer && TryGetClosestDamageable(out EnemyDamageableObject closestDamageable))
			{
				DamageObject(closestDamageable);
				damageablesToIgnore.Add(GetInstanceID());
			}

			_collidedPlayerHitboxes.Clear();
			_collidedTransforms.Clear();
			_collisionCheckCoroutine = null;
		}

		private Transform GetClosestCollidedTransform(Vector2 origin, out float maxDot)
		{
			Transform result = null;
			maxDot = -1f;
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

		private bool TryGetClosestDamageable(out EnemyDamageableObject result)
		{
			result = null;
			float closestSqrDistance = float.MaxValue;
			for (int i = 0; i < _collidedTransforms.Count; i++)
			{
				Transform candidate = _collidedTransforms[i];
				if (candidate == null ||
					!candidate.TryGetComponent(out EnemyDamageableObject damageable))
				{
					continue;
				}

				float sqrDistance = (candidate.position - transform.position).sqrMagnitude;
				if (sqrDistance < closestSqrDistance)
				{
					closestSqrDistance = sqrDistance;
					result = damageable;
				}
			}

			return result != null;
		}

		private float DotProductDamageAndObject(Vector2 origin, Vector2 damagePosition, Vector2 objectPosition)
		{
			Vector2 normalized = (objectPosition - origin).normalized;
			return Vector2.Dot((damagePosition - origin).normalized, normalized);
		}
	}
}
