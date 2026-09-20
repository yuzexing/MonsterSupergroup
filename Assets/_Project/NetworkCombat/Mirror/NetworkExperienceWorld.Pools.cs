using System;
using System.Collections.Generic;
using AstralShift.Pooling;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkExperienceWorld
    {
        // Small ownership wrapper around the existing pool, adding overflow destruction and explicit teardown.
        private sealed class PickupPool<T> where T : Component
        {
            private readonly BasePooler<T> pool;
            private readonly HashSet<T> idle = new();
            private readonly int capacity;
            public PickupPool(string name, Transform parent, int capacity)
            { this.capacity = capacity; pool = new BasePooler<T>(name, parent, capacity); }
            public T Rent()
            {
                while (pool.Get(out var value)) { idle.Remove(value); if (value != null) return value; }
                return null;
            }
            public void Return(T value)
            {
                if (value == null || idle.Contains(value)) return;
                value.gameObject.SetActive(false);
                if (idle.Count >= capacity) { UnityEngine.Object.Destroy(value.gameObject); return; }
                idle.Add(value); pool.Return(value);
            }
            public void Clear()
            { foreach (var item in idle) if (item != null) UnityEngine.Object.Destroy(item.gameObject); idle.Clear(); pool.Clear(); }
        }
        private readonly Dictionary<uint, PickupPool<NetworkExperienceGem>> entityPools = new();
        private readonly Dictionary<uint, PickupPool<Transform>> visualPools = new();
        private readonly HashSet<ExperienceCollectionFlight> flights = new();
        private bool clearingPools;
        public int EntitiesCreated { get; private set; }
        public int EntityPoolHits { get; private set; }
        public int VisualsCreated { get; private set; }
        public int VisualPoolHits { get; private set; }
        private PickupPool<NetworkExperienceGem> EntityPool(NetworkExperienceGem prefab, int capacity)
        {
            uint id = prefab.GetComponent<NetworkIdentity>().assetId;
            if (!entityPools.TryGetValue(id, out var pool))
                entityPools.Add(id, pool = new PickupPool<NetworkExperienceGem>("Pickup entity", transform, capacity));
            return pool;
        }
        private NetworkExperienceGem RentEntity(NetworkExperienceGem prefab, Vector3 position, int capacity)
        {
            var item = EntityPool(prefab, capacity).Rent();
            bool reused = item != null;
            if (item == null) { item = Instantiate(prefab); EntitiesCreated++; }
            else EntityPoolHits++;
            item.PrepareForReuse(prefab, capacity);
            item.transform.SetParent(null); item.transform.SetPositionAndRotation(position, Quaternion.identity);
            item.gameObject.SetActive(true);
            PickupAudit.Emit("pool-rent", runId, 0, $"instance={item.GetInstanceID()};asset={prefab.GetComponent<NetworkIdentity>().assetId};reused={reused};created={EntitiesCreated};hits={EntityPoolHits}");
            return item;
        }
        private void ReturnEntity(NetworkExperienceGem item)
        {
            if (item == null || item.PoolReturned) return;
            item.PoolReturned = true;
            PickupAudit.Emit("pool-return", item.RunId, item.DropId, $"instance={item.GetInstanceID()};effect={item.Effect};capacity={item.PoolCapacity}");
            item.ResetPresentation();
            item.transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(item.gameObject, gameObject.scene);
            item.transform.SetParent(transform, true);
            EntityPool(item.PoolPrefab, item.PoolCapacity).Return(item);
        }
        private void RecycleEntity(NetworkExperienceGem item)
        {
            if (item == null) return;
            if (item.netId != 0 && NetworkServer.spawned.ContainsKey(item.netId)) NetworkServer.UnSpawn(item.gameObject);
            ReturnEntity(item);
        }
        private void RegisterPickupPools()
        {
            Register(gemPrefab, xpDefinition?.IdleCapacity ?? 500);
            if (healthDefinition != null) Register(healthDefinition.Prefab, healthDefinition.IdleCapacity);
            void Register(NetworkExperienceGem prefab, int capacity)
            {
                if (prefab == null) return;
                NetworkClient.UnregisterPrefab(prefab.gameObject);
                NetworkClient.RegisterPrefab(prefab.gameObject,
                    (SpawnMessage message) => RentEntity(prefab, message.position, capacity).gameObject,
                    obj => { var gem = obj.GetComponent<NetworkExperienceGem>(); if (!gem.isServer) ReturnEntity(gem); });
            }
        }
        internal void PresentXpFlight(NetworkExperienceGem gem, Transform receiver)
        {
            if (gem.Visual == null || clearingPools) return;
            uint key = gem.GetComponent<NetworkIdentity>().assetId;
            if (!visualPools.TryGetValue(key, out var pool))
                visualPools.Add(key, pool = new PickupPool<Transform>("Pickup flight", transform, gem.PoolCapacity));
            var visual = pool.Rent();
            if (visual == null) { visual = Instantiate(gem.Visual); VisualsCreated++; }
            else VisualPoolHits++;
            visual.SetParent(null); visual.SetPositionAndRotation(gem.Visual.position, gem.Visual.rotation);
            visual.localScale = gem.Visual.lossyScale;
            visual.gameObject.SetActive(true);
            foreach (var renderer in visual.GetComponentsInChildren<SpriteRenderer>(true)) renderer.enabled = true;
            foreach (var ps in visual.GetComponentsInChildren<ParticleSystem>(true))
            { ps.Clear(); ps.Play(); }
            var flight = visual.GetComponent<ExperienceCollectionFlight>() ?? visual.gameObject.AddComponent<ExperienceCollectionFlight>();
            flights.Add(flight);
            flight.Begin(receiver, runId, () =>
            {
                flights.Remove(flight);
                if (clearingPools || this == null) { if (visual != null) Destroy(visual.gameObject); return; }
                visual.SetParent(transform, true); pool.Return(visual);
            });
        }
        private void ClearPools()
        {
            clearingPools = true;
            foreach (var flight in new List<ExperienceCollectionFlight>(flights)) if (flight != null) Destroy(flight.gameObject);
            flights.Clear();
            foreach (var pool in entityPools.Values) pool.Clear(); entityPools.Clear();
            foreach (var pool in visualPools.Values) pool.Clear(); visualPools.Clear();
            clearingPools = false;
        }
    }
}
