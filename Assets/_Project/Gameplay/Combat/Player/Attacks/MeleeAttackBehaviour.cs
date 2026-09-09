using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using AstralShift.QTI.Helpers.Attributes;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class MeleeAttackBehaviour : WeaponBehaviour
	{
		[Header("Attack Settings")]
		public AnimatedAttack prefab;
		public float spawnRadius = 1f;
		public bool overrideAnimationLength;
		[ConditionalHide("overrideAnimationLength", true)]
		public float animationLength = 3f;
		public float multiProjectilesInterval = 0.1f;

		private GenericPooler<AnimatedAttack> _pooler;
		private Transform _checkoutRoot;
		private Coroutine _attackCoroutine;
		private AttackSnapshotLease _burstLease;
		private bool _burstActive;
		private bool _presentationReplicaInitialized;
		private readonly Dictionary<AnimatedAttack, MeleePresentationKey> _attacks =
			new Dictionary<AnimatedAttack, MeleePresentationKey>();

		public event Action<MeleePresentationSpawn> PresentationSpawned;
		public event Action<MeleePresentationTermination> PresentationTerminated;

		public override void Init(uint id, AttackStats stats) => base.Init(id, stats);

		public override void InitNative(uint id)
		{
			base.InitNative(id);
			InitializePool();
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
		}

		public override float GetAttackSequenceDuration() => ProjectileCountValue > 1
			? ProjectileCountValue * Mathf.Max(0f, multiProjectilesInterval)
			: 0f;

		public override void RestoreCooldownRemaining(float remainingSeconds)
		{
			if (float.IsNaN(remainingSeconds) || float.IsInfinity(remainingSeconds) || remainingSeconds < 0f)
				throw new ArgumentOutOfRangeException(nameof(remainingSeconds));
			// A restored burst is not replayed, but its remaining launch delay still precedes cooldown.
			LastAttackElapsedTime = GetCooldown() - remainingSeconds;
		}

		private void InitializePool()
		{
			if (prefab == null) throw new InvalidOperationException("Melee weapon requires an AnimatedAttack prefab.");
			_pooler = PoolManager.Instance.GetOrCreatePooler(prefab);
			if (_checkoutRoot == null)
			{
				var checkout = new GameObject("Melee Pool Checkout");
				checkout.SetActive(false);
				checkout.transform.SetParent(transform, false);
				_checkoutRoot = checkout.transform;
			}
		}

		private void Update()
		{
			if (!_burstActive && CheckCooldown()) Attack();
			LastAttackElapsedTime += Time.deltaTime;
		}

		public override void Attack()
		{
			if (!CanAttack || _burstActive || _presentationReplicaInitialized) return;
			using (AttackSnapshot attack = BeginNativeGasAttack())
			{
				if (attack.Stats.ProjectileCount < 1 || attack.Stats.ProjectileCount > ushort.MaxValue + 1)
					throw new InvalidOperationException("Melee slash count cannot be represented by the presentation protocol.");
				_burstLease = attack.Retain();
				_burstActive = true;
				try
				{
					PlayAttackSound();
					// StartCoroutine can finish synchronously for one slash. Do not retain its stale handle.
					Coroutine running = StartCoroutine(AttackCoroutine(attack, player.attackDirection));
					if (_burstActive) _attackCoroutine = running;
				}
				catch
				{
					CancelAttacks();
					throw;
				}
			}
		}

		private IEnumerator AttackCoroutine(AttackSnapshot attack, Vector2 direction)
		{
			try
			{
				int count = attack.Stats.ProjectileCount;
				var interval = new WaitForSeconds(Mathf.Max(0f, multiProjectilesInterval));
				for (int i = 0; i < count; i++)
				{
					SpawnAttack(attack, i, count == 1 ? 0f : (i - count / 2) * 45f, direction);
					// Preserve the source burst's final interval before cooldown begins.
					if (count > 1) yield return interval;
				}
			}
			finally
			{
				_burstLease?.Dispose();
				_burstLease = null;
				_burstActive = false;
				_attackCoroutine = null;
				LastAttackElapsedTime = 0f;
			}
		}

		private void SpawnAttack(AttackSnapshot snapshot, int index, float angle, Vector2 direction)
		{
			Vector2 slashDirection = Quaternion.AngleAxis(
				Vector2.SignedAngle(direction, Vector2.right) + angle, -Vector3.forward) * Vector3.right;
			var key = new MeleePresentationKey(snapshot.Context.EventId.Value, (ushort)index);
			AnimatedAttack attack = GetOrCreateAttack(key);
			try
			{
				attack.InitNative(this, snapshot, null, () => ReturnAttack(attack));
				if (attack.hitbox != null) attack.hitbox.enabled = true;
				attack.transform.localPosition = (Vector3)slashDirection.normalized * spawnRadius;
				float duration = overrideAnimationLength ? animationLength : -1f;
				PresentationSpawned?.Invoke(new MeleePresentationSpawn(ID, key,
					attack.transform.localPosition, slashDirection, duration,
					ProjectilePresentationStats.From(snapshot.Stats, 0f)));
				attack.gameObject.SetActive(true);
				if (overrideAnimationLength) attack.Attack(slashDirection, duration);
				else attack.Attack(slashDirection);
			}
			catch
			{
				ReturnAttack(attack);
				throw;
			}
		}

		private AnimatedAttack GetOrCreateAttack(MeleePresentationKey key)
		{
			// A source prefab can be active. Checkout under an inactive parent prevents
			// audio/animation OnEnable from running before its native or visual binding.
			AnimatedAttack attack = _pooler.GetOrCreate(_checkoutRoot);
			attack.gameObject.SetActive(false);
			attack.transform.SetParent(transform, false);
			attack.transform.localPosition = prefab.transform.localPosition;
			attack.transform.localRotation = prefab.transform.localRotation;
			attack.transform.localScale = prefab.transform.localScale;
			_attacks.Add(attack, key);
			return attack;
		}

		private void ReturnAttack(AnimatedAttack attack)
		{
			if (!_attacks.TryGetValue(attack, out MeleePresentationKey key)) return;
			_attacks.Remove(attack);
			if (attack != null)
			{
				attack.Dispose();
				_pooler?.Return(attack);
			}
			if (!_presentationReplicaInitialized)
				PresentationTerminated?.Invoke(new MeleePresentationTermination(ID, key));
		}

		public void InitializePresentationReplica(uint weaponId, PlayerMovement owner)
		{
			if (_presentationReplicaInitialized) return;
			ConfigureOwner(owner);
			_id = weaponId;
			InitializePool();
			_presentationReplicaInitialized = true;
			enabled = false;
		}

		public AnimatedAttack PlayPresentation(MeleePresentationSpawn spawn, float elapsedSeconds,
			Action<AnimatedAttack> onReturned = null)
		{
			if (!_presentationReplicaInitialized || spawn.WeaponId != ID)
				throw new InvalidOperationException("Melee presentation replica is not initialized for this weapon.");
			if (elapsedSeconds >= prefab.GetPresentationDuration(spawn.AnimationDuration)) return null;
			AnimatedAttack attack = GetOrCreateAttack(spawn.Key);
			try
			{
				attack.InitPresentation(this, spawn.Stats, null, () =>
				{
					ReturnAttack(attack);
					onReturned?.Invoke(attack);
				});
				if (attack.hitbox != null) attack.hitbox.enabled = false;
				attack.transform.localPosition = spawn.LocalPosition;
				attack.gameObject.SetActive(true);
				attack.PlayPresentation(spawn.Direction, spawn.AnimationDuration, Mathf.Max(0f, elapsedSeconds));
				return _attacks.ContainsKey(attack) ? attack : null;
			}
			catch
			{
				ReturnAttack(attack);
				throw;
			}
		}

		public bool TerminatePresentation(MeleePresentationKey key)
		{
			foreach (var entry in _attacks)
			{
				if (!entry.Value.Equals(key)) continue;
				ReturnAttack(entry.Key);
				return true;
			}
			return false;
		}

		public void DisposePresentationReplica()
		{
			if (!_presentationReplicaInitialized) return;
			CancelAttacks();
			_presentationReplicaInitialized = false;
			_pooler = null;
		}

		private void CancelAttacks()
		{
			if (_attackCoroutine != null) StopCoroutine(_attackCoroutine);
			_attackCoroutine = null;
			_burstLease?.Dispose();
			_burstLease = null;
			_burstActive = false;
			foreach (AnimatedAttack attack in new List<AnimatedAttack>(_attacks.Keys))
				ReturnAttack(attack);
		}

		private void OnDisable()
		{
			// Disabling automatic execution for a build update preserves attacks already in flight.
			if (!gameObject.activeInHierarchy) CancelAttacks();
		}

		private void OnDestroy() => Dispose();

		protected override void Dispose()
		{
			CancelAttacks();
			LastAttackElapsedTime = 0f;
		}
	}
}
