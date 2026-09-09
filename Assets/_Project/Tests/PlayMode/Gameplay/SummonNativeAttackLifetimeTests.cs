using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Animancer;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Helpers;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GasAttackStats = MonsterSupergroup.GAS.AttackStats;
using GasDamageType = MonsterSupergroup.GAS.DamageType;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class SummonNativeAttackLifetimeTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<SummonTarget> targets = new List<SummonTarget>();
        private readonly List<SummonPresentationState> phases = new List<SummonPresentationState>();
        private readonly List<SummonPresentationTermination> terminated = new List<SummonPresentationTermination>();
        private PlayerMovement owner;
        private PlayerBuildRuntime build;
        private OvidSummonAttackBehaviour emitterPrefab;
        private OvidSummonAttackBehaviour weapon;
        private SummonAIBehaviour petPrefab;
        private WeaponData definition;
        private double now;
        private ulong identity;
        private int queryCount;
        private int rootCount;

        [SetUp]
        public void SetUp()
        {
            rootCount = queryCount = 0;
            Create("Summon Test Pool").AddComponent<PoolManager>().Init();
            owner = Create("Summon Test Owner", false).AddComponent<PlayerMovement>();
            owner.AttacksParent = Create("Summon Test Weapons").transform;
            build = owner.gameObject.AddComponent<PlayerBuildRuntime>();
            build.Initialize(owner, new FixedRandom());
            build.SetWeaponExecutionEnabled(false);
            petPrefab = CreatePet("Summon Test Pet");
            emitterPrefab = Create("Summon Test Emitter", false).AddComponent<OvidSummonAttackBehaviour>();
            var variants = new SummonAIVariants();
            SetField(variants, "defaultPrefab", petPrefab);
            SetField(variants, "firePrefab", CreatePet("Summon Test Fire Pet"));
            SetField(variants, "poisonPrefab", CreatePet("Summon Test Poison Pet"));
            SetField(emitterPrefab, "variants", variants);
            SetField(emitterPrefab, "cacoonStateTime", 60f);
            definition = ScriptableObject.CreateInstance<WeaponData>();
            objects.Add(definition);
            definition.ID = 402;
            definition.WeaponPrefab = emitterPrefab;
            definition.modifierFlags = (ModifierFlags)int.MaxValue;
            definition.ConfigureNativeGas(new GasAttackStats
            {
                damage = 24, speed = 0.2f, size = 1f, duration = 1f, projectileCount = 1,
                critMultiplier = 1f, critRate = 0f
            }, CombatTags.Attack, new WeaponPresentationSettings());
            weapon = (OvidSummonAttackBehaviour)build.EquipWeapon(definition);
            weapon.PresentationStateChanged += phases.Add;
            weapon.PresentationTerminated += terminated.Add;
            weapon.NativeAttackStarted += (_, __) => rootCount++;
            now = 100d;
            identity = 500;
        }

        [TearDown]
        public void TearDown()
        {
            build?.ClearBuild();
            for (int index = objects.Count - 1; index >= 0; index--)
                if (objects[index] != null) UnityEngine.Object.DestroyImmediate(objects[index]);
            objects.Clear();
            targets.Clear();
            phases.Clear();
            terminated.Clear();
            PoolManager.Instance = null;
        }

        [Test]
        public void SimulationRequiresExplicitClockAndCreatesOnePetWithoutAnAttackRoot()
        {
            weapon.TickNative(1f, 1f);
            Assert.That(weapon.ActiveSummon, Is.Null);
            Bind();
            weapon.TickNative(0.1f, 0.1f);
            SummonAIBehaviour original = weapon.ActiveSummon;
            Assert.That(original, Is.Not.Null);
            Assert.That(original.transform.parent, Is.Null, "AI following must not also inherit Owner translation.");
            Assert.That(original.Phase, Is.EqualTo(SummonPhase.Cocoon));
            Assert.That(weapon.MaturityAt, Is.EqualTo(160d));
            Assert.That(rootCount, Is.Zero);
            Assert.That(queryCount, Is.Zero);
            weapon.TickNative(0.1f, 0.1f);
            Assert.That(weapon.ActiveSummon, Is.SameAs(original));
            Assert.That(phases.Count, Is.EqualTo(1), "Ordinary ticks must not emit reliable phase spam.");
            Assert.That(typeof(SummonAIBehaviour).GetMethod("Update"), Is.Null, "Only the emitter may drive the FSM.");
        }

        [TestCase(161d, SummonPhase.Birth, 1f, false)]
        [TestCase(165d, SummonPhase.Positioning, 0f, false)]
        [TestCase(170d, SummonPhase.Positioning, 0f, true)]
        public void ReconnectSeeksMaturityAndPreservesOfflineOrdinaryCooldown(double reconnectAt,
            SummonPhase expected, float birthAge, bool ready)
        {
            now = reconnectAt;
            Bind(160d);
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(expected));
            Assert.That(weapon.IsAttackReady, Is.EqualTo(ready));
            if (expected == SummonPhase.Birth)
            {
                Assert.That(weapon.ActiveSummon.PhaseElapsedSeconds, Is.EqualTo(birthAge).Within(0.001f));
                Assert.That(CurrentAnimation(weapon.ActiveSummon).Time, Is.EqualTo(birthAge).Within(0.001));
            }
            else Assert.That(phases.Any(item => item.Phase == SummonPhase.Birth), Is.False);
            Assert.That(rootCount, Is.Zero);
        }

        [Test]
        public void BirthEndsAtAbsoluteDeadlineAndInitialCooldownDoesNotRestartAtNextTick()
        {
            Bind(100d);
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.Birth));
            now = 103d;
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.Birth));
            now = 104d;
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.Positioning));
            Assert.That(weapon.LastAttackElapsedTime, Is.EqualTo(0.2f).Within(0.001f));
            now = 108.8d;
            Assert.That(weapon.IsAttackReady, Is.True);
        }

        [Test]
        public void RepeatedBuildBindingKeepsPetAndRootAndExplicitUnbindReleasesClosures()
        {
            StartAttack();
            SummonAIBehaviour pet = weapon.ActiveSummon;
            AttackSnapshot snapshot = weapon.CurrentSnapshot;
            ulong petId = weapon.PetId;
            Bind(999d);
            Assert.That(weapon.ActiveSummon, Is.SameAs(pet));
            Assert.That(weapon.CurrentSnapshot, Is.SameAs(snapshot));
            Assert.That(weapon.MaturityAt, Is.Zero);
            Assert.That(weapon.PetId, Is.EqualTo(petId));
            weapon.UnbindSimulation();
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(weapon.ActiveSummon, Is.Null);
            Assert.That(weapon.HasSimulationBinding, Is.False);
            Assert.That(GetField<object>(weapon, "clock"), Is.Null);
            Assert.That(GetField<object>(weapon, "queryTargets"), Is.Null);
            weapon.TickNative(2f, 2f);
            Assert.That(weapon.ActiveSummon, Is.Null);
            Bind(0d);
            weapon.RestoreCooldownRemaining(2f);
            Assert.That(weapon.IsAttackReady, Is.False);
            now += 2d;
            Assert.That(weapon.IsAttackReady, Is.True);
        }

        [Test]
        public void EnterMainExitShareOneFrozenRootAndCooldownStartsOnlyAfterExit()
        {
            StartAttack();
            AttackSnapshot snapshot = weapon.CurrentSnapshot;
            Assert.That(snapshot.Stats.Damage, Is.EqualTo(24));
            Assert.That(weapon.GetAttackSequenceDuration(), Is.EqualTo(2.6333334f).Within(0.001f));
            GasAttackStats changed = weapon.NativeRuntime.Stats.BaseStats;
            changed.damage = 99;
            changed.duration = 9f;
            changed.projectileCount = 5;
            weapon.NativeRuntime.Stats.SetBaseStats(changed);
            weapon.NativeRuntime.RefreshStats();
            Step(1f, 1f);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.AttackMain));
            Assert.That(phases.Last().Stats.Duration, Is.EqualTo(1f));
            Assert.That(phases.Last().Stats.ProjectileCount, Is.EqualTo(1));
            Step(1f, 1f);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.AttackExit));
            Assert.That(snapshot.IsDisposed, Is.False);
            Assert.That(weapon.LastAttackElapsedTime, Is.Zero);
            Assert.That(rootCount, Is.EqualTo(1));
            Step(0.64f, 0.64f);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.Positioning));
            Assert.That(weapon.LastAttackElapsedTime, Is.Zero.Within(0.001f));
            Assert.That(phases.Last().AttackEventId, Is.Zero);
            now += 4.9d;
            Assert.That(weapon.IsAttackReady, Is.False);
            now += 0.1d;
            Assert.That(weapon.IsAttackReady, Is.True);
        }

        [UnityTest]
        public IEnumerator OvertimeNativeHitRetainsSourceExitGraceUntilLaserOrAttackEnds()
        {
            MeleeTestTarget victim = AddTarget("Victim", new Vector2(6f, 0f));
            Bind(0d);
            weapon.TickNative(0f, 0f);
            AttackSnapshot snapshot = weapon.CurrentSnapshot;
            var module = (OvidSummonAttackModule)weapon.ActiveSummon.AttackModule;
            var box = (PlayerAttackOvertimeHitBox)module.HitBox;
            box.SetHitInterval(0.02f);
            box.Toggle(true);
            Trigger(box, "OnTriggerEnter2D", victim.GetComponent<Collider2D>());
            Assert.That(victim.Health, Is.EqualTo(976));
            GasAttackStats changed = weapon.NativeRuntime.Stats.BaseStats;
            changed.damage = 100;
            weapon.NativeRuntime.Stats.SetBaseStats(changed);
            weapon.NativeRuntime.RefreshStats();
            Step(1f, 1f);
            Step(1f, 1f);
            box.Toggle(false);
            Trigger(box, "OnTriggerExit2D", victim.GetComponent<Collider2D>());
            // Source Exit disables colliders at .333s, but their Laser ancestor only at .633s.
            // Its existing 1.5s exit timeout must still permit repeat hits during this short tail.
            yield return new WaitForSeconds(0.08f);
            Assert.That(victim.NativeHitCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(victim.Health, Is.EqualTo(1000 - victim.NativeHitCount * 24));
            Assert.That(victim.LegacyDamageCalls, Is.Zero);
            Assert.That(snapshot.IsDisposed, Is.False);
            Step(0.64f, 0.64f);
            int hitsAtExit = victim.NativeHitCount;
            yield return new WaitForSeconds(0.08f);
            Assert.That(victim.NativeHitCount, Is.EqualTo(hitsAtExit));
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [Test]
        public void MainUsesOneSmoothDeltaTickAndFrozenBossHeading()
        {
            MeleeTestTarget victim = AddTarget("Boss", new Vector2(6f, 0f), true);
            Bind(0d);
            weapon.TickNative(0f, 0f);
            Step(1f, 0.01f);
            Step(0.2f, 0.1f);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.AttackMain));
            Assert.That(weapon.ActiveSummon.PhaseElapsedSeconds, Is.EqualTo(0.1f).Within(0.001f));
            float expectedHeading = GetField<float>(weapon.ActiveSummon.AttackModule, "targetAngle");
            victim.transform.position = new Vector3(-6f, 0f, 0f);
            Step(0.2f, 0.1f);
            Assert.That(GetField<float>(weapon.ActiveSummon.AttackModule, "targetAngle"), Is.EqualTo(expectedHeading));
            Assert.That(weapon.ActiveSummon.PhaseElapsedSeconds, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(rootCount, Is.EqualTo(1));
        }

        [Test]
        public void ZeroMainDurationStillRetainsEnterAndExit()
        {
            GasAttackStats stats = weapon.NativeRuntime.Stats.BaseStats;
            stats.duration = 0f;
            weapon.NativeRuntime.Stats.SetBaseStats(stats);
            weapon.NativeRuntime.RefreshStats();
            StartAttack();
            AttackSnapshot snapshot = weapon.CurrentSnapshot;
            Step(1f, 1f);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.AttackExit));
            Assert.That(snapshot.IsDisposed, Is.False);
            Step(0.64f, 0.64f);
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [TestCase("Pet")]
        [TestCase("Emitter")]
        [TestCase("Parent")]
        [TestCase("Unbind")]
        [TestCase("Selection")]
        public void CancellationTerminatesBeforePbrRetiresTheRoot(string cause)
        {
            StartAttack();
            AttackSnapshot snapshot = weapon.CurrentSnapshot;
            var order = new List<string>();
            weapon.PresentationTerminated += _ =>
            {
                Assert.That(snapshot.IsDisposed, Is.False);
                order.Add("Terminal");
            };
            build.NativeAttackCompleted += (_, __, ___) => order.Add("RootCompleted");
            Assert.That(weapon.enabled, Is.False, "Exercise disabled-emitter hierarchy cleanup.");
            switch (cause)
            {
                case "Pet": weapon.ActiveSummon.gameObject.SetActive(false); break;
                case "Emitter": weapon.gameObject.SetActive(false); break;
                case "Parent": owner.AttacksParent.gameObject.SetActive(false); break;
                case "Unbind": weapon.UnbindSimulation(); break;
                case "Selection":
                    owner.SetUpgradeSelectionLocked(true);
                    weapon.TickNative(0f, 0f);
                    break;
            }
            Assert.That(weapon.ActiveSummon, Is.Null);
            Assert.That(snapshot.IsDisposed, Is.True);
            typeof(PlayerBuildRuntime).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(build, null);
            Assert.That(order, Is.EqualTo(new[] { "Terminal", "RootCompleted" }));
            Assert.That(terminated.Count, Is.EqualTo(1));
        }

        [Test]
        public void SynchronousFirstPhaseCancellationCannotLeaveAWorldPetOrStartAnAttack()
        {
            AddTarget("Victim", new Vector2(6f, 0f));
            Bind(0d);
            weapon.PresentationStateChanged += _ => weapon.gameObject.SetActive(false);
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.ActiveSummon, Is.Null);
            Assert.That(weapon.CurrentSnapshot, Is.Null);
            Assert.That(terminated.Count, Is.EqualTo(1));
            Assert.That(rootCount, Is.Zero);
        }

        [Test]
        public void SynchronousAttackStartCancellationCannotLeaveAnUntrackedSnapshot()
        {
            AddTarget("Victim", new Vector2(6f, 0f));
            Bind(0d);
            weapon.NativeAttackStarted += (_, __) => weapon.gameObject.SetActive(false);
            int completed = 0;
            build.NativeAttackCompleted += (_, __, ___) => completed++;
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.ActiveSummon, Is.Null);
            Assert.That(weapon.CurrentSnapshot, Is.Null);
            typeof(PlayerBuildRuntime).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(build, null);
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(phases.Any(phase => phase.Phase == SummonPhase.AttackEnter), Is.False);
        }

        [Test]
        public void ElementChangesReplaceOneIdlePetWithoutRestartingMaturity()
        {
            Bind();
            weapon.TickNative(0f, 0f);
            SummonAIBehaviour first = weapon.ActiveSummon;
            double deadline = weapon.MaturityAt;
            GasAttackStats stats = weapon.NativeRuntime.Stats.BaseStats;
            stats.damageType = GasDamageType.Fire;
            weapon.NativeRuntime.Stats.SetBaseStats(stats);
            weapon.NativeRuntime.RefreshStats();
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.ActiveSummon, Is.Not.SameAs(first));
            Assert.That(weapon.CapturePresentationState().Element, Is.EqualTo(AttackElement.Fire));
            Assert.That(weapon.MaturityAt, Is.EqualTo(deadline));
            Assert.That(terminated.Count, Is.EqualTo(1));
            Assert.That(rootCount, Is.Zero);
        }

        [Test]
        public void DenseTargetsAreScoredInSourceChunksBeforeAnyNativeRootExists()
        {
            AddTarget("Isolated", new Vector2(-6f, 0f));
            AddTarget("Cluster A", new Vector2(6f, 0f));
            AddTarget("Cluster B", new Vector2(6f, 1f));
            AddTarget("Cluster C", new Vector2(6f, -1f));
            Bind(0d);
            weapon.TickNative(0f, 0f);
            Assert.That(rootCount, Is.Zero);
            weapon.TickNative(0f, 0f);
            weapon.TickNative(0f, 0f);
            Assert.That(rootCount, Is.Zero);
            weapon.TickNative(0f, 0f);
            Assert.That(rootCount, Is.EqualTo(1));
            SummonTarget selected = GetField<SummonTarget>(weapon.ActiveSummon.AttackModule, "target");
            Assert.That(selected.Position.x, Is.GreaterThan(0f));
        }

        [Test]
        public void PresentationReplicasSeekAndRejectStalePhasePoseWithoutAiOrGas()
        {
            OvidSummonAttackBehaviour replica = CreateReplica();
            SummonPresentationState state = State(SummonPhase.Birth, 1);
            state.PhaseElapsedSeconds = 0.5f;
            Assert.That(replica.ApplyPresentationState(state, 0.75f), Is.True);
            SummonAIBehaviour pet = replica.ActiveSummon;
            Assert.That(CurrentAnimation(pet).Time, Is.EqualTo(1.25d).Within(0.001d));
            Assert.That(pet.IsPresentation, Is.True);
            Assert.That(pet.GetComponent<Rigidbody2D>().simulated, Is.False);
            Assert.That(replica.NativeRuntime, Is.Null);
            Assert.That(((OvidSummonAttackModule)pet.AttackModule).HitBox.enabled, Is.False);
            replica.TickPresentation(0.25f);
            Assert.That(CurrentAnimation(pet).Time, Is.EqualTo(1.5d).Within(0.001d));
            state = State(SummonPhase.AttackMain, 2);
            Assert.That(replica.ApplyPresentationState(state, 0.2f), Is.True);
            Assert.That(replica.ApplyPresentationState(state, 0f), Is.False);
            var pose = new SummonPresentationPose
            {
                PetId = state.PetId, WeaponId = 402, PhaseSequence = 1, PoseSequence = 9,
                Pose = new SummonPose { Position = Vector3.one, MoveAnimationSpeed = 1f }
            };
            Assert.That(replica.ApplyPresentationPose(pose), Is.False);
            pose.PhaseSequence = 2;
            Assert.That(replica.ApplyPresentationPose(pose), Is.True);
            Assert.That(replica.ApplyPresentationPose(pose), Is.False);
            Assert.That(pet.transform.position, Is.EqualTo(Vector3.one));
            Assert.That(rootCount, Is.Zero);
            Assert.That(queryCount, Is.Zero);
            Assert.That(replica.TerminatePresentation(state.PetId), Is.True);
            Assert.That(replica.ActiveSummon, Is.Null);
        }

        [Test]
        public void RemoteExternalDisableTerminatesTrackingImmediately()
        {
            OvidSummonAttackBehaviour replica = CreateReplica();
            Assert.That(replica.ApplyPresentationState(State(SummonPhase.AttackExit, 1), 0f), Is.True);
            ulong petId = replica.PetId;
            int ends = 0;
            replica.PresentationTerminated += _ => ends++;
            replica.ActiveSummon.gameObject.SetActive(false);
            Assert.That(replica.PetId, Is.Zero);
            Assert.That(replica.ActiveSummon, Is.Null);
            Assert.That(replica.TerminatePresentation(petId), Is.False);
            Assert.That(ends, Is.EqualTo(1));
        }

        [Test]
        public void PooledPetRebindsOwnerAndSurfaceAndCannotReuseOldHitCallback()
        {
            StartAttack();
            SummonAIBehaviour original = weapon.ActiveSummon;
            var surface = original.GetComponentInChildren<SetAtSurfaceLevel>(true);
            Assert.That(GetField<Transform>(surface, "owner"), Is.EqualTo(owner.transform));
            weapon.UnbindSimulation();
            Assert.That(GetField<Transform>(surface, "owner"), Is.Null);
            Assert.That(original.GetComponent<Rigidbody2D>().linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(original.WeaponBehaviour, Is.Null);
            Assert.That(GetField<Action<IDamageable>>(((OvidSummonAttackModule)original.AttackModule).HitBox, "_onHit"), Is.Null);
            var otherOwner = Create("Other Summon Owner", false).AddComponent<PlayerMovement>();
            otherOwner.transform.position = new Vector3(3f, 4f, 7f);
            weapon.ConfigureOwner(otherOwner);
            targets.Clear();
            Bind(0d);
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.ActiveSummon, Is.SameAs(original));
            Assert.That(GetField<Transform>(surface, "owner"), Is.EqualTo(otherOwner.transform));
            Assert.That(surface.transform.position.z, Is.EqualTo(7f));
        }

        private void StartAttack()
        {
            AddTarget("Summon Victim", new Vector2(6f, 0f));
            Bind(0d);
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.CurrentSnapshot, Is.Not.Null);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.AttackEnter));
        }

        private void Bind(double deadline = double.NaN) => weapon.ConfigureSimulation(() => now, () => ++identity,
            (_, __, ___, results) => { queryCount++; results.AddRange(targets); }, deadline);

        private void Step(float delta, float smooth)
        {
            now += delta;
            weapon.TickNative(delta, smooth);
        }

        private SummonAIBehaviour CreatePet(string name)
        {
            GameObject root = Create(name, false);
            var ai = root.AddComponent<SummonAIBehaviour>();
            var animator = root.AddComponent<Animator>();
            var animancer = root.AddComponent<AnimancerComponent>();
            animancer.Animator = animator;
            var body = root.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            var mover = root.AddComponent<OvidSummonMover>();
            var iso = new GameObject("Iso").transform;
            iso.SetParent(root.transform, false);
            var rotation = new GameObject("Rotation").transform;
            rotation.SetParent(iso, false);
            var surface = new GameObject("Surface");
            surface.transform.SetParent(rotation, false);
            surface.AddComponent<SetAtSurfaceLevel>();
            SetField(mover, "rb", body);
            SetField(mover, "isoPivot", iso);
            SetField(mover, "rotationPivot", rotation);
            SetField(mover, "accelerationCurve", new CustomAnimationCurve());
            SetField(mover, "moveAnimation", Clip(1.0333334f));
            var idle = root.AddComponent<OvidSummonIdleModule>();
            SetField(idle, "mover", mover);
            SetField(idle, "idleAnimation", Clip(4f));
            SetField(idle, "birthAnimation", Clip(3.8f));
            var positioning = root.AddComponent<OvidSummonPositioningModule>();
            SetField(positioning, "mover", mover);
            var attack = root.AddComponent<OvidSummonAttackModule>();
            SetField(attack, "mover", mover);
            SetField(attack, "attackEnterAnimation", Clip(1f));
            SetField(attack, "attackLoopAnimation", Clip(0.33333334f));
            SetField(attack, "attackExitAnimation", Clip(0.6333334f));
            var box = root.AddComponent<PlayerAttackOvertimeHitBox>();
            box.collider = root.AddComponent<CircleCollider2D>();
            SetField(box, "timeoutAfterExit", 1.5f);
            SetField(attack, "hitBox", box);
            SetField(ai, "idlingStateModule", idle);
            SetField(ai, "positioningStateModule", positioning);
            SetField(ai, "attackingStateModule", attack);
            SetField(ai, "animancer", animancer);
            SetField(ai, "progressionScaler", root.AddComponent<AttackProgressionScaler>());
            return ai;
        }

        private OvidSummonAttackBehaviour CreateReplica()
        {
            var replica = UnityEngine.Object.Instantiate(emitterPrefab);
            objects.Add(replica.gameObject);
            replica.InitializePresentationReplica(402, owner);
            replica.enabled = false;
            replica.gameObject.SetActive(true);
            return replica;
        }

        private static SummonPresentationState State(SummonPhase phase, uint sequence) => new SummonPresentationState
        {
            WeaponId = 402, PetId = 700, PhaseSequence = sequence, Phase = phase,
            AttackEventId = phase >= SummonPhase.AttackEnter ? 900UL : 0UL,
            Stats = new ProjectilePresentationStats { Duration = 1f, ProjectileCount = 1, BaseProjectileCount = 1 },
            Pose = new SummonPose { Position = new Vector3(2f, 3f, 0f), MoveAnimationSpeed = 1f }
        };

        private MeleeTestTarget AddTarget(string name, Vector2 position, bool isBoss = false)
        {
            GameObject target = Create(name);
            target.transform.position = position;
            target.AddComponent<BoxCollider2D>();
            var result = target.AddComponent<MeleeTestTarget>();
            targets.Add(new SummonTarget(target.transform, isBoss));
            return result;
        }

        private ClipTransition Clip(float duration)
        {
            var clip = new AnimationClip();
            objects.Add(clip);
            clip.SetCurve("Iso", typeof(Transform), "m_LocalScale.x", AnimationCurve.Linear(0, 1, duration, 1));
            return new ClipTransition { Clip = clip, Speed = 1f };
        }

        private GameObject Create(string name, bool active = true)
        {
            var value = new GameObject(name);
            value.SetActive(active);
            objects.Add(value);
            return value;
        }

        private static AnimancerState CurrentAnimation(SummonAIBehaviour pet) => GetField<AnimancerState>(pet, "_phaseAnimation");
        private static void Trigger(BaseAttackHitBox box, string method, Collider2D target) =>
            typeof(PlayerAttackOvertimeHitBox).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(box, new object[] { target });

        private static FieldInfo FindField(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field;
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }

        private static void SetField(object target, string name, object value) => FindField(target, name).SetValue(target, value);
        private static T GetField<T>(object target, string name) => (T)FindField(target, name).GetValue(target);
        private sealed class FixedRandom : IRandomSource { public float Next01() => 0.99f; }
    }
}
