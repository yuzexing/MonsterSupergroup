#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    /// <summary>Actual remote prefab lifecycle, driven through the coordinator's received-message handlers.</summary>
    public sealed class NetworkPlayerUltimateReplicaRecoveryTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string DefinitionPath = "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Ultimate/MonoBehaviour/UltimateData_Dante.asset";
        private GameObject coordinatorObject, playerObject, attacksObject;
        private NetworkPlayerUltimate coordinator;
        private double originalTimeline;
        private static FieldInfo Timeline => typeof(NetworkClient).GetField("localTimeline", BindingFlags.Static | BindingFlags.NonPublic);
        private DanteUltimateAttack Replica => Field<DanteUltimateAttack>(coordinator, "remoteAttack");

        [SetUp]
        public void SetUp()
        {
            Assert.That(NetworkServer.active || NetworkClient.active, Is.False, "This receiver fixture does not start a transport or impersonate an owned player.");
            originalTimeline = (double)Timeline.GetValue(null);
            SetTime(100d);
            var definition = AssetDatabase.LoadAssetAtPath<UltimateData>(DefinitionPath);
            Assert.That(definition, Is.Not.Null, "Import the real Dante Ultimate before running the receiver fixture.");
            playerObject = new GameObject("Inactive Ultimate replica avatar");
            playerObject.SetActive(false);
            var player = playerObject.AddComponent<PlayerMovement>();
            attacksObject = new GameObject("Ultimate replica attacks");
            player.AttacksParent = attacksObject.transform;
            coordinatorObject = new GameObject("Remote Ultimate coordinator");
            coordinatorObject.SetActive(false);
            coordinator = coordinatorObject.AddComponent<NetworkPlayerUltimate>();
            coordinatorObject.GetComponent<PlayerBuildRuntime>().Initialize(player);
            SetField(coordinator, "ultimateData", definition);
            coordinatorObject.SetActive(true);
            SetField(coordinator, "player", player);
            typeof(NetworkIdentity).GetProperty("netId").SetValue(coordinatorObject.GetComponent<NetworkIdentity>(), 42u);
            Assert.That(coordinator.isOwned, Is.False);
        }

        [TearDown]
        public void TearDown()
        {
            if (coordinatorObject != null) Object.DestroyImmediate(coordinatorObject);
            if (attacksObject != null) Object.DestroyImmediate(attacksObject);
            if (playerObject != null) Object.DestroyImmediate(playerObject);
            Timeline.SetValue(null, originalTimeline);
        }

        [Test]
        public void DisabledRemoteRebuildsOnlyItsStillActiveRootAtTheCurrentAgeAndRejectsDuplicates()
        {
            NetworkUltimatePresentationSpawn spawn = Spawn(7);
            Apply(spawn);
            DanteUltimateAttack first = Replica;
            Assert.That(first.IsPresentationActive, Is.True);
            coordinator.enabled = false;
            Assert.That(first.ActiveUseId, Is.Zero);
            Assert.That(Replica, Is.Null);
            Assert.That(Field<ulong>(coordinator, "suspendedRemoteRootId"), Is.EqualTo(spawn.AttackEventId));
            SetTime(103d);
            coordinator.enabled = true;
            Apply(Spawn(6));
            Assert.That(Replica, Is.Null, "A disabled receiver cannot revive an older root.");
            NetworkUltimatePresentationSpawn otherEpoch = spawn;
            otherEpoch.AttackEventId = CombatEventId.Compose(2, 1, 7).Value;
            Apply(otherEpoch);
            Assert.That(Replica, Is.Null, "Equal sequence numbers from another full root identity do not match the suspended use.");
            Apply(spawn); // The same still-active server baseline now arrives after OnEnable.
            Assert.That(Replica, Is.Not.SameAs(first));
            Assert.That(Replica.ElapsedSeconds, Is.EqualTo(3.5f).Within(0.002f));
            Assert.That(coordinator.ReplicaSpawnCount, Is.EqualTo(2));
            Assert.That(Field<ulong>(coordinator, "suspendedRemoteRootId"), Is.Zero);
            DanteUltimateAttack resumed = Replica;
            Assert.That(resumed.NativeRuntime, Is.Null);
            Assert.That(resumed.WeaponData, Is.Null);
            UltimateDamageAttack[] waves = resumed.GetComponentsInChildren<UltimateDamageAttack>(true);
            Assert.That(waves.Count(wave => wave.IsWavePlaying), Is.EqualTo(1));
            foreach (UltimateDamageAttack wave in waves)
            {
                Assert.That(typeof(BasePlayerAttack).GetProperty("NativeAttackSnapshot", Private).GetValue(wave), Is.Null);
                Assert.That(wave.hitbox.collider.enabled, Is.False);
            }
            Apply(spawn);
            Assert.That(Replica, Is.SameAs(resumed));
            Assert.That(coordinator.ReplicaSpawnCount, Is.EqualTo(2), "A duplicate baseline cannot allocate another 428-particle wave hierarchy.");
            Assert.That(Replica.ElapsedSeconds, Is.EqualTo(3.5f).Within(0.002f));
        }

        [Test]
        public void EndReceivedWhileDisabledRevokesRecoveryAndAnEndedRootCannotRestart()
        {
            NetworkUltimatePresentationSpawn spawn = Spawn(7);
            Apply(spawn);
            coordinator.enabled = false;
            ReceiveEnd(spawn.AttackEventId);
            Assert.That(Field<ulong>(coordinator, "suspendedRemoteRootId"), Is.Zero);
            coordinator.enabled = true;
            Apply(spawn);
            Assert.That(Replica, Is.Null);
            Assert.That(coordinator.ReplicaSpawnCount, Is.EqualTo(1));
            NetworkUltimatePresentationSpawn next = Spawn(8);
            Apply(next);
            Assert.That(coordinator.HasRemotePresentation, Is.True);
            ReceiveEnd(next.AttackEventId);
            Assert.That(coordinator.HasRemotePresentation, Is.False);
            Apply(next);
            Assert.That(coordinator.HasRemotePresentation, Is.False);
            Assert.That(coordinator.ReplicaSpawnCount, Is.EqualTo(2));
        }

        [Test]
        public void ExpiredBaselineClearsSuspendedRecoveryWithoutAllocatingSourceEffects()
        {
            NetworkUltimatePresentationSpawn spawn = Spawn(7);
            Apply(spawn);
            coordinator.enabled = false;
            SetTime(106d);
            coordinator.enabled = true;
            Apply(spawn);
            Assert.That(Replica, Is.Null);
            Assert.That(Field<ulong>(coordinator, "suspendedRemoteRootId"), Is.Zero);
            Assert.That(coordinator.ReplicaSpawnCount, Is.EqualTo(1));
            NetworkUltimatePresentationSpawn next = Spawn(8);
            next.EventNetworkTime = 105.5d;
            Apply(next);
            Assert.That(coordinator.HasRemotePresentation, Is.True);
            Assert.That(coordinator.ReplicaSpawnCount, Is.EqualTo(2));
        }

        private static NetworkUltimatePresentationSpawn Spawn(uint sequence) => new NetworkUltimatePresentationSpawn
        {
            SourcePlayerId = 42, UltimateId = 0, AttackEventId = CombatEventId.Compose(1, 1, sequence).Value,
            EventNetworkTime = 99.5d,
            Stats = new ProjectilePresentationStats { Duration = 1, EffectiveSpeed = 1, ProjectileCount = 1, BaseProjectileCount = 1 }
        };
        private void Apply(NetworkUltimatePresentationSpawn spawn) =>
            typeof(NetworkPlayerUltimate).GetMethod("ApplyRemotePresentation", Private).Invoke(coordinator, new object[] { spawn });
        private void ReceiveEnd(ulong root)
        {
            // Exercise the woven client receiver body, avoiding a fake server or a call to the RPC send wrapper.
            MethodInfo receiver = typeof(NetworkPlayerUltimate).GetMethods(Private)
                .Single(method => method.Name.StartsWith("UserCode_RpcEndPresentation", StringComparison.Ordinal));
            receiver.Invoke(coordinator, new object[] { root });
        }
        private static void SetTime(double time) => Timeline.SetValue(null, time);
        private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Private).GetValue(value);
        private static void SetField(object value, string name, object field) => value.GetType().GetField(name, Private).SetValue(value, field);
    }
}
#endif
