using System.Collections.Generic;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Local-only projectile pool. Projectiles are never NetworkSpawned.</summary>
    public static class StraightProjectilePool
    {
        private const int MaxRetainedPerPrefab = 2048;
        private static readonly Dictionary<int, Stack<StraightProjectileBehaviour>> pools =
            new Dictionary<int, Stack<StraightProjectileBehaviour>>();

        public static int CreatedCount { get; private set; }
        public static int ReusedCount { get; private set; }
        public static int ReleaseCount { get; private set; }

        public static StraightProjectileBehaviour Spawn(
            StraightProjectileBehaviour prefab,
            Vector3 position,
            Quaternion rotation)
        {
            if (prefab == null)
            {
                throw new System.ArgumentNullException(nameof(prefab));
            }

            int key = prefab.GetInstanceID();
            StraightProjectileBehaviour projectile = null;
            if (pools.TryGetValue(key, out Stack<StraightProjectileBehaviour> pool))
            {
                while (pool.Count > 0 && projectile == null)
                {
                    projectile = pool.Pop();
                }
            }

            if (projectile == null)
            {
                projectile = Object.Instantiate(prefab, position, rotation);
                CreatedCount++;
            }
            else
            {
                ReusedCount++;
            }

            projectile.PrepareForPoolSpawn(key, position, rotation);
            return projectile;
        }

        public static void Release(StraightProjectileBehaviour projectile)
        {
            if (projectile == null)
            {
                return;
            }

            int key = projectile.PoolKey;
            if (key == 0)
            {
                Object.Destroy(projectile.gameObject);
                return;
            }

            if (!pools.TryGetValue(key, out Stack<StraightProjectileBehaviour> pool))
            {
                pool = new Stack<StraightProjectileBehaviour>();
                pools.Add(key, pool);
            }

            projectile.gameObject.SetActive(false);
            ReleaseCount++;
            if (pool.Count >= MaxRetainedPerPrefab)
            {
                Object.Destroy(projectile.gameObject);
            }
            else
            {
                pool.Push(projectile);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            pools.Clear();
            CreatedCount = 0;
            ReusedCount = 0;
            ReleaseCount = 0;
        }
    }
}
