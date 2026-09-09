using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>One local view per pet identity; two slots with the same definition still have separate views.</summary>
    public sealed class SummonPresentationReplica : IDisposable
    {
        private readonly PlayerMovement owner;
        private readonly RuntimeDB database;
        private readonly Dictionary<ulong, SummonAttackBehaviour> emitters = new Dictionary<ulong, SummonAttackBehaviour>();
        private bool disposed;
        public int ActivePetCount
        {
            get
            {
                int count = 0;
                foreach (var emitter in emitters.Values)
                    if (emitter != null && emitter.ActiveSummon != null) count++;
                return count;
            }
        }
        public SummonPresentationReplica(PlayerMovement owner, RuntimeDB database)
        {
            this.owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            this.database = database != null ? database : throw new ArgumentNullException(nameof(database));
        }
        public bool TryApplyState(SummonPresentationState state, float age)
        {
            ThrowIfDisposed();
            if (!state.IsValid || !ValidAge(age)) return false;
            if (emitters.TryGetValue(state.PetId, out var existing))
                return existing != null && existing.ID == state.WeaponId && existing.ApplyPresentationState(state, age);
            if (!database.TryGetWeaponData(state.WeaponId, out var data) || data == null ||
                !(data.WeaponPrefab is SummonAttackBehaviour prefab)) return false;
            SummonAttackBehaviour emitter = null;
            var staging = new GameObject("Summon Presentation Initialization");
            staging.SetActive(false);
            try
            {
                emitter = UnityEngine.Object.Instantiate(prefab, staging.transform);
                emitter.gameObject.SetActive(false);
                emitter.name = $"{prefab.name} (Remote Presentation)";
                emitter.transform.SetParent(owner.AttacksParent != null ? owner.AttacksParent : owner.transform, false);
                emitter.InitializePresentationReplica(state.WeaponId, owner);
                emitter.enabled = false;
                emitter.gameObject.SetActive(true);
                if (!emitter.ApplyPresentationState(state, age))
                { emitter.DisposePresentationReplica(); UnityEngine.Object.Destroy(emitter.gameObject); return false; }
                emitters.Add(state.PetId, emitter);
                return true;
            }
            catch
            {
                if (emitter != null) { emitter.DisposePresentationReplica(); UnityEngine.Object.Destroy(emitter.gameObject); }
                throw;
            }
            finally { UnityEngine.Object.Destroy(staging); }
        }
        public bool TryApplyPose(SummonPresentationPose pose)
        {
            ThrowIfDisposed();
            return emitters.TryGetValue(pose.PetId, out var emitter) && emitter != null &&
                emitter.ID == pose.WeaponId && emitter.ApplyPresentationPose(pose);
        }
        public bool TryTerminate(uint weaponId, ulong petId)
        {
            ThrowIfDisposed();
            if (!emitters.TryGetValue(petId, out var emitter) || emitter == null || emitter.ID != weaponId) return false;
            emitters.Remove(petId);
            emitter.DisposePresentationReplica();
            UnityEngine.Object.Destroy(emitter.gameObject);
            return true;
        }
        public void Tick(float deltaTime)
        {
            ThrowIfDisposed();
            foreach (var emitter in emitters.Values) if (emitter != null) emitter.TickPresentation(deltaTime);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var emitter in emitters.Values)
            {
                if (emitter == null) continue;
                emitter.DisposePresentationReplica();
                UnityEngine.Object.Destroy(emitter.gameObject);
            }
            emitters.Clear();
        }
        private static bool ValidAge(float age) => !float.IsNaN(age) && !float.IsInfinity(age) && age >= 0;
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(SummonPresentationReplica)); }
    }
}
