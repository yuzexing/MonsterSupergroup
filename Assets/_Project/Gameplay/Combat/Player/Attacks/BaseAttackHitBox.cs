using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public abstract class BaseAttackHitBox : MonoBehaviour
	{
		[Header("General Settings")]
		public Collider2D collider;

		public Collider2D[] otherColliders;

		public bool disableAfterTimeout;

		public float timeout = 0.25f;

		protected Action<IDamageable> _onHit;

		protected HashSet<int> _hitEntries = new HashSet<int>();

		protected Dictionary<int, CancellationTokenSource> _removalCTS = new Dictionary<int, CancellationTokenSource>();

		protected CancellationTokenSource _enableCts;

		protected virtual void Awake()
		{
			if (!collider)
			{
				collider = GetComponent<Collider2D>();
			}
		}

		public virtual void Init(Action<IDamageable> onHit)
		{
			_onHit = onHit;
			_hitEntries.Clear();
			CancelAllRemovalTokens();
			collider.isTrigger = true;
			if (otherColliders != null)
			{
				Collider2D[] array = otherColliders;
				for (int i = 0; i < array.Length; i++)
				{
					array[i].isTrigger = true;
				}
			}
		}

		protected virtual void OnEnable()
		{
			if (_enableCts != null)
			{
				_enableCts.Cancel();
				_enableCts.Dispose();
			}
			_enableCts = new CancellationTokenSource();
			if (disableAfterTimeout)
			{
				StartTimeoutAsync(_enableCts.Token).Forget();
			}
		}

		protected virtual void OnDisable()
		{
			if (_enableCts != null)
			{
				_enableCts.Cancel();
				_enableCts.Dispose();
				_enableCts = null;
			}
			CancelAllRemovalTokens();
		}

		public virtual void Toggle(bool state)
		{
			if (!collider)
			{
				return;
			}
			if (state)
			{
				_hitEntries.Clear();
				CancelAllRemovalTokens();
			}
			collider.enabled = state;
			if (otherColliders != null)
			{
				Collider2D[] array = otherColliders;
				for (int i = 0; i < array.Length; i++)
				{
					array[i].enabled = state;
				}
			}
		}

		protected virtual async UniTaskVoid StartTimeoutAsync(CancellationToken token)
		{
			if (!(await UniTask.Delay(TimeSpan.FromSeconds(timeout), ignoreTimeScale: false, PlayerLoopTiming.Update, token).SuppressCancellationThrow()))
			{
				Toggle(state: false);
			}
		}

		protected virtual async UniTaskVoid RemoveEntryAsync(int id, float timeoutAfterExit)
		{
			TryCancelPendingRemoval(id);
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
			CancellationTokenSource cts = new CancellationTokenSource();
			_removalCTS[id] = cts;
			CancellationTokenSource linkedTokenSource;
			try
			{
				linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token);
			}
			catch (ObjectDisposedException)
			{
				cts.Dispose();
				_removalCTS.Remove(id);
				return;
			}
			try
			{
				if (!(await UniTask.Delay(TimeSpan.FromSeconds(timeoutAfterExit), ignoreTimeScale: false, PlayerLoopTiming.Update, linkedTokenSource.Token).SuppressCancellationThrow()))
				{
					_hitEntries.Remove(id);
				}
			}
			finally
			{
				_removalCTS.Remove(id);
				cts.Dispose();
				linkedTokenSource.Dispose();
			}
		}

		protected bool TryCancelPendingRemoval(int id)
		{
			if (_removalCTS.TryGetValue(id, out var value))
			{
				value.Cancel();
				value.Dispose();
				_removalCTS.Remove(id);
				return true;
			}
			return false;
		}

		protected void CancelAllRemovalTokens()
		{
			foreach (KeyValuePair<int, CancellationTokenSource> removalCT in _removalCTS)
			{
				removalCT.Value.Cancel();
				removalCT.Value.Dispose();
			}
			_removalCTS.Clear();
		}

		public virtual void ClearCallbacks()
		{
			_onHit = null;
		}

		protected virtual void OnDrawGizmos()
		{
			if (!collider)
			{
				collider = GetComponent<Collider2D>();
			}
			if ((bool)collider)
			{
				DrawCollider(collider);
			}
			if (otherColliders != null)
			{
				Collider2D[] array = otherColliders;
				foreach (Collider2D collider2D in array)
				{
					DrawCollider(collider2D);
				}
			}
		}

		private void DrawCollider(Collider2D collider2D)
		{
			Color color = (collider2D.enabled ? new Color(1f, 0f, 0f, 0.3f) : new Color(0.5f, 0.5f, 0.5f, 0.1f));
			Color color2 = (collider2D.enabled ? Color.red : Color.gray);
			Matrix4x4 matrix = Gizmos.matrix;
			Gizmos.matrix = collider2D.transform.localToWorldMatrix;
			if (collider2D is BoxCollider2D boxCollider2D)
			{
				Gizmos.color = color;
				Gizmos.DrawCube(boxCollider2D.offset, boxCollider2D.size);
				Gizmos.color = color2;
				Gizmos.DrawWireCube(boxCollider2D.offset, boxCollider2D.size);
			}
			else if (collider2D is CircleCollider2D circleCollider2D)
			{
				Gizmos.color = color;
				Gizmos.DrawSphere(circleCollider2D.offset, circleCollider2D.radius);
				Gizmos.color = color2;
				Gizmos.DrawWireSphere(circleCollider2D.offset, circleCollider2D.radius);
			}
			else if (collider2D is PolygonCollider2D polygonCollider2D)
			{
				Gizmos.color = color2;
				Vector2[] points = polygonCollider2D.points;
				if (points.Length > 1)
				{
					for (int i = 0; i < points.Length; i++)
					{
						Vector3 vector = points[i] + polygonCollider2D.offset;
						Vector3 to = points[(i + 1) % points.Length] + polygonCollider2D.offset;
						Gizmos.DrawLine(vector, to);
					}
				}
			}
			else if (collider2D is EdgeCollider2D edgeCollider2D)
			{
				Gizmos.color = color2;
				Vector2[] points2 = edgeCollider2D.points;
				float edgeRadius = edgeCollider2D.edgeRadius;
				if (points2.Length > 1)
				{
					for (int j = 0; j < points2.Length; j++)
					{
						Vector3 vector2 = points2[j] + edgeCollider2D.offset;
						if (edgeRadius > 0f)
						{
							Gizmos.DrawWireSphere(vector2, edgeRadius);
						}
						if (j < points2.Length - 1)
						{
							Vector3 vector3 = points2[j + 1] + edgeCollider2D.offset;
							Gizmos.DrawLine(vector2, vector3);
							if (edgeRadius > 0.001f)
							{
								Vector3 normalized = (vector3 - vector2).normalized;
								Vector3 vector4 = new Vector3(0f - normalized.y, normalized.x, 0f) * edgeRadius;
								Gizmos.DrawLine(vector2 + vector4, vector3 + vector4);
								Gizmos.DrawLine(vector2 - vector4, vector3 - vector4);
							}
						}
					}
				}
			}
			Gizmos.matrix = matrix;
		}
	}
}
