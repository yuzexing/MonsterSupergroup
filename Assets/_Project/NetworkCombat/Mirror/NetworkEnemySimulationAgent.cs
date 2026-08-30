using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Interactions;
using AstralShift.QTI.Triggers;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Local;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(EnemySimulationAuthority))]
    [RequireComponent(typeof(EnemySnapshotInterpolator))]
    public sealed class NetworkEnemySimulationAgent : NetworkBehaviour
    {
        [SerializeField] private EnemySimulationAuthority authority;
        [SerializeField] private EnemySnapshotInterpolator interpolator;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private LocalEnemyChase localChase;
        [SerializeField] private EnemyController enemyController;
        [SerializeField] private bool productMovementOnly = true;

        [SyncVar(hook = nameof(HandleAssignmentChanged))]
        private EnemySimulationAssignment assignment;

        private uint snapshotSequence;
        private uint sequenceEpoch;
        private uint attackStateSequence;
        private uint attackSequenceEpoch;
        private uint receivedAttackStateSequence;
        private uint receivedAttackSequenceEpoch;
        private Transform resolvedTarget;
        private bool productEnemyInitialized;
        private CombatantBehaviour combatant;
        private readonly Queue<EnemyAttackPresentationEdge>
            pendingAttackPresentationEdges =
                new Queue<EnemyAttackPresentationEdge>();
        private EnemyAttackPresentationEdge latestAttackPresentation;
        private bool hasLatestAttackPresentation;
        private PlayerDamageInteraction[] localDamageInteractions =
            System.Array.Empty<PlayerDamageInteraction>();

        public EnemySimulationAssignment Assignment => assignment;

        public EnemySimulationAuthority Authority => authority;

        public EnemySimulationMode SimulationMode => authority.SimulationMode;

        public bool ProductEnemyInitialized =>
            enemyController == null || productEnemyInitialized;

        public bool ProductMovementOnly => productMovementOnly;

        public bool HasLatestAttackPresentation => hasLatestAttackPresentation;

        public EnemyAttackPresentationEdge LatestAttackPresentation =>
            latestAttackPresentation;

        public event Action<EnemyAttackPresentationEdge>
            AttackPresentationChanged;

        public bool IsCanonicalAlive
        {
            get
            {
                return combatant == null || combatant.IsAlive;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            localDamageInteractions =
                GetComponentsInChildren<PlayerDamageInteraction>(true);
            if (combatant != null)
            {
                combatant.HealthChanged += HandleHealthChanged;
            }
            if (enemyController != null)
            {
                enemyController.OnAttackPresentationPhaseChanged +=
                    HandleAttackPresentationPhaseChanged;
            }
            authority.ConfigureNetworkManaged(
                enableCombatDecisions:
                    !productMovementOnly ||
                    authority.SimulationMode == EnemySimulationMode.BossServer);
            interpolator.Configure(authority, body);
            if (localChase != null)
            {
                localChase.enabled = false;
            }
            if (enemyController != null)
            {
                if (enemyController.attackScript != null)
                {
                    // EnemyAttack subclasses can own Unity Start/Update/OnEnable
                    // gameplay outside EnemyAIManager. Keep them dormant until an
                    // authoritative simulation role has actually been assigned.
                    enemyController.attackScript.enabled = false;
                }
            }
            if (productMovementOnly && enemyController != null)
            {
                for (int i = 0; i < localDamageInteractions.Length; i++)
                {
                    localDamageInteractions[i].gameObject.SetActive(false);
                }
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            NetworkEnemySimulationWorld world = NetworkEnemySimulationWorld.Instance;
            if (world == null)
            {
                Debug.LogError(
                    "NetworkEnemySimulationWorld must exist before Enemy spawn.",
                    this);
                return;
            }

            world.RegisterEnemy(this);
            TryInitializeProductEnemy();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyAssignment(assignment);
            TryInitializeProductEnemy();
            // A cached late-join attack edge may be applied by registration.
            // Resolve the replicated role and initialize the product Enemy first
            // so the edge cannot be consumed while this object is still Frozen.
            NetworkEnemySimulationWorld.Instance?.RegisterClientEnemy(this);
            NetworkEnemySimulationWorld.Instance?
                .TryApplyPendingAttackPresentation(this);
            RefreshLocalDamageInteractions();
        }

        public void ConfigureProductSimulation(bool movementOnly)
        {
            productMovementOnly = movementOnly;
        }

        public override void OnStopClient()
        {
            SetLocalDamageInteractionsActive(false);
            SetAttackScriptExecutionActive(false);
            pendingAttackPresentationEdges.Clear();
            NetworkEnemySimulationWorld.Instance?.UnregisterClientEnemy(this);
            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            NetworkEnemySimulationWorld.Instance?.UnregisterEnemy(this);
            base.OnStopServer();
        }

        [Server]
        public void SetServerAssignment(EnemySimulationAssignment newAssignment)
        {
            if (newAssignment.EnemyEntityId != netId)
            {
                throw new System.ArgumentException(
                    "Assignment does not belong to this Enemy.",
                    nameof(newAssignment));
            }

            EnemySimulationAssignment previous = assignment;
            assignment = newAssignment;
            if (!previous.Equals(newAssignment))
            {
                snapshotSequence = 0u;
                sequenceEpoch = newAssignment.Epoch;
                ResetAttackPresentationForAssignment(newAssignment.Epoch);
            }
            ApplyAssignment(newAssignment);
            if (!previous.Equals(newAssignment))
            {
                QueueAssignmentAttackPresentationBaseline();
            }
        }

        public bool TryCaptureSnapshot(
            double networkTime,
            out EnemySimulationSnapshot snapshot)
        {
            if (authority == null || !authority.RunsNavigation ||
                assignment.EnemyEntityId == 0u ||
                assignment.Host == EnemySimulationHost.Frozen)
            {
                snapshot = default;
                return false;
            }

            if (sequenceEpoch != assignment.Epoch)
            {
                sequenceEpoch = assignment.Epoch;
                snapshotSequence = 0u;
            }

            snapshotSequence = unchecked(snapshotSequence + 1u);
            if (snapshotSequence == 0u)
            {
                snapshotSequence = 1u;
            }

            Vector2 velocity = body != null ? body.linearVelocity : Vector2.zero;
            Vector2 facing = velocity.sqrMagnitude > 0.0001f
                ? velocity.normalized
                : Vector2.right;
            if (enemyController != null && enemyController.Movement != null &&
                enemyController.FacingDirection.sqrMagnitude > 0.0001f)
            {
                facing = enemyController.FacingDirection.normalized;
            }

            snapshot = new EnemySimulationSnapshot
            {
                EnemyEntityId = netId,
                AssignmentEpoch = assignment.Epoch,
                Sequence = snapshotSequence,
                SampleNetworkTime = networkTime,
                Position = body != null ? body.position : (Vector2)transform.position,
                Velocity = velocity,
                Facing = facing,
                Flags = authority.ConsumeDiscontinuity()
                    ? EnemySimulationSnapshotFlags.Discontinuity
                    : EnemySimulationSnapshotFlags.None
            };
            return true;
        }

        public bool TryDequeueAttackPresentation(
            out EnemyAttackPresentationEdge edge)
        {
            if (pendingAttackPresentationEdges.Count == 0)
            {
                edge = default;
                return false;
            }

            edge = pendingAttackPresentationEdges.Dequeue();
            return true;
        }

        public void ReceiveRemoteSnapshot(EnemySimulationSnapshot snapshot)
        {
            if (!IsCanonicalAlive ||
                snapshot.EnemyEntityId != netId ||
                snapshot.AssignmentEpoch != assignment.Epoch)
            {
                return;
            }

            interpolator.Push(snapshot);
        }

        public bool ReceiveRemoteAttackPresentation(
            EnemyAttackPresentationEdge edge)
        {
            if (!IsCanonicalAlive || edge.EnemyEntityId != netId ||
                edge.AssignmentEpoch != assignment.Epoch ||
                !edge.IsFinite || !edge.HasKnownPhase)
            {
                return false;
            }

            if (receivedAttackSequenceEpoch != edge.AssignmentEpoch)
            {
                receivedAttackSequenceEpoch = edge.AssignmentEpoch;
                receivedAttackStateSequence = 0u;
            }
            if (!EnemySimulationSequence.IsNewer(
                edge.StateSequence,
                receivedAttackStateSequence))
            {
                return false;
            }

            receivedAttackStateSequence = edge.StateSequence;
            latestAttackPresentation = edge;
            hasLatestAttackPresentation = true;
            ApplyReplicaAttackPresentation(edge);
            AttackPresentationChanged?.Invoke(edge);
            return true;
        }

        [Server]
        public void SnapServerSimulationTo(EnemySimulationSnapshot snapshot)
        {
            if (snapshot.EnemyEntityId != netId)
            {
                return;
            }

            if (body != null)
            {
                body.position = snapshot.Position;
                body.linearVelocity = Vector2.zero;
            }
            else
            {
                transform.position = snapshot.Position;
            }
        }

        private void Update()
        {
            if (assignment.AggroTargetPlayerId != 0u && resolvedTarget == null)
            {
                ApplyAssignment(assignment);
            }
            TryInitializeProductEnemy();
        }

        private void HandleAssignmentChanged(
            EnemySimulationAssignment previous,
            EnemySimulationAssignment current)
        {
            snapshotSequence = 0u;
            sequenceEpoch = current.Epoch;
            ResetAttackPresentationForAssignment(current.Epoch);
            ApplyAssignment(current);
            if (NetworkClient.active)
            {
                NetworkEnemySimulationWorld.Instance?
                    .TryApplyPendingAttackPresentation(this);
            }
            QueueAssignmentAttackPresentationBaseline();
        }

        private void ApplyAssignment(EnemySimulationAssignment current)
        {
            if (authority == null)
            {
                return;
            }

            bool previouslyRanCombat = authority.RunsCombatDecisions;
            resolvedTarget = ResolvePlayerTarget(current.AggroTargetPlayerId);
            EnemySimulationRole role = ResolveRole(current, resolvedTarget != null);
            authority.ApplyRole(
                role,
                current.SimulationOwnerPlayerId,
                current.AggroTargetPlayerId,
                current.Epoch);

            if (enemyController != null)
            {
                enemyController.Target = resolvedTarget;
                if (enemyController.attackScript != null)
                {
                    enemyController.attackScript.Target = resolvedTarget;
                }
            }

            RefreshAttackScriptExecution();

            if (localChase != null)
            {
                bool runChase = authority.RunsNavigation && resolvedTarget != null;
                localChase.enabled = runChase;
                if (runChase)
                {
                    localChase.Initialize(resolvedTarget);
                }
            }

            if (!previouslyRanCombat && authority.RunsCombatDecisions &&
                productEnemyInitialized && !productMovementOnly)
            {
                QueueCurrentAttackPresentation();
            }
        }

        private EnemySimulationRole ResolveRole(
            EnemySimulationAssignment current,
            bool hasTarget)
        {
            if (current.Host == EnemySimulationHost.Frozen || !hasTarget)
            {
                return EnemySimulationRole.Frozen;
            }

            switch (current.Host)
            {
            case EnemySimulationHost.ClientPlayer:
                return IsLocallyOwnedPlayer(current.SimulationOwnerPlayerId)
                    ? EnemySimulationRole.ClientOwner
                    : EnemySimulationRole.Replica;
            case EnemySimulationHost.ServerFallback:
                return isServer
                    ? EnemySimulationRole.ServerFallback
                    : EnemySimulationRole.Replica;
            case EnemySimulationHost.ServerAuthoritative:
                return isServer
                    ? EnemySimulationRole.ServerAuthoritative
                    : EnemySimulationRole.Replica;
            default:
                return EnemySimulationRole.Frozen;
            }
        }

        private static bool IsLocallyOwnedPlayer(uint playerEntityId)
        {
            return playerEntityId != 0u &&
                NetworkClient.active &&
                NetworkClient.spawned.TryGetValue(
                    playerEntityId,
                    out NetworkIdentity identity) &&
                identity != null && identity.isOwned;
        }

        private Transform ResolvePlayerTarget(uint playerEntityId)
        {
            if (playerEntityId == 0u)
            {
                return null;
            }

            NetworkIdentity identity = null;
            if (isServer)
            {
                NetworkServer.spawned.TryGetValue(playerEntityId, out identity);
            }
            if (identity == null && NetworkClient.active)
            {
                NetworkClient.spawned.TryGetValue(playerEntityId, out identity);
            }
            if (identity == null)
            {
                return null;
            }

            PlayerMovement movement = identity.GetComponent<PlayerMovement>();
            return movement != null && movement.EnemyAttackTarget != null
                ? movement.EnemyAttackTarget
                : identity.transform;
        }

        private void ResolveReferences()
        {
            if (authority == null)
            {
                authority = GetComponent<EnemySimulationAuthority>();
            }
            if (interpolator == null)
            {
                interpolator = GetComponent<EnemySnapshotInterpolator>();
            }
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }
            if (localChase == null)
            {
                localChase = GetComponent<LocalEnemyChase>();
            }
            if (enemyController == null)
            {
                enemyController = GetComponent<EnemyController>();
            }
            if (combatant == null)
            {
                combatant = GetComponent<CombatantBehaviour>();
            }
        }

        private void HandleHealthChanged(int currentHealth, int maximumHealth)
        {
            if (currentHealth > 0)
            {
                ApplyAssignment(assignment);
                if (NetworkClient.active)
                {
                    NetworkEnemySimulationWorld.Instance?
                        .TryApplyPendingAttackPresentation(this);
                }
                RefreshLocalDamageInteractions();
                return;
            }

            SetLocalDamageInteractionsActive(false);
            SetAttackScriptExecutionActive(false);
            pendingAttackPresentationEdges.Clear();
            hasLatestAttackPresentation = false;
            interpolator.ClearSnapshots();
            if (localChase != null)
            {
                localChase.enabled = false;
            }
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
        }

        private void OnDestroy()
        {
            if (combatant != null)
            {
                combatant.HealthChanged -= HandleHealthChanged;
            }
            if (enemyController != null)
            {
                enemyController.OnAttackPresentationPhaseChanged -=
                    HandleAttackPresentationPhaseChanged;
            }
            AttackPresentationChanged = null;
        }

        private void HandleAttackPresentationPhaseChanged(
            EnemyAttackPresentationPhase phase,
            Vector2 facing)
        {
            if (productMovementOnly || authority == null ||
                !authority.RunsCombatDecisions || !IsCanonicalAlive ||
                assignment.EnemyEntityId == 0u ||
                assignment.Host == EnemySimulationHost.Frozen)
            {
                return;
            }

            QueueAttackPresentation(
                phase,
                facing,
                NetworkTime.time,
                enemyController.GetAttackPresentationPhaseDuration(phase));
        }

        private void QueueCurrentAttackPresentation()
        {
            if (enemyController == null)
            {
                return;
            }

            Vector2 facing = enemyController.Movement != null
                ? enemyController.FacingDirection
                : Vector2.right;
            QueueAttackPresentation(
                enemyController.CurrentAttackPresentationPhase,
                facing,
                NetworkTime.time,
                enemyController.GetAttackPresentationPhaseDuration(
                    enemyController.CurrentAttackPresentationPhase));
        }

        private void QueueAssignmentAttackPresentationBaseline()
        {
            if (!productMovementOnly && productEnemyInitialized &&
                authority != null && authority.RunsCombatDecisions &&
                IsCanonicalAlive && pendingAttackPresentationEdges.Count == 0)
            {
                QueueCurrentAttackPresentation();
            }
        }

        private void QueueAttackPresentation(
            EnemyAttackPresentationPhase phase,
            Vector2 facing,
            double stateStartNetworkTime,
            float phaseDuration)
        {
            if (attackSequenceEpoch != assignment.Epoch)
            {
                attackSequenceEpoch = assignment.Epoch;
                attackStateSequence = 0u;
            }

            attackStateSequence = NextSequence(attackStateSequence);
            if (facing.sqrMagnitude > 0.0001f)
            {
                facing.Normalize();
            }
            pendingAttackPresentationEdges.Enqueue(
                new EnemyAttackPresentationEdge
                {
                    EnemyEntityId = netId,
                    AssignmentEpoch = assignment.Epoch,
                    StateSequence = attackStateSequence,
                    StateStartNetworkTime = stateStartNetworkTime,
                    PhaseDuration = Mathf.Max(0f, phaseDuration),
                    Phase = phase,
                    Facing = facing
                });
        }

        private void ResetAttackPresentationForAssignment(uint epoch)
        {
            pendingAttackPresentationEdges.Clear();
            attackSequenceEpoch = epoch;
            attackStateSequence = 0u;
            receivedAttackSequenceEpoch = epoch;
            receivedAttackStateSequence = 0u;
            latestAttackPresentation = default;
            hasLatestAttackPresentation = false;
        }

        private static uint NextSequence(uint value)
        {
            value = unchecked(value + 1u);
            return value == 0u ? 1u : value;
        }

        private void ConfigureLocalDamageInteractions()
        {
            if (enemyController == null)
            {
                return;
            }

            for (int i = 0; i < localDamageInteractions.Length; i++)
            {
                PlayerDamageInteraction interaction = localDamageInteractions[i];
                if (interaction == null)
                {
                    continue;
                }

                interaction.enemyStats = enemyController.stats;
                InteractionTrigger[] triggers =
                    interaction.GetComponents<InteractionTrigger>();
                for (int triggerIndex = 0;
                     triggerIndex < triggers.Length;
                     triggerIndex++)
                {
                    if (triggers[triggerIndex].interaction == null)
                    {
                        triggers[triggerIndex].interaction = interaction;
                    }
                }
            }
        }

        private void RefreshLocalDamageInteractions()
        {
            bool continuousContactDamage = productMovementOnly &&
                productEnemyInitialized && enemyController != null &&
                enemyController.alwaysAttacking &&
                !enemyController.hasAttackAnimation;
            SetLocalDamageInteractionsActive(
                NetworkClient.active && IsCanonicalAlive && continuousContactDamage);
        }

        private void SetLocalDamageInteractionsActive(bool active)
        {
            for (int i = 0; i < localDamageInteractions.Length; i++)
            {
                if (localDamageInteractions[i] != null)
                {
                    localDamageInteractions[i].gameObject.SetActive(active);
                }
            }
        }

        private void RefreshAttackScriptExecution()
        {
            bool shouldExecute = !productMovementOnly &&
                productEnemyInitialized && IsCanonicalAlive &&
                authority != null && authority.RunsCombatDecisions;
            SetAttackScriptExecutionActive(shouldExecute);
        }

        private void SetAttackScriptExecutionActive(bool active)
        {
            if (enemyController != null && enemyController.attackScript != null)
            {
                enemyController.attackScript.enabled = active;
            }
        }

        private void ApplyReplicaAttackPresentation(
            EnemyAttackPresentationEdge edge)
        {
            if (productMovementOnly || !productEnemyInitialized ||
                enemyController == null || authority == null ||
                !authority.ConsumesSnapshots)
            {
                return;
            }

            enemyController.ApplyReplicatedAttackPresentation(
                edge.Phase,
                edge.Facing,
                edge.ElapsedAt(NetworkTime.time));
        }

        private void TryInitializeProductEnemy()
        {
            if (productEnemyInitialized || enemyController == null)
            {
                return;
            }
            if (netId == 0u || resolvedTarget == null || EnemyAIManager.Instance == null)
            {
                return;
            }

            enemyController.Target = resolvedTarget;
            if (productMovementOnly)
            {
                enemyController.InitNetworkMovementOnly(unchecked((int)netId));
            }
            else
            {
                enemyController.Init(unchecked((int)netId));
            }
            ConfigureLocalDamageInteractions();
            productEnemyInitialized = true;
            RefreshAttackScriptExecution();
            if (hasLatestAttackPresentation)
            {
                ApplyReplicaAttackPresentation(latestAttackPresentation);
            }
            RefreshLocalDamageInteractions();
        }
    }
}
