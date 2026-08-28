using System;
using System.Collections.Generic;
using System.Linq;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.GAS.Unity;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class GameplayCombatIntegrationTests
    {
        [Test]
        public void Combatant_AppliesDirectAndStatusDamageDeterministically()
        {
            var gameObject = new GameObject("Combatant Test");
            try
            {
                CombatantBehaviour combatant = gameObject.AddComponent<CombatantBehaviour>();
                combatant.Initialize(20);

                DamageInfo direct = combatant.ReceiveDamage(new DamageInfo(7, 7, false));
                StatusApplicationResult applied = combatant.ApplyStatus(new StatusApplication(
                    OnHitBurnModifier.BurnDefinition,
                    2,
                    3,
                    0.1f,
                    6f,
                    7));
                combatant.AdvanceStatuses(0.3f);

                Assert.That(direct.Value, Is.EqualTo(7));
                Assert.That(applied, Is.EqualTo(StatusApplicationResult.Added));
                Assert.That(combatant.CurrentHealth, Is.EqualTo(7));
                Assert.That(combatant.DirectDamageTaken, Is.EqualTo(7));
                Assert.That(combatant.StatusDamageTaken, Is.EqualTo(6));
                Assert.That(combatant.StatusTickCount, Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Combatant_LethalStatusStopsPendingTicksAndClearsStatusState()
        {
            var gameObject = new GameObject("Lethal Status Test");
            try
            {
                CombatantBehaviour combatant = gameObject.AddComponent<CombatantBehaviour>();
                combatant.Initialize(3);
                int predictedCount = 0;
                int confirmedCount = 0;
                combatant.PredictedLethalHitReceived += _ => predictedCount++;
                combatant.ConfirmedKillReceived += _ => confirmedCount++;
                combatant.ApplyStatus(new StatusApplication(
                    OnHitBurnModifier.BurnDefinition,
                    2,
                    3,
                    0.1f,
                    6f));

                combatant.AdvanceStatuses(1f);

                Assert.That(combatant.IsAlive, Is.False);
                Assert.That(combatant.CurrentHealth, Is.Zero);
                Assert.That(combatant.StatusDamageTaken, Is.EqualTo(3));
                Assert.That(combatant.StatusTickCount, Is.EqualTo(2));
                Assert.That(combatant.StatusController.Count, Is.Zero);
                Assert.That(predictedCount, Is.EqualTo(1));
                Assert.That(confirmedCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void OfflineCombatant_PredictedLethalImmediatelyProducesConfirmedKill()
        {
            GameObject weaponObject = null;
            GameObject targetObject = null;
            try
            {
                WeaponRuntimeBehaviour weapon = CreateWeapon(
                    out weaponObject,
                    damage: 10,
                    speed: 1f,
                    equipment: null,
                    perks: null);
                weapon.ConfigureCombatIdentity(12, 13);
                CombatantBehaviour target = CreateCombatant(out targetObject, 5);
                target.ConfigureEntityId(99);
                int predictedCount = 0;
                int confirmedCount = 0;
                ConfirmedKill confirmed = default;
                target.PredictedLethalHitReceived += _ => predictedCount++;
                target.ConfirmedKillReceived += value =>
                {
                    confirmed = value;
                    confirmedCount++;
                };

                weapon.Attack(target);

                Assert.That(predictedCount, Is.EqualTo(1));
                Assert.That(confirmedCount, Is.EqualTo(1));
                Assert.That(confirmed.KillerPlayerId, Is.EqualTo(12));
                Assert.That(confirmed.TargetEntityId, Is.EqualTo(99));
            }
            finally
            {
                Destroy(weaponObject, targetObject);
            }
        }

        [Test]
        public void NetworkedCombatant_WaitsForCanonicalConfirmedKill()
        {
            GameObject weaponObject = null;
            GameObject targetObject = null;
            try
            {
                WeaponRuntimeBehaviour weapon = CreateWeapon(
                    out weaponObject,
                    damage: 10,
                    speed: 1f,
                    equipment: null,
                    perks: null);
                weapon.ConfigureCombatIdentity(12, 13);
                CombatantBehaviour target = CreateCombatant(out targetObject, 5);
                target.ConfigureEntityId(99);
                target.ConfigureKillConfirmation(true);
                int predictedCount = 0;
                int confirmedCount = 0;
                target.PredictedLethalHitReceived += _ => predictedCount++;
                target.ConfirmedKillReceived += _ => confirmedCount++;

                weapon.Attack(target);

                Assert.That(predictedCount, Is.EqualTo(1));
                Assert.That(confirmedCount, Is.Zero);
                target.ReceiveConfirmedKill(new ConfirmedKill
                {
                    CauseEventId = 123,
                    KillerPlayerId = 12,
                    TargetEntityId = 99,
                    TargetStateVersion = 1
                });
                target.ReceiveConfirmedKill(new ConfirmedKill
                {
                    CauseEventId = 123,
                    KillerPlayerId = 12,
                    TargetEntityId = 99,
                    TargetStateVersion = 1
                });

                Assert.That(confirmedCount, Is.EqualTo(1));
            }
            finally
            {
                Destroy(weaponObject, targetObject);
            }
        }

        [Test]
        public void Weapon_ExecutesDamageBurnTickAndSpeedPerkVerticalSlice()
        {
            GameObject weaponObject = null;
            GameObject targetObject = null;
            try
            {
                WeaponRuntimeBehaviour weapon = CreateWeapon(
                    out weaponObject,
                    damage: 10,
                    speed: 2f,
                    equipment: FullEquipment(),
                    perks: SpeedPerks());
                CombatantBehaviour target = CreateCombatant(out targetObject, 100);

                DamageInfo direct = weapon.Attack(target);
                target.AdvanceStatuses(1f);

                Assert.That(weapon.ModifierCount, Is.EqualTo(2));
                Assert.That(weapon.Stats.DamageValue, Is.EqualTo(15));
                Assert.That(weapon.Stats.SpeedValue, Is.EqualTo(2.5f).Within(0.0001f));
                Assert.That(direct.Value, Is.EqualTo(15));
                Assert.That(target.DirectDamageTaken, Is.EqualTo(15));
                Assert.That(target.StatusDamageTaken, Is.EqualTo(14));
                Assert.That(target.StatusTickCount, Is.EqualTo(2));
                Assert.That(target.CurrentHealth, Is.EqualTo(71));
            }
            finally
            {
                Destroy(weaponObject, targetObject);
            }
        }

        [Test]
        public void ControllerWeaponBurnAndStatusDriver_FormOneLethalVerticalSlice()
        {
            GameObject weaponObject = null;
            GameObject targetObject = null;
            GameObject controllerObject = null;
            try
            {
                WeaponRuntimeBehaviour weapon = CreateWeapon(
                    out weaponObject,
                    damage: 10,
                    speed: 2f,
                    equipment: FullEquipment(),
                    perks: SpeedPerks());
                CombatantBehaviour target = CreateCombatant(out targetObject, 29);
                StatusUpdateDriver driver = targetObject.AddComponent<StatusUpdateDriver>();
                driver.Configure(target);
                controllerObject = new GameObject("Full Vertical Slice Controller");
                VerticalSliceCombatController controller =
                    controllerObject.AddComponent<VerticalSliceCombatController>();
                controller.Configure(weapon, target);

                controller.Tick(controller.AttackInterval + 0.01f);
                driver.Tick(1f);

                Assert.That(target.DirectDamageTaken, Is.EqualTo(15));
                Assert.That(target.StatusDamageTaken, Is.EqualTo(14));
                Assert.That(target.StatusTickCount, Is.EqualTo(2));
                Assert.That(target.IsAlive, Is.False);

                controller.Tick(10f);
                Assert.That(target.DirectDamageTaken, Is.EqualTo(15));
            }
            finally
            {
                Destroy(controllerObject, weaponObject, targetObject);
            }
        }

        [Test]
        public void SameSeed_ReproducesCriticalAndProbabilisticOnHitSequence()
        {
            GameObject firstWeaponObject = null;
            GameObject secondWeaponObject = null;
            GameObject firstTargetObject = null;
            GameObject secondTargetObject = null;
            try
            {
                IReadOnlyList<EquipmentDataModifier> equipment = ProbabilisticEquipment(0.5f);
                WeaponRuntimeBehaviour firstWeapon = CreateWeapon(
                    out firstWeaponObject,
                    10,
                    1f,
                    equipment,
                    null,
                    randomSource: new SeededRandomSource(314159),
                    critRate: 0.5f,
                    critMultiplier: 2f);
                WeaponRuntimeBehaviour secondWeapon = CreateWeapon(
                    out secondWeaponObject,
                    10,
                    1f,
                    equipment,
                    null,
                    randomSource: new SeededRandomSource(314159),
                    critRate: 0.5f,
                    critMultiplier: 2f);
                CombatantBehaviour firstTarget = CreateCombatant(out firstTargetObject, 100);
                CombatantBehaviour secondTarget = CreateCombatant(out secondTargetObject, 100);

                var firstDamages = new DamageInfo[3];
                var secondDamages = new DamageInfo[3];
                for (int index = 0; index < firstDamages.Length; index++)
                {
                    firstDamages[index] = firstWeapon.Attack(firstTarget);
                    secondDamages[index] = secondWeapon.Attack(secondTarget);
                    firstTarget.AdvanceStatuses(1f);
                    secondTarget.AdvanceStatuses(1f);
                }

                Assert.That(firstDamages, Is.EqualTo(secondDamages));
                Assert.That(firstDamages.All(damage => damage.Value == 10 || damage.Value == 20),
                    Is.True);
                Assert.That(firstTarget.CurrentHealth, Is.EqualTo(secondTarget.CurrentHealth));
                Assert.That(firstTarget.StatusTickCount, Is.EqualTo(secondTarget.StatusTickCount));
                Assert.That(firstTarget.StatusDamageTaken, Is.EqualTo(secondTarget.StatusDamageTaken));
            }
            finally
            {
                Destroy(
                    firstWeaponObject,
                    secondWeaponObject,
                    firstTargetObject,
                    secondTargetObject);
            }
        }

        [Test]
        public void Weapon_ReinitializeReplacesOwnedModifiersWithoutAccumulation()
        {
            GameObject weaponObject = null;
            GameObject targetObject = null;
            try
            {
                WeaponRuntimeBehaviour weapon = CreateWeapon(
                    out weaponObject,
                    damage: 10,
                    speed: 1f,
                    equipment: DamageOnly(0.5f),
                    perks: null);
                CombatantBehaviour target = CreateCombatant(out targetObject, 100);

                Assert.That(weapon.Attack(target).Value, Is.EqualTo(15));
                weapon.Initialize(
                    BaseStats(10, 1f),
                    DamageOnly(0.1f),
                    null,
                    new FixedRandom(0f),
                    99);
                target.Initialize(100);

                Assert.That(weapon.ModifierCount, Is.EqualTo(1));
                Assert.That(weapon.CombatId, Is.EqualTo(99));
                Assert.That(weapon.Attack(target).Value, Is.EqualTo(11));
            }
            finally
            {
                Destroy(weaponObject, targetObject);
            }
        }

        [Test]
        public void StatusUpdateDriver_DelegatesExplicitDeltaWithoutOwningTime()
        {
            var gameObject = new GameObject("Status Driver Test");
            try
            {
                CombatantBehaviour combatant = gameObject.AddComponent<CombatantBehaviour>();
                StatusUpdateDriver driver = gameObject.AddComponent<StatusUpdateDriver>();
                combatant.Initialize(10);
                driver.Configure(combatant);
                combatant.ApplyStatus(new StatusApplication(
                    OnHitBurnModifier.BurnDefinition,
                    2,
                    2,
                    0.25f,
                    4f));

                driver.Tick(0.5f);

                Assert.That(combatant.CurrentHealth, Is.EqualTo(6));
                Assert.That(combatant.StatusTickCount, Is.EqualTo(2));
                Assert.Throws<ArgumentOutOfRangeException>(() => driver.Tick(-0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Controller_UsesWeaponSpeedStopsAtDeathAndCanResetEncounter()
        {
            GameObject weaponObject = null;
            GameObject targetObject = null;
            GameObject controllerObject = null;
            try
            {
                WeaponRuntimeBehaviour weapon = CreateWeapon(
                    out weaponObject,
                    damage: 10,
                    speed: 2f,
                    equipment: DamageOnly(0.5f),
                    perks: SpeedPerks());
                CombatantBehaviour target = CreateCombatant(out targetObject, 30);
                controllerObject = new GameObject("Combat Controller Test");
                VerticalSliceCombatController controller =
                    controllerObject.AddComponent<VerticalSliceCombatController>();
                controller.Configure(weapon, target);

                Assert.That(controller.AttackInterval, Is.EqualTo(0.4f).Within(0.0001f));
                controller.Tick(0.81f);

                Assert.That(target.IsAlive, Is.False);
                Assert.That(target.DirectDamageTaken, Is.EqualTo(30));
                Assert.That(controller.LastDamage.Value, Is.EqualTo(15));

                controller.Tick(10f);
                Assert.That(target.DirectDamageTaken, Is.EqualTo(30));

                controller.ResetEncounter();
                Assert.That(target.CurrentHealth, Is.EqualTo(30));
                Assert.That(target.DirectDamageTaken, Is.Zero);
                Assert.That(controller.LastDamage.Value, Is.Zero);
            }
            finally
            {
                Destroy(controllerObject, weaponObject, targetObject);
            }
        }

        [Test]
        public void ControllerConfigure_IsAtomicAndResetsEncounterReadModel()
        {
            GameObject firstWeaponObject = null;
            GameObject secondWeaponObject = null;
            GameObject targetObject = null;
            GameObject controllerObject = null;
            try
            {
                WeaponRuntimeBehaviour firstWeapon = CreateWeapon(
                    out firstWeaponObject,
                    5,
                    1f,
                    null,
                    null);
                WeaponRuntimeBehaviour secondWeapon = CreateWeapon(
                    out secondWeaponObject,
                    9,
                    1f,
                    null,
                    null);
                CombatantBehaviour target = CreateCombatant(out targetObject, 100);
                controllerObject = new GameObject("Atomic Configure Test");
                VerticalSliceCombatController controller =
                    controllerObject.AddComponent<VerticalSliceCombatController>();
                controller.Configure(firstWeapon, target);
                controller.AttackOnce();

                Assert.Throws<ArgumentNullException>(() => controller.Configure(secondWeapon, null));
                Assert.That(controller.Weapon, Is.SameAs(firstWeapon));
                Assert.That(controller.Target, Is.SameAs(target));
                Assert.That(controller.LastDamage.Value, Is.EqualTo(5));

                controller.Configure(secondWeapon, target);
                Assert.That(controller.Weapon, Is.SameAs(secondWeapon));
                Assert.That(controller.LastDamage.Value, Is.Zero);
            }
            finally
            {
                Destroy(
                    controllerObject,
                    firstWeaponObject,
                    secondWeaponObject,
                    targetObject);
            }
        }

        [Test]
        public void RuntimeUpdateOrder_IsAttackBeforeStatusAdvance()
        {
            var controllerOrder = (DefaultExecutionOrder)Attribute.GetCustomAttribute(
                typeof(VerticalSliceCombatController),
                typeof(DefaultExecutionOrder));
            var statusOrder = (DefaultExecutionOrder)Attribute.GetCustomAttribute(
                typeof(StatusUpdateDriver),
                typeof(DefaultExecutionOrder));

            Assert.That(controllerOrder, Is.Not.Null);
            Assert.That(statusOrder, Is.Not.Null);
            Assert.That(controllerOrder.order, Is.LessThan(statusOrder.order));
        }

        [Test]
        public void OneHundredCreateAttackDestroyCycles_DoNotLeakRuntimeState()
        {
            IReadOnlyList<EquipmentDataModifier> equipment = FullEquipment();
            for (int index = 0; index < 100; index++)
            {
                GameObject weaponObject = null;
                GameObject targetObject = null;
                try
                {
                    WeaponRuntimeBehaviour weapon = CreateWeapon(
                        out weaponObject,
                        damage: 10,
                        speed: 1f,
                        equipment: equipment,
                        perks: SpeedPerks(),
                        combatId: (uint)(index + 1));
                    CombatantBehaviour target = CreateCombatant(out targetObject, 100);
                    int healthEvents = 0;
                    target.HealthChanged += (_, __) => healthEvents++;

                    Assert.That(weapon.ModifierCount, Is.EqualTo(2));
                    Assert.That(weapon.Attack(target).Value, Is.EqualTo(15));
                    target.AdvanceStatuses(1f);
                    Assert.That(healthEvents, Is.EqualTo(3));

                    target.ResetCombatant();
                    Assert.That(target.StatusController.Count, Is.Zero);
                    Assert.That(target.DirectDamageTaken, Is.Zero);
                    Assert.That(target.StatusDamageTaken, Is.Zero);
                    Assert.That(target.StatusTickCount, Is.Zero);
                    Assert.That(healthEvents, Is.EqualTo(4));

                    weapon.Shutdown();
                    Assert.That(weapon.IsInitialized, Is.False);
                    Assert.That(weapon.ModifierCount, Is.Zero);
                    Assert.That(weapon.Stats, Is.Null);
                }
                finally
                {
                    Destroy(weaponObject, targetObject);
                }
            }
        }

        [Test]
        public void StraightProjectilePool_ReusesLocalProjectileInstance()
        {
            var prefabObject = new GameObject("Projectile Pool Prefab");
            StraightProjectileBehaviour first = null;
            StraightProjectileBehaviour second = null;
            try
            {
                StraightProjectileBehaviour prefab =
                    prefabObject.AddComponent<StraightProjectileBehaviour>();
                first = StraightProjectilePool.Spawn(
                    prefab,
                    Vector3.zero,
                    Quaternion.identity);
                StraightProjectilePool.Release(first);
                second = StraightProjectilePool.Spawn(
                    prefab,
                    Vector3.one,
                    Quaternion.identity);

                Assert.That(second, Is.SameAs(first));
                Assert.That(second.transform.position, Is.EqualTo(Vector3.one));
                Assert.That(StraightProjectilePool.ReusedCount, Is.GreaterThan(0));
            }
            finally
            {
                if (second != null)
                {
                    StraightProjectilePool.Release(second);
                    UnityEngine.Object.DestroyImmediate(second.gameObject);
                }

                UnityEngine.Object.DestroyImmediate(prefabObject);
            }
        }

        [Test]
        public void GameplayAssembly_HasNoUiInputAudioOrLegacyFrameworkReferences()
        {
            string[] references = typeof(CombatantBehaviour).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();
            string[] forbidden =
            {
                "UnityEngine.UI",
                "Unity.InputSystem",
                "FMOD",
                "Rewired",
                "Assembly-CSharp"
            };

            foreach (string fragment in forbidden)
            {
                Assert.That(
                    references.Any(reference =>
                        reference.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    $"Gameplay assembly unexpectedly references {fragment}: {string.Join(", ", references)}");
            }
        }

        private static WeaponRuntimeBehaviour CreateWeapon(
            out GameObject gameObject,
            int damage,
            float speed,
            IReadOnlyList<EquipmentDataModifier> equipment,
            IReadOnlyList<PerkDataModifier> perks,
            uint combatId = 77,
            IRandomSource randomSource = null,
            float critRate = 0f,
            float critMultiplier = 1.5f)
        {
            gameObject = new GameObject("Weapon Test");
            WeaponRuntimeBehaviour weapon = gameObject.AddComponent<WeaponRuntimeBehaviour>();
            weapon.Initialize(
                BaseStats(damage, speed, critRate, critMultiplier),
                equipment,
                perks,
                randomSource ?? new FixedRandom(0f),
                combatId);
            return weapon;
        }

        private static CombatantBehaviour CreateCombatant(out GameObject gameObject, int health)
        {
            gameObject = new GameObject("Combatant Test");
            CombatantBehaviour combatant = gameObject.AddComponent<CombatantBehaviour>();
            combatant.Initialize(health);
            return combatant;
        }

        private static AttackStats BaseStats(
            int damage,
            float speed,
            float critRate = 0f,
            float critMultiplier = 1.5f)
        {
            return new AttackStats
            {
                damage = damage,
                critMultiplier = critMultiplier,
                critRate = critRate,
                speed = speed,
                size = 1f,
                duration = 1f,
                projectileCount = 1,
                knockbackDistance = 1f,
                damageType = DamageType.Normal
            };
        }

        private static IReadOnlyList<EquipmentDataModifier> FullEquipment()
        {
            return new[]
            {
                new EquipmentDataModifier(
                    new EquipmentModifierID(DamageStatModifier.ModifierIdValue),
                    new DamageStatModifierParameters(0.5f)),
                new EquipmentDataModifier(
                    new EquipmentModifierID(OnHitBurnModifier.ModifierIdValue),
                    new OnHitBurnModifierParameters(1f, 0.5f, 2, 0.5f))
            };
        }

        private static IReadOnlyList<EquipmentDataModifier> DamageOnly(float multiplier)
        {
            return new[]
            {
                new EquipmentDataModifier(
                    new EquipmentModifierID(DamageStatModifier.ModifierIdValue),
                    new DamageStatModifierParameters(multiplier))
            };
        }

        private static IReadOnlyList<EquipmentDataModifier> ProbabilisticEquipment(float chance)
        {
            return new[]
            {
                new EquipmentDataModifier(
                    new EquipmentModifierID(OnHitBurnModifier.ModifierIdValue),
                    new OnHitBurnModifierParameters(chance, 0.5f, 2, 0.5f))
            };
        }

        private static IReadOnlyList<PerkDataModifier> SpeedPerks()
        {
            return new[]
            {
                new PerkDataModifier(
                    new PerkModifierID(WeaponSpeedPerkModifier.ModifierIdValue),
                    new WeaponSpeedPerkModifierParameters(0.25f))
            };
        }

        private static void Destroy(params GameObject[] gameObjects)
        {
            for (int index = 0; index < gameObjects.Length; index++)
            {
                if (gameObjects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObjects[index]);
                }
            }
        }

        private sealed class FixedRandom : IRandomSource
        {
            private readonly float value;

            public FixedRandom(float value)
            {
                this.value = value;
            }

            public float Next01()
            {
                return value;
            }
        }
    }
}
