using System;
using AstralShift.Managers;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public class UltimateDamageAttack : BasePlayerAttack, IPausable
    {
        public Animator animator;
        private ParticleSystem[] particles;
        private bool playing;
        private bool configured;
        private float elapsed;
        private float endTime;
        public bool IsWavePlaying => playing;
        public float WaveElapsed => elapsed;

        public void PlayNativeWave(WeaponBehaviour behaviour, AttackSnapshot snapshot, float age, float animationEndTime, Action onEnd)
        {
            InitNative(behaviour, snapshot, null, onEnd);
            BeginPlayback(age, animationEndTime);
        }

        public void PlayPresentationWave(WeaponBehaviour behaviour, ProjectilePresentationStats stats,
            float age, float animationEndTime, Action onEnd)
        {
            InitPresentation(behaviour, stats, null, onEnd);
            BeginPlayback(age, animationEndTime);
        }

        private void BeginPlayback(float age, float animationEndTime)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("Ultimate wave requires its original Animator/controller.");
            if (float.IsNaN(age) || float.IsInfinity(age) || age < 0f || animationEndTime <= 0f)
                throw new ArgumentOutOfRangeException(nameof(age));
            configured = true;
            playing = true;
            elapsed = 0f;
            endTime = animationEndTime;
            if (particles == null) particles = GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem particle in particles)
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (hitbox != null) hitbox.Toggle(!IsPresentationOnly);
            gameObject.SetActive(true);
            animator.enabled = false;
            animator.Rebind();
            animator.Play("Wave", 0, 0f);
            animator.Update(0f);
            if (hitbox != null) hitbox.Toggle(!IsPresentationOnly);
            foreach (ParticleSystem particle in particles)
                if (particle.gameObject.activeInHierarchy && particle.main.playOnAwake) particle.Play(false);
            _onStart?.Invoke();
            if (age > 0f) Advance(age, true);
        }

        public override void Attack()
        {
            if (!configured) throw new InvalidOperationException("Use PlayNativeWave or PlayPresentationWave before executing an Ultimate wave.");
        }

        public void Advance(float seconds, bool seekParticles = false)
        {
            if (!playing || seconds <= 0f) return;
            elapsed += seconds;
            animator.Update(seconds);
            // A late replica seeks the real particles once, then lets their authored clocks advance naturally.
            if (playing && seekParticles)
                foreach (ParticleSystem particle in particles)
                    if (particle.gameObject.activeInHierarchy) particle.Simulate(seconds, false, false, false);
            if (playing && elapsed + 0.00001f >= endTime) Complete();
            else if (IsPresentationOnly && hitbox != null) hitbox.Toggle(false);
        }

        public void ResumeParticlesAfterSeek()
        {
            if (!playing || particles == null) return;
            foreach (ParticleSystem particle in particles)
                if (particle.gameObject.activeInHierarchy && particle.isPaused) particle.Play(false);
        }

        public void onAttackAnimationEnd() => Complete();
        public override void Dispose() => Complete();

        private void Complete()
        {
            if (!playing)
            {
                if (hitbox != null) { hitbox.Init(null); hitbox.Toggle(false); }
                ReleaseNativeAttackSnapshot();
                _onEnd = null;
                _onStart = null;
                return;
            }
            playing = false;
            if (hitbox != null) { hitbox.Init(null); hitbox.Toggle(false); }
            ReleaseNativeAttackSnapshot();
            Action end = _onEnd;
            _onEnd = null;
            _onStart = null;
            gameObject.SetActive(false);
            end?.Invoke();
        }

        private void OnDisable() => Complete();
        private void OnDestroy() => Complete();
        // Network gameplay never subscribes this visual to the old global PauseManager.
        public void OnPausePausables() { }
        public void OnResumePausables() { }
        public void OnGamePause() { }
        public void OnGameResume() { }
    }
}
