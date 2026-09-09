using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class OrbitPresentationReplicaPlayModeTests
    {
        private const float ShowDuration = 0.016666668f;
        private const float HideDuration = 0.8f;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator RemoteOrb_FollowsOnlyItsPlayerWithoutRuntimeSnapshotHitCallbacksOrNetworkIdentity()
        {
            using (var fixture = new Fixture())
            {
                var targetObject = new GameObject("Orbit Replica Safety Target");
                var otherPlayer = new GameObject("Unrelated Player");
                try
                {
                    var collider = targetObject.AddComponent<CircleCollider2D>();
                    collider.radius = 20f;
                    targetObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
                    var target = targetObject.AddComponent<PresentationTarget>();
                    var spawn = Spawn(301UL);
                    AnimatedAttack orb = fixture.Spawn(spawn, 0.2f, newInstance: true);
                    var emitter = orb.GetComponentInParent<CirclingAttackBehaviour>();
                    Assert.That(emitter.enabled, Is.False);
                    Assert.That(emitter.NativeRuntime, Is.Null);
                    AssertRootless(orb);
                    Assert.That(typeof(BaseAttackHitBox).GetField("_onHit", PrivateInstance).GetValue(orb.hitbox), Is.Null);
                    Assert.That(orb.hitbox.enabled, Is.False);
                    Assert.That(fixture.Attacks.GetComponentsInChildren<Mirror.NetworkIdentity>(true), Is.Empty);
                    Assert.That(emitter.OwnerPlayer, Is.SameAs(fixture.Player.GetComponent<PlayerMovement>()));
                    Vector3 position = orb.transform.position;
                    otherPlayer.transform.position += Vector3.right * 9f;
                    Assert.That(orb.transform.position, Is.EqualTo(position));
                    fixture.Attacks.transform.position += Vector3.right * 4f;
                    Assert.That(Vector3.Distance(orb.transform.position, position + Vector3.right * 4f), Is.LessThan(0.0001f));
                    typeof(BasePlayerAttack).GetMethod("OnHit", PrivateInstance).Invoke(orb, new object[] { target });
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    Assert.That(target.NativeHitCalls, Is.Zero);
                    Assert.That(target.DamageCalls, Is.Zero);
                    Assert.That(fixture.Replica.TryTerminate(new OrbitPresentationTermination(6u, spawn.Key)), Is.True);
                    Assert.That(fixture.Replica.ActiveOrbCount, Is.Zero);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(targetObject);
                    UnityEngine.Object.DestroyImmediate(otherPlayer);
                }
            }
        }

        [UnityTest]
        public IEnumerator AgedSpawn_SeeksShowMainAndHideWithoutChangingAuthoredOrbitPlane()
        {
            using (var fixture = new Fixture())
            {
                AnimatedAttack show = fixture.Spawn(Spawn(401UL), ShowDuration * 0.5f, newInstance: true);
                AssertStateTime(show, "_startAnimState", ShowDuration * 0.5f);
                AnimatedAttack main = fixture.Spawn(Spawn(402UL), ShowDuration + 0.3f, newInstance: true);
                AssertStateTime(main, "_mainAnimState", 0.3f);
                var hideSpawn = Spawn(403UL);
                AnimatedAttack hide = fixture.Spawn(hideSpawn, hideSpawn.OrbitDuration + 0.2f, newInstance: true);
                AssertStateTime(hide, "_endAnimState", 0.2f);
                AssertPosition(hide, hideSpawn, hideSpawn.OrbitDuration);
                Assert.That(hide.GetEndPresentationDuration(), Is.EqualTo(HideDuration).Within(0.0001f));
                foreach (AnimatedAttack orb in new[] { show, main, hide })
                {
                    Assert.That(Quaternion.Angle(orb.transform.localRotation, Quaternion.identity), Is.LessThan(0.001f));
                    var emitter = orb.GetComponentInParent<CirclingAttackBehaviour>();
                    Assert.That(Quaternion.Angle(emitter.transform.localRotation, Quaternion.Euler(45f, 0f, 0f)), Is.LessThan(0.001f));
                    AssertRootless(orb);
                }
                Assert.That(fixture.Replica.ActiveOrbCount, Is.EqualTo(3));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RootsAndOrbOrdinals_KeepIndependentPhasesAfterRemovingASibling()
        {
            using (var fixture = new Fixture())
            {
                var first = Spawn(501UL, index: 0, count: 3, duration: 4f);
                var sibling = Spawn(501UL, index: 2, count: 3, duration: 4f);
                var other = Spawn(502UL, duration: 4f, phase: 0.7f, radius: 3.1f, omega: -0.8f);
                AnimatedAttack firstOrb = fixture.Spawn(first, 0f, newInstance: true);
                AnimatedAttack siblingOrb = fixture.Spawn(sibling, 0f, newInstance: true);
                AnimatedAttack otherOrb = fixture.Spawn(other, 0f, newInstance: true);
                fixture.Replica.Tick(0.25f);
                AssertPosition(firstOrb, first, 0.25f);
                AssertPosition(siblingOrb, sibling, 0.25f);
                AssertPosition(otherOrb, other, 0.25f);
                Assert.That(fixture.Replica.TryTerminate(new OrbitPresentationTermination(6u, first.Key)), Is.True);
                fixture.Replica.Tick(0.2f);
                AssertPosition(siblingOrb, sibling, 0.45f);
                AssertPosition(otherOrb, other, 0.45f);
                Assert.That(fixture.Replica.ActiveOrbCount, Is.EqualTo(2));
                Assert.That(fixture.Replica.TryHide(new OrbitPresentationHiding(6u, first.Key, 4.05f), 0f), Is.False);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ReliableHiding_CorrectsFinalOvershootPositionSeeksHideAndStopsRevolving()
        {
            using (var fixture = new Fixture())
            {
                var spawn = Spawn(601UL, duration: 2f, phase: 0.4f);
                AnimatedAttack orb = fixture.Spawn(spawn, 0.2f, newInstance: true);
                fixture.Replica.Tick(0.4f);
                AssertPosition(orb, spawn, 0.6f);
                Assert.That(fixture.Replica.TryHide(new OrbitPresentationHiding(6u, spawn.Key, 2.05f), 0.2f), Is.True);
                AssertPosition(orb, spawn, 2.05f);
                AssertStateTime(orb, "_endAnimState", 0.2f);
                Vector3 finalPosition = orb.transform.localPosition;
                fixture.Replica.Tick(0.3f);
                Assert.That(orb.transform.localPosition, Is.EqualTo(finalPosition));
                yield return null;
                Assert.That(orb.transform.localPosition, Is.EqualTo(finalPosition));
                Assert.That(orb.hitbox.collider.enabled, Is.False, "The authored Hide clip must close its collider window.");
                AssertRootless(orb);
            }
        }

        [UnityTest]
        public IEnumerator LateHiding_CorrectsAlreadyPredictedHideWithoutAddingOrbitTime()
        {
            using (var fixture = new Fixture())
            {
                var spawn = Spawn(611UL, duration: 1f);
                AnimatedAttack orb = fixture.Spawn(spawn, 0.95f, newInstance: true);
                fixture.Replica.Tick(0.1f);
                AssertPosition(orb, spawn, 1f);
                AssertStateTime(orb, "_endAnimState", 0.05f);
                Assert.That(fixture.Replica.TryHide(new OrbitPresentationHiding(6u, spawn.Key, 1.04f), 0.25f), Is.True);
                AssertPosition(orb, spawn, 1.04f);
                AssertStateTime(orb, "_endAnimState", 0.25f);
                fixture.Replica.Tick(10f);
                AssertPosition(orb, spawn, 1.04f);
                Assert.That(fixture.Replica.ActiveOrbCount, Is.EqualTo(1), "Orbit ticks must not advance an independently playing Hide animation.");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator NaturalHideEnd_ReturnsInstanceAndRejectsOldOperationsAfterPoolReuse()
        {
            using (var fixture = new Fixture())
            {
                var oldSpawn = Spawn(701UL);
                AnimatedAttack oldOrb = fixture.Spawn(oldSpawn, oldSpawn.OrbitDuration + HideDuration - 0.08f, newInstance: true);
                float deadline = Time.realtimeSinceStartup + 2f;
                while (fixture.Replica.ActiveOrbCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(fixture.Replica.ActiveOrbCount, Is.Zero);
                Assert.That(oldOrb.gameObject.activeInHierarchy, Is.False);
                Assert.That(fixture.Replica.TryTerminate(new OrbitPresentationTermination(6u, oldSpawn.Key)), Is.False);
                var currentSpawn = Spawn(702UL, duration: 3f);
                AnimatedAttack currentOrb = fixture.Spawn(currentSpawn, 0.1f, newInstance: false);
                Assert.That(currentOrb, Is.SameAs(oldOrb), "Exercise the real shared pool rather than a replacement object.");
                Assert.That(fixture.Replica.TryHide(new OrbitPresentationHiding(6u, oldSpawn.Key, 2.1f), 0f), Is.False);
                Assert.That(fixture.Replica.TryTerminate(new OrbitPresentationTermination(6u, oldSpawn.Key)), Is.False);
                yield return new WaitForSeconds(0.2f);
                fixture.Replica.Tick(0.2f);
                Assert.That(fixture.Replica.ActiveOrbCount, Is.EqualTo(1));
                Assert.That(currentOrb.gameObject.activeInHierarchy, Is.True);
                AssertPosition(currentOrb, currentSpawn, 0.3f);
                AssertRootless(currentOrb);
            }
        }

        [UnityTest]
        public IEnumerator PredictedExpiry_StartsHideThenReturnsWithoutReliableEnd()
        {
            using (var fixture = new Fixture())
            {
                var spawn = Spawn(711UL, duration: 0.15f);
                AnimatedAttack orb = fixture.Spawn(spawn, 0.1f, newInstance: true);
                fixture.Replica.Tick(0.08f);
                AssertPosition(orb, spawn, 0.15f);
                AssertStateTime(orb, "_endAnimState", 0.03f);
                float deadline = Time.realtimeSinceStartup + 2f;
                while (fixture.Replica.ActiveOrbCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(fixture.Replica.ActiveOrbCount, Is.Zero);
                Assert.That(orb.gameObject.activeInHierarchy, Is.False);
                Assert.That(fixture.Replica.TryTerminate(new OrbitPresentationTermination(6u, spawn.Key)), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator ExternalOrbAndParentDeactivation_ClearCountsSynchronouslyWithoutTick()
        {
            using (var fixture = new Fixture())
            {
                var first = Spawn(801UL);
                AnimatedAttack firstOrb = fixture.Spawn(first, 0.1f, newInstance: true);
                var emitter = firstOrb.GetComponentInParent<CirclingAttackBehaviour>();
                firstOrb.gameObject.SetActive(false);
                Assert.That(fixture.Replica.ActiveOrbCount, Is.Zero);
                Assert.That(emitter.ActiveOrbCount, Is.Zero);
                Assert.That(fixture.Replica.TryHide(new OrbitPresentationHiding(6u, first.Key, 2.1f), 0f), Is.False);
                Assert.That(fixture.Replica.TryTerminate(new OrbitPresentationTermination(6u, first.Key)), Is.False);
                // External deactivation discards the instance to avoid reparenting within Unity's lifecycle stack.
                AnimatedAttack secondOrb = fixture.Spawn(Spawn(802UL), 0.1f, newInstance: true);
                fixture.Spawn(Spawn(803UL), 0.1f, newInstance: true);
                Assert.That(secondOrb, Is.Not.SameAs(firstOrb));
                fixture.Attacks.SetActive(false);
                Assert.That(fixture.Replica.ActiveOrbCount, Is.Zero);
                Assert.That(emitter.ActiveOrbCount, Is.Zero);
                yield return null;
                fixture.Attacks.SetActive(true);
                fixture.Spawn(Spawn(804UL), 0.1f, newInstance: true);
                Assert.That(fixture.Replica.ActiveOrbCount, Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator ExpiredInvalidAgeAndWrongWeaponInputs_LeaveTheLiveOrbUnchanged()
        {
            using (var fixture = new Fixture())
            {
                var spawn = Spawn(901UL);
                foreach (float age in new[] { -0.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    Assert.That(fixture.Replica.TrySpawn(spawn, age), Is.False);
                // Use an age beyond the clip boundary; Mono may retain extra precision in
                // the runtime's duration sum while the method argument is rounded to float.
                Assert.That(fixture.Replica.TrySpawn(spawn, spawn.OrbitDuration + HideDuration + 0.001f), Is.False);
                Assert.That(fixture.Replica.TrySpawn(Spawn(902UL, weaponId: 999u), 0f), Is.False);
                Assert.That(fixture.Replica.TrySpawn(Spawn(0UL), 0f), Is.False);
                Assert.That(fixture.Replica.ActiveOrbCount, Is.Zero);
                AnimatedAttack orb = fixture.Spawn(spawn, 0.1f, newInstance: true);
                Vector3 position = orb.transform.localPosition;
                Assert.That(fixture.Replica.TrySpawn(spawn, 0f), Is.False);
                foreach (float age in new[] { -0.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    Assert.That(fixture.Replica.TryHide(new OrbitPresentationHiding(6u, spawn.Key, 2.1f), age), Is.False);
                foreach (float elapsed in new[] { 1.9f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    Assert.That(fixture.Replica.TryHide(new OrbitPresentationHiding(6u, spawn.Key, elapsed), 0f), Is.False);
                Assert.That(fixture.Replica.TryHide(new OrbitPresentationHiding(999u, spawn.Key, 2.1f), 0f), Is.False);
                Assert.That(fixture.Replica.TryTerminate(new OrbitPresentationTermination(999u, spawn.Key)), Is.False);
                Assert.That(orb.transform.localPosition, Is.EqualTo(position));
                Assert.That(fixture.Replica.ActiveOrbCount, Is.EqualTo(1));
                Assert.That(typeof(AnimatedAttack).GetField("_endAnimState", PrivateInstance).GetValue(orb), Is.Null);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AlreadyExpiredReliableHide_ReturnsImmediatelyWithoutStaleTracking()
        {
            using (var fixture = new Fixture())
            {
                var spawn = Spawn(911UL);
                AnimatedAttack orb = fixture.Spawn(spawn, 0.1f, newInstance: true);
                Assert.That(fixture.Replica.TryHide(new OrbitPresentationHiding(6u, spawn.Key, 2.05f), HideDuration), Is.True);
                Assert.That(fixture.Replica.ActiveOrbCount, Is.Zero);
                Assert.That(orb.gameObject.activeInHierarchy, Is.False);
                Assert.That(fixture.Replica.TryTerminate(new OrbitPresentationTermination(6u, spawn.Key)), Is.False);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Dispose_IsIdempotentAndDestroysOnlyReplicaEmitters()
        {
            using (var fixture = new Fixture())
            {
                AnimatedAttack first = fixture.Spawn(Spawn(1001UL), 0f, newInstance: true);
                AnimatedAttack second = fixture.Spawn(Spawn(1002UL), 0f, newInstance: true);
                fixture.Replica.Dispose();
                fixture.Replica.Dispose();
                Assert.That(fixture.Replica.ActiveOrbCount, Is.Zero);
                Assert.That(first.gameObject.activeInHierarchy, Is.False);
                Assert.That(second.gameObject.activeInHierarchy, Is.False);
                AssertRootless(first);
                AssertRootless(second);
                Assert.Throws<ObjectDisposedException>(() => fixture.Replica.TrySpawn(Spawn(1003UL), 0f));
                yield return null;
                Assert.That(fixture.Attacks.GetComponentsInChildren<CirclingAttackBehaviour>(true), Is.Empty);
                Assert.That(fixture.Player, Is.Not.Null);
            }
        }

        [UnityTest]
        public IEnumerator DestroyingOnlyNetworkAdapter_DisposesItsOrbitReplicaWhileAvatarSurvives()
        {
            using (var fixture = new Fixture())
            {
                var movement = fixture.Player.GetComponent<PlayerMovement>();
                movement.ConfigureNetworkLifecycle();
                movement.enabled = false;
                var adapter = fixture.Player.AddComponent<NetworkWeaponCombatAdapter>();
                adapter.enabled = false;
                fixture.Player.SetActive(true);
                typeof(NetworkWeaponCombatAdapter).GetField("orbitPresentationReplica", PrivateInstance).SetValue(adapter, fixture.Replica);
                AnimatedAttack orb = fixture.Spawn(Spawn(1101UL), 0.1f, newInstance: true);
                Assert.That(adapter.ReplicaActiveOrbCount, Is.EqualTo(1));
                UnityEngine.Object.Destroy(adapter);
                yield return null;
                Assert.That(fixture.Replica.ActiveOrbCount, Is.Zero);
                Assert.That(orb.gameObject.activeInHierarchy, Is.False);
                Assert.That(fixture.Player, Is.Not.Null);
                Assert.That(fixture.Player.GetComponent<PlayerBuildRuntime>(), Is.Not.Null);
                // Replica emitter destruction is scheduled by the adapter's real OnDestroy.
                yield return null;
                Assert.That(fixture.Attacks.GetComponentsInChildren<CirclingAttackBehaviour>(true), Is.Empty);
            }
        }

        private static OrbitPresentationSpawn Spawn(ulong root, ushort index = 0, int count = 1,
            float duration = 2f, float? phase = null, float radius = 2.2f, float omega = 2f, uint weaponId = 6u) =>
            new OrbitPresentationSpawn(weaponId, new OrbitPresentationKey(root, index), count,
                phase ?? 2f * Mathf.PI * index / count, radius, omega, duration,
                new ProjectilePresentationStats { SizeMultiplierSum = 0.1f, Duration = duration, ProjectileCount = count, BaseProjectileCount = 2 });

        private static void AssertPosition(AnimatedAttack orb, OrbitPresentationSpawn spawn, float elapsed)
        {
            float phase = spawn.InitialPhaseRadians + spawn.AngularSpeedRadians * elapsed;
            Vector3 expected = new Vector3(Mathf.Cos(phase), Mathf.Sin(phase), 0f) * spawn.Radius;
            Assert.That(Vector3.Distance(orb.transform.localPosition, expected), Is.LessThan(0.0001f));
        }

        private static void AssertRootless(AnimatedAttack orb)
        {
            Assert.That(typeof(BasePlayerAttack).GetField("_nativeAttackLease", PrivateInstance).GetValue(orb), Is.Null);
            Assert.That(typeof(BasePlayerAttack).GetProperty("NativeAttackSnapshot", PrivateInstance).GetValue(orb), Is.Null);
            Assert.That(typeof(BasePlayerAttack).GetProperty("IsPresentationOnly", PrivateInstance).GetValue(orb), Is.True);
        }

        private static void AssertStateTime(AnimatedAttack attack, string field, float expected)
        {
            object state = typeof(AnimatedAttack).GetField(field, PrivateInstance).GetValue(attack);
            Assert.That(state, Is.Not.Null, field);
            double time = Convert.ToDouble(state.GetType().GetProperty("Time").GetValue(state));
            Assert.That(time, Is.EqualTo(expected).Within(0.001f), field);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject poolObject;
            private readonly GameObject databaseObject;
            private readonly WeaponDB weaponDatabase;
            public GameObject Player { get; }
            public GameObject Attacks { get; }
            public OrbitPresentationReplica Replica { get; }

            public Fixture()
            {
                poolObject = new GameObject("Orbit Replica Pool");
                poolObject.AddComponent<PoolManager>().Init();
                Player = new GameObject("Orbit Replica Player");
                Player.SetActive(false);
                var movement = Player.AddComponent<PlayerMovement>();
                Attacks = new GameObject("Orbit Replica Attacks");
                movement.AttacksParent = Attacks.transform;
                databaseObject = new GameObject("Orbit Replica Database");
                var database = databaseObject.AddComponent<RuntimeDB>();
#if UNITY_EDITOR
                var weapon = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>(
                    "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Circling/MonoBehaviour/WeaponData_Dante_Circling.asset");
                Assert.That(weapon, Is.Not.Null);
                weaponDatabase = ScriptableObject.CreateInstance<WeaponDB>();
                weaponDatabase.Configure(new[] { weapon });
                database.ConfigureWeaponDatabase(weaponDatabase);
#else
                throw new NotSupportedException("This asset integration test requires the Editor.");
#endif
                Replica = new OrbitPresentationReplica(movement, database);
            }

            public AnimatedAttack Spawn(OrbitPresentationSpawn spawn, float elapsedSeconds, bool newInstance)
            {
                AnimatedAttack[] previous = Attacks.GetComponentsInChildren<AnimatedAttack>();
                // The preserved StudioParameterTrigger looks up this event in Awake only.
                // Pool reuse does not invent an OnEnable audio playback requirement.
                if (newInstance) LogAssert.Expect(LogType.Exception,
                    "EventNotFoundException: [FMOD] Event not found: {840d4d3b-6223-4aab-a508-f0bcd8e4de60} ()");
                Assert.That(Replica.TrySpawn(spawn, elapsedSeconds), Is.True);
                return Attacks.GetComponentsInChildren<AnimatedAttack>().Except(previous).Single();
            }

            public void Dispose()
            {
                Replica?.Dispose();
                UnityEngine.Object.DestroyImmediate(Attacks);
                UnityEngine.Object.DestroyImmediate(Player);
                UnityEngine.Object.DestroyImmediate(databaseObject);
                UnityEngine.Object.DestroyImmediate(weaponDatabase);
                UnityEngine.Object.DestroyImmediate(poolObject);
                PoolManager.Instance = null;
            }
        }

        private sealed class PresentationTarget : MonoBehaviour, IDamageable, INativeGasDamageable
        {
            public int DamageCalls { get; private set; }
            public int NativeHitCalls { get; private set; }
            public bool ResolveNativeGasHit(NativeGasHit hit) { NativeHitCalls++; return true; }
            public int GetID() => GetInstanceID();
            public Vector2 GetPosition() => transform.position;
            public bool IsActive() => true;
            public void Damage(Vector2 position, WeaponBehaviour weapon, DamageType type) => DamageCalls++;
            public void Damage(int value, DamageType type) => DamageCalls++;
        }
    }
}
