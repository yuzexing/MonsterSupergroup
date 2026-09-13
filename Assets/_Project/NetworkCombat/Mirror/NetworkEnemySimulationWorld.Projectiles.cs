using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        private readonly EnemyProjectileHistory presentedProjectiles = new EnemyProjectileHistory();
        private readonly EnemyProjectileHistory acceptedProjectiles = new EnemyProjectileHistory();
        private readonly Dictionary<uint, List<EnemyProjectileLaunch>> pendingProjectileLaunches = new Dictionary<uint, List<EnemyProjectileLaunch>>();
        private readonly Dictionary<uint, BulletProjectile> projectileDefinitions = new Dictionary<uint, BulletProjectile>();
        private readonly Dictionary<uint, Stack<BulletProjectile>> projectilePools = new Dictionary<uint, Stack<BulletProjectile>>();
        private readonly Dictionary<BulletProjectile, uint> borrowedProjectiles = new Dictionary<BulletProjectile, uint>();
        private Transform projectilePoolRoot, projectileFlightRoot;
        public int PresentedProjectileCount { get; private set; }
        public int AcceptedProjectileCount { get; private set; }
        public int ActiveEnemyProjectileCount => borrowedProjectiles.Count;
        public event Action<EnemyProjectileLaunch, BulletProjectile> EnemyProjectilePresented;
        public event Action<EnemyProjectileLaunch> EnemyProjectileAccepted;

        public void EmitEnemyProjectile(NetworkEnemySimulationAgent agent, EnemyProjectileLaunch launch)
        {
            if (BootGameplayNetworkManager.CombatHasEnded) return;
            if (!launch.IsValid) throw new InvalidOperationException("Invalid local enemy projectile launch.");
            projectileDefinitions[launch.EnemyPrefabAssetId] = agent.GetComponent<EnemyProjectileAttack>().bulletPrefab;
            if (NetworkClient.active) PresentEnemyProjectile(launch);
            if (isServer && agent.Assignment.Host != EnemySimulationHost.ClientPlayer)
            {
                if (TryAcceptEnemyProjectile(0, launch)) BroadcastEnemyProjectiles(new[] { launch });
            }
            else
            {
                uint owner = agent.Assignment.SimulationOwnerPlayerId;
                if (!pendingProjectileLaunches.TryGetValue(owner, out var list)) pendingProjectileLaunches[owner] = list = new List<EnemyProjectileLaunch>();
                list.Add(launch);
            }
        }

        internal void CollectClientProjectileLaunches(uint owner, List<EnemyProjectileLaunch> output)
        {
            output.Clear();
            if (pendingProjectileLaunches.TryGetValue(owner, out var pending)) { output.AddRange(pending); pending.Clear(); }
        }

        internal bool TryAcceptEnemyProjectile(uint owner, EnemyProjectileLaunch launch)
        {
            if (BootGameplayNetworkManager.CombatHasEnded || !launch.IsValid || !enemies.TryGetValue(launch.Key.EnemyEntityId, out var enemy) || enemy == null ||
                !IsServerEnemyAlive(launch.Key.EnemyEntityId) || !Registry.TryGetAssignment(launch.Key.EnemyEntityId, out var assignment) ||
                assignment.Epoch != launch.AssignmentEpoch ||
                (owner == 0 ? assignment.Host != EnemySimulationHost.ServerFallback && assignment.Host != EnemySimulationHost.ServerAuthoritative :
                    assignment.Host != EnemySimulationHost.ClientPlayer || assignment.SimulationOwnerPlayerId != owner)) return false;
            var attack = enemy.GetComponent<EnemyProjectileAttack>();
            var stats = enemy.GetComponent<EnemyController>().stats;
            var pose = launch.Checkpoint.Movement;
            if (Registry.TryGetLatestSnapshot(launch.Key.EnemyEntityId, out var current) &&
                current.Runtime.Action.ActionId > launch.Key.ActionId) return false;
            if (attack == null || attack.bulletPrefab == null || enemy.netIdentity.assetId != launch.EnemyPrefabAssetId ||
                !Mathf.Approximately(launch.Speed, attack.bulletPrefab.speed) || !Mathf.Approximately(launch.Lifetime, attack.bulletPrefab.duration) ||
                launch.Damage != stats.Damage || !Mathf.Approximately(launch.StunTime, stats.StunTime) ||
                pose.EnemyEntityId != launch.Key.EnemyEntityId || pose.AssignmentEpoch != assignment.Epoch || !pose.IsFinite ||
                pose.Runtime.Action.ActionId != launch.Key.ActionId || !pose.Runtime.Action.ProjectileEmitted ||
                pose.Runtime.Action.Phase != AstralShift.HellMaiden.AI.EnemyAttackPresentationPhase.Active ||
                acceptedProjectiles.Contains(launch.Key, NetworkTime.time)) return false;
            Registry.ConfirmProjectileLaunch(launch);
            acceptedProjectiles.Add(launch.Key, NetworkTime.time);
            AcceptPendingTermination(launch.Key);
            AcceptedProjectileCount++;
            EnemyProjectileAccepted?.Invoke(launch);
            return true;
        }

        private void SubmitEnemyProjectiles(uint owner, EnemyProjectileLaunch[] launches)
        {
            if (launches == null || launches.Length > maximumAttackPresentationEdgesPerBatch) return;
            var accepted = new List<EnemyProjectileLaunch>();
            foreach (var launch in launches) if (TryAcceptEnemyProjectile(owner, launch)) accepted.Add(launch);
            if (accepted.Count > 0) BroadcastEnemyProjectiles(accepted.ToArray());
        }
        private void BroadcastEnemyProjectiles(EnemyProjectileLaunch[] launches) =>
            RpcApplyAttackPresentations(new EnemyAttackPresentationBatch { Round = CurrentRound, ProjectileLaunches = launches });

        public bool PresentEnemyProjectile(EnemyProjectileLaunch launch)
        {
            if (!NetworkClient.active || !launch.IsValid || endedProjectiles.Contains(launch.Key, NetworkTime.time) || presentedProjectiles.Contains(launch.Key, NetworkTime.time)) return false;
            var prefab = ResolveEnemyBullet(launch.EnemyPrefabAssetId);
            if (prefab == null) { Debug.LogError("Unregistered enemy projectile asset " + launch.EnemyPrefabAssetId); return false; }
            var bullet = BorrowEnemyBullet(launch.EnemyPrefabAssetId, prefab);
            bullet.transform.SetPositionAndRotation(launch.Origin, Quaternion.identity);
            bullet.speed = launch.Speed; bullet.duration = launch.Lifetime; bullet.pierce = 1; bullet.bulletHasTimeOut = true;
            bullet.ShooterController = null;
            bullet.damageInteraction.ConfigureLocalProjectile(launch.Damage, launch.StunTime, () => EndLocalEnemyProjectile(launch.Key, EnemyProjectileEndReason.Hit));
            bullet.damageInteraction.enabled = true;
            foreach (var c in bullet.GetComponentsInChildren<Collider2D>(true)) c.enabled = true;
            bullet.OnReturn = () => EndLocalEnemyProjectile(launch.Key, EnemyProjectileEndReason.Expired);
            bullet.OnDisabled = () => EndLocalEnemyProjectile(launch.Key, EnemyProjectileEndReason.Despawned, true);
            liveProjectiles.Add(launch.Key, bullet);
            bullet.gameObject.SetActive(true);
            bullet.Fire(launch.Direction);
            presentedProjectiles.Add(launch.Key, NetworkTime.time);
            PresentedProjectileCount++;
            EnemyProjectilePresented?.Invoke(launch, bullet);
            return true;
        }

        private BulletProjectile ResolveEnemyBullet(uint assetId)
        {
            if (projectileDefinitions.TryGetValue(assetId, out var definition) && definition != null) return definition;
            if (NetworkManager.singleton != null)
                foreach (var prefab in NetworkManager.singleton.spawnPrefabs)
                    if (prefab != null && prefab.TryGetComponent<NetworkIdentity>(out var identity) && identity.assetId == assetId &&
                        prefab.TryGetComponent<NetworkEnemyProjectileAdapter>(out var adapter))
                        return projectileDefinitions[assetId] = adapter.Attack.bulletPrefab;
            return null;
        }

        internal BulletProjectile BorrowEnemyBullet(uint assetId, BulletProjectile prefab)
        {
            if (!NetworkClient.active) return null;
            if (projectilePoolRoot == null)
            {
                projectilePoolRoot = new GameObject("Enemy projectile pool").transform; projectilePoolRoot.SetParent(transform);
                projectilePoolRoot.gameObject.SetActive(false);
                projectileFlightRoot = new GameObject("Enemy projectiles").transform; projectileFlightRoot.SetParent(transform);
            }
            if (!projectilePools.TryGetValue(assetId, out var pool)) projectilePools[assetId] = pool = new Stack<BulletProjectile>();
            BulletProjectile bullet = null;
            while (pool.Count > 0 && bullet == null) bullet = pool.Pop();
            if (bullet == null) bullet = Instantiate(prefab, projectilePoolRoot);
            bullet.gameObject.SetActive(false);
            bullet.transform.SetParent(projectileFlightRoot, false);
            borrowedProjectiles.Add(bullet, assetId);
            return bullet;
        }
        internal void ReturnEnemyBullet(BulletProjectile bullet)
        {
            if (bullet == null || !borrowedProjectiles.TryGetValue(bullet, out uint asset)) return;
            borrowedProjectiles.Remove(bullet);
            bullet.OnReturn = null; bullet.OnDisabled = null; bullet.onAttackFiredEnd = null; bullet.ShooterController = null;
            bullet.damageInteraction?.DiscardPendingCollisions();
            bullet.gameObject.SetActive(false);
            bullet.transform.SetParent(projectilePoolRoot, false);
            projectilePools[asset].Push(bullet);
        }
        private void ClearEnemyProjectiles()
        {
            foreach (var pair in borrowedProjectiles) if (pair.Key != null) { pair.Key.OnReturn = null; pair.Key.OnDisabled = null; pair.Key.damageInteraction?.DiscardPendingCollisions(); pair.Key.gameObject.SetActive(false); }
            borrowedProjectiles.Clear(); projectilePools.Clear(); projectileDefinitions.Clear();
            if (projectileFlightRoot != null) Destroy(projectileFlightRoot.gameObject);
            if (projectilePoolRoot != null) Destroy(projectilePoolRoot.gameObject);
            projectileFlightRoot = projectilePoolRoot = null;
            pendingProjectileLaunches.Clear(); presentedProjectiles.Clear();
            liveProjectiles.Clear(); endedProjectiles.Clear(); pendingClientTerminations.Clear();
            PresentedProjectileCount = 0;
        }
    }
}
