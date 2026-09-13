using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class PreparationRoomGameplayTests
    {
        private static ushort FreeLocalPort()
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
        }
        private IEnumerator LocalClean() => Until(() => manager.mode == NetworkManagerMode.Offline &&
            !NetworkClient.active && !NetworkServer.active && !manager.IsLeavingRoom && !manager.IsGameplayTransitioning);

        [UnityTest]
        public IEnumerator LocalHost_AfterOfflineSolo_ListensAndUsesTheSamePreparationFlow()
        {
            yield return Open(); manager.LeavePreparationRoom(); yield return LocalClean();
            Assert.That(manager.TryCreateLocalPreparationRoom(0, out _), Is.False);
            ushort port = FreeLocalPort();
            Assert.That(manager.TryCreateLocalPreparationRoom(port, out var error), Is.True, error);
            yield return Until(() => manager.RoomSnapshot.SelfId != 0 && !manager.IsLocalRoomConnecting);
            Assert.That(manager.IsKcpPreparationRoom, Is.True);
            Assert.That(NetworkServer.listen && manager.transport.ServerActive(), Is.True);
            Assert.That(NetworkClient.localPlayer, Is.Null);
            Assert.That(manager.IsGameplayLoaded, Is.False);
            Assert.That(manager.GetComponent<SteamLobbyService>().CurrentLobbyId, Is.Zero);
            manager.LeavePreparationRoom(); yield return LocalClean();
            Assert.That(manager.LocalRoomPort, Is.EqualTo(port));
            Assert.That(manager.transport.ServerActive(), Is.False);
        }

        [UnityTest]
        public IEnumerator LocalHost_OccupiedPortCleansMirrorSetup_ThenCanRetry()
        {
            yield return manager.EnsureMainMenu();
            using (var occupied = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp))
            {
                occupied.DualMode = true; occupied.ExclusiveAddressUse = true;
                occupied.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
                ushort port = (ushort)((IPEndPoint)occupied.LocalEndPoint).Port;
                Assert.That(manager.TryCreateLocalPreparationRoom(port, out var error), Is.False);
                StringAssert.Contains("端口已被占用", error);
                yield return LocalClean();
            }
            Assert.That(manager.TryCreateLocalPreparationRoom(FreeLocalPort(), out var retryError), Is.True, retryError);
            yield return Until(() => manager.RoomSnapshot.SelfId != 0 && !manager.IsLocalRoomConnecting);
        }

        [UnityTest]
        public IEnumerator LocalJoin_CancelAndTimeout_AllowRetryWithoutStaleOperation()
        {
            yield return manager.EnsureMainMenu();
            Assert.That(manager.TryJoinLocalPreparationRoom(FreeLocalPort(), out _), Is.True);
            Assert.That(manager.TryCreateLocalPreparationRoom(FreeLocalPort(), out _), Is.False);
            manager.CancelLocalRoomConnection(); yield return LocalClean();
            StringAssert.Contains("已取消", manager.MenuNotice);
            using (var blackhole = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                Assert.That(manager.TryJoinLocalPreparationRoom((ushort)((IPEndPoint)blackhole.Client.LocalEndPoint).Port, out _), Is.True);
                typeof(BootGameplayNetworkManager).GetField("localRoomDeadline", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, Time.realtimeSinceStartupAsDouble - 1);
                yield return Until(() => !manager.IsLocalRoomConnecting); yield return LocalClean();
            }
            StringAssert.Contains("超时", manager.MenuNotice);
            Assert.That(manager.CanStartLocalRoom, Is.True);
        }

        [UnityTest]
        public IEnumerator LocalHost_RequiresRoomSnapshotBeyondTransportConnection()
        {
            yield return manager.EnsureMainMenu();
            Assert.That(manager.TryCreateLocalPreparationRoom(FreeLocalPort(), out _), Is.True);
            NetworkClient.RegisterHandler<PreparationRoomSnapshot>(_ => { });
            yield return Until(() => NetworkClient.isConnected);
            yield return null; yield return null;
            Assert.That(manager.IsLocalRoomConnecting, Is.True);
            Assert.That(manager.RoomSnapshot.SelfId, Is.Zero);
            manager.CancelLocalRoomConnection(); yield return LocalClean();
        }

        [UnityTest]
        public IEnumerator LocalHome_PortEditingSurvivesNotices_RejectsInvalidPort()
        {
            yield return manager.EnsureMainMenu(); yield return null;
            yield return Until(() => Object.FindObjectsByType<InputField>(FindObjectsSortMode.None).Any(f => f.name == "Local KCP Port"));
            yield return null;
            var field = Object.FindObjectsByType<InputField>(FindObjectsSortMode.None).Single(f => f.name == "Local KCP Port");
            Canvas.ForceUpdateCanvases();
            var bounds = (RectTransform)field.transform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, bounds.TransformPoint(bounds.rect.center)) };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Any(hit => hit.gameObject == field.gameObject), Is.True,
                $"Mouse cannot reach port: position={pointer.position}, depth={field.GetComponent<Image>().depth}, screen={Screen.width}x{Screen.height}, hits={hits.Count}, raycast={field.GetComponent<Image>().raycastTarget}");
            field.text = "65536"; field.Select(); field.ActivateInputField(); yield return null;
            manager.ShowMenuNotice("test notice"); yield return null; yield return null;
            Assert.That(Object.FindObjectsByType<InputField>(FindObjectsSortMode.None).Single(f => f.name == "Local KCP Port"), Is.SameAs(field));
            Assert.That(field.text, Is.EqualTo("65536"));
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(field.gameObject));
            Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == "创建本地主机").onClick.Invoke();
            Assert.That(NetworkServer.active || NetworkClient.active, Is.False);
            StringAssert.Contains("端口必须", manager.MenuNotice);
        }

        [UnityTest]
        public IEnumerator LocalHost_SteamDisconnectDoesNotStopKcp()
        {
            yield return manager.EnsureMainMenu();
            Assert.That(manager.TryCreateLocalPreparationRoom(FreeLocalPort(), out _), Is.True);
            yield return Until(() => manager.RoomSnapshot.SelfId != 0 && !manager.IsLocalRoomConnecting);
            var callback = typeof(SteamLobbyService).GetMethod("HandleSteamDisconnected", BindingFlags.Instance | BindingFlags.NonPublic);
            callback.Invoke(manager.GetComponent<SteamLobbyService>(), new[] { System.Activator.CreateInstance(callback.GetParameters()[0].ParameterType) });
            yield return null; yield return null;
            Assert.That(NetworkServer.active && NetworkClient.isConnected && manager.transport.ServerActive(), Is.True);
            Assert.That(manager.RoomSnapshot.Phase, Is.EqualTo(PreparationPhase.Preparing));
            Assert.That(manager.MenuNotice, Is.Empty);
        }
    }
}
