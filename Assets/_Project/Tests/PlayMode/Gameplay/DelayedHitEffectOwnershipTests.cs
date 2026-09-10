using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Pooling;
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
    public sealed class DelayedHitEffectOwnershipTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<PlayerBuildRuntime> builds = new List<PlayerBuildRuntime>();
        private readonly List<AttackSnapshot> snapshots = new List<AttackSnapshot>();
        private readonly List<CountingHitEffectPool> pools = new List<CountingHitEffectPool>();
        private DelayedEffectTestWeapon weaponA;
        private DelayedEffectTestWeapon weaponB;
        private RecordingSink eventsA;
        private RecordingSink eventsB;
        private ControlledDelayedHitEffect prefab;

        [SetUp]
        public void SetUp()
        {
            prefab = Create("Controlled impact prefab").AddComponent<ControlledDelayedHitEffect>();
            weaponA = CreateWeapon(41, 19, out eventsA);
            weaponB = CreateWeapon(42, 31, out eventsB);
        }

        [TearDown]
        public void TearDown()
        {
            // Tests may stop at the first assertion while effects are intentionally still alive.
            foreach (CountingHitEffectPool pool in pools)
                foreach (BaseAttackHitEffect effect in pool.Created.ToArray())
                    if (effect != null)
                    {
                        if (effect is ControlledDelayedHitEffect controlled)
                        {
                            controlled.Stop();
                            controlled.ForgetCallbacks();
                        }
                        UnityEngine.Object.DestroyImmediate(effect.gameObject);
                    }
            foreach (AttackSnapshot snapshot in snapshots) snapshot.Dispose();
            foreach (PlayerBuildRuntime build in builds) if (build != null) build.Shutdown();
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
            builds.Clear();
            snapshots.Clear();
            pools.Clear();
        }

        [Test]
        public void RebindingSharedResolverKeepsEachDelayedHitOnItsOriginalOwnerAndSnapshot()
        {
            SpawnableHitEffectResolver resolver = CreateResolver(prefab, out CountingHitEffectPool pool);
            AttackSnapshot attackA = Begin(weaponA);
            resolver.Initialize(weaponA, attackA);
            var effectA = (ControlledDelayedHitEffect)pool.LastAcquired;
            attackA.Dispose();
            Assert.That(attackA.IsDisposed, Is.False, "The delayed effect must retain its attack.");

#if UNITY_EDITOR
            EquipmentData card = UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset");
            Assert.That(card, Is.Not.Null);
            builds[0].AddEquipment(weaponA, card, 0);
            Assert.That(weaponA.DamageValue, Is.EqualTo(25), "The migrated card raises damage by 30%, rounded by existing GAS stats.");
#endif
            AttackSnapshot attackB = Begin(weaponB);
            resolver.Initialize(weaponB, attackB);
            var effectB = (ControlledDelayedHitEffect)pool.LastAcquired;
            attackB.Dispose();
            Assert.That(effectB, Is.Not.SameAs(effectA));

            effectA.transform.position = new Vector3(11, 12, 0);
            resolver.transform.position = new Vector3(90, 91, 0);
            DelayedEffectTestTarget firstTarget = Target(901);
            DelayedEffectTestTarget laterTarget = Target(902);
            DelayedEffectTestTarget targetB = Target(903);
            effectA.Hit(firstTarget);
            effectA.Hit(laterTarget);
            effectB.Hit(targetB);

            Assert.That(weaponA.ReceivedSnapshots, Is.EqualTo(new[] { attackA, attackA }),
                "Reusing the resolver must not send the old effect through the new player's weapon.");
            Assert.That(weaponB.ReceivedSnapshots, Is.EqualTo(new[] { attackB }));
            Assert.That(firstTarget.LastWeapon, Is.SameAs(weaponA));
            Assert.That(firstTarget.LastRuntime, Is.SameAs(weaponA.NativeRuntime));
            Assert.That(firstTarget.LastAttackPosition, Is.EqualTo(new Vector2(11, 12)));
            Assert.That(firstTarget.Health, Is.EqualTo(981));
            Assert.That(laterTarget.Health, Is.EqualTo(981), "A new target is resolved at hit time with the old frozen damage.");
            Assert.That(targetB.Health, Is.EqualTo(969));
            AssertDamageEvents(eventsA, attackA, 41, new uint[] { 901, 902 }, 19);
            AssertDamageEvents(eventsB, attackB, 42, new uint[] { 903 }, 31);
            Assert.That(firstTarget.LegacyDamageCalls + laterTarget.LegacyDamageCalls + targetB.LegacyDamageCalls, Is.Zero);

            effectA.Stop();
            Assert.That(attackA.IsDisposed, Is.True);
            Assert.That(attackB.IsDisposed, Is.False);
            effectB.Stop();
            Assert.That(attackB.IsDisposed, Is.True);
            Assert.That(pool.ReturnCount, Is.EqualTo(2));
        }

        [Test]
        public void DestroyingResolverDoesNotOrphanAnAlreadySpawnedEffectOrItsOriginalPool()
        {
            SpawnableHitEffectResolver resolver = CreateResolver(prefab, out CountingHitEffectPool pool);
            AttackSnapshot attack = Begin(weaponA);
            resolver.Initialize(weaponA, attack);
            var effect = (ControlledDelayedHitEffect)pool.LastAcquired;
            attack.Dispose();
            UnityEngine.Object.DestroyImmediate(resolver.gameObject);

            Assert.That(attack.IsDisposed, Is.False);
            DelayedEffectTestTarget target = Target(910);
            effect.Hit(target);
            Assert.That(target.Health, Is.EqualTo(981));
            effect.Stop();
            Assert.That(attack.IsDisposed, Is.True);
            Assert.That(pool.ReturnCount, Is.EqualTo(1), "The completion must capture the live pool, not the destroyed resolver's field.");
            Assert.That(effect.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void StaleHitAndEndCallbacksCannotDamageOrReturnAnInstanceReusedByAnotherOwner()
        {
            SpawnableHitEffectResolver resolver = CreateResolver(prefab, out CountingHitEffectPool pool);
            AttackSnapshot attackA = Begin(weaponA);
            resolver.Initialize(weaponA, attackA);
            var effect = (ControlledDelayedHitEffect)pool.LastAcquired;
            Action oldEnd = effect.End;
            Action<IDamageable> oldHit = effect.HitCallback;
            attackA.Dispose();
            effect.Stop();
            effect.Stop();
            Assert.That(pool.ReturnCount, Is.EqualTo(1), "Repeated end callbacks must return a spawn only once.");

            AttackSnapshot attackB = Begin(weaponB);
            resolver.Initialize(weaponB, attackB);
            attackB.Dispose();
            Assert.That(pool.LastAcquired, Is.SameAs(effect));
            DelayedEffectTestTarget target = Target(920);
            Assert.DoesNotThrow(() => oldEnd());
            Assert.DoesNotThrow(() => oldHit(target));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(effect.gameObject.activeSelf, Is.True);
            Assert.That(attackB.IsDisposed, Is.False);
            Assert.That(target.Health, Is.EqualTo(1000));
            Assert.That(weaponA.ReceivedSnapshots, Is.Empty);
            Assert.That(weaponB.ReceivedSnapshots, Is.Empty);

            effect.Hit(target);
            Assert.That(target.Health, Is.EqualTo(969));
            effect.Stop();
            effect.Stop();
            Assert.That(pool.ReturnCount, Is.EqualTo(2));
            Assert.That(attackA.IsDisposed && attackB.IsDisposed, Is.True);
        }

        [Test]
        public void FailedPlaybackReleasesItsLeaseAndCannotReturnTheEffectAgainLater()
        {
            prefab.ThrowOnPlay = true;
            SpawnableHitEffectResolver resolver = CreateResolver(prefab, out CountingHitEffectPool pool);
            AttackSnapshot attack = Begin(weaponA);
            Assert.Throws<InvalidOperationException>(() => resolver.Initialize(weaponA, attack));
            attack.Dispose();
            Assert.That(attack.IsDisposed, Is.True);
            var effect = (ControlledDelayedHitEffect)pool.LastAcquired;
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            effect.Stop();
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
        }

        [Test]
        public void DestroyedOwnerCannotTransferItsOutstandingDelayedHitToAnotherOwner()
        {
            SpawnableHitEffectResolver resolver = CreateResolver(prefab, out CountingHitEffectPool pool);
            AttackSnapshot attackA = Begin(weaponA);
            resolver.Initialize(weaponA, attackA);
            var effectA = (ControlledDelayedHitEffect)pool.LastAcquired;
            attackA.Dispose();
            builds[0].UnequipWeapon(weaponA);
            UnityEngine.Object.DestroyImmediate(weaponA.gameObject);
            AttackSnapshot attackB = Begin(weaponB);
            resolver.Initialize(weaponB, attackB);
            attackB.Dispose();
            DelayedEffectTestTarget target = Target(930);
            Assert.DoesNotThrow(() => effectA.Hit(target));
            Assert.That(target.Health, Is.EqualTo(1000));
            Assert.That(weaponB.ReceivedSnapshots, Is.Empty);
            effectA.Stop();
            Assert.That(attackA.IsDisposed, Is.True);
            ((ControlledDelayedHitEffect)pool.LastAcquired).Stop();
            Assert.That(attackB.IsDisposed, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActualDanteImpactExternalDisableOrDestroyReleasesLeaseAndAllowsFreshSpawn(bool destroy)
        {
            AttackHitParticleEffect realPrefab = DanteImpactPrefab();
            SpawnableHitEffectResolver resolver = CreateResolver(realPrefab, out CountingHitEffectPool pool);
            AttackSnapshot first = Begin(weaponA);
            AssertDanteImpactAudioAvailable();
            resolver.Initialize(weaponA, first);
            var effect = (AttackHitParticleEffect)pool.LastAcquired;
            first.Dispose();
            Assert.That(first.IsDisposed, Is.False);
            Assert.That(effect.gameObject.activeInHierarchy, Is.True);
            if (destroy) UnityEngine.Object.DestroyImmediate(effect.gameObject);
            else effect.gameObject.SetActive(false);
            Assert.That(first.IsDisposed, Is.True, "External lifetime cleanup must release the delayed attack even without its animation callback.");

            AttackSnapshot second = Begin(weaponB);
            AssertDanteImpactAudioAvailable();
            resolver.Initialize(weaponB, second);
            var reused = (AttackHitParticleEffect)pool.LastAcquired;
            second.Dispose();
            Assert.That(reused, Is.Not.Null);
            Assert.That(reused.gameObject.activeInHierarchy, Is.True);
            Assert.That(second.IsDisposed, Is.False);
            DelayedEffectTestTarget target = Target(940);
            Trigger(reused.hitbox, target.GetComponent<Collider2D>());
            Assert.That(target.Health, Is.EqualTo(969));
            Assert.That(target.LastWeapon, Is.SameAs(weaponB));
            reused.gameObject.SetActive(false);
            Assert.That(second.IsDisposed, Is.True);
        }

        [Test]
        public void UnequippedWeaponCannotReceiveDelayedHitBeforeItsDeferredUnityDestruction()
        {
            SpawnableHitEffectResolver resolver = CreateResolver(prefab, out CountingHitEffectPool pool);
            AttackSnapshot attack = Begin(weaponA);
            resolver.Initialize(weaponA, attack);
            var effect = (ControlledDelayedHitEffect)pool.LastAcquired;
            attack.Dispose();
            Assert.That(builds[0].UnequipWeapon(weaponA), Is.True);
            Assert.That(weaponA != null, Is.True, "Unity Destroy is deferred; the source still exists during this frame.");
            Assert.That(weaponA.NativeRuntime.IsInitialized, Is.False, "Build removal shuts down GAS immediately.");
            Assert.That(attack.IsDisposed, Is.False, "The visual still owns its lease until completion.");

            DelayedEffectTestTarget target = Target(950);
            Assert.DoesNotThrow(() => effect.Hit(target));
            Assert.That(target.Health, Is.EqualTo(1000));
            Assert.That(weaponA.ReceivedSnapshots, Is.Empty);
            Assert.That(eventsA.Events.Count(value => value.Kind == CombatEventKind.DamageResolved), Is.Zero);
            effect.Stop();
            Assert.That(attack.IsDisposed, Is.True);
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ActualDanteImpactRepeatedStopCompletesAndReturnsOnlyOnce()
        {
            SpawnableHitEffectResolver resolver = CreateResolver(DanteImpactPrefab(), out CountingHitEffectPool pool);
            AttackSnapshot attack = Begin(weaponA);
            AssertDanteImpactAudioAvailable();
            resolver.Initialize(weaponA, attack);
            var effect = (AttackHitParticleEffect)pool.LastAcquired;
            attack.Dispose();
            effect.Stop();
            effect.Stop();
            float deadline = Time.realtimeSinceStartup + 3;
            while (!attack.IsDisposed && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(attack.IsDisposed, Is.True);
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(effect.gameObject.activeSelf, Is.False);
        }

        private DelayedEffectTestWeapon CreateWeapon(ushort source, int damage, out RecordingSink sink)
        {
            GameObject playerObject = Create("Delayed effect owner " + source, false);
            PlayerMovement owner = playerObject.AddComponent<PlayerMovement>();
            owner.AttacksParent = Create("Owner attacks " + source).transform;
            PlayerBuildRuntime build = playerObject.AddComponent<PlayerBuildRuntime>();
            builds.Add(build);
            sink = new RecordingSink();
            playerObject.GetComponent<CombatRuntimeServiceProvider>().Configure(new CombatRuntimeServices(
                source, (uint)(source + 100), new SequentialCombatEventIdSource(source, 3), sink));
            build.Initialize(owner, new FixedRandom());
            build.SetWeaponExecutionEnabled(false);
            DelayedEffectTestWeapon weaponPrefab = Create("Weapon prefab " + source, false).AddComponent<DelayedEffectTestWeapon>();
            WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
            objects.Add(data);
            data.ID = source;
            data.WeaponPrefab = weaponPrefab;
            data.modifierFlags = (ModifierFlags)int.MaxValue;
            data.ConfigureNativeGas(new GasAttackStats
            {
                damage = damage, critRate = 0, critMultiplier = 1,
                speed = 1, size = 1, duration = 1, projectileCount = 1
            }, CombatTags.Attack, new WeaponPresentationSettings());
            return (DelayedEffectTestWeapon)build.EquipWeapon(data);
        }

        private SpawnableHitEffectResolver CreateResolver(BaseAttackHitEffect effectPrefab, out CountingHitEffectPool pool)
        {
            pool = new CountingHitEffectPool(effectPrefab, Create("Hit effect pool").transform);
            pools.Add(pool);
            var resolver = Create("Shared effect resolver").AddComponent<SpawnableHitEffectResolver>();
            SetField(resolver, "hitEffect", effectPrefab);
            SetField(resolver, "_hitEffectPooler", pool);
            SetField(resolver, "damageMode", DamageMode.ExplosionHit);
            return resolver;
        }

        private AttackSnapshot Begin(DelayedEffectTestWeapon weapon)
        {
            AttackSnapshot snapshot = weapon.Begin();
            snapshots.Add(snapshot);
            return snapshot;
        }

        private DelayedEffectTestTarget Target(uint id)
        {
            GameObject value = Create("Impact target " + id);
            value.AddComponent<BoxCollider2D>();
            var target = value.AddComponent<DelayedEffectTestTarget>();
            target.EntityId = id;
            return target;
        }

        private GameObject Create(string name, bool active = true)
        {
            var value = new GameObject(name);
            value.SetActive(active);
            objects.Add(value);
            return value;
        }

        private static void AssertDamageEvents(RecordingSink sink, AttackSnapshot root, ushort source,
            uint[] targetIds, int damage)
        {
            CombatEvent[] events = sink.Events.Where(value => value.Kind == CombatEventKind.DamageResolved).ToArray();
            Assert.That(events.Select(value => value.Context.TargetEntityId), Is.EqualTo(targetIds));
            foreach (CombatEvent value in events)
            {
                Assert.That(value.Context.SourcePlayerId, Is.EqualTo((uint)source));
                Assert.That(value.Context.SourceEntityId, Is.EqualTo((uint)(source + 100)));
                Assert.That(value.Context.RootEventId, Is.EqualTo(root.Context.EventId));
                Assert.That(value.Context.EventId.SourceSlot, Is.EqualTo(source), "Results must use the original owner's event sequence.");
                Assert.That(value.Context.EventId.ConnectionEpoch, Is.EqualTo(3));
                Assert.That(value.ResolvedDamage.Value, Is.EqualTo(damage));
            }
        }

        private static void AssertDanteImpactAudioAvailable() => Assert.That(FMODUnity.RuntimeManager.GetEventDescription(FMOD.GUID.Parse("1235e4b8-dcb5-43e8-8bb7-41a363bff4b8")).isValid(), Is.True);

        private static AttackHitParticleEffect DanteImpactPrefab()
        {
#if UNITY_EDITOR
            GameObject asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/GameObject/PlayerAttack_Dante_Projectile_Impact.prefab");
            Assert.That(asset, Is.Not.Null);
            AttackHitParticleEffect effect = asset.GetComponent<AttackHitParticleEffect>();
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.hitbox, Is.Not.Null);
            return effect;
#else
            throw new NotSupportedException("The recovered Dante impact is loaded through the Editor.");
#endif
        }

        private static void Trigger(BaseAttackHitBox hitbox, Collider2D target) => typeof(PlayerAttackHitBox)
            .GetMethod("OnTriggerEnter2D", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hitbox, new object[] { target });

        private static void SetField(object instance, string name, object value)
        {
            for (Type type = instance.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null) continue;
                field.SetValue(instance, value);
                return;
            }
            throw new MissingFieldException(instance.GetType().Name, name);
        }

        private sealed class FixedRandom : IRandomSource { public float Next01() => 0.99f; }
        private sealed class RecordingSink : ICombatEventSink
        {
            public readonly List<CombatEvent> Events = new List<CombatEvent>();
            public void Publish(CombatEvent combatEvent) => Events.Add(combatEvent);
        }

        private sealed class CountingHitEffectPool : GenericPooler<BaseAttackHitEffect>
        {
            public readonly HashSet<BaseAttackHitEffect> Created = new HashSet<BaseAttackHitEffect>();
            public BaseAttackHitEffect LastAcquired { get; private set; }
            public int ReturnCount { get; private set; }
            public CountingHitEffectPool(BaseAttackHitEffect effectPrefab, Transform parent)
                : base(effectPrefab, effectPrefab.name, parent) { }
            public override BaseAttackHitEffect GetOrCreate(Transform parent, bool activate = false)
            {
                LastAcquired = base.GetOrCreate(parent, activate);
                Created.Add(LastAcquired);
                return LastAcquired;
            }
            public override void Return(BaseAttackHitEffect effect, bool deactivate = true)
            {
                ReturnCount++;
                base.Return(effect, deactivate);
            }
        }
    }

    // These doubles control only presentation callbacks; hits still run the production GAS pipeline.
    public sealed class ControlledDelayedHitEffect : BaseAttackHitEffect
    {
        public bool ThrowOnPlay;
        public Action End { get; private set; }
        public Action<IDamageable> HitCallback { get; private set; }
        public override void Init(WeaponBehaviour behaviour) { }
        public override void Init(WeaponBehaviour behaviour, AttackSnapshot attack) { }
        public override void PlayOnEnable(Action onEnd) => Play(onEnd);
        public override void PlayOnEnable(Action<IDamageable> onHit, Action onEnd) => Play(onHit, onEnd);
        public override void Play(Action onEnd) => Play(null, onEnd);
        public override void Play(Action<IDamageable> onHit, Action onEnd)
        {
            HitCallback = onHit;
            End = onEnd;
            if (ThrowOnPlay) throw new InvalidOperationException("Controlled playback failure.");
        }
        public override void Stop() => End?.Invoke();
        public void Hit(IDamageable target) => HitCallback?.Invoke(target);
        public void ForgetCallbacks() { End = null; HitCallback = null; }
    }

    public sealed class DelayedEffectTestWeapon : WeaponBehaviour
    {
        public readonly List<AttackSnapshot> ReceivedSnapshots = new List<AttackSnapshot>();
        public AttackSnapshot Begin() => BeginNativeGasAttack();
        protected override void Dispose() { }
        public override void OnNativeGasHit(Vector2 position, IDamageable damageable, AttackSnapshot attack)
        {
            ReceivedSnapshots.Add(attack);
            base.OnNativeGasHit(position, damageable, attack);
        }
    }

    public sealed class DelayedEffectTestTarget : MonoBehaviour, IDamageable, INativeGasDamageable,
        ICombatTarget, ICombatStateIdentity
    {
        public int Health { get; private set; } = 1000;
        public int LegacyDamageCalls { get; private set; }
        public WeaponBehaviour LastWeapon { get; private set; }
        public WeaponRuntimeBehaviour LastRuntime { get; private set; }
        public Vector2 LastAttackPosition { get; private set; }
        public uint EntityId { get; set; }
        public uint StateVersion => 1;
        public bool IsAlive => Health > 0;
        public int GetID() => GetInstanceID();
        public Vector2 GetPosition() => transform.position;
        public bool IsActive() => isActiveAndEnabled;
        public void Damage(Vector2 position, WeaponBehaviour weapon, LegacyDamageType type) => LegacyDamageCalls++;
        public void Damage(int value, LegacyDamageType type) => LegacyDamageCalls++;
        public bool ResolveNativeGasHit(NativeGasHit hit)
        {
            LastWeapon = hit.PresentationWeapon;
            LastRuntime = hit.Runtime;
            LastAttackPosition = hit.AttackPosition;
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
