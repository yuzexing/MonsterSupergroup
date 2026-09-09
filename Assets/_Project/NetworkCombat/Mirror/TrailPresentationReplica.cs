using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Replays the Owner's sampled world points using the authored particle assets.</summary>
    public sealed class TrailPresentationReplica : IDisposable
    {
        private readonly PlayerMovement owner;
        private readonly RuntimeDB database;
        private readonly Dictionary<uint, DashAttackBehaviour> emitters = new Dictionary<uint, DashAttackBehaviour>();
        private readonly Dictionary<ulong, ActiveTrail> trails = new Dictionary<ulong, ActiveTrail>();
        private bool disposed;
        public int ActiveTrailCount => trails.Count;
        public TrailPresentationReplica(PlayerMovement owner, RuntimeDB database)
        {
            this.owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            this.database = database != null ? database : throw new ArgumentNullException(nameof(database));
        }
        public bool TrySpawn(TrailPresentationSpawn spawn, float age)
        {
            ThrowIfDisposed();
            if (!spawn.IsValid || !ValidAge(age) || trails.ContainsKey(spawn.AttackEventId) ||
                !TryGetEmitter(spawn.WeaponId, out DashAttackBehaviour emitter)) return false;
            bool returnedDuringSpawn = false;
            MultiParticlePlayerTrailAttack attack = emitter.PlayPresentation(spawn, age, returned =>
            {
                returnedDuringSpawn = true;
                if (trails.TryGetValue(spawn.AttackEventId, out ActiveTrail active) && active.Attack == returned)
                    trails.Remove(spawn.AttackEventId);
            });
            if (attack == null || returnedDuringSpawn) return false;
            trails.Add(spawn.AttackEventId, new ActiveTrail(spawn.WeaponId, emitter, attack));
            return true;
        }
        public bool TryPoint(TrailPresentationPoint point, float age)
        {
            ThrowIfDisposed();
            return ValidAge(age) && trails.TryGetValue(point.AttackEventId, out ActiveTrail trail) &&
                trail.WeaponId == point.WeaponId && trail.Emitter.ApplyPresentationPoint(point, age);
        }
        public bool TryEndSampling(TrailPresentationSamplingEnded end, float age)
        {
            ThrowIfDisposed();
            return ValidAge(age) && trails.TryGetValue(end.AttackEventId, out ActiveTrail trail) &&
                trail.WeaponId == end.WeaponId && trail.Emitter.ApplyPresentationSamplingEnded(end, age);
        }
        public bool TryTerminate(TrailPresentationTermination end)
        {
            ThrowIfDisposed();
            if (!trails.TryGetValue(end.AttackEventId, out ActiveTrail trail) || trail.WeaponId != end.WeaponId) return false;
            trails.Remove(end.AttackEventId);
            return trail.Emitter != null && trail.Emitter.TerminatePresentation(end.AttackEventId);
        }
        private bool TryGetEmitter(uint weaponId, out DashAttackBehaviour emitter)
        {
            if (emitters.TryGetValue(weaponId, out emitter) && emitter != null) return true;
            if (!database.TryGetWeaponData(weaponId, out WeaponData data) || data == null ||
                !(data.WeaponPrefab is DashAttackBehaviour prefab)) { emitter = null; return false; }
            var staging = new GameObject("Trail Presentation Initialization");
            staging.SetActive(false);
            try
            {
                emitter = UnityEngine.Object.Instantiate(prefab, staging.transform);
                emitter.gameObject.SetActive(false);
                emitter.name = $"{prefab.name} (Remote Presentation)";
                emitter.transform.SetParent(owner.AttacksParent != null ? owner.AttacksParent : owner.transform, false);
                emitter.InitializePresentationReplica(weaponId, owner);
                emitter.enabled = false;
                emitter.gameObject.SetActive(true);
                emitters[weaponId] = emitter;
                return true;
            }
            catch { if (emitter != null) UnityEngine.Object.Destroy(emitter.gameObject); throw; }
            finally { UnityEngine.Object.Destroy(staging); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            trails.Clear();
            foreach (DashAttackBehaviour emitter in emitters.Values)
            {
                if (emitter == null) continue;
                emitter.DisposePresentationReplica();
                UnityEngine.Object.Destroy(emitter.gameObject);
            }
            emitters.Clear();
        }
        private static bool ValidAge(float age) => !float.IsNaN(age) && !float.IsInfinity(age) && age >= 0;
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(TrailPresentationReplica)); }
        private readonly struct ActiveTrail
        {
            public readonly uint WeaponId;
            public readonly DashAttackBehaviour Emitter;
            public readonly MultiParticlePlayerTrailAttack Attack;
            public ActiveTrail(uint weaponId, DashAttackBehaviour emitter, MultiParticlePlayerTrailAttack attack)
            { WeaponId = weaponId; Emitter = emitter; Attack = attack; }
        }
    }
}
