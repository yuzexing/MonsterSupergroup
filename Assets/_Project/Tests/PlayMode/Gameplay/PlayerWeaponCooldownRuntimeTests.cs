using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlayerWeaponCooldownRuntimeTests
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private RuntimeDB database;
        private PlayerBuildRuntime build;
        private NetworkWeaponCombatAdapter adapter;

        [SetUp]
        public void SetUp()
        {
            CreateObject("Cooldown Test Pool").AddComponent<PoolManager>().Init();
            database = CreateObject("Cooldown Definitions").AddComponent<RuntimeDB>();
#if UNITY_EDITOR
            var definitions = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponDB>(
                "Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasWeaponDB.asset");
            Assert.That(definitions, Is.Not.Null);
            database.ConfigureWeaponDatabase(definitions);
#else
            throw new NotSupportedException("Definition integration tests require the Editor.");
#endif
            GameObject go = CreateObject("Cooldown Participant");
            go.SetActive(false);
            PlayerMovement player = go.AddComponent<PlayerMovement>();
            var attacks = new GameObject("Attacks");
            attacks.transform.SetParent(go.transform, false);
            player.AttacksParent = attacks.transform;
            build = go.AddComponent<PlayerBuildRuntime>();
            build.Initialize(player, new FixedRandom());
            build.StartInitialBuild(database);
            build.SetWeaponExecutionEnabled(false);
            adapter = go.AddComponent<NetworkWeaponCombatAdapter>();
            SetField(adapter, "playerBuildRuntime", build);
            SetField(adapter, "bridge", go.GetComponent<MirrorNetworkCombatBridge>());
            SetField(adapter, "serviceProvider", go.GetComponent<CombatRuntimeServiceProvider>());
        }

        [TearDown]
        public void TearDown()
        {
            if (adapter != null) adapter.OnStopClient();
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
            PoolManager.Instance = null;
        }

        [Test]
        public void MeleeReconnectRetainsFrozenBurstDelayAfterCurrentCountBecomesOne()
        {
            MeleeAttackBehaviour melee = EquipMelee();
            PlayerBuildEquipmentHandle count = build.AddEquipment(melee,
                LoadEquipment("StatRaise_ProjectileCountRaiseEquipment"), 1);
            Assert.That(melee.ProjectileCountValue, Is.EqualTo(3));
            Assert.That(melee.GetAttackSequenceDuration(), Is.EqualTo(0.3f).Within(0.00001f));
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(2, melee.ID, CombatEventId.Compose(3, 7, 1).Value,
                100, 100, melee.GetCooldown(), default, out var saved, melee.GetAttackSequenceDuration()), Is.True);
            Assert.That(build.RemoveEquipment(count), Is.True);
            Assert.That(melee.GetAttackSequenceDuration(), Is.Zero);
            adapter.PrepareServerRestore(new[] { saved });
            adapter.ApplyOwnerCooldownBaseline(new[] { saved }, 100.05);

            Assert.That(melee.LastAttackElapsedTime, Is.EqualTo(-0.25f).Within(0.00001f));
            Assert.That(melee.GetCooldown() - melee.LastAttackElapsedTime,
                Is.EqualTo(saved.RemainingAt(100.05)).Within(0.00001));
            Assert.That(adapter.CaptureCooldowns().Single().SequenceSeconds, Is.EqualTo(saved.SequenceSeconds));
            float elapsed = melee.LastAttackElapsedTime;
            adapter.ApplyOwnerCooldownBaseline(new[] { saved }, 100.1);
            Assert.That(melee.LastAttackElapsedTime, Is.EqualTo(elapsed), "A repeated baseline cannot reset the same instance.");
            ulong newRoot = CombatEventId.Compose(3, 8, 1).Value;
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(2, melee.ID, newRoot,
                101, 101, melee.GetCooldown(), saved, out _, melee.GetAttackSequenceDuration()), Is.False);
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(2, melee.ID, newRoot,
                saved.ReadyAt, saved.ReadyAt, melee.GetCooldown(), saved, out var next,
                melee.GetAttackSequenceDuration()), Is.True);
            Assert.That(next.SequenceSeconds, Is.Zero);
            Assert.That(next.ReadyAt, Is.EqualTo(saved.ReadyAt + melee.GetCooldown()));
        }

        [Test]
        public void MeleeCaptureFreezesOldCountButAppliesCurrentSpeedDeltaOnce()
        {
            MeleeAttackBehaviour melee = EquipMelee();
            PlayerBuildEquipmentHandle count = build.AddEquipment(melee,
                LoadEquipment("StatRaise_ProjectileCountRaiseEquipment"), 1);
            float oldCooldown = melee.GetCooldown();
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(2, melee.ID, CombatEventId.Compose(3, 7, 1).Value,
                100, 100, oldCooldown, default, out var saved, melee.GetAttackSequenceDuration()), Is.True);
            adapter.PrepareServerRestore(new[] { saved });
            Assert.That(build.RemoveEquipment(count), Is.True);
            Assert.That(adapter.CaptureCooldowns().Single().ReadyAt, Is.EqualTo(saved.ReadyAt));
            var speed = build.AddEquipment(melee, LoadEquipment("StatRaise_SpeedRaiseEquipment"), 0);
            float currentCooldown = melee.GetCooldown();
            Assert.That(currentCooldown, Is.LessThan(oldCooldown));
            var captured = adapter.CaptureCooldowns().Single();
            Assert.That(captured.ReadyAt, Is.EqualTo(100 + currentCooldown + saved.SequenceSeconds).Within(0.00001));
            Assert.That(captured.SequenceSeconds, Is.EqualTo(saved.SequenceSeconds));
            Assert.That(adapter.CaptureCooldowns().Single().ReadyAt, Is.EqualTo(captured.ReadyAt));
            adapter.ApplyOwnerCooldownBaseline(new[] { captured }, 100.05);
            Assert.That(melee.GetCooldown() - melee.LastAttackElapsedTime,
                Is.EqualTo(captured.RemainingAt(100.05)).Within(0.00001));
            Assert.That(build.RemoveEquipment(speed), Is.True);
            Assert.That(adapter.CaptureCooldowns().Single().ReadyAt, Is.EqualTo(saved.ReadyAt).Within(0.00001));
        }

        [Test]
        public void MeleeFrozenSequenceSurvivesGeneratedMirrorSerialization()
        {
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(2, 1, CombatEventId.Compose(3, 7, 1).Value,
                100, 100, 1, default, out var saved, 0.3f), Is.True);
            var writer = new Mirror.NetworkWriter();
            writer.Write(saved);
            var reader = new Mirror.NetworkReader(writer.ToArraySegment());
            PlayerWeaponCooldownSnapshot restored = reader.Read<PlayerWeaponCooldownSnapshot>();
            Assert.That(restored.IsValid, Is.True);
            Assert.That(restored.SequenceSeconds, Is.EqualTo(saved.SequenceSeconds));
            Assert.That(restored.CooldownSeconds, Is.EqualTo(saved.CooldownSeconds));
            Assert.That(restored.ReadyAt, Is.EqualTo(saved.ReadyAt));
            Assert.That(restored.LastAttackEventId, Is.EqualTo(saved.LastAttackEventId));
            Assert.That(reader.Remaining, Is.Zero);
        }

        [Test]
        public void RemainingCooldownRestoresEachSlotAndExpiredDeadlinesBecomeReady()
        {
            WeaponBehaviour first = build.InitialWeapon;
            WeaponBehaviour second = build.EquipWeaponAtSlot(2, first.WeaponData);
            double clock = 100;
            float cooldown = first.GetCooldown();
            var snapshots = new[]
            {
                Snapshot(0, first, clock, cooldown * 0.75f),
                Snapshot(2, second, clock, 0f)
            };

            adapter.ApplyOwnerCooldownBaseline(snapshots, clock);

            Assert.That(first.LastAttackElapsedTime, Is.EqualTo(cooldown * 0.25f).Within(0.0001f));
            Assert.That(second.LastAttackElapsedTime, Is.EqualTo(second.GetCooldown()).Within(0.0001f));
            Assert.That(build.WeaponCount, Is.EqualTo(2));
        }

        [Test]
        public void RepeatedBaselinePreservesCooldownUntilAuthorityIsRebound()
        {
            WeaponBehaviour weapon = build.InitialWeapon;
            float cooldown = weapon.GetCooldown();
            var snapshots = new[] { Snapshot(0, weapon, 100, cooldown * 0.75f) };
            adapter.ApplyOwnerCooldownBaseline(snapshots, 100);
            weapon.RestoreCooldownRemaining(cooldown * 0.2f);
            float elapsedBeforeBaseline = weapon.LastAttackElapsedTime;

            build.ReconcileState(database, build.CaptureState());
            adapter.ApplyOwnerCooldownBaseline(snapshots, 100);

            Assert.That(build.InitialWeapon, Is.SameAs(weapon));
            Assert.That(weapon.LastAttackElapsedTime, Is.EqualTo(elapsedBeforeBaseline));
            adapter.OnStopAuthority();
            adapter.OnStartAuthority();
            adapter.ApplyOwnerCooldownBaseline(snapshots, 100);
            Assert.That(weapon.LastAttackElapsedTime, Is.EqualTo(cooldown * 0.25f).Within(0.0001f),
                "A new authority binding must restore the server baseline even when Unity retained the weapon instance.");
        }

        [Test]
        public void RebuiltWeaponReceivesItsSavedRemainingDeadlineAfterReconciliation()
        {
            WeaponBehaviour oldWeapon = build.InitialWeapon;
            float cooldown = oldWeapon.GetCooldown();
            var snapshots = new[] { Snapshot(0, oldWeapon, 100, cooldown * 0.75f) };
            PlayerBuildSnapshot buildSnapshot = build.CaptureState();
            adapter.ApplyOwnerCooldownBaseline(snapshots, 100);

            build.RestoreState(database, buildSnapshot);
            adapter.ApplyOwnerCooldownBaseline(snapshots, 100 + cooldown * 0.25f);

            Assert.That(build.InitialWeapon, Is.Not.SameAs(oldWeapon));
            Assert.That(build.InitialWeapon.LastAttackElapsedTime,
                Is.EqualTo(cooldown * 0.5f).Within(0.0001f));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void EquipmentSpeedChangeUsesTheSameDeadlineForCaptureOwnerRestoreAndAdmission(bool addSpeed)
        {
            EquipmentData speedCard;
#if UNITY_EDITOR
            var equipmentDatabase = UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentDB>(
                "Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasEquipmentDB.asset");
            speedCard = equipmentDatabase.Equipments.Single(card =>
                card.Levels[0].Modifiers.Length == 1 &&
                card.Levels[0].Modifiers[0].ModifierIdValue == SpeedStatModifier.ModifierIdValue);
#else
            throw new NotSupportedException("Definition integration tests require the Editor.");
#endif
            WeaponBehaviour weapon = build.InitialWeapon;
            PlayerBuildEquipmentHandle equipment = default;
            if (!addSpeed) equipment = build.AddEquipment(weapon, speedCard, 0);
            float oldCooldown = weapon.GetCooldown();
            var saved = Snapshot(0, weapon, 100, oldCooldown);
            saved.CooldownSeconds = oldCooldown;
            adapter.PrepareServerRestore(new[] { saved });
            Assert.That(adapter.CaptureCooldowns().Single().ReadyAt, Is.EqualTo(saved.ReadyAt));

            if (addSpeed) build.AddEquipment(weapon, speedCard, 0);
            else Assert.That(build.RemoveEquipment(equipment), Is.True);
            float currentCooldown = weapon.GetCooldown();
            if (addSpeed) Assert.That(currentCooldown, Is.LessThan(oldCooldown));
            else Assert.That(currentCooldown, Is.GreaterThan(oldCooldown));
            double now = 100 + Math.Min(oldCooldown, currentCooldown) * 0.25;
            double readyAt = 100 + currentCooldown;

            // Restoration may receive a checkpoint captured before the stat change. It must
            // agree with both live capture and the next server attack admission.
            adapter.ApplyOwnerCooldownBaseline(new[] { saved }, now);
            PlayerWeaponCooldownSnapshot captured = adapter.CaptureCooldowns().Single();
            Assert.That(captured.ReadyAt, Is.EqualTo(readyAt).Within(0.000001));
            Assert.That(captured.CooldownSeconds, Is.EqualTo(currentCooldown));
            Assert.That(adapter.CaptureCooldowns().Single().ReadyAt, Is.EqualTo(captured.ReadyAt));
            Assert.That(weapon.LastAttackElapsedTime, Is.EqualTo(now - 100).Within(0.000001));
            Assert.That(currentCooldown - weapon.LastAttackElapsedTime,
                Is.EqualTo(captured.RemainingAt(now)).Within(0.000001));
            ulong reconnectedId = CombatEventId.Compose(3, 8, 1).Value;
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, weapon.ID, reconnectedId,
                now, now, currentCooldown, captured, out _), Is.False);
            Assert.That(PlayerWeaponCooldownSnapshot.TryAdmit(0, weapon.ID, reconnectedId,
                readyAt, readyAt, currentCooldown, captured, out var next), Is.True);
            Assert.That(next.ReadyAt, Is.EqualTo(readyAt + currentCooldown).Within(0.000001));
        }

        [Test]
        public void LegacyCheckpointCapturePreservesDeadlineWithoutInventingAnInterval()
        {
            WeaponBehaviour weapon = build.InitialWeapon;
            float cooldown = weapon.GetCooldown();
            var saved = Snapshot(0, weapon, 100, cooldown * 0.75f);
            adapter.PrepareServerRestore(new[] { saved });

            PlayerWeaponCooldownSnapshot captured = adapter.CaptureCooldowns().Single();
            Assert.That(captured.ReadyAt, Is.EqualTo(saved.ReadyAt));
            Assert.That(captured.CooldownSeconds, Is.Zero);
            adapter.ApplyOwnerCooldownBaseline(new[] { captured }, 100);
            Assert.That(weapon.LastAttackElapsedTime, Is.EqualTo(cooldown * 0.25f).Within(0.000001));
        }

        [Test]
        public void InvalidBaselineFailsBeforeChangingAnyWeaponAndRemainderIsBounded()
        {
            WeaponBehaviour weapon = build.InitialWeapon;
            float elapsed = weapon.LastAttackElapsedTime;
            var snapshot = Snapshot(0, weapon, 100, weapon.GetCooldown());
            Assert.Throws<ArgumentException>(() => adapter.ApplyOwnerCooldownBaseline(
                new[] { snapshot, snapshot }, 100));
            Assert.Throws<ArgumentNullException>(() => adapter.ApplyOwnerCooldownBaseline(null, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => adapter.ApplyOwnerCooldownBaseline(
                new[] { snapshot }, double.NaN));
            Assert.That(weapon.LastAttackElapsedTime, Is.EqualTo(elapsed));
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
                Assert.Throws<ArgumentOutOfRangeException>(() => weapon.RestoreCooldownRemaining(invalid));
            weapon.RestoreCooldownRemaining(weapon.GetCooldown() * 2);
            Assert.That(weapon.LastAttackElapsedTime, Is.Zero);
        }

        [Test]
        public void NativeAttackForwardingUsesExistingEventIdentityAndDetachesRemovedWeapons()
        {
            WeaponBehaviour first = build.InitialWeapon;
            WeaponBehaviour second = build.EquipWeaponAtSlot(2, first.WeaponData);
            var events = new List<(int Slot, uint Weapon, CombatEventId Event)>();
            build.NativeAttackStarted += (slot, weapon, id) => events.Add((slot, weapon, id));
            using (AttackSnapshot firstAttack = BeginAttack(first))
            using (AttackSnapshot secondAttack = BeginAttack(second))
            {
                Assert.That(events.Count, Is.EqualTo(2));
                Assert.That(events[0].Slot, Is.Zero);
                Assert.That(events[1].Slot, Is.EqualTo(2));
                Assert.That(events[0].Weapon, Is.EqualTo(first.WeaponData.ID));
                Assert.That(events[1].Weapon, Is.EqualTo(second.WeaponData.ID));
                Assert.That(events[0].Event, Is.EqualTo(firstAttack.Context.EventId));
                Assert.That(events[1].Event, Is.EqualTo(secondAttack.Context.EventId));
                Assert.That(events[1].Event.Sequence, Is.GreaterThan(events[0].Event.Sequence));
            }

            Assert.That(SubscriberCount(typeof(WeaponBehaviour), first, "NativeAttackStarted", build), Is.EqualTo(1));
            build.UnequipWeapon(first);
            Assert.That(SubscriberCount(typeof(WeaponBehaviour), first, "NativeAttackStarted", build), Is.Zero);
            using (BeginAttack(second)) { }
            Assert.That(events.Count, Is.EqualTo(3));
            build.ClearBuild();
            Assert.That(SubscriberCount(typeof(WeaponBehaviour), second, "NativeAttackStarted", build), Is.Zero);
        }

        [Test]
        public void RepeatedAuthorityCallbacksKeepOneAttackSubscriptionAndStopClientRemovesIt()
        {
            for (int cycle = 0; cycle < 3; cycle++)
            {
                adapter.OnStartAuthority();
                adapter.OnStartAuthority();
                Assert.That(SubscriberCount(typeof(PlayerBuildRuntime), build, "NativeAttackStarted", adapter), Is.EqualTo(1));
                adapter.OnStopAuthority();
                adapter.OnStopAuthority();
                Assert.That(SubscriberCount(typeof(PlayerBuildRuntime), build, "NativeAttackStarted", adapter), Is.Zero);
            }
            adapter.OnStartAuthority();
            adapter.OnStopClient();
            Assert.That(SubscriberCount(typeof(PlayerBuildRuntime), build, "NativeAttackStarted", adapter), Is.Zero);
        }

        [Test]
        public void DestroyingOnlyAdapterDetachesEveryOwnerCallbackFromSurvivingBuild()
        {
            // OnDestroy is a Unity lifecycle callback only after the object has been active.
            // Keep automatic movement and weapon execution off in this existing fixture.
            build.Owner.enabled = false;
            build.gameObject.SetActive(true);
            adapter.OnStartAuthority();
            adapter.OnStartAuthority();
            var bridge = build.GetComponent<MirrorNetworkCombatBridge>();
            string[] buildEvents =
            {
                "NativeAttackStarted", "NativeAttackCompleted",
                "ProjectilePresentationSpawned", "ProjectilePresentationTerminated",
                "MeleePresentationSpawned", "MeleePresentationTerminated"
            };
            foreach (string eventName in buildEvents)
                Assert.That(SubscriberCount(typeof(PlayerBuildRuntime), build, eventName, adapter),
                    Is.EqualTo(1), eventName);
            Assert.That(SubscriberCount(typeof(MirrorNetworkCombatBridge), bridge,
                "OwnerCollectorReady", adapter), Is.EqualTo(1));

            UnityEngine.Object.DestroyImmediate(adapter);

            Assert.That(build.IsBuildActive, Is.True);
            foreach (string eventName in buildEvents)
                Assert.That(SubscriberCount(typeof(PlayerBuildRuntime), build, eventName, adapter),
                    Is.Zero, eventName);
            Assert.That(SubscriberCount(typeof(MirrorNetworkCombatBridge), bridge,
                "OwnerCollectorReady", adapter), Is.Zero);
            using (BeginAttack(build.InitialWeapon)) { }
            typeof(PlayerBuildRuntime).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(build, null);
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
        }

        private static PlayerWeaponCooldownSnapshot Snapshot(int slot, WeaponBehaviour weapon,
            double clock, float remaining) => new PlayerWeaponCooldownSnapshot
        {
            SlotIndex = slot, WeaponId = weapon.WeaponData.ID,
            LastAttackEventId = CombatEventId.Compose(3, 7, (uint)slot + 1).Value,
            ServerReceivedAt = clock, ReadyAt = clock + remaining
        };

        private MeleeAttackBehaviour EquipMelee()
        {
#if UNITY_EDITOR
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>(
                "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Melee/MonoBehaviour/WeaponData_Dante_Melee.asset");
            Assert.That(definition, Is.Not.Null);
            return (MeleeAttackBehaviour)build.EquipWeaponAtSlot(2, definition);
#else
            throw new NotSupportedException("Definition integration tests require the Editor.");
#endif
        }

        private static EquipmentData LoadEquipment(string name)
        {
#if UNITY_EDITOR
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<EquipmentData>("Assets/MonoBehaviour/" + name + ".asset");
            Assert.That(definition, Is.Not.Null);
            return definition;
#else
            throw new NotSupportedException("Definition integration tests require the Editor.");
#endif
        }

        // Invoke the production native begin boundary without unrelated projectile/audio effects.
        private static AttackSnapshot BeginAttack(WeaponBehaviour weapon) => (AttackSnapshot)
            typeof(WeaponBehaviour).GetMethod("BeginNativeGasAttack", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(weapon, null);

        private static int SubscriberCount(Type declaringType, object owner, string eventName, object subscriber)
        {
            var handler = (Delegate)declaringType.GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(owner);
            return handler?.GetInvocationList().Count(item => ReferenceEquals(item.Target, subscriber)) ?? 0;
        }

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name);
            objects.Add(result);
            return result;
        }

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private sealed class FixedRandom : IRandomSource
        {
            public float Next01() => 0.5f;
        }
    }
}
