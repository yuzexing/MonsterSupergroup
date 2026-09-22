using System.Collections;
using System.Reflection;
using Mirror;
using Mirror.FizzySteam;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class SteamLobbyCleanupPlayModeTests
    {
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator AcceptWhileConnecting_WaitsForRealDisconnectBeforeStartingNewJoin()
        {
            NetworkManager.ResetStatics();
            var root = new GameObject("Steam invitation asynchronous cleanup"); root.SetActive(false);
            var transport = root.AddComponent<HangingFizzyTransport>();
            var manager = root.AddComponent<BootGameplayNetworkManager>();
            var service = root.AddComponent<SteamLobbyService>();
            manager.transport = transport; service.Configure(manager, transport); root.SetActive(true);
            int joins = 0;
            service.ConfigureJoinForTests(_ => { Assert.That(manager.mode, Is.EqualTo(NetworkManagerMode.Offline)); joins++; });
            try
            {
                manager.networkAddress = "76561198000000001"; manager.StartClient();
                SetPrivateField(service, "networkObserved", true);
                SetPrivateField(service, "<State>k__BackingField", SteamLobbyState.ConnectingClient);
                service.AcceptInvitation(109775242349881186);
                yield return null;
                Assert.That(joins, Is.Zero);
                Assert.That(service.CanStartOperation, Is.False);
                Assert.That(manager.mode, Is.EqualTo(NetworkManagerMode.ClientOnly));
                transport.CompleteDisconnect();
                for (int i = 0; i < 5 && joins == 0; i++) yield return null;
                Assert.That(joins, Is.EqualTo(1));
                Assert.That(manager.IsLeavingRoom, Is.False);
            }
            finally
            {
                NetworkClient.Shutdown(); NetworkServer.Shutdown(); NetworkManager.ResetStatics();
                Object.DestroyImmediate(root);
            }
        }
#endif

        [UnityTest]
        public IEnumerator LeaveWhileConnecting_WaitsForMirrorDisconnect()
        {
            NetworkManager.ResetStatics();
            GameObject root = new GameObject("Steam Lobby Cleanup Test");
            root.SetActive(false);
            var transport = root.AddComponent<HangingFizzyTransport>();
            var manager = root.AddComponent<BootGameplayNetworkManager>();
            var service = root.AddComponent<SteamLobbyService>();
            manager.transport = transport;
            service.Configure(manager, transport);
            root.SetActive(true);

            try
            {
                manager.networkAddress = "76561198000000001";
                manager.StartClient();
                Assert.That(NetworkClient.isConnecting, Is.True);
                Assert.That(manager.mode, Is.EqualTo(NetworkManagerMode.ClientOnly));
                SetPrivateField(service, "networkObserved", true);

                service.LeaveAndStop();
                Assert.That(service.State, Is.EqualTo(SteamLobbyState.Leaving));
                Assert.That(manager.mode, Is.EqualTo(NetworkManagerMode.ClientOnly));

                transport.CompleteDisconnect();
                yield return null;

                Assert.That(NetworkClient.active, Is.False);
                Assert.That(manager.mode, Is.EqualTo(NetworkManagerMode.Offline));
                Assert.That(service.State, Is.EqualTo(SteamLobbyState.Idle));
            }
            finally
            {
                NetworkClient.Shutdown();
                NetworkServer.Shutdown();
                NetworkManager.ResetStatics();
                Object.DestroyImmediate(root);
            }
        }

        private sealed class HangingFizzyTransport : FizzySteamworks
        {
            public override void ClientConnect(string address)
            {
            }

            public override void ClientDisconnect()
            {
            }

            public override void Shutdown()
            {
            }

            public void CompleteDisconnect()
            {
                OnClientDisconnected?.Invoke();
            }
        }

        private static void SetPrivateField<T>(
            SteamLobbyService service,
            string fieldName,
            T value)
        {
            FieldInfo field = typeof(SteamLobbyService).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(service, value);
        }
    }
}
