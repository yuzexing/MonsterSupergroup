using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Interactions;
using AstralShift.Pooling;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>
    /// Reconstructs an EnemyAttackMelee warning and local Player damage window on
    /// an observing Client. It never enters the legacy Enemy attack state machine.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkEnemySimulationAgent))]
    [RequireComponent(typeof(EnemySimulationAuthority))]
    public sealed class NetworkEnemyMeleeReplica : MonoBehaviour
    {
        [SerializeField] private NetworkEnemySimulationAgent simulationAgent;
        [SerializeField] private EnemySimulationAuthority simulationAuthority;
        [SerializeField] private EnemyController controller;
        [SerializeField] private EnemyAttackMelee meleeAttack;

        private GenericPooler<EnemyAttackPrefab> attackPool;
        private EnemyAttackPrefab attackInstance;
        private bool instanceBorrowedFromPool;
        private bool damageWindowActive;
        private double damageWindowEndNetworkTime;
        private uint lastAppliedSequence;
        private uint lastAppliedAssignmentEpoch;
        private EnemyAttackPresentationPhase lastAppliedPhase;
        private bool unsupportedAttackLogged;
        private uint instanceGeneration;
        private Vector2 dashWarningOrigin;
        private bool ownsDashWindow;
        private EnemyAttackDash DashAttack => meleeAttack as EnemyAttackDash;
        public EnemyAttackPrefab ReplicaAttackInstance => attackInstance;

        public bool HasReplicaAttackInstance => attackInstance != null;

        public bool DamageWindowActive => damageWindowActive;

        public uint LastAppliedSequence => lastAppliedSequence;

        public uint LastAppliedAssignmentEpoch => lastAppliedAssignmentEpoch;

        public EnemyAttackPresentationPhase LastAppliedPhase => lastAppliedPhase;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (simulationAgent != null)
            {
                simulationAgent.AttackPresentationChanged -=
                    HandleAttackPresentationChanged;
                simulationAgent.AttackPresentationChanged +=
                    HandleAttackPresentationChanged;
            }
            if (simulationAuthority != null)
            {
                simulationAuthority.RoleChanged -= HandleRoleChanged;
                simulationAuthority.RoleChanged += HandleRoleChanged;
            }
        }

        private void Update()
        {
            if (attackInstance != null && DashAttack != null) attackInstance.transform.position = dashWarningOrigin;
            if (attackInstance != null &&
                (simulationAgent == null || !simulationAgent.IsCanonicalAlive ||
                 simulationAuthority == null ||
                 !simulationAuthority.ConsumesSnapshots))
            {
                ReleaseAttackInstance();
                return;
            }

            if (damageWindowActive &&
                EnemySimulationClock.CombatNow >= damageWindowEndNetworkTime)
            {
                FinishWindowAndPresentation();
            }
        }

        private void OnDisable()
        {
            if (simulationAgent != null)
            {
                simulationAgent.AttackPresentationChanged -=
                    HandleAttackPresentationChanged;
            }
            if (simulationAuthority != null)
            {
                simulationAuthority.RoleChanged -= HandleRoleChanged;
            }
            ReleaseAttackInstance();
        }

        private void HandleRoleChanged(
            EnemySimulationRole previous,
            EnemySimulationRole current)
        {
            if (current != EnemySimulationRole.Replica)
            {
                ReleaseAttackInstance();
            }
        }

        private void HandleAttackPresentationChanged(
            EnemyAttackPresentationEdge edge)
        {
            if (simulationAuthority == null ||
                !simulationAuthority.ConsumesSnapshots ||
                simulationAgent == null || !simulationAgent.IsCanonicalAlive)
            {
                ReleaseAttackInstance();
                return;
            }

            lastAppliedSequence = edge.StateSequence;
            lastAppliedAssignmentEpoch = edge.AssignmentEpoch;
            lastAppliedPhase = edge.Phase;
            if (DashAttack != null) dashWarningOrigin = edge.Checkpoint.Movement.Runtime.Action.DashWarningOrigin;
            switch (edge.Phase)
            {
            case EnemyAttackPresentationPhase.Warning:
                ApplyWarning(edge);
                break;
            case EnemyAttackPresentationPhase.Active:
                ApplyActive(edge);
                break;
            case EnemyAttackPresentationPhase.Recovery:
                FinishWindowAndPresentation();
                break;
            case EnemyAttackPresentationPhase.Inactive:
            case EnemyAttackPresentationPhase.Cancelled:
                ReleaseAttackInstance();
                break;
            }
        }

        private void ApplyWarning(EnemyAttackPresentationEdge edge)
        {
            if (!TryAcquireAttackInstance())
            {
                return;
            }

            PositionAttack(edge.Facing);
            SetDamageEnabled(false);
            EnemyAttackWarning warning = attackInstance.attackWarning;
            if (warning == null)
            {
                return;
            }

            warning.SetWarningTime(
                (float)edge.RemainingAt(EnemySimulationClock.CombatNow),
                meleeAttack.AttackTime);
            warning.Show();
            if (edge.IsExpiredAt(EnemySimulationClock.CombatNow))
            {
                warning.Hide();
            }
        }

        private void ApplyActive(EnemyAttackPresentationEdge edge)
        {
            if (!TryAcquireAttackInstance())
            {
                return;
            }

            PositionAttack(edge.Facing);
            attackInstance.attackWarning?.Hide();

            double remaining = edge.RemainingAt(EnemySimulationClock.CombatNow);
            if (remaining <= 0d)
            {
                // Presentation has already been fast-forwarded by EnemyAnimator.
                // Never compensate for network delay by applying stale damage.
                SetDamageEnabled(false);
                return;
            }

            damageWindowEndNetworkTime = EnemySimulationClock.CombatNow + remaining;
            SetDamageEnabled(true);
        }

        private async void FinishWindowAndPresentation()
        {
            if (ownsDashWindow) DashAttack.SetDashDamageEnabled(false, true);
            attackInstance?.damageInteraction?.SettlePendingCollisions();
            SetDamageEnabled(false);
            var instance = attackInstance;
            uint generation = instanceGeneration;
            if (instance != null && instance.attackWarning != null) await instance.attackWarning.AwaitableHide();
            if (generation == instanceGeneration && instance == attackInstance) ReleaseAttackInstance();
        }

        private bool TryAcquireAttackInstance()
        {
            if (attackInstance != null)
            {
                return true;
            }
            if (meleeAttack == null || meleeAttack.attackPrefab == null ||
                controller == null)
            {
                LogUnsupportedAttackOnce(
                    "Replica melee presentation requires EnemyAttackMelee, " +
                    "its attackPrefab, and EnemyController.");
                return false;
            }
            if (meleeAttack.attackPrefab.damageInteraction == null && DashAttack == null)
            {
                LogUnsupportedAttackOnce(
                    "The first network melee slice supports the " +
                    "PlayerDamageInteraction attack mode only.");
                return false;
            }

            if (PoolManager.Instance != null)
            {
                attackPool = PoolManager.Instance.GetOrCreatePooler(
                    meleeAttack.attackPrefab);
                attackInstance = attackPool.GetOrCreate(transform, true);
                instanceBorrowedFromPool = true;
            }
            else
            {
                attackInstance = Instantiate(meleeAttack.attackPrefab, transform);
                instanceBorrowedFromPool = false;
            }

            attackInstance.SetStats(controller.stats);
            instanceGeneration++;
            MonsterSupergroup.Gameplay.Combat.GameplayMapPresentation.Warning(attackInstance.gameObject);
            PlayerDamageInteraction interaction = attackInstance.damageInteraction;
            if (interaction != null) interaction.enemyStats = controller.stats;
            SetDamageEnabled(false);
            return true;
        }

        private void PositionAttack(Vector2 facing)
        {
            if (facing.sqrMagnitude <= 0.0001f)
            {
                facing = controller.FacingDirection.sqrMagnitude > 0.0001f
                    ? controller.FacingDirection
                    : Vector2.right;
            }

            attackInstance.transform.SetParent(transform, true);
            attackInstance.transform.position = DashAttack != null ? (Vector3)dashWarningOrigin : transform.position;
            float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            attackInstance.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void SetDamageEnabled(bool enabled)
        {
            damageWindowActive = enabled;
            if (DashAttack != null)
            {
                if (enabled || ownsDashWindow) DashAttack.SetDashDamageEnabled(enabled);
                ownsDashWindow = enabled;
            }
            if (attackInstance == null)
            {
                return;
            }

            PlayerDamageInteraction interaction = attackInstance.damageInteraction;
            if (interaction != null)
            {
                interaction.gameObject.SetActive(enabled);
            }
            if (attackInstance.hitBox != null)
            {
                // HitBox mode is not part of this first vertical slice.
                attackInstance.hitBox.Toggle(false);
            }
        }

        private void ReleaseAttackInstance()
        {
            instanceGeneration++;
            if (ownsDashWindow) { DashAttack.SetDashDamageEnabled(false); ownsDashWindow = false; }
            // Epoch/role changes invalidate deferred contacts from the old window.
            attackInstance?.damageInteraction?.DiscardPendingCollisions();
            if (attackInstance == null)
            {
                damageWindowActive = false;
                return;
            }

            SetDamageEnabled(false);
            EnemyAttackPrefab released = attackInstance;
            attackInstance = null;
            if (instanceBorrowedFromPool && attackPool != null)
            {
                if (!gameObject.activeInHierarchy)
                {
                    released.gameObject.SetActive(false);
                    EnemyAttackPrefab.ReturnAfterHierarchyChange(released, attackPool);
                }
                else attackPool.Return(released);
            }
            else
            {
                released.gameObject.SetActive(false);
                Destroy(released.gameObject);
            }
            instanceBorrowedFromPool = false;
        }

        private void ResolveReferences()
        {
            if (simulationAgent == null)
            {
                simulationAgent = GetComponent<NetworkEnemySimulationAgent>();
            }
            if (simulationAuthority == null)
            {
                simulationAuthority = GetComponent<EnemySimulationAuthority>();
            }
            if (controller == null)
            {
                controller = GetComponent<EnemyController>();
            }
            if (meleeAttack == null)
            {
                meleeAttack = GetComponent<EnemyAttackMelee>();
            }
        }

        private void LogUnsupportedAttackOnce(string message)
        {
            if (unsupportedAttackLogged)
            {
                return;
            }

            unsupportedAttackLogged = true;
            Debug.LogError(message, this);
        }
    }
}
