using System.Collections;
using AstralShift.HellMaiden.Combat.Traps;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss.Scarmiglione
{
	public class FireConcentricCirclesAttackBehaviour : BossAttackBehaviour
	{
		[Header("Attack specific")]
		[SerializeField]
		private Transform centerPosition;

		[SerializeField]
		private float betweenAttacksTimer = 0.5f;

		[SerializeField]
		private int numberOfAttacks = 2;

		private GenericPooler<FireConcentricCircle> _pooler;

		[SerializeField]
		private BarrierTrap barrierTrap;

		private BarrierTrap _currentTrap;

		public override void Positioning()
		{
			onPositioningEnd?.Invoke();
		}

		public override void Warning()
		{
			BarkWarning();
			WarningBossAnimation(onWarningEnd);
		}

		public override void Attack()
		{
			AttackBossAnimation(delegate
			{
				StartCoroutine(AttackRoutine());
			});
		}

		private IEnumerator AttackRoutine()
		{
			for (int i = 0; i < numberOfAttacks; i++)
			{
				SpawnTrap();
				yield return new WaitForSeconds(betweenAttacksTimer);
			}
			_currentTrap = null;
			onAttackEnd?.Invoke();
			yield return null;
		}

		public override void Stop()
		{
			StopAllCoroutines();
			if ((bool)_currentTrap)
			{
				_currentTrap.Stop();
			}
		}

		private void SpawnTrap()
		{
			_currentTrap = Object.Instantiate(barrierTrap, centerPosition, worldPositionStays: false);
			_currentTrap.target = centerPosition;
			_currentTrap.Init();
		}

		public override void Dispose()
		{
			if (_currentTrap != null)
			{
				_currentTrap.StopAllParticleSystems();
				_currentTrap.gameObject.SetActive(value: false);
			}
			base.Dispose();
		}
	}
}
