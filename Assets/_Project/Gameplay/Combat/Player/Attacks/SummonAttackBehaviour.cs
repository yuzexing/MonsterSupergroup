using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using UnityEngine;
using GasDamageType = MonsterSupergroup.GAS.DamageType;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public class SummonAttackBehaviour : WeaponBehaviour
    {
        [SerializeField] protected SummonAIVariants variants;
        protected SummonAIBehaviour _summonAI;
        private Transform checkoutRoot;
        private SummonLifetimeAnchor lifetimeAnchor;
        private Func<double> clock;
        private Func<ulong> nextPetId;
        private Action<Vector2, float, float, List<SummonTarget>> queryTargets;
        private bool configured;
        private bool replica;
        private bool cancelling;
        private uint generation;
        private ulong petId;
        private AttackElement element;
        private uint phaseSequence;
        private uint poseSequence;
        private double cooldownStartedAt;
        private AttackSnapshot attackSnapshot;

        public event Action<SummonPresentationState> PresentationStateChanged;
        public event Action<SummonPresentationPose> PresentationPoseChanged;
        public event Action<SummonPresentationTermination> PresentationTerminated;
        public ulong PetId => petId;
        public SummonAIBehaviour ActiveSummon => _summonAI;
        public AttackSnapshot CurrentSnapshot => attackSnapshot;
        public bool HasSimulationBinding => configured;
        public virtual float InitialMaturityDelay => 0f;
        public float BirthPresentationDuration => (variants.GetPrefab(AttackElement.Default).IdleModule as OvidSummonIdleModule)?.BirthDuration ?? 0f;
        public virtual double MaturityAt { get; protected set; }
        protected double ClockNow => ReadClock();
        public bool IsAttackReady => configured && !replica && !cancelling && gameObject.activeInHierarchy &&
            CanAttack && attackSnapshot == null && GetMaturityPhase(out _) == SummonPhase.Positioning && CheckCooldown();

        public override float LastAttackElapsedTime
        {
            get => !configured || attackSnapshot != null ? 0f : (float)(ReadClock() - cooldownStartedAt);
            protected set { if (configured) cooldownStartedAt = ReadClock() - value; }
        }

        public ProjectilePresentationStats CurrentPresentationStats => ProjectilePresentationStats.From(
            attackSnapshot != null ? attackSnapshot.Stats : NativeRuntime.Stats.CreateSnapshot(), 0f);

        public override void InitNative(uint id)
        {
            CancelPet();
            base.InitNative(id);
            InitializePool();
        }

        private void InitializePool()
        {
            if (variants == null || variants.GetPrefab(AttackElement.Default) == null)
                throw new InvalidOperationException("Summon requires an authored AI variant.");
            variants.Init();
            if (lifetimeAnchor == null)
            {
                var anchor = new GameObject("Summon Lifetime");
                anchor.transform.SetParent(transform, false);
                lifetimeAnchor = anchor.AddComponent<SummonLifetimeAnchor>();
                lifetimeAnchor.Deactivated = () => CancelPet(true);
            }
            if (checkoutRoot != null) return;
            var checkout = new GameObject("Summon Pool Checkout");
            checkout.SetActive(false);
            checkout.transform.SetParent(transform, false);
            checkoutRoot = checkout.transform;
        }

        public void ConfigureSimulation(Func<double> sessionClock, Func<ulong> allocatePetId,
            Action<Vector2, float, float, List<SummonTarget>> enemyQuery, double maturityAt = double.NaN)
        {
            if (replica) throw new InvalidOperationException("A presentation replica cannot simulate summons.");
            if (sessionClock == null || allocatePetId == null || enemyQuery == null) throw new ArgumentNullException();
            double now = sessionClock();
            ValidateTime(now);
            if (!double.IsNaN(maturityAt)) ValidateTime(maturityAt);
            double nextMaturity = double.IsNaN(maturityAt) ? now + InitialMaturityDelay : maturityAt;
            ValidateTime(nextMaturity);
            ValidateTime(nextMaturity + BirthPresentationDuration);
            clock = sessionClock;
            nextPetId = allocatePetId;
            queryTargets = enemyQuery;
            if (configured) return; // Repeated Build baselines do not cancel a pet or a retained attack root.
            configured = true;
            MaturityAt = nextMaturity;
            cooldownStartedAt = MaturityAt + BirthPresentationDuration;
        }

        public virtual void RestoreMaturityAt(double maturityAt)
        {
            ValidateTime(maturityAt);
            ValidateTime(maturityAt + BirthPresentationDuration);
            if (!configured) throw new InvalidOperationException("Bind the session clock before restoring maturity.");
            CancelPet();
            MaturityAt = maturityAt;
            cooldownStartedAt = maturityAt + BirthPresentationDuration;
        }

        public void UnbindSimulation()
        {
            try { CancelPet(); }
            finally
            {
                configured = false;
                clock = null;
                nextPetId = null;
                queryTargets = null;
            }
        }

        public virtual SummonPhase GetMaturityPhase(out float elapsed)
        {
            elapsed = 0f;
            return SummonPhase.Positioning;
        }

        public override void RestoreCooldownRemaining(float remainingSeconds)
        {
            if (!SummonPose.Finite(remainingSeconds) || remainingSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(remainingSeconds));
            if (!configured) throw new InvalidOperationException("Bind Summon's session clock before restoring cooldown.");
            cooldownStartedAt = ReadClock() + remainingSeconds - GetCooldown();
        }

        public override float GetAttackSequenceDuration()
        {
            SummonAIBehaviour prefab = variants.GetPrefab(ResolveNativeElement());
            var module = prefab.AttackModule as OvidSummonAttackModule;
            return module != null ? module.EnterDuration + DurationValue + module.ExitDuration : 0f;
        }

        public virtual void Update() => TickNative(Time.deltaTime, Time.smoothDeltaTime);

        public void TickNative(float deltaTime, float smoothDeltaTime)
        {
            if (replica || !configured || cancelling) return;
            if (!SummonPose.Finite(deltaTime) || deltaTime < 0f || !SummonPose.Finite(smoothDeltaTime) || smoothDeltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (!gameObject.activeInHierarchy || !CanAttack) { CancelPet(); return; }
            if (NativeRuntime == null || !NativeRuntime.IsInitialized) { CancelPet(); return; }
            uint version = generation;
            EnsureSummon();
            if (Cancelled(version)) return;
            if (attackSnapshot == null && _summonAI != null && ResolveNativeElement() != element)
            {
                Vector3 position = _summonAI.transform.position;
                CancelPet();
                version = generation;
                EnsureSummon(position);
            }
            if (Cancelled(version) || _summonAI == null) return;
            _summonAI.UpdateProgressionScaler();
            _summonAI.TickNative(deltaTime, smoothDeltaTime);
            if (Cancelled(version) || _summonAI == null) return;
            PresentationPoseChanged?.Invoke(new SummonPresentationPose
            {
                WeaponId = ID, PetId = petId, PhaseSequence = phaseSequence, PoseSequence = ++poseSequence,
                Pose = _summonAI.Mover.CapturePose()
            });
            Cancelled(version);
        }

        private void EnsureSummon(Vector3? position = null)
        {
            if (_summonAI != null || cancelling || !gameObject.activeInHierarchy) return;
            uint version = generation;
            ulong identity = nextPetId();
            if (Cancelled(version) || identity == 0) return;
            element = ResolveNativeElement();
            SummonAIBehaviour summon = Checkout(element);
            _summonAI = summon;
            petId = identity;
            phaseSequence = poseSequence = 0;
            summon.transform.position = position ?? player.transform.position;
            summon.transform.rotation = Quaternion.identity;
            summon.Init(this);
            summon.Deactivated += HandleSummonDeactivated;
            summon.gameObject.SetActive(true);
            if (Cancelled(version) || _summonAI != summon) return;
            summon.StartSimulation();
            Cancelled(version);
        }

        private SummonAIBehaviour Checkout(AttackElement requested)
        {
            SummonAIBehaviour summon = variants.GetOrCreate(requested, checkoutRoot, false);
            summon.gameObject.SetActive(false);
            // A summon follows its owner through the mover; parenting it to the player would apply movement twice.
            summon.transform.SetParent(null, true);
            SummonAIBehaviour prefab = variants.GetPrefab(requested);
            summon.transform.localScale = prefab.transform.localScale;
            return summon;
        }

        private AttackElement ResolveNativeElement()
        {
            GasDamageType type = attackSnapshot != null ? attackSnapshot.Stats.DamageType :
                NativeRuntime.Stats.CreateSnapshot().DamageType;
            return variants.ResolveElement(type == GasDamageType.Fire ? AttackElement.Fire :
                type == GasDamageType.Poison ? AttackElement.Poison : AttackElement.Default);
        }

        public override void Attack()
        {
            if (!IsAttackReady || _summonAI == null) return;
            _summonAI.RequestAttack();
        }

        internal bool TryBeginNativeAttack()
        {
            if (!IsAttackReady || _summonAI == null) return false;
            uint version = generation;
            AttackSnapshot snapshot = BeginNativeGasAttack();
            if (Cancelled(version) || _summonAI == null || snapshot.IsDisposed)
            {
                snapshot.Dispose();
                return false;
            }
            attackSnapshot = snapshot;
            _summonAI.UpdateProgressionScaler();
            return true;
        }

        internal void CompleteNativeAttack()
        {
            AttackSnapshot ended = attackSnapshot;
            attackSnapshot = null;
            if (ended != null) SetLastAttackTime();
            try { _summonAI?.SetPhase(SummonPhase.Positioning); }
            finally { ended?.Dispose(); }
        }

        public virtual void SetLastAttackTime() { if (configured) cooldownStartedAt = ReadClock(); }

        internal void NotifyPhaseChanged()
        {
            if (_summonAI == null || replica) return;
            phaseSequence++;
            poseSequence = 0;
            PresentationStateChanged?.Invoke(CapturePresentationState());
        }

        public SummonPresentationState CapturePresentationState()
        {
            if (_summonAI == null || petId == 0) return default;
            return new SummonPresentationState
            {
                WeaponId = ID, PetId = petId, PhaseSequence = phaseSequence,
                Phase = _summonAI.Phase, PhaseElapsedSeconds = _summonAI.PhaseElapsedSeconds,
                AttackEventId = _summonAI.Phase >= SummonPhase.AttackEnter && attackSnapshot != null ?
                    attackSnapshot.Context.EventId.Value : 0,
                Element = element, Stats = CurrentPresentationStats, Pose = _summonAI.Mover.CapturePose()
            };
        }

        public void QueryTargets(Vector2 position, float minRadius, float maxRadius, List<SummonTarget> results)
        {
            results.Clear();
            if (replica || !configured) return;
            queryTargets(position, minRadius, maxRadius, results);
            float minSquared = minRadius * minRadius, maxSquared = maxRadius * maxRadius;
            results.RemoveAll(target => !target.IsAvailable ||
                (target.Position - position).sqrMagnitude < minSquared ||
                (target.Position - position).sqrMagnitude > maxSquared);
        }

        private bool Cancelled(uint version)
        {
            if (!gameObject.activeInHierarchy) { CancelPet(); return true; }
            return version != generation || cancelling;
        }

        private void HandleSummonDeactivated(SummonAIBehaviour summon)
        {
            if (summon == _summonAI) CancelPet(true);
        }

        private void CancelPet(bool externallyDeactivated = false)
        {
            if (cancelling) return;
            cancelling = true;
            generation++;
            SummonAIBehaviour previous = _summonAI;
            ulong previousPetId = petId;
            AttackSnapshot previousAttack = attackSnapshot;
            _summonAI = null;
            petId = 0;
            attackSnapshot = null;
            phaseSequence = poseSequence = 0;
            try
            {
                if (previous != null)
                {
                    previous.Deactivated -= HandleSummonDeactivated;
                    previous.Dispose();
                }
                if (previousAttack != null && configured) SetLastAttackTime();
                if (previousPetId != 0) PresentationTerminated?.Invoke(new SummonPresentationTermination(ID, previousPetId));
            }
            finally
            {
                previousAttack?.Dispose();
                if (previous != null)
                {
                    if (externallyDeactivated || !gameObject.activeInHierarchy)
                    {
                        variants.Discard(previous);
                        Destroy(previous.gameObject);
                    }
                    else variants.Return(previous);
                }
                cancelling = false;
            }
        }

        public void InitializePresentationReplica(uint weaponId, PlayerMovement owner)
        {
            CancelPet();
            ConfigureOwner(owner);
            _id = weaponId;
            replica = true;
            configured = false;
            InitializePool();
        }

        public bool ApplyPresentationState(SummonPresentationState state, float age)
        {
            if (!replica || !gameObject.activeInHierarchy || !state.IsValid || state.WeaponId != ID ||
                !SummonPose.Finite(age) || age < 0f || !SummonPose.Finite(state.PhaseElapsedSeconds + age)) return false;
            if (petId != 0 && petId != state.PetId) return false;
            if (petId != 0 && unchecked((int)(state.PhaseSequence - phaseSequence)) <= 0) return false;
            if (_summonAI != null && element != state.Element) CancelPet();
            if (_summonAI == null)
            {
                element = state.Element;
                _summonAI = Checkout(element);
                petId = state.PetId;
                _summonAI.InitPresentation(this, state.Stats);
                _summonAI.Deactivated += HandleSummonDeactivated;
                _summonAI.gameObject.SetActive(true);
                if (_summonAI == null) return false;
            }
            phaseSequence = state.PhaseSequence;
            poseSequence = 0;
            _summonAI.ApplyPresentation(state.Phase, state.PhaseElapsedSeconds + age, state.Stats, state.Pose);
            return _summonAI != null;
        }

        public bool ApplyPresentationPose(SummonPresentationPose pose)
        {
            if (!replica || _summonAI == null || pose.WeaponId != ID || pose.PetId != petId ||
                pose.PhaseSequence != phaseSequence || pose.PoseSequence == 0 ||
                unchecked((int)(pose.PoseSequence - poseSequence)) <= 0 || !pose.Pose.IsFinite) return false;
            poseSequence = pose.PoseSequence;
            _summonAI.ApplyPose(pose.Pose);
            return true;
        }

        public void TickPresentation(float deltaTime)
        {
            if (!replica || !SummonPose.Finite(deltaTime) || deltaTime < 0f) return;
            _summonAI?.TickPresentation(deltaTime);
        }

        public bool TerminatePresentation(ulong identity)
        {
            if (!replica || identity == 0 || identity != petId) return false;
            CancelPet();
            return true;
        }

        public void DisposePresentationReplica() { CancelPet(); replica = false; }
        protected override void Dispose() => CancelPet();
        private void OnDisable() => CancelPet();
        private void OnDestroy() { CancelPet(true); clock = null; nextPetId = null; queryTargets = null; configured = false; }

        private double ReadClock()
        {
            double now = clock != null ? clock() : throw new InvalidOperationException("Summon requires a session clock.");
            ValidateTime(now);
            return now;
        }

        private static void ValidateTime(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    // The emitter can be disabled independently of its GameObject (for example by a Build lock).
    // This always-enabled child only forwards hierarchy lifetime; it owns no gameplay state.
    internal sealed class SummonLifetimeAnchor : MonoBehaviour
    {
        public Action Deactivated;
        private void OnDisable() => Deactivated?.Invoke();
    }
}
