#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class PreparationMenuProcessProbe
    {
        private NetworkGameplayMenuController CombatMenu => FindFirstObjectByType<NetworkGameplayMenuController>();
        private IEnumerator CombatMenuRun(NetworkBackendBootstrap backend)
        {
            if (profile == "combat-menu-solo")
            {
                Require(manager.TryStartOfflineRoom(out string error), error);
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "solo room");
                yield return SelectAndLaunchSolo(); yield return null;
                yield return GameplayMenuUiScenario.Run(manager, Shot, true);
                CombatMenu.OpenMenu(); CombatMenu.RequestExit(); CombatMenu.ConfirmExit(); CombatMenu.ConfirmExit();
                yield return WaitClean();
                Require(!GameplayMenuInput.IsOpen, "Exit retained menu input lock");
                yield return Shot("combat-return-home");
                Stage("combat-menu-solo-complete"); yield break;
            }
            Require(backend.TryPrepareKcp("127.0.0.1", ushort.Parse(Arg("--menu-port=") ?? "7998"), false, out string backendError), backendError);
            if (role == "host") { NetworkServer.listen = true; manager.StartHost(); Mark("host-open"); }
            else manager.StartClient();
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing && manager.RoomSnapshot.SelfId != 0, "party room");
            manager.SetOwnLoadout(2);
            yield return Wait(() => Self().WeaponId == 2, "weapon");
            manager.SetOwnReady(true);
            if (role == "host")
            {
                yield return Wait(() => manager.RoomSnapshot.Members.Length == 3 && manager.RoomSnapshot.Members.All(m => m.Ready), "three ready");
                manager.StartPreparedGame();
            }
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame && Owner != null, "party gameplay");
            if (role == "host") foreach (var spawner in FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None)) spawner.enabled = false;
            yield return null;
            yield return GameplayMenuUiScenario.Run(manager, null, false);
            Mark("ui-checked-" + role, Owner.netId.ToString());
            CombatMenu.OpenMenu(); Mark("menu-open-" + role);
            if (role == "host")
            {
                yield return Wait(() => Seen("menu-open-a") && Seen("menu-open-b"), "all menus open");
                var world = NetworkEnemySimulationWorld.Instance;
                var prefab = FindFirstObjectByType<NetworkGameplayEnemySpawner>(FindObjectsInactive.Include).EnemyPrefab;
                var actors = new List<NetworkEnemySimulationAgent>();
                foreach (string participant in new[] { "host", "a", "b" })
                {
                    uint id = uint.Parse(Read("ui-checked-" + participant));
                    var instance = Instantiate(prefab, NetworkServer.spawned[id].transform.position + Vector3.right * 14, Quaternion.identity);
                    var agent = instance.GetComponent<NetworkEnemySimulationAgent>();
                    agent.ConfigureRuntimeMinimumHealthOverride(100000);
                    agent.ConfigureInitialServerTarget(id); NetworkServer.Spawn(instance); actors.Add(agent);
                }
                yield return Wait(() => actors.All(a => world.TryReadHandoff(a.netId, out var h) && !h.AwaitingFirstSnapshot), "all first frames");
                var before = new Dictionary<uint, EnemySimulationSnapshot>();
                foreach (var actor in actors) { Require(world.Registry.TryGetLatestSnapshot(actor.netId, out var s), "Missing first snapshot"); before.Add(actor.netId, s); }
                var attackCounts = new Dictionary<uint, int>();
                foreach (var connection in NetworkServer.connections.Values)
                    if (connection.identity != null) attackCounts[connection.identity.netId] = connection.identity.GetComponent<NetworkWeaponCombatAdapter>().AcceptedCooldownReportCount;
                yield return new WaitForSecondsRealtime(3);
                foreach (var actor in actors)
                {
                    Require(world.Registry.TryGetLatestSnapshot(actor.netId, out var after), "Lost enemy snapshot");
                    var first = before[actor.netId];
                    Require(after.Sequence > first.Sequence && after.SampleNetworkTime > first.SampleNetworkTime && Vector2.Distance(after.Position, first.Position) > .01f,
                        "Menu interrupted accepted movement for " + actor.Assignment.SimulationOwnerPlayerId);
                    Stage("combat-menu-movement", $"enemy={actor.netId} owner={actor.Assignment.SimulationOwnerPlayerId} frames={after.Sequence - first.Sequence} distance={Vector2.Distance(after.Position, first.Position):F2}");
                }
                foreach (var item in attackCounts)
                {
                    int after = NetworkServer.spawned[item.Key].GetComponent<NetworkWeaponCombatAdapter>().AcceptedCooldownReportCount;
                    Require(after > item.Value, "Menu stopped automatic attacks for " + item.Key);
                    Stage("combat-menu-auto-attack", $"avatar={item.Key} accepted={after - item.Value}");
                }
                Mark("allow-client-exit");
                yield return Wait(() => Seen("a-left") && manager.Session.Participants.Count(p => p.ConnectionState == RunConnectionState.Connected) == 2, "client leaves");
                uint departed = uint.Parse(Read("ui-checked-a"));
                yield return Wait(() => actors.All(a => a == null || a.Assignment.AggroTargetPlayerId != departed), "enemy target transferred");
                Require(manager.RoomSnapshot.Phase == PreparationPhase.InGame, "Client exit ended host combat");
                CombatMenu.RequestExit(); CombatMenu.ConfirmExit();
                yield return WaitClean();
                yield return Wait(() => Seen("home-b"), "host closure reaches client");
                Stage("combat-menu-client-and-host-exit");
                NetworkServer.listen = true; manager.StartHost();
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "second room");
                Mark("second-menu-room");
                yield return Wait(() => manager.RoomSnapshot.Members.Length == 3 && manager.RoomSnapshot.Members.All(m => m.Ready || m.IsHost), "second party ready");
                manager.SetOwnReady(true);
                yield return Wait(() => manager.RoomSnapshot.Members.All(m => m.Ready), "second ready acknowledgement");
                manager.StartPreparedGame();
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame && Seen("second-open-a") && Seen("second-open-b"), "second menus");
                manager.StopHost(); yield return WaitClean();
                yield return Wait(() => Seen("drop-home-a") && Seen("drop-home-b"), "unexpected host shutdown cleanup");
                Require(manager.TryStartOfflineRoom(out var error), error);
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "restart solo");
                yield return SelectAndLaunchSolo();
                CombatMenu.OpenMenu(); CombatMenu.RequestExit(); CombatMenu.ConfirmExit(); yield return WaitClean();
                Mark("combat-menu-complete");
            }
            else
            {
                if (role == "a")
                {
                    yield return Wait(() => Seen("allow-client-exit"), "movement checks");
                    CombatMenu.RequestExit(); CombatMenu.ConfirmExit(); yield return WaitClean(); Mark("a-left");
                }
                else { yield return WaitClean(); Mark("home-b"); }
                Require(!GameplayMenuInput.IsOpen, "Returning home retained input lock");
                yield return Wait(() => Seen("second-menu-room"), "second room");
                manager.StartClient();
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "second join");
                manager.SetOwnReady(true);
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame, "second game");
                CombatMenu.OpenMenu(); Mark("second-open-" + role);
                yield return WaitClean();
                Require(!GameplayMenuInput.IsOpen, "Host drop retained menu lock"); Mark("drop-home-" + role);
                yield return Wait(() => Seen("combat-menu-complete"), "host solo restart");
            }
            Stage("combat-menu-party-complete");
        }
    }
}
#endif
