using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class CirclingAttackBehaviour : WeaponBehaviour
	{
		[Header("Attack Settings")]
		public AnimatedAttack attackPrefab;
		public float baseRadius = 1.5f;
		public float baseSpeed = 2f;
		// Preserved serialized source data; the source also uses the inherited 1 / SpeedValue cooldown.
		public float baseCooldown = 5f;

		private readonly Dictionary<AnimatedAttack, OrbitInstance> _attacks = new Dictionary<AnimatedAttack, OrbitInstance>();
		private GenericPooler<AnimatedAttack> _pooler;
		private Transform _checkoutRoot;
		private Coroutine _movementCoroutine;
		private float _elapsedAttackTime;
		private bool _motionActive;
		private bool _spawning;
		private bool _cancelling;
		private bool _presentationReplicaInitialized;
		private uint _attackGeneration;

		public event Action<OrbitPresentationSpawn> PresentationSpawned;
		public event Action<OrbitPresentationHiding> PresentationHiding;
		public event Action<OrbitPresentationTermination> PresentationTerminated;
		public int ActiveOrbCount => _attacks.Count;

		public override void Init(uint id, AttackStats stats) => base.Init(id, stats);

		public override void InitNative(uint id)
		{
			CancelAttacks();
			base.InitNative(id);
			InitializePool();
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
		}

		public override float GetAttackSequenceDuration() => DurationValue;

		public override void RestoreCooldownRemaining(float remainingSeconds)
		{
			if (!IsFinite(remainingSeconds) || remainingSeconds < 0f)
				throw new ArgumentOutOfRangeException(nameof(remainingSeconds));
			LastAttackElapsedTime = GetCooldown() - remainingSeconds;
		}

		private void InitializePool()
		{
			if (attackPrefab == null) throw new InvalidOperationException("Circling weapon requires an AnimatedAttack prefab.");
			_pooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			if (_checkoutRoot == null)
			{
				var checkout = new GameObject("Orbit Pool Checkout");
				checkout.SetActive(false);
				checkout.transform.SetParent(transform, false);
				_checkoutRoot = checkout.transform;
			}
		}

		private void Update()
		{
			if (_presentationReplicaInitialized || _motionActive) return;
			if (CheckCooldown()) Attack();
			else LastAttackElapsedTime += Time.deltaTime;
		}

		public override void Attack()
		{
			if (!gameObject.activeInHierarchy || _presentationReplicaInitialized || _spawning || _cancelling ||
				_motionActive || !CanAttack) return;
			_spawning = true;
			try
			{
				// At high speed the next root can start during the previous root's Hide animation.
				// End those old instances first; never rebind a still-retained old snapshot to the new root.
				CancelAttacks();
				uint generation = _attackGeneration;
				using (AttackSnapshot snapshot = BeginNativeGasAttack())
				{
					if (Cancelled(generation)) return;
					int count = snapshot.Stats.ProjectileCount;
					float radius = snapshot.Stats.Size * baseRadius;
					float speed = snapshot.Stats.Speed * baseSpeed;
					float duration = snapshot.Stats.Duration;
					if (count < 1 || count > ushort.MaxValue + 1 || !IsFinite(radius) || radius < 0f ||
						!IsFinite(speed) || !IsFinite(duration) || duration < 0f ||
						!OrbitPresentationSpawn.IsFinitePhase(2f * Mathf.PI, speed, duration))
						throw new InvalidOperationException("Invalid Circling attack orbit data.");
					_motionActive = true;
					_elapsedAttackTime = 0f;
					LastAttackElapsedTime = 0f;
					for (int index = 0; index < count; index++)
					{
						if (Cancelled(generation)) return;
						var spawn = new OrbitPresentationSpawn(ID,
							new OrbitPresentationKey(snapshot.Context.EventId.Value, (ushort)index), count,
							2f * Mathf.PI * index / count, radius, speed, duration,
							ProjectilePresentationStats.From(snapshot.Stats, 0f));
						AnimatedAttack attack = Checkout(spawn, null);
						attack.InitNative(this, snapshot, null, () => ReturnAttack(attack));
						attack.Deactivated += HandleAttackDeactivated;
						if (attack.hitbox != null) attack.hitbox.enabled = true;
						PositionOrb(attack, _attacks[attack], 0f);
						PresentationSpawned?.Invoke(spawn);
						if (Cancelled(generation)) return;
						if (!_attacks.ContainsKey(attack)) continue;
						attack.gameObject.SetActive(true);
						if (Cancelled(generation)) return;
						if (_attacks.ContainsKey(attack)) attack.PlayExternallyTimedAnimation();
					}
					if (Cancelled(generation)) return;
					Coroutine running = StartCoroutine(Movement(duration, generation));
					if (_motionActive) _movementCoroutine = running;
				}
			}
			catch { CancelAttacks(); throw; }
			finally { _spawning = false; }
		}

		private bool Cancelled(uint generation)
		{
			if (!gameObject.activeInHierarchy) { CancelAttacks(); return true; }
			return generation != _attackGeneration;
		}

		private IEnumerator Movement(float duration, uint generation)
		{
			try
			{
				while (_elapsedAttackTime < duration)
				{
					if (Cancelled(generation)) yield break;
					// Preserve the source's smoothDeltaTime integration, including its last-frame overshoot.
					_elapsedAttackTime += Time.smoothDeltaTime;
					foreach (var entry in _attacks) PositionOrb(entry.Key, entry.Value, _elapsedAttackTime);
					LastAttackElapsedTime = 0f;
					yield return null;
				}
				foreach (AnimatedAttack attack in new List<AnimatedAttack>(_attacks.Keys))
				{
					if (Cancelled(generation)) yield break;
					if (!_attacks.TryGetValue(attack, out OrbitInstance orbit)) continue;
					orbit.Hiding = true;
					orbit.Elapsed = _elapsedAttackTime;
					PresentationHiding?.Invoke(new OrbitPresentationHiding(ID, orbit.Spawn.Key, _elapsedAttackTime));
					if (Cancelled(generation)) yield break;
					if (_attacks.ContainsKey(attack)) attack.PlayExternallyTimedEnd();
				}
			}
			finally
			{
				// A synchronous notification may cancel this root and start another one.
				// Its movement clock belongs to the newer generation.
				if (generation == _attackGeneration)
				{
					_motionActive = false;
					_movementCoroutine = null;
					LastAttackElapsedTime = 0f;
				}
			}
		}

		private AnimatedAttack Checkout(OrbitPresentationSpawn spawn, Action<AnimatedAttack> onReturned)
		{
			AnimatedAttack attack = _pooler.GetOrCreate(_checkoutRoot);
			attack.gameObject.SetActive(false);
			attack.transform.SetParent(transform, false);
			attack.transform.localPosition = attackPrefab.transform.localPosition;
			attack.transform.localRotation = attackPrefab.transform.localRotation;
			attack.transform.localScale = attackPrefab.transform.localScale;
			_attacks.Add(attack, new OrbitInstance(spawn, onReturned));
			return attack;
		}

		private static void PositionOrb(AnimatedAttack attack, OrbitInstance orbit, float elapsed)
		{
			if (attack == null) return;
			if (!OrbitPresentationSpawn.IsFinitePhase(orbit.Spawn.InitialPhaseRadians, orbit.Spawn.AngularSpeedRadians, elapsed)) return;
			float phase = (float)((double)orbit.Spawn.InitialPhaseRadians + (double)orbit.Spawn.AngularSpeedRadians * elapsed);
			attack.transform.localPosition = new Vector3(Mathf.Cos(phase), Mathf.Sin(phase), 0f) * orbit.Spawn.Radius;
		}

		private void HandleAttackDeactivated(AnimatedAttack attack)
		{
			ReturnAttack(attack, true);
			// A disabled emitter gets no OnDisable when its parent object deactivates, while
			// Unity still stops its coroutine. Clear the movement clock through the child notification.
			if (!gameObject.activeInHierarchy) CancelAttacks();
		}

		private void ReturnAttack(AnimatedAttack attack, bool externallyDeactivated = false)
		{
			if (!_attacks.TryGetValue(attack, out OrbitInstance orbit)) return;
			_attacks.Remove(attack);
			if (attack != null)
			{
				attack.Deactivated -= HandleAttackDeactivated;
				attack.Dispose();
				if (externallyDeactivated) Destroy(attack.gameObject);
				else _pooler?.Return(attack);
			}
			if (_presentationReplicaInitialized) orbit.OnReturned?.Invoke(attack);
			else PresentationTerminated?.Invoke(new OrbitPresentationTermination(ID, orbit.Spawn.Key));
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

		public AnimatedAttack PlayPresentation(OrbitPresentationSpawn spawn, float age,
			Action<AnimatedAttack> onReturned = null)
		{
			if (!_presentationReplicaInitialized || spawn.WeaponId != ID)
				throw new InvalidOperationException("Orbit presentation replica is not initialized for this weapon.");
			if (!gameObject.activeInHierarchy || _cancelling) return null;
			if (!IsValid(spawn) || !IsFinite(age) || age < 0f ||
				age >= spawn.OrbitDuration + attackPrefab.GetEndPresentationDuration()) return null;
			foreach (OrbitInstance orbit in _attacks.Values) if (orbit.Spawn.Key.Equals(spawn.Key)) return null;
			AnimatedAttack attack = Checkout(spawn, onReturned);
			try
			{
				OrbitInstance orbit = _attacks[attack];
				orbit.Elapsed = Mathf.Min(age, spawn.OrbitDuration);
				attack.InitPresentation(this, spawn.Stats, null, () => ReturnAttack(attack));
				attack.Deactivated += HandleAttackDeactivated;
				if (attack.hitbox != null) attack.hitbox.enabled = false;
				PositionOrb(attack, orbit, orbit.Elapsed);
				attack.gameObject.SetActive(true);
				if (!_attacks.ContainsKey(attack)) return null;
				if (age >= spawn.OrbitDuration)
				{
					orbit.Hiding = true;
					attack.PlayExternallyTimedEnd(age - spawn.OrbitDuration);
				}
				else attack.PlayExternallyTimedAnimation(age);
				return _attacks.ContainsKey(attack) ? attack : null;
			}
			catch { ReturnAttack(attack); throw; }
		}

		public bool ApplyPresentationHiding(OrbitPresentationHiding hiding, float age)
		{
			if (!_presentationReplicaInitialized || hiding.WeaponId != ID || !IsFinite(age) || age < 0f ||
				!IsFinite(hiding.OrbitElapsedSeconds) || hiding.OrbitElapsedSeconds < 0f) return false;
			foreach (var entry in _attacks)
			{
				OrbitInstance orbit = entry.Value;
				if (!orbit.Spawn.Key.Equals(hiding.Key)) continue;
				if (hiding.OrbitElapsedSeconds < orbit.Spawn.OrbitDuration ||
					!OrbitPresentationSpawn.IsFinitePhase(orbit.Spawn.InitialPhaseRadians, orbit.Spawn.AngularSpeedRadians, hiding.OrbitElapsedSeconds)) return false;
				orbit.Hiding = true;
				orbit.Elapsed = hiding.OrbitElapsedSeconds;
				PositionOrb(entry.Key, orbit, orbit.Elapsed);
				entry.Key.PlayExternallyTimedEnd(age);
				return true;
			}
			return false;
		}

		public void TickPresentation(float deltaTime)
		{
			if (!_presentationReplicaInitialized || !IsFinite(deltaTime) || deltaTime < 0f) return;
			foreach (AnimatedAttack attack in new List<AnimatedAttack>(_attacks.Keys))
			{
				if (!_attacks.TryGetValue(attack, out OrbitInstance orbit) || orbit.Hiding) continue;
				orbit.Elapsed += deltaTime;
				PositionOrb(attack, orbit, Mathf.Min(orbit.Elapsed, orbit.Spawn.OrbitDuration));
				if (orbit.Elapsed >= orbit.Spawn.OrbitDuration)
				{
					orbit.Hiding = true;
					attack.PlayExternallyTimedEnd(orbit.Elapsed - orbit.Spawn.OrbitDuration);
				}
			}
		}

		public bool TerminatePresentation(OrbitPresentationKey key)
		{
			if (!_presentationReplicaInitialized) return false;
			foreach (var entry in _attacks)
			{
				if (!entry.Value.Spawn.Key.Equals(key)) continue;
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
		}

		private void CancelAttacks()
		{
			if (_cancelling) return;
			_cancelling = true;
			try
			{
				_attackGeneration++;
				if (_movementCoroutine != null) StopCoroutine(_movementCoroutine);
				_movementCoroutine = null;
				bool discard = !gameObject.activeInHierarchy;
				foreach (AnimatedAttack attack in new List<AnimatedAttack>(_attacks.Keys)) ReturnAttack(attack, discard);
				_motionActive = false;
				LastAttackElapsedTime = 0f;
			}
			finally { _cancelling = false; }
		}

		private void OnDisable() { if (!gameObject.activeInHierarchy) CancelAttacks(); }
		private void OnDestroy() => Dispose();
		protected override void Dispose() => CancelAttacks();
		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
		private static bool IsValid(OrbitPresentationSpawn spawn) => spawn.Key.IsValid && spawn.OrbCount > 0 &&
			spawn.OrbCount <= ushort.MaxValue + 1 && spawn.Key.OrbIndex < spawn.OrbCount &&
			IsFinite(spawn.InitialPhaseRadians) && IsFinite(spawn.Radius) && spawn.Radius >= 0f &&
			IsFinite(spawn.AngularSpeedRadians) && IsFinite(spawn.OrbitDuration) && spawn.OrbitDuration >= 0f &&
			OrbitPresentationSpawn.IsFinitePhase(spawn.InitialPhaseRadians, spawn.AngularSpeedRadians, spawn.OrbitDuration) && spawn.Stats.IsFinite;

		private sealed class OrbitInstance
		{
			public OrbitInstance(OrbitPresentationSpawn spawn, Action<AnimatedAttack> onReturned)
			{ Spawn = spawn; OnReturned = onReturned; }
			public OrbitPresentationSpawn Spawn { get; }
			public Action<AnimatedAttack> OnReturned { get; }
			public float Elapsed;
			public bool Hiding;
		}
	}
}
