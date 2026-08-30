using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class StraightProjectileBehaviour : MonoBehaviour
    {
        private readonly HashSet<int> hitTargets = new HashSet<int>();

        private Rigidbody2D body;
        private WeaponRuntimeBehaviour weapon;
        private AttackSnapshotLease attackLease;
        private CombatTeam ownerTeam;
        private Vector2 direction;
        private float speed;
        private float remainingLifetime;
        private int remainingHits;
        private Action<StraightProjectileBehaviour> onFinished;
        private bool initialized;
        private bool finished;
        private Vector3 baseScale;

        public Vector2 Direction => direction;
        internal int PoolKey { get; private set; }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.useFullKinematicContacts = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            GetComponent<Collider2D>().isTrigger = true;
            baseScale = transform.localScale;
        }

        internal void PrepareForPoolSpawn(int poolKey, Vector3 position, Quaternion rotation)
        {
            PoolKey = poolKey;
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = baseScale;
            gameObject.SetActive(true);
        }

        public void Initialize(
            WeaponRuntimeBehaviour sourceWeapon,
            AttackSnapshot attackSnapshot,
            CombatTeam sourceTeam,
            Vector2 movementDirection,
            float movementSpeed,
            int hitCount,
            bool rotateToMovement,
            Action<StraightProjectileBehaviour> finishedCallback)
        {
            weapon = sourceWeapon ?? throw new ArgumentNullException(nameof(sourceWeapon));
            if (attackSnapshot == null)
            {
                throw new ArgumentNullException(nameof(attackSnapshot));
            }

            attackLease?.Dispose();
            attackLease = attackSnapshot.Retain();
            if (sourceTeam == CombatTeam.Neutral)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceTeam));
            }

            if (movementDirection.sqrMagnitude <= 0.000001f)
            {
                throw new ArgumentException("Projectile direction cannot be zero.", nameof(movementDirection));
            }

            if (movementSpeed <= 0f || float.IsNaN(movementSpeed) || float.IsInfinity(movementSpeed))
            {
                throw new ArgumentOutOfRangeException(nameof(movementSpeed));
            }

            if (hitCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(hitCount));
            }

            ownerTeam = sourceTeam;
            direction = movementDirection.normalized;
            speed = movementSpeed;
            remainingHits = hitCount;
            remainingLifetime = Mathf.Max(0.01f, attackSnapshot.Stats.Duration);
            onFinished = finishedCallback;
            hitTargets.Clear();
            finished = false;
            initialized = true;
            transform.localScale = baseScale * Mathf.Max(0.01f, attackSnapshot.Stats.Size);

            if (rotateToMovement)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void FixedUpdate()
        {
            if (!initialized || finished)
            {
                return;
            }

            remainingLifetime -= Time.fixedDeltaTime;
            if (remainingLifetime <= 0f)
            {
                Finish();
                return;
            }

            body.MovePosition(body.position + direction * (speed * Time.fixedDeltaTime));
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!initialized || finished)
            {
                return;
            }

            CombatTeamBehaviour candidate = other.GetComponentInParent<CombatTeamBehaviour>();
            if (candidate == null || candidate.Team == CombatTeam.Neutral || candidate.Team == ownerTeam)
            {
                return;
            }

            CombatantBehaviour combatant = candidate.Combatant;
            if (combatant == null || !combatant.IsAlive || !hitTargets.Add(combatant.GetInstanceID()))
            {
                return;
            }

            weapon.ResolveHit(attackLease.Snapshot, combatant);
            remainingHits--;
            if (remainingHits <= 0)
            {
                Finish();
            }
        }

        public void Cancel()
        {
            Finish();
        }

        private void Finish()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            initialized = false;
            attackLease?.Dispose();
            attackLease = null;
            Action<StraightProjectileBehaviour> callback = onFinished;
            onFinished = null;
            callback?.Invoke(this);
            if (callback == null)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (!finished)
            {
                finished = true;
                attackLease?.Dispose();
                attackLease = null;
                Action<StraightProjectileBehaviour> callback = onFinished;
                onFinished = null;
                callback?.Invoke(this);
            }
        }
    }
}
