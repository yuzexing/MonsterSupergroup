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
    public sealed class TrailPresentationReplicaPlayModeTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator FireAndPoisonWorldPointsDoNotDriftWithTheAvatarAndNeverExecuteGas()
        {
            using (var fixture = new Fixture())
            {
                var targetObject = new GameObject("Trail Replica Safety Target");
                try
                {
                    targetObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
                    targetObject.AddComponent<CircleCollider2D>().radius = 20f;
                    var target = targetObject.AddComponent<PresentationTarget>();
                    foreach (AttackElement element in new[] { AttackElement.Default, AttackElement.Poison })
                    {
                        var spawn = Spawn(element == AttackElement.Default ? 801UL : 802UL, element);
                        MultiParticlePlayerTrailAttack trail = fixture.Spawn(spawn, 0.3f);
                        Assert.That(trail.name, Does.Contain(element == AttackElement.Poison ? "Poison" : "FireTrailAttack"));
                        Assert.That(fixture.Point(spawn, 0, new Vector2(1f, 2f), 0.1f, 0.2f), Is.True);
                        Assert.That(fixture.Point(spawn, 1, new Vector2(2f, 2.5f), 0.2f, 0.1f), Is.True);
                        ParticleSystem[] particles = Segments(trail);
                        Vector3[] positions = particles.Select(value => value.transform.position).ToArray();
                        Assert.That(positions, Is.EqualTo(new[] { new Vector3(1f, 2f, 0f), new Vector3(2f, 2.5f, 0f) }));
                        Assert.That(trail.transform.parent, Is.Null, "The trail root must stay outside the avatar's Rigidbody hierarchy.");
                        AssertPresentationOnly(fixture, trail);

                        fixture.Player.transform.position = new Vector3(40f, -20f, 0f);
                        fixture.Attacks.transform.position = new Vector3(-12f, 8f, 0f);
                        fixture.Attacks.transform.rotation = Quaternion.Euler(0f, 0f, 70f);
                        yield return new WaitForFixedUpdate();
                        yield return null;
                        Assert.That(trail.transform.position, Is.EqualTo((Vector3)spawn.Origin));
                        Assert.That(particles.Select(value => value.transform.position), Is.EqualTo(positions));
                        ParticleSystem step = (ParticleSystem)typeof(MultiParticlePlayerTrailAttack)
                            .GetField("trailStepParticles", PrivateInstance).GetValue(trail);
                        Assert.That(step.transform.position, Is.EqualTo(fixture.Player.transform.position),
                            "Only the authored moving step effect follows the owner.");
                        Assert.That(trail.ParticleInstanceCount, Is.EqualTo(2), "Remote avatar motion must not generate new trail samples.");
                        typeof(BasePlayerAttack).GetMethod("OnHit", PrivateInstance).Invoke(trail, new object[] { target });
                        yield return new WaitForFixedUpdate();
                        Assert.That(target.DamageCalls, Is.Zero);
                        Assert.That(target.NativeHitCalls, Is.Zero);
                        Assert.That(fixture.Replica.TryTerminate(new TrailPresentationTermination(8u, spawn.AttackEventId)), Is.True);
                        Assert.That(particles.All(value => !value.gameObject.activeInHierarchy && !value.IsAlive(true)), Is.True);
                    }
                    Assert.That(fixture.Replica.ActiveTrailCount, Is.Zero);
                }
                finally { UnityEngine.Object.DestroyImmediate(targetObject); }
            }
        }

        [UnityTest]
        public IEnumerator AgedSpawnRetiresOldSegmentsButStillAcceptsLaterReliablePoints()
        {
            using (var fixture = new Fixture())
            {
                var spawn = Spawn(811UL);
                MultiParticlePlayerTrailAttack trail = fixture.Spawn(spawn, 1.45f);
                Assert.That(trail.IsSampling, Is.True);
                yield return null;
                Assert.That(fixture.Replica.ActiveTrailCount, Is.EqualTo(1),
                    "Spawn can arrive after its source sampling duration, before points from that same reliable history.");
                Assert.That(fixture.Point(spawn, 0, Vector2.left, 0.05f, 1.4f), Is.True);
                ParticleSystem old = Segments(trail).Single();
                yield return null;
                Assert.That(trail.ActiveSegmentCount, Is.Zero);
                Assert.That(old.IsAlive(true), Is.False, "The source's 0.625-second emission and 0.6-second tail have both elapsed.");
                Assert.That(fixture.Replica.ActiveTrailCount, Is.EqualTo(1), "An empty aged trail still awaits the reliable end edge.");

                Assert.That(fixture.Point(spawn, 1, Vector2.right * 2f, 1.2f, 0.25f), Is.True,
                    "A later point must not disappear because a previous point was already too old to render.");
                ParticleSystem current = Segments(trail).Last();
                Assert.That(current.transform.position, Is.EqualTo(Vector3.right * 2f));
                Assert.That(current.IsAlive(true), Is.True);
                Assert.That(trail.ActiveSegmentCount, Is.EqualTo(1));
                AssertPresentationOnly(fixture, trail);
                Assert.That(fixture.Replica.TryEndSampling(new TrailPresentationSamplingEnded(8u, spawn.AttackEventId, 1.25f), 0.2f), Is.True);
                yield return AwaitRetirement(fixture, 3f);
                Assert.That(current.IsAlive(true), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator SamplingEndPreservesParticleTailsThenReturnsTheRoot()
        {
            using (var fixture = new Fixture())
            {
                var spawn = Spawn(821UL, AttackElement.Poison);
                MultiParticlePlayerTrailAttack trail = fixture.Spawn(spawn, 1.25f);
                Assert.That(fixture.Point(spawn, 0, new Vector2(3f, 1f), 1.2f, 0.05f), Is.True);
                ParticleSystem particles = Segments(trail).Single();
                var end = new TrailPresentationSamplingEnded(8u, spawn.AttackEventId, 1.25f);
                Assert.That(fixture.Replica.TryEndSampling(end, 0f), Is.True);
                Assert.That(fixture.Replica.TryEndSampling(end, 0f), Is.False);
                Assert.That(trail.IsSampling, Is.False);
                Assert.That(fixture.Point(spawn, 1, Vector2.right * 4f, 1.24f, 0f), Is.False);
                Assert.That(fixture.Replica.ActiveTrailCount, Is.EqualTo(1));
                yield return new WaitForSeconds(0.7f);
                Assert.That(fixture.Replica.ActiveTrailCount, Is.EqualTo(1), "The latest source particles should outlive segment emission.");
                Assert.That(particles.IsAlive(true), Is.True);
                Assert.That(trail.ActiveSegmentCount, Is.Zero);
                yield return AwaitRetirement(fixture, 2f);
                Assert.That(trail.ParticleInstanceCount, Is.Zero);
                Assert.That(particles.gameObject.activeInHierarchy, Is.False);
                Assert.That(fixture.Replica.TryTerminate(new TrailPresentationTermination(8u, spawn.AttackEventId)), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator CancelIsScopedToItsRootAndPoolReuseRejectsOldMessages()
        {
            using (var fixture = new Fixture())
            {
                var first = Spawn(831UL);
                var other = Spawn(832UL, AttackElement.Poison);
                MultiParticlePlayerTrailAttack original = fixture.Spawn(first, 0f);
                MultiParticlePlayerTrailAttack sibling = fixture.Spawn(other, 0f);
                Assert.That(fixture.Point(first, 0, Vector2.left, 0.1f, 0.1f), Is.True);
                Assert.That(fixture.Point(other, 0, Vector2.right, 0.1f, 0.1f), Is.True);
                ParticleSystem oldParticle = Segments(original).Single();
                Assert.That(fixture.Replica.TryTerminate(new TrailPresentationTermination(3u, first.AttackEventId)), Is.False);
                Assert.That(fixture.Replica.TryTerminate(new TrailPresentationTermination(8u, first.AttackEventId)), Is.True);
                Assert.That(fixture.Replica.ActiveTrailCount, Is.EqualTo(1));
                Assert.That(sibling.gameObject.activeInHierarchy, Is.True);
                Assert.That(oldParticle.gameObject.activeInHierarchy, Is.False);
                Assert.That(oldParticle.IsAlive(true), Is.False);

                var next = Spawn(833UL);
                MultiParticlePlayerTrailAttack reused = fixture.Spawn(next, 0f);
                Assert.That(reused, Is.SameAs(original));
                Assert.That(reused.ParticleInstanceCount, Is.Zero);
                Assert.That(fixture.Point(first, 1, Vector2.one, 0.2f, 0f), Is.False);
                Assert.That(fixture.Replica.TryEndSampling(new TrailPresentationSamplingEnded(8u, first.AttackEventId, 1.25f), 0f), Is.False);
                Assert.That(fixture.Replica.TryTerminate(new TrailPresentationTermination(8u, first.AttackEventId)), Is.False);
                Assert.That(fixture.Point(next, 0, Vector2.up * 2f, 0.1f, 0.1f), Is.True);
                Assert.That(reused.ParticleInstanceCount, Is.EqualTo(1));
                AssertPresentationOnly(fixture, reused);
                yield return null;
                Assert.That(fixture.Replica.ActiveTrailCount, Is.EqualTo(2));
            }
        }

        [UnityTest]
        public IEnumerator ExternalDeactivationAndDisposeImmediatelyClearDetachedWorldParticles()
        {
            using (var fixture = new Fixture())
            {
                var first = Spawn(841UL);
                MultiParticlePlayerTrailAttack trail = fixture.Spawn(first, 0f);
                Assert.That(fixture.Point(first, 0, Vector2.one, 0.1f, 0.1f), Is.True);
                ParticleSystem[] segments = Segments(trail);
                trail.gameObject.SetActive(false);
                Assert.That(fixture.Replica.ActiveTrailCount, Is.Zero);
                Assert.That(segments.All(value => !value.gameObject.activeInHierarchy && !value.IsAlive(true)), Is.True);

                var second = Spawn(842UL, AttackElement.Poison);
                MultiParticlePlayerTrailAttack another = fixture.Spawn(second, 0f);
                Assert.That(fixture.Point(second, 0, Vector2.up, 0.1f, 0.1f), Is.True);
                segments = Segments(another);
                fixture.Attacks.SetActive(false);
                Assert.That(fixture.Replica.ActiveTrailCount, Is.Zero,
                    "Deactivating the owning presentation must clean detached trails without needing another Update.");
                Assert.That(segments.All(value => !value.gameObject.activeInHierarchy && !value.IsAlive(true)), Is.True);
                fixture.Replica.Dispose();
                fixture.Replica.Dispose();
                yield return null;
                Assert.That(fixture.Replica.ActiveTrailCount, Is.Zero);
                Assert.That(fixture.Attacks.GetComponentsInChildren<DashAttackBehaviour>(true), Is.Empty);
                Assert.That(fixture.Player, Is.Not.Null);
                Assert.Throws<ObjectDisposedException>(() => fixture.Replica.TrySpawn(Spawn(843UL), 0f));
            }
        }

        [UnityTest]
        public IEnumerator InvalidOrRepeatedPointsLeaveTheCurrentTraceUnchanged()
        {
            using (var fixture = new Fixture())
            {
                var spawn = Spawn(851UL);
                Assert.That(fixture.Replica.TrySpawn(spawn, float.NaN), Is.False);
                MultiParticlePlayerTrailAttack trail = fixture.Spawn(spawn, 0f);
                Assert.That(fixture.Replica.TrySpawn(spawn, 0f), Is.False);
                Assert.That(fixture.Point(spawn, 1, Vector2.one, 0.1f, 0f), Is.False);
                Assert.That(fixture.Replica.TryPoint(new TrailPresentationPoint(3u, spawn.AttackEventId, 0, Vector2.one, 0.1f), 0f), Is.False);
                Assert.That(fixture.Point(spawn, 0, new Vector2(float.NaN, 0f), 0.1f, 0f), Is.False);
                Assert.That(fixture.Point(spawn, 0, Vector2.one, 1.25f, 0f), Is.False);
                Assert.That(trail.ParticleInstanceCount, Is.Zero);
                Assert.That(fixture.Point(spawn, 0, Vector2.one, 0.2f, 0.1f), Is.True);
                Assert.That(fixture.Point(spawn, 0, Vector2.right * 7f, 0.2f, 0f), Is.False);
                Assert.That(fixture.Point(spawn, 1, Vector2.right * 7f, 0.1f, 0f), Is.False);
                Assert.That(fixture.Replica.TryEndSampling(new TrailPresentationSamplingEnded(8u, spawn.AttackEventId, 0.5f), 0f), Is.False);
                Assert.That(trail.ParticleInstanceCount, Is.EqualTo(1));
                Assert.That(Segments(trail).Single().transform.position, Is.EqualTo((Vector3)Vector2.one));
                yield return null;
                Assert.That(fixture.Replica.ActiveTrailCount, Is.EqualTo(1));
            }
        }

        private static IEnumerator AwaitRetirement(Fixture fixture, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (fixture.Replica.ActiveTrailCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(fixture.Replica.ActiveTrailCount, Is.Zero, "The original particle tails must drain and return the trail.");
        }

        private static void AssertPresentationOnly(Fixture fixture, MultiParticlePlayerTrailAttack trail)
        {
            Assert.That(fixture.Attacks.GetComponentsInChildren<DashAttackBehaviour>(true).All(value => value.NativeRuntime == null && !value.enabled), Is.True);
            Assert.That(typeof(BasePlayerAttack).GetField("_nativeAttackLease", PrivateInstance).GetValue(trail), Is.Null);
            Assert.That(typeof(BasePlayerAttack).GetProperty("IsPresentationOnly", PrivateInstance).GetValue(trail), Is.True);
            Assert.That(typeof(BaseAttackHitBox).GetField("_onHit", PrivateInstance).GetValue(trail.hitbox), Is.Null);
            Assert.That(trail.hitbox.enabled, Is.False);
            Assert.That(trail.GetComponentsInChildren<Collider2D>(true).All(value => !value.enabled), Is.True);
            Assert.That(trail.GetComponentsInChildren<Mirror.NetworkIdentity>(true), Is.Empty);
        }

        private static ParticleSystem[] Segments(MultiParticlePlayerTrailAttack trail)
        {
            var records = (IList)typeof(MultiParticlePlayerTrailAttack).GetField("_particles", PrivateInstance).GetValue(trail);
            return records.Cast<object>().Select(record => (ParticleSystem)record.GetType().GetField("Particle").GetValue(record)).ToArray();
        }

        private static TrailPresentationSpawn Spawn(ulong eventId, AttackElement element = AttackElement.Default) =>
            new TrailPresentationSpawn(8u, eventId, eventId + 1000UL, element, new Vector2(-1f, 0f), 1.25f, 0.625f, 1f,
                new ProjectilePresentationStats { Duration = 1.25f, ProjectileCount = 1, BaseProjectileCount = 1 });

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject poolObject;
            private readonly GameObject databaseObject;
            private readonly WeaponDB weaponDatabase;
            public GameObject Player { get; }
            public GameObject Attacks { get; }
            public TrailPresentationReplica Replica { get; }

            public Fixture()
            {
                poolObject = new GameObject("Trail Replica Pool");
                poolObject.AddComponent<PoolManager>().Init();
                Player = new GameObject("Trail Replica Player");
                Player.SetActive(false);
                var movement = Player.AddComponent<PlayerMovement>();
                Attacks = new GameObject("Trail Replica Attacks");
                movement.AttacksParent = Attacks.transform;
                databaseObject = new GameObject("Trail Replica Database");
                var database = databaseObject.AddComponent<RuntimeDB>();
#if UNITY_EDITOR
                WeaponData weapon = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>(
                    "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Dash/MonoBehaviour/WeaponData_Dante_FireTrail.asset");
                Assert.That(weapon, Is.Not.Null, "Run the focused Dante Dash importer before the real-resource tests.");
                weaponDatabase = ScriptableObject.CreateInstance<WeaponDB>();
                weaponDatabase.Configure(new[] { weapon });
                database.ConfigureWeaponDatabase(weaponDatabase);
#else
                throw new NotSupportedException("This real-asset integration test requires the Editor.");
#endif
                Replica = new TrailPresentationReplica(movement, database);
            }

            public MultiParticlePlayerTrailAttack Spawn(TrailPresentationSpawn spawn, float age)
            {
                // Exactly one playback attempt per root, including pooled reuse. Keep the source
                // event reference and assert only its known absent bank; no blanket log suppression.
                LogAssert.Expect(LogType.Exception,
                    "EventNotFoundException: [FMOD] Event not found: {0ddbe74c-0c1e-4afc-a293-557438dbd8e0} ()");
                Assert.That(Replica.TrySpawn(spawn, age), Is.True);
                return UnityEngine.Object.FindObjectsByType<MultiParticlePlayerTrailAttack>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Single(value => value.PresentationSpawn.AttackEventId == spawn.AttackEventId);
            }

            public bool Point(TrailPresentationSpawn spawn, uint index, Vector2 position, float elapsed, float age) =>
                Replica.TryPoint(new TrailPresentationPoint(8u, spawn.AttackEventId, index, position, elapsed), age);

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
