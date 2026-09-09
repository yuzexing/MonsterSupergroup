using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Owner-relative local orbit replay. Neither emitters nor orbs create a GAS runtime.</summary>
    public sealed class OrbitPresentationReplica : IDisposable
    {
        private readonly PlayerMovement owner;
        private readonly RuntimeDB database;
        private readonly Dictionary<uint, CirclingAttackBehaviour> emitters = new Dictionary<uint, CirclingAttackBehaviour>();
        private readonly Dictionary<OrbitPresentationKey, ActiveOrb> orbs = new Dictionary<OrbitPresentationKey, ActiveOrb>();
        private bool disposed;
        public int ActiveOrbCount => orbs.Count;

        public OrbitPresentationReplica(PlayerMovement owner, RuntimeDB database)
        {
            this.owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            this.database = database != null ? database : throw new ArgumentNullException(nameof(database));
        }

        public bool TrySpawn(OrbitPresentationSpawn spawn, float elapsedSeconds)
        {
            ThrowIfDisposed();
            if (!spawn.Key.IsValid || !IsAgeValid(elapsedSeconds) || orbs.ContainsKey(spawn.Key) ||
                !TryGetEmitter(spawn.WeaponId, out CirclingAttackBehaviour emitter)) return false;
            bool returnedDuringSpawn = false;
            AnimatedAttack attack = emitter.PlayPresentation(spawn, elapsedSeconds, returned =>
            {
                returnedDuringSpawn = true;
                if (orbs.TryGetValue(spawn.Key, out ActiveOrb orb) && orb.Attack == returned) orbs.Remove(spawn.Key);
            });
            if (attack == null || returnedDuringSpawn) return false;
            orbs.Add(spawn.Key, new ActiveOrb(spawn.WeaponId, emitter, attack));
            return true;
        }

        public bool TryHide(OrbitPresentationHiding hiding, float elapsedSeconds)
        {
            ThrowIfDisposed();
            return IsAgeValid(elapsedSeconds) && orbs.TryGetValue(hiding.Key, out ActiveOrb orb) &&
                orb.WeaponId == hiding.WeaponId && orb.Emitter != null &&
                orb.Emitter.ApplyPresentationHiding(hiding, elapsedSeconds);
        }

        public bool TryTerminate(OrbitPresentationTermination termination)
        {
            ThrowIfDisposed();
            if (!orbs.TryGetValue(termination.Key, out ActiveOrb orb) || orb.WeaponId != termination.WeaponId) return false;
            orbs.Remove(termination.Key);
            return orb.Emitter != null && orb.Emitter.TerminatePresentation(termination.Key);
        }

        public void Tick(float deltaTime)
        {
            ThrowIfDisposed();
            foreach (CirclingAttackBehaviour emitter in emitters.Values)
                if (emitter != null) emitter.TickPresentation(deltaTime);
        }

        private bool TryGetEmitter(uint weaponId, out CirclingAttackBehaviour emitter)
        {
            if (emitters.TryGetValue(weaponId, out emitter) && emitter != null) return true;
            if (!database.TryGetWeaponData(weaponId, out WeaponData data) || data == null ||
                !(data.WeaponPrefab is CirclingAttackBehaviour prefab)) { emitter = null; return false; }
            Transform parent = owner.AttacksParent != null ? owner.AttacksParent : owner.transform;
            var staging = new GameObject("Orbit Presentation Initialization");
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
            finally { UnityEngine.Object.Destroy(staging); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            orbs.Clear();
            foreach (CirclingAttackBehaviour emitter in emitters.Values)
            {
                if (emitter == null) continue;
                emitter.DisposePresentationReplica();
                UnityEngine.Object.Destroy(emitter.gameObject);
            }
            emitters.Clear();
        }

        private static bool IsAgeValid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
        private void ThrowIfDisposed()
        { if (disposed) throw new ObjectDisposedException(nameof(OrbitPresentationReplica)); }

        private readonly struct ActiveOrb
        {
            public ActiveOrb(uint weaponId, CirclingAttackBehaviour emitter, AnimatedAttack attack)
            { WeaponId = weaponId; Emitter = emitter; Attack = attack; }
            public uint WeaponId { get; }
            public CirclingAttackBehaviour Emitter { get; }
            public AnimatedAttack Attack { get; }
        }
    }
}
