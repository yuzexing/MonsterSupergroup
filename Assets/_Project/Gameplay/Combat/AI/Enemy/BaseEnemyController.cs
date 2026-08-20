using System;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public abstract class BaseEnemyController : MonoBehaviour
	{
		public string selectedName;

		[Header("Base References")]
		public EnemyStatus status;

		public EnemyStats stats;

		public SpriteRenderer spriteRenderer;

		public bool enemyFlyingType;

		public Transform movementCenter;

		public bool isElite;

		public bool enemyRanged;

		protected float _distanceToTarget;

		public float stoppingDistance = 0.1f;

		protected float _attackDistanceToPlayer;

		[SerializeField]
		protected Transform onHitEffectCenterPivot;

		[SerializeField]
		protected Transform onHitEffectCenterPivot1;

		[SerializeField]
		protected Transform onHitEffectCenterPivot2;

		[SerializeField]
		protected Transform onHitEffectCenterPivot3;

		[SerializeField]
		protected Transform onHitEffectTopPivot;

		[SerializeField]
		protected Transform onHitEffectBottomPivot;

		protected Transform _transform;

		protected bool _canRubberband = true;

		protected float rubberbandMaxDistance = 20f;

		public Vector2 windDirection = Vector2.zero;

		[HideInInspector]
		public bool forceWindInteraction;

		[HideInInspector]
		public Vector2 ultimateWindInteraction = Vector2.zero;

		protected bool _isImmune;

		protected KnockbackSettings _knockbackSettingsOverride;

		public Action OnInit;

		public Action OnInitPersist;

		public int ID { get; protected set; }

		public virtual Bounds LocalBounds => spriteRenderer.sprite.bounds;

		public virtual Bounds Bounds
		{
			get
			{
				Bounds bounds = spriteRenderer.sprite.bounds;
				Vector3 center = base.transform.TransformPoint(bounds.center);
				Vector3 size = Vector3.Scale(bounds.size, base.transform.lossyScale);
				return new Bounds(center, size);
			}
		}

		public Vector2 MovementCenterPosition => movementCenter ? movementCenter.position : base.transform.position;

		public bool IsInStoppingRange => _distanceToTarget <= stoppingDistance;

		public float DistanceToTarget => _distanceToTarget;

		public Transform OnHitEffectCenterPivot => onHitEffectCenterPivot;

		public Transform OnHitEffectCenterPivot1 => onHitEffectCenterPivot1;

		public Transform OnHitEffectCenterPivot2 => onHitEffectCenterPivot2;

		public Transform OnHitEffectCenterPivot3 => onHitEffectCenterPivot3;

		public Transform OnHitEffectTopPivot => onHitEffectTopPivot;

		public Transform OnHitEffectBottomPivot => onHitEffectBottomPivot;

		public Transform Transform => _transform;

		public bool CanRubberband => _canRubberband;

		public float RubberbandMaxDistance
		{
			get
			{
				return rubberbandMaxDistance;
			}
			set
			{
				rubberbandMaxDistance = value;
			}
		}

		public bool IsImmune => _isImmune;

		public abstract bool IsDead { get; }

		public abstract void Init(int id);

		public abstract void Dispose();

		public abstract void Damage(Vector2 attackPosition, WeaponBehaviour weapon, DamageType damageType);

		public abstract void Damage(int value, DamageType damageType);

		protected virtual void ApplyDamage(DamageInfo damageInfo)
		{
			ApplyDamage(damageInfo.value);
		}

		protected virtual void ApplyDamage(int value)
		{
			stats.Health -= value;
			stats.Health = Mathf.Max(0, stats.Health);
		}

		protected virtual void ApplyOnHitEffects(WeaponBehaviour weapon, DamageInfo damageInfo)
		{
			OnHitModifierArgs args = new OnHitModifierArgs
			{
				Enemy = this,
				Weapon = weapon,
				DamageInfo = damageInfo
			};
			for (int i = 0; i < weapon.EquipmentModifiers.OnHitModifiers.Count; i++)
			{
				args = weapon.EquipmentModifiers.OnHitModifiers[i].Apply(args);
			}
		}

		protected void ApplyOnKillEffects(WeaponBehaviour weapon)
		{
			OnKillModifierArgs args = new OnKillModifierArgs
			{
				Enemy = this,
				Weapon = weapon
			};
			for (int i = 0; i < weapon.EquipmentModifiers.OnKillModifiers.Count; i++)
			{
				args = weapon.EquipmentModifiers.OnKillModifiers[i].Apply(args);
			}
		}

		public abstract void ApplyKnockBack(Vector2 attackPosition, WeaponBehaviour weaponBehaviour, bool isFatal);

		public abstract void BruteforceKnockBack(Vector2 attackPosition, KnockbackSettings settings);

		public virtual void OverrideKnockbackSettings(KnockbackSettings settings)
		{
			_knockbackSettingsOverride = settings;
		}

		public virtual void SetImmunity(bool state)
		{
			_isImmune = state;
		}

		protected virtual void ShowDamageNumbers(DamageInfo info, DamageType damageType, Transform pivot)
		{
			ShowDamageNumbers((int)info.id, info.value, damageType, info.isCritical, pivot);
		}

		protected virtual void ShowDamageNumbers(int sourceID, int damage, DamageType damageType, bool isCritical, Transform pivot)
		{
			int num = damage / 10 + 1;
			int maxExclusive = num + 1;
			int minInclusive = ((!isCritical) ? (-num) : 0);
			int number = Mathf.Clamp(damage + UnityEngine.Random.Range(minInclusive, maxExclusive), 1, 10000000);
			int damageableID = GetEntityId();
			PoolManager.Instance.SpawnDamageNumber(sourceID, damageableID, pivot, number, damageType, isCritical);
		}

		public abstract Vector2 GetHurtBoxPosition();
	}
}
