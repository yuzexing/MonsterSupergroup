using System;
using System.Collections;
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
    public sealed class MeleePresentationReplicaPlayModeTests
    {
        [UnityTest]
        public IEnumerator RemoteSlash_FollowsPlayerAndCannotDamageOrCreateGasRuntime()
        {
            using (var fixture = new Fixture())
            {
                var targetObject = new GameObject("Melee Replica Safety Target");
                try
                {
                    var collider = targetObject.AddComponent<CircleCollider2D>();
                    collider.radius = 20f;
                    var body = targetObject.AddComponent<Rigidbody2D>();
                    body.gravityScale = 0f;
                    var target = targetObject.AddComponent<PresentationTarget>();
                    MeleePresentationSpawn spawn = Spawn(301UL, 0, 1f);
                    ExpectRecoveredSlashAudio();
                    Assert.That(fixture.Replica.TrySpawn(spawn, 0f), Is.True);
                    Assert.That(fixture.Replica.TrySpawn(spawn, 0f), Is.False);
                    var emitter = fixture.Attacks.GetComponentInChildren<MeleeAttackBehaviour>();
                    var slash = emitter.GetComponentInChildren<AnimatedAttack>();
                    Assert.That(emitter.enabled, Is.False);
                    Assert.That(emitter.NativeRuntime, Is.Null);
                    Assert.That(slash, Is.Not.Null);
                    Assert.That(fixture.Attacks.GetComponentsInChildren<Mirror.NetworkIdentity>(true), Is.Empty);
                    Assert.That(slash.transform.localPosition, Is.EqualTo(spawn.LocalPosition));
                    Vector3 position = slash.transform.position;
                    fixture.Attacks.transform.position += Vector3.right * 4f;
                    Assert.That(slash.transform.position, Is.EqualTo(position + Vector3.right * 4f));

                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    Assert.That(target.DamageCalls, Is.Zero);
                    Assert.That(target.NativeHitCalls, Is.Zero);
                    Assert.That(fixture.Replica.TryTerminate(
                        new MeleePresentationTermination(2u, spawn.Key)), Is.False);
                    Assert.That(fixture.Replica.ActiveSlashCount, Is.EqualTo(1));
                    Assert.That(fixture.Replica.TryTerminate(
                        new MeleePresentationTermination(1u, spawn.Key)), Is.True);
                    Assert.That(fixture.Replica.ActiveSlashCount, Is.Zero);
                    Assert.That(fixture.Replica.TryTerminate(
                        new MeleePresentationTermination(1u, spawn.Key)), Is.False);
                }
                finally { UnityEngine.Object.DestroyImmediate(targetObject); }
            }
        }

        [UnityTest]
        public IEnumerator NaturalExpiryAndLateReplay_LeaveNoActiveEntry()
        {
            using (var fixture = new Fixture())
            {
                Assert.That(fixture.Replica.TrySpawn(Spawn(401UL, 0, -1f), 10f), Is.False);
                Assert.That(fixture.Replica.ActiveSlashCount, Is.Zero);
                ExpectRecoveredSlashAudio();
                Assert.That(fixture.Replica.TrySpawn(Spawn(402UL, 0, -1f), 0f), Is.True);
                float deadline = Time.realtimeSinceStartup + 2f;
                while (fixture.Replica.ActiveSlashCount > 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(fixture.Replica.ActiveSlashCount, Is.Zero,
                    "Authored clip completion must remove the replica's active entry.");
                ExpectRecoveredSlashAudio();
                Assert.That(fixture.Replica.TrySpawn(Spawn(403UL, 0, 0.1f), 0.08f), Is.True);
                deadline = Time.realtimeSinceStartup + 1f;
                while (fixture.Replica.ActiveSlashCount > 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(fixture.Replica.ActiveSlashCount, Is.Zero,
                    "A seeked override must return the pooled slash after its remaining duration.");
            }
        }

        [UnityTest]
        public IEnumerator Dispose_ReturnsAllSlashesAndDestroysOnlyReplicaEmitters()
        {
            using (var fixture = new Fixture())
            {
                for (ushort index = 0; index < 3; index++)
                {
                    ExpectRecoveredSlashAudio();
                    Assert.That(fixture.Replica.TrySpawn(Spawn(501UL, index, 1f), 0f), Is.True);
                }
                Assert.That(fixture.Replica.ActiveSlashCount, Is.EqualTo(3));
                fixture.Replica.Dispose();
                fixture.Replica.Dispose();
                Assert.That(fixture.Replica.ActiveSlashCount, Is.Zero);
                Assert.Throws<ObjectDisposedException>(() =>
                    fixture.Replica.TrySpawn(Spawn(502UL, 0, 1f), 0f));
                yield return null;
                Assert.That(fixture.Attacks.GetComponentsInChildren<MeleeAttackBehaviour>(true), Is.Empty);
                Assert.That(fixture.Player, Is.Not.Null);
            }
        }

        private static void ExpectRecoveredSlashAudio()
        {
            // The source event reference is preserved; its bank is not in this migration.
            // Expect exactly one activation per slash, including a returned pooled instance.
            LogAssert.Expect(LogType.Exception,
                "EventNotFoundException: [FMOD] Event not found: {d34df8fa-1ca2-43a3-b073-e782b614760a} ()");
        }

        private static MeleePresentationSpawn Spawn(ulong root, ushort index, float duration) =>
            new MeleePresentationSpawn(1u, new MeleePresentationKey(root, index),
                new Vector3(1f, 0.5f, 0f), Vector2.right, duration,
                new ProjectilePresentationStats
                {
                    Duration = 1f,
                    ProjectileCount = 3,
                    BaseProjectileCount = 1
                });

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject poolObject;
            private readonly GameObject databaseObject;
            private readonly WeaponDB weaponDatabase;
            public GameObject Player { get; }
            public GameObject Attacks { get; }
            public MeleePresentationReplica Replica { get; }

            public Fixture()
            {
                poolObject = new GameObject("Melee Replica Pool");
                poolObject.AddComponent<PoolManager>().Init();
                Player = new GameObject("Melee Replica Player");
                Player.SetActive(false);
                var movement = Player.AddComponent<PlayerMovement>();
                // An active presentation parent with an inactive input/movement fixture
                // reproduces remote visuals without initializing unrelated player input.
                Attacks = new GameObject("Melee Replica Attacks");
                movement.AttacksParent = Attacks.transform;
                databaseObject = new GameObject("Melee Replica Database");
                var database = databaseObject.AddComponent<RuntimeDB>();
#if UNITY_EDITOR
                var weapon = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>(
                    "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Melee/MonoBehaviour/WeaponData_Dante_Melee.asset");
                Assert.That(weapon, Is.Not.Null);
                weaponDatabase = ScriptableObject.CreateInstance<WeaponDB>();
                weaponDatabase.Configure(new[] { weapon });
                database.ConfigureWeaponDatabase(weaponDatabase);
#else
                throw new NotSupportedException("This asset integration test requires the Editor.");
#endif
                Replica = new MeleePresentationReplica(movement, database);
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
            public bool ResolveNativeGasHit(NativeGasHit hit)
            {
                NativeHitCalls++;
                return true;
            }
            public int GetID() => GetInstanceID();
            public Vector2 GetPosition() => transform.position;
            public bool IsActive() => true;
            public void Damage(Vector2 position, WeaponBehaviour weapon, DamageType type) => DamageCalls++;
            public void Damage(int value, DamageType type) => DamageCalls++;
        }
    }
}
