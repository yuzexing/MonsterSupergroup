using System;
using System.Collections;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GasDamageInfo = MonsterSupergroup.GAS.DamageInfo;
using LegacyDamageType = AstralShift.HellMaiden.Player.Attacks.DamageType;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class DanteNativeGasRuntimeTests
    {
        private const string WeaponPath =
            "Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset";
        private const string DamageEquipmentPath =
            "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/" +
            "NativeGasEquipment_Damage.asset";
        private const string WeaponDatabasePath =
            "Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasWeaponDB.asset";

        [Test]
        public void RuntimeDatabase_StartsAndClearsTheOwningPlayersInitialBuild()
        {
            GameObject poolObject = null;
            GameObject playerObject = null;
            GameObject databaseObject = null;
            try
            {
                poolObject = new GameObject("Test Pool Manager");
                PoolManager pool = poolObject.AddComponent<PoolManager>();
                pool.Init();

                PlayerBuildRuntime build = CreateInactivePlayer(
                    "Configured Player",
                    out playerObject);
                WeaponDB weaponDatabase = ResourcesLoadAsset<WeaponDB>(
                    WeaponDatabasePath);
                NativeGasEquipmentDefinition damageEquipment =
                    ResourcesLoadAsset<NativeGasEquipmentDefinition>(
                        DamageEquipmentPath);
                databaseObject = new GameObject("Runtime DB");
                RuntimeDB runtimeDatabase = databaseObject.AddComponent<RuntimeDB>();
                runtimeDatabase.ConfigureWeaponDatabase(weaponDatabase);

                WeaponBehaviour weapon = build.StartInitialBuild(runtimeDatabase);

                Assert.That(build.IsBuildActive, Is.True);
                Assert.That(build.WeaponCount, Is.EqualTo(1));
                Assert.That(build.BuildDatabase, Is.SameAs(runtimeDatabase));
                Assert.That(build.InitialWeapon, Is.SameAs(weapon));
                Assert.That(weapon.ID, Is.EqualTo(2u));
                Assert.That(weapon.UsesNativeGasRuntime, Is.True);
                PlayerBuildEquipmentHandle damageHandle =
                    build.AddEquipment(weapon, damageEquipment, 0);
                Assert.That(weapon.DamageValue, Is.EqualTo(20));
                Assert.That(build.RemoveEquipment(damageHandle), Is.True);
                Assert.That(weapon.DamageValue, Is.EqualTo(15));

                build.ClearBuild();

                Assert.That(build.IsBuildActive, Is.False);
                Assert.That(build.WeaponCount, Is.Zero);
                Assert.That(build.EquipmentCount, Is.Zero);
                Assert.That(build.PerkCount, Is.Zero);
                Assert.That(build.BuildDatabase, Is.Null);
            }
            finally
            {
                if (databaseObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(databaseObject);
                }

                if (playerObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(playerObject);
                }

                if (poolObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(poolObject);
                }

                PoolManager.Instance = null;
            }
        }

        [Test]
        public void RealDanteWeapon_UsesIndependentPlayerBuildsAndFrozenNativeDamage()
        {
            GameObject poolObject = null;
            GameObject playerObjectA = null;
            GameObject playerObjectB = null;
            try
            {
                poolObject = new GameObject("Test Pool Manager");
                PoolManager pool = poolObject.AddComponent<PoolManager>();
                pool.Init();

                PlayerBuildRuntime buildA = CreateInactivePlayer(
                    "Player A",
                    out playerObjectA);
                PlayerBuildRuntime buildB = CreateInactivePlayer(
                    "Player B",
                    out playerObjectB);

                WeaponData weaponData = ResourcesLoadAsset<WeaponData>(WeaponPath);
                NativeGasEquipmentDefinition damageEquipment =
                    ResourcesLoadAsset<NativeGasEquipmentDefinition>(DamageEquipmentPath);

                WeaponBehaviour weaponA = buildA.EquipWeapon(weaponData);
                WeaponBehaviour weaponB = buildB.EquipWeapon(weaponData);

                Assert.That(
                    weaponA,
                    Is.TypeOf<AstralShift.HellMaiden.Player.Attacks.ProjectileAttackBehaviour>());
                Assert.That(weaponA.UsesNativeGasRuntime, Is.True);
                Assert.That(weaponB.UsesNativeGasRuntime, Is.True);
                Assert.That(weaponA.NativeRuntime.RuntimeModifiers,
                    Is.Not.SameAs(weaponB.NativeRuntime.RuntimeModifiers));
                Assert.That(weaponA.DamageValue, Is.EqualTo(15));
                Assert.That(weaponB.DamageValue, Is.EqualTo(15));

                PlayerBuildEquipmentHandle damageHandle =
                    buildA.AddEquipment(weaponA, damageEquipment, 0);
                Assert.That(weaponA.DamageValue, Is.EqualTo(20));
                Assert.That(weaponB.DamageValue, Is.EqualTo(15));

                using (AttackSnapshot frozen = weaponA.NativeRuntime.BeginAttack(
                    weaponA.NativeDefinition.AttackTags))
                {
                    Assert.That(frozen.Stats.Damage, Is.EqualTo(20));
                    Assert.That(buildA.RemoveEquipment(damageHandle), Is.True);
                    Assert.That(weaponA.DamageValue, Is.EqualTo(15));

                    var target = new RuntimeTarget(100);
                    CombatResolution resolution = weaponA.NativeRuntime.ResolveHitDetailed(
                        frozen,
                        target);
                    Assert.That(resolution.ResolvedDamage.Value, Is.EqualTo(20));
                    Assert.That(resolution.ResolvedDamage.IsCritical, Is.False);
                    Assert.That(target.Health, Is.EqualTo(80));
                }

                Assert.Throws<InvalidOperationException>(() =>
                    weaponA.CalculateDamage((BaseEnemyController)null));
                Assert.That(buildA.EquipmentCount, Is.Zero);
                Assert.That(buildB.EquipmentCount, Is.Zero);
            }
            finally
            {
                if (playerObjectA != null)
                {
                    UnityEngine.Object.DestroyImmediate(playerObjectA);
                }

                if (playerObjectB != null)
                {
                    UnityEngine.Object.DestroyImmediate(playerObjectB);
                }

                if (poolObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(poolObject);
                }

                PoolManager.Instance = null;
            }
        }

        [UnityTest]
        public IEnumerator RealDanteWeapon_AttackSpawnsProjectileAndResolvesOnlyThroughNativeGas()
        {
            GameObject poolObject = null;
            GameObject playerObject = null;
            GameObject targetObject = null;
            const int projectileLayer = 9;
            const int targetLayer = 0;
            bool layersWereIgnored = Physics2D.GetIgnoreLayerCollision(
                projectileLayer,
                targetLayer);

            try
            {
                Physics2D.IgnoreLayerCollision(projectileLayer, targetLayer, false);

                poolObject = new GameObject("Test Pool Manager");
                PoolManager pool = poolObject.AddComponent<PoolManager>();
                pool.Init();

                PlayerBuildRuntime build = CreateInactivePlayer(
                    "Projectile Owner",
                    out playerObject);
                PlayerMovement player = playerObject.GetComponent<PlayerMovement>();
                player.SetAimDirection(Vector2.right);

                WeaponData weaponData = ResourcesLoadAsset<WeaponData>(WeaponPath);
                NativeGasEquipmentDefinition damageEquipment =
                    ResourcesLoadAsset<NativeGasEquipmentDefinition>(DamageEquipmentPath);
                WeaponBehaviour weapon = build.EquipWeapon(weaponData);
                PlayerBuildEquipmentHandle damageHandle =
                    build.AddEquipment(weapon, damageEquipment, 0);

                targetObject = new GameObject("Native GAS Projectile Target");
                targetObject.layer = targetLayer;
                targetObject.transform.position = new Vector3(0.5f, 0f, 0f);
                CircleCollider2D targetCollider =
                    targetObject.AddComponent<CircleCollider2D>();
                targetCollider.radius = 1f;
                Rigidbody2D targetBody = targetObject.AddComponent<Rigidbody2D>();
                targetBody.bodyType = RigidbodyType2D.Kinematic;
                targetBody.gravityScale = 0f;
                NativeProjectileTarget target =
                    targetObject.AddComponent<NativeProjectileTarget>();
                target.Initialize(100);

                bool ignoredLogs = LogAssert.ignoreFailingMessages;
                try
                {
                    // The recovered prefab intentionally keeps its FMOD references,
                    // while the original banks are not part of this migration slice.
                    LogAssert.ignoreFailingMessages = true;
                    weapon.Attack();
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = ignoredLogs;
                }
                Assert.That(build.RemoveEquipment(damageHandle), Is.True);
                Assert.That(weapon.DamageValue, Is.EqualTo(15));

                for (int frame = 0; frame < 20 && target.NativeHitCount == 0; frame++)
                {
                    yield return new WaitForFixedUpdate();
                }

                Assert.That(target.NativeHitCount, Is.EqualTo(1));
                Assert.That(target.LegacyDamageCallCount, Is.Zero);
                Assert.That(target.LastDamage, Is.EqualTo(20));
                Assert.That(target.Health, Is.EqualTo(80));
            }
            finally
            {
                Physics2D.IgnoreLayerCollision(
                    projectileLayer,
                    targetLayer,
                    layersWereIgnored);

                if (targetObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(targetObject);
                }

                if (playerObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(playerObject);
                }

                if (poolObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(poolObject);
                }

                PoolManager.Instance = null;
            }
        }

        private static PlayerBuildRuntime CreateInactivePlayer(
            string name,
            out GameObject playerObject)
        {
            playerObject = new GameObject(name);
            playerObject.SetActive(false);
            PlayerMovement player = playerObject.AddComponent<PlayerMovement>();
            var attacks = new GameObject("Attacks");
            attacks.transform.SetParent(playerObject.transform, false);
            player.AttacksParent = attacks.transform;

            PlayerBuildRuntime build = playerObject.AddComponent<PlayerBuildRuntime>();
            build.Initialize(player, new FixedRandom(0.99f));
            return build;
        }

        private static T ResourcesLoadAsset<T>(string path)
            where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            T asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
#else
            throw new NotSupportedException("This content integration test requires the Editor.");
#endif
        }

        private sealed class FixedRandom : IRandomSource
        {
            private readonly float value;

            public FixedRandom(float value)
            {
                this.value = value;
            }

            public float Next01() => value;
        }

        private sealed class NativeProjectileTarget : MonoBehaviour,
            IDamageable,
            INativeGasDamageable,
            ICombatTarget
        {
            public int Health { get; private set; }
            public int NativeHitCount { get; private set; }
            public int LegacyDamageCallCount { get; private set; }
            public int LastDamage { get; private set; }
            public bool IsAlive => Health > 0;

            public void Initialize(int health)
            {
                Health = health;
            }

            public int GetID() => GetInstanceID();

            public Vector2 GetPosition() => transform.position;

            public bool IsActive() => isActiveAndEnabled;

            public void Damage(
                Vector2 attackPosition,
                WeaponBehaviour weapon,
                LegacyDamageType damageType)
            {
                LegacyDamageCallCount++;
            }

            public void Damage(int value, LegacyDamageType damageType)
            {
                LegacyDamageCallCount++;
            }

            public bool ResolveNativeGasHit(NativeGasHit hit)
            {
                NativeHitCount++;
                CombatResolution resolution = hit.Runtime.ResolveHitDetailed(
                    hit.Attack,
                    this);
                LastDamage = resolution.ResolvedDamage.Value;
                return true;
            }

            public GasDamageInfo ReceiveDamage(GasDamageInfo requestedDamage)
            {
                int accepted = Math.Min(Health, requestedDamage.Value);
                Health -= accepted;
                return new GasDamageInfo(
                    requestedDamage.Id,
                    accepted,
                    requestedDamage.IsCritical);
            }

            public StatusApplicationResult ApplyStatus(StatusApplication application)
            {
                return StatusApplicationResult.Rejected;
            }
        }

        private sealed class RuntimeTarget : ICombatTarget
        {
            public RuntimeTarget(int health)
            {
                Health = health;
            }

            public int Health { get; private set; }
            public bool IsAlive => Health > 0;

            public GasDamageInfo ReceiveDamage(GasDamageInfo requestedDamage)
            {
                int accepted = Math.Min(Health, requestedDamage.Value);
                Health -= accepted;
                return new GasDamageInfo(
                    requestedDamage.Id,
                    accepted,
                    requestedDamage.IsCritical);
            }

            public StatusApplicationResult ApplyStatus(StatusApplication application)
            {
                return StatusApplicationResult.Rejected;
            }
        }
    }
}
