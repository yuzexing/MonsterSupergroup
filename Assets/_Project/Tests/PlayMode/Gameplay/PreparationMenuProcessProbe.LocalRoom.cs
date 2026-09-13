#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class PreparationMenuProcessProbe
    {
        private InputField LocalPort => FindObjectsByType<InputField>(FindObjectsSortMode.None).Single(f => f.name == "Local KCP Port");
        private ushort LocalTestPort => ushort.Parse(Arg("--menu-port=") ?? "7777");
        private static IEnumerator WaitLocalButton(string name) => Wait(() => FindObjectsByType<Button>(FindObjectsSortMode.None)
            .Any(b => b.name == name && b.isActiveAndEnabled && b.interactable), "local button " + name);

        private IEnumerator ConnectLocalRoomUi(bool host)
        {
            yield return Wait(() => manager.CanStartLocalRoom && FindObjectsByType<InputField>(FindObjectsSortMode.None).Any(f => f.name == "Local KCP Port"), "local controls");
            LocalPort.text = LocalTestPort.ToString();
            yield return WaitLocalButton(host ? "创建本地主机" : "加入本地主机");
            Click(host ? "创建本地主机" : "加入本地主机");
            yield return Wait(() => manager.RoomSnapshot.SelfId != 0 && !manager.IsLocalRoomConnecting, "local room snapshot");
            Require(manager.IsKcpPreparationRoom && manager.GetComponent<SteamLobbyService>().CurrentLobbyId == 0, "Wrong room backend");
            Stage("local-ui-connected", $"port={LocalTestPort} participant={manager.RoomSnapshot.SelfId} steamInitialized={manager.GetComponent<SteamLobbyService>().IsSteamInitialized}");
        }

        private IEnumerator LocalRoomScenario(NetworkBackendBootstrap backend)
        {
            if (profile == "local-release")
            {
                Require(!BootGameplayNetworkManager.LocalPreparationAvailable, "Release exposed development controls");
                Require(!FindObjectsByType<InputField>(FindObjectsSortMode.None).Any(f => f.name == "Local KCP Port"), "Release displayed local controls");
                Require(!manager.TryCreateLocalPreparationRoom(LocalTestPort, out _) && !manager.TryJoinLocalPreparationRoom(LocalTestPort, out _), "Release accepted a local menu operation");
                yield return Shot("release-home"); yield break;
            }
            Require(BootGameplayNetworkManager.LocalPreparationAvailable, "Development controls missing");
            if (profile == "local-errors") { yield return LocalRoomErrors(); yield break; }
            if (profile == "local-admission") { yield return LocalRoomAdmission(); yield break; }
            yield return RunEndScenario(backend);
        }

        private IEnumerator LocalRoomErrors()
        {
            var field = LocalPort;
            field.text = "65536"; field.Select(); field.ActivateInputField(); yield return null;
            manager.ShowMenuNotice("输入保持测试"); yield return null; yield return null;
            Require(LocalPort == field && field.text == "65536", "Port editor was rebuilt");
            Click("创建本地主机");
            Require(!NetworkServer.active && !NetworkClient.active && manager.MenuNotice.Contains("端口必须"), "Invalid port started a session");

            field.text = LocalTestPort.ToString(); Click("加入本地主机");
            yield return Wait(() => !manager.IsLocalRoomConnecting && !string.IsNullOrEmpty(manager.MenuNotice), "no host rejection");
            yield return WaitClean(); yield return Shot("local-no-host");

            using (var occupied = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp))
            {
                occupied.DualMode = true; occupied.ExclusiveAddressUse = true;
                occupied.Bind(new IPEndPoint(IPAddress.IPv6Any, LocalTestPort));
                field.text = LocalTestPort.ToString(); Click("加入本地主机");
                Click("取消连接"); yield return WaitClean();
                Require(manager.MenuNotice.Contains("已取消"), "Cancel lost its result");
                yield return WaitLocalButton("创建本地主机");
                Click("创建本地主机"); yield return WaitClean();
                Require(manager.MenuNotice.Contains("端口已被占用"), "Occupied port was not rejected");
                yield return Shot("local-port-occupied");
            }
            yield return ConnectLocalRoomUi(true);
            Require(manager.RoomSnapshot.Members.Length == 1, "Failure recovery created duplicate members");
            manager.LeavePreparationRoom(); yield return WaitClean();
            yield return WaitLocalButton("创建本地主机");
            Require(LocalPort.text == LocalTestPort.ToString(), "Return forgot the selected port");
            Stage("local-errors-complete");
        }

        private IEnumerator LocalRoomAdmission()
        {
            if (role == "fifth")
            {
                yield return Wait(() => Seen("four-seats"), "four seats");
                LocalPort.text = LocalTestPort.ToString(); Click("加入本地主机");
                yield return WaitClean(); Require(manager.MenuNotice.Contains("房间已满"), "Missing full-room reason");
                Mark("fifth-rejected");
                yield return Wait(() => Seen("local-loading"), "loading barrier");
                yield return WaitLocalButton("加入本地主机");
                Click("加入本地主机"); yield return WaitClean();
                Require(manager.MenuNotice.Contains("房间正在加载"), "Loading accepted a new member");
                Mark("loading-rejected"); yield return Wait(() => Seen("local-admission-done"), "host completion");
                yield break;
            }
            yield return ConnectLocalRoomUi(role == "host");
            if (role == "host") Mark("host-open");
            yield return Wait(() => manager.RoomSnapshot.Members.Length == 4, "four member room");
            yield return WaitLocalButton("准备");
            Click("准备");
            if (role == "host")
            {
                Mark("four-seats"); yield return Shot("local-four-seats");
                yield return Wait(() => Seen("fifth-rejected") && manager.RoomSnapshot.Members.All(m => m.Ready), "all ready");
                var pending = new List<(NetworkConnectionToClient connection, GameplayReady message)>();
                var receive = typeof(BootGameplayNetworkManager).GetMethod("ReceiveGameplayReady", BindingFlags.Instance | BindingFlags.NonPublic);
                NetworkServer.RegisterHandler<GameplayReady>((connection, message) => pending.Add((connection, message)));
                yield return WaitLocalButton("开始游戏");
                Click("开始游戏"); yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Loading, "loading");
                Mark("local-loading");
                yield return Wait(() => Seen("loading-rejected"), "loading join rejection");
                NetworkServer.RegisterHandler<GameplayReady>((connection, message) => receive.Invoke(manager, new object[] { connection, message }));
                foreach (var item in pending) receive.Invoke(manager, new object[] { item.connection, item.message });
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame, "combat");
                manager.LeavePreparationRoom(); yield return WaitClean(); Mark("local-admission-done");
            }
            else { yield return Wait(() => Seen("local-admission-done"), "host exit"); yield return WaitClean(); }
            Stage("local-admission-complete");
        }
    }
}
#endif
