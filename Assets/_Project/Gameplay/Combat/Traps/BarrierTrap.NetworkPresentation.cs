using System;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Traps
{
    public enum BarrierPhase : byte { Framing, Building, Shrinking, Stopping, Complete }

    public partial class BarrierTrap
    {
        private bool networkAuthority, networkReplica;
        private float networkEntryScale;
        private int networkReplicaGroups;
        private Action<bool> networkEntryEffects;
        public BarrierPhase NetworkPhase { get; private set; }
        public int NetworkVisibleGroups { get; private set; }
        public int NetworkGroupCount => _allParticleSystems?.Count ?? 0;
        public float NetworkInnerRadius => _currentRadius - _collider.edgeRadius;
        public bool NetworkCollisionEnabled => _collider != null && _collider.enabled;

        // The existing trap executes its original coroutine only on the authority.
        // Networking supplies the target and entry effects; no Mirror dependency here.
        public void ConfigureNetworkAuthority(Vector3 center, float entryScale, Action<bool> entryEffects)
        {
            networkAuthority = true; networkReplica = false;
            networkEntryScale = entryScale; networkEntryEffects = entryEffects;
            transform.position = center; targetPlayer = false; target = transform;
            _collider.enabled = false;
        }

        public void ApplyNetworkPresentation(Vector3 center, BarrierPhase phase, float innerRadius,
            int groups, int visibleGroups, float entryScale, bool collisionEnabled)
        {
            if (networkAuthority) throw new InvalidOperationException("Authority cannot also execute a barrier replica.");
            networkReplica = true; transform.position = center;
            trapTransform.rotation = applyIsometricRotation ? Quaternion.Euler(45, 0, 0) : Quaternion.identity;
            if (_particleSystemPooler == null) _particleSystemPooler = PoolManager.Instance.GetOrCreatePooler(particleSystem, 200);
            if (_points == null && groups > 0) GenerateCollider();
            _currentRadius = innerRadius + _collider.edgeRadius;
            if (_points != null) UpdateColliderPoints(_currentRadius);
            if (groups > 0 && groups != NetworkGroupCount)
            {
                StopAllParticleSystems();
                // Native ring count changes at its five-second rearrangement, not each frame.
                networkReplicaGroups = groups;
                CreateParticleSystems();
            }
            for (int i = 0; i < NetworkGroupCount; i++)
            {
                var root = _allParticleSystems[i][0];
                float angle = 2 * Mathf.PI * i / NetworkGroupCount;
                root.transform.localPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * innerRadius;
                foreach (var ps in _allParticleSystems[i])
                {
                    var main = ps.main; main.simulationSpeed = phase == BarrierPhase.Building ? 1 / entryScale : 1;
                }
                if (phase == BarrierPhase.Stopping || phase == BarrierPhase.Complete)
                { if (NetworkPhase != phase) root.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
                else if (i < visibleGroups || phase == BarrierPhase.Shrinking)
                { if (!root.isPlaying) root.Play(true); }
                else root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            _collider.enabled = collisionEnabled;
            NetworkPhase = phase; NetworkVisibleGroups = visibleGroups;
        }

        public void CancelNetworkLifecycle()
        {
            StopAllCoroutines(); _inOutCoroutine = null;
            if (networkAuthority) networkEntryEffects?.Invoke(false);
            networkEntryEffects = null;
            if (_collider != null) _collider.enabled = false;
            if (_allParticleSystems != null)
                foreach (var systems in _allParticleSystems)
                    systems[0].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            StopAllParticleSystems(); NetworkPhase = BarrierPhase.Complete;
            onTrapEnd = null; onSpawnFinished = null;
        }
    }
}
