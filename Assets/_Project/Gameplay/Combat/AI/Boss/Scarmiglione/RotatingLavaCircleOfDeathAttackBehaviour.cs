using AstralShift.HellMaiden.AI.Boss.Minos;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.AI.Boss.Scarmiglione
{
	public class RotatingLavaCircleOfDeathAttackBehaviour : BossAttackBehaviour
	{
		[FormerlySerializedAs("_bossMovementController")]
		[SerializeField]
		private MinosMovementController minosMovementController;

		[SerializeField]
		private FireCircleOfDeath fireCircleOfDeath;

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
			AttackBossAnimation(onAttackEnd);
			ActivateCircle();
			void ActivateCircle()
			{
				fireCircleOfDeath.gameObject.SetActive(value: true);
			}
		}

		public override void Stop()
		{
			fireCircleOfDeath.Despawn();
			base.Dispose();
		}
	}
}
