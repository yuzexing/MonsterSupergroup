using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlayerNativeAttackLifetimeTests
    {
        private GameObject poolObject;
        private GameObject playerObject;
        private PlayerBuildRuntime build;
        private WeaponBehaviour weapon;
        private readonly List<(int Slot, uint Weapon, CombatEventId Event)> completed =
            new List<(int, uint, CombatEventId)>();

        [SetUp]
        public void SetUp()
        {
            poolObject = new GameObject("Native Lifetime Pool");
            poolObject.AddComponent<PoolManager>().Init();
            playerObject = new GameObject("Native Lifetime Owner");
            playerObject.SetActive(false);
            PlayerMovement player = playerObject.AddComponent<PlayerMovement>();
            var attacks = new GameObject("Attacks");
            attacks.transform.SetParent(playerObject.transform, false);
            player.AttacksParent = attacks.transform;
            build = playerObject.AddComponent<PlayerBuildRuntime>();
            build.Initialize(player);
#if UNITY_EDITOR
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>(
                "Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset");
            Assert.That(data, Is.Not.Null);
            weapon = build.EquipWeaponAtSlot(2, data);
#else
            throw new NotSupportedException("Definition integration tests require the Editor.");
#endif
            build.NativeAttackCompleted += (slot, definition, id) =>
                completed.Add((slot, definition, id));
        }

        [TearDown]
        public void TearDown()
        {
            if (playerObject != null) UnityEngine.Object.DestroyImmediate(playerObject);
            if (poolObject != null) UnityEngine.Object.DestroyImmediate(poolObject);
            PoolManager.Instance = null;
            completed.Clear();
        }

        [Test]
        public void CompletionWaitsForEveryProjectileAndDelayedEffectLease()
        {
            using (AttackSnapshot attack = BeginAttack(weapon))
            using (AttackSnapshotLease projectile = attack.Retain())
            using (AttackSnapshotLease delayedEffect = attack.Retain())
            {
                attack.Dispose();
                projectile.Dispose();
                build.SetWeaponExecutionEnabled(false);
                Tick();
                Assert.That(attack.IsDisposed, Is.False);
                Assert.That(completed, Is.Empty);

                delayedEffect.Dispose();
                Assert.That(attack.IsDisposed, Is.True);
                Tick();
                Tick();
                Assert.That(completed.Count, Is.EqualTo(1));
                Assert.That(completed[0], Is.EqualTo((2, weapon.WeaponData.ID, attack.Context.EventId)));
            }
        }

        [Test]
        public void RemovedWeaponKeepsItsOriginalRootUntilDelayedLeaseFinishes()
        {
            WeaponData definition = weapon.WeaponData;
            using (AttackSnapshot attack = BeginAttack(weapon))
            using (AttackSnapshotLease delayedEffect = attack.Retain())
            {
                attack.Dispose();
                Assert.That(build.UnequipWeapon(weapon), Is.True);
                UnityEngine.Object.DestroyImmediate(weapon.gameObject);
                weapon = build.EquipWeaponAtSlot(2, definition);
                Tick();
                Assert.That(completed, Is.Empty);

                delayedEffect.Dispose();
                Tick();
                Assert.That(completed.Count, Is.EqualTo(1));
                Assert.That(completed[0], Is.EqualTo((2, definition.ID, attack.Context.EventId)));
                using (AttackSnapshot replacementAttack = BeginAttack(weapon))
                {
                    replacementAttack.Dispose();
                    Tick();
                    Assert.That(completed.Count, Is.EqualTo(2));
                    Assert.That(completed[1].Event, Is.EqualTo(replacementAttack.Context.EventId));
                    Assert.That(completed[1].Event, Is.Not.EqualTo(completed[0].Event));
                }
            }
        }

        [Test]
        public void ClearBuildCancelsRealProjectileAndPublishesCompletionOnce()
        {
            CombatEventId started = default;
            build.NativeAttackStarted += (_, __, id) => started = id;
            // The recovered projectile keeps its original event; its FMOD bank is not imported.
            LogAssert.Expect(LogType.Exception, new Regex(@"EventNotFoundException: \[FMOD\] Event not found:.*"));
            weapon.Attack();
            Tick();
            Assert.That(started.IsValid, Is.True);
            Assert.That(completed, Is.Empty);

            build.ClearBuild();
            Assert.That(completed.Count, Is.EqualTo(1));
            Assert.That(completed[0].Event, Is.EqualTo(started));
            Tick();
            Assert.That(completed.Count, Is.EqualTo(1));
        }

        [Test]
        public void ShutdownDiscardsObservationWithoutReleasingAnotherEffectsLease()
        {
            using (AttackSnapshot attack = BeginAttack(weapon))
            using (AttackSnapshotLease delayedEffect = attack.Retain())
            {
                attack.Dispose();
                build.Shutdown();
                Assert.That(attack.IsDisposed, Is.False);
                delayedEffect.Dispose();
                Tick();
                Assert.That(attack.IsDisposed, Is.True);
                Assert.That(completed, Is.Empty);
            }
        }

        private static AttackSnapshot BeginAttack(WeaponBehaviour value) => (AttackSnapshot)
            typeof(WeaponBehaviour).GetMethod("BeginNativeGasAttack", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(value, null);

        private void Tick() => typeof(PlayerBuildRuntime)
            .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(build, null);
    }
}
