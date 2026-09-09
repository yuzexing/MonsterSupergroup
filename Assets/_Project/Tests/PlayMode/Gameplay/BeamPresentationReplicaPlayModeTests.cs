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
    public sealed class BeamPresentationReplicaPlayModeTests
    {
        private const float StartDuration = 0.36666667f;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator RemoteFireAndPoison_FollowAvatarWithoutNativeRuntimeSnapshotOrDamage()
        {
            using (var fixture = new Fixture())
            {
                var targetObject = new GameObject("Beam Replica Safety Target");
                try
                {
                    var collider = targetObject.AddComponent<CircleCollider2D>();
                    collider.radius = 20f;
                    targetObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
                    var target = targetObject.AddComponent<PresentationTarget>();
                    foreach (AttackElement element in new[] { AttackElement.Default, AttackElement.Poison })
                    {
                        var spawn = Spawn(element == AttackElement.Default ? 301UL : 302UL, element: element);
                        AnimatedAttack beam = fixture.Spawn(spawn, StartDuration + 0.2f, newInstance: true);
                        Assert.That(beam.name, Does.Contain(element == AttackElement.Poison ? "Poison" : "Fire"));
                        var emitter = beam.GetComponentInParent<PlayerBeamAttackBehaviour>();
                        Assert.That(emitter.enabled, Is.False);
                        Assert.That(emitter.NativeRuntime, Is.Null);
                        Assert.That(typeof(BasePlayerAttack).GetField("_nativeAttackLease", PrivateInstance).GetValue(beam), Is.Null);
                        Assert.That(typeof(BasePlayerAttack).GetProperty("IsPresentationOnly", PrivateInstance).GetValue(beam), Is.True);
                        Assert.That(typeof(BaseAttackHitBox).GetField("_onHit", PrivateInstance).GetValue(beam.hitbox), Is.Null);
                        Assert.That(beam.hitbox.enabled, Is.False);
                        Assert.That(fixture.Attacks.GetComponentsInChildren<Mirror.NetworkIdentity>(true), Is.Empty);
                        Vector3 position = beam.transform.position;
                        fixture.Attacks.transform.position += Vector3.right * 4f;
                        Assert.That(beam.transform.position, Is.EqualTo(position + Vector3.right * 4f));
                        // Even an accidentally delivered local hit callback cannot execute GAS on a replica.
                        typeof(BasePlayerAttack).GetMethod("OnHit", PrivateInstance).Invoke(beam, new object[] { target });
                        yield return new WaitForFixedUpdate();
                        yield return new WaitForFixedUpdate();
                        Assert.That(target.DamageCalls, Is.Zero);
                        Assert.That(target.NativeHitCalls, Is.Zero);
                        Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(3u, spawn.Key)), Is.True);
                    }
                    Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero);
                }
                finally { UnityEngine.Object.DestroyImmediate(targetObject); }
            }
        }

        [UnityTest]
        public IEnumerator AgedReplay_SeeksStartMainAndEndPhasesAndRejectsExpiredVisuals()
        {
            using (var fixture = new Fixture())
            {
                Assert.That(fixture.Replica.TrySpawn(Spawn(400UL), StartDuration + 3f), Is.False);
                Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero);
                AnimatedAttack start = fixture.Spawn(Spawn(401UL), 0.2f, newInstance: true);
                AssertStateTime(start, "_startAnimState", 0.2f);
                AnimatedAttack main = fixture.Spawn(Spawn(402UL), StartDuration + 0.5f, newInstance: true);
                AssertStateTime(main, "_mainAnimState", 0.5f);
                AnimatedAttack end = fixture.Spawn(Spawn(403UL), StartDuration + 2f + 0.2f, newInstance: true);
                AssertStateTime(end, "_endAnimState", 0.2f);
                Assert.That(fixture.Replica.ActiveBeamCount, Is.EqualTo(3));
                fixture.Replica.Dispose();
                yield return null;
                Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator AimIsScopedToAttackRootAndRetainsBeamOrdinalAfterSiblingCancellation()
        {
            using (var fixture = new Fixture())
            {
                var first = Spawn(501UL, 0, count: 2);
                var sibling = Spawn(501UL, 1, count: 2);
                var other = Spawn(502UL, direction: Vector2.left);
                AnimatedAttack firstBeam = fixture.Spawn(first, 0f, newInstance: true);
                AnimatedAttack siblingBeam = fixture.Spawn(sibling, 0f, newInstance: true);
                AnimatedAttack otherBeam = fixture.Spawn(other, 0f, newInstance: true);
                Vector3 otherPosition = otherBeam.transform.localPosition;
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(3u, 999UL, Vector2.up)), Is.False);
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(2u, 501UL, Vector2.up)), Is.False);
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(3u, 501UL, Vector2.up)), Is.True);
                fixture.Replica.Tick(1f);
                Assert.That(Vector3.Distance(firstBeam.transform.localPosition, Vector3.up * 1.5f), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(siblingBeam.transform.localPosition, Vector3.down * 1.5f), Is.LessThan(0.001f));
                Assert.That(otherBeam.transform.localPosition, Is.EqualTo(otherPosition));
                Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(3u, first.Key)), Is.True);
                fixture.Replica.Tick(1f);
                Assert.That(Vector3.Distance(siblingBeam.transform.localPosition, Vector3.down * 1.5f), Is.LessThan(0.001f),
                    "A surviving beam keeps its original angular slot, rather than being renumbered after another beam ends.");
                Assert.That(fixture.Replica.ActiveBeamCount, Is.EqualTo(2));
                Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(3u, sibling.Key)), Is.True);
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(3u, 501UL, Vector2.right)), Is.False);
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(3u, 502UL, Vector2.down)), Is.True);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator NaturalEnd_ReturnsAgedMainAndEndPhaseInstancesWithoutStaleEntries()
        {
            using (var fixture = new Fixture())
            {
                var endSpawn = Spawn(601UL);
                fixture.Spawn(endSpawn, StartDuration + 2f + 0.85f, newInstance: true);
                float deadline = Time.realtimeSinceStartup + 2f;
                while (fixture.Replica.ActiveBeamCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero, "Late end-phase replay must return at the clip's remaining time.");
                Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(3u, endSpawn.Key)), Is.False);

                var mainSpawn = Spawn(602UL, duration: 0.15f);
                fixture.Spawn(mainSpawn, StartDuration + 0.1f, newInstance: false);
                deadline = Time.realtimeSinceStartup + 2.5f;
                while (fixture.Replica.ActiveBeamCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero, "Aged main phase must wait only its remaining timeout, then play Out.");
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(3u, mainSpawn.Key.AttackEventId, Vector2.up)), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator UpgradedDurationBeyondSourceClip_HoldsMainThenPlaysOutAndReturns()
        {
            using (var fixture = new Fixture())
            {
                var spawn = Spawn(651UL, duration: 5f);
                AnimatedAttack beam = fixture.Spawn(spawn, StartDuration + 4.2f, newInstance: true);
                AssertStateTime(beam, "_mainAnimState", 4.2f);
                Assert.That(typeof(AnimatedAttack).GetField("_endAnimState", PrivateInstance).GetValue(beam), Is.Null);
                Assert.That(beam.GetComponentInParent<PlayerBeamAttackBehaviour>().NativeRuntime, Is.Null);
                Assert.That(typeof(BasePlayerAttack).GetField("_nativeAttackLease", PrivateInstance).GetValue(beam), Is.Null);

                // The actual imported Idle clip is only four seconds and does not loop. Crossing
                // that clip end must not override the five-second gameplay presentation duration.
                yield return new WaitForSeconds(0.2f);
                Assert.That(fixture.Replica.ActiveBeamCount, Is.EqualTo(1));
                Assert.That(typeof(AnimatedAttack).GetField("_endAnimState", PrivateInstance).GetValue(beam), Is.Null,
                    "The four-second Idle clip must hold its last frame until the upgraded Duration expires.");

                float deadline = Time.realtimeSinceStartup + 2f;
                while (typeof(AnimatedAttack).GetField("_endAnimState", PrivateInstance).GetValue(beam) == null &&
                       fixture.Replica.ActiveBeamCount > 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(fixture.Replica.ActiveBeamCount, Is.EqualTo(1));
                Assert.That(typeof(AnimatedAttack).GetField("_endAnimState", PrivateInstance).GetValue(beam), Is.Not.Null,
                    "Only the remaining 0.8 seconds of Main should precede the authored Out phase.");
                Assert.That(typeof(BasePlayerAttack).GetField("_nativeAttackLease", PrivateInstance).GetValue(beam), Is.Null);

                deadline = Time.realtimeSinceStartup + 2f;
                while (fixture.Replica.ActiveBeamCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero,
                    "The upgraded beam must finish its one-second Out phase rather than retain a stalled entry.");
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(3u, spawn.Key.AttackEventId, Vector2.up)), Is.False);
                Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(3u, spawn.Key)), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator ExternalBeamAndAvatarDeactivation_ClearReplicaImmediatelyWithoutWaitingForTick()
        {
            using (var fixture = new Fixture())
            {
                var first = Spawn(661UL);
                AnimatedAttack firstBeam = fixture.Spawn(first, StartDuration + 0.2f, newInstance: true);
                firstBeam.gameObject.SetActive(false);
                Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero,
                    "External deactivation must report the return even when the next network tick never arrives.");
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(3u, first.Key.AttackEventId, Vector2.up)), Is.False);
                Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(3u, first.Key)), Is.False);

                // The externally disabled object is discarded, so the next checkout must initialize
                // a fresh prefab rather than reuse an object while its old OnDisable is still active.
                var second = Spawn(662UL);
                AnimatedAttack secondBeam = fixture.Spawn(second, StartDuration + 0.2f, newInstance: true);
                Assert.That(secondBeam, Is.Not.SameAs(firstBeam));
                fixture.Attacks.SetActive(false);
                Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero,
                    "Avatar presentation deactivation must synchronously clear every remaining beam.");
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(3u, second.Key.AttackEventId, Vector2.up)), Is.False);
                Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(3u, second.Key)), Is.False);
                yield return null;
                Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator CancelAndPoolReuse_RejectOldKeysWithoutInterruptingTheNewBeam()
        {
            using (var fixture = new Fixture())
            {
                var oldSpawn = Spawn(701UL, duration: 0.1f);
                AnimatedAttack oldBeam = fixture.Spawn(oldSpawn, StartDuration + 0.05f, newInstance: true);
                Assert.That(fixture.Replica.TrySpawn(oldSpawn, 0f), Is.False);
                Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(2u, oldSpawn.Key)), Is.False);
                Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(3u, oldSpawn.Key)), Is.True);
                var currentSpawn = Spawn(702UL, duration: 3f);
                AnimatedAttack currentBeam = fixture.Spawn(currentSpawn, StartDuration + 0.1f, newInstance: false);
                Assert.That(currentBeam, Is.SameAs(oldBeam), "Exercise the actual shared pool, including its old timeout cancellation.");
                Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(3u, oldSpawn.Key)), Is.False);
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(3u, oldSpawn.Key.AttackEventId, Vector2.down)), Is.False);
                yield return new WaitForSeconds(0.2f);
                Assert.That(fixture.Replica.ActiveBeamCount, Is.EqualTo(1));
                Assert.That(currentBeam.gameObject.activeInHierarchy, Is.True);
                Assert.That(fixture.Replica.TryAim(new BeamPresentationAim(3u, currentSpawn.Key.AttackEventId, Vector2.up)), Is.True);
                Assert.That(fixture.Replica.TryTerminate(new BeamPresentationTermination(3u, currentSpawn.Key)), Is.True);
                Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator Dispose_ReturnsEveryVariantAndDestroysOnlyReplicaEmitters()
        {
            using (var fixture = new Fixture())
            {
                AnimatedAttack fire = fixture.Spawn(Spawn(801UL), 0f, newInstance: true);
                AnimatedAttack poison = fixture.Spawn(Spawn(802UL, element: AttackElement.Poison), 0f, newInstance: true);
                fixture.Replica.Dispose();
                fixture.Replica.Dispose();
                Assert.That(fixture.Replica.ActiveBeamCount, Is.Zero);
                Assert.That(fire.gameObject.activeInHierarchy, Is.False);
                Assert.That(poison.gameObject.activeInHierarchy, Is.False);
                Assert.That(typeof(BasePlayerAttack).GetField("_nativeAttackLease", PrivateInstance).GetValue(fire), Is.Null);
                Assert.That(typeof(BasePlayerAttack).GetField("_nativeAttackLease", PrivateInstance).GetValue(poison), Is.Null);
                Assert.Throws<ObjectDisposedException>(() => fixture.Replica.TrySpawn(Spawn(803UL), 0f));
                yield return null;
                Assert.That(fixture.Attacks.GetComponentsInChildren<PlayerBeamAttackBehaviour>(true), Is.Empty);
                Assert.That(fixture.Player, Is.Not.Null);
            }
        }

        private static BeamPresentationSpawn Spawn(ulong root, ushort index = 0, float duration = 2f,
            int count = 1, AttackElement element = AttackElement.Default, Vector2? direction = null) =>
            new BeamPresentationSpawn(3u, new BeamPresentationKey(root, index), count,
                direction ?? Vector2.right, element, duration,
                new ProjectilePresentationStats { Duration = duration, ProjectileCount = count, BaseProjectileCount = 1 });

        private static void AssertStateTime(AnimatedAttack attack, string field, float expected)
        {
            object state = typeof(AnimatedAttack).GetField(field, PrivateInstance).GetValue(attack);
            Assert.That(state, Is.Not.Null, field);
            double time = Convert.ToDouble(state.GetType().GetProperty("Time").GetValue(state));
            Assert.That(time, Is.EqualTo(expected).Within(0.001f), field);
        }

        private static void ExpectSourceBeamAudio(bool newInstance)
        {
            const string message = "EventNotFoundException: [FMOD] Event not found: {b865f76c-9c2a-4679-9709-a288f22c9619} ()";
            // StudioParameterTrigger.Awake looks up this exact source event on a new instance.
            if (newInstance) LogAssert.Expect(LogType.Exception, message);
            // StudioEventEmitter also looks up the same event each time its Start child enables.
            LogAssert.Expect(LogType.Exception, message);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject poolObject;
            private readonly GameObject databaseObject;
            private readonly WeaponDB weaponDatabase;
            public GameObject Player { get; }
            public GameObject Attacks { get; }
            public BeamPresentationReplica Replica { get; }

            public Fixture()
            {
                poolObject = new GameObject("Beam Replica Pool");
                poolObject.AddComponent<PoolManager>().Init();
                Player = new GameObject("Beam Replica Player");
                Player.SetActive(false);
                var movement = Player.AddComponent<PlayerMovement>();
                Attacks = new GameObject("Beam Replica Attacks");
                movement.AttacksParent = Attacks.transform;
                databaseObject = new GameObject("Beam Replica Database");
                var database = databaseObject.AddComponent<RuntimeDB>();
#if UNITY_EDITOR
                var weapon = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>(
                    "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Beam/MonoBehaviour/WeaponData_Dante_DragonsBreath.asset");
                Assert.That(weapon, Is.Not.Null);
                weaponDatabase = ScriptableObject.CreateInstance<WeaponDB>();
                weaponDatabase.Configure(new[] { weapon });
                database.ConfigureWeaponDatabase(weaponDatabase);
#else
                throw new NotSupportedException("This asset integration test requires the Editor.");
#endif
                Replica = new BeamPresentationReplica(movement, database);
            }

            public AnimatedAttack Spawn(BeamPresentationSpawn spawn, float elapsedSeconds, bool newInstance)
            {
                AnimatedAttack[] previous = Attacks.GetComponentsInChildren<AnimatedAttack>();
                ExpectSourceBeamAudio(newInstance);
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
