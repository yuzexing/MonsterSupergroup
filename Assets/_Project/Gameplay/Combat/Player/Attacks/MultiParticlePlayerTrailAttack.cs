using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using FMOD.Studio;
using FMODUnity;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public class MultiParticlePlayerTrailAttack : BasePlayerAttack
    {
        private struct TrailParticleData
        {
            public ParticleSystem Particle;
            public Vector2 WorldPosition;
            public float StartTime;
            public bool Stopped;
        }

        [SerializeField] private ParticleSystem trailParticles;
        [SerializeField] private ParticleSystem trailStepParticles;
        [SerializeField] private EdgeCollider2D edgeCollider;
        [SerializeField] private float trailDelta = 0.5f;
        [SerializeField] private EventReference soundEvent;

        private readonly List<TrailParticleData> _particles = new List<TrailParticleData>();
        private readonly List<Vector2> _colliderPoints = new List<Vector2>();
        private GenericPooler<ParticleSystem> _pooler;
        private PlayerMovement _player;
        private TrailPresentationSpawn _spawn;
        private Vector2 _lastParticlePosition;
        private Quaternion _worldRotation;
        private float _attackStart;
        private float _attackStartCache;
        private float _trailParticleDuration = -1f;
        private float _segmentDuration;
        private float _lastPointElapsed = -1f;
        private float _nextParticleEndCheck;
        private int _firstLivePoint;
        private uint _nextPointIndex;
        private uint _generation;
        private bool _initialized;
        private bool _configured;
        private bool _playing;
        private bool _samplingEnded;
        private EventInstance _soundInstance;

        public Action onAttackDurationFinished;
        public Transform trailStart { get; set; }
        public event Action<MultiParticlePlayerTrailAttack> Deactivated;
        public event Action<TrailPresentationPoint> PointAdded;
        public event Action<TrailPresentationSamplingEnded> SamplingEnded;
        public int ActiveSegmentCount => _particles.Count - _firstLivePoint;
        public int ParticleInstanceCount => _particles.Count;
        public bool IsSampling => _playing && !_samplingEnded;
        public TrailPresentationSpawn PresentationSpawn => _spawn;

        public override void InitNative(WeaponBehaviour behaviour, AttackSnapshot attack, Action onStart = null, Action onEnd = null)
        {
            Dispose();
            base.InitNative(behaviour, attack, onStart, onEnd);
            try { InitializeTrail(behaviour); }
            catch { Dispose(); throw; }
        }

        public override void InitPresentation(WeaponBehaviour behaviour, ProjectilePresentationStats stats,
            Action onStart = null, Action onEnd = null)
        {
            Dispose();
            base.InitPresentation(behaviour, stats, onStart, onEnd);
            try { InitializeTrail(behaviour); }
            catch { Dispose(); throw; }
        }

        private void InitializeTrail(WeaponBehaviour behaviour)
        {
            if (trailParticles == null || edgeCollider == null || !(hitbox is PlayerAttackOvertimeHitBox))
                throw new InvalidOperationException("Trail requires its authored particle system, edge collider, and overtime hitbox.");
            _player = behaviour.OwnerPlayer;
            if (_player == null) throw new InvalidOperationException("Trail requires an explicitly configured owner.");
            _pooler = PoolManager.Instance.GetOrCreatePooler(trailParticles);
            _initialized = true;
            trailStart = _player.transform;
            edgeCollider.enabled = false;
            edgeCollider.points = Array.Empty<Vector2>();
            hitbox.enabled = !IsPresentationOnly;
            if (IsPresentationOnly) hitbox.Toggle(false);
        }

        public void ConfigureTrail(TrailPresentationSpawn spawn)
        {
            if (!_initialized || !spawn.IsValid) throw new InvalidOperationException("Invalid trail initialization.");
            _spawn = spawn;
            _segmentDuration = _trailParticleDuration < 0f ? spawn.SegmentDuration : _trailParticleDuration;
            _worldRotation = transform.rotation;
            transform.position = spawn.Origin;
            _lastParticlePosition = spawn.Origin;
            // Source Init intentionally uses SpeedValue seconds, not 1 / SpeedValue.
            ((PlayerAttackOvertimeHitBox)hitbox).SetHitInterval(spawn.HitInterval);
            _configured = true;
        }

        public override void Attack()
        {
            if (!_initialized || !_configured || IsPresentationOnly || _playing) return;
            BeginPlaying(0f);
            Tick();
        }

        public void PlayPresentation(float age)
        {
            if (!_initialized || !_configured || !IsPresentationOnly || _playing ||
                !TrailPresentationSpawn.IsFinite(age) || age < 0) return;
            BeginPlaying(age);
            // The reliable end edge, not a local clock, closes sampling. Later point messages
            // from the same reliable batch must still be able to populate an initially empty trail.
        }

        private void BeginPlaying(float age)
        {
            _attackStart = Time.time - age;
            _attackStartCache = _attackStart;
            _playing = true;
            _samplingEnded = false;
            _nextParticleEndCheck = 0f;
            if (trailStepParticles != null)
            {
                MoveStepsToOwner();
                trailStepParticles.gameObject.SetActive(true);
                trailStepParticles.Clear(true);
                trailStepParticles.Play(true);
            }
            if (!soundEvent.IsNull)
            {
                try
                {
                    _soundInstance = OptionalAudio.CreateInstance(soundEvent);
                    if (_soundInstance.isValid())
                    {
                        OptionalAudio.AttachInstanceToGameObject(_soundInstance, _player.transform);
                        _soundInstance.start();
                    }
                }
                // The source event reference remains intact when its bank is unavailable.
                // Audio failure must not cancel an admitted gameplay root or its replica.
                catch (FMODUnity.EventNotFoundException error) { Debug.LogException(error, this); }
            }
            _onStart?.Invoke();
        }

        private void Update() => Tick();

        private void FixedUpdate()
        {
            if (!_playing) return;
            AnchorWorldGeometry();
            UpdateCollider();
        }

        private void Tick()
        {
            if (!_playing) return;
            uint generation = _generation;
            if (_behaviour == null || !_behaviour.gameObject.activeInHierarchy)
            {
                End();
                return;
            }
            AnchorWorldGeometry();
            MoveStepsToOwner();
            float elapsed = Time.time - _attackStart;
            if (!IsPresentationOnly && !_samplingEnded)
            {
                if (elapsed < _spawn.SamplingDuration)
                {
                    if (trailStart != null)
                    {
                        Vector2 position = trailStart.position;
                        Vector2 delta = position - _lastParticlePosition;
                        // Preserve the source's isometric projection, strict distance test,
                        // and at most one sample per frame (no catch-up/interpolation).
                        if (new Vector2(delta.x - delta.y, (delta.y + delta.x) * 0.5f).magnitude > trailDelta)
                        {
                            delta.y *= 0.5f;
                            delta.Normalize();
                            Vector2 point = _lastParticlePosition + delta * trailDelta;
                            if (TrailPresentationSpawn.IsFinite(point.x) && TrailPresentationSpawn.IsFinite(point.y))
                            {
                                var message = new TrailPresentationPoint(_spawn.WeaponId, _spawn.AttackEventId,
                                    _nextPointIndex, point, elapsed);
                                AddPoint(message, 0f);
                                PointAdded?.Invoke(message);
                                if (!_playing || generation != _generation) return;
                            }
                        }
                    }
                }
                else
                {
                    FinishSampling(elapsed);
                    if (!_playing || generation != _generation) return;
                }
            }
            // Source retires only the oldest live point per frame, using a strict > comparison.
            if (_firstLivePoint < _particles.Count)
            {
                TrailParticleData oldest = _particles[_firstLivePoint];
                if (Time.time - oldest.StartTime > _segmentDuration)
                {
                    StopParticle(ref oldest);
                    _particles[_firstLivePoint] = oldest;
                    _firstLivePoint++;
                }
            }
            UpdateCollider();
            TryEnd();
        }

        private void AnchorWorldGeometry()
        {
            // Neither the collision trail nor its detached particles follow player movement.
            transform.SetPositionAndRotation(_spawn.Origin, _worldRotation);
        }

        private void MoveStepsToOwner()
        {
            if (trailStepParticles != null && _player != null)
                trailStepParticles.transform.SetPositionAndRotation(_player.transform.position, _player.transform.rotation);
        }

        private void AddPoint(TrailPresentationPoint point, float age)
        {
            ParticleSystem particle = _pooler.GetOrCreate(null, activate: true);
            particle.gameObject.SetActive(true);
            particle.transform.position = point.WorldPosition;
            particle.Clear(true);
            particle.Play(true);
            var data = new TrailParticleData
            {
                Particle = particle, WorldPosition = point.WorldPosition,
                StartTime = Time.time - age
            };
            if (IsPresentationOnly)
            {
                particle.Simulate(Mathf.Min(age, _segmentDuration), true, true);
                if (age > _segmentDuration)
                {
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    particle.Simulate(age - _segmentDuration, true, false);
                    ResumeStoppedTail(particle);
                    data.Stopped = true;
                }
                else particle.Play(true);
            }
            _particles.Add(data);
            _lastParticlePosition = point.WorldPosition;
            _lastPointElapsed = point.ElapsedSeconds;
            _nextPointIndex++;
            UpdateCollider();
        }

        private static void ResumeStoppedTail(ParticleSystem root)
        {
            // Simulate leaves even empty looping containers paused and IsAlive returns true.
            // Stop alone does not unpause children which still contain live particles.
            // Resume only those particles, then stop emission again without clearing them.
            // Subsequent updates use the same natural particle clock as the source trail.
            foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particle.particleCount > 0) particle.Play(false);
                particle.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        public bool ApplyPresentationPoint(TrailPresentationPoint point, float age)
        {
            if (!IsPresentationOnly || !_playing || _samplingEnded || point.WeaponId != _spawn.WeaponId ||
                point.AttackEventId != _spawn.AttackEventId || point.PointIndex != _nextPointIndex ||
                !TrailPresentationSpawn.IsFinite(age) || age < 0 ||
                !TrailPresentationSpawn.IsFinite(point.WorldPosition.x) || !TrailPresentationSpawn.IsFinite(point.WorldPosition.y) ||
                !TrailPresentationSpawn.IsFinite(point.ElapsedSeconds) || point.ElapsedSeconds < 0 ||
                point.ElapsedSeconds >= _spawn.SamplingDuration || point.ElapsedSeconds < _lastPointElapsed) return false;
            AddPoint(point, age);
            return true;
        }

        public bool ApplyPresentationSamplingEnded(TrailPresentationSamplingEnded end, float age)
        {
            if (!IsPresentationOnly || !_playing || _samplingEnded || end.WeaponId != _spawn.WeaponId ||
                end.AttackEventId != _spawn.AttackEventId || !TrailPresentationSpawn.IsFinite(age) || age < 0 ||
                !TrailPresentationSpawn.IsFinite(end.SamplingElapsedSeconds) ||
                end.SamplingElapsedSeconds < _spawn.SamplingDuration || end.SamplingElapsedSeconds < _lastPointElapsed) return false;
            FinishSampling(end.SamplingElapsedSeconds);
            if (_playing) TryEnd();
            return true;
        }

        private void FinishSampling(float elapsed)
        {
            if (_samplingEnded) return;
            _samplingEnded = true;
            uint generation = _generation;
            if (!IsPresentationOnly)
                SamplingEnded?.Invoke(new TrailPresentationSamplingEnded(_spawn.WeaponId, _spawn.AttackEventId, elapsed));
            if (_playing && generation == _generation) onAttackDurationFinished?.Invoke();
        }

        private void UpdateCollider()
        {
            if (edgeCollider == null) return;
            if (IsPresentationOnly) { edgeCollider.enabled = false; return; }
            _colliderPoints.Clear();
            for (int index = _firstLivePoint; index < _particles.Count; index++)
                _colliderPoints.Add(transform.InverseTransformPoint(_particles[index].WorldPosition));
            edgeCollider.points = _colliderPoints.Count == 1
                ? new[] { _colliderPoints[0], _colliderPoints[0] } : _colliderPoints.ToArray();
            edgeCollider.enabled = _playing && _colliderPoints.Count > 0;
        }

        private static void StopParticle(ref TrailParticleData particle)
        {
            if (particle.Stopped) return;
            particle.Stopped = true;
            if (particle.Particle != null) particle.Particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void TryEnd()
        {
            if (!_playing || !_samplingEnded || _firstLivePoint < _particles.Count || Time.time < _nextParticleEndCheck) return;
            foreach (TrailParticleData particle in _particles)
                if (particle.Particle != null && particle.Particle.IsAlive(true))
                {
                    _nextParticleEndCheck = Time.time + 0.5f;
                    return;
                }
            End();
        }

        private void End()
        {
            if (!_initialized) return;
            Action callback = _onEnd;
            Dispose();
            callback?.Invoke();
        }

        public override void Dispose()
        {
            _generation++;
            _playing = false;
            _initialized = false;
            _configured = false;
            _samplingEnded = true;
            hitbox?.ClearCallbacks();
            if (hitbox != null) hitbox.enabled = false;
            if (edgeCollider != null)
            {
                edgeCollider.enabled = false;
                edgeCollider.points = Array.Empty<Vector2>();
            }
            foreach (TrailParticleData particle in _particles)
            {
                if (particle.Particle == null) continue;
                particle.Particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _pooler?.Return(particle.Particle);
            }
            _particles.Clear();
            _colliderPoints.Clear();
            _firstLivePoint = 0;
            _nextPointIndex = 0;
            _lastPointElapsed = -1f;
            _trailParticleDuration = -1f;
            if (trailStepParticles != null)
            {
                trailStepParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                trailStepParticles.gameObject.SetActive(false);
            }
            if (_soundInstance.isValid())
            {
                _soundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                _soundInstance.release();
                _soundInstance.clearHandle();
            }
            ReleaseNativeAttackSnapshot();
            _player = null;
            trailStart = null;
            _behaviour = null;
            _onStart = null;
            _onEnd = null;
            onAttackDurationFinished = null;
        }

        private void OnDisable()
        {
            if (!_initialized) return;
            Action<MultiParticlePlayerTrailAttack> callback = Deactivated;
            Dispose();
            callback?.Invoke(this);
        }

        private void OnDestroy()
        {
            if (_initialized)
            {
                Action<MultiParticlePlayerTrailAttack> callback = Deactivated;
                Dispose();
                callback?.Invoke(this);
            }
            else Dispose();
        }

        public void SetTrailDelta(float delta)
        {
            if (!TrailPresentationSpawn.IsFinite(delta) || delta <= 0) throw new ArgumentOutOfRangeException(nameof(delta));
            trailDelta = delta;
        }
        public void SetTrailParticleDuration(float duration)
        {
            if (!TrailPresentationSpawn.IsFinite(duration) || duration < 0) throw new ArgumentOutOfRangeException(nameof(duration));
            _trailParticleDuration = duration;
            if (_configured) _segmentDuration = duration;
        }

        // Retained for the unmigrated Horace call sites. Native Dash never interrupts its sampling clock.
        public void Interrupt() => _attackStart = -100000f;
        public void ReturnFromInterrupt() => _attackStart = _attackStartCache;
    }
}
