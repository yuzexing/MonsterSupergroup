using System.Collections.Generic;
using System.Linq;
using AstralShift.Pooling;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Traps
{
	public class PatternBarrierTrap : BarrierTrap
	{
		[SerializeField]
		private ImagePatternSpawnShape _imagePatternSpawnShape;

		[SerializeField]
		private ParticleSystem particleSystem2;

		private GenericPooler<ParticleSystem> _particleSystemPooler2;

		private const float RegenerateColliderTimeout = 3f;

		private const float RegeneratedColliderRadius = 2f;

		private bool _isColliderRegenerated;

		private float _regenerateColliderTimer;

		public override void Init()
		{
			if (_particleSystemPooler2 == null)
			{
				_particleSystemPooler2 = PoolManager.Instance.GetOrCreatePooler(particleSystem2, 200);
			}
			base.Init();
		}

		protected override void ShrinkCollision()
		{
		}

		protected override void GenerateCollider()
		{
			_collider.points = null;
			List<Vector2> list = new List<Vector2>();
			_points = new Vector2[numberOfSides];
			for (int i = 0; i < numberOfSides; i++)
			{
				list.Add(_imagePatternSpawnShape.GetEnemyPosition(Vector2.zero, numberOfSides, i));
			}
			list.Add(list[0]);
			if (list.Count < 3)
			{
				return;
			}
			Vector2 center = Vector2.zero;
			foreach (Vector2 item in list)
			{
				center += item;
			}
			center /= (float)list.Count;
			list = list.OrderBy((Vector2 p) => Mathf.Atan2(p.y - center.y, p.x - center.x)).ToList();
			for (int num = 0; num < list.Count - 2; num++)
			{
				if (Mathf.Approximately(list[num].x, list[num + 1].x))
				{
					if (list[num].sqrMagnitude > list[num + 1].sqrMagnitude)
					{
						list.RemoveAt(num);
					}
					else
					{
						list.RemoveAt(num + 1);
					}
				}
				if (Mathf.Approximately(list[num].y, list[num + 1].y))
				{
					if (list[num].sqrMagnitude > list[num + 1].sqrMagnitude)
					{
						list.RemoveAt(num);
					}
					else
					{
						list.RemoveAt(num + 1);
					}
				}
			}
			List<Vector2> list2 = list;
			list2[list2.Count - 1] = list[0];
			float edgeRadius = _collider.edgeRadius;
			Vector2[] array = new Vector2[list.Count];
			for (int num2 = 0; num2 < list.Count; num2++)
			{
				Vector2 zero = Vector2.zero;
				if (num2 == 0 || num2 == list.Count - 1)
				{
					Vector2 vector = list[0];
					List<Vector2> list3 = list;
					Vector2 normalized = (vector - list3[list3.Count - 2]).normalized;
					Vector2 normalized2 = (list[1] - list[0]).normalized;
					Vector2 vector2 = new Vector2(0f - normalized.y, normalized.x);
					Vector2 vector3 = new Vector2(0f - normalized2.y, normalized2.x);
					zero = (vector2 + vector3).normalized;
				}
				else
				{
					Vector2 normalized3 = (list[num2] - list[num2 - 1]).normalized;
					Vector2 normalized4 = (list[num2 + 1] - list[num2]).normalized;
					Vector2 vector4 = new Vector2(0f - normalized3.y, normalized3.x);
					Vector2 vector5 = new Vector2(0f - normalized4.y, normalized4.x);
					zero = (vector4 + vector5).normalized;
				}
				array[num2] = list[num2] + -zero * edgeRadius;
			}
			_points = array;
			_collider.points = _points;
		}

		protected override void CreateParticleSystems()
		{
			int num = numberOfSides;
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
			for (int i = 0; i < num; i++)
			{
				ParticleSystem particleSystem = null;
				Vector3 localPosition = _imagePatternSpawnShape.GetEnemyPosition(Vector2.zero, num, i);
				particleSystem = ((!(localPosition.x > 0.5f)) ? _particleSystemPooler2.GetOrCreate(trapTransform, activate: true) : _particleSystemPooler.GetOrCreate(trapTransform, activate: true));
				particleSystem.transform.localPosition = localPosition;
				ParticleSystem[] componentsInChildren = particleSystem.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
				_allParticleSystems.Add(componentsInChildren);
			}
			SpreadLoopInstances();
		}

		protected override void ReArrangeParticleSystems()
		{
		}

		protected override void UpdateParticleSystemsPositions()
		{
			if (_allParticleSystems == null)
			{
				return;
			}
			_reArrangeTimer += Time.fixedDeltaTime;
			_regenerateColliderTimer += Time.fixedDeltaTime;
			if (_regenerateColliderTimer > 3f && !_isColliderRegenerated)
			{
				_isColliderRegenerated = true;
				_collider.edgeRadius = 2f;
				GenerateCollider();
			}
			if (_reArrangeTimer > 5f)
			{
				_reArrangeTimer = 0f;
				ReArrangeParticleSystems();
				return;
			}
			base.transform.localScale -= Vector3.one * (Time.fixedDeltaTime * 0.015f);
			int num = Mathf.Max(1, _allParticleSystems.Count / 8);
			int num2 = 0;
			for (int i = 0; i < _allParticleSystems.Count; i++)
			{
				_allParticleSystems[i][0].transform.localScale = new Vector3(1f / base.transform.localScale.x, 1f / base.transform.localScale.y, 1f);
				if (i % num == 0 && num2 < _particleLoopSoundInstances.Count)
				{
					_particleLoopSoundInstances[num2].set3DAttributes(GetFlattenedPosition(_allParticleSystems[i][0].transform.localPosition).To3DAttributes());
					num2++;
				}
			}
		}
	}
}
