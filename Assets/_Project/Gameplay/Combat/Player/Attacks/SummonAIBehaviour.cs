using System;
using System.Collections.Generic;
using Animancer;
using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public class SummonAIBehaviour : MonoBehaviour
    {
        [SerializeField] protected AttackProgressionScaler progressionScaler;
        [SerializeField] private IdleStateModule idlingStateModule;
        [SerializeField] private PositioningStateModule positioningStateModule;
        [SerializeField] private AttackStateModule attackingStateModule;
        [SerializeField] private AnimancerComponent animancer;
        private SummonAttackBehaviour _weaponBehaviour;
        private SummonAIStateModule _currentModule;
        private AnimancerState _phaseAnimation;
        private bool _hasPhase;
        private bool _disposed = true;
        private ParticleSystem[] _particles;
        private GameObject[] _particleObjects;
        private bool[] _initialParticleActivation;
        private AudioSource[] _audioSources;
        private bool[] _audioPlayOnAwake;

        public IdleStateModule IdleModule => idlingStateModule;
        public PositioningStateModule PositioningModule => positioningStateModule;
        public AttackStateModule AttackModule => attackingStateModule;
        public AttackProgressionScaler ProgressionScaler => progressionScaler;
        public AnimancerComponent Animancer => animancer;
        public SummonAttackBehaviour WeaponBehaviour => _weaponBehaviour;
        public Transform Transform => transform;
        public bool IsPresentation { get; private set; }
        public SummonPhase Phase { get; private set; }
        public float PhaseElapsedSeconds { get; private set; }
        public float DeltaTime { get; private set; }
        public float SmoothDeltaTime { get; private set; }
        public bool IsInitialized => !_disposed;
        public OvidSummonMover Mover => (idlingStateModule as OvidSummonIdleModule)?.Mover;
        public event Action<SummonAIBehaviour> Deactivated;

        public void Init(SummonAttackBehaviour weapon)
        {
            Initialize(weapon, false);
            idlingStateModule.Init(this, CompleteIdle);
            positioningStateModule.Init(this, CompletePositioning);
            attackingStateModule.Init(this, CompleteAttack);
            UpdateProgressionScaler();
        }

        public void InitPresentation(SummonAttackBehaviour weapon, ProjectilePresentationStats stats)
        {
            Initialize(weapon, true);
            progressionScaler?.Apply(stats);
            DisableDamage();
        }

        private void Initialize(SummonAttackBehaviour weapon, bool presentation)
        {
            CacheParticleConfiguration();
            Dispose();
            // Animation leaves particle ancestors active when the pet is returned to its pool.
            // Restore the actual prefab defaults while checkout is still inactive.
            for (int index = 0; index < _particleObjects.Length; index++)
                _particleObjects[index].SetActive(_initialParticleActivation[index]);
            _weaponBehaviour = weapon != null ? weapon : throw new System.ArgumentNullException(nameof(weapon));
            IsPresentation = presentation;
            _disposed = false;
            Mover.Init(this);
            foreach (SetAtSurfaceLevel surface in GetComponentsInChildren<SetAtSurfaceLevel>(true))
                surface.BindOwner(weapon.OwnerPlayer.transform);
        }

        public void StartSimulation()
        {
            if (_disposed || IsPresentation || !gameObject.activeInHierarchy) return;
            _currentModule = idlingStateModule;
            _currentModule.Enter();
        }

        // The owning emitter is the sole simulation clock. This component has no Unity Update.
        public void TickNative(float deltaTime, float smoothDeltaTime)
        {
            if (_disposed || IsPresentation || !gameObject.activeInHierarchy) return;
            DeltaTime = deltaTime;
            SmoothDeltaTime = smoothDeltaTime;
            if (Phase == SummonPhase.Positioning) PhaseElapsedSeconds += deltaTime;
            _currentModule?.OnUpdate();
        }

        public void OnUpdate() => TickNative(Time.deltaTime, Time.smoothDeltaTime);

        public void SetPhase(SummonPhase phase, float elapsed = 0f)
        {
            if (_disposed) return;
            bool firstPhase = !_hasPhase;
            bool changed = !_hasPhase || Phase != phase;
            Phase = phase;
            PhaseElapsedSeconds = elapsed;
            _hasPhase = true;
            if (!changed) return;
            // Only a restored initial Birth is historical Native presentation. New attacks
            // must never seek through their collision windows or create extra damage ticks.
            if (!IsPresentation && firstPhase && phase == SummonPhase.Birth && elapsed > 0f)
                SeekPresentationParticles(phase, elapsed, 0f, true);
            else PlayPhase(phase, elapsed);
            if (!_disposed && !IsPresentation) _weaponBehaviour.NotifyPhaseChanged();
        }

        public void ApplyPresentation(SummonPhase phase, float elapsed, ProjectilePresentationStats stats, SummonPose pose)
        {
            if (_disposed || !IsPresentation) return;
            bool firstPhase = !_hasPhase;
            progressionScaler?.Apply(stats);
            Phase = phase;
            PhaseElapsedSeconds = elapsed;
            _hasPhase = true;
            // Place world-space emitters before the one-off visual seek.
            Mover.ApplyPose(pose);
            SeekPresentationParticles(phase, elapsed, stats.Duration, firstPhase);
            if (_disposed) return;
            Mover.ApplyPose(pose);
            RefreshSurfaces();
            DisableDamage();
        }

        public void TickPresentation(float deltaTime)
        {
            if (_disposed || !IsPresentation || !_hasPhase) return;
            PhaseElapsedSeconds += deltaTime;
            SeekPhase();
            DisableDamage();
        }

        public void ApplyPose(SummonPose pose)
        {
            if (_disposed || !IsPresentation) return;
            Mover.ApplyPose(pose);
            RefreshSurfaces();
            if (Phase == SummonPhase.Positioning && _phaseAnimation != null)
                _phaseAnimation.Speed = pose.MoveAnimationSpeed;
        }

        private void PlayPhase(SummonPhase phase, float elapsed)
        {
            ClipTransition clip = PhaseClip(phase);
            _phaseAnimation = clip != null && clip.Clip != null ? animancer.Play(clip) : null;
            if (_phaseAnimation != null) _phaseAnimation.Time = elapsed * Mathf.Abs(clip.Speed);
        }

        private void RefreshSurfaces()
        {
            // Applying a world pose changes every child's world plane before the next LateUpdate.
            foreach (SetAtSurfaceLevel surface in GetComponentsInChildren<SetAtSurfaceLevel>(true))
                surface.RefreshSurface();
        }

        private void SeekPhase()
        {
            ClipTransition clip = PhaseClip(Phase);
            if (_phaseAnimation != null && _phaseAnimation.IsValid() && clip != null)
                _phaseAnimation.Time = PhaseElapsedSeconds * Mathf.Abs(clip.Speed);
        }

        private void CacheParticleConfiguration()
        {
            if (_particles != null) return;
            _particles = GetComponentsInChildren<ParticleSystem>(true);
            var objects = new HashSet<GameObject>();
            foreach (ParticleSystem particle in _particles)
                for (Transform node = particle.transform; node != null && node != transform; node = node.parent)
                    objects.Add(node.gameObject);
            _particleObjects = new GameObject[objects.Count];
            objects.CopyTo(_particleObjects);
            _initialParticleActivation = new bool[_particleObjects.Length];
            for (int index = 0; index < _particleObjects.Length; index++)
                _initialParticleActivation[index] = _particleObjects[index].activeSelf;
            _audioSources = GetComponentsInChildren<AudioSource>(true);
            _audioPlayOnAwake = new bool[_audioSources.Length];
        }

        private void SeekPresentationParticles(SummonPhase phase, float elapsed, float mainDuration, bool firstPhase)
        {
            var active = new bool[_particles.Length];
            var advance = new bool[_particles.Length];
            var ages = new float[_particles.Length];
            var expired = new bool[_particles.Length];
            for (int index = 0; index < _particles.Length; index++)
            {
                active[index] = _particles[index].gameObject.activeInHierarchy;
                // Existing particles already ran during packet transit. Only newly activated
                // systems catch up on a normal phase edge; never restart a continuing burst.
                advance[index] = firstPhase || !active[index];
                ages[index] = _particles[index].time;
            }
            bool historical = firstPhase || elapsed > 0f;
            for (int index = 0; index < _audioSources.Length; index++)
            {
                _audioPlayOnAwake[index] = _audioSources[index].playOnAwake;
                if (historical) _audioSources[index].playOnAwake = false;
            }
            try
            {
                // A late Main/Exit baseline needs the source Enter activation at .533s,
                // including LaserStartBlue's authored off/on edge. Do not replay Birth audio.
                if (firstPhase && phase >= SummonPhase.AttackMain)
                    ReplayParticlePhase(SummonPhase.AttackEnter, ClipDuration(PhaseClip(SummonPhase.AttackEnter)), active, advance, ages, expired);
                if (firstPhase && phase == SummonPhase.AttackExit && !_disposed)
                    ReplayParticlePhase(SummonPhase.AttackMain, Mathf.Max(0f, mainDuration), active, advance, ages, expired);
                if (!_disposed) ReplayParticlePhase(phase, elapsed, active, advance, ages, expired);
            }
            finally
            {
                for (int index = 0; index < _audioSources.Length; index++)
                    if (_audioSources[index] != null) _audioSources[index].playOnAwake = _audioPlayOnAwake[index];
                if (!_disposed)
                    for (int index = 0; index < _particles.Length; index++)
                    {
                        ParticleSystem particle = _particles[index];
                        // Unity can reset time to zero when a non-looping system completes.
                        // That zero is not evidence that an old burst should start again.
                        if (expired[index]) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                        else if (advance[index] && particle.gameObject.activeInHierarchy && particle.isPaused) particle.Play(false);
                    }
            }
        }

        private void ReplayParticlePhase(SummonPhase phase, float elapsed, bool[] active, bool[] advance, float[] ages, bool[] expired)
        {
            PlayPhase(phase, 0f);
            if (_phaseAnimation == null) return;
            ClipTransition clip = PhaseClip(phase);
            float duration = ClipDuration(clip);
            animancer.Evaluate(0f);
            TrackParticleActivation(active, advance, ages, expired);
            // The source Cocoon and Move clips activate no particles. Their animation clock
            // may be arbitrarily old, so it must never drive an elapsed-seconds replay loop.
            float sampled = phase == SummonPhase.Cocoon || phase == SummonPhase.Positioning ? 0f : Mathf.Min(elapsed, duration);
            float time = 0f;
            while (!_disposed && time < sampled)
            {
                float step = Mathf.Min(1f / 60f, sampled - time);
                for (int index = 0; index < _particles.Length; index++)
                    if (active[index] && advance[index] && (_particles[index].isPlaying || _particles[index].isPaused))
                        AdvanceParticle(index, step, false, ages, expired);
                time += step;
                _phaseAnimation.Time = time * Mathf.Abs(clip.Speed);
                animancer.Evaluate(0f);
                TrackParticleActivation(active, advance, ages, expired);
            }
            if (_disposed) return;
            // After the short source clip ends, activation is constant. Skip expired whole
            // emission cycles while retaining enough source lifetime to reconstruct live particles.
            float remaining = elapsed - sampled;
            if (remaining > 0f)
                for (int index = 0; index < _particles.Length; index++)
                {
                    ParticleSystem particle = _particles[index];
                    if (!active[index] || !advance[index] || (!particle.isPlaying && !particle.isPaused)) continue;
                    var main = particle.main;
                    float speed = main.simulationSpeed;
                    if (speed <= 0f) continue;
                    double particleSeconds = (double)remaining * speed;
                    float lifetime = CurveMaximum(main.startLifetime) + CurveMaximum(main.startDelay);
                    float bounded = (float)Math.Min(particleSeconds, main.duration + lifetime);
                    if (main.loop && main.duration > 0f)
                    {
                        float horizon = (Mathf.Ceil(lifetime / main.duration) + 1f) * main.duration;
                        bounded = (float)(particleSeconds <= horizon ? particleSeconds : horizon + particleSeconds % main.duration);
                    }
                    AdvanceParticle(index, bounded / speed, true, ages, expired);
                }
            _phaseAnimation.Time = elapsed * Mathf.Abs(clip.Speed);
            animancer.Evaluate(0f);
            TrackParticleActivation(active, advance, ages, expired);
        }

        private void AdvanceParticle(int index, float seconds, bool fixedStep, float[] ages, bool[] expired)
        {
            ParticleSystem particle = _particles[index];
            var main = particle.main;
            ages[index] += seconds * main.simulationSpeed;
            if (!main.loop && ages[index] >= main.duration + CurveMaximum(main.startLifetime) + CurveMaximum(main.startDelay))
            {
                expired[index] = true;
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            else particle.Simulate(seconds, false, false, fixedStep);
        }

        private void TrackParticleActivation(bool[] active, bool[] advance, float[] ages, bool[] expired)
        {
            for (int index = 0; index < _particles.Length; index++)
            {
                ParticleSystem particle = _particles[index];
                bool current = particle.gameObject.activeInHierarchy;
                if (!current && active[index]) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (current && !active[index])
                {
                    advance[index] = true;
                    ages[index] = 0f;
                    expired[index] = false;
                    if (particle.main.playOnAwake) particle.Play(false);
                }
                active[index] = current;
            }
        }

        private static float CurveMaximum(ParticleSystem.MinMaxCurve curve)
        {
            if (curve.mode == ParticleSystemCurveMode.Constant) return Mathf.Max(0f, curve.constant);
            if (curve.mode == ParticleSystemCurveMode.TwoConstants) return Mathf.Max(0f, curve.constantMin, curve.constantMax);
            // Source Ovid lifetimes are constants. Sampling also bounds future authored curves
            // without tying particle catch-up work to an untrusted network age.
            float maximum = 0f;
            for (int index = 0; index <= 32; index++)
            {
                float t = index / 32f;
                maximum = Mathf.Max(maximum, curve.Evaluate(t, 0f), curve.Evaluate(t, 1f));
            }
            return maximum;
        }

        public ClipTransition PhaseClip(SummonPhase phase)
        {
            var idle = idlingStateModule as OvidSummonIdleModule;
            var attack = attackingStateModule as OvidSummonAttackModule;
            switch (phase)
            {
                case SummonPhase.Cocoon: return idle?.CocoonAnimation;
                case SummonPhase.Birth: return idle?.BirthAnimation;
                case SummonPhase.Positioning: return Mover.MoveAnimation;
                case SummonPhase.AttackEnter: return attack?.EnterAnimation;
                case SummonPhase.AttackMain: return attack?.MainAnimation;
                case SummonPhase.AttackExit: return attack?.ExitAnimation;
                default: return null;
            }
        }

        public static float ClipDuration(ClipTransition clip)
        {
            if (clip == null || clip.Clip == null) return 0f;
            float speed = Mathf.Abs(clip.Speed);
            if (!SummonPose.Finite(speed) || speed <= 0f) throw new InvalidOperationException("Summon clips require a finite positive speed.");
            return clip.Clip.length / speed;
        }

        public void UpdateProgressionScaler()
        {
            if (!_disposed && !IsPresentation) progressionScaler?.Apply(_weaponBehaviour.CurrentPresentationStats);
        }

        public void DisableDamage()
        {
            foreach (BaseAttackHitBox hitbox in GetComponentsInChildren<BaseAttackHitBox>(true))
            {
                hitbox.ClearCallbacks();
                hitbox.Toggle(false);
                hitbox.enabled = false;
            }
        }

        private void CompleteIdle()
        {
            if (_disposed) return;
            _currentModule = positioningStateModule;
            _currentModule.Enter();
        }

        private void CompletePositioning()
        {
            if (_disposed) return;
            _currentModule = attackingStateModule;
            _currentModule.Enter();
        }

        public void RequestAttack()
        {
            if (!_disposed && !IsPresentation && _currentModule == positioningStateModule) CompletePositioning();
        }

        private void CompleteAttack()
        {
            if (_disposed) return;
            _weaponBehaviour.CompleteNativeAttack();
            if (_disposed) return;
            _currentModule = idlingStateModule;
            _currentModule.Enter();
        }

        public void Activate() => StartSimulation();
        public void Deactivate() => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Deactivated = null;
            _currentModule = null;
            idlingStateModule?.Dispose();
            positioningStateModule?.Dispose();
            attackingStateModule?.Dispose();
            DisableDamage();
            Mover?.Dispose();
            if (animancer != null) animancer.Stop();
            if (_particles != null)
                foreach (ParticleSystem particle in _particles)
                    if (particle != null) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (SetAtSurfaceLevel surface in GetComponentsInChildren<SetAtSurfaceLevel>(true)) surface.UnbindOwner();
            _weaponBehaviour = null;
            _phaseAnimation = null;
            _hasPhase = false;
            PhaseElapsedSeconds = DeltaTime = SmoothDeltaTime = 0f;
        }

        private void OnDisable()
        {
            Action<SummonAIBehaviour> handler = Deactivated;
            Deactivated = null;
            try { handler?.Invoke(this); }
            finally { Dispose(); }
        }

        private void OnDestroy() => Dispose();

        private void LateUpdate()
        {
            if (_disposed) return;
            if (_weaponBehaviour == null || !_weaponBehaviour.gameObject.activeInHierarchy)
            {
                gameObject.SetActive(false);
                return;
            }
            // Animation curves may enable source colliders; replicas never retain a collision callback.
            if (IsPresentation) DisableDamage();
        }
    }
}
