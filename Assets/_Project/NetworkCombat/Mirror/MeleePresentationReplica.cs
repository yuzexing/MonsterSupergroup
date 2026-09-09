using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Replays slash animations without creating a weapon or attack GAS runtime.</summary>
    public sealed class MeleePresentationReplica : IDisposable
    {
        private readonly PlayerMovement owner;
        private readonly RuntimeDB database;
        private readonly Dictionary<uint, MeleeAttackBehaviour> emitters =
            new Dictionary<uint, MeleeAttackBehaviour>();
        private readonly Dictionary<MeleePresentationKey, ActiveSlash> activeSlashes =
            new Dictionary<MeleePresentationKey, ActiveSlash>();
        private bool disposed;

        public MeleePresentationReplica(PlayerMovement owner, RuntimeDB database)
        {
            this.owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            this.database = database != null ? database : throw new ArgumentNullException(nameof(database));
        }

        public int ActiveSlashCount => activeSlashes.Count;

        public bool TrySpawn(MeleePresentationSpawn spawn, float elapsedSeconds)
        {
            ThrowIfDisposed();
            if (!spawn.Key.IsValid || float.IsNaN(elapsedSeconds) ||
                float.IsInfinity(elapsedSeconds) || elapsedSeconds < 0f ||
                activeSlashes.ContainsKey(spawn.Key) ||
                !TryGetEmitter(spawn.WeaponId, out MeleeAttackBehaviour emitter)) return false;

            // Seeking can reach the clip end inside PlayPresentation. Do not add a pooled
            // or already-returned slash after its synchronous completion callback.
            bool returnedDuringSpawn = false;
            AnimatedAttack attack = emitter.PlayPresentation(spawn, elapsedSeconds, returned =>
            {
                returnedDuringSpawn = true;
                HandleReturned(spawn.Key, returned);
            });
            if (attack == null || returnedDuringSpawn) return false;

            activeSlashes.Add(spawn.Key, new ActiveSlash(spawn.WeaponId, emitter, attack));
            return true;
        }

        public bool TryTerminate(MeleePresentationTermination termination)
        {
            ThrowIfDisposed();
            if (!termination.Key.IsValid ||
                !activeSlashes.TryGetValue(termination.Key, out ActiveSlash slash) ||
                slash.WeaponId != termination.WeaponId) return false;

            activeSlashes.Remove(termination.Key);
            return slash.Emitter != null && slash.Emitter.TerminatePresentation(termination.Key);
        }

        private bool TryGetEmitter(uint weaponId, out MeleeAttackBehaviour emitter)
        {
            if (emitters.TryGetValue(weaponId, out emitter) && emitter != null) return true;
            if (!database.TryGetWeaponData(weaponId, out WeaponData data) || data == null ||
                !(data.WeaponPrefab is MeleeAttackBehaviour prefab))
            {
                emitter = null;
                return false;
            }

            Transform parent = owner.AttacksParent != null ? owner.AttacksParent : owner.transform;
            var staging = new GameObject("Melee Presentation Initialization");
            staging.SetActive(false);
            try
            {
                emitter = UnityEngine.Object.Instantiate(prefab, staging.transform);
                emitter.gameObject.SetActive(false);
                emitter.name = $"{prefab.name} (Remote Presentation)";
                emitter.transform.SetParent(parent, false);
                emitter.InitializePresentationReplica(weaponId, owner);
                emitter.enabled = false;
                emitter.gameObject.SetActive(true);
                emitters[weaponId] = emitter;
                return true;
            }
            catch
            {
                if (emitter != null) UnityEngine.Object.Destroy(emitter.gameObject);
                throw;
            }
            finally
            {
                UnityEngine.Object.Destroy(staging);
            }
        }

        private void HandleReturned(MeleePresentationKey key, AnimatedAttack returned)
        {
            if (activeSlashes.TryGetValue(key, out ActiveSlash slash) && slash.Attack == returned)
                activeSlashes.Remove(key);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            activeSlashes.Clear();
            foreach (MeleeAttackBehaviour emitter in emitters.Values)
            {
                if (emitter == null) continue;
                emitter.DisposePresentationReplica();
                UnityEngine.Object.Destroy(emitter.gameObject);
            }
            emitters.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(MeleePresentationReplica));
        }

        private readonly struct ActiveSlash
        {
            public ActiveSlash(uint weaponId, MeleeAttackBehaviour emitter, AnimatedAttack attack)
            { WeaponId = weaponId; Emitter = emitter; Attack = attack; }
            public uint WeaponId { get; }
            public MeleeAttackBehaviour Emitter { get; }
            public AnimatedAttack Attack { get; }
        }
    }
}
