using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class PlayerAttackOvertimeHitBox : BaseAttackHitBox
	{
		private class CollisionEntry
		{
			public readonly IDamageable Damageable;

			public readonly int Id;

			public float Timestamp;

			public CollisionEntry(IDamageable damageable, int id)
			{
				Damageable = damageable;
				Id = id;
				Timestamp = Time.time;
			}
		}

		[Space]
		[SerializeField]
		protected float timeoutAfterExit = 0.3f;

		[SerializeField]
		protected float hitInterval = 0.5f;

		private const int ChecksPerSecond = 30;

		private readonly Dictionary<int, CollisionEntry> _collisionEntriesMap = new Dictionary<int, CollisionEntry>();

		private readonly List<CollisionEntry> _collisionEntries = new List<CollisionEntry>();

		private uint _enableVersion;
		private uint _entriesVersion;

		public float HitInterval => hitInterval;

		public override void Init(Action<IDamageable> onHit)
		{
			_entriesVersion++;
			CancelPendingRemovals();
			_collisionEntriesMap.Clear();
			_collisionEntries.Clear();
			base.Init(onHit);
		}

		protected override void OnEnable()
		{
			_enableVersion++;
			base.OnEnable();
			_collisionEntriesMap.Clear();
			_collisionEntries.Clear();
			CancelPendingRemovals();
			HitOvertimeRoutine(_enableCts.Token, _enableVersion).Forget();
		}

		protected override void OnDisable()
		{
			_enableVersion++;
			CancelPendingRemovals();
			_collisionEntriesMap.Clear();
			_collisionEntries.Clear();
			base.OnDisable();
		}

		public void SetHitInterval(float time)
		{
			hitInterval = time;
		}

		protected virtual void OnTriggerEnter2D(Collider2D other)
		{
			if (!CanProcessHits) return;
			if (other.TryGetComponent<IDamageable>(out var component))
			{
				int iD = component.GetID();
				if (!TryCancelPendingRemoval(iD) && !_collisionEntriesMap.ContainsKey(iD))
				{
					CollisionEntry collisionEntry = new CollisionEntry(component, iD);
					_collisionEntriesMap.Add(iD, collisionEntry);
					_collisionEntries.Add(collisionEntry);
					_onHit?.Invoke(component);
				}
			}
		}

		protected virtual void OnTriggerExit2D(Collider2D other)
		{
			if (!CanProcessHits) return;
			if (other.TryGetComponent<IDamageable>(out var component))
			{
				RemoveEntryAsync(component.GetID(), timeoutAfterExit).Forget();
			}
		}

		protected override async UniTaskVoid RemoveEntryAsync(int id, float timeoutAfterExit)
		{
			if (_removalCTS.TryGetValue(id, out var value))
			{
				_removalCTS.Remove(id);
				value.Cancel();
				value.Dispose();
			}
			CancellationToken token;
			try
			{
				if (_enableCts == null || _enableCts.IsCancellationRequested)
				{
					return;
				}
				token = _enableCts.Token;
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			CancellationTokenSource localCts = new CancellationTokenSource();
			_removalCTS[id] = localCts;
			CancellationTokenSource linkedCts;
			try
			{
				linkedCts = CancellationTokenSource.CreateLinkedTokenSource(localCts.Token, token);
			}
			catch (ObjectDisposedException)
			{
				localCts.Dispose();
				RemoveOwnedToken(id, localCts);
				return;
			}
			try
			{
				if (!(await UniTask.Delay(TimeSpan.FromSeconds(timeoutAfterExit), ignoreTimeScale: false, PlayerLoopTiming.Update, linkedCts.Token).SuppressCancellationThrow()) && _collisionEntriesMap.Remove(id, out var value2))
				{
					_collisionEntries.Remove(value2);
				}
			}
			finally
			{
				RemoveOwnedToken(id, localCts);
				localCts.Dispose();
				linkedCts.Dispose();
			}
		}

		private bool CanProcessHits => isActiveAndEnabled && _onHit != null &&
			_enableCts != null && !_enableCts.IsCancellationRequested;

		private void RemoveOwnedToken(int id, CancellationTokenSource token)
		{
			if (_removalCTS.TryGetValue(id, out var current) && ReferenceEquals(current, token))
				_removalCTS.Remove(id);
		}

		private void CancelPendingRemovals()
		{
			var pending = new List<CancellationTokenSource>(_removalCTS.Values);
			_removalCTS.Clear();
			foreach (CancellationTokenSource token in pending) { token.Cancel(); token.Dispose(); }
		}

		public override void ClearCallbacks()
		{
			_entriesVersion++;
			base.ClearCallbacks();
			CancelPendingRemovals();
			_collisionEntriesMap.Clear();
			_collisionEntries.Clear();
		}

		private async UniTaskVoid HitOvertimeRoutine(CancellationToken token, uint enableVersion)
		{
			while (!token.IsCancellationRequested && enableVersion == _enableVersion)
			{
				for (int num = _collisionEntries.Count - 1; num >= 0; num--)
				{
					CollisionEntry collisionEntry = _collisionEntries[num];
					if (!(collisionEntry.Damageable as UnityEngine.Object) || !collisionEntry.Damageable.IsActive())
					{
							if (_removalCTS.TryGetValue(collisionEntry.Id, out var value))
							{
								_removalCTS.Remove(collisionEntry.Id);
								value.Cancel();
								value.Dispose();
						}
						_collisionEntriesMap.Remove(collisionEntry.Id);
						_collisionEntries.RemoveAt(num);
					}
					else if (Time.time - collisionEntry.Timestamp >= HitInterval)
					{
						collisionEntry.Timestamp = Time.time;
						uint entriesVersion = _entriesVersion;
						_onHit?.Invoke(collisionEntry.Damageable);
						// Damage may synchronously clear a build, return this beam, and even reuse it.
						if (token.IsCancellationRequested || enableVersion != _enableVersion)
							return;
						// Init can also replace callbacks and entries without disabling the object.
						if (entriesVersion != _entriesVersion) break;
					}
				}
				if (await UniTask.Delay(TimeSpan.FromSeconds(0.03333333507180214), ignoreTimeScale: false, PlayerLoopTiming.Update, token).SuppressCancellationThrow())
				{
					break;
				}
			}
		}
	}
}
