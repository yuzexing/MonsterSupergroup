using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public class DashAttackBehaviour : WeaponBehaviour
    {
        [Header("Attack Settings")]
        [SerializeField] private BasePlayerAttackVariants variants;
        private readonly Dictionary<MultiParticlePlayerTrailAttack, TrailInstance> _trails =
            new Dictionary<MultiParticlePlayerTrailAttack, TrailInstance>();
        private Transform _checkoutRoot;
        private DashTrailLifetimeAnchor _lifetimeAnchor;
        private PlayerMovement _subscribedOwner;
        private ulong _lastDashUseId;
        private uint _generation;
        private bool _spawning;
        private bool _cancelling;
        private bool _presentationReplicaInitialized;

        public event Action<TrailPresentationSpawn> PresentationSpawned;
        public event Action<TrailPresentationPoint> PresentationPointAdded;
        public event Action<TrailPresentationSamplingEnded> PresentationSamplingEnded;
        public event Action<TrailPresentationTermination> PresentationTerminated;
        public int ActiveTrailCount => _trails.Count;
        /// <summary>Assigned before NativeAttackStarted so admission sees the committed dash.</summary>
        public ulong CurrentDashUseId { get; private set; }

        public override void Init(uint id, AttackStats stats) => base.Init(id, stats);
        public override void InitNative(uint id)
        {
            Dispose();
            base.InitNative(id);
            InitializePool();
            _presentationReplicaInitialized = false;
            _lastDashUseId = 0;
            _subscribedOwner = OwnerPlayer;
            _subscribedOwner.OnDashStart += Attack;
        }

        // Dash admission owns resource recovery. This legacy display value is never an attack gate.
        public override float GetCooldown() => 1f;
        public override float GetAttackSequenceDuration() => DurationValue;

        private void InitializePool()
        {
            if (variants == null || !(variants.GetPrefab(AttackElement.Default) is MultiParticlePlayerTrailAttack))
                throw new InvalidOperationException("Native Dash requires a MultiParticlePlayerTrailAttack variant.");
            variants.Init();
            if (_checkoutRoot == null)
            {
                var checkout = new GameObject("Trail Pool Checkout");
                checkout.SetActive(false);
                checkout.transform.SetParent(transform, false);
                _checkoutRoot = checkout.transform;
            }
            if (_lifetimeAnchor == null)
            {
                var anchor = new GameObject("Trail Lifetime");
                anchor.transform.SetParent(transform, false);
                _lifetimeAnchor = anchor.AddComponent<DashTrailLifetimeAnchor>();
                _lifetimeAnchor.Deactivated = CancelTrails;
            }
        }

        public override void Attack()
        {
            ulong useId = OwnerPlayer != null ? OwnerPlayer.CurrentDashUseId : 0;
            if (!gameObject.activeInHierarchy || _presentationReplicaInitialized || _spawning || _cancelling ||
                !CanAttack || NativeRuntime == null || !NativeRuntime.IsInitialized || useId == 0 || useId <= _lastDashUseId) return;
            _spawning = true;
            _lastDashUseId = useId;
            CurrentDashUseId = useId;
            uint generation = _generation;
            try
            {
                using (AttackSnapshot snapshot = BeginNativeGasAttack())
                {
                    if (Cancelled(generation)) return;
                    AttackElement element = variants.ResolveElement(ActiveElement);
                    var spawn = new TrailPresentationSpawn(ID, snapshot.Context.EventId.Value, useId, element,
                        OwnerPlayer.CurrentPosition, snapshot.Stats.Duration, snapshot.Stats.Duration / 2f,
                        snapshot.Stats.Speed, ProjectilePresentationStats.From(snapshot.Stats, 0f));
                    if (!spawn.IsValid) throw new InvalidOperationException("Invalid frozen Dash trail parameters.");
                    MultiParticlePlayerTrailAttack trail = Checkout(spawn, null);
                    trail.InitNative(this, snapshot, null, () => ReturnTrail(trail));
                    trail.ConfigureTrail(spawn);
                    Subscribe(trail);
                    PresentationSpawned?.Invoke(spawn);
                    if (Cancelled(generation) || !_trails.ContainsKey(trail)) return;
                    trail.gameObject.SetActive(true);
                    if (Cancelled(generation) || !_trails.ContainsKey(trail)) return;
                    trail.Attack();
                }
            }
            catch { CancelDashUse(useId); throw; }
            finally { _spawning = false; }
        }

        private bool Cancelled(uint generation)
        {
            if (!gameObject.activeInHierarchy) { CancelTrails(); return true; }
            return _generation != generation;
        }

        private MultiParticlePlayerTrailAttack Checkout(TrailPresentationSpawn spawn,
            Action<MultiParticlePlayerTrailAttack> onReturned)
        {
            if (!(variants.GetPrefab(spawn.Element) is MultiParticlePlayerTrailAttack prefab))
                throw new InvalidOperationException("Dash trail variant is not supported by Native GAS.");
            var trail = (MultiParticlePlayerTrailAttack)variants.GetOrCreate(spawn.Element, _checkoutRoot);
            trail.gameObject.SetActive(false);
            // A world trail must not become a compound collider of the player's Rigidbody2D.
            trail.transform.SetParent(null, false);
            trail.transform.SetPositionAndRotation(spawn.Origin, prefab.transform.rotation);
            trail.transform.localScale = prefab.transform.localScale;
            _trails.Add(trail, new TrailInstance(spawn, onReturned));
            return trail;
        }

        private void Subscribe(MultiParticlePlayerTrailAttack trail)
        {
            trail.Deactivated += HandleDeactivated;
            trail.PointAdded += HandlePoint;
            trail.SamplingEnded += HandleSamplingEnded;
        }
        private void HandlePoint(TrailPresentationPoint point) => PresentationPointAdded?.Invoke(point);
        private void HandleSamplingEnded(TrailPresentationSamplingEnded end) => PresentationSamplingEnded?.Invoke(end);
        private void HandleDeactivated(MultiParticlePlayerTrailAttack trail)
        {
            ReturnTrail(trail, true);
            if (!gameObject.activeInHierarchy) CancelTrails();
        }

        private void ReturnTrail(MultiParticlePlayerTrailAttack trail, bool externallyDeactivated = false)
        {
            if (!_trails.TryGetValue(trail, out TrailInstance instance)) return;
            _trails.Remove(trail);
            if (trail != null)
            {
                trail.Deactivated -= HandleDeactivated;
                trail.PointAdded -= HandlePoint;
                trail.SamplingEnded -= HandleSamplingEnded;
                trail.Dispose();
                if (externallyDeactivated) { variants.Discard(trail); Destroy(trail.gameObject); }
                else variants.Return(trail);
            }
            if (_presentationReplicaInitialized) instance.OnReturned?.Invoke(trail);
            else PresentationTerminated?.Invoke(new TrailPresentationTermination(ID, instance.Spawn.AttackEventId));
        }

        public void CancelDashUse(ulong useId)
        {
            if (useId == 0) return;
            // Rejection can arrive inside NativeAttackStarted, before checkout has created a body.
            if (_spawning && CurrentDashUseId == useId) _generation++;
            foreach (var trail in new List<MultiParticlePlayerTrailAttack>(_trails.Keys))
                if (_trails.TryGetValue(trail, out TrailInstance instance) && instance.Spawn.DashUseId == useId)
                    ReturnTrail(trail, !gameObject.activeInHierarchy);
        }

        public void InitializePresentationReplica(uint weaponId, PlayerMovement owner)
        {
            if (_presentationReplicaInitialized) return;
            Dispose();
            ConfigureOwner(owner);
            _id = weaponId;
            InitializePool();
            _presentationReplicaInitialized = true;
            enabled = false;
        }

        public MultiParticlePlayerTrailAttack PlayPresentation(TrailPresentationSpawn spawn, float age,
            Action<MultiParticlePlayerTrailAttack> onReturned = null)
        {
            if (!_presentationReplicaInitialized || spawn.WeaponId != ID)
                throw new InvalidOperationException("Trail replica is not initialized for this weapon.");
            if (!gameObject.activeInHierarchy || _cancelling || !spawn.IsValid ||
                !TrailPresentationSpawn.IsFinite(age) || age < 0) return null;
            foreach (TrailInstance instance in _trails.Values)
                if (instance.Spawn.AttackEventId == spawn.AttackEventId) return null;
            MultiParticlePlayerTrailAttack trail = Checkout(spawn, onReturned);
            try
            {
                trail.InitPresentation(this, spawn.Stats, null, () => ReturnTrail(trail));
                trail.ConfigureTrail(spawn);
                Subscribe(trail);
                trail.gameObject.SetActive(true);
                if (!_trails.ContainsKey(trail)) return null;
                trail.PlayPresentation(age);
                return _trails.ContainsKey(trail) ? trail : null;
            }
            catch { ReturnTrail(trail); throw; }
        }

        public bool ApplyPresentationPoint(TrailPresentationPoint point, float age)
        {
            if (!_presentationReplicaInitialized || point.WeaponId != ID) return false;
            foreach (var entry in _trails)
                if (entry.Value.Spawn.AttackEventId == point.AttackEventId) return entry.Key.ApplyPresentationPoint(point, age);
            return false;
        }

        public bool ApplyPresentationSamplingEnded(TrailPresentationSamplingEnded end, float age)
        {
            if (!_presentationReplicaInitialized || end.WeaponId != ID) return false;
            foreach (var entry in _trails)
                if (entry.Value.Spawn.AttackEventId == end.AttackEventId) return entry.Key.ApplyPresentationSamplingEnded(end, age);
            return false;
        }

        public bool TerminatePresentation(ulong attackEventId)
        {
            if (!_presentationReplicaInitialized) return false;
            foreach (var entry in _trails)
                if (entry.Value.Spawn.AttackEventId == attackEventId) { ReturnTrail(entry.Key); return true; }
            return false;
        }

        public void DisposePresentationReplica()
        {
            if (!_presentationReplicaInitialized) return;
            CancelTrails();
            _presentationReplicaInitialized = false;
        }

        private void CancelTrails()
        {
            if (_cancelling) return;
            _cancelling = true;
            try
            {
                _generation++;
                foreach (var trail in new List<MultiParticlePlayerTrailAttack>(_trails.Keys))
                    ReturnTrail(trail, !gameObject.activeInHierarchy);
            }
            finally { _cancelling = false; }
        }
        private void OnDisable() { if (!gameObject.activeInHierarchy) CancelTrails(); }
        private void OnDestroy() => Dispose();
        protected override void Dispose()
        {
            if (_subscribedOwner != null) _subscribedOwner.OnDashStart -= Attack;
            _subscribedOwner = null;
            CancelTrails();
        }

        private sealed class TrailInstance
        {
            public TrailInstance(TrailPresentationSpawn spawn, Action<MultiParticlePlayerTrailAttack> onReturned)
            { Spawn = spawn; OnReturned = onReturned; }
            public TrailPresentationSpawn Spawn { get; }
            public Action<MultiParticlePlayerTrailAttack> OnReturned { get; }
        }
    }

    // Unlike a disabled WeaponBehaviour, this active child receives parent deactivation.
    // It owns no simulation state; the detached world trail remains owned by its weapon.
    internal sealed class DashTrailLifetimeAnchor : MonoBehaviour
    {
        public Action Deactivated;
        private void OnDisable() => Deactivated?.Invoke();
        private void OnDestroy() { Deactivated?.Invoke(); Deactivated = null; }
    }
}
