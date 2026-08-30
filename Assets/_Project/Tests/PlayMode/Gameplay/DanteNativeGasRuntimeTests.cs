using System;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using GasDamageInfo = MonsterSupergroup.GAS.DamageInfo;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class DanteNativeGasRuntimeTests
    {
        private const string WeaponPath =
            "Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset";
        private const string DamageEquipmentPath =
            "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/" +
            "NativeGasEquipment_Damage.asset";

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
