#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System.Collections;
using System.Linq;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class PreparationMenuProcessProbe
    {
        private IEnumerator RunEndScenario(NetworkBackendBootstrap backend)
        {
            bool local = profile.StartsWith("local-");
            bool solo = profile == "run-end-solo" || profile == "local-solo";
            timeout = Time.realtimeSinceStartup + 360;
            if (local)
            {
                yield return ConnectLocalRoomUi(role == "host");
                if (role == "host") Mark("host-open");
            }
            else if (solo) Require(manager.TryStartOfflineRoom(out var error), error);
            else
            {
                Require(backend.TryPrepareKcp("127.0.0.1", ushort.Parse(Arg("--menu-port=") ?? "7998"), false, out var error), error);
                if (role == "host") { NetworkServer.listen = true; manager.StartHost(); Mark("host-open"); }
                else manager.StartClient();
            }
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing && manager.RoomSnapshot.SelfId != 0, "initial room");
            ulong participantId = manager.RoomSnapshot.SelfId;
            if (local)
            {
                Require(manager.IsKcpPreparationRoom && manager.GetComponent<SteamLobbyService>().CurrentLobbyId == 0, "Local UI created a Steam/offline room");
                Require(Owner == null && !manager.IsGameplayLoaded, "Local UI bypassed preparation");
                if (role == "host") yield return Shot("local-room");
            }
            int seat = Self().Seat;
            var connection = NetworkClient.connection;
            uint previousAvatar = 0; ushort previousEpoch = 0; string previousRun = null;
            uint[] weapons = { 1, 2, 3, 6, 8, 402 };
            int cycles = solo ? weapons.Length : 5;
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                if (manager.RoomSnapshot.Phase == PreparationPhase.Preparing)
                {
                    uint weapon = solo ? weapons[cycle] : 2;
                    manager.SetOwnLoadout(weapon);
                    yield return Wait(() => Self().WeaponId == weapon, "loadout");
                    if (local) { if (!solo) { yield return WaitLocalButton("准备"); Click("准备"); } }
                    else manager.SetOwnReady(true);
                    if (role == "host")
                    {
                        yield return Wait(() => manager.RoomSnapshot.Members.Length == (solo ? 1 : 3) &&
                            (solo || manager.RoomSnapshot.Members.All(m => m.Ready)), "party ready");
                        if (local) { yield return WaitLocalButton("开始游戏"); Click("开始游戏"); } else manager.StartPreparedGame();
                    }
                }
                yield return WaitForRunEndRound(cycle);
                yield return null;
                Require(ReferenceEquals(connection, NetworkClient.connection), "Round transition reconnected the transport");
                Require(manager.RoomSnapshot.SelfId == participantId && Self().Seat == seat, "Round changed participant or seat");
                var participant = Owner.GetComponent<NetworkRunParticipant>();
                ushort epoch = Owner.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;
                Require(Owner.netId != previousAvatar && epoch != previousEpoch && participant.RunId != previousRun, "Round reused avatar, epoch or RunId");
                previousAvatar = Owner.netId; previousEpoch = epoch; previousRun = participant.RunId;
                var health = Owner.GetComponent<CombatantBehaviour>();
                var progression = Owner.GetComponent<NetworkModifierSelection>();
                Require(health.IsAlive && health.CurrentHealth == health.MaxHealth, "New round retained damage/death");
                Require(progression.Level == 1 && progression.Experience == 0 && !progression.IsSelecting, "New round retained XP/rewards");
                Require(Owner.GetComponent<PlayerBuildRuntime>().InitialWeaponId == (solo ? weapons[cycle] : 2), "Wrong initial weapon");
                var buildState = Owner.GetComponent<PlayerBuildRuntime>().CaptureState();
                Require(buildState.Weapons.Length == 1 && buildState.Equipment.Length == 0 && buildState.Perks.Length == 0, "Old build survived restart");
                Require(Owner.GetComponent<NetworkPlayerDash>().PendingOwnerUseCount == 0, "Old dash requests survived restart");
                Require(!GameplayMenuInput.IsOpen && !Owner.GetComponent<PlayerMovement>().IsMenuInputBlocked, "New round retained menu lock");
                if (role == "host")
                {
                    foreach (var spawner in FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None)) spawner.enabled = false;
                    Require(manager.Session.Participants.All(p => p.Checkpoint == null), "Old checkpoint retained");
                    Require(NetworkExperienceWorld.Current.RunId == previousRun, "XP world did not enter the new run");
                }
                Stage("run-end-fresh-round", $"cycle={cycle} participant={participantId} avatar={Owner.netId} epoch={epoch} run={previousRun}");
                Mark($"fresh-{cycle}-{role}", Owner.netId.ToString());
                if (role == "host" && !solo)
                {
                    yield return Wait(() => Seen($"fresh-{cycle}-a") && Seen($"fresh-{cycle}-b"), "all fresh avatars");
                    yield return CheckRunEndMovement(cycle);
                }
                if (role == "host") Mark("damage-" + cycle);
                yield return Wait(() => Seen("damage-" + cycle), "damage permission");
                // Last cycle: an alive disconnected member must not prevent the remaining party's defeat.
                if (!solo && cycle == cycles - 1 && role == "a")
                {
                    manager.LeavePreparationRoom(); yield return WaitClean(); Mark("alive-left");
                    yield return Wait(() => Seen("run-end-complete"), "host completion");
                    yield break;
                }
                if (role == "host") Require(progression.TryGrantExperience(1), "New round XP grant failed");
                CombatMenu.OpenMenu();
                if (role == "host" && !solo)
                {
                    yield return new WaitForSecondsRealtime(.3f);
                    Require(manager.RoomSnapshot.Phase == PreparationPhase.InGame, "Ended while host was alive");
                    if (cycle == cycles - 1) yield return Wait(() => Seen("alive-left"), "alive member leaves");
                    if (local && cycle == 0) yield return Wait(() => Seen("local-downed-rejoined"), "downed local reconnect");
                }
                health.ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
                if (local && !solo && role == "a" && cycle == 0)
                {
                    yield return Wait(() => NetworkCombatWorld.Instance.Replica.TryGetEntity(Owner.netId, out var state) && !state.Alive, "confirmed local Downed");
                    manager.LeavePreparationRoom(); yield return WaitClean();
                    yield return ConnectLocalRoomUi(false);
                    yield return Wait(() => Owner != null && !Owner.GetComponent<CombatantBehaviour>().IsAlive &&
                        Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline, "restored local Downed");
                    Require(manager.RoomSnapshot.SelfId == participantId, "Local reconnect changed participant");
                    var movement = Owner.GetComponent<PlayerMovement>(); var body = Owner.GetComponent<Rigidbody2D>();
                    Vector2 position = body.position;
                    for (int frame = 0; frame < 8; frame++) { movement.SetDirection(Vector2.right); movement.Dash(); yield return new WaitForFixedUpdate(); }
                    Require(Vector2.Distance(position, body.position) < .01f, "Reconnected Downed player moved");
                    connection = NetworkClient.connection; Mark("local-downed-rejoined");
                }
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.GameOver, "all-down game over");
                yield return null;
                Require(!CombatMenu.IsOpen, "Game over did not replace Esc menu");
                CombatMenu.HandleEscape(); Require(!CombatMenu.IsOpen, "Esc reopened the ended game");
                Require(FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text == "游戏结束" || t.text == "Game Over"), "End title not visible");
                if (!solo && role != "host")
                {
                    manager.ChooseRunEndAction(RunEndAction.Restart);
                    yield return Wait(() => manager.MenuNotice.Contains("只有房主"), "client action rejection");
                    Require(manager.RoomSnapshot.Phase == PreparationPhase.GameOver, "Client controlled the round transition");
                    if (role == "a" && cycle == 1)
                    {
                        manager.StopClient(); yield return WaitClean();
                        if (local) yield return ConnectLocalRoomUi(false); else manager.StartClient();
                        yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.GameOver, "ended reconnect");
                        Require(Owner == null && !manager.IsGameplayLoaded && manager.RoomSnapshot.SelfId == participantId,
                            "Ended reconnect restored a combat avatar or lost identity");
                        connection = NetworkClient.connection;
                        Stage("run-end-reconnect-without-avatar");
                    }
                }
                if (!solo && role == "b")
                {
                    Click("离开房间"); yield return null;
                    Require(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.name == "取消", "Exit confirmation did not default to cancel");
                    Click("取消"); yield return null;
                    Require(NetworkClient.isConnected && manager.IsRunEndScreen, "Cancel left the ended room");
                    if (cycle == cycles - 1)
                    {
                        Click("离开房间"); yield return null; Click("确认离开");
                        yield return WaitClean(); Mark($"ended-{cycle}-{role}"); Mark("ended-client-left");
                        yield return Wait(() => Seen("run-end-complete"), "host final exit");
                        Stage("run-end-confirmed-client-leave"); yield break;
                    }
                }
                Mark($"ended-{cycle}-{role}");
                if (role == "host")
                {
                    if (!solo) yield return Wait(() => Seen($"ended-{cycle}-b") && (cycle == cycles - 1 || Seen($"ended-{cycle}-a")), "all end screens");
                    yield return Shot("run-end-" + cycle);
                    if (cycle == cycles - 1)
                    {
                        manager.LeavePreparationRoom(); yield return WaitClean(); Mark("run-end-complete");
                        break;
                    }
                    bool returnRoom = solo || cycle == 3;
                    Click(returnRoom ? "回到大厅" : "重新开始");
                    // An opposite second request cannot start a second transition.
                    manager.ChooseRunEndAction(returnRoom ? RunEndAction.Restart : RunEndAction.ReturnToRoom);
                }
                if (cycle == cycles - 1)
                {
                    yield return WaitClean(); yield return Wait(() => Seen("run-end-complete"), "host exit"); break;
                }
                bool lobby = solo || cycle == 3;
                yield return Wait(() => manager.RoomSnapshot.RunId != previousRun &&
                    manager.RoomSnapshot.Phase == (lobby ? PreparationPhase.Preparing : PreparationPhase.InGame), "next round ready");
                if (lobby)
                {
                    Require(Owner == null && !manager.IsGameplayLoaded, "Lobby retained combat objects");
                    Require(!Self().Ready, "Return retained ready flag");
                    if (role == "host") yield return Shot("run-end-return-room");
                }
            }
            Stage("run-end-complete");
        }

        private IEnumerator WaitForRunEndRound(int cycle)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 60;
            double nextReport = Time.realtimeSinceStartupAsDouble + 5;
            while (manager.RoomSnapshot.Phase != PreparationPhase.InGame || Owner == null ||
                !Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline)
            {
                if (Time.realtimeSinceStartupAsDouble >= nextReport)
                {
                    var build = Owner != null ? Owner.GetComponent<PlayerBuildRuntime>() : null;
                    var participant = Owner != null ? Owner.GetComponent<NetworkRunParticipant>() : null;
                    manager.CanBeginRun(out var beginError);
                    Stage("run-end-loading", $"cycle={cycle} phase={manager.RoomSnapshot.Phase} scene={manager.IsGameplayLoaded} " +
                        $"avatar={Owner?.netId} owned={Owner?.isOwned} build={build?.IsBuildActive} weapon={build?.InitialWeaponId}/{participant?.InitialWeaponId} " +
                        $"identity={participant?.ParticipantId}/{manager.RoomSnapshot.SelfId} run={participant?.RunId}/{manager.RoomSnapshot.RunId} " +
                        $"input={Owner?.GetComponent<NetworkPlayerBootstrap>().IsLocalOwnerBound} " +
                        $"progression={Owner?.GetComponent<NetworkModifierSelection>().HasOwnerBaseline} dash={Owner?.GetComponent<NetworkPlayerDash>().HasOwnerBaseline} " +
                        $"ultimate={(Owner != null && Owner.GetComponent<NetworkPlayerUltimate>().TryReadDebugState(false, out _))} " +
                        $"health={(Owner != null && NetworkCombatWorld.Instance.Replica.TryGetEntity(Owner.netId, out _))} " +
                        $"ready={string.Join(",", manager.RoomSnapshot.Members.Select(m => m.ParticipantId + ":" + m.GameplayReady))} begin={beginError}");
                    nextReport += 5;
                }
                Require(Time.realtimeSinceStartupAsDouble < deadline, "Timed out: round " + cycle);
                yield return null;
            }
        }

        private IEnumerator CheckRunEndMovement(int cycle)
        {
            var world = NetworkEnemySimulationWorld.Instance;
            var prefab = FindFirstObjectByType<NetworkGameplayEnemySpawner>(FindObjectsInactive.Include).EnemyPrefab;
            var actors = new System.Collections.Generic.List<NetworkEnemySimulationAgent>();
            foreach (string peer in new[] { "host", "a", "b" })
            {
                uint id = uint.Parse(Read($"fresh-{cycle}-{peer}"));
                var instance = Instantiate(prefab, NetworkServer.spawned[id].transform.position + Vector3.right * 12, Quaternion.identity);
                var agent = instance.GetComponent<NetworkEnemySimulationAgent>();
                agent.ConfigureRuntimeMinimumHealthOverride(100000); agent.ConfigureInitialServerTarget(id);
                NetworkServer.Spawn(instance); actors.Add(agent);
            }
            yield return Wait(() => actors.All(a => world.Registry.TryGetLatestSnapshot(a.netId, out _)), "new simulators first frames");
            var before = actors.Select(a => { world.Registry.TryGetLatestSnapshot(a.netId, out var pose); return pose; }).ToArray();
            var attacks = NetworkServer.connections.Values.Where(c => c.identity != null).ToDictionary(c => c.identity.netId,
                c => c.identity.GetComponent<NetworkWeaponCombatAdapter>().AcceptedCooldownReportCount);
            yield return new WaitForSecondsRealtime(2.5f);
            for (int i = 0; i < actors.Count; i++)
            {
                Require(world.Registry.TryGetLatestSnapshot(actors[i].netId, out var pose) && pose.Sequence > before[i].Sequence &&
                    Vector2.Distance(pose.Position, before[i].Position) > .01f, "New round has no accepted moving snapshots");
                Stage("run-end-accepted-movement", $"cycle={cycle} enemy={actors[i].netId} frames={pose.Sequence - before[i].Sequence}");
            }
            foreach (var item in attacks)
                Require(NetworkServer.spawned[item.Key].GetComponent<NetworkWeaponCombatAdapter>().AcceptedCooldownReportCount > item.Value,
                    "New round has no accepted weapon attacks");
            // Spawned test enemies belong to Gameplay and must be removed by the real round cleanup.
        }
    }
}
#endif
