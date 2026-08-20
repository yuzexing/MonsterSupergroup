using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Interactions;
using AstralShift.Pooling;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackTrail : EnemyAttack
	{
		public EnemyTrailController attackPrefab;

		private EnemyTrailController _activeTrail;

		private GenericPooler<EnemyTrailController> _pooler;

		public PlayerDamageInteraction thornsDamageInteraction;

		private void Awake()
		{
			if (_pooler == null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			}
		}

		private void Start()
		{
			if (!(thornsDamageInteraction == null))
			{
				thornsDamageInteraction.enemyStats = base.controller.stats;
			}
		}

		public override void AttackWarningEnter()
		{
			base.AttackWarningEnter();
			base.controller.Movement.FreezeRigidbody(state: false);
		}

		public override void AttackEnter()
		{
			base.AttackEnter();
			if (_pooler == null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			}
			EnemyTrailController trail = _pooler.GetOrCreate(base.transform, activate: true);
			_activeTrail = trail;
			trail.Attack(base.controller, delegate
			{
				ReturnCurrentAttackToPool(trail);
			}, base.AttackTime);
		}

		private void ReturnCurrentAttackToPool(EnemyTrailController trail)
		{
			if (!(trail == null))
			{
				if (_activeTrail == trail)
				{
					_activeTrail = null;
				}
				_pooler.Return(trail);
			}
		}

		public override void CancelAttack()
		{
			if (!(_activeTrail == null))
			{
				_activeTrail.CancelAttackFadeOut();
				_activeTrail = null;
			}
		}

		private void OnDisable()
		{
			if (!(_activeTrail == null))
			{
				_activeTrail.CancelAttackFadeOut();
				_activeTrail = null;
			}
		}
	}
}
