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

		private Dictionary<int, CollisionEntry> _collisionEntriesMap;

		private List<CollisionEntry> _collisionEntries;

		public float HitInterval => hitInterval;

		protected override void Awake()
		{
			base.Awake();
			_collisionEntriesMap = new Dictionary<int, CollisionEntry>();
			_collisionEntries = new List<CollisionEntry>();
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			_collisionEntriesMap.Clear();
			_collisionEntries.Clear();
			CancelAllRemovalTokens();
			HitOvertimeRoutine(_enableCts.Token).Forget();
		}

		protected override void OnDisable()
		{
			CancelAllRemovalTokens();
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
			if (other.TryGetComponent<IDamageable>(out var component))
			{
				RemoveEntryAsync(component.GetID(), timeoutAfterExit).Forget();
			}
		}

		protected override async UniTaskVoid RemoveEntryAsync(int id, float timeoutAfterExit)
		{
			if (_removalCTS.TryGetValue(id, out var value))
			{
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
				_removalCTS.Remove(id);
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
				_removalCTS.Remove(id);
				localCts.Dispose();
				linkedCts.Dispose();
			}
		}

		private async UniTaskVoid HitOvertimeRoutine(CancellationToken token)
		{
			while (!token.IsCancellationRequested)
			{
				for (int num = _collisionEntries.Count - 1; num >= 0; num--)
				{
					CollisionEntry collisionEntry = _collisionEntries[num];
					if (!(collisionEntry.Damageable as UnityEngine.Object) || !collisionEntry.Damageable.IsActive())
					{
						if (_removalCTS.TryGetValue(collisionEntry.Id, out var value))
						{
							value.Cancel();
							value.Dispose();
							_removalCTS.Remove(collisionEntry.Id);
						}
						_collisionEntriesMap.Remove(collisionEntry.Id);
						_collisionEntries.RemoveAt(num);
					}
					else if (Time.time - collisionEntry.Timestamp >= HitInterval)
					{
						_onHit?.Invoke(collisionEntry.Damageable);
						collisionEntry.Timestamp = Time.time;
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
