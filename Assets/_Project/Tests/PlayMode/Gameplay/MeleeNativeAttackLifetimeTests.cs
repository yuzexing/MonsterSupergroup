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
using GasDamageInfo = MonsterSupergroup.GAS.DamageInfo;
using LegacyDamageType = AstralShift.HellMaiden.Player.Attacks.DamageType;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class MeleeNativeAttackLifetimeTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<MeleePresentationSpawn> spawns = new List<MeleePresentationSpawn>();
        private readonly List<MeleePresentationTermination> ends = new List<MeleePresentationTermination>();
        private PlayerBuildRuntime build;
        private PlayerMovement owner;
        private MeleeAttackBehaviour weapon;
        private WeaponData definition;
        private Transform weaponParent;

        [SetUp]
        public void SetUp()
        {
            Create("Melee Pool").AddComponent<PoolManager>().Init();
            GameObject player = Create("Melee Owner", false);
            owner = player.AddComponent<PlayerMovement>();
            weaponParent = Create("Live Weapons").transform;
            owner.AttacksParent = weaponParent;
            build = player.AddComponent<PlayerBuildRuntime>();
            build.Initialize(owner, new FixedRandom());
            build.SetWeaponExecutionEnabled(false);

            var hitPrefab = Create("Test Slash", false).AddComponent<MeleeSnapshotTestAttack>();
            var collider = hitPrefab.gameObject.AddComponent<BoxCollider2D>();
            hitPrefab.hitbox = hitPrefab.gameObject.AddComponent<PlayerAttackHitBox>();
            hitPrefab.hitbox.collider = collider;
            var weaponPrefab = Create("Test Melee", false).AddComponent<MeleeAttackBehaviour>();
            weaponPrefab.prefab = hitPrefab;
            weaponPrefab.multiProjectilesInterval = 0.02f;
            definition = ScriptableObject.CreateInstance<WeaponData>();
            objects.Add(definition);
            definition.ID = 1;
            definition.WeaponPrefab = weaponPrefab;
            definition.modifierFlags = (ModifierFlags)int.MaxValue;
            ConfigureStats(1);
        }

        [TearDown]
        public void TearDown()
        {
            build?.ClearBuild();
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
            spawns.Clear();
            ends.Clear();
            PoolManager.Instance = null;
        }

        [Test]
        public void ActivePrefabEnablesOnlyAfterBindingOnceForFreshAndReusedSlash()
        {
            definition.WeaponPrefab.GetComponent<MeleeAttackBehaviour>().prefab.gameObject.SetActive(true);
            Equip(1);
            weapon.Attack();
            var first = ActiveSlashes().Single();
            Assert.That(first.EnableCount, Is.EqualTo(1));
            Assert.That(first.EarlyEnableCount, Is.Zero);
            first.Complete();

            weapon.Attack();
            var reused = ActiveSlashes().Single();
            Assert.That(reused, Is.SameAs(first));
            Assert.That(reused.EnableCount, Is.EqualTo(2));
            Assert.That(reused.EarlyEnableCount, Is.Zero);
            reused.Complete();
        }

        [Test]
        public void SingleSlashCanAttackAgainAfterSynchronousBurstCompletionAndPoolReuse()
        {
            Equip(1);
            weapon.Attack();
            MeleeSnapshotTestAttack slash = ActiveSlashes().Single();
            AttackSnapshot first = slash.LastSnapshot;
            Assert.That(first.IsDisposed, Is.False);
            slash.Complete();
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(slash.BoundWeapon, Is.Null, "A pooled slash must release its previous weapon and player reference.");
            weapon.Attack();
            MeleeSnapshotTestAttack reused = ActiveSlashes().Single();
            Assert.That(reused, Is.SameAs(slash));
            Assert.That(reused.LastSnapshot, Is.Not.SameAs(first));
            Assert.That(reused.BoundWeapon, Is.SameAs(weapon));
            Assert.That(spawns.Count, Is.EqualTo(2));
            Assert.That(spawns[0].Key.AttackEventId, Is.Not.EqualTo(spawns[1].Key.AttackEventId));
            reused.Complete();
            Assert.That(ends.Count, Is.EqualTo(2));
            Assert.That(reused.LastSnapshot.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator BurstLeaseKeepsRootAliveBetweenSlashesAndCompletesAfterFinalSlash()
        {
            Equip(3);
            weapon.Attack();
            MeleeSnapshotTestAttack first = ActiveSlashes().Single();
            AttackSnapshot snapshot = first.LastSnapshot;
            first.Complete();
            Assert.That(snapshot.IsDisposed, Is.False, "The burst still owns future slashes.");
            yield return new WaitForSeconds(0.12f);
            MeleeSnapshotTestAttack[] remaining = ActiveSlashes();
            Assert.That(remaining.Length, Is.EqualTo(2));
            Assert.That(spawns.Select(value => value.Key.AttackEventId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(spawns.Select(value => value.Key.SlashIndex), Is.EqualTo(new ushort[] { 0, 1, 2 }));
            foreach (MeleeSnapshotTestAttack slash in remaining)
            {
                Assert.That(slash.LastSnapshot, Is.SameAs(snapshot));
                slash.Complete();
            }
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(ends.Count, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator BuildChangeAndDisabledAutomaticExecutionKeepFrozenDamageAndPerSlashDedup()
        {
            Equip(3);
            weapon.enabled = true;
            weapon.Attack();
            AttackSnapshot snapshot = ActiveSlashes().Single().LastSnapshot;
#if UNITY_EDITOR
            var damageCard = UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset");
            Assert.That(damageCard, Is.Not.Null);
            build.SetWeaponExecutionEnabled(false);
            build.AddEquipment(weapon, damageCard, 0);
#else
            throw new NotSupportedException("The integration card is loaded through the Editor.");
#endif
            Assert.That(weapon.DamageValue, Is.GreaterThan(19));
            yield return new WaitForSeconds(0.12f);
            MeleeSnapshotTestAttack[] slashes = ActiveSlashes();
            Assert.That(slashes.Length, Is.EqualTo(3));
            var target = Create("Melee Damage Target").AddComponent<MeleeTestTarget>();
            Collider2D targetCollider = target.gameObject.AddComponent<BoxCollider2D>();
            foreach (MeleeSnapshotTestAttack slash in slashes)
            {
                Assert.That(slash.LastSnapshot, Is.SameAs(snapshot));
                Assert.That(slash.LastSnapshot.Stats.Damage, Is.EqualTo(19));
                Trigger(slash.hitbox, targetCollider);
                Trigger(slash.hitbox, targetCollider);
                slash.Complete();
            }
            Assert.That(target.NativeHitCount, Is.EqualTo(3), "Each slash deduplicates independently.");
            Assert.That(target.Health, Is.EqualTo(1000 - 19 * 3));
            Assert.That(target.LegacyDamageCalls, Is.Zero);
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator WeaponRemovalStopsFutureSlashesAndReleasesEveryLeaseOnce()
        {
            Equip(3);
            weapon.Attack();
            AttackSnapshot snapshot = ActiveSlashes().Single().LastSnapshot;
            Assert.That(build.UnequipWeapon(weapon), Is.True);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(ends.Count, Is.EqualTo(1));
            yield return new WaitForSeconds(0.12f);
            Assert.That(spawns.Count, Is.EqualTo(1));
            Assert.That(ends.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DeactivatingWeaponCancelsBurstWhileRepeatedCleanupIsSafe()
        {
            Equip(3);
            weapon.Attack();
            AttackSnapshot snapshot = ActiveSlashes().Single().LastSnapshot;
            weapon.gameObject.SetActive(false);
            weapon.Deactivate();
            yield return new WaitForSeconds(0.12f);
            Assert.That(spawns.Count, Is.EqualTo(1));
            Assert.That(ends.Count, Is.EqualTo(1));
            Assert.That(snapshot.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator AnimatedAttackReuseCancelsOldTimeoutAndReleasesOldSnapshot()
        {
            Equip(1);
            GameObject go = Create("Animated Slash");
            go.AddComponent<Animator>();
            var attack = go.AddComponent<AnimatedAttack>();
            attack.animancer = go.AddComponent<AnimancerComponent>();
            attack.animancer.Animator = go.GetComponent<Animator>();
            var clip = new AnimationClip();
            objects.Add(clip);
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 1));
            attack.attackAnim = new ClipTransition { Clip = clip };
            attack.attackAnimTransitionAfterFinish = false;
            int oldEnd = 0;
            int newEnd = 0;
            using (AttackSnapshot first = weapon.NativeRuntime.BeginAttack(definition.AttackTags))
            using (AttackSnapshot second = weapon.NativeRuntime.BeginAttack(definition.AttackTags))
            {
                attack.InitNative(weapon, first, null, () => oldEnd++);
                attack.Attack(Vector2.right, 0.03f);
                first.Dispose();
                attack.InitNative(weapon, second, null, () => newEnd++);
                attack.Attack(Vector2.right, 0.3f);
                second.Dispose();
                Assert.That(first.IsDisposed, Is.True);
                yield return new WaitForSeconds(0.1f);
                Assert.That(oldEnd, Is.Zero);
                Assert.That(newEnd, Is.Zero);
                Assert.That(second.IsDisposed, Is.False);
                go.SetActive(false);
                Assert.That(second.IsDisposed, Is.True);
                yield return new WaitForSeconds(0.3f);
                Assert.That(newEnd, Is.Zero);
            }
        }

        [Test]
        public void PresentationAttackCannotDealDamageAndPoolReuseRestoresNativeHitCallback()
        {
            Equip(1);
            var replica = Create("Melee Replica", false).AddComponent<MeleeAttackBehaviour>();
            replica.prefab = weapon.prefab;
            replica.InitializePresentationReplica(1, owner);
            replica.gameObject.SetActive(true);
            var clip = new AnimationClip();
            objects.Add(clip);
            clip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 1));
            // The test slash intentionally holds its animation until Complete; the definition supplies its lifetime.
            weapon.prefab.attackAnim = new ClipTransition { Clip = clip };
            var key = new MeleePresentationKey(42, 0);
            var spawn = new MeleePresentationSpawn(1, key, Vector3.right, Vector2.right, -1,
                new ProjectilePresentationStats { ProjectileCount = 1, BaseProjectileCount = 1 });
            var slash = (MeleeSnapshotTestAttack)replica.PlayPresentation(spawn, 0);
            Assert.That(slash, Is.Not.Null);
            var target = Create("Presentation Target").AddComponent<MeleeTestTarget>();
            Collider2D targetCollider = target.gameObject.AddComponent<BoxCollider2D>();
            Trigger(slash.hitbox, targetCollider);
            Assert.That(target.NativeHitCount, Is.Zero);
            Assert.That(replica.TerminatePresentation(key), Is.True);
            Assert.That(replica.TerminatePresentation(key), Is.False);
            weapon.Attack();
            var reused = ActiveSlashes().Single();
            Assert.That(reused, Is.SameAs(slash));
            Assert.That(reused.hitbox.enabled, Is.True);
            Trigger(reused.hitbox, targetCollider);
            Assert.That(target.NativeHitCount, Is.EqualTo(1));
            Assert.That(replica.PlayPresentation(spawn, 5), Is.Null);
            replica.DisposePresentationReplica();
        }

        private void ConfigureStats(int count) => definition.ConfigureNativeGas(new GasAttackStats
        {
            damage = 19, speed = 1, size = 1, duration = 1,
            projectileCount = count, critMultiplier = 1, critRate = 0
        }, CombatTags.Attack, new WeaponPresentationSettings());

        private void Equip(int count)
        {
            ConfigureStats(count);
            weapon = (MeleeAttackBehaviour)build.EquipWeapon(definition);
            weapon.PresentationSpawned += spawns.Add;
            weapon.PresentationTerminated += ends.Add;
        }

        private MeleeSnapshotTestAttack[] ActiveSlashes() =>
            weapon.GetComponentsInChildren<MeleeSnapshotTestAttack>();

        private GameObject Create(string name, bool active = true)
        {
            var value = new GameObject(name);
            value.SetActive(active);
            objects.Add(value);
            return value;
        }

        private static void Trigger(BaseAttackHitBox hitbox, Collider2D collider) =>
            typeof(PlayerAttackHitBox).GetMethod("OnTriggerEnter2D", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(hitbox, new object[] { collider });

        private sealed class FixedRandom : IRandomSource { public float Next01() => 0.99f; }
    }

    public sealed class MeleeSnapshotTestAttack : AnimatedAttack
    {
        public WeaponBehaviour BoundWeapon => _behaviour;
        public AttackSnapshot LastSnapshot { get; private set; }
        public int EnableCount { get; private set; }
        public int EarlyEnableCount { get; private set; }
        private void OnEnable()
        {
            EnableCount++;
            if (NativeAttackSnapshot == null && !IsPresentationOnly) EarlyEnableCount++;
        }
        public override void Attack() { LastSnapshot = NativeAttackSnapshot; }
        public void Complete() => EndCallback();
    }

    public sealed class MeleeTestTarget : MonoBehaviour, IDamageable, INativeGasDamageable, ICombatTarget
    {
        public int Health { get; private set; } = 1000;
        public int NativeHitCount { get; private set; }
        public int LegacyDamageCalls { get; private set; }
        public bool IsAlive => Health > 0;
        public int GetID() => GetInstanceID();
        public Vector2 GetPosition() => transform.position;
        public bool IsActive() => true;
        public void Damage(Vector2 position, WeaponBehaviour weapon, LegacyDamageType type) => LegacyDamageCalls++;
        public void Damage(int value, LegacyDamageType type) => LegacyDamageCalls++;
        public bool ResolveNativeGasHit(NativeGasHit hit)
        {
            NativeHitCount++;
            hit.Runtime.ResolveHitDetailed(hit.Attack, this);
            return true;
        }
        public GasDamageInfo ReceiveDamage(GasDamageInfo damage)
        {
            Health -= damage.Value;
            return damage;
        }
        public StatusApplicationResult ApplyStatus(StatusApplication application) => StatusApplicationResult.Rejected;
    }
}
