using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Local beam animations, with independent directions per attack root and no GAS simulation.</summary>
    public sealed class BeamPresentationReplica : IDisposable
    {
        private readonly PlayerMovement owner;
        private readonly RuntimeDB database;
        private readonly Dictionary<uint, PlayerBeamAttackBehaviour> emitters = new Dictionary<uint, PlayerBeamAttackBehaviour>();
        private readonly Dictionary<BeamPresentationKey, ActiveBeam> beams = new Dictionary<BeamPresentationKey, ActiveBeam>();
        private bool disposed;
        public int ActiveBeamCount => beams.Count;

        public BeamPresentationReplica(PlayerMovement owner, RuntimeDB database)
        {
            this.owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            this.database = database != null ? database : throw new ArgumentNullException(nameof(database));
        }

        public bool TrySpawn(BeamPresentationSpawn spawn, float elapsedSeconds)
        {
            ThrowIfDisposed();
            if (!spawn.Key.IsValid || float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) || elapsedSeconds < 0 ||
                beams.ContainsKey(spawn.Key) || !TryGetEmitter(spawn.WeaponId, out PlayerBeamAttackBehaviour emitter)) return false;
            bool returnedDuringSpawn = false;
            AnimatedAttack attack = emitter.PlayPresentation(spawn, elapsedSeconds, returned =>
            {
                returnedDuringSpawn = true;
                if (beams.TryGetValue(spawn.Key, out ActiveBeam beam) && beam.Attack == returned) beams.Remove(spawn.Key);
            });
            if (attack == null || returnedDuringSpawn) return false;
            beams.Add(spawn.Key, new ActiveBeam(spawn.WeaponId, emitter, attack));
            return true;
        }

        public bool TryAim(BeamPresentationAim aim)
        {
            ThrowIfDisposed();
            return emitters.TryGetValue(aim.WeaponId, out PlayerBeamAttackBehaviour emitter) && emitter != null &&
                emitter.ApplyPresentationAim(aim);
        }

        public bool TryTerminate(BeamPresentationTermination termination)
        {
            ThrowIfDisposed();
            if (!beams.TryGetValue(termination.Key, out ActiveBeam beam) || beam.WeaponId != termination.WeaponId) return false;
            beams.Remove(termination.Key);
            return beam.Emitter != null && beam.Emitter.TerminatePresentation(termination.Key);
        }

        public void Tick(float deltaTime)
        {
            ThrowIfDisposed();
            foreach (PlayerBeamAttackBehaviour emitter in emitters.Values)
                if (emitter != null) emitter.TickPresentation(deltaTime);
        }

        private bool TryGetEmitter(uint weaponId, out PlayerBeamAttackBehaviour emitter)
        {
            if (emitters.TryGetValue(weaponId, out emitter) && emitter != null) return true;
            if (!database.TryGetWeaponData(weaponId, out WeaponData data) || data == null ||
                !(data.WeaponPrefab is PlayerBeamAttackBehaviour prefab)) { emitter = null; return false; }
            Transform parent = owner.AttacksParent != null ? owner.AttacksParent : owner.transform;
            var staging = new GameObject("Beam Presentation Initialization");
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
            beams.Clear();
            foreach (PlayerBeamAttackBehaviour emitter in emitters.Values)
            {
                if (emitter == null) continue;
                emitter.DisposePresentationReplica();
                UnityEngine.Object.Destroy(emitter.gameObject);
            }
            emitters.Clear();
        }

        private void ThrowIfDisposed()
        { if (disposed) throw new ObjectDisposedException(nameof(BeamPresentationReplica)); }

        private readonly struct ActiveBeam
        {
            public ActiveBeam(uint weaponId, PlayerBeamAttackBehaviour emitter, AnimatedAttack attack)
            { WeaponId = weaponId; Emitter = emitter; Attack = attack; }
            public uint WeaponId { get; }
            public PlayerBeamAttackBehaviour Emitter { get; }
            public AnimatedAttack Attack { get; }
        }
    }
}
