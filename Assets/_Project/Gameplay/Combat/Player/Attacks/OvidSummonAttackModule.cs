using System;
using System.Collections.Generic;
using Animancer;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public class OvidSummonAttackModule : AttackStateModule
    {
        [SerializeField] private OvidSummonMover mover;
        [SerializeField] private BaseAttackHitBox hitBox;
        [SerializeField] private float minDetectionRadius = 5f;
        [SerializeField] private float maxDetectionRadius = 10f;
        [SerializeField] private float clusterSearchRadius = 4f;
        [SerializeField] private int framePartitioningCount = 4;
        [SerializeField] private int maxEnemiesToProcess = 100;
        [SerializeField] private float angleOffset = 180f;
        [SerializeField] private float aimSmoothing = 15f;
        [SerializeField] private float sweepAngle = 30f;
        [SerializeField] private CustomAnimationCurve sweepAccelerationCurve;
        [SerializeField] private float predictionLeadTime = 0.2f;
        [SerializeField] private float velocitySmoothing = 10f;
        [SerializeField] private ClipTransition attackEnterAnimation;
        [SerializeField] private ClipTransition attackLoopAnimation;
        [SerializeField] private ClipTransition attackExitAnimation;
        [SerializeField] private EventReference beamSound;

        private readonly List<SummonTarget> targets = new List<SummonTarget>();
        private SummonTarget target;
        private EventInstance attackSoundInstance;
        private Transform rotationPivot;
        private Vector2 lastTargetPosition;
        private Vector2 smoothedVelocity;
        private bool searching;
        private bool started;
        private int searchIndex;
        private int chunkSize;
        private int bestScore;
        private float elapsed;
        private float mainDuration;
        private float enterDuration;
        private float exitDuration;
        private float startAngle;
        private float targetAngle;

        public float MinDetectionRadius => _aiBehaviour.WeaponBehaviour.SizeValue * minDetectionRadius;
        public float MaxDetectionRadius => _aiBehaviour.WeaponBehaviour.SizeValue * maxDetectionRadius;
        public ClipTransition EnterAnimation => attackEnterAnimation;
        public ClipTransition MainAnimation => attackLoopAnimation;
        public ClipTransition ExitAnimation => attackExitAnimation;
        public float EnterDuration => SummonAIBehaviour.ClipDuration(attackEnterAnimation);
        public float ExitDuration => SummonAIBehaviour.ClipDuration(attackExitAnimation);
        public BaseAttackHitBox HitBox => hitBox;

        public override void Init(SummonAIBehaviour behaviour, Action onComplete)
        {
            base.Init(behaviour, onComplete);
            rotationPivot = mover.GetRotationPivot();
        }

        public override void Enter()
        {
            isComplete = false;
            started = false;
            target = default;
            smoothedVelocity = Vector2.zero;
            _aiBehaviour.WeaponBehaviour.QueryTargets(_aiBehaviour.transform.position,
                MinDetectionRadius, MaxDetectionRadius, targets);
            if (targets.Count == 0) { Exit(); return; }
            if (targets.Count == 1) { target = targets[0]; StartAttack(); return; }
            Vector2 position = _aiBehaviour.transform.position;
            if (targets.Count > maxEnemiesToProcess)
            {
                targets.Sort((left, right) => (left.Position - position).sqrMagnitude.CompareTo((right.Position - position).sqrMagnitude));
                targets.RemoveRange(Mathf.Max(1, maxEnemiesToProcess), targets.Count - Mathf.Max(1, maxEnemiesToProcess));
            }
            searching = true;
            searchIndex = 0;
            bestScore = -1;
            chunkSize = Mathf.Max(1, Mathf.CeilToInt((float)targets.Count / Mathf.Max(1, framePartitioningCount)));
            SearchChunk();
        }

        private void SearchChunk()
        {
            int end = Mathf.Min(targets.Count, searchIndex + chunkSize);
            for (; searchIndex < end; searchIndex++)
            {
                SummonTarget candidate = targets[searchIndex];
                if (!candidate.IsAvailable) continue;
                int score = 0;
                for (int other = 0; other < targets.Count; other++)
                    if (other != searchIndex && targets[other].IsAvailable &&
                        (targets[other].Position - candidate.Position).sqrMagnitude <= clusterSearchRadius * clusterSearchRadius) score++;
                if (score > bestScore) { bestScore = score; target = candidate; }
            }
            if (searchIndex < targets.Count) return;
            searching = false;
            if (target.IsAvailable) StartAttack();
            else Exit();
        }

        private void StartAttack()
        {
            if (_aiBehaviour == null || !target.IsAvailable || !_aiBehaviour.WeaponBehaviour.TryBeginNativeAttack())
            {
                if (_aiBehaviour != null) Exit();
                return;
            }
            if (_aiBehaviour == null) return;
            started = true;
            elapsed = 0f;
            mainDuration = _aiBehaviour.WeaponBehaviour.CurrentSnapshot.Stats.Duration;
            enterDuration = EnterDuration;
            exitDuration = ExitDuration;
            lastTargetPosition = target.Position;
            hitBox.enabled = true;
            hitBox.Init(OnHit);
            _aiBehaviour.SetPhase(SummonPhase.AttackEnter);
            if (_aiBehaviour == null) return;
            if (!beamSound.IsNull)
            {
                attackSoundInstance = RuntimeManager.CreateInstance(beamSound);
                Vector3 position = _aiBehaviour.transform.position;
                position.z = 0f;
                attackSoundInstance.set3DAttributes(position.To3DAttributes());
                attackSoundInstance.start();
            }
            if (enterDuration <= 0f) BeginMain();
        }

        public override void OnUpdate()
        {
            if (_aiBehaviour == null || isComplete) return;
            if (searching) { SearchChunk(); return; }
            if (!started) return;
            SummonPhase phase = _aiBehaviour.Phase;
            if (phase == SummonPhase.AttackEnter)
            {
                elapsed += _aiBehaviour.DeltaTime;
                AimAtTarget();
                _aiBehaviour.SetPhase(phase, elapsed);
                if (elapsed >= enterDuration) BeginMain();
            }
            else if (phase == SummonPhase.AttackMain)
            {
                elapsed += _aiBehaviour.SmoothDeltaTime;
                if (target.IsBoss)
                    SetYAngle(Mathf.LerpAngle(rotationPivot.localEulerAngles.y, targetAngle, _aiBehaviour.SmoothDeltaTime * aimSmoothing));
                else if (target.IsAvailable)
                {
                    float fraction = mainDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / mainDuration);
                    float eased = sweepAccelerationCurve != null ? sweepAccelerationCurve.EasePercentage(fraction) : fraction;
                    SetYAngle(Mathf.LerpAngle(startAngle, startAngle + sweepAngle, eased));
                }
                _aiBehaviour.SetPhase(phase, elapsed);
                if (elapsed >= mainDuration || (!target.IsBoss && !target.IsAvailable)) BeginExit();
            }
            else if (phase == SummonPhase.AttackExit)
            {
                elapsed += _aiBehaviour.DeltaTime;
                _aiBehaviour.SetPhase(phase, elapsed);
                if (elapsed >= exitDuration) Exit();
            }
        }

        private void AimAtTarget()
        {
            if (!target.IsAvailable) return;
            float delta = _aiBehaviour.SmoothDeltaTime;
            Vector2 position = target.Position;
            if (delta > 0f)
                smoothedVelocity = Vector2.Lerp(smoothedVelocity, (position - lastTargetPosition) / delta, delta * velocitySmoothing);
            lastTargetPosition = position;
            Vector2 direction = position + smoothedVelocity * predictionLeadTime - (Vector2)_aiBehaviour.transform.position;
            float desired = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg + angleOffset;
            if (!target.IsBoss) desired -= sweepAngle / 2f;
            SetYAngle(Mathf.LerpAngle(rotationPivot.localEulerAngles.y, desired, delta * aimSmoothing));
        }

        private void BeginMain()
        {
            if (_aiBehaviour == null) return;
            elapsed = 0f;
            startAngle = rotationPivot.localEulerAngles.y;
            if (target.IsAvailable)
            {
                // The source boss path freezes its predicted heading once at the start of Main.
                Vector2 direction = target.Position + smoothedVelocity * predictionLeadTime - (Vector2)_aiBehaviour.transform.position;
                targetAngle = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg + angleOffset;
            }
            else targetAngle = startAngle;
            _aiBehaviour.SetPhase(SummonPhase.AttackMain);
            if (_aiBehaviour != null && (mainDuration <= 0f || !target.IsAvailable)) BeginExit();
        }

        private void BeginExit()
        {
            if (_aiBehaviour == null) return;
            elapsed = 0f;
            _aiBehaviour.SetPhase(SummonPhase.AttackExit);
            if (_aiBehaviour != null && exitDuration <= 0f) Exit();
        }

        private void SetYAngle(float angle)
        {
            if (SummonPose.Finite(angle))
                rotationPivot.localRotation = Quaternion.Euler(rotationPivot.localEulerAngles.x, angle, 0f);
        }

        public override void Exit()
        {
            if (_aiBehaviour == null || isComplete) return;
            searching = started = false;
            hitBox.ClearCallbacks();
            hitBox.Toggle(false);
            hitBox.enabled = false;
            StopSound();
            _aiBehaviour.WeaponBehaviour.SetLastAttackTime();
            base.Exit();
        }

        private void OnHit(IDamageable damageable)
        {
            if (_aiBehaviour == null || !started || _aiBehaviour.IsPresentation) return;
            SummonAttackBehaviour weapon = _aiBehaviour.WeaponBehaviour;
            if (weapon.CurrentSnapshot != null && !weapon.CurrentSnapshot.IsDisposed)
                weapon.OnNativeGasHit(hitBox.transform.position, damageable, weapon.CurrentSnapshot);
        }

        private void StopSound()
        {
            if (!attackSoundInstance.isValid()) return;
            attackSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            attackSoundInstance.release();
            attackSoundInstance = default;
        }

        public override void Dispose()
        {
            searching = started = false;
            targets.Clear();
            target = default;
            if (hitBox != null)
            {
                hitBox.ClearCallbacks();
                hitBox.Toggle(false);
                hitBox.enabled = false;
            }
            StopSound();
            rotationPivot = null;
            base.Dispose();
        }
    }
}
