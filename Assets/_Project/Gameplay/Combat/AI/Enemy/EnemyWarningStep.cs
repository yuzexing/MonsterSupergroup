using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
    /// <summary>Source warning-only stepping. Network presentation never drives this component.</summary>
    public class EnemyWarningStep : MonoBehaviour
    {
        [SerializeField] private EnemyAttackMelee _attackMelee;
        [SerializeField] private Rigidbody2D _rigidbody;
        public float stepDistance = .5f;
        private EnemyController controller;
        private ulong actionId;
        private EnemyWarningStepState motion;
        private int lastFrame = -1;
        private double requestFixedTime;
        private bool subscribed;

        public void Configure(EnemyAttackMelee attack, Rigidbody2D body, float distance)
        { _attackMelee = attack; _rigidbody = body; stepDistance = distance; }

        private void Start()
        {
            Resolve();
            // Legacy callbacks and shared simulation must never both drive a step.
            if (_attackMelee == null || controller == null || controller.UsesSharedAttackTimeline) return;
            _attackMelee.OnWarningTick += AttackWarningTick;
            _attackMelee.onAttackWarningExit += AttackWarningEnd;
            subscribed = true;
        }

        private void Resolve()
        {
            if (_attackMelee == null) _attackMelee = GetComponent<EnemyAttackMelee>();
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody2D>();
            if (controller == null) controller = GetComponent<EnemyController>();
        }

        public void Begin(EnemyActionState state, ref EnemyWarningStepState checkpoint)
        {
            Release(); Resolve(); actionId = state.ActionId;
            motion = new EnemyWarningStepState { Enabled = enabled, SampledAt = state.ComboStartedAt };
            checkpoint = motion;
        }

        public void Capture(ulong id, ref EnemyWarningStepState checkpoint)
        {
            if (id == 0 || id != actionId) return;
            if (motion.Pending && Time.fixedTimeAsDouble > requestFixedTime) motion.Pending = false;
            checkpoint = motion;
        }

        public void Advance(EnemyActionState state, double now)
        {
            Resolve();
            if (!enabled || !state.WarningStep.Enabled || !state.Sequence || state.ActionId == 0 ||
                controller == null || !controller.IsAlive || controller.DeathRequested || _rigidbody == null ||
                _rigidbody.bodyType != RigidbodyType2D.Dynamic) return;
            if (state.ActionId != actionId) { Restore(state, now); return; }
            if (state.Phase == EnemyAttackPresentationPhase.Cancelled) { Release(); return; }
            if (Time.timeScale <= 0 || now <= motion.SampledAt || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;

            double start = state.ComboStartedAt;
            for (int i = 0; i < 3; i++)
            {
                byte bit = (byte)(1 << i);
                // Only warnings actually executed by this action get an exit step.
                if ((motion.StartedMask & bit) != 0 && (motion.CompletedMask & bit) == 0 && now >= start + state.SequenceWarnings[i])
                {
                    Request(_rigidbody.position + motion.Facing * stepDistance);
                    motion.CompletedMask |= bit;
                    controller.Movement.FreezeRigidbody(true);
                }
                start += (double)state.SequenceWarnings[i] + state.SequenceActives[i];
            }
            if (state.Phase == EnemyAttackPresentationPhase.Warning && EnemyActionTimeline.HasPose(state))
            {
                motion.StartedMask |= (byte)(1 << state.StrikeIndex);
                motion.Facing = state.Facing;
                controller.Movement.FreezeRigidbody(false);
                float u = Mathf.Clamp01((float)((now - state.WarningStartedAt) / (state.WarningUntil - state.WarningStartedAt)));
                Request(_rigidbody.position + state.Facing * (stepDistance * u));
            }
            motion.SampledAt = now;
        }

        public void Restore(EnemyActionState state, double now)
        {
            Release(); Resolve(); actionId = state.ActionId; motion = state.WarningStep;
            lastFrame = Time.frameCount;
            if (!motion.Enabled || !state.Sequence || state.Phase == EnemyAttackPresentationPhase.Cancelled) return;
            // Only restore an already requested physics move, not elapsed warning ticks.
            if (motion.Pending && _rigidbody != null && _rigidbody.bodyType == RigidbodyType2D.Dynamic)
                Request(motion.RequestedPosition);
            double start = state.ComboStartedAt;
            for (int i = 0; i < 3; i++)
            {
                if (now >= start + state.SequenceWarnings[i]) motion.CompletedMask |= (byte)(1 << i);
                start += (double)state.SequenceWarnings[i] + state.SequenceActives[i];
            }
            motion.SampledAt = System.Math.Max(motion.SampledAt, now);
            bool warning = state.Phase == EnemyAttackPresentationPhase.Warning && EnemyActionTimeline.HasPose(state);
            controller.Movement.FreezeRigidbody(!warning && (state.Phase == EnemyAttackPresentationPhase.Warning ||
                state.Phase == EnemyAttackPresentationPhase.Active || state.Phase == EnemyAttackPresentationPhase.Recovery));
        }

        private void Request(Vector2 position)
        {
            _rigidbody.MovePosition(position);
            motion.Pending = true; motion.RequestedPosition = position; requestFixedTime = Time.fixedTimeAsDouble;
        }

        public void Release()
        {
            if (motion.Pending && _rigidbody != null && _rigidbody.bodyType == RigidbodyType2D.Dynamic &&
                Time.fixedTimeAsDouble <= requestFixedTime)
                _rigidbody.MovePosition(_rigidbody.position);
            if (actionId != 0 && controller != null && controller.IsAlive && !controller.DeathRequested)
                controller.Movement?.FreezeRigidbody(false);
            actionId = 0; motion = default; lastFrame = -1;
        }

        private void AttackWarningTick(float progress)
        {
            if (controller == null || controller.UsesSharedAttackTimeline) return;
            controller.Movement.FreezeRigidbody(false);
            _rigidbody.MovePosition(_rigidbody.position + controller.FacingDirection * (stepDistance * Mathf.Clamp01(progress)));
        }

        private void AttackWarningEnd()
        {
            if (controller == null || controller.UsesSharedAttackTimeline) return;
            _rigidbody.MovePosition(_rigidbody.position + controller.FacingDirection * stepDistance);
            controller.Movement.FreezeRigidbody(true);
        }

        private void OnDisable() => Release();
        private void OnDestroy()
        {
            if (!subscribed || _attackMelee == null) return;
            _attackMelee.OnWarningTick -= AttackWarningTick;
            _attackMelee.onAttackWarningExit -= AttackWarningEnd;
        }
    }
}
