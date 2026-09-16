using System;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Interactions;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
    public partial class EnemyAttackMelee
    {
        private ulong localActionId;
        private int localStrikeIndex = -1;
        public override bool SupportsSharedTimeline => GetType() == typeof(EnemyAttackMelee);
        public override EnemyAttackPrefab LocalAttackInstance => _attack;
        public override PlayerDamageInteraction LocalDamageInteraction => _attack != null ? _attack.damageInteraction : null;
        protected virtual Vector3 TimelineLocalOffset(int strike) => Vector3.zero;

        public override void ApplyLocalFrame(EnemyActionState state, double now, bool changed)
        {
            bool warning = state.Phase == EnemyAttackPresentationPhase.Warning;
            bool active = state.Phase == EnemyAttackPresentationPhase.Active && now >= state.WarningUntil && now < state.ActiveUntil;
            if (state.ActionId != localActionId || state.StrikeIndex != localStrikeIndex)
            {
                ReleaseLocalFrame(); localActionId = state.ActionId; localStrikeIndex = state.StrikeIndex;
            }
            if (!EnemyActionTimeline.HasPose(state) || state.Phase == EnemyAttackPresentationPhase.Cancelled ||
                state.Phase == EnemyAttackPresentationPhase.Inactive)
            { ReleaseLocalFrame(); return; }
            if (_attack == null && (warning || active))
            {
                _attackPooler = PoolManager.Instance.GetOrCreatePooler(attackPrefab);
                _attack = _attackPooler.GetOrCreate(transform, true);
                simulationGeneration++;
                _attack.SetStats(controller.stats);
                MonsterSupergroup.Gameplay.Combat.GameplayMapPresentation.Warning(_attack.gameObject);
                _warning = _attack.attackWarning;
                _collidersGameObject = _attack.damageInteraction != null ? _attack.damageInteraction.gameObject : null;
                _hitBox = _attack.hitBox;
                // The network path uses the existing local-player interaction, never a second HitBox damage route.
                _hitBox?.Toggle(false);
                if (_collidersGameObject != null) _collidersGameObject.SetActive(false);
            }
            if (_attack == null) return;
            _attack.SetStats(controller.stats);
            _attack.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(state.Facing.y, state.Facing.x) * Mathf.Rad2Deg);
            if (state.Dash) _attack.transform.position = state.DashWarningOrigin;
            else _attack.transform.localPosition = TimelineLocalOffset(state.StrikeIndex);
            if (_collidersGameObject != null && _collidersGameObject.activeSelf != active) _collidersGameObject.SetActive(active);
            _hitBox?.Toggle(false);
            _warning?.SampleTimeline((float)(state.WarningUntil - state.WarningStartedAt),
                (float)(state.ActiveUntil - state.WarningUntil), (float)(now - state.WarningStartedAt), warning, changed);
            if (!warning && !active && (_warning == null || now >= state.WarningUntil + _warning.TimelineHideDuration)) ReleaseLocalFrame();
        }

        public override void ReleaseLocalFrame()
        {
            // Deliberately bypass the Dash override: presentation does not own physical motion.
            simulationGeneration++;
            if (_attack == null) return;
            _attack.damageInteraction?.DiscardPendingCollisions();
            _attack.damageInteraction?.ConfigureAttackWindow(null);
            if (_collidersGameObject != null) _collidersGameObject.SetActive(false);
            _hitBox?.Toggle(false);
            var released = _attack;
            _attack = null; _warning = null; _collidersGameObject = null; _hitBox = null;
            released.gameObject.SetActive(false);
            if (!gameObject.activeInHierarchy) EnemyAttackPrefab.ReturnAfterHierarchyChange(released, _attackPooler);
            else _attackPooler?.Return(released);
        }
    }
}
