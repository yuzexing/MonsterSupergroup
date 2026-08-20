using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Interactions;
using AstralShift.Pooling;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

public class EnemyTrailController : MonoBehaviour
{
	[Serializable]
	private class TrailSegment
	{
		public ParticleSystem particle;

		public Vector2 localPoint;

		public float deathTime;
	}

	[Header("Particles")]
	public ParticleSystem trailParticles;

	public ParticleSystem trailStepParticles;

	[Header("Lifetime")]
	public bool applyChangesToLifeTime;

	[SerializeField]
	private ParticleSystemLifeTimeControler lifeTimeControler;

	[Header("Trail")]
	public float trailDelta = 1f;

	public EdgeCollider2D edgeCollider;

	[Header("Audio")]
	[SerializeField]
	private EventReference soundEvent;

	[Header("Damage")]
	public PlayerDamageInteraction fireDamageInteraction;

	private EnemyController _controller;

	private float _attackDuration = 3f;

	private float _attackStartTime;

	private GenericPooler<ParticleSystem> _pooler;

	private EventInstance _soundEventInstance;

	private Vector2 _lastParticlePosition = Vector2.one;

	private bool _cancelRequested;

	private bool _ending;

	private Action _onEnd;

	private bool _colliderDirty;

	private List<TrailSegment> _segments = new List<TrailSegment>();

	public void SetStats(EnemyStats stats)
	{
		if (fireDamageInteraction != null)
		{
			fireDamageInteraction.enemyStats = stats;
		}
	}

	public void Attack(EnemyController controller, Action onEnd, float attackTime)
	{
		_controller = controller;
		_attackDuration = attackTime;
		_onEnd = onEnd;
		_cancelRequested = false;
		_ending = false;
		base.transform.SetParent(null);
		base.transform.position = _controller.transform.position;
		base.transform.rotation = Quaternion.identity;
		SetStats(_controller.stats);
		_pooler = PoolManager.Instance.GetOrCreatePooler(trailParticles);
		_segments.Clear();
		if (edgeCollider != null)
		{
			edgeCollider.enabled = false;
			edgeCollider.points = Array.Empty<Vector2>();
		}
		if (trailStepParticles != null)
		{
			trailStepParticles.gameObject.SetActive(value: true);
			trailStepParticles.transform.SetParent(_controller.transform);
			trailStepParticles.transform.localPosition = Vector3.zero;
			trailStepParticles.transform.localRotation = Quaternion.identity;
			trailStepParticles.Clear(withChildren: true);
			trailStepParticles.Play(withChildren: true);
		}
		if (_soundEventInstance.isValid())
		{
			_soundEventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
			_soundEventInstance.release();
			_soundEventInstance = RuntimeManager.CreateInstance(soundEvent);
			RuntimeManager.AttachInstanceToGameObject(_soundEventInstance, _controller.transform);
			_soundEventInstance.start();
		}
		_attackStartTime = Time.time;
		_lastParticlePosition = _controller.transform.position;
	}

	private void Update()
	{
		if (!_ending && !(_controller == null))
		{
			if (!_controller.gameObject.activeInHierarchy)
			{
				CancelAttackFadeOut();
			}
			else if (!_cancelRequested && Time.time - _attackStartTime < _attackDuration)
			{
				TrySpawnTrailParticle();
			}
			PruneExpiredSegments();
		}
	}

	private void PruneExpiredSegments()
	{
		bool flag = false;
		float time = Time.time;
		for (int num = _segments.Count - 1; num >= 0; num--)
		{
			TrailSegment trailSegment = _segments[num];
			if (trailSegment == null || !(time < trailSegment.deathTime))
			{
				_segments.RemoveAt(num);
				flag = true;
				if (trailSegment?.particle != null)
				{
					trailSegment.particle.transform.SetParent(null, worldPositionStays: true);
					_pooler.Return(trailSegment.particle);
				}
			}
		}
		if (flag || _colliderDirty)
		{
			RebuildCollider();
			_colliderDirty = false;
		}
		if ((_cancelRequested || time - _attackStartTime >= _attackDuration) && _segments.Count == 0)
		{
			End();
		}
	}

	private void TrySpawnTrailParticle()
	{
		Vector2 vector = _controller.transform.position;
		Vector2 vector2 = ComputeIsoDelta(vector, _lastParticlePosition);
		if (vector2.magnitude > 5f)
		{
			_lastParticlePosition = vector;
			return;
		}
		int num = 0;
		while (vector2.magnitude > trailDelta && num++ < 32)
		{
			if (!SpawnSingleParticle(vector))
			{
				return;
			}
			vector2 = ComputeIsoDelta(vector, _lastParticlePosition);
		}
		if (_colliderDirty)
		{
			RebuildCollider();
			_colliderDirty = false;
		}
	}

	private bool SpawnSingleParticle(Vector2 enemyPosition)
	{
		ParticleSystem orCreateParticle = GetOrCreateParticle();
		Vector2 vector = enemyPosition - _lastParticlePosition;
		vector.y *= 0.5f;
		vector.Normalize();
		Vector2 vector2 = (_lastParticlePosition += vector * trailDelta);
		orCreateParticle.transform.position = vector2;
		orCreateParticle.transform.rotation = Quaternion.identity;
		ParticleStoppedNotifier component = orCreateParticle.GetComponent<ParticleStoppedNotifier>();
		if (component != null)
		{
			component.ClearCallback();
		}
		TrailSegment item = new TrailSegment
		{
			particle = orCreateParticle,
			localPoint = base.transform.InverseTransformPoint(vector2),
			deathTime = Time.time + GetSegmentLifetime(orCreateParticle)
		};
		_segments.Add(item);
		orCreateParticle.Clear(withChildren: true);
		orCreateParticle.Play(withChildren: true);
		_colliderDirty = true;
		return true;
	}

	private float GetSegmentLifetime(ParticleSystem particle)
	{
		if (applyChangesToLifeTime && lifeTimeControler != null)
		{
			return _attackDuration;
		}
		return particle.main.startLifetime.constantMax;
	}

	private static Vector2 ComputeIsoDelta(Vector2 enemyPosition, Vector2 lastPosition)
	{
		Vector2 vector = enemyPosition - lastPosition;
		return new Vector2(vector.x - vector.y, (vector.y + vector.x) * 0.5f);
	}

	public void CancelAttackFadeOut()
	{
		if (!_cancelRequested && !_ending)
		{
			_cancelRequested = true;
			if (trailStepParticles != null)
			{
				trailStepParticles.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
			}
			StopAllParticlesEmission();
		}
	}

	private void StopAllParticlesEmission()
	{
		for (int i = 0; i < _segments.Count; i++)
		{
			TrailSegment trailSegment = _segments[i];
			if (!(trailSegment?.particle == null))
			{
				trailSegment.particle.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
			}
		}
	}

	private void RebuildCollider()
	{
		if (edgeCollider == null)
		{
			return;
		}
		int count = _segments.Count;
		if (count == 0)
		{
			edgeCollider.enabled = false;
			edgeCollider.points = Array.Empty<Vector2>();
			return;
		}
		Vector2[] array = new Vector2[count];
		for (int i = 0; i < count; i++)
		{
			array[i] = _segments[i].localPoint;
		}
		edgeCollider.enabled = true;
		edgeCollider.points = ((count != 1) ? array : new Vector2[2]
		{
			array[0],
			array[0]
		});
	}

	private ParticleSystem GetOrCreateParticle()
	{
		ParticleSystem orCreate = _pooler.GetOrCreate(base.transform, activate: true);
		orCreate.transform.SetParent(base.transform, worldPositionStays: true);
		orCreate.transform.localScale = Vector3.one;
		orCreate.transform.localRotation = Quaternion.identity;
		ParticleSystem.MainModule main = orCreate.main;
		main.loop = false;
		main.stopAction = ParticleSystemStopAction.Callback;
		if (applyChangesToLifeTime && lifeTimeControler != null)
		{
			lifeTimeControler.SetParticleSystemLifeTime(orCreate, _attackDuration);
		}
		return orCreate;
	}

	public void End()
	{
		if (!_ending)
		{
			_ending = true;
			if (edgeCollider != null)
			{
				edgeCollider.enabled = false;
				edgeCollider.points = Array.Empty<Vector2>();
			}
			if (_soundEventInstance.isValid())
			{
				_soundEventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
				_soundEventInstance.release();
			}
			_segments.Clear();
			_controller = null;
			_cancelRequested = false;
			Action onEnd = _onEnd;
			_onEnd = null;
			onEnd?.Invoke();
		}
	}
}
