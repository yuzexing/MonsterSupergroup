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

        public int BufferedSnapshotCount => buffer.Count;

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
                return false;
            }

            bool accepted = buffer.Push(snapshot);
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
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
        }

        private void FixedUpdate()
        {
            if (!NetworkClient.active || authority == null ||
                !authority.ConsumesSnapshots)
            {
                return;
            }

            double renderTime = NetworkTime.time - interpolationBackTime;
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

            if (role == EnemySimulationRole.Replica ||
                role == EnemySimulationRole.Frozen)
            {
                CapturePhysicsState();
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.bodyType = RigidbodyType2D.Kinematic;
                body.simulated = true;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                return;
            }

            RestorePhysicsState();
        }

        private void SetPosition(Vector2 position)
        {
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector2.zero;
            }
            else
            {
                transform.position = position;
            }
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
