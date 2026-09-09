using System;
using System.Linq;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Timeline;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using UnityEngine;
using GasAttackSnapshot = MonsterSupergroup.GAS.AttackSnapshot;

namespace AstralShift.HellMaiden.Player.Attacks
{
    /// <summary>The original Dante presentation with an explicitly admitted, owner-scoped GAS attack.</summary>
    public class DanteUltimateAttack : UltimateAttackWeaponBehaviour
    {
        [Header("Burn Settings")]
        public float burnStrength = 0.1f;
        public float burnDuration = 4f;
        public float burnRate = 0.5f;
        [Header("Wave Settings")]
        public GameObject DanteUltimateWavePrefab;

        [SerializeField, HideInInspector] private float entryTransitionDuration;

        private readonly UltimateDamageAttack[] waves = new UltimateDamageAttack[2];
        private readonly bool[] waveStarted = new bool[2];
        private readonly bool[] waveEnded = new bool[2];
        private readonly float[] waveTimes = new float[2];
        private ParticleSystem[] mainParticles;
        private WeaponData compatibilityView;
        private GasAttackSnapshot activeSnapshot;
        private ProjectilePresentationStats frozenStats;
        private bool nativeConfigured;
        private bool presentationConfigured;
        private bool activeNative;
        private bool preparing;
        private bool finishing;
        private bool seeking;
        private float elapsed;
        private float mainDuration;
        private float waveEndTime;
        private ulong lastStartedUseId;
        private Action<DanteUltimateAttack> onReturned;
        private Action<int> cameraShake;

        public ulong ActiveUseId { get; private set; }
        public bool IsNativeActive => ActiveUseId != 0 && activeNative;
        public bool IsPresentationActive => ActiveUseId != 0 && !activeNative;
        public float ElapsedSeconds => elapsed;
        public float EntryTransitionDuration => entryTransitionDuration;
        public float SequenceDuration { get { EnsureVisualConfiguration(); return Mathf.Max(mainDuration, waveTimes[1] + waveEndTime); } }
        public event Action<UltimatePresentationSpawn> PresentationSpawned;
        public event Action<UltimatePresentationTermination> PresentationTerminated;
        public event Action<ulong> NativeAttackCompleted;
        public event Action<byte> WaveStarted;
        public event Action<byte> WaveEnded;

        private void Awake()
        {
            // The source root only uses TimelineEffects.ShakeCamera. Explicit binding suppresses its global fallback.
            // Inactive prefab instances may be configured before their first Awake.
            BindCamera(cameraShake);
        }

        public void ConfigureNativeUltimate(PlayerMovement owner, WeaponRuntimeBehaviour runtime,
            UltimateData source, Action<int> localCameraShake = null)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (runtime == null || !runtime.IsInitialized) throw new ArgumentException("An initialized external GAS runtime is required.", nameof(runtime));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (runtime.CombatId != UltimateNativeDefinitionAdapter.EncodeAbilityId(source.Id))
                throw new ArgumentException("The GAS runtime must use the source Ultimate's namespaced ability ID.", nameof(runtime));
            DisposeNativeUltimate();
            ultimateData = source;
            ConfigureOwner(owner);
            compatibilityView = UltimateNativeDefinitionAdapter.Create(source);
            try
            {
                ConfigureNativeRuntime(runtime, compatibilityView);
                base.InitNative(compatibilityView.ID);
                EnsureVisualConfiguration();
                nativeConfigured = true;
                presentationConfigured = false;
                BindCamera(localCameraShake);
            }
            catch { ReleaseCompatibilityView(); throw; }
        }

        public void InitializePresentationReplica(PlayerMovement owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            DisposeNativeUltimate();
            ConfigureOwner(owner);
            if (ultimateData == null) throw new InvalidOperationException("The source Ultimate definition is missing.");
            UltimateNativeDefinitionAdapter.EncodeAbilityId(ultimateData.Id);
            EnsureVisualConfiguration();
            presentationConfigured = true;
            BindCamera(null);
        }

        public bool TryBegin(ulong admittedUseId, float elapsedSeconds = 0f)
        {
            if (!nativeConfigured || !CanAttack || !CanBegin(admittedUseId, elapsedSeconds)) return false;
            WeaponRuntimeBehaviour runtime = NativeRuntime;
            var context = CombatContext.CreateRoot(new CombatEventId(admittedUseId), runtime.SourcePlayerId,
                runtime.SourceEntityId, runtime.CombatId, compatibilityView.AttackTags);
            GasAttackSnapshot snapshot = runtime.BeginAttack(context);
            activeSnapshot = snapshot;
            frozenStats = ProjectilePresentationStats.From(snapshot.Stats, 1f);
            activeNative = true;
            ActiveUseId = admittedUseId;
            lastStartedUseId = admittedUseId;
            try
            {
                // Admission precedes this method. No second root/event identity is allocated here.
                PresentationSpawned?.Invoke(new UltimatePresentationSpawn(ultimateData.Id, admittedUseId, frozenStats));
                if (ActiveUseId != admittedUseId) return false;
                StartPlayback(elapsedSeconds);
                return true;
            }
            catch { Finish(UltimateTerminationReason.Cancelled); throw; }
        }

        public bool PlayPresentation(UltimatePresentationSpawn spawn, float elapsedSeconds,
            Action<DanteUltimateAttack> returned = null)
        {
            if (!presentationConfigured || spawn.UltimateId != ultimateData.Id || !spawn.Stats.IsFinite ||
                spawn.Element != AttackElement.Default || !CanBegin(spawn.AttackEventId, elapsedSeconds)) return false;
            frozenStats = spawn.Stats;
            activeNative = false;
            ActiveUseId = spawn.AttackEventId;
            lastStartedUseId = spawn.AttackEventId;
            onReturned = returned;
            try { StartPlayback(elapsedSeconds); return true; }
            catch { Finish(UltimateTerminationReason.Cancelled); throw; }
        }

        private bool CanBegin(ulong useId, float age)
        {
            if (ActiveUseId != 0 || useId == 0 || useId == lastStartedUseId || !Finite(age) || age < 0f) return false;
            return age < SequenceDuration;
        }

        private void EnsureVisualConfiguration()
        {
            if (mainParticles != null) return;
            if (animator == null || animator.runtimeAnimatorController == null || DanteUltimateWavePrefab == null)
                throw new InvalidOperationException("Dante Ultimate requires the original root Animator and wave prefab.");
            AnimationClip main = animator.runtimeAnimatorController.animationClips.FirstOrDefault(clip => clip.name == "DanteUltimate_BaseAnim");
            var wave = DanteUltimateWavePrefab.GetComponent<UltimateDamageAttack>();
            if (main == null || wave == null || wave.animator == null || wave.animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("Dante Ultimate's original main clip/wave Animator is missing.");
            AnimationClip waveClip = wave.animator.runtimeAnimatorController.animationClips.FirstOrDefault(clip => clip.name == "DanteUltimateWave");
            if (waveClip == null) throw new InvalidOperationException("Dante Ultimate's original wave clip is missing.");
            AnimationEvent[] spawns = main.events.Where(value => value.functionName == "SpawnWave").OrderBy(value => value.time).ToArray();
            AnimationEvent[] ends = waveClip.events.Where(value => value.functionName == "onAttackAnimationEnd").ToArray();
            if (spawns.Length != 2 || spawns[0].intParameter != 1 || spawns[1].intParameter != 2 ||
                spawns[0].time != 0f || spawns[1].time <= 0f || ends.Length != 1 || ends[0].time <= 0f)
                throw new InvalidOperationException("Dante Ultimate requires its authored two-wave and terminal animation events.");
            waveTimes[0] = spawns[0].time;
            waveTimes[1] = spawns[1].time;
            waveEndTime = ends[0].time;
            mainDuration = main.length;
            // Capture only the original root particles, before cached wave children are created.
            mainParticles = GetComponentsInChildren<ParticleSystem>(true);
        }

        private void StartPlayback(float age)
        {
            EnsureVisualConfiguration();
            elapsed = 0f;
            Array.Clear(waveStarted, 0, waveStarted.Length);
            Array.Clear(waveEnded, 0, waveEnded.Length);
            preparing = true;
            seeking = age > 0f;
            try
            {
                foreach (UltimateDamageAttack wave in waves) if (wave != null) wave.Dispose();
                foreach (ParticleSystem particle in mainParticles)
                    particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                gameObject.SetActive(true);
                animator.enabled = false;
                animator.Rebind();
                animator.Update(0f);
                animator.SetTrigger("Attack");
                animator.Update(0f);
            }
            finally { preparing = false; }
            foreach (ParticleSystem particle in mainParticles)
                if (particle.gameObject.activeInHierarchy && particle.main.playOnAwake) particle.Play(false);
            SpawnDueWaves();
            if (age == 0f)
            {
                try { PlayAttackSound(); }
                catch (FMODUnity.EventNotFoundException exception) { Debug.LogException(exception, this); }
            }
            if (age > 0f) Advance(age, true);
            seeking = false;
            if (ActiveUseId != 0)
            {
                foreach (ParticleSystem particle in mainParticles)
                    if (particle.gameObject.activeInHierarchy && particle.isPaused) particle.Play(false);
                foreach (UltimateDamageAttack wave in waves) wave?.ResumeParticlesAfterSeek();
            }
        }

        private void Update()
        {
            if (ActiveUseId == 0) return;
            if (activeNative && !CanAttack) { Cancel(ActiveUseId); return; }
            Advance(Time.unscaledDeltaTime, false);
        }

        private void Advance(float seconds, bool seekParticles)
        {
            // Bounded small steps preserve source event order even when a late packet crosses both spawn times.
            float remaining = Mathf.Min(seconds, SequenceDuration - elapsed);
            while (ActiveUseId != 0 && remaining > 0.000001f)
            {
                float step = Mathf.Min(remaining, 1f / 60f);
                elapsed += step;
                foreach (UltimateDamageAttack wave in waves)
                    if (wave != null && wave.IsWavePlaying) wave.Advance(step, seekParticles);
                if (ActiveUseId == 0) break;
                animator.Update(step);
                if (seekParticles)
                    foreach (ParticleSystem particle in mainParticles)
                        if (particle.gameObject.activeInHierarchy) particle.Simulate(step, false, false, false);
                remaining -= step;
                TryComplete();
            }
            if (ActiveUseId != 0 && elapsed + 0.00001f >= SequenceDuration)
                Finish(UltimateTerminationReason.Completed);
        }

        // Original main-clip animation event. The ordinal is one-based in the source clip.
        public void SpawnWave(int waveIndex)
        {
            int index = waveIndex - 1;
            if (preparing || ActiveUseId == 0 || index < 0 || index >= waves.Length || waveStarted[index]) return;
            float age = Mathf.Max(0f, elapsed - waveTimes[index]);
            waveStarted[index] = true;
            if (age >= waveEndTime)
            {
                waveEnded[index] = true;
                return;
            }
            UltimateDamageAttack wave = waves[index];
            if (wave == null)
            {
                // Parent to this player's root, matching the authored Ultimate manager and both waves.
                GameObject instance = Instantiate(DanteUltimateWavePrefab, transform);
                instance.SetActive(false);
                wave = instance.GetComponent<UltimateDamageAttack>();
                waves[index] = wave;
            }
            WaveStarted?.Invoke((byte)index);
            if (activeNative)
                wave.PlayNativeWave(this, activeSnapshot, age, waveEndTime, () => HandleWaveEnd(index));
            else
                wave.PlayPresentationWave(this, frozenStats, age, waveEndTime, () => HandleWaveEnd(index));
        }

        private void SpawnDueWaves()
        {
            for (int index = 0; index < waves.Length; index++)
                if (!waveStarted[index] && elapsed + 0.00001f >= waveTimes[index]) SpawnWave(index + 1);
        }

        private void HandleWaveEnd(int index)
        {
            if (ActiveUseId == 0 || !waveStarted[index] || waveEnded[index]) return;
            waveEnded[index] = true;
            WaveEnded?.Invoke((byte)index);
            TryComplete();
        }

        private void TryComplete()
        {
            if (!preparing && !finishing && ActiveUseId != 0 && elapsed + 0.00001f >= mainDuration &&
                waveEnded[0] && waveEnded[1]) Finish(UltimateTerminationReason.Completed);
        }

        public bool Cancel(ulong admittedUseId)
        {
            if (ActiveUseId == 0 || ActiveUseId != admittedUseId) return false;
            Finish(UltimateTerminationReason.Cancelled);
            return true;
        }

        private void Finish(UltimateTerminationReason reason)
        {
            if (ActiveUseId == 0 || finishing) return;
            finishing = true;
            ulong completed = ActiveUseId;
            bool wasNative = activeNative;
            ActiveUseId = 0;
            activeNative = false;
            Action<DanteUltimateAttack> returned = onReturned;
            onReturned = null;
            try
            {
                foreach (UltimateDamageAttack wave in waves) if (wave != null) wave.Dispose();
                if (mainParticles != null)
                    foreach (ParticleSystem particle in mainParticles)
                        particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                activeSnapshot?.Dispose();
                activeSnapshot = null;
                if (animator != null) animator.enabled = false;
                gameObject.SetActive(false);
            }
            finally { finishing = false; }
            if (wasNative)
            {
                try { PresentationTerminated?.Invoke(new UltimatePresentationTermination(ultimateData.Id, completed, reason)); }
                finally { NativeAttackCompleted?.Invoke(completed); }
            }
            returned?.Invoke(this);
        }

        private void BindCamera(Action<int> handler)
        {
            cameraShake = handler;
            var events = GetComponent<TimelineEffects>();
            if (events != null) events.BindLocalCameraShake(index => { if (!seeking && IsNativeActive) cameraShake?.Invoke(index); });
        }

        public void DisposeNativeUltimate()
        {
            if (ActiveUseId != 0) Cancel(ActiveUseId);
            nativeConfigured = false;
            presentationConfigured = false;
            BindCamera(null);
            ReleaseCompatibilityView();
        }

        public void DisposePresentationReplica()
        {
            DisposeNativeUltimate();
            foreach (UltimateDamageAttack wave in waves)
                if (wave != null) { wave.Dispose(); Destroy(wave.gameObject); }
            Array.Clear(waves, 0, waves.Length);
        }

        private void ReleaseCompatibilityView()
        {
            if (compatibilityView == null) return;
            if (Application.isPlaying) Destroy(compatibilityView);
            else DestroyImmediate(compatibilityView);
            compatibilityView = null;
        }

        public override void Init() => throw new InvalidOperationException("Dante Ultimate uses ConfigureNativeUltimate with an external per-player GAS runtime.");
        public override void Attack() => throw new InvalidOperationException("Dante Ultimate must be started with an admitted use ID through TryBegin.");
        public override float GetAttackSequenceDuration() => SequenceDuration;
        public override void Interrupt() { if (ActiveUseId != 0) Cancel(ActiveUseId); }
        protected override void Dispose() => DisposeNativeUltimate();
        private void OnDisable() { if (ActiveUseId != 0) Cancel(ActiveUseId); }
        private void OnDestroy() => DisposeNativeUltimate();
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
