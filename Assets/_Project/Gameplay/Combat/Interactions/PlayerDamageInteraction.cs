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
        private AstralShift.HellMaiden.AI.EnemyAttackWindow attackWindow;
        private AstralShift.HellMaiden.AI.EnemyContactStamp pendingAttackContact;
        public AstralShift.HellMaiden.AI.EnemyContactStamp LastAttackContact => pendingAttackContact;
        public void ConfigureAttackWindow(AstralShift.HellMaiden.AI.EnemyAttackWindow window)
        {
            if (attackWindow != window) DiscardPendingCollisions();
            attackWindow = window;
            FlushPendingCollisionsOnDisable = window == null;
        }
		[SerializeField]
		protected bool directDamage;

		[SerializeField]
		protected int damage = 1;

		[SerializeField]
		protected float stunTime;

		public EnemyStats enemyStats;

		public DamageType damageType;
        public static event System.Action<PlayerDamageInteraction, PlayerMovement, int> DamageAttempted;
        private bool localProjectileMode, localProjectileConsumed;
        private System.Action localProjectileHit;
        private System.Action<PlayerHitbox> requestProjectileHit;

        public void ConfigureLocalProjectile(int amount, float stun, System.Action onHit, System.Action<PlayerHitbox> requestHit = null)
        {
            DiscardPendingCollisions();
            localProjectileMode = true;
            localProjectileConsumed = false;
            directDamage = true; damage = amount; stunTime = stun; enemyStats = null;
            damageType = DamageType.Projectile;
            localProjectileHit = onHit;
            requestProjectileHit = requestHit;
            FlushPendingCollisionsOnDisable = false;
        }

		// Active attack windows retain their legacy final-frame settlement.
		// Continuous contact can opt out without depending on OnDisable order.
		public bool FlushPendingCollisionsOnDisable { get; set; } = true;

		private Coroutine _collisionCheckCoroutine;

		private readonly List<PlayerHitbox> _collidedPlayerHitboxes = new List<PlayerHitbox>();

		private readonly List<Transform> _collidedTransforms = new List<Transform>();
		private readonly HashSet<PlayerCombatantBinding> _processedPlayerOwners = new HashSet<PlayerCombatantBinding>();

		public static TimedCollection<int> damageablesToIgnore = new TimedCollection<int>();

		public override void Interact(IInteractor interactor)
		{
            if (attackWindow != null)
            {
                if (!attackWindow.TryCapture(out var stamp)) return;
                pendingAttackContact = stamp;
            }
            if (localProjectileMode)
            {
                if (!isActiveAndEnabled || localProjectileConsumed ||
                    !interactor.Transform.TryGetComponent<PlayerHitbox>(out var localHitbox) || !localHitbox.IsLocallyControlled) return;
                localProjectileConsumed = true;
                if (requestProjectileHit != null) { requestProjectileHit(localHitbox); return; }
                DamagePlayer(localHitbox);
                // Remote hitboxes never reach OnEnd (which consumes the legacy bullet).
                localProjectileHit?.Invoke();
                return;
            }
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
            // Parent/child OnDisable ordering is not guaranteed. A disabled enemy
            // must never flush deferred hits while its attack is being released.
            var enemy = GetComponentInParent<AstralShift.HellMaiden.AI.Enemy.EnemyController>(true);
            if (!FlushPendingCollisionsOnDisable || (enemy != null &&
                (!enemy.gameObject.activeSelf || !enemy.gameObject.activeInHierarchy || !enemy.enabled)))
			{
				DiscardPendingCollisions();
				return;
			}
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
                DamageAttempted?.Invoke(this, player, resolvedDamage);
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
                DamageAttempted?.Invoke(this, player, enemyStats.Damage);
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

		public void DiscardPendingCollisions()
		{
			if (_collisionCheckCoroutine != null) StopCoroutine(_collisionCheckCoroutine);
			_collisionCheckCoroutine = null;
			_collidedPlayerHitboxes.Clear();
			_collidedTransforms.Clear();
		}

        // A normal window boundary settles contacts that occurred while the window was open.
        // Death, authority loss and cancellation must continue to call DiscardPendingCollisions.
        public void SettlePendingCollisions()
        {
            if (_collisionCheckCoroutine != null) StopCoroutine(_collisionCheckCoroutine);
            _collisionCheckCoroutine = null;
            if (_collidedPlayerHitboxes.Count > 0 || _collidedTransforms.Count > 0) VerifyCollisions();
        }

		private void VerifyCollisions()
		{
			using var diagnosticScope = AstralShift.DebugTools.CombatPerformanceCounters.Measure(AstralShift.DebugTools.CombatPerformanceCounters.Area.CollisionChecks);
            if (attackWindow != null && !attackWindow.CanSettle(pendingAttackContact))
            { DiscardPendingCollisions(); return; }
			_processedPlayerOwners.Clear();
			var damagedObjects = new HashSet<EnemyDamageableObject>();
			bool processedLocalPlayer = false;
			for (int i = 0; i < _collidedPlayerHitboxes.Count; i++)
			{
				PlayerHitbox playerHitbox = _collidedPlayerHitboxes[i];
				if (playerHitbox == null ||
					!playerHitbox.TryGetOwner(out PlayerCombatantBinding binding) ||
					!binding.AcceptsLocalMutations || binding.PlayerMovement == null ||
					!_processedPlayerOwners.Add(binding))
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
			_processedPlayerOwners.Clear();
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
