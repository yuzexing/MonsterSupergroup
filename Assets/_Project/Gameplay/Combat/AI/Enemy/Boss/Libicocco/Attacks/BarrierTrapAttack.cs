using AstralShift.HellMaiden.AI.Boss;
using AstralShift.HellMaiden.Interactions;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy.Boss.Libicocco.Attacks
{
	public class BarrierTrapAttack : BossAttackBehaviour
	{
		public TrapInteraction trapInteraction;

		public Transform trapTarget;

		public override void Positioning()
		{
			onPositioningEnd?.Invoke();
		}

		public override void Warning()
		{
			onWarningEnd?.Invoke();
		}

		public override void Attack()
		{
			trapInteraction.Interact(null);
			onAttackEnd?.Invoke();
		}
	}
}
