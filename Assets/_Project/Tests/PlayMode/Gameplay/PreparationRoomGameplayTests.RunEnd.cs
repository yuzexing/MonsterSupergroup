using System.Collections;
using System.Linq;
using System.Reflection;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class PreparationRoomGameplayTests
    {
        [UnityTest]
        public IEnumerator Wipe_CleanupTimeoutClosesSession_ThenCanCreateAnotherRoom()
        {
            yield return Open(); manager.StartPreparedGame();
            yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame);
            yield return null;
            NetworkClient.localPlayer.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
            yield return Until(() => manager.IsRunEndScreen);
            RunCleanupReady captured = default;
            NetworkServer.RegisterHandler<RunCleanupReady>((client, message) => captured = message);
            manager.ChooseRunEndAction(RunEndAction.Restart);
            yield return Until(() => captured.Round != 0);
            typeof(BootGameplayNetworkManager).GetField("cleanupDeadline", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, Time.realtimeSinceStartupAsDouble - 1);
            yield return Until(() => !NetworkServer.active && !NetworkClient.active && !manager.IsLeavingRoom && !manager.IsGameplayLoaded);
            StringAssert.Contains("清理超过 120 秒", manager.MenuNotice);
            yield return Open();
            Assert.That(manager.RoomSnapshot.Phase, Is.EqualTo(PreparationPhase.Preparing));
        }

        [UnityTest]
        public IEnumerator Wipe_RestartThreeTimes_ThenReturnToSameParty()
        {
            yield return Open();
            ulong id = manager.RoomSnapshot.SelfId;
            var connection = NetworkClient.connection;
            manager.SetOwnLoadout(402);
            yield return Until(() => manager.RoomSnapshot.Members[0].WeaponId == 402);
            manager.StartPreparedGame();
            uint oldAvatar = 0;
            for (int i = 0; i < 4; i++)
            {
                yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame);
                yield return null;
                var owner = NetworkClient.localPlayer; string run = manager.Session.RunId;
                Assert.That(owner.netId, Is.Not.EqualTo(oldAvatar)); oldAvatar = owner.netId;
                Assert.That(manager.RoomSnapshot.SelfId, Is.EqualTo(id));
                Assert.That(NetworkClient.connection, Is.SameAs(connection));
                var health = owner.GetComponent<CombatantBehaviour>();
                Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
                var build = owner.GetComponent<PlayerBuildRuntime>().CaptureState();
                Assert.That(build.InitialWeaponId, Is.EqualTo(402)); Assert.That(build.Weapons.Length, Is.EqualTo(1));
                Assert.That(build.Equipment, Is.Empty); Assert.That(build.Perks, Is.Empty);
                foreach (var spawner in Object.FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None)) spawner.enabled = false;
                if (i == 0)
                {
                    var progression = owner.GetComponent<NetworkModifierSelection>();
                    Assert.That(progression.TryGrantExperience(progression.ExperiencePerLevel), Is.True);
                    yield return Until(() => progression.PendingEventId != 0 && progression.ServerOffers.Count > 0);
                    for (int step = 0; step < 3 && progression.PendingEventId != 0; step++)
                    {
                        Assert.That(progression.ServerSelect(NetworkServer.localConnection, progression.PendingEventId, 0, out var error), Is.True, error);
                        yield return null;
                    }
                    var grown = owner.GetComponent<PlayerBuildRuntime>().CaptureState();
                    Assert.That(grown.Weapons.Length + grown.Equipment.Length + grown.Perks.Length, Is.GreaterThan(1), "The reset test must begin with an upgraded build.");
                    Assert.That(progression.PendingEventId, Is.Zero);
                }
                var menu = Object.FindFirstObjectByType<NetworkGameplayMenuController>(); menu.OpenMenu();
                health.ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
                yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.GameOver);
                yield return null;
                Assert.That(menu.IsOpen, Is.False); menu.HandleEscape(); Assert.That(menu.IsOpen, Is.False);
                Assert.That(NetworkCombatWorld.Instance.Gateway.CombatStopped, Is.True);
                manager.ChooseRunEndAction(i < 3 ? RunEndAction.Restart : RunEndAction.ReturnToRoom);
                yield return Until(() => manager.Session.RunId != run);
            }
            yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing);
            Assert.That(NetworkClient.localPlayer, Is.Null); Assert.That(manager.IsGameplayLoaded, Is.False);
            Assert.That(NetworkClient.connection, Is.SameAs(connection));
            Assert.That(manager.RoomSnapshot.Members[0].Ready, Is.False);
            Assert.That(manager.RoomSnapshot.Members[0].WeaponId, Is.EqualTo(402));
        }

        [UnityTest]
        public IEnumerator Wipe_DelayedCleanupBlocksRestart_StaleAckCannotReleaseIt()
        {
            yield return Open(); manager.StartPreparedGame();
            yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame);
            yield return null;
            var world = NetworkCombatWorld.Instance;
            var oldGateway = world.Gateway;
            string run = manager.Session.RunId;
            NetworkClient.localPlayer.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
            yield return Until(() => manager.IsRunEndScreen);
            RunCleanupReady captured = default;
            NetworkConnectionToClient sender = null;
            NetworkServer.RegisterHandler<RunCleanupReady>((client, message) => { sender = client; captured = message; });
            manager.ChooseRunEndAction(RunEndAction.Restart);
            yield return Until(() => captured.Round != 0);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(manager.Session.RunId, Is.EqualTo(run));
            Assert.That(manager.ServerRoom.Phase, Is.EqualTo(PreparationPhase.Transitioning));
            var receive = typeof(BootGameplayNetworkManager).GetMethod("ReceiveCleanupReady", BindingFlags.Instance | BindingFlags.NonPublic);
            receive.Invoke(manager, new object[] { sender, new RunCleanupReady { RunId = "old", Round = captured.Round } });
            yield return null; Assert.That(manager.Session.RunId, Is.EqualTo(run));
            receive.Invoke(manager, new object[] { sender, captured });
            yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame);
            Assert.That(world.Gateway, Is.SameAs(oldGateway));
            Assert.That(world.Gateway.CombatStopped, Is.False);
            var apply = typeof(NetworkCombatWorld).GetMethod("ApplyCanonicalForRound", BindingFlags.Instance | BindingFlags.NonPublic);
            apply.Invoke(world, new object[] { new CanonicalWorldBatch { Entities = new[] {
                new CanonicalEntityState { EntityId = 999999, MaxHealth = 100, StateVersion = 99 }
            } }, captured.Round });
            Assert.That(world.Replica.TryGetEntity(999999, out _), Is.False, "Old round repopulated the canonical cache.");
            Assert.That(world.Gateway.Ledger.GetAllStates().Any(s => s.Kind == (byte)CombatEntityKind.Player && s.Alive), Is.True);
        }
    }
}
