using AstralShift.HellMaiden.AI;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(EnemySimulationAuthority))]
    public sealed class EnemySnapshotInterpolator : MonoBehaviour
    {
        [SerializeField] private EnemySimulationAuthority authority;
        [SerializeField] private Rigidbody2D body;
        [SerializeField, Min(0f)] private float interpolationBackTime = 0.1f;
        [SerializeField, Min(0f)] private float maximumExtrapolation = 0.1f;

        private readonly EnemySnapshotBuffer buffer = new EnemySnapshotBuffer();
        private RigidbodyType2D originalBodyType;
        private RigidbodyConstraints2D originalConstraints;
        private bool originalSimulated;
        private bool physicsStateCaptured;
        private RigidbodyInterpolation2D originalInterpolation;
        private bool interpolationCaptured;

        public int BufferedSnapshotCount => buffer.Count;
        public string LastPushReason { get; private set; }
        internal bool ObserveWithoutInterpolation { get; set; }

        private void Awake()
        {
            ResolveReferences();
            CapturePhysicsState();
        }

        private void OnEnable()
        {
            ResolveReferences();
            authority.RoleChanged += HandleRoleChanged;
            ApplyPhysicsForRole(authority.Role);
        }

        private void OnDisable()
        {
            if (authority != null)
            {
                authority.RoleChanged -= HandleRoleChanged;
            }
            RestorePhysicsState();
            if (interpolationCaptured && body != null) body.interpolation = originalInterpolation;
            interpolationCaptured = false;
            buffer.Clear();
        }

        public void Configure(
            EnemySimulationAuthority simulationAuthority,
            Rigidbody2D rigidbody)
        {
            authority = simulationAuthority;
            body = rigidbody;
            CapturePhysicsState();
            ApplyPhysicsForRole(authority.Role);
        }

        public bool Push(EnemySimulationSnapshot snapshot)
        {
            if (authority == null || !authority.ConsumesSnapshots ||
                snapshot.AssignmentEpoch != authority.AssignmentEpoch)
            {
                LastPushReason = authority == null ? "AuthorityUnavailable" : !authority.ConsumesSnapshots ? "RoleDoesNotConsumeSnapshots" : "WrongEpoch";
                return false;
            }

            bool accepted = buffer.Push(snapshot, out var rejection);
            LastPushReason = accepted ? "None" : rejection.ToString();
            if (accepted &&
                (snapshot.Flags & EnemySimulationSnapshotFlags.Discontinuity) != 0)
            {
                SetPosition(snapshot.Position);
            }
            return accepted;
        }

        public void ClearSnapshots()
        {
            buffer.Clear();
            if (body != null)
            {
                StopVelocity();
                ResetRenderPose(body.position);
            }
        }

        // Teleports/handoffs must seed a new physics render history, never blend from the old location.
        public void ResetRenderPose(Vector2 position)
        {
            if (body == null) return;
            body.interpolation = RigidbodyInterpolation2D.None;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            body.position = position;
            RefreshRenderInterpolation();
        }

        private void LateUpdate()
        {
            RefreshRenderInterpolation();
            UpdateReplicaPosition();
        }

        private void RefreshRenderInterpolation()
        {
            if (body == null || authority == null) return;
            // Snapshot replicas already interpolate. Dedicated servers need only the physics pose.
            bool localSimulation = !ObserveWithoutInterpolation && NetworkClient.active && authority.RunsNavigation &&
                body.simulated && body.bodyType != RigidbodyType2D.Static && Time.timeScale > 0 &&
                (body.constraints & RigidbodyConstraints2D.FreezePosition) != RigidbodyConstraints2D.FreezePosition;
            var wanted = localSimulation ? RigidbodyInterpolation2D.Interpolate : RigidbodyInterpolation2D.None;
            if (body.interpolation == wanted) return;
            var position = body.position;
            body.interpolation = RigidbodyInterpolation2D.None;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            body.position = position;
            body.interpolation = wanted;
        }

        private void StopVelocity()
        {
            if (body.bodyType == RigidbodyType2D.Static) return;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0;
        }

        private void UpdateReplicaPosition()
        {
            if (!NetworkClient.active || Time.timeScale <= 0 || authority == null ||
                !authority.ConsumesSnapshots)
            {
                return;
            }

            double renderTime = EnemySimulationClock.Now - interpolationBackTime;
            if (buffer.TrySample(
                renderTime,
                maximumExtrapolation,
                out Vector2 position,
                out _))
            {
                SetPosition(position);
            }
        }

        private void HandleRoleChanged(
            EnemySimulationRole previous,
            EnemySimulationRole current)
        {
            buffer.Clear();
            ApplyPhysicsForRole(current);
        }

        private void ApplyPhysicsForRole(EnemySimulationRole role)
        {
            if (body == null)
            {
                return;
            }

            if (!interpolationCaptured)
            {
                originalInterpolation = body.interpolation;
                interpolationCaptured = true;
            }

            if (role == EnemySimulationRole.Replica ||
                role == EnemySimulationRole.Frozen)
            {
                CapturePhysicsState();
                StopVelocity();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.simulated = true;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                RefreshRenderInterpolation();
                return;
            }

            RestorePhysicsState();
            RefreshRenderInterpolation();
        }

        private void SetPosition(Vector2 position)
        {
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector2.zero;
            }
            // Rigidbody2D.position alone can leave its displayed Transform at the
            // previous physics tick. Replicas render the sampled pose this frame.
            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        private void ResolveReferences()
        {
            if (authority == null)
            {
                authority = GetComponent<EnemySimulationAuthority>();
            }
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }
        }

        private void CapturePhysicsState()
        {
            if (physicsStateCaptured || body == null)
            {
                return;
            }

            originalBodyType = body.bodyType;
            originalConstraints = body.constraints;
            originalSimulated = body.simulated;
            physicsStateCaptured = true;
        }

        private void RestorePhysicsState()
        {
            if (!physicsStateCaptured || body == null)
            {
                return;
            }

            body.bodyType = originalBodyType;
            body.constraints = originalConstraints;
            body.simulated = originalSimulated;
            physicsStateCaptured = false;
        }
    }
}
