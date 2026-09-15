using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackMelee : EnemyAttack
	{
		public EnemyAttackPrefab attackPrefab;

		protected EnemyAttackPrefab _attack;

		protected EnemyAttackWarning _warning;

		protected GameObject _collidersGameObject;

		protected BaseAttackHitBox _hitBox;

		private uint simulationGeneration;
        public bool HasSimulationAttackInstance => _attack != null;
        public EnemyAttackPrefab SimulationAttackInstance => _attack;

        private void OnDisable()
        {
            SuspendSimulation();
        }

		public override void AttackWarningEnter()
		{
			SuspendSimulation();
			base.AttackWarningEnter();
			_attackPooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
			_attack = _attackPooler.GetOrCreate(base.transform, activate: true);
			_attack.transform.position = base.transform.position;
			_attack.SetStats(base.controller.stats);
			Vector2 vector = ((!(base.Target != null)) ? base.controller.FacingDirection : ((Vector2)(base.Target.position - _attack.transform.position)));
			float z = Mathf.Atan2(vector.y, vector.x) * 57.29578f;
			_attack.transform.rotation = Quaternion.Euler(0f, 0f, z);
			_warning = _attack.attackWarning;
			_warning.SetWarningTime(base.WarningTime, base.AttackTime);
			_warning.Show();
			if ((bool)_attack.damageInteraction)
			{
				_collidersGameObject = _attack.damageInteraction.gameObject;
				_collidersGameObject.SetActive(value: false);
			}
			if ((bool)_attack.hitBox)
			{
				_hitBox = _attack.hitBox;
				_hitBox.Toggle(state: false);
			}
		}

		public override void AttackEnter()
		{
			base.AttackEnter();
			if ((bool)_attack)
			{
				_attack.EnableDamage();
			}
			_warning?.Hide();
			if ((bool)_collidersGameObject)
			{
				_collidersGameObject.SetActive(value: true);
			}
			if ((bool)_hitBox)
			{
				_hitBox.Toggle(state: true);
			}
		}

        public override async void AttackExit()
        {
            if (controller != null && controller.IsAlive) _attack?.damageInteraction?.SettlePendingCollisions();
            else _attack?.damageInteraction?.DiscardPendingCollisions();
            if (_collidersGameObject != null) _collidersGameObject.SetActive(false);
            if (_hitBox != null) _hitBox.Toggle(false);
            var instance = _attack;
            var warning = _warning;
            uint generation = simulationGeneration;
            if (warning != null) await warning.AwaitableHide();
            if (generation == simulationGeneration && instance == _attack) SuspendSimulation();
        }

        public override void CancelAttack()
        {
            controller.lastAttackTime = Time.time;
            SuspendSimulation();
        }

        public virtual void SuspendSimulation()
        {
            simulationGeneration++;
            if (_attack == null) return;
            _attack.damageInteraction?.DiscardPendingCollisions();
            if (_collidersGameObject != null) _collidersGameObject.SetActive(false);
            if (_hitBox != null) _hitBox.Toggle(false);
            var released = _attack;
            _attack = null; _warning = null; _collidersGameObject = null; _hitBox = null;
            released.gameObject.SetActive(false);
            if (!gameObject.activeInHierarchy) EnemyAttackPrefab.ReturnAfterHierarchyChange(released, _attackPooler);
            else _attackPooler?.Return(released);
        }

        public virtual void RestoreSimulation(EnemyAttackPresentationPhase phase, Vector2 facing, float remaining)
        {
            SuspendSimulation();
            if ((phase != EnemyAttackPresentationPhase.Warning && phase != EnemyAttackPresentationPhase.Active) || remaining <= 0) return;
            _attackPooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
            _attack = _attackPooler.GetOrCreate(transform, true);
            _attack.transform.position = transform.position;
            _attack.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg);
            _attack.SetStats(controller.stats);
            _warning = _attack.attackWarning;
            _collidersGameObject = _attack.damageInteraction != null ? _attack.damageInteraction.gameObject : null;
            _hitBox = _attack.hitBox;
            bool active = phase == EnemyAttackPresentationPhase.Active;
            if (_collidersGameObject != null) _collidersGameObject.SetActive(active);
            if (_hitBox != null) _hitBox.Toggle(active);
            if (_warning != null)
            {
                if (active) _warning?.Hide();
                else { _warning.SetWarningTime(remaining, AttackTime); _warning.Show(); }
            }
        }
    }
}
