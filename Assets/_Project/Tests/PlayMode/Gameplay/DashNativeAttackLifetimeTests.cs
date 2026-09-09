using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GasAttackStats = MonsterSupergroup.GAS.AttackStats;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class DashNativeAttackLifetimeTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<TrailPresentationSpawn> spawns = new List<TrailPresentationSpawn>();
        private readonly List<TrailPresentationPoint> points = new List<TrailPresentationPoint>();
        private readonly List<TrailPresentationSamplingEnded> samplingEnds = new List<TrailPresentationSamplingEnded>();
        private readonly List<TrailPresentationTermination> ends = new List<TrailPresentationTermination>();
        private PlayerMovement owner;
        private PlayerBuildRuntime build;
        private DashAttackBehaviour prefab;
        private DashAttackBehaviour weapon;
        private DashSnapshotTestTrail trailPrefab;
        private WeaponData definition;

        [SetUp]
        public void SetUp()
        {
            Create("Dash Native Pool").AddComponent<PoolManager>().Init();
            var player = Create("Dash Native Owner", false);
            owner = player.AddComponent<PlayerMovement>();
            owner.AttacksParent = Create("Dash Native Weapons").transform;
            build = player.AddComponent<PlayerBuildRuntime>();
            build.Initialize(owner, new FixedRandom());
            build.SetWeaponExecutionEnabled(false);

            ParticleSystem particles = Create("Dash Native Segment", false).AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.3f;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 40f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 2) });
            trailPrefab = Create("Dash Native Trail", false).AddComponent<DashSnapshotTestTrail>();
            var collider = trailPrefab.gameObject.AddComponent<EdgeCollider2D>();
            collider.isTrigger = false; // Source prefab value; BaseAttackHitBox.Init sets it to true.
            var hitbox = trailPrefab.gameObject.AddComponent<PlayerAttackOvertimeHitBox>();
            hitbox.collider = collider;
            trailPrefab.hitbox = hitbox;
            SetTrailField(trailPrefab, "trailParticles", particles);
            SetTrailField(trailPrefab, "edgeCollider", collider);
            SetTrailField(trailPrefab, "trailDelta", 1f);

            prefab = Create("Dash Native Emitter", false).AddComponent<DashAttackBehaviour>();
            var variants = new BasePlayerAttackVariants();
            typeof(AttackVariantSet<BasePlayerAttack>).GetField("defaultPrefab", Private).SetValue(variants, trailPrefab);
            typeof(DashAttackBehaviour).GetField("variants", Private).SetValue(prefab, variants);
            definition = ScriptableObject.CreateInstance<WeaponData>();
            objects.Add(definition);
            definition.ID = 8;
            definition.WeaponPrefab = prefab;
            definition.modifierFlags = (ModifierFlags)int.MaxValue;
        }

        [TearDown]
        public void TearDown()
        {
            build?.ClearBuild();
            for (int index = objects.Count - 1; index >= 0; index--)
                if (objects[index] != null) UnityEngine.Object.DestroyImmediate(objects[index]);
            objects.Clear(); spawns.Clear(); points.Clear(); samplingEnds.Clear(); ends.Clear();
            PoolManager.Instance = null;
        }

        [Test]
        public void OnlyCommittedDashSignalsStartRootsAndEachUseStartsOncePerWeapon()
        {
            Equip();
            int roots = 0;
            weapon.NativeAttackStarted += (source, _) =>
            {
                roots++;
                Assert.That(((DashAttackBehaviour)source).CurrentDashUseId, Is.EqualTo(owner.CurrentDashUseId));
            };
            weapon.Attack();
            Assert.That(roots, Is.Zero);
            SignalDash(7);
            weapon.Attack();
            SignalDash(7);
            Assert.That(roots, Is.EqualTo(1));
            Assert.That(spawns.Single().DashUseId, Is.EqualTo(7));
            SignalDash(6);
            Assert.That(roots, Is.EqualTo(1), "An older use cannot create another attack.");
            SignalDash(8);
            Assert.That(roots, Is.EqualTo(2), "No ordinary weapon cooldown gates a new committed dash.");
            Assert.That(weapon.ActiveTrailCount, Is.EqualTo(2));
            Assert.That(ActiveTrails().Select(trail => trail.CurrentSnapshot.Context.EventId).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void OneDashUseTriggersAllEquippedDashWeaponsWithoutConsumingResourcesAgain()
        {
            Equip();
            DashAttackBehaviour second = (DashAttackBehaviour)build.EquipWeapon(definition);
            var secondSpawns = new List<TrailPresentationSpawn>();
            second.PresentationSpawned += secondSpawns.Add;
            SignalDash(11);
            Assert.That(spawns.Count, Is.EqualTo(1));
            Assert.That(secondSpawns.Count, Is.EqualTo(1));
            Assert.That(secondSpawns[0].DashUseId, Is.EqualTo(spawns[0].DashUseId));
            Assert.That(secondSpawns[0].AttackEventId, Is.Not.EqualTo(spawns[0].AttackEventId));
        }

        [Test]
        public void SourceProjectionSamplesAtMostOneWorldPointAndKeepsOriginalStepLength()
        {
            Equip();
            SignalDash(1);
            DashSnapshotTestTrail trail = ActiveTrails().Single();
            Assert.That(points, Is.Empty);
            owner.transform.position = new Vector3(10f, 4f, 0f);
            Tick(trail);
            Assert.That(points.Count, Is.EqualTo(1));
            Vector2 expected = new Vector2(10f, 2f).normalized;
            Assert.That(Vector2.Distance(points[0].WorldPosition, expected), Is.LessThan(0.0001f));
            Assert.That(points[0].PointIndex, Is.Zero);
            Assert.That(trail.transform.parent, Is.Null, "World trail must not join the player Rigidbody compound collider.");
            var edge = trail.GetComponent<EdgeCollider2D>();
            Assert.That(edge.isTrigger, Is.True, "This is the actual source Init behavior.");
            Assert.That(edge.points.Length, Is.EqualTo(2));
            Assert.That(Vector2.Distance(edge.transform.TransformPoint(edge.points[0]), expected), Is.LessThan(0.0001f));
            Assert.That(edge.points[0], Is.EqualTo(edge.points[1]));
        }

        [UnityTest]
        public IEnumerator RealPhysicsHitsUseFrozenNativeDamageAndOriginalSpeedInterval()
        {
            Equip(0.45f, 0.05f);
            SignalDash(1);
            DashSnapshotTestTrail trail = ActiveTrails().Single();
            AttackSnapshot snapshot = trail.CurrentSnapshot;
#if UNITY_EDITOR
            build.AddEquipment(weapon, UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset"), 0);
            build.AddEquipment(weapon, UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_SpeedRaiseEquipment.asset"), 0);
#endif
            Assert.That(weapon.DamageValue, Is.GreaterThan(9));
            Assert.That(snapshot.Stats.Damage, Is.EqualTo(9));
            Assert.That(((PlayerAttackOvertimeHitBox)trail.hitbox).HitInterval, Is.EqualTo(0.05f).Within(0.00001f));
            var targetObject = Create("Dash Actual Physics Target");
            targetObject.transform.position = Vector3.right * 8f;
            targetObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var collider = targetObject.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(32f, 8f);
            collider.isTrigger = true;
            MeleeTestTarget target = targetObject.AddComponent<MeleeTestTarget>();
            owner.transform.position = Vector3.right * 20f;
            Physics2D.SyncTransforms();
            float deadline = Time.realtimeSinceStartup + 1f;
            while (target.NativeHitCount < 2 && Time.realtimeSinceStartup < deadline) yield return new WaitForFixedUpdate();
            Assert.That(target.NativeHitCount, Is.GreaterThanOrEqualTo(2), "Real EdgeCollider2D/trigger callbacks must reach Native GAS.");
            Assert.That(target.Health, Is.EqualTo(1000 - target.NativeHitCount * 9));
            Assert.That(target.LegacyDamageCalls, Is.Zero);
            weapon.CancelDashUse(1);
            int previous = target.NativeHitCount;
            yield return new WaitForSeconds(0.12f);
            Assert.That(target.NativeHitCount, Is.EqualTo(previous));
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator SamplingDurationAndSegmentLifetimeAreFrozenAndNaturalParticleTailRetainsRoot()
        {
            Equip(0.12f, 1f);
            SignalDash(1);
            DashSnapshotTestTrail trail = ActiveTrails().Single();
            AttackSnapshot snapshot = trail.CurrentSnapshot;
            owner.transform.position = Vector3.right * 4f;
            Tick(trail);
#if UNITY_EDITOR
            build.AddEquipment(weapon, UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_DurationRaiseEquipment.asset"), 0);
#endif
            Assert.That(trail.PresentationSpawn.SamplingDuration, Is.EqualTo(0.12f));
            Assert.That(trail.PresentationSpawn.SegmentDuration, Is.EqualTo(0.06f));
            float deadline = Time.realtimeSinceStartup + 1f;
            while ((samplingEnds.Count == 0 || trail.ActiveSegmentCount > 0) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(samplingEnds.Count, Is.EqualTo(1));
            Assert.That(samplingEnds[0].SamplingElapsedSeconds, Is.GreaterThanOrEqualTo(0.12f));
            Assert.That(trail.ActiveSegmentCount, Is.Zero);
            Assert.That(trail.GetComponent<EdgeCollider2D>().enabled, Is.False);
            Assert.That(snapshot.IsDisposed, Is.False, "Stopped emitters still own their natural particle tail.");
            deadline = Time.realtimeSinceStartup + 2f;
            while (weapon.ActiveTrailCount != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(weapon.ActiveTrailCount, Is.Zero);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(ends.Count, Is.EqualTo(1));
        }

        [Test]
        public void ZeroDurationWithNoPointsClosesSamplingAndRootImmediately()
        {
            Equip(0f);
            int completed = 0;
            build.NativeAttackCompleted += (_, __, ___) => completed++;
            SignalDash(1);
            Assert.That(spawns.Count, Is.EqualTo(1));
            Assert.That(samplingEnds.Count, Is.EqualTo(1));
            Assert.That(ends.Count, Is.EqualTo(1));
            Assert.That(weapon.ActiveTrailCount, Is.Zero);
            PollCompletions();
            Assert.That(completed, Is.EqualTo(1));
        }

        [Test]
        public void RejectionDuringNativeStartStopsBeforePresentationAndCompletesRoot()
        {
            Equip();
            int completed = 0;
            build.NativeAttackCompleted += (_, __, ___) => completed++;
            weapon.NativeAttackStarted += (_, __) => weapon.CancelDashUse(weapon.CurrentDashUseId);
            SignalDash(1);
            Assert.That(spawns, Is.Empty);
            Assert.That(weapon.ActiveTrailCount, Is.Zero);
            PollCompletions();
            Assert.That(completed, Is.EqualTo(1));
        }

        [Test]
        public void CancellationInsideSpawnDoesNotActivateOrLeakTheCheckedOutTrail()
        {
            Equip();
            AttackSnapshot snapshot = null;
            weapon.PresentationSpawned += _ =>
            {
                snapshot = ActiveTrails().Single().CurrentSnapshot;
                weapon.CancelDashUse(weapon.CurrentDashUseId);
            };
            SignalDash(1);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(weapon.ActiveTrailCount, Is.Zero);
            Assert.That(ends.Count, Is.EqualTo(1));
            Assert.That(points, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ExternalTrailDisableAndDisabledWeaponParentCancelSynchronously()
        {
            Equip();
            SignalDash(1);
            DashSnapshotTestTrail old = ActiveTrails().Single();
            AttackSnapshot snapshot = old.CurrentSnapshot;
            old.gameObject.SetActive(false);
            Assert.That(weapon.ActiveTrailCount, Is.Zero);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(ends.Count, Is.EqualTo(1));
            yield return null;
            SignalDash(2);
            Assert.That(ActiveTrails().Single(), Is.Not.SameAs(old));
            snapshot = ActiveTrails().Single().CurrentSnapshot;
            Assert.That(weapon.enabled, Is.False);
            weapon.gameObject.SetActive(false);
            Assert.That(weapon.ActiveTrailCount, Is.Zero, "Lifetime anchor covers component-disabled emitter parents.");
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(ends.Count, Is.EqualTo(2));
        }

        [Test]
        public void CancellingOldUsePreservesNewRootAndRepeatedCancelDoesNotDoubleReturn()
        {
            Equip();
            SignalDash(1);
            AttackSnapshot old = ActiveTrails().Single().CurrentSnapshot;
            SignalDash(2);
            AttackSnapshot current = ActiveTrails().Single(trail => trail.CurrentSnapshot != old).CurrentSnapshot;
            weapon.CancelDashUse(1);
            weapon.CancelDashUse(1);
            Assert.That(old.IsDisposed, Is.True);
            Assert.That(current.IsDisposed, Is.False);
            Assert.That(weapon.ActiveTrailCount, Is.EqualTo(1));
            Assert.That(ends.Count, Is.EqualTo(1));
        }

        [Test]
        public void PoolReuseClearsOldPointsCallbacksAndSnapshots()
        {
            Equip();
            SignalDash(1);
            DashSnapshotTestTrail original = ActiveTrails().Single();
            AttackSnapshot old = original.CurrentSnapshot;
            owner.transform.position = Vector3.right * 10f;
            Tick(original);
            Assert.That(original.ParticleInstanceCount, Is.EqualTo(1));
            weapon.CancelDashUse(1);
            SignalDash(2);
            DashSnapshotTestTrail reused = ActiveTrails().Single();
            Assert.That(reused, Is.SameAs(original));
            Assert.That(old.IsDisposed, Is.True);
            Assert.That(reused.CurrentSnapshot, Is.Not.SameAs(old));
            Assert.That(reused.ParticleInstanceCount, Is.Zero);
            owner.transform.position += Vector3.right * 10f;
            Tick(reused);
            Assert.That(points.Last().PointIndex, Is.Zero);
            Assert.That(points.Last().AttackEventId, Is.EqualTo(spawns.Last().AttackEventId));
        }

        [Test]
        public void UnequipRemovesDashSubscriptionAndPreventsSameFrameOldWeaponAttack()
        {
            Equip();
            SignalDash(1);
            AttackSnapshot snapshot = ActiveTrails().Single().CurrentSnapshot;
            int subscribers = DashSubscribers();
            Assert.That(build.UnequipWeapon(weapon), Is.True);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(DashSubscribers(), Is.EqualTo(subscribers - 1));
            SignalDash(2);
            weapon.Attack();
            Assert.That(spawns.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ReplicaWaitsForReliablePointsAndNeverSamplesOrCreatesGameplay()
        {
            Equip();
            DashAttackBehaviour replica = Replica();
            TrailPresentationSpawn spawn = Spawn(100);
            var trail = (DashSnapshotTestTrail)replica.PlayPresentation(spawn, 1.2f);
            Assert.That(trail.CurrentSnapshot, Is.Null);
            Assert.That(replica.NativeRuntime, Is.Null);
            Assert.That(trail.hitbox.enabled, Is.False);
            Assert.That(trail.GetComponent<EdgeCollider2D>().enabled, Is.False);
            owner.transform.position = new Vector3(50f, 50f);
            yield return null;
            Assert.That(replica.ActiveTrailCount, Is.EqualTo(1), "Spawn age cannot discard later reliable points.");
            Assert.That(trail.ParticleInstanceCount, Is.Zero);
            var point = new TrailPresentationPoint(8, 100, 0, new Vector2(3f, 7f), 0.1f);
            Assert.That(replica.ApplyPresentationPoint(point, 0f), Is.True);
            ParticleSystem particle = SegmentParticles(trail).Single();
            owner.transform.position = new Vector3(100f, 100f);
            yield return null;
            Assert.That(particle.transform.position, Is.EqualTo(new Vector3(3f, 7f, 0f)));
            Assert.That(trail.ParticleInstanceCount, Is.EqualTo(1));
            Assert.That(replica.ApplyPresentationSamplingEnded(new TrailPresentationSamplingEnded(8, 100, 1.25f), 0f), Is.True);
            Assert.That(replica.ApplyPresentationPoint(new TrailPresentationPoint(8, 100, 1, Vector2.zero, 0.2f), 0f), Is.False);
            replica.DisposePresentationReplica();
            Assert.That(replica.ActiveTrailCount, Is.Zero);
        }

        [Test]
        public void ReplicaRejectsDuplicateOutOfOrderAndMalformedPointsWithoutMutation()
        {
            Equip();
            DashAttackBehaviour replica = Replica();
            Assert.That(replica.PlayPresentation(Spawn(100), float.NaN), Is.Null);
            var trail = (DashSnapshotTestTrail)replica.PlayPresentation(Spawn(100), 0f);
            Assert.That(replica.ApplyPresentationPoint(new TrailPresentationPoint(8, 100, 1, Vector2.one, 0.1f), 0f), Is.False);
            Assert.That(replica.ApplyPresentationPoint(new TrailPresentationPoint(8, 100, 0, new Vector2(float.NaN, 0), 0.1f), 0f), Is.False);
            Assert.That(replica.ApplyPresentationPoint(new TrailPresentationPoint(8, 100, 0, Vector2.one, 0.1f), -1f), Is.False);
            Assert.That(replica.ApplyPresentationPoint(new TrailPresentationPoint(8, 100, 0, Vector2.one, 1.25f), 0f), Is.False);
            var valid = new TrailPresentationPoint(8, 100, 0, Vector2.one, 0.1f);
            Assert.That(replica.ApplyPresentationPoint(valid, 0f), Is.True);
            Assert.That(replica.ApplyPresentationPoint(valid, 0f), Is.False);
            Assert.That(replica.ApplyPresentationPoint(new TrailPresentationPoint(8, 100, 1, Vector2.one, 0.05f), 0f), Is.False);
            Assert.That(trail.ParticleInstanceCount, Is.EqualTo(1));
            Assert.That(replica.ApplyPresentationSamplingEnded(new TrailPresentationSamplingEnded(8, 100, 1f), 0f), Is.False);
            Assert.That(replica.TerminatePresentation(100), Is.True);
            Assert.That(replica.TerminatePresentation(100), Is.False);
            Assert.That(replica.ApplyPresentationPoint(valid, 0f), Is.False);
        }

        [Test]
        public void ReplicaParentDisableImmediatelyCancelsDetachedTrails()
        {
            Equip();
            DashAttackBehaviour replica = Replica();
            int returned = 0;
            replica.PlayPresentation(Spawn(100), 0f, _ => returned++);
            replica.gameObject.SetActive(false);
            Assert.That(replica.ActiveTrailCount, Is.Zero);
            Assert.That(returned, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AgedReplicaResumesExistingTailWithoutRestartingEmission()
        {
            Equip();
            DashAttackBehaviour replica = Replica();
            var trail = (DashSnapshotTestTrail)replica.PlayPresentation(Spawn(300), 1.25f);
            Assert.That(replica.ApplyPresentationPoint(new TrailPresentationPoint(8, 300, 0, Vector2.one, 0.1f), 0.7f), Is.True);
            ParticleSystem particles = SegmentParticles(trail).Single();
            Assert.That(particles.particleCount, Is.GreaterThan(0), "The final emitted particles are still within their source lifetime.");
            Assert.That(particles.isPaused, Is.False, "A stopped tail must continue aging after the one-time seek.");
            Assert.That(particles.isEmitting, Is.False);
            Assert.That(replica.ApplyPresentationSamplingEnded(new TrailPresentationSamplingEnded(8, 300, 1.25f), 0f), Is.True);
            float deadline = Time.realtimeSinceStartup + 2f;
            while (replica.ActiveTrailCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(replica.ActiveTrailCount, Is.Zero);
            Assert.That(particles.IsAlive(true), Is.False);
        }

        private void Equip(float duration = 1.25f, float speed = 1f)
        {
            definition.ConfigureNativeGas(new GasAttackStats
            {
                damage = 9, speed = speed, size = 1f, duration = duration,
                projectileCount = 1, critMultiplier = 1, critRate = 0
            }, CombatTags.Attack, new WeaponPresentationSettings());
            weapon = (DashAttackBehaviour)build.EquipWeapon(definition);
            weapon.PresentationSpawned += spawns.Add;
            weapon.PresentationPointAdded += points.Add;
            weapon.PresentationSamplingEnded += samplingEnds.Add;
            weapon.PresentationTerminated += ends.Add;
        }

        // Unit boundary of the already committed NetworkPlayerDash callback. No resource or hit
        // is manufactured here; the integration test above uses actual EdgeCollider2D physics.
        private void SignalDash(ulong useId)
        {
            typeof(PlayerMovement).GetProperty(nameof(PlayerMovement.CurrentDashUseId)).SetValue(owner, useId);
            ((Action)typeof(PlayerMovement).GetField("OnDashStart", Private).GetValue(owner))?.Invoke();
        }

        private int DashSubscribers() =>
            ((Action)typeof(PlayerMovement).GetField("OnDashStart", Private).GetValue(owner))?.GetInvocationList().Length ?? 0;
        private DashSnapshotTestTrail[] ActiveTrails() => Resources.FindObjectsOfTypeAll<DashSnapshotTestTrail>()
            .Where(trail => trail.CurrentSnapshot != null && trail.CurrentWeapon == weapon).ToArray();
        private DashAttackBehaviour Replica()
        {
            DashAttackBehaviour replica = UnityEngine.Object.Instantiate(prefab);
            objects.Add(replica.gameObject);
            replica.InitializePresentationReplica(8, owner);
            replica.gameObject.SetActive(true);
            return replica;
        }
        private static TrailPresentationSpawn Spawn(ulong root) => new TrailPresentationSpawn(8, root, 1, AttackElement.Default,
            Vector2.zero, 1.25f, 0.625f, 1f, new ProjectilePresentationStats { Duration = 1.25f, ProjectileCount = 1, BaseProjectileCount = 1 });
        private static ParticleSystem[] SegmentParticles(MultiParticlePlayerTrailAttack trail)
        {
            var entries = (IEnumerable)typeof(MultiParticlePlayerTrailAttack).GetField("_particles", Private).GetValue(trail);
            var particles = new List<ParticleSystem>();
            foreach (object entry in entries) particles.Add((ParticleSystem)entry.GetType().GetField("Particle").GetValue(entry));
            return particles.ToArray();
        }
        private void PollCompletions() => typeof(PlayerBuildRuntime).GetMethod("LateUpdate", Private).Invoke(build, null);
        private static void Tick(MultiParticlePlayerTrailAttack trail) =>
            typeof(MultiParticlePlayerTrailAttack).GetMethod("Tick", Private).Invoke(trail, null);
        private static void SetTrailField(MultiParticlePlayerTrailAttack target, string name, object value) =>
            typeof(MultiParticlePlayerTrailAttack).GetField(name, Private).SetValue(target, value);
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject Create(string name, bool active = true)
        {
            var go = new GameObject(name); go.SetActive(active); objects.Add(go); return go;
        }
        private sealed class FixedRandom : IRandomSource { public float Next01() => 0.99f; }
    }

    public sealed class DashSnapshotTestTrail : MultiParticlePlayerTrailAttack
    {
        public AttackSnapshot CurrentSnapshot => NativeAttackSnapshot;
        public WeaponBehaviour CurrentWeapon => _behaviour;
    }
}

