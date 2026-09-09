using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public class OvidSummonPositioningModule : PositioningStateModule
    {
        [SerializeField] private OvidSummonMover mover;
        [SerializeField] private float stopDistance = 4f;
        [Tooltip("The distance Ovid wants to be from the target before firing.")]
        [SerializeField] private float optimalAttackDistance = 6f;
        [SerializeField] private float minDetectionRadius = 5f;
        [SerializeField] private float maxDetectionRadius = 15f;
        private readonly List<SummonTarget> targets = new List<SummonTarget>();

        public override void Init(SummonAIBehaviour behaviour, Action onComplete)
        {
            base.Init(behaviour, onComplete);
            mover.Init(behaviour);
        }

        public override void Enter()
        {
            isComplete = false;
            _aiBehaviour.SetPhase(SummonPhase.Positioning);
        }

        public override void Exit()
        {
            if (_aiBehaviour == null || isComplete) return;
            mover.Stop(true);
            base.Exit();
        }

        public override void OnUpdate()
        {
            if (_aiBehaviour == null || isComplete) return;
            SummonAttackBehaviour weapon = _aiBehaviour.WeaponBehaviour;
            if (weapon.OwnerPlayer == null) return;
            Vector2 position = _aiBehaviour.Transform.position;
            weapon.QueryTargets(position, weapon.SizeValue * minDetectionRadius, weapon.SizeValue * maxDetectionRadius, targets);
            SummonTarget closest = default;
            float closestSquared = float.PositiveInfinity;
            foreach (SummonTarget target in targets)
            {
                if (!target.IsAvailable) continue;
                float squared = (target.Position - position).sqrMagnitude;
                if (squared < closestSquared) { closestSquared = squared; closest = target; }
            }
            if (closest.IsAvailable && weapon.IsAttackReady)
            {
                float desired = weapon.SizeValue * optimalAttackDistance;
                float distance = Mathf.Sqrt(closestSquared);
                if (distance <= desired + 0.5f) { Exit(); return; }
                mover.Move((closest.Position - position).normalized, distance, desired);
            }
            else
            {
                Vector2 delta = (Vector2)weapon.OwnerPlayer.transform.position - position;
                float desired = weapon.SizeValue * stopDistance;
                if (delta.magnitude > desired) mover.Move(delta.normalized, delta.magnitude, desired);
                else mover.Stop();
            }
            mover.UpdateAnimation();
        }

        public override void Dispose() { targets.Clear(); mover.Stop(true); base.Dispose(); }
    }
}