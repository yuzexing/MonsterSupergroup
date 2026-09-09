using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Animancer;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Helpers;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class SummonPresentationReplicaPlayModeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string VisualRoot = "Iso Rotation/Self Rotation";
        private const string WeaponPath = "Assets/_Project/Content/HellMaiden/NativeGAS/Ovid/Summon/MonoBehaviour/WeaponData_Ovid_Summon.asset";

        [UnityTest]
        public IEnumerator ThreeSourceVariantsRemainIndependentAndNeverCreateGameplayOrNetworkBodies()
        {
            using (var fixture = new Fixture())
            {
                foreach (AttackElement element in new[] { AttackElement.Default, AttackElement.Fire, AttackElement.Poison })
                {
                    ulong petId = (ulong)(100 + (int)element);
                    SummonAIBehaviour pet = fixture.Apply(State(petId, SummonPhase.AttackMain, 1, element), 0.15f);
                    Assert.That(pet.name, Does.Contain(element == AttackElement.Default ? "ButterflyAI" : "ButterflyAI" + element));
                    AssertPresentationOnly(pet);
                    Assert.That(pet.Mover, Is.SameAs(((OvidSummonIdleModule)pet.IdleModule).Mover));
                    Assert.That(pet.Mover.gameObject, Is.Not.SameAs(pet.gameObject),
                        "The actual source mover is a separate child, resolved through the existing module reference.");
                    Assert.That(pet.ProgressionScaler, Is.Not.Null);
                    Assert.That(pet.ProgressionScaler.gameObject, Is.Not.SameAs(pet.gameObject));
                    Assert.That(pet.WeaponBehaviour.OwnerPlayer, Is.SameAs(fixture.Movement));
                    Assert.That(pet.WeaponBehaviour.enabled, Is.False);
                    Assert.That(pet.WeaponBehaviour.CurrentSnapshot, Is.Null);
                    Assert.That(pet.WeaponBehaviour.NativeRuntime, Is.Null);
                    Assert.That(pet.WeaponBehaviour.HasSimulationBinding, Is.False);
                }
                Assert.That(fixture.Replica.ActivePetCount, Is.EqualTo(3));
                yield return new WaitForFixedUpdate();
                yield return null;
                Assert.That(fixture.Replica.ActivePetCount, Is.EqualTo(3));
                foreach (SummonAIBehaviour pet in fixture.Pets()) AssertPresentationOnly(pet);
                Assert.That(fixture.Attacks.GetComponentsInChildren<Mirror.NetworkIdentity>(true), Is.Empty);
                Assert.That(fixture.Attacks.GetComponentsInChildren<WeaponRuntimeBehaviour>(true), Is.Empty);
            }
        }

        [UnityTest]
        public IEnumerator ActualClipsSeekAllSixPhasesAtTheirOriginalAnimatorRoot()
        {
            using (var fixture = new Fixture())
            {
                SummonPhase[] phaseNames = { SummonPhase.Cocoon, SummonPhase.Birth, SummonPhase.Positioning,
                    SummonPhase.AttackEnter, SummonPhase.AttackMain, SummonPhase.AttackExit };
                float[] ages = { 0.7f, 1.4f, 0.2f, 0.75f, 0.2f, 0.4f };
                string[] clipNames = { "Cacoon", "Birth", "Move", "Attack_Enter", "Attack_Loop", "Attack_Exit" };
                SummonAIBehaviour original = null;
                for (int index = 0; index < phaseNames.Length; index++)
                {
                    SummonAIBehaviour pet = fixture.Apply(State(201, phaseNames[index], (uint)(index + 1)), ages[index]);
                    if (original == null) original = pet;
                    Assert.That(pet, Is.SameAs(original));
                    Assert.That(pet.Animancer.Animator.transform, Is.EqualTo(pet.transform.Find(VisualRoot)));
                    Assert.That(pet.PhaseClip(phaseNames[index]).Clip.name, Does.Contain(clipNames[index]));
                    Assert.That(CurrentAnimation(pet).Time, Is.EqualTo(ages[index]).Within(0.001f));
                    pet.Animancer.Evaluate(0f);
                    Transform laser = pet.transform.Find(VisualRoot + "/Buterfly_Attack Laser");
                    Assert.That(laser.gameObject.activeSelf, Is.EqualTo(phaseNames[index] >= SummonPhase.AttackEnter));
                    fixture.Replica.Tick(0f);
                    AssertPresentationOnly(pet);
                }
                SummonAIBehaviour ending = fixture.Apply(State(201, SummonPhase.AttackExit, 7), 0.6333334f);
                ending.Animancer.Evaluate(0f);
                Assert.That(ending.transform.Find(VisualRoot + "/Buterfly_Attack Laser").gameObject.activeSelf, Is.False,
                    "The actual Laser ancestor ends at the source Exit clip boundary.");
                fixture.Replica.Tick(0f);
                AssertPresentationOnly(ending);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RemotePoseUsesWorldCoordinatesAndNeitherOwnerMovementNorOtherPetMovesIt()
        {
            using (var fixture = new Fixture())
            {
                var firstState = State(301, SummonPhase.Positioning);
                var secondState = State(302, SummonPhase.AttackMain);
                secondState.Pose.Position = new Vector3(-4f, 2f, 0f);
                SummonAIBehaviour first = fixture.Apply(firstState, 0f);
                SummonAIBehaviour second = fixture.Apply(secondState, 0f);
                Assert.That(first.transform.parent, Is.Null);
                Vector3 initial = first.transform.position;
                fixture.Player.transform.position = new Vector3(30, 20, 0);
                fixture.Attacks.transform.position = new Vector3(-10, 50, 0);
                fixture.Attacks.transform.rotation = Quaternion.Euler(0, 0, 60);
                fixture.Replica.Tick(0.2f);
                yield return new WaitForFixedUpdate();
                Assert.That(first.transform.position, Is.EqualTo(initial));
                Assert.That(second.transform.position, Is.EqualTo(secondState.Pose.Position));
                var pose = new SummonPresentationPose
                {
                    WeaponId = 402, PetId = 301, PhaseSequence = 1, PoseSequence = 1,
                    Pose = new SummonPose
                    {
                        Position = new Vector3(9f, 7f, 0f), RotationPivotEuler = new Vector3(-20, 135, 0),
                        IsoLocalPosition = new Vector3(0.2f, 0.1f, 0), MoveAnimationSpeed = 1.5f
                    }
                };
                Assert.That(fixture.Replica.TryApplyPose(pose), Is.True);
                Assert.That(first.transform.position, Is.EqualTo(pose.Pose.Position));
                Assert.That(Quaternion.Angle(first.Mover.GetRotationPivot().localRotation,
                    Quaternion.Euler(pose.Pose.RotationPivotEuler)), Is.LessThan(0.001f));
                Assert.That(second.transform.position, Is.EqualTo(secondState.Pose.Position));
                Assert.That(fixture.Replica.TryApplyPose(pose), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator RemoteHitboxAndModuleCannotResolveAnyHitEvenWhileSourceClipEnablesLaser()
        {
            using (var fixture = new Fixture())
            {
                var targetObject = new GameObject("Summon Replica Safety Target");
                try
                {
                    var collider = targetObject.AddComponent<CircleCollider2D>();
                    collider.radius = 50f;
                    targetObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
                    var victim = targetObject.AddComponent<MeleeTestTarget>();
                    SummonAIBehaviour pet = fixture.Apply(State(401, SummonPhase.AttackMain), 0.2f);
                    var attack = (OvidSummonAttackModule)pet.AttackModule;
                    pet.Animancer.Evaluate(0f);
                    fixture.Replica.Tick(0f);
                    typeof(PlayerAttackOvertimeHitBox).GetMethod("OnTriggerEnter2D", Private)
                        .Invoke(attack.HitBox, new object[] { collider });
                    typeof(OvidSummonAttackModule).GetMethod("OnHit", Private).Invoke(attack, new object[] { victim });
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    Assert.That(victim.Health, Is.EqualTo(1000));
                    Assert.That(victim.NativeHitCount, Is.Zero);
                    Assert.That(victim.LegacyDamageCalls, Is.Zero);
                    AssertPresentationOnly(pet);
                }
                finally { UnityEngine.Object.DestroyImmediate(targetObject); }
            }
        }

        [UnityTest]
        public IEnumerator ReturnAndPoolReuseClearOldOwnerSurfaceBindingsAndPhaseState()
        {
            using (var fixture = new Fixture())
            {
                SummonAIBehaviour first = fixture.Apply(State(501, SummonPhase.AttackMain), 0.2f);
                var surfaces = first.GetComponentsInChildren<SetAtSurfaceLevel>(true);
                Assert.That(surfaces.Length, Is.EqualTo(2));
                Assert.That(surfaces.All(surface => BoundSurface(surface) == fixture.Movement.transform), Is.True);
                Assert.That(fixture.Replica.TryTerminate(402, 501), Is.True);
                Assert.That(first.gameObject.activeInHierarchy, Is.False);
                Assert.That(first.WeaponBehaviour, Is.Null);
                Assert.That(surfaces.All(surface => BoundSurface(surface) == null), Is.True);
                Assert.That(CurrentAnimation(first), Is.Null);
                fixture.Movement.transform.position = new Vector3(1, 2, 7);
                SummonAIBehaviour reused = fixture.Apply(State(502, SummonPhase.Birth), 0.6f);
                Assert.That(reused, Is.SameAs(first));
                Assert.That(reused.WeaponBehaviour.PetId, Is.EqualTo(502));
                Assert.That(CurrentAnimation(reused).Time, Is.EqualTo(0.6d).Within(0.001));
                Assert.That(surfaces.All(surface => BoundSurface(surface) == fixture.Movement.transform), Is.True);
                Assert.That(surfaces.All(surface => Mathf.Approximately(surface.transform.position.z, 7f)), Is.True);
                AssertPresentationOnly(reused);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator DisablingPetOrDisabledEmitterHierarchyRemovesViewsImmediately()
        {
            using (var fixture = new Fixture())
            {
                SummonAIBehaviour first = fixture.Apply(State(601, SummonPhase.AttackEnter), 0.2f);
                SummonAIBehaviour second = fixture.Apply(State(602, SummonPhase.AttackMain), 0.2f);
                first.gameObject.SetActive(false);
                Assert.That(fixture.Replica.ActivePetCount, Is.EqualTo(1));
                Assert.That(first.WeaponBehaviour, Is.Null);
                Assert.That(second.WeaponBehaviour.enabled, Is.False);
                fixture.Attacks.SetActive(false);
                Assert.That(fixture.Replica.ActivePetCount, Is.Zero);
                Assert.That(second.WeaponBehaviour, Is.Null);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator InvalidStateAndOldPoseCannotChangeTheCurrentAssetInstance()
        {
            using (var fixture = new Fixture())
            {
                SummonPresentationState valid = State(701, SummonPhase.AttackMain, 5);
                Assert.That(fixture.Replica.TryApplyState(valid, float.NaN), Is.False);
                var invalid = valid; invalid.WeaponId = 999;
                Assert.That(fixture.Replica.TryApplyState(invalid, 0), Is.False);
                SummonAIBehaviour pet = fixture.Apply(valid, 0.2f);
                Assert.That(fixture.Replica.TryApplyState(valid, 0), Is.False);
                invalid = valid; invalid.PhaseSequence = 4;
                Assert.That(fixture.Replica.TryApplyState(invalid, 0), Is.False);
                var pose = new SummonPresentationPose
                {
                    WeaponId = 402, PetId = 701, PhaseSequence = 4, PoseSequence = 9,
                    Pose = new SummonPose { Position = Vector3.one, MoveAnimationSpeed = 1f }
                };
                Assert.That(fixture.Replica.TryApplyPose(pose), Is.False);
                pose.PhaseSequence = 5;
                pose.Pose.Position.x = float.PositiveInfinity;
                Assert.That(fixture.Replica.TryApplyPose(pose), Is.False);
                Assert.That(pet.transform.position, Is.EqualTo(valid.Pose.Position));
                Assert.That(fixture.Replica.ActivePetCount, Is.EqualTo(1));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AgedEnterUsesAuthoredLaserActivationThenParticlesAdvanceWithoutAnotherSeek()
        {
            using (var fixture = new Fixture())
            {
                SummonAIBehaviour pet = fixture.Apply(State(901, SummonPhase.AttackEnter), .75f);
                ParticleSystem laser = pet.transform.Find(VisualRoot + "/Buterfly_Attack Laser").GetComponent<ParticleSystem>();
                ParticleSystem start = pet.transform.Find(VisualRoot + "/Buterfly_Attack Laser/Rotate/LaserStartBlue").GetComponent<ParticleSystem>();
                Assert.That(laser.time, Is.EqualTo(.75f - .53333336f).Within(.025f));
                Assert.That(start.time, Is.EqualTo(.75f - .6666667f).Within(.025f),
                    "The source off/on edge must restart LaserStartBlue, not age it from phase zero.");
                Assert.That(laser.isPlaying, Is.True);
                Assert.That(start.isPlaying, Is.True);
                float before = laser.time;
                fixture.Replica.Tick(.1f);
                Assert.That(laser.time, Is.EqualTo(before), "Presentation ticks must not manually advance or pause particles.");
                yield return null;
                yield return null;
                Assert.That(laser.time, Is.GreaterThan(before), "Unity must continue the original particle clock after catch-up.");
                Assert.That(laser.isPaused, Is.False);
                AssertPresentationOnly(pet);
            }
        }

        [UnityTest]
        public IEnumerator LateMainAndFollowingExitFollowTheActualSourceClipParticleTransitions()
        {
            using (var fixture = new Fixture())
            {
                // Native source directly plays Main after Enter. Use that real transition as
                // the reference: Animation may rebind a keyed particle ancestor between clips.
                SummonAIBehaviour source = fixture.Apply(State(912, SummonPhase.AttackEnter), 1f);
                ParticleSystem sourceLaser = source.transform.Find(VisualRoot + "/Buterfly_Attack Laser").GetComponent<ParticleSystem>();
                source.SetPhase(SummonPhase.AttackMain);
                source.Animancer.Evaluate(0f);
                sourceLaser.Simulate(.2f, false, false, false);
                if (sourceLaser.isPaused) sourceLaser.Play(false);
                SummonAIBehaviour pet = fixture.Apply(State(902, SummonPhase.AttackMain), .2f);
                ParticleSystem laser = pet.transform.Find(VisualRoot + "/Buterfly_Attack Laser").GetComponent<ParticleSystem>();
                Assert.That(laser.time, Is.EqualTo(sourceLaser.time).Within(.03f),
                    "A late baseline must use the source clip transition, not an assumed sum of particle ages.");
                source.SetPhase(SummonPhase.AttackExit);
                source.Animancer.Evaluate(0f);
                fixture.Apply(State(902, SummonPhase.AttackExit, 2), .2f);
                Assert.That(laser.time, Is.EqualTo(sourceLaser.time).Within(.001f),
                    "A live replica must retain the result of the source transition without adding packet transit time twice.");
                Assert.That(laser.isPlaying, Is.True);
                fixture.Apply(State(902, SummonPhase.AttackExit, 3), .6333334f);
                Assert.That(laser.gameObject.activeInHierarchy, Is.False);
                Assert.That(laser.particleCount, Is.Zero);
                Assert.That(laser.isPlaying, Is.False);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AgedBirthKeepsItsBurstAndDoesNotReplayHistoricalAudioOrRestartOnPresentationTicks()
        {
            using (var fixture = new Fixture())
            {
                SummonAIBehaviour pet = fixture.Apply(State(903, SummonPhase.Birth), 1.2f);
                ParticleSystem burst = pet.transform.Find(VisualRoot + "/Power_Burst_V3").GetComponent<ParticleSystem>();
                AudioSource audio = pet.transform.Find(VisualRoot + "/Power_Burst_V3/OrbitalBeamSmallYellow").GetComponent<AudioSource>();
                Assert.That(burst.time, Is.EqualTo(1.2f).Within(.025f));
                Assert.That(burst.isPaused, Is.False);
                Assert.That(audio.playOnAwake, Is.True, "Seek suppression cannot modify the source audio configuration.");
                Assert.That(audio.isPlaying, Is.False, "A late visual baseline must not replay the past Birth sound.");
                float before = burst.time;
                fixture.Replica.Tick(.5f);
                fixture.Replica.Tick(.5f);
                Assert.That(burst.time, Is.EqualTo(before));
                yield return null;
                yield return null;
                Assert.That(burst.time, Is.GreaterThan(before));
            }
        }

        [UnityTest]
        public IEnumerator LongPhaseAgesDoNotReplayExpiredBurstsOrIterateTheWholeCocoonDeadline()
        {
            using (var fixture = new Fixture())
            {
                SummonAIBehaviour cocoon = fixture.Apply(State(904, SummonPhase.Cocoon), 1000000f);
                Assert.That(CurrentAnimation(cocoon).Time, Is.EqualTo(1000000d));
                Assert.That(cocoon.GetComponentsInChildren<ParticleSystem>(true).All(particle => particle.particleCount == 0), Is.True);
                SummonAIBehaviour oldBirth = fixture.Apply(State(905, SummonPhase.Birth), 1000000f);
                ParticleSystem burst = oldBirth.transform.Find(VisualRoot + "/Power_Burst_V3").GetComponent<ParticleSystem>();
                Assert.That(burst.particleCount, Is.Zero);
                Assert.That(burst.isPlaying, Is.False, "An expired non-looping burst must not be Play()ed again after seek.");
                SummonAIBehaviour main = fixture.Apply(State(906, SummonPhase.AttackMain), 1000000f);
                ParticleSystem laser = main.transform.Find(VisualRoot + "/Buterfly_Attack Laser").GetComponent<ParticleSystem>();
                Assert.That(laser.time, Is.LessThan(25f), "Catch-up retains a bounded particle lifetime window, not a million-second simulation.");
                Assert.That(laser.isPlaying, Is.True);
                Assert.That(CurrentAnimation(main).Time, Is.EqualTo(1000000d));
                SummonAIBehaviour extreme = fixture.Apply(State(909, SummonPhase.AttackEnter), 0f);
                ParticleSystem extremeLaser = extreme.transform.Find(VisualRoot + "/Buterfly_Attack Laser").GetComponent<ParticleSystem>();
                var fast = extremeLaser.main;
                fast.simulationSpeed = 8f; // Instance-only numeric boundary, not a changed source asset.
                fixture.Apply(State(909, SummonPhase.AttackMain, 2), float.MaxValue / 4f);
                Assert.That(float.IsNaN(extremeLaser.time) || float.IsInfinity(extremeLaser.time), Is.False,
                    "Finite age and speed may have a product exceeding float; bounded catch-up must still stay finite.");
                Assert.That(extremeLaser.time, Is.LessThan(25f));
                Assert.That(extremeLaser.isPlaying, Is.True);
                yield return null;
                Assert.That(burst.isPlaying, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator ReturningBirthClearsParticlesAndReusedCocoonRestoresPrefabActivation()
        {
            using (var fixture = new Fixture())
            {
                SummonAIBehaviour birth = fixture.Apply(State(907, SummonPhase.Birth), .8f);
                ParticleSystem[] particles = birth.GetComponentsInChildren<ParticleSystem>(true);
                Assert.That(birth.transform.Find(VisualRoot + "/Power_Burst_V3").gameObject.activeSelf, Is.True);
                Assert.That(fixture.Replica.TryTerminate(402, 907), Is.True);
                Assert.That(particles.All(particle => particle.particleCount == 0 && !particle.isPlaying), Is.True);
                SummonAIBehaviour reused = fixture.Apply(State(908, SummonPhase.Cocoon), 59f);
                Assert.That(reused, Is.SameAs(birth));
                Assert.That(reused.transform.Find(VisualRoot + "/Power_Burst_V3").gameObject.activeSelf, Is.False,
                    "Cocoon does not key the Birth burst, so pool checkout must restore its source inactive default.");
                Assert.That(particles.All(particle => particle.particleCount == 0 && !particle.isPlaying), Is.True);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator DisposeCancelsEveryWorldPetAndLeavesTheOwnerObjectAlive()
        {
            using (var fixture = new Fixture())
            {
                SummonAIBehaviour first = fixture.Apply(State(801, SummonPhase.Cocoon), 1f);
                SummonAIBehaviour second = fixture.Apply(State(802, SummonPhase.AttackMain), 0.2f);
                fixture.Replica.Dispose();
                fixture.Replica.Dispose();
                Assert.That(fixture.Replica.ActivePetCount, Is.Zero);
                Assert.That(first.gameObject.activeInHierarchy, Is.False);
                Assert.That(second.gameObject.activeInHierarchy, Is.False);
                Assert.Throws<ObjectDisposedException>(() => fixture.Replica.TryApplyState(State(803, SummonPhase.Cocoon), 0f));
                yield return null;
                Assert.That(fixture.Attacks.GetComponentsInChildren<SummonAttackBehaviour>(true), Is.Empty);
                Assert.That(fixture.Player, Is.Not.Null);
            }
        }

        private static SummonPresentationState State(ulong pet, SummonPhase phase, uint sequence = 1,
            AttackElement element = AttackElement.Default) => new SummonPresentationState
        {
            WeaponId = 402, PetId = pet, PhaseSequence = sequence, Phase = phase,
            AttackEventId = phase >= SummonPhase.AttackEnter ? pet + 1000UL : 0UL,
            Element = element,
            Stats = new ProjectilePresentationStats { Duration = 1, ProjectileCount = 1, BaseProjectileCount = 1 },
            Pose = new SummonPose
            {
                Position = new Vector3(2f, 3f, 0f), RotationPivotEuler = new Vector3(0, 180f, 0),
                IsoLocalPosition = Vector3.zero, MoveAnimationSpeed = 1f
            }
        };

        private static AnimancerState CurrentAnimation(SummonAIBehaviour pet) =>
            (AnimancerState)typeof(SummonAIBehaviour).GetField("_phaseAnimation", Private).GetValue(pet);
        private static Transform BoundSurface(SetAtSurfaceLevel surface) =>
            (Transform)typeof(SetAtSurfaceLevel).GetField("owner", Private).GetValue(surface);

        private static void AssertPresentationOnly(SummonAIBehaviour pet)
        {
            Assert.That(pet.IsPresentation, Is.True);
            Assert.That(pet.GetComponent<Rigidbody2D>().simulated, Is.False);
            Assert.That(pet.GetComponentsInChildren<Mirror.NetworkIdentity>(true), Is.Empty);
            foreach (BaseAttackHitBox hitbox in pet.GetComponentsInChildren<BaseAttackHitBox>(true))
            {
                Assert.That(hitbox.enabled, Is.False);
                Assert.That(typeof(BaseAttackHitBox).GetField("_onHit", Private).GetValue(hitbox), Is.Null);
            }
            Assert.That(pet.GetComponentsInChildren<Collider2D>(true).All(collider => !collider.enabled), Is.True);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject pool;
            private readonly GameObject databaseObject;
            private readonly WeaponDB weapons;
            public GameObject Player { get; }
            public PlayerMovement Movement { get; }
            public GameObject Attacks { get; }
            public SummonPresentationReplica Replica { get; }

            public Fixture()
            {
                pool = new GameObject("Summon Replica Pool");
                pool.AddComponent<PoolManager>().Init();
                Player = new GameObject("Summon Replica Owner");
                Player.SetActive(false);
                Movement = Player.AddComponent<PlayerMovement>();
                Attacks = new GameObject("Summon Replica Emitters");
                Movement.AttacksParent = Attacks.transform;
                databaseObject = new GameObject("Summon Replica Database");
                var database = databaseObject.AddComponent<RuntimeDB>();
#if UNITY_EDITOR
                WeaponData weapon = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>(WeaponPath);
                Assert.That(weapon, Is.Not.Null, "Import the actual Ovid Summon resources before running this integration fixture.");
                weapons = ScriptableObject.CreateInstance<WeaponDB>();
                weapons.Configure(new[] { weapon });
                database.ConfigureWeaponDatabase(weapons);
#else
                throw new NotSupportedException("The actual prefab integration fixture requires the Unity Editor.");
#endif
                Replica = new SummonPresentationReplica(Movement, database);
            }

            public SummonAIBehaviour Apply(SummonPresentationState state, float age)
            {
                // The two source FMOD emitters use Play/Stop=None and Preload=false.
                // Do not add playback or suppress unrelated exceptions to make asset loading pass.
                Assert.That(Replica.TryApplyState(state, age), Is.True);
                return Attacks.GetComponentsInChildren<SummonAttackBehaviour>(true)
                    .Single(emitter => emitter.PetId == state.PetId).ActiveSummon;
            }

            public SummonAIBehaviour[] Pets() => Attacks.GetComponentsInChildren<SummonAttackBehaviour>(true)
                .Select(emitter => emitter.ActiveSummon).Where(pet => pet != null).ToArray();

            public void Dispose()
            {
                Replica.Dispose();
                UnityEngine.Object.DestroyImmediate(Attacks);
                UnityEngine.Object.DestroyImmediate(Player);
                UnityEngine.Object.DestroyImmediate(databaseObject);
                UnityEngine.Object.DestroyImmediate(weapons);
                UnityEngine.Object.DestroyImmediate(pool);
                PoolManager.Instance = null;
            }
        }
    }
}
