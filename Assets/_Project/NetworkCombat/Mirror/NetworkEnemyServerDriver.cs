using MonsterSupergroup.Gameplay.Combat;
using System.Collections;
using AstralShift.HellMaiden.AI.Enemy;
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
        [SerializeField] private bool waitForDeathPresentation;
        private EnemyController enemy;
        private bool awaitingPresentation;

        public override void OnStartServer()
        {
            base.OnStartServer();
            awaitingPresentation = false;
            if (waitForDeathPresentation) TryGetComponent(out enemy);
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
                    // Canonical death has already removed this entity from all
                    // source alive counts and disabled combat. Retain only its
                    // existing presentation until the normal death callback.
                    if (waitForDeathPresentation && enemy != null && !enemy.DeathPresentationComplete)
                    {
                        if (!awaitingPresentation)
                        { awaitingPresentation = true; StartCoroutine(FinishPresentation()); }
                        return;
                    }
                    NetworkServer.Destroy(gameObject);
                    return;
                }
            }
        }

        private IEnumerator FinishPresentation()
        {
            // Rendering may be culled, disabled, or interrupted. Confirmed corpses
            // must still leave the network world, even while gameplay is paused.
            float clipDuration = enemy != null && enemy.enemyAnimator != null ? enemy.enemyAnimator.DeadTime : 0;
            double deadline = Time.unscaledTimeAsDouble + Mathf.Clamp(clipDuration, 1, 10) + 1;
            while (enemy != null && !enemy.DeathPresentationComplete && Time.unscaledTimeAsDouble < deadline) yield return null;
            if (enemy != null && !enemy.DeathPresentationComplete)
                NetworkEnemySimulationWorld.Instance?.RecordDeathCleanupTimeout();
            if (NetworkServer.active) NetworkServer.Destroy(gameObject);
        }
    }
}
