using System;
using Animancer;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public class OvidSummonIdleModule : IdleStateModule
    {
        [SerializeField] private OvidSummonMover mover;
        [SerializeField] private float stopDistance = 2f;
        [SerializeField] private ClipTransition idleAnimation;
        [SerializeField] private ClipTransition birthAnimation;
        public bool IsCacoon { get; set; } = true;
        public OvidSummonMover Mover => mover;
        public float BirthDuration => SummonAIBehaviour.ClipDuration(birthAnimation);
        public ClipTransition CocoonAnimation => idleAnimation;
        public ClipTransition BirthAnimation => birthAnimation;

        public override void Init(SummonAIBehaviour behaviour, Action onComplete)
        {
            base.Init(behaviour, onComplete);
            mover.Init(behaviour);
        }

        public override void Enter() { isComplete = false; OnUpdate(); }

        public override void OnUpdate()
        {
            if (_aiBehaviour == null || isComplete) return;
            SummonAttackBehaviour weapon = _aiBehaviour.WeaponBehaviour;
            SummonPhase phase = weapon.GetMaturityPhase(out float elapsed);
            IsCacoon = phase == SummonPhase.Cocoon;
            if (phase == SummonPhase.Positioning) { Exit(); return; }
            _aiBehaviour.SetPhase(phase, elapsed);
            if (_aiBehaviour == null) return;
            if (!IsCacoon) { mover.StopCacoon(); return; }
            Transform owner = weapon.OwnerPlayer != null ? weapon.OwnerPlayer.transform : null;
            if (owner == null) return;
            Vector2 delta = owner.position - _aiBehaviour.Transform.position;
            mover.MoveCacoon(delta.normalized, delta.magnitude, weapon.SizeValue * stopDistance);
        }

        public override void Exit()
        {
            if (_aiBehaviour == null || isComplete) return;
            mover.StopCacoon();
            IsCacoon = false;
            base.Exit();
        }

        public void ExitInstant() => Exit();
        public override void Dispose() { mover.StopCacoon(); base.Dispose(); }
    }
}
