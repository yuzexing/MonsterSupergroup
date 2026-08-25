using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Local;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>
    /// Keeps the existing lightweight enemy chase on the server and destroys the
    /// network object only after the canonical ledger confirms death.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(CombatantBehaviour))]
    public sealed class NetworkEnemyServerDriver : NetworkBehaviour
    {
        [SerializeField] private LocalEnemyChase chase;
        [SerializeField, Min(0.05f)] private float retargetInterval = 0.25f;

        private float nextRetargetTime;

        private void Awake()
        {
            if (chase == null)
            {
                chase = GetComponent<LocalEnemyChase>();
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (chase != null)
            {
                chase.enabled = true;
            }

            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null)
            {
                world.ServerCanonicalBatchProduced += HandleCanonicalBatch;
            }

            Retarget();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!isServer && chase != null)
            {
                chase.enabled = false;
            }
        }

        [ServerCallback]
        private void Update()
        {
            if (Time.unscaledTime < nextRetargetTime)
            {
                return;
            }

            nextRetargetTime = Time.unscaledTime + retargetInterval;
            Retarget();
        }

        public override void OnStopServer()
        {
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null)
            {
                world.ServerCanonicalBatchProduced -= HandleCanonicalBatch;
            }

            base.OnStopServer();
        }

        [Server]
        private void Retarget()
        {
            if (chase == null)
            {
                return;
            }

            var teams = CombatTeamBehaviour.ActiveTeams;
            Transform nearest = null;
            float nearestDistance = float.PositiveInfinity;
            Vector2 position = transform.position;
            for (int i = 0; i < teams.Count; i++)
            {
                CombatTeamBehaviour candidate = teams[i];
                if (candidate == null || candidate.Team != CombatTeam.Player ||
                    candidate.Combatant == null || !candidate.Combatant.IsAlive)
                {
                    continue;
                }

                float distance = ((Vector2)candidate.transform.position - position).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = candidate.transform;
                }
            }

            if (nearest != null)
            {
                chase.Initialize(nearest);
            }
        }

        [Server]
        private void HandleCanonicalBatch(CanonicalWorldBatch batch)
        {
            CanonicalEntityState[] entities = batch.Entities;
            if (entities == null)
            {
                return;
            }

            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i].EntityId == netId && !entities[i].Alive)
                {
                    NetworkServer.Destroy(gameObject);
                    return;
                }
            }
        }
    }
}
