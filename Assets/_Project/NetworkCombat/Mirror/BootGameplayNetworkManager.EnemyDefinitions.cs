using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using MonsterSupergroup.Gameplay.Combat.Content;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public struct EnemyCatalogHello : NetworkMessage { public string Fingerprint; }
    public struct EnemyCatalogAccepted : NetworkMessage { public string Fingerprint; }

    public sealed partial class BootGameplayNetworkManager
    {
        [SerializeField] private EnemyDefinitionCatalog enemyDefinitions;
        private EnemyDefinitionRegistry enemyDefinitionRegistry;
        private readonly HashSet<int> acceptedEnemyCatalog = new HashSet<int>();
        private readonly HashSet<int> pendingEnemyCatalog = new HashSet<int>();
        public EnemyDefinitionCatalog EnemyCatalog => enemyDefinitions;
        public EnemyDefinitionRegistry EnemyDefinitions => enemyDefinitionRegistry ??
            (enemyDefinitions != null ? enemyDefinitionRegistry = enemyDefinitions.Capture() : null);

        private void PrepareEnemyCatalog(bool server)
        {
            if (!NetworkServer.active || server) enemyDefinitionRegistry = null;
            var registry = EnemyDefinitions;
            var registered = new HashSet<GameObject>();
            if (registry != null)
                foreach (var definition in registry.Entries)
                {
                    var prefab = definition.Prefab;
                    if (prefab.GetComponent<NetworkIdentity>() == null || prefab.GetComponent<NetworkEnemySimulationAgent>() == null)
                        throw new InvalidOperationException("Invalid network enemy definition: " + definition.Id);
                    if (!spawnPrefabs.Contains(prefab)) spawnPrefabs.Add(prefab);
                    if (!server && registered.Add(prefab) && !NetworkClient.prefabs.ContainsKey(prefab.GetComponent<NetworkIdentity>().assetId))
                        NetworkClient.RegisterPrefab(prefab);
                }
            if (server)
            {
                acceptedEnemyCatalog.Clear(); pendingEnemyCatalog.Clear();
                NetworkServer.RegisterHandler<EnemyCatalogAccepted>(ReceiveEnemyCatalogAccepted);
            }
            else NetworkClient.RegisterHandler<EnemyCatalogHello>(ReceiveEnemyCatalogHello);
        }
        // With no catalog, explicit old PerMember test entry points remain usable.
        // A production Wave run still requires every referenced DefinitionId to be in the catalog.
        private bool CheckEnemyCatalogBeforeReady(NetworkConnectionToClient connection)
        {
            if (enemyDefinitions == null || connection is LocalConnectionToClient || acceptedEnemyCatalog.Contains(connection.connectionId)) return true;
            if (pendingEnemyCatalog.Add(connection.connectionId))
            {
                connection.Send(new EnemyCatalogHello { Fingerprint = EnemyDefinitions.Fingerprint });
                StartCoroutine(WaitForEnemyCatalog(connection));
            }
            return false;
        }
        private IEnumerator WaitForEnemyCatalog(NetworkConnectionToClient connection)
        {
            yield return new WaitForSecondsRealtime(20);
            if (NetworkServer.active && pendingEnemyCatalog.Contains(connection.connectionId) &&
                NetworkServer.connections.TryGetValue(connection.connectionId, out var current) && ReferenceEquals(current, connection))
            {
                Debug.LogWarning("[EnemyDefinitions] Content handshake timed out: " + connection.connectionId);
                connection.Disconnect();
            }
        }
        private void ReceiveEnemyCatalogHello(EnemyCatalogHello hello)
        {
            try
            {
                if (EnemyDefinitions == null || hello.Fingerprint != EnemyDefinitions.Fingerprint)
                    throw new InvalidOperationException("Enemy content versions differ. Use the same build/content catalog on both peers.");
                NetworkClient.Send(new EnemyCatalogAccepted { Fingerprint = EnemyDefinitions.Fingerprint });
            }
            catch (Exception error)
            {
                ShowMenuNotice(error.Message); Debug.LogError("[EnemyDefinitions] " + error.Message);
                NetworkClient.Disconnect();
            }
        }
        private void ReceiveEnemyCatalogAccepted(NetworkConnectionToClient connection, EnemyCatalogAccepted accepted)
        {
            if (!pendingEnemyCatalog.Remove(connection.connectionId)) return;
            if (EnemyDefinitions == null || accepted.Fingerprint != EnemyDefinitions.Fingerprint)
            { connection.Disconnect(); return; }
            acceptedEnemyCatalog.Add(connection.connectionId);
            OnServerReady(connection); // Resume the existing additive-load/restore path, once content is known.
        }
        private void ForgetEnemyCatalog(NetworkConnectionToClient connection)
        {
            if (connection == null) return;
            acceptedEnemyCatalog.Remove(connection.connectionId); pendingEnemyCatalog.Remove(connection.connectionId);
        }
        private void StopEnemyCatalog(bool server)
        {
            if (server)
            {
                NetworkServer.UnregisterHandler<EnemyCatalogAccepted>();
                acceptedEnemyCatalog.Clear(); pendingEnemyCatalog.Clear();
            }
            else NetworkClient.UnregisterHandler<EnemyCatalogHello>();
            if (server || !NetworkServer.active) enemyDefinitionRegistry = null;
        }
    }
}
