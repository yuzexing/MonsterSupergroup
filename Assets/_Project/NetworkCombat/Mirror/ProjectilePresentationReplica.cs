using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>
    /// Replays remote projectile presentation without creating an AttackSnapshot
    /// or participating in hit and damage resolution.
    /// </summary>
    public sealed class ProjectilePresentationReplica : IDisposable
    {
        private readonly PlayerMovement owner;
        private readonly RuntimeDB database;
        private readonly Dictionary<uint, ProjectileAttackBehaviour> emitters =
            new Dictionary<uint, ProjectileAttackBehaviour>();
        private readonly Dictionary<ProjectilePresentationKey, ProjectileAttack>
            activeProjectiles =
                new Dictionary<ProjectilePresentationKey, ProjectileAttack>();

        private bool disposed;

        public ProjectilePresentationReplica(
            PlayerMovement owner,
            RuntimeDB database)
        {
            this.owner = owner != null
                ? owner
                : throw new ArgumentNullException(nameof(owner));
            this.database = database != null
                ? database
                : throw new ArgumentNullException(nameof(database));
        }

        public int ActiveProjectileCount => activeProjectiles.Count;

        public bool TrySpawn(
            ProjectilePresentationSpawn spawn,
            float elapsedSeconds)
        {
            ThrowIfDisposed();
            if (!spawn.Key.IsValid || activeProjectiles.ContainsKey(spawn.Key) ||
                !TryGetEmitter(spawn.WeaponId, out ProjectileAttackBehaviour emitter))
            {
                return false;
            }

            ProjectileAttack projectile = emitter.PlayPresentation(
                spawn,
                elapsedSeconds,
                returned => HandleReturned(spawn.Key, returned));
            activeProjectiles.Add(spawn.Key, projectile);
            return true;
        }

        public bool TryTerminate(ProjectilePresentationTermination termination)
        {
            ThrowIfDisposed();
            if (!termination.Key.IsValid ||
                !activeProjectiles.TryGetValue(
                    termination.Key,
                    out ProjectileAttack projectile) ||
                projectile == null)
            {
                return false;
            }

            projectile.TerminatePresentation(
                termination.Phase,
                termination.Position);
            return true;
        }

        private bool TryGetEmitter(
            uint weaponId,
            out ProjectileAttackBehaviour emitter)
        {
            if (emitters.TryGetValue(weaponId, out emitter) && emitter != null)
            {
                return true;
            }

            if (!database.TryGetWeaponData(weaponId, out WeaponData weaponData) ||
                weaponData == null ||
                !(weaponData.WeaponPrefab is ProjectileAttackBehaviour prefab))
            {
                emitter = null;
                return false;
            }

            Transform parent = owner.AttacksParent != null
                ? owner.AttacksParent
                : owner.transform;
            emitter = UnityEngine.Object.Instantiate(prefab, parent);
            emitter.gameObject.SetActive(false);
            emitter.name = $"{prefab.name} (Remote Presentation)";
            emitter.InitializePresentationReplica(weaponId, owner);
            emitter.gameObject.SetActive(true);
            emitters[weaponId] = emitter;
            return true;
        }

        private void HandleReturned(
            ProjectilePresentationKey key,
            ProjectileAttack returned)
        {
            if (activeProjectiles.TryGetValue(key, out ProjectileAttack current) &&
                current == returned)
            {
                activeProjectiles.Remove(key);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            activeProjectiles.Clear();
            foreach (ProjectileAttackBehaviour emitter in emitters.Values)
            {
                if (emitter == null)
                {
                    continue;
                }

                emitter.DisposePresentationReplica();
                UnityEngine.Object.Destroy(emitter.gameObject);
            }
            emitters.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ProjectilePresentationReplica));
            }
        }
    }
}
