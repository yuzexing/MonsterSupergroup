using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.Managers;
using AstralShift.Pooling;
using AstralShift.QTI.Helpers.Attributes;
using Com.LuisPedroFonseca.ProCamera2D;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Traps
{
	public class BarrierTrap : Trap
	{
		[SerializeField]
		protected Transform trapTransform;

		[SerializeField]
		protected ParticleSystem particleSystem;

		[SerializeField]
		[Range(3f, 100f)]
		protected int numberOfSides = 20;

		[SerializeField]
		private float minRadius = 5f;

		[SerializeField]
		protected float maxRadius = 15f;

		[SerializeField]
		[Tooltip("If static it does not shring and will only use duration time")]
		protected bool isStatic;

		[SerializeField]
		private float shrinkDuration = 60f;

		[SerializeField]
		private float spawnAnimationDuration = 2f;

		[SerializeField]
		protected float particleSystemRadius = 2.5f;

		[SerializeField]
		private float onSpawnCameraTargetOffset = 10f;

		[SerializeField]
		private float onSpawnCameraTargetDuration = 0.5f;

		[SerializeField]
		private float onSpawnCameraFramingTimeout = 1f;

		[SerializeField]
		private bool destroyOnShrink;

		[SerializeField]
		private bool targetPlayer = true;

		[ConditionalHide("targetPlayer", false)]
		public Transform target;

		[Header("Sounds")]
		[SerializeField]
		protected EventReference trapStartSound;

		[SerializeField]
		protected EventInstance trapStartSoundInstance;

		[SerializeField]
		protected EventReference trapLoopSound;

		[SerializeField]
		protected EventReference trapEndSound;

		protected List<EventInstance> _particleLoopSoundInstances;

		protected const int MaxTrapLoopSFXInstances = 8;

		public Action onSpawnFinished;

		protected GenericPooler<ParticleSystem> _particleSystemPooler;

		protected List<ParticleSystem[]> _allParticleSystems;

		protected Vector2[] _points;

		[SerializeField]
		protected EdgeCollider2D _collider;

		protected float _currentSpeed;

		protected float _currentRadius;

		private bool _isSlowMoActive;

		[SerializeField]
		private bool hasSlowMo = true;

		private Coroutine _inOutCoroutine;

		protected float _reArrangeTimer;

		protected const float ReArrangeInterval = 5f;

		private uint slowMoRequestId;

		private float _elapsedTime;

		[SerializeField]
		protected bool applyIsometricRotation = true;

		[SerializeField]
		private bool reverseSpawn;

		public int NumberOfSides => numberOfSides;

		public float MinRadius => minRadius;

		public float MaxRadius => maxRadius;

		public float ShrinkDuration => shrinkDuration;

		public float SpawnAnimationDuration => spawnAnimationDuration;

		public float ParticleSystemRadius => particleSystemRadius;

		public float OnSpawnCameraTargetOffset => onSpawnCameraTargetOffset;

		public float OnSpawnCameraTargetDuration => onSpawnCameraTargetDuration;

		public float OnSpawnCameraFramingTimeout => onSpawnCameraFramingTimeout;

		public bool DestroyOnShrink => destroyOnShrink;

		public override void Init()
		{
			onTrapEnd = null;
			if (_particleSystemPooler == null)
			{
				_particleSystemPooler = PoolManager.Instance.GetOrCreatePooler(particleSystem, 200);
			}
			if (_inOutCoroutine != null)
			{
				StopCoroutine(_inOutCoroutine);
				_inOutCoroutine = null;
			}
			SetShrinkDuration(shrinkDuration);
			_inOutCoroutine = StartCoroutine(InitializeCoroutine());
			_elapsedTime = 0f;
		}

		private IEnumerator InitializeCoroutine()
		{
			if (targetPlayer)
			{
				base.transform.position = GameDirector.Instance.Player.transform.position;
			}
			else
			{
				base.transform.position = target.position;
			}
			if (applyIsometricRotation)
			{
				trapTransform.rotation = Quaternion.Euler(45f, 0f, 0f);
			}
			if (hasSlowMo)
			{
				SetTrapAsCameraTarget();
			}
			yield return new WaitForSeconds(onSpawnCameraTargetDuration + onSpawnCameraFramingTimeout);
			float slowMoTimeScale = PauseManager.Instance.SlowMoTimeScaleValue;
			if (hasSlowMo)
			{
				slowMoRequestId = PauseManager.Instance.StartSlowMo(immediate: true);
				GameDirector.Instance.Player.SetInvulnerable(state: true);
				_isSlowMoActive = true;
			}
			GenerateCollider();
			CreateParticleSystems();
			trapStartSoundInstance = RuntimeManager.CreateInstance(trapStartSound);
			trapStartSoundInstance.set3DAttributes(GetFlattenedPosition(_allParticleSystems[0][0].transform.localPosition).To3DAttributes());
			trapStartSoundInstance.start();
			float num = spawnAnimationDuration / (float)_allParticleSystems.Count;
			WaitForSeconds spawnWaitInstance = new WaitForSeconds(num * slowMoTimeScale);
			if (reverseSpawn)
			{
				for (int i = _allParticleSystems.Count - 1; i >= 0; i--)
				{
					for (int num2 = _allParticleSystems[i].Length - 1; num2 >= 0; num2--)
					{
						ParticleSystem.MainModule main = _allParticleSystems[i][num2].main;
						main.simulationSpeed = 1f / slowMoTimeScale;
						_allParticleSystems[i][num2].Play(withChildren: true);
					}
					trapStartSoundInstance.set3DAttributes(GetFlattenedPosition(_allParticleSystems[i][0].transform.localPosition).To3DAttributes());
					yield return spawnWaitInstance;
				}
			}
			else
			{
				for (int i = 0; i < _allParticleSystems.Count; i++)
				{
					for (int j = 0; j < _allParticleSystems[i].Length; j++)
					{
						ParticleSystem.MainModule main2 = _allParticleSystems[i][j].main;
						main2.simulationSpeed = 1f / slowMoTimeScale;
						_allParticleSystems[i][j].Play(withChildren: true);
					}
					trapStartSoundInstance.set3DAttributes(GetFlattenedPosition(_allParticleSystems[i][0].transform.localPosition).To3DAttributes());
					yield return spawnWaitInstance;
				}
			}
			if (hasSlowMo)
			{
				SetPlayerAsCameraTarget();
				PauseManager.Instance.StopSlowMo(immediate: true, slowMoRequestId);
				GameDirector.Instance.Player.SetInvulnerable(state: false);
				_isSlowMoActive = false;
			}
			onSpawnFinished?.Invoke();
			for (int k = 0; k < _allParticleSystems.Count; k++)
			{
				for (int l = 0; l < _allParticleSystems[k].Length; l++)
				{
					ParticleSystem.MainModule main3 = _allParticleSystems[k][l].main;
					main3.simulationSpeed = 1f;
				}
			}
			foreach (EventInstance particleLoopSoundInstance in _particleLoopSoundInstances)
			{
				particleLoopSoundInstance.start();
			}
			trapStartSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			trapStartSoundInstance.release();
			_inOutCoroutine = null;
		}

		public override void Stop()
		{
			if (_inOutCoroutine != null)
			{
				StopCoroutine(_inOutCoroutine);
				_inOutCoroutine = null;
			}
			if (_isSlowMoActive)
			{
				PauseManager.Instance.StopSlowMo(immediate: true, slowMoRequestId);
				_isSlowMoActive = false;
			}
			ClearAllLoopInstances(outSfx: true);
			_inOutCoroutine = StartCoroutine(StopCoroutine());
		}

		private IEnumerator StopCoroutine()
		{
			if (_allParticleSystems != null)
			{
				foreach (ParticleSystem[] allParticleSystem in _allParticleSystems)
				{
					allParticleSystem[0].Stop(withChildren: true);
				}
			}
			if ((bool)_collider)
			{
				_collider.enabled = false;
			}
			while (AreParticleSystemsAlive())
			{
				yield return null;
			}
			_inOutCoroutine = null;
			onTrapEnd?.Invoke();
			if (_allParticleSystems != null)
			{
				foreach (ParticleSystem[] allParticleSystem2 in _allParticleSystems)
				{
					_particleSystemPooler.Return(allParticleSystem2[0]);
				}
				_allParticleSystems.Clear();
			}
			yield return null;
			UnityEngine.Object.Destroy(base.gameObject);
		}

		private void OnDestroy()
		{
			trapStartSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			trapStartSoundInstance.release();
			ClearAllLoopInstances();
		}

		private bool AreParticleSystemsAlive()
		{
			if (_allParticleSystems == null)
			{
				return false;
			}
			foreach (ParticleSystem[] allParticleSystem in _allParticleSystems)
			{
				if (allParticleSystem[0].IsAlive(withChildren: true))
				{
					return true;
				}
			}
			return false;
		}

		private void SetTrapAsCameraTarget()
		{
			ProCamera2D.Instance.AddCameraTarget(base.transform, 1f, 1f, onSpawnCameraTargetDuration, new Vector2(0f - maxRadius - onSpawnCameraTargetOffset, 0f));
			ProCamera2D.Instance.AddCameraTarget(base.transform, 1f, 1f, onSpawnCameraTargetDuration, new Vector2(maxRadius + onSpawnCameraTargetOffset, 0f));
		}

		private void SetPlayerAsCameraTarget()
		{
			ProCamera2D.Instance.RemoveAllCameraTargets();
			ProCamera2D.Instance.AddCameraTarget(GameDirector.Instance.Player.transform);
		}

		public void SetRadius(float min, float max)
		{
			minRadius = min;
			maxRadius = max;
		}

		public void SetShrinkDuration(float value)
		{
			_currentSpeed = (maxRadius - minRadius) / value;
		}

		public override float GetShrinkDuration()
		{
			return shrinkDuration;
		}

		public void SetShrinkSpeed(float value)
		{
			_currentSpeed = value;
		}

		public float GetShrinkSpeed()
		{
			return _currentSpeed;
		}

		private void FixedUpdate()
		{
			if (Time.timeScale == 0f || _allParticleSystems == null || _points == null)
			{
				return;
			}
			if (!isStatic)
			{
				if (!Mathf.Approximately(minRadius, maxRadius))
				{
					ShrinkCollision();
					UpdateParticleSystemsPositions();
					if (destroyOnShrink && _currentRadius <= minRadius + _collider.edgeRadius && _inOutCoroutine == null)
					{
						Stop();
					}
				}
			}
			else
			{
				_elapsedTime += Time.fixedDeltaTime;
				if (_elapsedTime > ShrinkDuration && _inOutCoroutine == null)
				{
					Stop();
				}
			}
		}

		protected virtual void ShrinkCollision()
		{
			if (!(_currentRadius <= minRadius + _collider.edgeRadius))
			{
				_currentRadius -= _currentSpeed * Time.fixedDeltaTime;
				UpdateColliderPoints(_currentRadius);
			}
		}

		protected virtual void GenerateCollider()
		{
			_collider.points = null;
			_points = new Vector2[numberOfSides + 1];
			float currentRadius = maxRadius + _collider.edgeRadius;
			_currentRadius = currentRadius;
			UpdateColliderPoints(_currentRadius);
		}

		private void UpdateColliderPoints(float radius)
		{
			float num = 360f / (float)numberOfSides;
			float edgeRadius = _collider.edgeRadius;
			float num2 = radius - edgeRadius;
			float num3 = (applyIsometricRotation ? Mathf.Cos(MathF.PI / 4f) : 1f);
			for (int i = 0; i < numberOfSides; i++)
			{
				float f = MathF.PI / 180f * num * (float)i;
				float num4 = Mathf.Cos(f);
				float num5 = Mathf.Sin(f);
				float num6 = num4 * num3;
				float num7 = num5;
				float num8 = Mathf.Sqrt(num6 * num6 + num7 * num7);
				num6 /= num8;
				num7 /= num8;
				float num9 = num2 * num4;
				float num10 = num2 * num5;
				float num11 = edgeRadius * num7;
				if (applyIsometricRotation)
				{
					num11 /= num3;
				}
				float x = num9 + edgeRadius * num6;
				float y = num10 + num11;
				_points[i] = new Vector2(x, y);
			}
			_points[numberOfSides] = _points[0];
			_collider.points = _points;
		}

		protected virtual void CreateParticleSystems()
		{
			float num = _currentRadius - _collider.edgeRadius;
			int num2 = Mathf.CeilToInt(MathF.PI * 2f * num / (particleSystemRadius * 2f));
			float num3 = 360f / (float)num2;
			if (_allParticleSystems == null)
			{
				_allParticleSystems = new List<ParticleSystem[]>();
			}
			_allParticleSystems.Clear();
			if (_particleLoopSoundInstances == null)
			{
				_particleLoopSoundInstances = new List<EventInstance>();
			}
			trapTransform.position = new Vector3(trapTransform.position.x, trapTransform.position.y, 100f);
			for (int i = 0; i < num2; i++)
			{
				float f = MathF.PI / 180f * num3 * (float)i;
				Vector3 localPosition = new Vector2(Mathf.Cos(f) * num, Mathf.Sin(f) * num);
				ParticleSystem orCreate = _particleSystemPooler.GetOrCreate(trapTransform, activate: true);
				orCreate.transform.localPosition = localPosition;
				_allParticleSystems.Add(orCreate.GetComponentsInChildren<ParticleSystem>(includeInactive: true));
			}
			SpreadLoopInstances();
		}

		protected virtual void SpreadLoopInstances()
		{
			ClearAllLoopInstances();
			if (_allParticleSystems == null || _allParticleSystems.Count == 0)
			{
				return;
			}
			int num = Mathf.Max(1, _allParticleSystems.Count / 8);
			for (int i = 0; i < _allParticleSystems.Count; i++)
			{
				if (i % num == 0 && _particleLoopSoundInstances.Count < 8)
				{
					EventInstance item = RuntimeManager.CreateInstance(trapLoopSound);
					item.set3DAttributes(GetFlattenedPosition(_allParticleSystems[i][0].transform.localPosition).To3DAttributes());
					_particleLoopSoundInstances.Add(item);
				}
			}
		}

		protected virtual void ReArrangeParticleSystems()
		{
			float num = _currentRadius - _collider.edgeRadius;
			int num2 = Mathf.CeilToInt(MathF.PI * 2f * num / (particleSystemRadius * 2f));
			float num3 = 360f / (float)num2;
			for (int i = 0; i < num2; i++)
			{
				float f = MathF.PI / 180f * num3 * (float)i;
				Vector3 localPosition = new Vector2(Mathf.Cos(f) * num, Mathf.Sin(f) * num);
				_allParticleSystems[i][0].transform.localPosition = localPosition;
			}
			for (int num4 = _allParticleSystems.Count - 1; num4 >= num2; num4--)
			{
				_allParticleSystems[num4][0].Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
				_particleSystemPooler.Return(_allParticleSystems[num4][0]);
				_allParticleSystems.RemoveAt(num4);
			}
			SpreadLoopInstances();
			foreach (EventInstance particleLoopSoundInstance in _particleLoopSoundInstances)
			{
				particleLoopSoundInstance.start();
			}
		}

		private void ClearAllLoopInstances(bool outSfx = false)
		{
			if (_particleLoopSoundInstances == null)
			{
				return;
			}
			for (int i = 0; i < _particleLoopSoundInstances.Count; i++)
			{
				_particleLoopSoundInstances[i].stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				_particleLoopSoundInstances[i].release();
				if (outSfx)
				{
					RuntimeManager.PlayOneShot(trapEndSound, GetFlattenedPosition(_allParticleSystems[0][0].transform.localPosition));
				}
			}
			_particleLoopSoundInstances.Clear();
		}

		protected virtual void UpdateParticleSystemsPositions()
		{
			if (_allParticleSystems == null || _allParticleSystems[0][0].transform.localPosition.magnitude <= minRadius)
			{
				return;
			}
			_reArrangeTimer += Time.fixedDeltaTime;
			if (_reArrangeTimer > 5f)
			{
				_reArrangeTimer = 0f;
				ReArrangeParticleSystems();
				return;
			}
			int num = Mathf.Max(1, _allParticleSystems.Count / 8);
			int num2 = 0;
			for (int i = 0; i < _allParticleSystems.Count; i++)
			{
				Vector3 localPosition = _allParticleSystems[i][0].transform.localPosition;
				_allParticleSystems[i][0].transform.localPosition -= localPosition.normalized * (_currentSpeed * Time.fixedDeltaTime);
				if (i % num == 0 && num2 < _particleLoopSoundInstances.Count)
				{
					_particleLoopSoundInstances[num2].set3DAttributes(GetFlattenedPosition(_allParticleSystems[i][0].transform.localPosition).To3DAttributes());
					num2++;
				}
			}
		}

		protected Vector3 GetFlattenedPosition(Vector3 localPosition)
		{
			Vector3 result = trapTransform.TransformPoint(localPosition);
			result.z = 0f;
			return result;
		}

		public void StopAllParticleSystems()
		{
			if (_allParticleSystems == null)
			{
				return;
			}
			foreach (ParticleSystem[] allParticleSystem in _allParticleSystems)
			{
				_particleSystemPooler.Return(allParticleSystem[0]);
			}
			_allParticleSystems.Clear();
		}
	}
}
