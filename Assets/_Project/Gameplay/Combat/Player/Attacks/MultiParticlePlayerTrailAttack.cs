using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class MultiParticlePlayerTrailAttack : BasePlayerAttack
	{
		private struct TrailParticleData
		{
			public ParticleSystem particle;

			public float duration;

			public float startTime;
		}

		[SerializeField]
		private ParticleSystem trailParticles;

		[SerializeField]
		private ParticleSystem trailStepParticles;

		[SerializeField]
		private EdgeCollider2D edgeCollider;

		[SerializeField]
		private float trailDelta = 0.5f;

		[SerializeField]
		private EventReference soundEvent;

		private PlayerMovement _player;

		private GenericPooler<ParticleSystem> _pooler;

		private List<TrailParticleData> _particles;

		private List<Vector2> _colliderPoints;

		private Vector2 _lastParticlePosition = Vector2.one * float.MinValue;

		private float _attackStart;

		private int particleIdx;

		private float _trailParticleDuration = -1f;

		public Action onAttackDurationFinished;

		private bool _durationTicking;

		private EventInstance soundEventStartInstance;

		private float _attackStartCache;

		private WaitForSeconds _particleEndWaitForSeconds;

		private const float ParticleEndCheckInterval = 0.5f;

		public Transform trailStart { get; set; }

		public override void Attack()
		{
			_attackStart = Time.time;
			_attackStartCache = _attackStart;
			_durationTicking = true;
			edgeCollider.enabled = true;
			_lastParticlePosition = _player.CurrentPosition;
			RuntimeManager.AttachInstanceToGameObject(soundEventStartInstance, _player.transform);
			soundEventStartInstance.start();
		}

		public override void Dispose()
		{
			End();
		}

		public override void Init(WeaponBehaviour behaviour, Action onStart = null, Action onEnd = null)
		{
			base.Init(behaviour, onStart, onEnd);
			_pooler = PoolManager.Instance.GetOrCreatePooler(trailParticles);
			_particles = new List<TrailParticleData>();
			_colliderPoints = new List<Vector2>();
			_player = GameDirector.Instance.Player;
			edgeCollider.points = null;
			if (trailStart == null)
			{
				trailStart = _player.transform;
			}
			if (trailStepParticles != null)
			{
				trailStepParticles.gameObject.SetActive(value: true);
				trailStepParticles.transform.parent = _player.transform;
				trailStepParticles.transform.localPosition = Vector3.zero;
				trailStepParticles.Clear();
				trailStepParticles.Play();
			}
			soundEventStartInstance = RuntimeManager.CreateInstance(soundEvent);
			((PlayerAttackOvertimeHitBox)hitbox).SetHitInterval(behaviour.SpeedValue);
			base.transform.position = _player.CurrentPosition;
		}

		public void SetTrailDelta(float delta)
		{
			trailDelta = delta;
		}

		public void SetTrailParticleDuration(float duration)
		{
			_trailParticleDuration = duration;
		}

		private void Update()
		{
			if (Time.time - _attackStart < _behaviour.DurationValue)
			{
				Vector2 vector = trailStart.position;
				Vector2 vector2 = vector - _lastParticlePosition;
				if (new Vector2(vector2.x - vector2.y, (vector2.y + vector2.x) * 0.5f).magnitude > trailDelta)
				{
					ParticleSystem orCreateParticle = GetOrCreateParticle();
					Vector2 vector3 = vector - _lastParticlePosition;
					vector3.y *= 0.5f;
					vector3.Normalize();
					orCreateParticle.transform.position = _lastParticlePosition + vector3 * trailDelta;
					_lastParticlePosition = orCreateParticle.transform.position;
					TrailParticleData item = new TrailParticleData
					{
						particle = orCreateParticle,
						startTime = Time.time,
						duration = ((_trailParticleDuration == -1f) ? (_behaviour.DurationValue / 2f) : _trailParticleDuration)
					};
					Vector2 item2 = base.transform.InverseTransformPoint(_lastParticlePosition);
					_colliderPoints.Add(item2);
					_particles.Add(item);
					orCreateParticle.Clear();
					orCreateParticle.Play();
					UpdateCollider();
				}
			}
			else if (_durationTicking)
			{
				onAttackDurationFinished?.Invoke();
				_durationTicking = false;
			}
			if (_colliderPoints.Count <= 0)
			{
				return;
			}
			List<TrailParticleData> particles = _particles;
			int count = _colliderPoints.Count;
			TrailParticleData trailParticleData = particles[particles.Count - count];
			if (Time.time - trailParticleData.startTime > trailParticleData.duration)
			{
				trailParticleData.particle.Stop();
				_colliderPoints.RemoveAt(0);
				if (_colliderPoints.Count == 0)
				{
					edgeCollider.enabled = false;
					StartCoroutine(CheckParticleEnd());
				}
			}
			UpdateCollider();
		}

		private ParticleSystem GetOrCreateParticle()
		{
			return _pooler.GetOrCreate(null, activate: true);
		}

		private IEnumerator CheckParticleEnd()
		{
			if (_particleEndWaitForSeconds == null)
			{
				_particleEndWaitForSeconds = new WaitForSeconds(0.5f);
			}
			bool finished;
			do
			{
				finished = true;
				foreach (TrailParticleData particle in _particles)
				{
					if (particle.particle.IsAlive(withChildren: true))
					{
						finished = false;
						yield return _particleEndWaitForSeconds;
						break;
					}
				}
			}
			while (!finished);
			End();
		}

		private void UpdateCollider()
		{
			edgeCollider.points = null;
			if (_colliderPoints.Count == 1)
			{
				Vector2[] points = new Vector2[2]
				{
					_colliderPoints[0],
					_colliderPoints[0]
				};
				edgeCollider.points = points;
			}
			else
			{
				Vector2[] points2 = _colliderPoints.ToArray();
				edgeCollider.points = points2;
			}
		}

		private void End()
		{
			soundEventStartInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			if (trailStepParticles != null)
			{
				trailStepParticles.transform.parent = base.transform;
				trailStepParticles.Stop();
				trailStepParticles.gameObject.SetActive(value: false);
			}
			Debug.Log("all trail particles ended, returning to pool");
			foreach (TrailParticleData particle in _particles)
			{
				_pooler.Return(particle.particle);
			}
			_onEnd?.Invoke();
		}

		private void OnDestroy()
		{
			if (trailStepParticles != null)
			{
				UnityEngine.Object.Destroy(trailStepParticles.gameObject);
			}
			soundEventStartInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			soundEventStartInstance.release();
		}

		public void Interrupt()
		{
			_attackStart = -100000f;
		}

		public void ReturnFromInterrupt()
		{
			_attackStart = _attackStartCache;
		}
	}
}
