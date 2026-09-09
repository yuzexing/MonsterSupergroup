using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Animancer;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GasAttackStats = MonsterSupergroup.GAS.AttackStats;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class BeamNativeAttackLifetimeTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<BeamPresentationSpawn> spawns = new List<BeamPresentationSpawn>();
        private readonly List<BeamPresentationTermination> ends = new List<BeamPresentationTermination>();
        private PlayerBuildRuntime build;
        private PlayerMovement owner;
        private PlayerBeamAttackBehaviour weapon;
        private PlayerBeamAttackBehaviour weaponPrefab;
        private BeamSnapshotTestAttack beamPrefab;
        private WeaponData definition;

        [SetUp]
        public void SetUp()
        {
            Create("Beam Pool").AddComponent<PoolManager>().Init();
            GameObject player = Create("Beam Owner", false);
            owner = player.AddComponent<PlayerMovement>();
            owner.AttacksParent = Create("Live Beam Weapons").transform;
            SetAim(Vector2.up);
            build = player.AddComponent<PlayerBuildRuntime>();
            build.Initialize(owner, new FixedRandom());
            build.SetWeaponExecutionEnabled(false);
            beamPrefab = Create("Test Beam", false).AddComponent<BeamSnapshotTestAttack>();
            var collider = beamPrefab.gameObject.AddComponent<BoxCollider2D>();
            var hitbox = beamPrefab.gameObject.AddComponent<PlayerAttackOvertimeHitBox>();
            hitbox.collider = collider;
            hitbox.SetHitInterval(0.04f);
            beamPrefab.hitbox = hitbox;
            beamPrefab.attackStartAnim = Clip(0.1f);
            beamPrefab.attackAnim = Clip(0.2f);
            beamPrefab.attackEndAnim = Clip(0.1f);
            beamPrefab.attackStartAnimTransitionAfterFinish = true;
            beamPrefab.attackAnimTransitionAfterFinish = false;
            weaponPrefab = Create("Test Beam Emitter", false).AddComponent<PlayerBeamAttackBehaviour>();
            var variants = new AnimatedAttackVariants();
            SetField(typeof(AttackVariantSet<AnimatedAttack>), variants, "defaultPrefab", beamPrefab);
            SetField(typeof(PlayerBeamAttackBehaviour), weaponPrefab, "variants", variants);
            definition = ScriptableObject.CreateInstance<WeaponData>();
            objects.Add(definition);
            definition.ID = 3;
            definition.WeaponPrefab = weaponPrefab;
            definition.modifierFlags = (ModifierFlags)int.MaxValue;
        }

        [TearDown]
        public void TearDown()
        {
            build?.ClearBuild();
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear(); spawns.Clear(); ends.Clear();
            PoolManager.Instance = null;
        }

        [Test]
        public void MultipleBeamsShareSnapshotAndKeepOrdinalAfterOneEnds()
        {
            Equip(3, true);
            weapon.Attack();
            var beams = ActiveBeams();
            Assert.That(beams.Length, Is.EqualTo(3));
            AttackSnapshot snapshot = beams[0].LastSnapshot;
            Assert.That(beams.All(value => ReferenceEquals(value.LastSnapshot, snapshot)), Is.True);
            Assert.That(spawns.Select(value => value.Key.BeamIndex), Is.EqualTo(new ushort[] { 0, 1, 2 }));
            Assert.That(spawns.Select(value => value.Key.AttackEventId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(Vector2.Distance(beams[0].transform.localPosition, Vector2.up * weapon.spawnRadius), Is.LessThan(0.001f));
            Vector3 thirdPosition = beams[2].transform.localPosition;
            beams[1].Complete();
            Invoke(weapon, "UpdateDirection", 0.1f);
            Assert.That(Vector3.Distance(beams[2].transform.localPosition, thirdPosition), Is.LessThan(0.001f),
                "A remaining beam must not be redistributed using the live list count.");
            Assert.That(snapshot.IsDisposed, Is.False);
            beams[0].Complete(); beams[2].Complete();
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(ends.Count, Is.EqualTo(3));
        }

        [Test]
        public void AuthoredSingleBeamRuleIgnoresAdditionalProjectileCount()
        {
            Equip(3, false);
            weapon.Attack();
            Assert.That(weapon.ActiveBeamCount, Is.EqualTo(1));
            Assert.That(spawns.Single().BeamCount, Is.EqualTo(1));
            Assert.That(spawns.Single().Stats.ProjectileCount, Is.EqualTo(3));
            weapon.Attack();
            Assert.That(spawns.Count, Is.EqualTo(1), "A beam sequence cannot overlap a second root.");
        }

        [Test]
        public void DirectionStartsAtOwnerAimAndCrossesAngleWrapWithoutAFullTurn()
        {
            Equip(1, false);
            SetAim(Direction(179f));
            weapon.Attack();
            var beam = ActiveBeams().Single();
            SetAim(Direction(-179f));
            Invoke(weapon, "UpdateDirection", 0.1f);
            Assert.That(Vector2.Angle(beam.transform.localPosition, Vector2.left), Is.LessThan(2f));
        }

        [Test]
        public void CooldownFreezesUntilLastBeamEndsAndReconnectRetainsSequenceRemainder()
        {
            Equip(2, true);
            Assert.That(weapon.GetAttackSequenceDuration(), Is.EqualTo(2.2f).Within(0.001f));
            weapon.Attack();
            var beams = ActiveBeams();
            float elapsed = weapon.LastAttackElapsedTime;
            Invoke(weapon, "Update");
            Assert.That(weapon.LastAttackElapsedTime, Is.EqualTo(elapsed));
            beams[0].Complete();
            Invoke(weapon, "Update");
            Assert.That(weapon.LastAttackElapsedTime, Is.EqualTo(elapsed));
            beams[1].Complete();
            Assert.That(weapon.LastAttackElapsedTime, Is.Zero);
            weapon.RestoreCooldownRemaining(2f);
            Assert.That(weapon.LastAttackElapsedTime, Is.EqualTo(weapon.GetCooldown() - 2f).Within(0.0001f));
            Assert.That((bool)Invoke(weapon, "CheckCooldown"), Is.False);
        }

        [UnityTest]
        public IEnumerator SameTargetReceivesPeriodicNativeHitsUsingFrozenBuild()
        {
            Equip(1, false);
            weapon.Attack();
            var beam = ActiveBeams().Single();
            AttackSnapshot snapshot = beam.LastSnapshot;
#if UNITY_EDITOR
            foreach (string name in new[] { "Damage", "Duration", "Speed" })
                build.AddEquipment(weapon, UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>(
                    $"Assets/MonoBehaviour/StatRaise_{name}RaiseEquipment.asset"), 0);
#else
            throw new NotSupportedException("The integration cards are loaded through the Editor.");
#endif
            Assert.That(weapon.DamageValue, Is.GreaterThan(5));
            Assert.That(weapon.DurationValue, Is.GreaterThan(2f));
            Assert.That(snapshot.Stats.Duration, Is.EqualTo(2f));
            Assert.That(beam.RequestedDuration, Is.EqualTo(2f));
            var target = Target("Repeated Beam Target");
            Trigger(beam.hitbox, "OnTriggerEnter2D", target.GetComponent<Collider2D>());
            Trigger(beam.hitbox, "OnTriggerEnter2D", target.GetComponent<Collider2D>());
            Assert.That(target.NativeHitCount, Is.EqualTo(1));
            yield return new WaitForSeconds(0.15f);
            Assert.That(target.NativeHitCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(target.Health, Is.EqualTo(1000 - target.NativeHitCount * 5));
            Assert.That(target.LegacyDamageCalls, Is.Zero);
            beam.Complete();
            int hits = target.NativeHitCount;
            yield return new WaitForSeconds(0.08f);
            Assert.That(target.NativeHitCount, Is.EqualTo(hits));
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator PoolCheckoutBindsBeforeEnableAndCleanupReleasesAllLeasesOnce()
        {
            beamPrefab.gameObject.SetActive(true);
            Equip(2, true);
            weapon.Attack();
            var beams = ActiveBeams();
            AttackSnapshot snapshot = beams[0].LastSnapshot;
            Assert.That(beams.All(value => value.EarlyEnableCount == 0), Is.True);
            build.SetWeaponExecutionEnabled(false);
            Assert.That(snapshot.IsDisposed, Is.False);
            weapon.gameObject.SetActive(false);
            weapon.Deactivate();
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(ends.Count, Is.EqualTo(2));
            // Destroy is deferred until the end of the frame. Re-enable only after cancelled children are gone.
            yield return null;
            weapon.gameObject.SetActive(true);
            weapon.Attack();
            var fresh = ActiveBeams();
            Assert.That(fresh.All(value => !beams.Contains(value)), Is.True,
                "Externally deactivated beams are discarded rather than reparented inside OnDisable.");
            Assert.That(fresh.All(value => value.EarlyEnableCount == 0), Is.True);
            Assert.That(fresh.All(value => value.LastSnapshot != snapshot), Is.True);

            foreach (var beam in fresh) beam.Complete();
            weapon.Attack();
            var reused = ActiveBeams();
            Assert.That(reused.All(value => fresh.Contains(value)), Is.True,
                "Normal animation completion still returns reusable instances to the pool.");
            Assert.That(reused.All(value => value.EarlyEnableCount == 0), Is.True);
        }

        [Test]
        public void PresentationKeepsIndependentRootAimAndCannotDamage()
        {
            Equip(1, false);
            var replica = UnityEngine.Object.Instantiate(weaponPrefab);
            objects.Add(replica.gameObject);
            replica.InitializePresentationReplica(3, owner);
            replica.gameObject.SetActive(true);
            var firstSpawn = Spawn(100, 0, 1, Vector2.right);
            var secondSpawn = Spawn(200, 0, 1, Vector2.up);
            AnimatedAttack first = replica.PlayPresentation(firstSpawn, 0);
            AnimatedAttack second = replica.PlayPresentation(secondSpawn, 0);
            Assert.That(first, Is.Not.Null); Assert.That(second, Is.Not.Null);
            Assert.That(replica.NativeRuntime, Is.Null);
            Assert.That(replica.enabled, Is.False);
            Vector3 secondPosition = second.transform.localPosition;
            Assert.That(replica.ApplyPresentationAim(new BeamPresentationAim(3, 100, Vector2.left)), Is.True);
            replica.TickPresentation(1f);
            Assert.That(Vector2.Angle(first.transform.localPosition, Vector2.left), Is.LessThan(0.1f));
            Assert.That(second.transform.localPosition, Is.EqualTo(secondPosition));
            var target = Target("Remote Beam Target");
            Trigger(first.hitbox, "OnTriggerEnter2D", target.GetComponent<Collider2D>());
            Assert.That(target.NativeHitCount, Is.Zero);
            Assert.That(RemovalCount((PlayerAttackOvertimeHitBox)first.hitbox), Is.Zero);
            Assert.That(replica.TerminatePresentation(firstSpawn.Key), Is.True);
            Assert.That(replica.ApplyPresentationAim(new BeamPresentationAim(3, 100, Vector2.up)), Is.False);
            Assert.That(replica.ActiveBeamCount, Is.EqualTo(1));
            replica.DisposePresentationReplica();
            Assert.That(replica.ActiveBeamCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ReturningHitboxInsidePeriodicHitStopsIterationAndAllowsReuse()
        {
            var box = Create("Reentrant Hitbox", false).AddComponent<PlayerAttackOvertimeHitBox>();
            box.collider = box.gameObject.AddComponent<BoxCollider2D>();
            box.SetHitInterval(0.01f);
            int hits = 0;
            box.Init(_ => { if (++hits == 3) box.gameObject.SetActive(false); });
            box.gameObject.SetActive(true);
            var first = Target("First Reentrant Target");
            var second = Target("Second Reentrant Target");
            Trigger(box, "OnTriggerEnter2D", first.GetComponent<Collider2D>());
            Trigger(box, "OnTriggerEnter2D", second.GetComponent<Collider2D>());
            yield return new WaitForSeconds(0.08f);
            Assert.That(hits, Is.EqualTo(3));
            box.Init(_ => hits++);
            box.gameObject.SetActive(true);
            Trigger(box, "OnTriggerEnter2D", first.GetComponent<Collider2D>());
            Assert.That(hits, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator CancelledExitCannotEraseReplacementRemovalForSameTarget()
        {
            var box = Create("Removal Token Hitbox", false).AddComponent<PlayerAttackOvertimeHitBox>();
            box.collider = box.gameObject.AddComponent<BoxCollider2D>();
            box.SetHitInterval(10f);
            SetField(typeof(PlayerAttackOvertimeHitBox), box, "timeoutAfterExit", 0.08f);
            int hits = 0;
            box.Init(_ => hits++);
            box.gameObject.SetActive(true);
            var target = Target("Removal Target").GetComponent<Collider2D>();
            Trigger(box, "OnTriggerEnter2D", target);
            Trigger(box, "OnTriggerExit2D", target);
            Trigger(box, "OnTriggerEnter2D", target);
            Trigger(box, "OnTriggerExit2D", target);
            yield return null;
            Assert.That(RemovalCount(box), Is.EqualTo(1));
            yield return new WaitForSeconds(0.1f);
            Assert.That(RemovalCount(box), Is.Zero);
            Trigger(box, "OnTriggerEnter2D", target);
            Assert.That(hits, Is.EqualTo(2));
        }

        [Test]
        public void SourceSpeedScalerAdjustsHitIntervalAndResetsForPoolReuse()
        {
            var scaler = beamPrefab.gameObject.AddComponent<PlayerAttackOvertimeHitBoxProgressionScaler>();
            SetField(typeof(PlayerAttackOvertimeHitBoxProgressionScaler), scaler, "hitbox", beamPrefab.hitbox);
            SetField(typeof(PlayerAttackOvertimeHitBoxProgressionScaler), scaler, "defaultHitInterval", 0.5f);
            SetField(typeof(PlayerAttackOvertimeHitBoxProgressionScaler), scaler, "scallingFactor", 1f);
            scaler.Apply(1f);
            Assert.That(((PlayerAttackOvertimeHitBox)beamPrefab.hitbox).HitInterval, Is.EqualTo(0.25f));
            scaler.Apply(0f);
            Assert.That(((PlayerAttackOvertimeHitBox)beamPrefab.hitbox).HitInterval, Is.EqualTo(0.5f));
        }

        [Test]
        public void CancellingInsideSpawnNotificationPreventsEveryRemainingBeam()
        {
            Equip(3, true);
            AttackSnapshot snapshot = null;
            weapon.PresentationSpawned += _ =>
            {
                var attack = weapon.GetComponentsInChildren<BeamSnapshotTestAttack>(true)
                    .Single(value => value.CurrentSnapshot != null);
                snapshot = attack.CurrentSnapshot;
                weapon.gameObject.SetActive(false);
                weapon.Attack();
            };
            weapon.Attack();
            Assert.That(spawns.Count, Is.EqualTo(1));
            Assert.That(ends.Count, Is.EqualTo(1));
            Assert.That(weapon.ActiveBeamCount, Is.Zero);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [Test]
        public void ExternalBeamDisableIsPrunedAndCooldownCanResume()
        {
            Equip(1, false);
            weapon.Attack();
            var beam = ActiveBeams().Single();
            AttackSnapshot snapshot = beam.LastSnapshot;
            beam.gameObject.SetActive(false);
            Invoke(weapon, "Update");
            Assert.That(weapon.ActiveBeamCount, Is.Zero);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(ends.Count, Is.EqualTo(1));
            Assert.That(weapon.LastAttackElapsedTime, Is.LessThan(weapon.GetCooldown()));
            weapon.Attack();
            Assert.That(weapon.ActiveBeamCount, Is.EqualTo(1));
        }

        [Test]
        public void ExternalDisablePublishesEndBeforeRootCompletionWhileWeaponExecutionIsDisabled()
        {
            Equip(1, false);
            var order = new List<string>();
            weapon.PresentationTerminated += _ => order.Add("end");
            build.NativeAttackCompleted += (_, __, ___) => order.Add("complete");
            weapon.Attack();
            var beam = ActiveBeams().Single();
            AttackSnapshot snapshot = beam.LastSnapshot;
            weapon.enabled = false;
            beam.gameObject.SetActive(false);

            Assert.That(weapon.ActiveBeamCount, Is.Zero, "Tracking closes synchronously; disabled execution has no next Update.");
            Assert.That(order, Is.EqualTo(new[] { "end" }));
            typeof(PlayerBuildRuntime).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(build, null);
            Assert.That(order, Is.EqualTo(new[] { "end", "complete" }));
            Assert.That(snapshot.IsDisposed, Is.True);
            // The externally deactivated object is scheduled for destruction, never placed in the shared pool.
            weapon.Attack();
            Assert.That(ActiveBeams().Single(), Is.Not.SameAs(beam));
        }

        [Test]
        public void ExternalRemoteDisableRemovesReplicaTrackingImmediatelyWithoutTick()
        {
            Equip(1, false);
            var database = Create("Beam Immediate Cleanup Database").AddComponent<RuntimeDB>();
            var weaponDatabase = ScriptableObject.CreateInstance<WeaponDB>();
            objects.Add(weaponDatabase);
            weaponDatabase.Configure(new[] { definition });
            database.ConfigureWeaponDatabase(weaponDatabase);
            using (var replica = new BeamPresentationReplica(owner, database))
            {
                var spawn = Spawn(701, 0, 1, Vector2.right);
                Assert.That(replica.TrySpawn(spawn, 0), Is.True);
                var beam = owner.AttacksParent.GetComponentsInChildren<BeamSnapshotTestAttack>().Single();
                beam.gameObject.SetActive(false);
                Assert.That(replica.ActiveBeamCount, Is.Zero);
                Assert.That(replica.TryAim(new BeamPresentationAim(3, spawn.Key.AttackEventId, Vector2.up)), Is.False);
                Assert.That(replica.TryTerminate(new BeamPresentationTermination(3, spawn.Key)), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator ReinitializingInsidePeriodicHitKeepsTheNewCallbackRunning()
        {
            var box = Create("Reinitialized Hitbox", false).AddComponent<PlayerAttackOvertimeHitBox>();
            box.collider = box.gameObject.AddComponent<BoxCollider2D>();
            box.SetHitInterval(0.01f);
            var first = Target("Reinit First Target").GetComponent<Collider2D>();
            var second = Target("Reinit Second Target").GetComponent<Collider2D>();
            int oldHits = 0;
            int newHits = 0;
            box.Init(_ =>
            {
                if (++oldHits != 3) return;
                box.Init(__ => newHits++);
                Trigger(box, "OnTriggerEnter2D", first);
            });
            box.gameObject.SetActive(true);
            Trigger(box, "OnTriggerEnter2D", first);
            Trigger(box, "OnTriggerEnter2D", second);
            yield return new WaitForSeconds(0.13f);
            Assert.That(oldHits, Is.EqualTo(3));
            Assert.That(newHits, Is.GreaterThanOrEqualTo(2), "Init must not permanently stop periodic hits on the active object.");
        }

        private void Equip(int count, bool multiple)
        {
            SetField(typeof(PlayerBeamAttackBehaviour), weaponPrefab, "allowMultipleAttacks", multiple);
            definition.ConfigureNativeGas(new GasAttackStats
            {
                damage = 5, speed = 3, size = 1, duration = 2,
                projectileCount = count, critMultiplier = 1, critRate = 0
            }, CombatTags.Attack, new WeaponPresentationSettings());
            weapon = (PlayerBeamAttackBehaviour)build.EquipWeapon(definition);
            weapon.PresentationSpawned += spawns.Add;
            weapon.PresentationTerminated += ends.Add;
        }

        private ClipTransition Clip(float duration)
        {
            var clip = new AnimationClip(); objects.Add(clip);
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, duration, 1));
            return new ClipTransition { Clip = clip };
        }

        private BeamPresentationSpawn Spawn(ulong root, ushort index, int count, Vector2 direction) =>
            new BeamPresentationSpawn(3, new BeamPresentationKey(root, index), count, direction,
                AttackElement.Default, 2f, new ProjectilePresentationStats { Duration = 2f, ProjectileCount = count, BaseProjectileCount = 1 });
        private BeamSnapshotTestAttack[] ActiveBeams() => weapon.GetComponentsInChildren<BeamSnapshotTestAttack>();
        private MeleeTestTarget Target(string name)
        {
            GameObject target = Create(name);
            target.AddComponent<BoxCollider2D>();
            return target.AddComponent<MeleeTestTarget>();
        }
        private GameObject Create(string name, bool active = true)
        {
            var go = new GameObject(name); go.SetActive(active); objects.Add(go); return go;
        }
        private static Vector2 Direction(float angle) => new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        private void SetAim(Vector2 direction) => owner.SetRuntimeAimDirection(direction);
        private static void SetField(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static object Invoke(PlayerBeamAttackBehaviour target, string method, params object[] args)
        {
            MethodInfo info = typeof(PlayerBeamAttackBehaviour).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? typeof(WeaponBehaviour).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            return info.Invoke(target, args);
        }
        private static void Trigger(BaseAttackHitBox box, string method, Collider2D other) =>
            typeof(PlayerAttackOvertimeHitBox).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(box, new object[] { other });
        private static int RemovalCount(PlayerAttackOvertimeHitBox box) =>
            ((IDictionary)typeof(BaseAttackHitBox).GetField("_removalCTS", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(box)).Count;
        private sealed class FixedRandom : IRandomSource { public float Next01() => 0.99f; }
    }

    public sealed class BeamSnapshotTestAttack : AnimatedAttack
    {
        public AttackSnapshot CurrentSnapshot => NativeAttackSnapshot;
        public AttackSnapshot LastSnapshot { get; private set; }
        public float RequestedDuration { get; private set; }
        public int EarlyEnableCount { get; private set; }
        private void OnEnable() { if (NativeAttackSnapshot == null && !IsPresentationOnly) EarlyEnableCount++; }
        public override void Attack() { LastSnapshot = NativeAttackSnapshot; RequestedDuration = _attackAnimDuration; }
        public void Complete() => EndCallback();
    }
}
