using MonsterSupergroup.Gameplay.Combat;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Destroys the network Enemy only after canonical death is confirmed.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(CombatantBehaviour))]
    public sealed class NetworkEnemyServerDriver : NetworkBehaviour
    {
        public override void OnStartServer()
        {
            base.OnStartServer();
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            if (world != null)
            {
                world.ServerCanonicalBatchProduced += HandleCanonicalBatch;
            }
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
