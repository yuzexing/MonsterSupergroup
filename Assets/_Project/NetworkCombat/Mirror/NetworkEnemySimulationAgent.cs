using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
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
    public sealed partial class NetworkEnemySimulationAgent : NetworkBehaviour
    {
        [SerializeField] private EnemySimulationAuthority authority;
        [SerializeField] private EnemySnapshotInterpolator interpolator;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private LocalEnemyChase localChase;
        [SerializeField] private EnemyController enemyController;
        [SerializeField] private bool productMovementOnly = true;

        [SyncVar(hook = nameof(HandleHandoffChanged))]
        private EnemySimulationHandoff handoff;
        private EnemySimulationAssignment assignment;

        [SyncVar]
        private int runtimeMinimumHealthOverride;

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
        private EnemyContactDamage contactDamage;
        private uint initialServerTargetPlayerId;
        private bool networkStartCallbacksReady;

        public EnemySimulationAssignment Assignment => assignment;
        public Vector2 ServerSpawnPosition { get; private set; }

        public EnemySimulationAuthority Authority => authority;

        public EnemySimulationMode SimulationMode => authority.SimulationMode;

        public bool ProductEnemyInitialized =>
            enemyController == null || productEnemyInitialized;

        /// <summary>
        /// Controls how much of the product Enemy simulation runs on the
        /// SimulationOwner. It never grants or removes local hit detection.
        /// </summary>
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
                var world = NetworkCombatWorld.Instance;
                if (world != null && netId != 0)
                {
                    if (NetworkServer.active && world.Gateway.Ledger.TryGetState(netId, out var server))
                        return server.Alive;
                    if (world.Replica.TryGetEntity(netId, out var replica)) return replica.Alive;
                }
                return IsLocallyAlive;
            }
        }

        internal bool IsLocallyAlive => combatant == null || combatant.IsAlive;

        public uint InitialServerTargetPlayerId => initialServerTargetPlayerId;

        private void Awake()
        {
            ResolveReferences();
            contactDamage = GetComponent<EnemyContactDamage>();
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
            // Local contact geometry must not become active until its product
            // Stats and replicated simulation role are initialized. This is
            // independent of whether the owner runs movement-only or full AI.
            SetContinuousContactDamageInteractionsActive(false);
        }

        public override void OnStartServer()
        {
            if (MonsterSupergroup.GAS.CombatEvidence.Enabled) MonsterSupergroup.GAS.CombatEvidence.Event("Server", "entity.spawn", "Observed", null, target: netId, input: new { name, assignment, position = transform.position, localAlive = IsLocallyAlive, canonicalAlive = IsCanonicalAlive });
            base.OnStartServer();
            networkStartCallbacksReady = true;
            PrepareBirthForRegistration();
            ServerSpawnPosition = transform.position;
            NetworkEnemySimulationWorld world = NetworkEnemySimulationWorld.Instance;
            if (world == null)
            {
                Debug.LogError(
                    "NetworkEnemySimulationWorld must exist before Enemy spawn.",
                    this);
                return;
            }

            world.RegisterEnemy(this, initialServerTargetPlayerId);
            TryInitializeProductEnemy();
        }

        public override void OnStartClient()
        {
            if (MonsterSupergroup.GAS.CombatEvidence.Enabled) MonsterSupergroup.GAS.CombatEvidence.Event("Replica", "entity.spawn", "Observed", null, target: netId, input: new { name, assignment, position = transform.position, localAlive = IsLocallyAlive, canonicalAlive = IsCanonicalAlive });
            base.OnStartClient();
            networkStartCallbacksReady = true;
            PrepareBirthForRegistration();
            ApplyHandoff(handoff);
            TryInitializeProductEnemy();
            // A cached late-join attack edge may be applied by registration.
            // Resolve the replicated role and initialize the product Enemy first
            // so the edge cannot be consumed while this object is still Frozen.
            NetworkEnemySimulationWorld.Instance?.RegisterClientEnemy(this);
            NetworkEnemySimulationWorld.Instance?
                .TryApplyPendingAttackPresentation(this);
            RefreshContinuousContactDamageInteractions();
        }

        public void ConfigureProductSimulation(bool movementOnly)
        {
            productMovementOnly = movementOnly;
        }

        [Server]
        public void ConfigureInitialServerTarget(uint playerEntityId)
        {
            if (netId != 0u)
            {
                throw new InvalidOperationException(
                    "Initial Enemy target must be configured before NetworkServer.Spawn.");
            }
            if (playerEntityId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(playerEntityId));
            }

            initialServerTargetPlayerId = playerEntityId;
        }

        [Server]
        public void ConfigureRuntimeMinimumHealthOverride(int minimumHealth)
        {
            if (netId != 0u)
            {
                throw new InvalidOperationException(
                    "Runtime Enemy health override must be configured before " +
                    "NetworkServer.Spawn.");
            }

            runtimeMinimumHealthOverride = Mathf.Max(0, minimumHealth);
        }

        public override void OnStopClient()
        {
            if (MonsterSupergroup.GAS.CombatEvidence.Enabled) MonsterSupergroup.GAS.CombatEvidence.Event("Replica", "entity.destroy", "Observed", null, target: netId, input: new { name, assignment, position = transform.position, localAlive = IsLocallyAlive, canonicalAlive = IsCanonicalAlive });
            ReleaseDecoyTargetAnchor();
            NetworkCombatWorld.Instance?.ForgetEnemyHitPresentation(netId);
            hasPendingFutureSnapshot = false;
            pendingFutureSnapshot = default;
            CancelNetworkKnockbackState(true);
            SetContinuousContactDamageInteractionsActive(false);
            SetAttackScriptExecutionActive(false);
            pendingAttackPresentationEdges.Clear();
            NetworkEnemySimulationWorld.Instance?.UnregisterClientEnemy(this);
            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            if (MonsterSupergroup.GAS.CombatEvidence.Enabled) MonsterSupergroup.GAS.CombatEvidence.Event("Server", "entity.destroy", "Observed", null, target: netId, input: new { name, assignment, position = transform.position, localAlive = IsLocallyAlive, canonicalAlive = IsCanonicalAlive });
            CancelNetworkKnockbackState(true);
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

            var world = NetworkEnemySimulationWorld.Instance;
            EnemySimulationSnapshot seed = default;
            world?.Registry.TryGetLatestSnapshot(netId, out seed);
            if (world != null && world.Registry.TryGetTargetState(netId, out var target)) SetServerTarget(target);
            seed.EnemyEntityId = netId;
            SetServerHandoff(new EnemySimulationHandoff { Assignment = newAssignment,
                Checkpoint = new EnemySimulationCheckpoint { Movement = seed }, CommittedAt = EnemySimulationClock.Now });
        }

        public bool TryCaptureSnapshot(
            double networkTime,
            out EnemySimulationSnapshot snapshot)
        {
            if (!IsLocallyAlive || authority == null || !authority.RunsNavigation ||
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
                Runtime = CaptureSimulationRuntime(networkTime),
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
            if (!IsLocallyAlive) { TraceReceivedMovement(snapshot, "Ignored", "LocallyDead"); return; }
            if (IsCanonicalAlive && snapshot.EnemyEntityId == netId && snapshot.IsFinite &&
                (EnemySimulationSequence.IsNewer(snapshot.AssignmentEpoch, assignment.Epoch) ||
                 snapshot.AssignmentEpoch == assignment.Epoch && appliedHandoffEpoch != assignment.Epoch))
            {
                if (!hasPendingFutureSnapshot || EnemySimulationSequence.IsNewer(snapshot.AssignmentEpoch, pendingFutureSnapshot.AssignmentEpoch) ||
                    (snapshot.AssignmentEpoch == pendingFutureSnapshot.AssignmentEpoch && EnemySimulationSequence.IsNewer(snapshot.Sequence, pendingFutureSnapshot.Sequence)))
                { pendingFutureSnapshot = snapshot; hasPendingFutureSnapshot = true; }
                TraceReceivedMovement(snapshot, "Deferred", "HandoffPending");
                return;
            }
            if (!IsCanonicalAlive ||
                snapshot.EnemyEntityId != netId ||
                snapshot.AssignmentEpoch != assignment.Epoch)
            {
                TraceReceivedMovement(snapshot, "Ignored", !IsCanonicalAlive ? "CanonicalDead" : snapshot.EnemyEntityId != netId ? "WrongEntity" : "WrongEpoch");
                return;
            }

            bool accepted = interpolator.Push(snapshot);
            if (accepted) AcceptedRemoteSnapshotCount++;
            TraceReceivedMovement(snapshot, accepted ? "Accepted" : "Ignored", accepted ? "None" : "InterpolatorRejected");
        }

        private void TraceReceivedMovement(EnemySimulationSnapshot snapshot, string outcome, string reason)
        {
            if (MonsterSupergroup.GAS.CombatEvidence.Enabled) MonsterSupergroup.GAS.CombatEvidence.Write(new MonsterSupergroup.GAS.DiagnosticRecord {
                role = authority.Role.ToString(), stage = "movement.receive", outcome = outcome, reason = reason, target = netId,
                assignmentEpoch = assignment.Epoch, input = snapshot, after = new { position = transform.position, assignment, localAlive = IsLocallyAlive, canonicalAlive = IsCanonicalAlive } });
        }
        public bool ReceiveRemoteAttackPresentation(
            EnemyAttackPresentationEdge edge)
        {
            if (!IsLocallyAlive || !IsCanonicalAlive || edge.EnemyEntityId != netId ||
                edge.AssignmentEpoch != assignment.Epoch ||
                !edge.IsFinite || !edge.HasKnownPhase || !edge.Checkpoint.Movement.Runtime.IsFinite || !ValidateSequenceAction(edge.Checkpoint.Movement.Runtime.Action))
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
            if (enemyController?.attackScript?.SupportsSharedTimeline == true && LimboAttackTimelineObservation.SuppressPhaseEdge(edge)) return true;
            RememberSequenceCancellation(edge.Checkpoint.Movement.Runtime.Action);
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

            // Handoff changes the replica's kinematic body back to dynamic immediately.
            // Keep its Transform in step too: a body-type change can otherwise restore the
            // previous Transform pose before the next physics step (the spawn pose on a server-only peer).
            interpolator.ResetRenderPose(snapshot.Position);
            if (body != null)
            {
                body.position = snapshot.Position;
                body.linearVelocity = Vector2.zero;
            }
        }

        private bool runEndStopped;
        public void StopForRunEnd()
        {
            if (runEndStopped) return;
            runEndStopped = true;
            ApplyAssignment(assignment);
            pendingAttackPresentationEdges.Clear();
            CancelNetworkKnockbackState();
            if (body != null) { body.linearVelocity = Vector2.zero; body.angularVelocity = 0; }
        }
        private void Update()
        {
            if (BootGameplayNetworkManager.CombatHasEnded) { StopForRunEnd(); return; }
            if (appliedHandoffEpoch != assignment.Epoch) ApplyHandoff(handoff);
            if (appliedHandoffEpoch == assignment.Epoch && AssignmentNeedsLocalRefresh())
            {
                ApplyAssignment(assignment);
            }
            TryInitializeProductEnemy();
            UpdateNetworkKnockbackState();
            RefreshReferenceReplicaMovement();
            TickExplosionLifecycle();
        }

        private bool AssignmentNeedsLocalRefresh()
        {
            if (assignment.AggroTargetPlayerId == 0u)
            {
                return false;
            }
            if (resolvedTarget == null)
            {
                return true;
            }
            if (assignment.Host != EnemySimulationHost.ClientPlayer)
            {
                return false;
            }

            bool locallyOwned =
                IsLocallyOwnedPlayer(assignment.SimulationOwnerPlayerId);
            return locallyOwned
                ? authority.Role != EnemySimulationRole.ClientOwner
                : authority.Role == EnemySimulationRole.ClientOwner;
        }

        private void ApplyAssignment(EnemySimulationAssignment current)
        {
            if (authority == null)
            {
                return;
            }

            bool previouslyRanCombat = authority.RunsCombatDecisions;
            enemyController?.ConfigureSimulationClock(() => EnemySimulationClock.CombatNow, current.Epoch);
            resolvedTarget = ResolveSimulationTarget();
            EnemySimulationRole role = ResolveRole(current, resolvedTarget != null);
            authority.ApplyRole(
                role,
                current.SimulationOwnerPlayerId,
                current.AggroTargetPlayerId,
                current.Epoch);

            if (activeKnockbackEpoch != 0 &&
                (activeKnockbackEpoch != current.Epoch || !authority.RunsNavigation))
                CancelNetworkKnockbackState();

            BindSimulationTarget();

            RefreshAttackScriptExecution();

            if (localChase != null)
            {
                bool runChase = IsLocallyAlive && authority.RunsNavigation && resolvedTarget != null;
                localChase.enabled = runChase;
                if (runChase)
                {
                    localChase.Initialize(resolvedTarget);
                }
            }

            if (!restoringHandoff && !previouslyRanCombat && authority.RunsCombatDecisions &&
                productEnemyInitialized && !productMovementOnly)
            {
                QueueCurrentAttackPresentation();
            }

            RefreshContinuousContactDamageInteractions();
        }

        private EnemySimulationRole ResolveRole(
            EnemySimulationAssignment current,
            bool hasTarget)
        {
            if (!IsLocallyAlive || BootGameplayNetworkManager.CombatHasEnded || current.Host == EnemySimulationHost.Frozen || !hasTarget)
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
                if (restoringHandoff || appliedHandoffEpoch != assignment.Epoch) return;
                ApplyAssignment(assignment);
                if (NetworkClient.active)
                {
                    NetworkEnemySimulationWorld.Instance?
                        .TryApplyPendingAttackPresentation(this);
                }
                RefreshContinuousContactDamageInteractions();
                return;
            }

            CancelNetworkKnockbackState();
            SetContinuousContactDamageInteractionsActive(false);
            SetAttackScriptExecutionActive(false);
            pendingAttackPresentationEdges.Clear();
            hasLatestAttackPresentation = false;
            hasPendingFutureSnapshot = false;
            pendingFutureSnapshot = default;
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
            ReleaseDecoyTargetAnchor();
            NetworkCombatWorld.Instance?.ForgetEnemyHitPresentation(netId);
            CancelNetworkKnockbackState(true);
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
            if (restoringHandoff || productMovementOnly || authority == null ||
                !authority.RunsCombatDecisions || !IsLocallyAlive || !IsCanonicalAlive ||
                assignment.EnemyEntityId == 0u ||
                assignment.Host == EnemySimulationHost.Frozen)
            {
                return;
            }

            var action = enemyController.CaptureSimulationAction(EnemySimulationClock.CombatNow);
            bool timed = phase == EnemyAttackPresentationPhase.Warning || phase == EnemyAttackPresentationPhase.Active ||
                phase == EnemyAttackPresentationPhase.Recovery;
            QueueAttackPresentation(phase, facing, timed ? action.StartAt(phase) : EnemySimulationClock.CombatNow,
                timed ? (float)Math.Max(0, action.EndAt(phase) - action.StartAt(phase)) : 0);
        }

        private void QueueCurrentAttackPresentation()
        {
            if (enemyController == null)
            {
                return;
            }

            var state = enemyController.CaptureSimulationAction(EnemySimulationClock.CombatNow);
            var phase = state.PhaseAt(EnemySimulationClock.CombatNow);
            QueueAttackPresentation(phase, state.Facing,
                phase == EnemyAttackPresentationPhase.Inactive || phase == EnemyAttackPresentationPhase.Cancelled ? EnemySimulationClock.CombatNow : state.StartAt(phase),
                phase == EnemyAttackPresentationPhase.Inactive || phase == EnemyAttackPresentationPhase.Cancelled ? 0 : (float)(state.EndAt(phase) - state.StartAt(phase)));
        }

        private void QueueAssignmentAttackPresentationBaseline()
        {
            if (!productMovementOnly && productEnemyInitialized &&
                authority != null && authority.RunsCombatDecisions &&
                IsLocallyAlive && IsCanonicalAlive && pendingAttackPresentationEdges.Count == 0)
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
                    Facing = facing,
                    Checkpoint = CaptureCurrentCheckpoint()
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
            if (enemyController != null) contactDamage?.Bind(enemyController);
        }

        private void RefreshContinuousContactDamageInteractions()
        {
            bool hasActiveSimulationAssignment = authority != null &&
                authority.Role != EnemySimulationRole.Frozen;
            SetContinuousContactDamageInteractionsActive(NetworkClient.active &&
                productEnemyInitialized && IsLocallyAlive && IsCanonicalAlive && hasActiveSimulationAssignment);
        }

        private void SetContinuousContactDamageInteractionsActive(bool active)
        {
            contactDamage?.SetRuntimeReady(active);
        }

        private void RefreshAttackScriptExecution()
        {
            bool shouldExecute = !productMovementOnly &&
                productEnemyInitialized && IsLocallyAlive && IsCanonicalAlive &&
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
            // Sequence replicas advance their whole absolute timeline, including the presentation index.
            if (enemyController != null && enemyController.attackScript != null && enemyController.attackScript.SupportsSharedTimeline) return;
            if (productMovementOnly || !productEnemyInitialized ||
                enemyController == null || authority == null ||
                !authority.ConsumesSnapshots)
            {
                return;
            }

            enemyController.ApplyReplicatedAttackPresentation(
                edge.Phase,
                edge.Facing,
                edge.ElapsedAt(EnemySimulationClock.CombatNow));
        }

        private void TryInitializeProductEnemy()
        {
            // SyncVar handoff hooks can run during initial deserialization, before
            // OnStartClient resolves the definition. Never initialize appearance/HP early.
            if (!networkStartCallbacksReady || productEnemyInitialized || enemyController == null)
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
                enemyController.InitNetworkMovementOnly(unchecked((int)netId), ApplyBirthAfterReset);
            }
            else
            {
                enemyController.Init(unchecked((int)netId), ApplyBirthAfterReset);
            }
            if (combatant != null &&
                combatant.MaxHealth < runtimeMinimumHealthOverride)
            {
                combatant.SetMaximumHealthPreservingMissingHealth(
                    runtimeMinimumHealthOverride);
            }
            ConfigureLocalDamageInteractions();
            RestoreCanonicalAfterBirthInitialization();
            productEnemyInitialized = true;
            NetworkCombatWorld.Instance?.TryPresentPendingEnemyHit(netId);
            RefreshAttackScriptExecution();
            if (hasLatestAttackPresentation)
            {
                ApplyReplicaAttackPresentation(latestAttackPresentation);
            }
            RefreshContinuousContactDamageInteractions();
        }
    }
}
