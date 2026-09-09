using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class PlayerBeamAttackBehaviour : WeaponBehaviour
	{
		[SerializeField] private AnimatedAttackVariants variants;
		public float spawnRadius = 1.5f;
		[SerializeField] protected float baseLerpSpeed = 4f;
		[SerializeField] private bool allowMultipleAttacks;

		private float _currentAngle;
		private float _previousAngle;
		private float _targetAngle;
		private ulong _nativeRootEventId;
		private Transform _checkoutRoot;
		private bool _presentationReplicaInitialized;
		private bool _spawning;
		private bool _cancelling;
		private uint _attackGeneration;
		private readonly Dictionary<AnimatedAttack, ActiveBeam> _currentAttacks =
			new Dictionary<AnimatedAttack, ActiveBeam>();
		private readonly Dictionary<ulong, PresentationHeading> _presentationHeadings =
			new Dictionary<ulong, PresentationHeading>();

		public event Action<BeamPresentationSpawn> PresentationSpawned;
		public event Action<BeamPresentationAim> PresentationAimChanged;
		public event Action<BeamPresentationTermination> PresentationTerminated;
		public int ActiveBeamCount => _currentAttacks.Count;

		public override void Init(uint id, AttackStats stats) => base.Init(id, stats);

		public override void InitNative(uint id)
		{
			CancelAttacks();
			base.InitNative(id);
			InitializePool();
			LastAttackElapsedTime = GetCooldown() - Time.deltaTime;
		}

		public override float GetAttackSequenceDuration()
		{
			AnimatedAttack prefab = variants?.GetPrefab(ActiveElement);
			if (prefab == null) throw new InvalidOperationException("Beam weapon requires an AnimatedAttack variant.");
			return prefab.GetPresentationDuration(DurationValue);
		}

		public override void RestoreCooldownRemaining(float remainingSeconds)
		{
			if (!IsFinite(remainingSeconds) || remainingSeconds < 0f)
				throw new ArgumentOutOfRangeException(nameof(remainingSeconds));
			// Reconnect does not replay the old beam, but its remaining lifetime still precedes cooldown.
			LastAttackElapsedTime = GetCooldown() - remainingSeconds;
		}

		private void InitializePool()
		{
			if (variants?.GetPrefab(AttackElement.Default) == null)
				throw new InvalidOperationException("Beam weapon requires an AnimatedAttack variant.");
			variants.Init();
			if (_checkoutRoot == null)
			{
				var checkout = new GameObject("Beam Pool Checkout");
				checkout.SetActive(false);
				checkout.transform.SetParent(transform, false);
				_checkoutRoot = checkout.transform;
			}
		}

		private void Update()
		{
			if (_presentationReplicaInitialized) return;
			PruneInactiveAttacks();
			if (_currentAttacks.Count > 0)
			{
				UpdateDirection(Time.deltaTime);
				return;
			}
			if (CheckCooldown()) Attack();
			if (_currentAttacks.Count == 0) LastAttackElapsedTime += Time.deltaTime;
		}

		private void UpdateDirection(float deltaTime)
		{
			Vector2 direction = NormalizeDirection(player.attackDirection);
			float angle = DirectionAngle(direction);
			_targetAngle += Mathf.DeltaAngle(_previousAngle, angle);
			_previousAngle = angle;
			_currentAngle = Mathf.Lerp(_currentAngle, _targetAngle, deltaTime * baseLerpSpeed);
			if (Mathf.Abs(_targetAngle) > 360f)
			{
				float offset = Mathf.Sign(_targetAngle) * 360f;
				_targetAngle -= offset;
				_currentAngle -= offset;
			}
			foreach (var entry in _currentAttacks) PositionBeam(entry.Key, entry.Value, _currentAngle);
			PresentationAimChanged?.Invoke(new BeamPresentationAim(ID, _nativeRootEventId,
				AngleDirection(_currentAngle)));
		}

		public override void Attack()
		{
			if (!gameObject.activeInHierarchy || _spawning || _cancelling || _presentationReplicaInitialized) return;
			PruneInactiveAttacks();
			if (!CanAttack || _currentAttacks.Count > 0) return;
			uint generation = _attackGeneration;
			_spawning = true;
			try
			{
				using (AttackSnapshot snapshot = BeginNativeGasAttack())
				{
					if (!gameObject.activeInHierarchy) { CancelAttacks(); return; }
					if (generation != _attackGeneration) return;
					int count = allowMultipleAttacks ? snapshot.Stats.ProjectileCount : 1;
					if (count < 1 || count > ushort.MaxValue + 1)
						throw new InvalidOperationException("Beam count cannot be represented by the presentation protocol.");
					Vector2 direction = NormalizeDirection(player.attackDirection);
					_currentAngle = _previousAngle = _targetAngle = DirectionAngle(direction);
					_nativeRootEventId = snapshot.Context.EventId.Value;
					AttackElement element = variants.ResolveElement(ActiveElement);
					for (int index = 0; index < count; index++)
					{
						if (!gameObject.activeInHierarchy) { CancelAttacks(); break; }
						if (generation != _attackGeneration) break;
						var key = new BeamPresentationKey(_nativeRootEventId, (ushort)index);
						AnimatedAttack attack = Checkout(element, key, count);
						attack.InitNative(this, snapshot, null, () => ReturnAttack(attack));
						attack.Deactivated += HandleAttackDeactivated;
						if (attack.hitbox != null) attack.hitbox.enabled = true;
						PositionBeam(attack, _currentAttacks[attack], _currentAngle);
						PresentationSpawned?.Invoke(new BeamPresentationSpawn(ID, key, count,
							direction, element, snapshot.Stats.Duration,
							ProjectilePresentationStats.From(snapshot.Stats, 0f)));
						if (!gameObject.activeInHierarchy) { CancelAttacks(); break; }
						if (generation != _attackGeneration || !_currentAttacks.ContainsKey(attack)) break;
						attack.gameObject.SetActive(true);
						if (!gameObject.activeInHierarchy) { CancelAttacks(); break; }
						if (generation != _attackGeneration || !_currentAttacks.ContainsKey(attack)) break;
						attack.Attack(AngleDirection(_currentAngle + index * (360f / count)), snapshot.Stats.Duration);
					}
				}
			}
			catch { CancelAttacks(); throw; }
			finally
			{
				_spawning = false;
				if (_currentAttacks.Count == 0) FinishNativeSequence();
			}
		}

		private AnimatedAttack Checkout(AttackElement element, BeamPresentationKey key, int count,
			Action<AnimatedAttack> onReturned = null)
		{
			AnimatedAttack prefab = variants.GetPrefab(element);
			AnimatedAttack attack = variants.GetOrCreate(element, _checkoutRoot, false);
			attack.gameObject.SetActive(false);
			attack.transform.SetParent(transform, false);
			attack.transform.localPosition = prefab.transform.localPosition;
			attack.transform.localRotation = prefab.transform.localRotation;
			attack.transform.localScale = prefab.transform.localScale;
			_currentAttacks.Add(attack, new ActiveBeam(key, count, onReturned));
			return attack;
		}

		private void PositionBeam(AnimatedAttack attack, ActiveBeam beam, float rootAngle)
		{
			if (attack == null) return;
			Vector2 direction = AngleDirection(rootAngle + beam.Key.BeamIndex * (360f / beam.Count));
			attack.transform.localPosition = (Vector3)direction * spawnRadius;
			attack.UpdateRotation(direction);
		}

		private void HandleAttackDeactivated(AnimatedAttack attack) => ReturnAttack(attack, true);

		private void ReturnAttack(AnimatedAttack attack, bool externallyDeactivated = false)
		{
			if (!_currentAttacks.TryGetValue(attack, out ActiveBeam beam)) return;
			_currentAttacks.Remove(attack);
			if (externallyDeactivated) variants.Discard(attack);
			if (attack != null)
			{
				attack.Deactivated -= HandleAttackDeactivated;
				attack.Dispose();
				if (externallyDeactivated) Destroy(attack.gameObject);
				else variants.Return(attack);
			}
			if (_presentationReplicaInitialized)
			{
				bool rootRemains = false;
				foreach (ActiveBeam current in _currentAttacks.Values)
					if (current.Key.AttackEventId == beam.Key.AttackEventId) { rootRemains = true; break; }
				if (!rootRemains) _presentationHeadings.Remove(beam.Key.AttackEventId);
				beam.OnReturned?.Invoke(attack);
			}
			else
			{
				if (_currentAttacks.Count == 0 && !_spawning) FinishNativeSequence();
				PresentationTerminated?.Invoke(new BeamPresentationTermination(ID, beam.Key));
			}
		}

		private void FinishNativeSequence()
		{
			_nativeRootEventId = 0;
			LastAttackElapsedTime = 0f;
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

		public AnimatedAttack PlayPresentation(BeamPresentationSpawn spawn, float elapsedSeconds,
			Action<AnimatedAttack> onReturned = null)
		{
			if (!_presentationReplicaInitialized || spawn.WeaponId != ID)
				throw new InvalidOperationException("Beam presentation replica is not initialized for this weapon.");
			if (!spawn.Key.IsValid || spawn.BeamCount <= 0 || spawn.BeamCount > ushort.MaxValue + 1 ||
				spawn.Key.BeamIndex >= spawn.BeamCount || !IsFinite(elapsedSeconds) || elapsedSeconds < 0f ||
				!IsFinite(spawn.AnimationDuration) || spawn.AnimationDuration < 0f ||
				!IsDirection(spawn.RootDirection) || !spawn.Stats.IsFinite) return null;
			foreach (ActiveBeam beam in _currentAttacks.Values)
				if (beam.Key.Equals(spawn.Key)) return null;
			AnimatedAttack prefab = variants.GetPrefab(spawn.Element);
			if (elapsedSeconds >= prefab.GetPresentationDuration(spawn.AnimationDuration)) return null;
			if (!_presentationHeadings.TryGetValue(spawn.Key.AttackEventId, out PresentationHeading heading))
			{
				heading = new PresentationHeading(DirectionAngle(spawn.RootDirection));
				_presentationHeadings.Add(spawn.Key.AttackEventId, heading);
			}
			AnimatedAttack attack = Checkout(spawn.Element, spawn.Key, spawn.BeamCount, onReturned);
			try
			{
				attack.InitPresentation(this, spawn.Stats, null, () => ReturnAttack(attack));
				attack.Deactivated += HandleAttackDeactivated;
				if (attack.hitbox != null) attack.hitbox.enabled = false;
				PositionBeam(attack, _currentAttacks[attack], heading.Current);
				attack.gameObject.SetActive(true);
				attack.PlayPresentation(AngleDirection(heading.Current + spawn.Key.BeamIndex * (360f / spawn.BeamCount)),
					spawn.AnimationDuration, elapsedSeconds);
				return _currentAttacks.ContainsKey(attack) ? attack : null;
			}
			catch { ReturnAttack(attack); throw; }
		}

		public bool ApplyPresentationAim(BeamPresentationAim aim)
		{
			if (!_presentationReplicaInitialized || aim.WeaponId != ID || !IsDirection(aim.RootDirection) ||
				!_presentationHeadings.TryGetValue(aim.AttackEventId, out PresentationHeading heading)) return false;
			heading.Target = DirectionAngle(aim.RootDirection);
			return true;
		}

		public void TickPresentation(float deltaTime)
		{
			if (!_presentationReplicaInitialized || !IsFinite(deltaTime) || deltaTime < 0f) return;
			PruneInactiveAttacks();
			foreach (PresentationHeading heading in _presentationHeadings.Values)
				heading.Current = Mathf.LerpAngle(heading.Current, heading.Target, 1f - Mathf.Exp(-20f * deltaTime));
			foreach (var entry in _currentAttacks)
				PositionBeam(entry.Key, entry.Value, _presentationHeadings[entry.Value.Key.AttackEventId].Current);
		}

		public bool TerminatePresentation(BeamPresentationKey key)
		{
			if (!_presentationReplicaInitialized) return false;
			foreach (var entry in _currentAttacks)
			{
				if (!entry.Value.Key.Equals(key)) continue;
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
				foreach (AnimatedAttack attack in new List<AnimatedAttack>(_currentAttacks.Keys)) ReturnAttack(attack);
				_presentationHeadings.Clear();
				_nativeRootEventId = 0;
			}
			finally { _cancelling = false; }
		}

		private void PruneInactiveAttacks()
		{
			List<AnimatedAttack> inactive = null;
			foreach (AnimatedAttack attack in _currentAttacks.Keys)
				if (attack == null || !attack.gameObject.activeInHierarchy)
					(inactive ??= new List<AnimatedAttack>()).Add(attack);
			if (inactive != null)
				foreach (AnimatedAttack attack in inactive) ReturnAttack(attack);
		}

		private void OnDisable()
		{
			// Build updates disable automatic execution while existing attacks keep their frozen snapshot.
			if (!gameObject.activeInHierarchy) CancelAttacks();
		}
		private void OnDestroy() => Dispose();
		protected override void Dispose() { CancelAttacks(); LastAttackElapsedTime = 0f; }
		private static float DirectionAngle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
		private static Vector2 AngleDirection(float angle) => new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
		private static bool IsDirection(Vector2 direction) => IsFinite(direction.x) && IsFinite(direction.y) && direction.sqrMagnitude > 0.000001f;
		private static Vector2 NormalizeDirection(Vector2 direction) => IsDirection(direction) ? direction.normalized : Vector2.right;

		private readonly struct ActiveBeam
		{
			public ActiveBeam(BeamPresentationKey key, int count, Action<AnimatedAttack> onReturned)
			{ Key = key; Count = count; OnReturned = onReturned; }
			public BeamPresentationKey Key { get; }
			public int Count { get; }
			public Action<AnimatedAttack> OnReturned { get; }
		}
		private sealed class PresentationHeading
		{
			public PresentationHeading(float angle) { Current = Target = angle; }
			public float Current;
			public float Target;
		}
	}
}
