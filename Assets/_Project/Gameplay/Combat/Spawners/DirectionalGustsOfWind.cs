using System;
using System.Collections.Generic;
using System.Threading;
using AstralShift.HellMaiden.Common;
using AstralShift.HellMaiden.Helpers;
using AstralShift.HellMaiden.Interactions;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.Pooling;
using Com.LuisPedroFonseca.ProCamera2D;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners
{
	public class DirectionalGustsOfWind : SerializedProgressable, IPausable
	{
		public WindInteraction windPrefab;

		[Space]
		public bool randomDirection;

		public float magnitude = 1f;

		public Direction windDirection;

		private WindInteraction _wind;

		private ParticleSystem _particles;

		private BoxCollider2D _collider;

		private GenericPooler<WindInteraction> _windPool;

		private CancellationTokenSource _cts;

		public float LifetimeMultiplier = 18f;

		private const float ParticlesPoolingTimeout = 15f;

		private float _startTimestamp;

		private float _pausedDuration;

		private float _pauseStartTimestamp;

		private float _elapsed;

		private bool _isPaused;

		public EnemyContinuousSpawner spawner { get; set; }

		public override void Init()
		{
			((IPausable)this).Subscribe();
			_windPool = PoolManager.Instance.GetOrCreatePooler(windPrefab);
			_wind = _windPool.GetOrCreate(base.transform, activate: true);
			if (_cts == null)
			{
				_cts = new CancellationTokenSource();
			}
			_collider = _wind.GetComponent<BoxCollider2D>();
			Bounds cameraWorldSpaceBounds = CameraHelpers.GetCameraWorldSpaceBounds();
			_collider.size = cameraWorldSpaceBounds.size;
			_collider.offset = Vector2.zero;
			_wind.magnitude = magnitude;
			float num = 0f;
			if (randomDirection)
			{
				num = UnityEngine.Random.Range(0f, 360f);
				_wind.windDirection = new Vector2(Mathf.Cos(num * (MathF.PI / 180f)), Mathf.Sin(num * (MathF.PI / 180f)));
			}
			else
			{
				Vector2 vector = -windDirection.ToVector2();
				num = Mathf.Atan2(vector.y, vector.x) * 57.29578f;
				_wind.windDirection = vector;
			}
			Transform child = _wind.transform.GetChild(0);
			_particles = child.GetComponentInChildren<ParticleSystem>();
			if ((bool)spawner)
			{
				spawner.direction = VectorToDirection(-_wind.windDirection);
			}
			Camera gameCamera = ProCamera2D.Instance.GameCamera;
			if ((bool)gameCamera)
			{
				_wind.transform.SetParent(gameCamera.transform, worldPositionStays: false);
				_wind.transform.localPosition = Vector3.zero;
			}
			Vector2 vector2 = ProCamera2DHelpers.GetPointOutsideCamera(exclusionBounds: new Bounds(Vector3.zero, Vector3.zero), direction: -_wind.windDirection, distance: 5f);
			child.transform.position = vector2;
			child.transform.rotation = Quaternion.Euler(0f, 0f, num);
			float num2 = Vector2.Distance(child.transform.position, GetRightAnglePosition(child.transform.position, GetSecondFarthestDistance(cameraWorldSpaceBounds, _wind.windDirection, child, 1)));
			SetShape(new Vector3(1f, 1f, num2 * 2f));
			float num3 = Vector2.Distance(child.transform.position, GetRightAnglePosition(child.transform.position, GetSecondFarthestDistance(cameraWorldSpaceBounds, _wind.windDirection, child, 0)));
			SetLifetime(num3 * LifetimeMultiplier);
			SetEmission(setEmission: true);
			_elapsed = 0f;
			_pausedDuration = 0f;
			_startTimestamp = Time.time;
		}

		public override void ProgressUpdate()
		{
			if (!_isPaused)
			{
				_elapsed = Time.time - _startTimestamp - _pausedDuration;
				if (_elapsed >= base.Duration)
				{
					base.hasEnded = true;
				}
			}
		}

		public override void End()
		{
			_wind.transform.SetParent(null);
			_wind.magnitude = 0f;
			_wind.windDirection = Vector2.zero;
			_collider.offset = Vector2.zero;
			SetEmission(setEmission: false);
			RunParticlesReturnToPoolTimeout(15f).Forget();
			((IPausable)this).UnSubscribe();
		}

		private void SetLifetime(float lifetime)
		{
			if (_particles != null)
			{
				ParticleSystem.MainModule main = _particles.main;
				main.startLifetime = lifetime;
			}
		}

		private void SetShape(Vector3 size)
		{
			ParticleSystem.ShapeModule shape = _particles.shape;
			shape.scale = size;
		}

		private void SetEmission(bool setEmission)
		{
			ParticleSystem.EmissionModule emission = _particles.emission;
			emission.enabled = setEmission;
		}

		private Vector2 GetRightAnglePosition(Vector2 a, Vector2 b)
		{
			Vector2 vector = b - a;
			Vector2 normalized = new Vector2(0f - vector.y, vector.x).normalized;
			float num = Vector2.Distance(a, b) / Mathf.Sqrt(2f);
			return (a + b) / 2f + normalized * num;
		}

		private Vector2 GetSecondFarthestDistance(Bounds cameraBounds, Vector2 direction, Transform windParent, int index)
		{
			direction.Normalize();
			Vector2[] obj = new Vector2[4]
			{
				new Vector2(cameraBounds.min.x, cameraBounds.min.y),
				new Vector2(cameraBounds.max.x, cameraBounds.min.y),
				new Vector2(cameraBounds.min.x, cameraBounds.max.y),
				new Vector2(cameraBounds.max.x, cameraBounds.max.y)
			};
			Vector2 vector = windParent.position;
			List<(Vector2, float)> list = new List<(Vector2, float)>();
			Vector2[] array = obj;
			foreach (Vector2 vector2 in array)
			{
				float item = Vector2.Dot(vector2 - vector, direction);
				list.Add((vector2, item));
			}
			list.Sort(((Vector2 corner, float projection) a, (Vector2 corner, float projection) b) => b.projection.CompareTo(a.projection));
			if (list.Count <= index)
			{
				return list[0].Item1;
			}
			return list[index].Item1;
		}

		private async UniTaskVoid RunParticlesReturnToPoolTimeout(float seconds)
		{
			await UniTask.Delay(TimeSpan.FromSeconds(seconds), ignoreTimeScale: false, PlayerLoopTiming.Update, _cts.Token);
			if (!_cts.IsCancellationRequested && (bool)_wind)
			{
				_windPool?.Return(_wind);
			}
		}

		private Direction VectorToDirection(Vector2 dir)
		{
			if (dir == Vector2.zero)
			{
				return Direction.Right;
			}
			float num = Mathf.Atan2(dir.y, dir.x) * 57.29578f;
			num = (num + 360f) % 360f;
			if (num >= 45f && num < 135f)
			{
				return Direction.Up;
			}
			if (num >= 135f && num < 225f)
			{
				return Direction.Left;
			}
			if (num >= 225f && num < 315f)
			{
				return Direction.Down;
			}
			return Direction.Right;
		}

		private void OnDestroy()
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
			((IPausable)this).UnSubscribe();
		}

		public void OnPausePausables()
		{
			_isPaused = true;
			_pauseStartTimestamp = Time.time;
			if (_particles != null)
			{
				_particles.Stop();
			}
			if (_wind != null)
			{
				_wind.SetPaused(paused: true);
			}
		}

		public void OnResumePausables()
		{
			_isPaused = false;
			_pausedDuration += Time.time - _pauseStartTimestamp;
			if (_particles != null)
			{
				_particles.Play();
			}
			if (_wind != null)
			{
				_wind.SetPaused(paused: false);
			}
		}
	}
}
