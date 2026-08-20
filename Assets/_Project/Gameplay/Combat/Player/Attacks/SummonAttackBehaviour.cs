using AstralShift.HellMaiden.Combat.Hand;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class SummonAttackBehaviour : WeaponBehaviour
	{
		[SerializeField]
		protected SummonAIVariants variants;

		protected SummonAIBehaviour _summonAI;

		private AttackElement _summonElement;

		private float _lastAttackElapsedTime;

		protected float _lastAttackTimeStamp;

		public override float LastAttackElapsedTime
		{
			get
			{
				_lastAttackElapsedTime = Time.time - _lastAttackTimeStamp;
				return _lastAttackElapsedTime;
			}
			protected set
			{
				_lastAttackElapsedTime = value;
			}
		}

		public override void Init(uint id, AttackStats stats)
		{
			base.Init(id, stats);
			variants.Init();
			EnsureSummon();
		}

		public virtual void Update()
		{
			_summonAI?.OnUpdate();
		}

		public new virtual bool CheckCooldown()
		{
			return LastAttackElapsedTime >= GetCooldown();
		}

		public virtual void SetLastAttackTime()
		{
			_lastAttackTimeStamp = Time.time;
		}

		public override void UpdateModifiers(RuntimeEquipmentModifiers runtimeModifiers)
		{
			base.UpdateModifiers(runtimeModifiers);
			RefreshSummonElement();
			_summonAI?.UpdateProgressionScaler();
		}

		protected void EnsureSummon()
		{
			if (!(_summonAI != null))
			{
				_summonElement = variants.ResolveElement(base.ActiveElement);
				_summonAI = variants.GetOrCreate(_summonElement, worldPositionStays: true);
				ConfigureSummon(_summonAI, isInitialSpawn: true);
				_summonAI.Init(this);
			}
		}

		protected void RefreshSummonElement()
		{
			if (!(_summonAI == null))
			{
				AttackElement attackElement = variants.ResolveElement(base.ActiveElement);
				if (attackElement != _summonElement)
				{
					Vector3 position = _summonAI.Transform.position;
					Quaternion rotation = _summonAI.Transform.rotation;
					_summonAI.Dispose();
					variants.Return(_summonAI);
					_summonElement = attackElement;
					_summonAI = variants.GetOrCreate(attackElement, worldPositionStays: true);
					_summonAI.Transform.position = position;
					_summonAI.Transform.rotation = rotation;
					ConfigureSummon(_summonAI, isInitialSpawn: false);
					_summonAI.Init(this);
				}
			}
		}

		protected virtual void ConfigureSummon(SummonAIBehaviour summon, bool isInitialSpawn)
		{
		}

		protected override void Dispose()
		{
			_summonAI?.Dispose();
			variants.Dispose();
			_summonAI = null;
			_summonElement = AttackElement.Default;
		}
	}
}
