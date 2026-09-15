using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class SequenceEnemyAttack : EnemyAttackMelee
	{
		public int currentAttackCount;

		public int consecutiveAttacks = 1;

		public Vector3 areaSideWarpDistance = new Vector3(0f, 0.5f, 0f);

		public override void AttackWarningEnter()
		{
            if(controller.TryReadSimulationAction(out _,out _))((MultipleAttackAnimator)enemyAnimator).SetSequencePresentationIndex(currentAttackCount);
			base.AttackWarningEnter();
			if (currentAttackCount <= consecutiveAttacks && currentAttackCount % 2 != 0)
			{
				_attack.transform.localPosition = areaSideWarpDistance;
			}
			else
			{
				_attack.transform.localPosition = -areaSideWarpDistance;
			}
		}

        public override void AttackWarningTick()
        { if (!controller.TickSequenceAction()) base.AttackWarningTick(); }
        public override void AttackTick()
        { if (!controller.TickSequenceAction()) base.AttackTick(); }
        public override void RecoveryTick()
        { if (!controller.TickSequenceAction()) base.RecoveryTick(); }

        public void RestoreSequence(EnemyActionState state, double now, bool settlePrevious)
        {
            if (settlePrevious) _attack?.damageInteraction?.SettlePendingCollisions();
            ((MultipleAttackAnimator)enemyAnimator).SetSequencePresentationIndex(state.StrikeIndex);
            currentAttackCount = state.Phase == EnemyAttackPresentationPhase.Recovery ? 0 : state.StrikeIndex;
            base.RestoreSimulation(state.Phase, state.Facing,
                EnemySequenceTimeline.HasPose(state) ? (float)System.Math.Max(0, state.EndAt(state.Phase) - now) : 0);
            if (_attack != null)
            {
                _attack.transform.localPosition = (state.StrikeIndex % 2 == 0 ? -1 : 1) * areaSideWarpDistance;
                if (state.Phase == EnemyAttackPresentationPhase.Warning)
                    _warning?.RestoreProgress((float)(state.WarningUntil-state.WarningStartedAt),
                        (float)(state.ActiveUntil-state.WarningUntil), (float)(now-state.WarningStartedAt));
            }
        }

		public override void RecoveryEnter()
		{
			currentAttackCount++;
			if (currentAttackCount <= consecutiveAttacks)
			{
				base.controller.TransitionToWarning();
				return;
			}
			currentAttackCount = 0;
			base.RecoveryEnter();
		}

		public override void CancelAttack()
		{
			currentAttackCount = 0;
			base.CancelAttack();
		}
	}
}
