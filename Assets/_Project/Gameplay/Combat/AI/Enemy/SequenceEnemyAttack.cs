using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class SequenceEnemyAttack : EnemyAttackMelee
	{
        private EnemyWarningStep warningStep;
        private EnemyWarningStep WarningStep => warningStep != null ? warningStep : warningStep = GetComponent<EnemyWarningStep>();
        public override void PrepareTimeline(ref EnemyActionState state) => WarningStep?.Begin(state, ref state.WarningStep);
        public override void CaptureSimulationMotion(ref EnemyActionState state) => WarningStep?.Capture(state.ActionId, ref state.WarningStep);
        public override void ApplySimulationFrame(EnemyActionState state, double now) => WarningStep?.Advance(state, now);
        public override void RestoreSimulationMotion(EnemyActionState state, double now) => WarningStep?.Restore(state, now);
        public override void ReleaseSimulationMotion() => WarningStep?.Release();
        public override void SuspendSimulation()
        {
            ReleaseSimulationMotion();
            base.SuspendSimulation();
        }
        public override bool SupportsSharedTimeline => true;
        protected override EnemyStrikeTiming[] CreateTimelineStrikes()
        {
            var animator = (MultipleAttackAnimator)enemyAnimator;
            var warnings = animator.SequenceWarnings; var actives = animator.SequenceActives;
            return new[] { new EnemyStrikeTiming(warnings.x, actives.x), new EnemyStrikeTiming(warnings.y, actives.y),
                new EnemyStrikeTiming(warnings.z, actives.z) };
        }
        protected override Vector3 TimelineLocalOffset(int strike) => (strike % 2 == 0 ? -1 : 1) * areaSideWarpDistance;
        public override void ApplyLocalFrame(EnemyActionState state, double now, bool changed)
        {
            ((MultipleAttackAnimator)enemyAnimator).SetSequencePresentationIndex(state.StrikeIndex);
            currentAttackCount = state.Phase == EnemyAttackPresentationPhase.Recovery ? 0 : state.StrikeIndex;
            base.ApplyLocalFrame(state, now, changed);
        }
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
            ReleaseSimulationMotion();
			currentAttackCount = 0;
			base.CancelAttack();
		}
	}
}
