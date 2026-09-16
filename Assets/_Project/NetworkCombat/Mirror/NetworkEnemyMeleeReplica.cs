using System;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>
    /// One local presentation and local-player hit-window executor per enemy, on every client role.
    /// The historical component name/GUID is retained for existing prefabs. It never owns movement or HP.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkEnemySimulationAgent))]
    [RequireComponent(typeof(EnemySimulationAuthority))]
    public sealed class NetworkEnemyMeleeReplica : MonoBehaviour
    {
        [SerializeField] private NetworkEnemySimulationAgent simulationAgent;
        [SerializeField] private EnemySimulationAuthority simulationAuthority;
        [SerializeField] private EnemyController controller;
        [SerializeField] private EnemyAttackMelee meleeAttack;
        private EnemyAttack attack;
        private EnemyActionState receivedAction, appliedAction;
        private EnemyAttackWindow window;
        private uint generation, lastAppliedSequence, lastAppliedAssignmentEpoch;
        private ulong cancelledAction;
        private bool hasAction;
        public EnemyAttackPrefab ReplicaAttackInstance => attack?.LocalAttackInstance;
        public bool HasReplicaAttackInstance => ReplicaAttackInstance != null;
        public bool DamageWindowActive => attack != null && attack.LocalDamageEnabled && hasAction;
        public uint LastAppliedSequence => lastAppliedSequence;
        public uint LastAppliedAssignmentEpoch => lastAppliedAssignmentEpoch;
        public EnemyAttackPresentationPhase LastAppliedPhase => appliedAction.Phase;
        internal EnemyActionState AppliedSequenceState => appliedAction;
        public EnemyActionState AppliedAction => appliedAction;
        public static event Action<NetworkEnemyMeleeReplica, string> TimelineObserved;

        private void Awake() => ResolveReferences();
        private void OnEnable()
        {
            ResolveReferences();
            if (simulationAgent != null)
            {
                simulationAgent.AttackPresentationChanged -= HandleAttackPresentationChanged;
                simulationAgent.AttackPresentationChanged += HandleAttackPresentationChanged;
            }
        }

        private void LateUpdate()
        {
            if (attack == null || !attack.SupportsSharedTimeline) return;
            if (!NetworkClient.active || simulationAgent == null || !simulationAgent.ProductEnemyInitialized ||
                !simulationAgent.IsCanonicalAlive || controller.DeathRequested || !controller.IsAlive ||
                BootGameplayNetworkManager.CombatHasEnded || simulationAuthority == null ||
                simulationAuthority.Role == EnemySimulationRole.Frozen)
            { ReleaseAction(); return; }

            var state = simulationAuthority.RunsCombatDecisions
                ? controller.CaptureSimulationAction(EnemySimulationClock.CombatNow) : receivedAction;
            if (state.ActionId == cancelledAction) state.Phase = EnemyAttackPresentationPhase.Cancelled;
            ApplyAction(state, simulationAgent.Assignment.Epoch, EnemySimulationClock.CombatNow);
        }

        private void HandleAttackPresentationChanged(EnemyAttackPresentationEdge edge)
        {
            if (attack == null || !attack.SupportsSharedTimeline || !NetworkClient.active || !simulationAgent.IsCanonicalAlive) return;
            lastAppliedSequence = edge.StateSequence;
            receivedAction = edge.Checkpoint.Movement.Runtime.Action;
            if (receivedAction.ActionId == cancelledAction) receivedAction.Phase = EnemyAttackPresentationPhase.Cancelled;
            if (simulationAuthority.ConsumesSnapshots)
                ApplyAction(receivedAction, edge.AssignmentEpoch, EnemySimulationClock.CombatNow);
        }

        // Accepted local simulation/checkpoint state only; there is no client damage RPC.
        internal void ApplyAction(EnemyActionState state, uint epoch, double now)
        {
            if (attack == null || !attack.SupportsSharedTimeline) return;
            var frame = EnemyActionTimeline.Resolve(state, now);
            bool sameAction = hasAction && frame.ActionId == appliedAction.ActionId;
            if (sameAction && EnemyActionTimeline.Order(frame) < EnemyActionTimeline.Order(appliedAction))
                frame = EnemyActionTimeline.Resolve(appliedAction, now);
            // An older pose update cannot erase a strike already locked on this peer.
            if (sameAction && frame.StrikeIndex == appliedAction.StrikeIndex && EnemyActionTimeline.HasPose(appliedAction) &&
                !EnemyActionTimeline.HasPose(frame))
            {
                frame.Facing = appliedAction.Facing; frame.TargetPosition = appliedAction.TargetPosition;
                frame.PoseStrikeIndex = appliedAction.PoseStrikeIndex; frame.LockedStrikeMask |= appliedAction.LockedStrikeMask;
            }
            bool epochChanged = hasAction && epoch != lastAppliedAssignmentEpoch;
            bool changed = !sameAction || frame.Phase != appliedAction.Phase || frame.StrikeIndex != appliedAction.StrikeIndex ||
                EnemyActionTimeline.HasPose(frame) != EnemyActionTimeline.HasPose(appliedAction);
            if (epochChanged) { attack.LocalDamageInteraction?.DiscardPendingCollisions(); generation++; }
            if (changed)
            {
                bool normalBoundary = sameAction && !epochChanged && frame.Phase != EnemyAttackPresentationPhase.Cancelled &&
                    controller.IsAlive && !controller.DeathRequested;
                if (normalBoundary) attack.LocalDamageInteraction?.SettlePendingCollisions();
                else attack.LocalDamageInteraction?.DiscardPendingCollisions();
                if (!sameAction || frame.StrikeIndex != appliedAction.StrikeIndex)
                { window.Cancel(); attack.ReleaseLocalFrame(); generation++; }
            }
            if (frame.ActionId == 0 || frame.Phase == EnemyAttackPresentationPhase.Cancelled ||
                frame.Phase == EnemyAttackPresentationPhase.Inactive)
            {
                if (frame.Phase == EnemyAttackPresentationPhase.Cancelled) cancelledAction = frame.ActionId;
                window.Cancel();
                if (frame.Phase == EnemyAttackPresentationPhase.Cancelled) attack.CancelLocalFrame();
                else attack.ReleaseLocalFrame();
                if (changed) controller.ApplyReplicatedAttackPresentation(frame.Phase, frame.Facing, 0);
                appliedAction = frame; hasAction = frame.ActionId != 0; lastAppliedAssignmentEpoch = epoch;
                if (changed || epochChanged) TimelineObserved?.Invoke(this, "phase");
                return;
            }
            lastAppliedAssignmentEpoch = epoch;
            window.Bind(frame, epoch, generation);
            attack.controller = controller; attack.enemyAnimator = controller.enemyAnimator;
            attack.ApplyLocalFrame(frame, now, changed);
            attack.LocalDamageInteraction?.ConfigureAttackWindow(window);
            controller.ApplyTimedLocalAttackPresentation(frame.Phase, frame.Facing, Math.Max(0, now - frame.StartAt(frame.Phase)));
            appliedAction = frame; hasAction = true;
            if (changed || epochChanged) TimelineObserved?.Invoke(this, epochChanged ? "handoff" : "phase");
        }

        private void ReleaseAction()
        {
            bool observed = hasAction;
            window?.Cancel();
            if (hasAction || attack?.LocalAttackInstance != null || attack?.LocalDamageEnabled == true)
                attack?.ReleaseLocalFrame();
            hasAction = false;
            appliedAction = default;
            if (observed) TimelineObserved?.Invoke(this, "release");
        }

        internal void CancelActionPresentation(ulong actionId)
        {
            if (actionId == 0 || simulationAgent.CurrentActionId != actionId) return;
            cancelledAction = actionId;
            var action = hasAction && appliedAction.ActionId == actionId ? appliedAction :
                simulationAuthority.RunsCombatDecisions ? controller.CaptureSimulationAction(EnemySimulationClock.CombatNow) : receivedAction;
            action.Phase = EnemyAttackPresentationPhase.Cancelled;
            receivedAction = action;
            ApplyAction(action, simulationAgent.Assignment.Epoch, EnemySimulationClock.CombatNow);
        }

        internal void CancelSequencePresentation(ulong actionId) => CancelActionPresentation(actionId);

        private void OnDisable()
        {
            if (simulationAgent != null) simulationAgent.AttackPresentationChanged -= HandleAttackPresentationChanged;
            ReleaseAction();
        }

        private void ResolveReferences()
        {
            if (simulationAgent == null) simulationAgent = GetComponent<NetworkEnemySimulationAgent>();
            if (simulationAuthority == null) simulationAuthority = GetComponent<EnemySimulationAuthority>();
            if (controller == null) controller = GetComponent<EnemyController>();
            if (meleeAttack == null) meleeAttack = GetComponent<EnemyAttackMelee>();
            attack = controller != null ? controller.attackScript : null;
            if (attack == null) attack = GetComponent<EnemyAttack>();
            if (window == null) window = new EnemyAttackWindow(() => EnemySimulationClock.CombatNow, () =>
                isActiveAndEnabled && controller != null && controller.IsAlive && !controller.DeathRequested &&
                simulationAgent != null && simulationAgent.IsCanonicalAlive &&
                simulationAgent.Assignment.Epoch == lastAppliedAssignmentEpoch && !BootGameplayNetworkManager.CombatHasEnded);
        }
    }
}
