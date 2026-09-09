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
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GasAttackStats = MonsterSupergroup.GAS.AttackStats;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class CirclingNativeAttackLifetimeTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<OrbitPresentationSpawn> spawns = new List<OrbitPresentationSpawn>();
        private readonly List<OrbitPresentationHiding> hiding = new List<OrbitPresentationHiding>();
        private readonly List<OrbitPresentationTermination> ends = new List<OrbitPresentationTermination>();
        private PlayerMovement owner;
        private PlayerBuildRuntime build;
        private CirclingAttackBehaviour weaponPrefab;
        private CirclingAttackBehaviour weapon;
        private CirclingSnapshotTestAttack orbPrefab;
        private WeaponData definition;

        [SetUp]
        public void SetUp()
        {
            Create("Circling Test Pool").AddComponent<PoolManager>().Init();
            GameObject player = Create("Circling Test Owner", false);
            owner = player.AddComponent<PlayerMovement>();
            owner.AttacksParent = Create("Circling Test Weapons").transform;
            owner.SetRuntimeAimDirection(Vector2.up);
            build = player.AddComponent<PlayerBuildRuntime>();
            build.Initialize(owner, new FixedRandom());
            build.SetWeaponExecutionEnabled(false);

            orbPrefab = Create("Circling Test Orb", false).AddComponent<CirclingSnapshotTestAttack>();
            orbPrefab.transform.localRotation = Quaternion.Euler(12f, 23f, 34f);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(orbPrefab.transform, false);
            var animator = orbPrefab.gameObject.AddComponent<Animator>();
            orbPrefab.animancer = orbPrefab.gameObject.AddComponent<AnimancerComponent>();
            orbPrefab.animancer.Animator = animator;
            var hitbox = orbPrefab.gameObject.AddComponent<PlayerAttackOvertimeHitBox>();
            hitbox.collider = orbPrefab.gameObject.AddComponent<CircleCollider2D>();
            hitbox.SetHitInterval(0.04f);
            orbPrefab.hitbox = hitbox;
            orbPrefab.attackStartAnim = Clip(0.12f);
            orbPrefab.attackAnim = Clip(0.2f);
            orbPrefab.attackEndAnim = Clip(0.8f);
            orbPrefab.attackStartAnimTransitionAfterFinish = true;
            orbPrefab.attackAnimTransitionAfterFinish = false;

            weaponPrefab = Create("Circling Test Emitter", false).AddComponent<CirclingAttackBehaviour>();
            weaponPrefab.attackPrefab = orbPrefab;
            weaponPrefab.baseRadius = 2f;
            weaponPrefab.baseSpeed = 2f;
            weaponPrefab.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            definition = ScriptableObject.CreateInstance<WeaponData>();
            objects.Add(definition);
            definition.ID = 6;
            definition.WeaponPrefab = weaponPrefab;
            definition.modifierFlags = (ModifierFlags)int.MaxValue;
        }

        [TearDown]
        public void TearDown()
        {
            build?.ClearBuild();
            for (int index = objects.Count - 1; index >= 0; index--)
                if (objects[index] != null) UnityEngine.Object.DestroyImmediate(objects[index]);
            objects.Clear(); spawns.Clear(); hiding.Clear(); ends.Clear();
            PoolManager.Instance = null;
        }

        [UnityTest]
        public IEnumerator EveryOrbSharesFrozenBuildAndKeepsItsOriginalOrdinal()
        {
            Equip(3, 1f);
            weapon.Attack();
            CirclingSnapshotTestAttack[] orbs = ActiveOrbs();
            AttackSnapshot snapshot = orbs[0].CurrentSnapshot;
            Assert.That(orbs.Length, Is.EqualTo(3));
            Assert.That(orbs.All(orb => ReferenceEquals(orb.CurrentSnapshot, snapshot)), Is.True);
            Assert.That(spawns.Select(spawn => spawn.Key.OrbIndex), Is.EqualTo(new ushort[] { 0, 1, 2 }));
            Assert.That(spawns.Select(spawn => spawn.Key.AttackEventId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(spawns.All(spawn => spawn.Radius == 2.2f && spawn.AngularSpeedRadians == 2f && spawn.OrbitDuration == 1f), Is.True);
#if UNITY_EDITOR
            var replaceable = new List<PlayerBuildEquipmentHandle>();
            foreach (string stat in new[] { "Damage", "Size", "Speed" })
            {
                PlayerBuildEquipmentHandle handle = build.AddEquipment(weapon,
                    UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>(
                        $"Assets/MonoBehaviour/StatRaise_{stat}RaiseEquipment.asset"), 0);
                if (stat != "Damage") replaceable.Add(handle);
            }
            Assert.That(weapon.SizeValue, Is.GreaterThan(1.1f));
            Assert.That(weapon.SpeedValue, Is.GreaterThan(1f));
            // A weapon has three equipment slots. Replace two cards to exercise all five stats legally.
            foreach (PlayerBuildEquipmentHandle handle in replaceable) build.RemoveEquipment(handle);
            foreach (string stat in new[] { "Duration", "ProjectileCount" })
                build.AddEquipment(weapon, UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>(
                    $"Assets/MonoBehaviour/StatRaise_{stat}RaiseEquipment.asset"), 0);
#else
            throw new NotSupportedException("Integration cards are loaded through the Editor.");
#endif
            Assert.That(weapon.DamageValue, Is.GreaterThan(12));
            Assert.That(weapon.ProjectileCountValue, Is.GreaterThan(3));
            Assert.That(snapshot.Stats.Damage, Is.EqualTo(12));
            Assert.That(snapshot.Stats.Duration, Is.EqualTo(1f));
            orbs[1].gameObject.SetActive(false);
            yield return null;
            Assert.That(weapon.ActiveOrbCount, Is.EqualTo(2));
            float elapsed = Field<float>(weapon, "_elapsedAttackTime");
            AssertPosition(orbs[2], spawns[2], elapsed);
            Assert.That(Quaternion.Angle(orbs[2].transform.localRotation, orbPrefab.transform.localRotation), Is.LessThan(0.001f));
            Assert.That(snapshot.IsDisposed, Is.False);
            weapon.Deactivate();
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator PeriodicHitsUseNativeSnapshotAfterBuildChangesAndStopOnCancellation()
        {
            Equip(1, 1f);
            weapon.Attack();
            CirclingSnapshotTestAttack orb = ActiveOrbs().Single();
            AttackSnapshot snapshot = orb.CurrentSnapshot;
#if UNITY_EDITOR
            build.AddEquipment(weapon, UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset"), 0);
#endif
            MeleeTestTarget target = Target("Circling Periodic Target");
            Trigger(orb.hitbox, target.GetComponent<Collider2D>());
            Trigger(orb.hitbox, target.GetComponent<Collider2D>());
            Assert.That(target.NativeHitCount, Is.EqualTo(1));
            yield return new WaitForSeconds(0.15f);
            Assert.That(target.NativeHitCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(target.Health, Is.EqualTo(1000 - 12 * target.NativeHitCount));
            Assert.That(target.LegacyDamageCalls, Is.Zero);
            weapon.Deactivate();
            int hits = target.NativeHitCount;
            yield return new WaitForSeconds(0.08f);
            Assert.That(target.NativeHitCount, Is.EqualTo(hits));
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator DurationIncludesShowAndCooldownBeginsWhileHideStillRetainsTheSnapshot()
        {
            Equip(2, 0.06f);
            bool hidingBeforeAnimation = true;
            weapon.PresentationHiding += _ => hidingBeforeAnimation &= ActiveOrbs().Any(orb => !orb.HasEndState);
            weapon.Attack();
            CirclingSnapshotTestAttack[] orbs = ActiveOrbs();
            AttackSnapshot snapshot = orbs[0].CurrentSnapshot;
            Assert.That(weapon.GetAttackSequenceDuration(), Is.EqualTo(0.06f).Within(0.0001f));
            Assert.That(orbs.All(orb => orb.RequestedDuration == -1f), Is.True);
            Invoke("Update");
            Assert.That(weapon.LastAttackElapsedTime, Is.Zero);
            yield return WaitUntilHiding(2);
            Assert.That(hidingBeforeAnimation, Is.True);
            Assert.That(hiding.All(edge => edge.OrbitElapsedSeconds >= 0.06f), Is.True);
            Assert.That(orbs.All(orb => orb.HasEndState), Is.True);
            Assert.That(snapshot.IsDisposed, Is.False, "Hide remains a retained native collision lifetime.");
            Assert.That(weapon.LastAttackElapsedTime, Is.Zero);
            AssertPosition(orbs[1], spawns[1], hiding[1].OrbitElapsedSeconds);
            Invoke("Update");
            Assert.That(weapon.LastAttackElapsedTime, Is.GreaterThan(0));
            foreach (CirclingSnapshotTestAttack orb in orbs) orb.Complete();
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator FasterCooldownTerminatesTheOldHideBeforeStartingANewRoot()
        {
            Equip(2, 0.02f, 10f);
            var order = new List<string>();
            weapon.PresentationSpawned += _ => order.Add("spawn");
            weapon.PresentationTerminated += _ => order.Add("end");
            weapon.Attack();
            AttackSnapshot oldSnapshot = ActiveOrbs()[0].CurrentSnapshot;
            yield return WaitUntilHiding(2);
            Assert.That(weapon.GetCooldown(), Is.LessThan(orbPrefab.GetEndPresentationDuration()));
            Assert.That(ActiveOrbs().All(orb => orb.HasEndState), Is.True);
            weapon.RestoreCooldownRemaining(0f);
            Invoke("Update");
            Assert.That(order, Is.EqualTo(new[] { "spawn", "spawn", "end", "end", "spawn", "spawn" }));
            Assert.That(oldSnapshot.IsDisposed, Is.True);
            Assert.That(weapon.ActiveOrbCount, Is.EqualTo(2));
            Assert.That(ActiveOrbs().All(orb => orb.CurrentSnapshot != oldSnapshot), Is.True);
            Assert.That(spawns[2].Key.AttackEventId, Is.Not.EqualTo(spawns[0].Key.AttackEventId));
        }

        [Test]
        public void ZeroDurationHidesSynchronouslyWithoutLeavingACoroutineOrAddingShowTime()
        {
            Equip(2, 0f);
            weapon.Attack();
            Assert.That(hiding.Count, Is.EqualTo(2));
            Assert.That(hiding.All(edge => edge.OrbitElapsedSeconds == 0f), Is.True);
            Assert.That(weapon.GetAttackSequenceDuration(), Is.Zero);
            Assert.That(Field<Coroutine>(weapon, "_movementCoroutine"), Is.Null);
            Assert.That(Field<bool>(weapon, "_motionActive"), Is.False);
            Assert.That(ActiveOrbs().All(orb => orb.HasEndState), Is.True);
            weapon.RestoreCooldownRemaining(2f);
            Assert.That(weapon.LastAttackElapsedTime, Is.EqualTo(weapon.GetCooldown() - 2f));
            Assert.That((bool)Invoke("CheckCooldown"), Is.False);
        }

        [UnityTest]
        public IEnumerator DisablingEveryOrbDoesNotShortenTheFrozenMovementDeadline()
        {
            Equip(2, 0.06f);
            weapon.Attack();
            CirclingSnapshotTestAttack[] orbs = ActiveOrbs();
            AttackSnapshot snapshot = orbs[0].CurrentSnapshot;
            foreach (CirclingSnapshotTestAttack orb in orbs) orb.gameObject.SetActive(false);
            Assert.That(weapon.ActiveOrbCount, Is.Zero);
            Assert.That(ends.Count, Is.EqualTo(2));
            Assert.That(snapshot.IsDisposed, Is.True, "An empty orbit clock needs no gameplay snapshot lease.");
            Assert.That(Field<bool>(weapon, "_motionActive"), Is.True);
            weapon.Attack();
            Assert.That(spawns.Count, Is.EqualTo(2));
            float deadline = Time.realtimeSinceStartup + 2f;
            while (Field<bool>(weapon, "_motionActive") && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(Field<bool>(weapon, "_motionActive"), Is.False);
            Assert.That(hiding, Is.Empty, "Cancelled bodies must not publish a later Hide.");
            Assert.That(weapon.LastAttackElapsedTime, Is.Zero);
        }

        [UnityTest]
        public IEnumerator EmptyOrbitThenDisabledParentDeactivationRetainsNoGameplaySnapshot()
        {
            Equip(2, 1f);
            weapon.Attack();
            CirclingSnapshotTestAttack[] orbs = ActiveOrbs();
            AttackSnapshot snapshot = orbs[0].CurrentSnapshot;
            foreach (CirclingSnapshotTestAttack orb in orbs) orb.gameObject.SetActive(false);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(weapon.enabled, Is.False);
            weapon.gameObject.SetActive(false);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(weapon.ActiveOrbCount, Is.Zero);
            // The explicit weapon lifecycle also clears Unity's stopped, presentation-free orbit clock.
            weapon.Deactivate();
            Assert.That(Field<bool>(weapon, "_motionActive"), Is.False);
            Assert.That(Field<Coroutine>(weapon, "_movementCoroutine"), Is.Null);
            yield return null;
            weapon.gameObject.SetActive(true);
            weapon.Attack();
            Assert.That(ActiveOrbs().Length, Is.EqualTo(2));
            Assert.That(ActiveOrbs().All(orb => orb.CurrentSnapshot != snapshot), Is.True);
        }

        [UnityTest]
        public IEnumerator DisabledEmitterParentDeactivationClosesEveryLeaseImmediately()
        {
            Equip(3, 1f);
            weapon.Attack();
            AttackSnapshot snapshot = ActiveOrbs()[0].CurrentSnapshot;
            Assert.That(weapon.enabled, Is.False);
            weapon.gameObject.SetActive(false);
            Assert.That(weapon.ActiveOrbCount, Is.Zero);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(ends.Count, Is.EqualTo(3));
            weapon.Attack();
            yield return null;
            Assert.That(spawns.Count, Is.EqualTo(3));
            Assert.That(hiding, Is.Empty);
            Assert.That(Field<Coroutine>(weapon, "_movementCoroutine"), Is.Null);
        }

        [Test]
        public void CancelInsideSpawnNotificationPreventsEveryRemainingOrb()
        {
            Equip(3, 1f);
            AttackSnapshot snapshot = null;
            weapon.PresentationSpawned += _ =>
            {
                snapshot = weapon.GetComponentsInChildren<CirclingSnapshotTestAttack>(true)
                    .Single(orb => orb.CurrentSnapshot != null).CurrentSnapshot;
                weapon.gameObject.SetActive(false);
                weapon.Attack();
            };
            weapon.Attack();
            Assert.That(spawns.Count, Is.EqualTo(1));
            Assert.That(ends.Count, Is.EqualTo(1));
            Assert.That(weapon.ActiveOrbCount, Is.Zero);
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [Test]
        public void CancelInsideAnimationStartPreventsEveryRemainingOrb()
        {
            Equip(3, 1f);
            AttackSnapshot snapshot = null;
            weapon.PresentationSpawned += _ =>
            {
                CirclingSnapshotTestAttack orb = weapon.GetComponentsInChildren<CirclingSnapshotTestAttack>(true)
                    .Single(attack => attack.CurrentSnapshot != null);
                snapshot = orb.CurrentSnapshot;
                orb.Beginning = () => weapon.gameObject.SetActive(false);
            };
            weapon.Attack();
            Assert.That(spawns.Count, Is.EqualTo(1));
            Assert.That(ends.Count, Is.EqualTo(1));
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(weapon.ActiveOrbCount, Is.Zero);
        }

        [Test]
        public void ExternalHideDisablePublishesEndBeforeRootCompletionWithExecutionDisabled()
        {
            Equip(1, 0f);
            var order = new List<string>();
            weapon.PresentationTerminated += _ => order.Add("end");
            build.NativeAttackCompleted += (_, __, ___) => order.Add("complete");
            weapon.Attack();
            CirclingSnapshotTestAttack orb = ActiveOrbs().Single();
            AttackSnapshot snapshot = orb.CurrentSnapshot;
            Assert.That(weapon.enabled, Is.False);
            orb.gameObject.SetActive(false);
            Assert.That(order, Is.EqualTo(new[] { "end" }));
            Assert.That(weapon.ActiveOrbCount, Is.Zero);
            typeof(PlayerBuildRuntime).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(build, null);
            Assert.That(order, Is.EqualTo(new[] { "end", "complete" }));
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [Test]
        public void RemoteStartMainAndHideSeekPreserveRotationAndNeverCreateDamage()
        {
            Equip(1, 1.2f);
            CirclingAttackBehaviour replica = CreateReplica();
            OrbitPresentationSpawn firstSpawn = Spawn(101, 1, 2, 1.2f);
            OrbitPresentationSpawn secondSpawn = Spawn(202, 0, 1, 2f);
            var first = (CirclingSnapshotTestAttack)replica.PlayPresentation(firstSpawn, 0.05f);
            var second = (CirclingSnapshotTestAttack)replica.PlayPresentation(secondSpawn, 0.3f);
            Assert.That(first.StartTime, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(second.MainTime, Is.EqualTo(0.18f).Within(0.0001f));
            Assert.That(first.CurrentSnapshot, Is.Null);
            Assert.That(replica.NativeRuntime, Is.Null);
            Assert.That(replica.enabled, Is.False);
            Assert.That(first.hitbox.enabled, Is.False);
            Assert.That(Quaternion.Angle(first.transform.localRotation, orbPrefab.transform.localRotation), Is.LessThan(0.001f));
            AssertPosition(first, firstSpawn, 0.05f);
            Vector3 beforeMove = first.transform.position;
            replica.transform.position += Vector3.right * 3f;
            Assert.That(Vector3.Distance(first.transform.position, beforeMove + Vector3.right * 3f), Is.LessThan(0.001f));
            replica.TickPresentation(0.2f);
            AssertPosition(first, firstSpawn, 0.25f);
            AssertPosition(second, secondSpawn, 0.5f);
            Assert.That(replica.ApplyPresentationHiding(new OrbitPresentationHiding(6, firstSpawn.Key, 1.24f), 0.3f), Is.True);
            Assert.That(first.EndTime, Is.EqualTo(0.3f).Within(0.0001f));
            AssertPosition(first, firstSpawn, 1.24f);
            replica.TickPresentation(0.1f);
            AssertPosition(first, firstSpawn, 1.24f);
            AssertPosition(second, secondSpawn, 0.6f);
            MeleeTestTarget target = Target("Remote Circling Target");
            Trigger(first.hitbox, target.GetComponent<Collider2D>());
            Assert.That(target.NativeHitCount, Is.Zero);
            Assert.That(target.LegacyDamageCalls, Is.Zero);
            replica.DisposePresentationReplica();
            Assert.That(replica.ActiveOrbCount, Is.Zero);
        }

        [Test]
        public void RemoteLateHideCorrectsAbsoluteAnimationAgeAndExpiresOnce()
        {
            Equip(1, 1f);
            CirclingAttackBehaviour replica = CreateReplica();
            OrbitPresentationSpawn spawn = Spawn(303, 0, 1, 1f);
            int returned = 0;
            var orb = (CirclingSnapshotTestAttack)replica.PlayPresentation(spawn, 1.3f, _ => returned++);
            Assert.That(orb.EndTime, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(replica.ApplyPresentationHiding(new OrbitPresentationHiding(6, spawn.Key, 1.04f), 0.4f), Is.True);
            Assert.That(orb.EndTime, Is.EqualTo(0.4f).Within(0.0001f), "A reliable Hide correction sets age rather than adding to it.");
            AssertPosition(orb, spawn, 1.04f);
            Assert.That(replica.ApplyPresentationHiding(new OrbitPresentationHiding(6, spawn.Key, 1.04f), 0.8f), Is.True);
            Assert.That(returned, Is.EqualTo(1));
            Assert.That(replica.ActiveOrbCount, Is.Zero);
            Assert.That(replica.TerminatePresentation(spawn.Key), Is.False);
            Assert.That(replica.PlayPresentation(Spawn(304, 0, 1, 1f), 2f), Is.Null);
        }

        [Test]
        public void RemoteExternalDisableRemovesTrackingWithoutTickAndRejectsEarlyHide()
        {
            Equip(1, 1f);
            CirclingAttackBehaviour replica = CreateReplica();
            OrbitPresentationSpawn spawn = Spawn(401, 0, 1, 1f);
            int returned = 0;
            AnimatedAttack orb = replica.PlayPresentation(spawn, 0f, _ => returned++);
            Assert.That(replica.ApplyPresentationHiding(new OrbitPresentationHiding(6, spawn.Key, 0.2f), 0f), Is.False);
            orb.gameObject.SetActive(false);
            Assert.That(returned, Is.EqualTo(1));
            Assert.That(replica.ActiveOrbCount, Is.Zero);
            Assert.That(replica.TerminatePresentation(spawn.Key), Is.False);
            replica.gameObject.SetActive(false);
            Assert.That(replica.PlayPresentation(Spawn(402, 0, 1, 1f), 0f), Is.Null);
        }

        [UnityTest]
        public IEnumerator PoolReuseBindsBeforeEnableAndExternalCancelDiscardsTheOldInstance()
        {
            Equip(2, 0f);
            weapon.Attack();
            CirclingSnapshotTestAttack[] original = ActiveOrbs();
            AttackSnapshot snapshot = original[0].CurrentSnapshot;
            Assert.That(original.All(orb => orb.EarlyEnableCount == 0), Is.True);
            foreach (CirclingSnapshotTestAttack orb in original) orb.Complete();
            Assert.That(snapshot.IsDisposed, Is.True);
            weapon.Attack();
            CirclingSnapshotTestAttack[] reused = ActiveOrbs();
            Assert.That(reused.All(orb => original.Contains(orb)), Is.True);
            Assert.That(reused.All(orb => orb.EarlyEnableCount == 0 && orb.CurrentSnapshot != snapshot), Is.True);
            weapon.gameObject.SetActive(false);
            Assert.That(weapon.ActiveOrbCount, Is.Zero);
            yield return null;
            weapon.gameObject.SetActive(true);
            weapon.Attack();
            Assert.That(ActiveOrbs().All(orb => !reused.Contains(orb)), Is.True);
            Assert.That(ActiveOrbs().All(orb => orb.EarlyEnableCount == 0), Is.True);
        }

        private void Equip(int count, float duration, float speed = 1f)
        {
            definition.ConfigureNativeGas(new GasAttackStats
            {
                damage = 12, speed = speed, size = 1.1f, duration = duration,
                projectileCount = count, critMultiplier = 1, critRate = 0
            }, CombatTags.Attack, new WeaponPresentationSettings());
            weapon = (CirclingAttackBehaviour)build.EquipWeapon(definition);
            weapon.PresentationSpawned += spawns.Add;
            weapon.PresentationHiding += hiding.Add;
            weapon.PresentationTerminated += ends.Add;
        }

        private CirclingAttackBehaviour CreateReplica()
        {
            var replica = UnityEngine.Object.Instantiate(weaponPrefab);
            objects.Add(replica.gameObject);
            replica.InitializePresentationReplica(6, owner);
            replica.gameObject.SetActive(true);
            return replica;
        }

        private IEnumerator WaitUntilHiding(int count)
        {
            float deadline = Time.realtimeSinceStartup + 2f;
            while (hiding.Count < count && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(hiding.Count, Is.EqualTo(count));
        }

        private ClipTransition Clip(float duration)
        {
            var clip = new AnimationClip();
            objects.Add(clip);
            clip.SetCurve("Visual", typeof(Transform), "m_LocalScale.x", AnimationCurve.Linear(0, 1, duration, 1));
            return new ClipTransition { Clip = clip };
        }

        private static OrbitPresentationSpawn Spawn(ulong root, ushort index, int count, float duration) =>
            new OrbitPresentationSpawn(6, new OrbitPresentationKey(root, index), count,
                2f * Mathf.PI * index / count, 2.2f, 2f, duration,
                new ProjectilePresentationStats { Duration = duration, ProjectileCount = count, BaseProjectileCount = count });

        private static void AssertPosition(AnimatedAttack orb, OrbitPresentationSpawn spawn, float elapsed)
        {
            float phase = spawn.InitialPhaseRadians + spawn.AngularSpeedRadians * elapsed;
            Vector3 expected = new Vector3(Mathf.Cos(phase), Mathf.Sin(phase), 0f) * spawn.Radius;
            Assert.That(Vector3.Distance(orb.transform.localPosition, expected), Is.LessThan(0.0001f));
        }

        private CirclingSnapshotTestAttack[] ActiveOrbs() => weapon.GetComponentsInChildren<CirclingSnapshotTestAttack>()
            .Where(orb => orb.CurrentSnapshot != null).ToArray();

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

        private static T Field<T>(CirclingAttackBehaviour target, string field) =>
            (T)typeof(CirclingAttackBehaviour).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private object Invoke(string method)
        {
            MethodInfo info = typeof(CirclingAttackBehaviour).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? typeof(WeaponBehaviour).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            return info.Invoke(weapon, null);
        }

        private static void Trigger(BaseAttackHitBox box, Collider2D other) =>
            typeof(PlayerAttackOvertimeHitBox).GetMethod("OnTriggerEnter2D", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(box, new object[] { other });

        private sealed class FixedRandom : IRandomSource { public float Next01() => 0.99f; }
    }

    public sealed class CirclingSnapshotTestAttack : AnimatedAttack
    {
        public AttackSnapshot CurrentSnapshot => NativeAttackSnapshot;
        public float RequestedDuration { get; private set; }
        public int EarlyEnableCount { get; private set; }
        public bool HasEndState => _endAnimState.IsValid();
        public double StartTime => _startAnimState.IsValid() ? _startAnimState.Time : -1d;
        public double MainTime => _mainAnimState.IsValid() ? _mainAnimState.Time : -1d;
        public double EndTime => _endAnimState.IsValid() ? _endAnimState.Time : -1d;
        public Action Beginning { get; set; }
        private void OnEnable() { if (CurrentSnapshot == null && !IsPresentationOnly) EarlyEnableCount++; }
        public override void Attack()
        {
            RequestedDuration = _attackAnimDuration;
            Beginning?.Invoke();
            if (gameObject.activeInHierarchy && (CurrentSnapshot != null || IsPresentationOnly)) base.Attack();
        }
        public override void Dispose() { Beginning = null; base.Dispose(); }
        public void Complete() => EndCallback();
    }
}
