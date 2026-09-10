using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Files coordinate test phases only. Gameplay state still traverses production Mirror/GAS paths.
    public sealed class RuntimeBoundaryProcessProbe : MonoBehaviour
    {
        private string role, directory;
        private ushort port;
        private float deadline;
        private bool finished;
        private BootGameplayNetworkManager manager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            string roleArg = args.FirstOrDefault(a => a.StartsWith("--runtime-boundary-role="));
            if (roleArg == null || FindFirstObjectByType<RuntimeBoundaryProcessProbe>() != null) return;
            var probe = new GameObject("Runtime Boundary Validation").AddComponent<RuntimeBoundaryProcessProbe>();
            probe.role = roleArg.Split('=')[1];
            probe.directory = args.First(a => a.StartsWith("--runtime-boundary-sync=")).Substring(24);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--runtime-boundary-port=")).Split('=')[1]);
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            deadline = Time.realtimeSinceStartup + 150;
            SceneManager.sceneLoaded += PrepareGameplay;
            var stack = new Stack<IEnumerator>();
            stack.Push(Run());
            while (stack.Count > 0)
            {
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    next = stack.Peek().Current;
                    if (next is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    Finish(false);
                    yield break;
                }
                yield return next;
            }
            Finish(true);
        }

        private void Update()
        {
            if (!finished && Time.realtimeSinceStartup > deadline) Finish(false);
        }

        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            // Enemy combat has independent regression coverage. Keep this lifecycle fixture deterministic.
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                    { spawner.Configure(spawner.EnemyPrefab, 5); spawner.enabled = false; }
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Boot manager missing.");
            // Private in-memory definitions make a reconnect shorter than the
            // cooldown, without changing production assets or the GAS formula.
            var database = FindFirstObjectByType<RuntimeDB>();
            var weapons = Instantiate(database.WeaponDB);
            weapons.Configure(weapons.Weapons.Select(definition =>
            {
                var copy = Instantiate(definition);
                var stats = copy.BaseStats;
                stats.speed = 1f / 90f;
                copy.ConfigureNativeGas(stats, copy.AttackTags, copy.Presentation);
                return copy;
            }).ToArray());
            database.ConfigureWeaponDatabase(weapons);
            Require(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", port, false, out var error), error);
            if (role == "host") manager.StartHost();
            else if (role == "server") manager.StartServer();
            else manager.StartClient();
            if (role == "server" || role == "host")
            {
                Mark("listening");
                yield return ServerScenario();
                if (role == "host") manager.StopHost(); else manager.StopServer();
            }
            else yield return ClientScenario();
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active || NetworkServer.active) yield return null;
            yield return null; // Unity applies deferred object destruction at the end of the stop frame.
            Require(FindObjectsByType<NetworkPlayerBootstrap>(FindObjectsSortMode.None).Length == 0, "Avatar leaked after stop.");
        }

        private NetworkIdentity[] ServerPlayers() => NetworkServer.spawned.Values
            .Where(p => p != null && p.GetComponent<NetworkRunParticipant>() != null)
            .OrderBy(p => p.GetComponent<NetworkRunParticipant>().ParticipantId).ToArray();

        private IEnumerator ServerScenario()
        {
            while (ServerPlayers().Length != 2 || ServerPlayers().Any(p => !p.GetComponent<PlayerBuildRuntime>().IsBuildActive))
                yield return null;
            while (ServerPlayers().Any(p => p.GetComponent<NetworkWeaponCombatAdapter>().CaptureCooldowns().Length == 0))
                yield return null;
            manager.BeginRun();
            Require(manager.Session.IsRosterLocked && manager.Session.Participants.Count == 2, "Roster did not lock.");
            if (role == "server")
            {
                Require(GameplayRuntimeEnvironment.IsDedicatedServer && !NetworkClient.active, "Server accidentally became a client.");
                Require(FindObjectsByType<Camera>(FindObjectsSortMode.None).Length == 0, "Dedicated server has an active camera.");
                Require(FindFirstObjectByType<CombatHUDController>() == null, "Dedicated server created personal HUD.");
                Require(!FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                    .Any(c => c != null && c.GetType().FullName == "Rewired.InputManager"), "Dedicated server initialized Rewired.");
            }
            var initial = ServerPlayers();
            foreach (NetworkIdentity player in initial)
            {
                ulong id = player.GetComponent<NetworkRunParticipant>().ParticipantId;
                var build = player.GetComponent<PlayerBuildRuntime>();
                var authority = player.GetComponent<NetworkModifierSelection>();
                var damage = build.BuildDatabase.EquipmentDB.Equipments.First(e => e.name.Contains("DamageRaise"));
                build.AddEquipment(0, damage, (int)id - 1);
                PlayerProgressionSnapshot progress = authority.CaptureProgression();
                progress.BuildRevision++;
                progress.Experience = 1;
                authority.RestoreProgression(progress);
                var world = NetworkCombatWorld.Instance;
                ServerEntityCheckpoint health = world.Gateway.Ledger.CaptureEntityState(player.netId);
                var state = health.State;
                state.Health = 50 + (int)id * 10;
                var poison = new StatusInstance(new StatusInstanceId(0xFFFF000000000000UL + id),
                    new StatusDefinition(EnemyStatusID.Poison, StatusStackMode.Add, 3), player.netId, player.netId,
                    player.netId, 1, NetworkTime.time, 120, StatusExecutionAuthority.TargetOwnerClient,
                    1, 0, 120, 0, 1, 0, player.netId);
                world.RestorePlayerState(player.netId, new PlayerRuntimeCheckpoint
                {
                    PreviousAvatarId = player.netId,
                    Health = new ServerEntityCheckpoint(state, false),
                    Statuses = new[] { CanonicalStatusState.From(poison) }
                });
                authority.ServerQueueUpgrades(2);
                Require(build.EquipmentCount == 1 && build.WeaponCount == 1, "Host/server duplicate Build mutation.");
            }
            while (initial.Any(p => p.GetComponent<NetworkModifierSelection>().ServerOffers.Count == 0)) yield return null;
            if (role == "host")
            {
                yield return VerifyOwner(false);
                Mark("verified-host");
            }
            Mark("configured");
            while (!Has("verified-client") || (role == "server" && !Has("verified-client2"))) yield return null;
            var returning = initial.First(p => p.connectionToClient is not LocalConnectionToClient);
            ulong returningId = returning.GetComponent<NetworkRunParticipant>().ParticipantId;
            uint oldAvatar = returning.netId;
            ushort oldEpoch = returning.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch;
            PlayerProgressionSnapshot before = returning.GetComponent<NetworkModifierSelection>().CaptureProgression();
            var cooldownBefore = returning.GetComponent<NetworkWeaponCombatAdapter>().CaptureCooldowns().Single();
            Require(cooldownBefore.RemainingAt(NetworkTime.time) > 0, "Fixture needs an attack still cooling down.");
            Mark("disconnect");
            while (NetworkServer.spawned.ContainsKey(oldAvatar)) yield return null;
            var retained = manager.Session.Participants.First(p => p.Id == returningId);
            Require(retained.ConnectionState == RunConnectionState.Disconnected && retained.AvatarId == 0 && retained.Checkpoint != null,
                "Disconnect did not retain participant checkpoint.");
            Require(retained.Checkpoint.Progression.PendingUpgradeCount == 2, "Pending upgrades lost on disconnect.");
            Require(retained.Checkpoint.WeaponCooldowns.Single().ReadyAt == cooldownBefore.ReadyAt,
                "Disconnect did not preserve the server-observed cooldown deadline.");
            Mark("saved");
            while (ServerPlayers().Length != 2 || retained.AvatarId == 0) yield return null;
            Require(retained.AvatarId != oldAvatar && retained.ConnectionEpoch != oldEpoch, "Reconnect reused avatar/epoch.");
            var restored = NetworkServer.spawned[retained.AvatarId];
            var selection = restored.GetComponent<NetworkModifierSelection>();
            while (selection.ServerOffers.Count == 0) yield return null;
            Require(selection.PendingUpgradeCount == 2 && selection.Level == before.Level && selection.Experience == before.Experience,
                "Progression changed on reconnect.");
            Require(selection.ServerOffers.Select(o => o.EquipmentId).SequenceEqual(before.Offers.Select(o => o.EquipmentId)),
                "Reconnect rerolled offers.");
            Require(selection.ServerOffers.All(o => before.Offers.All(old => old.PreviousOfferId != o.OfferId)), "Stale offer identity survived.");
            Require(restored.GetComponent<PlayerBuildRuntime>().EquipmentCount == 1, "Restored modifiers duplicated.");
            Require(restored.GetComponent<NetworkWeaponCombatAdapter>().CaptureCooldowns().Single().ReadyAt == cooldownBefore.ReadyAt,
                "Reconnect restarted the cooldown duration.");
            Mark("restored");
            while (!Has("resumed-client")) yield return null;
            Mark("finish");
            while (!Has("stopped-client") || (role == "server" && !Has("stopped-client2"))) yield return null;
            Debug.Log("[RuntimeBoundary] event=server-restoration-verified role=" + role);
        }

        private IEnumerator ClientScenario()
        {
            yield return WaitForOwner();
            while (!Has("configured")) yield return null;
            yield return VerifyOwner(false);
            Mark("verified-" + role);
            if (role == "client")
            {
                var identity = NetworkClient.localPlayer.GetComponent<NetworkRunParticipant>();
                ulong oldParticipant = identity.ParticipantId;
                uint oldAvatar = identity.AvatarId;
                ushort oldEpoch = identity.ConnectionEpoch;
                while (!Has("disconnect")) yield return null;
                manager.StopClient();
                while (manager.IsGameplayLoaded || NetworkClient.active) yield return null;
                while (!Has("saved")) yield return null;
                manager.StartClient();
                yield return WaitForOwner();
                identity = NetworkClient.localPlayer.GetComponent<NetworkRunParticipant>();
                Require(identity.ParticipantId == oldParticipant && identity.AvatarId != oldAvatar && identity.ConnectionEpoch != oldEpoch,
                    "Identity was not resumed into a new avatar/epoch.");
                while (!Has("restored")) yield return null;
                yield return VerifyOwner(true);
                Mark("resumed-client");
            }
            while (!Has("finish")) yield return null;
            manager.StopClient();
            while (manager.IsGameplayLoaded || NetworkClient.active) yield return null;
            Mark("stopped-" + role);
        }

        private IEnumerator WaitForOwner()
        {
            while (NetworkClient.localPlayer == null ||
                !NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive ||
                !NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                NetworkClient.localPlayer.GetComponent<ModifierSelectionController>().BoundBuild == null) yield return null;
        }

        private IEnumerator VerifyOwner(bool resumed)
        {
            yield return WaitForOwner();
            var owner = NetworkClient.localPlayer;
            var build = owner.GetComponent<PlayerBuildRuntime>();
            var selection = owner.GetComponent<NetworkModifierSelection>();
            var combatant = owner.GetComponent<CombatantBehaviour>();
            ulong id = owner.GetComponent<NetworkRunParticipant>().ParticipantId;
            selection.OnStartAuthority(); // Request current full baseline through the real Command/TargetRpc.
            while (build.EquipmentCount != 1 || owner.GetComponent<ModifierSelectionController>().Offers.Count == 0 ||
                combatant.CurrentHealth != 50 + (int)id * 10) yield return null;
            Require(build.InitialWeapon.DamageValue == (id == 1 ? 20 : 23), "Player Build leaked or duplicated modifiers.");
            if (resumed) Require(build.InitialWeapon.GetCooldown() - build.InitialWeapon.LastAttackElapsedTime > 0,
                "Reconnect made a cooling-down weapon immediately ready.");
            Require(combatant.StatusController.Has(EnemyStatusID.Poison), "Canonical status missing.");
            Require(selection.Experience == 1, "Player XP missing.");
            Require(owner.GetComponent<NetworkPlayerBootstrap>().IsLocalOwnerBound, "Owner input did not bind.");
            Require(FindObjectsByType<GameplayUIRoot>(FindObjectsSortMode.None).Length == 1, "Gameplay created duplicate UI roots.");
            Require(FindFirstObjectByType<CombatHUDController>().BoundCombatant == combatant, "Personal HUD bound wrong player.");
            foreach (var remote in FindObjectsByType<NetworkPlayerBootstrap>(FindObjectsSortMode.None))
                if (!remote.isOwned)
                {
                    Require(!remote.IsLocalOwnerBound && !remote.GetComponent<PlayerMovement>().enabled, "Remote bound local input.");
                    if (!remote.isServer) Require(!remote.GetComponent<PlayerBuildRuntime>().IsBuildActive, "Remote executes another player's Build.");
                }
            var initialWeapon = build.InitialWeapon;
            var bridge = owner.GetComponent<MirrorNetworkCombatBridge>();
            var collector = bridge.Collector;
            for (int i = 0; i < 3; i++)
            {
                owner.GetComponent<NetworkPlayerBootstrap>().OnStartAuthority();
                bridge.OnStartAuthority();
                owner.GetComponent<NetworkCombatantAdapter>().OnStartAuthority();
            }
            Require(ReferenceEquals(collector, bridge.Collector), "Repeated binding created extra collector subscriptions.");
            Require(build.InitialWeapon == initialWeapon && build.EquipmentCount == 1, "Repeated binding reset Build.");
            if (!resumed)
            {
                var weaponAdapter = owner.GetComponent<NetworkWeaponCombatAdapter>();
                var healthAdapter = owner.GetComponent<NetworkCombatantAdapter>();
                var bootstrap = owner.GetComponent<NetworkPlayerBootstrap>();
                var previousEvent = bridge.EventIds.Next();
                for (int i = 0; i < 3; i++)
                {
                    weaponAdapter.OnStopAuthority();
                    healthAdapter.OnStopAuthority();
                    selection.OnStopAuthority();
                    bootstrap.OnStopAuthority();
                    bridge.OnStopAuthority();
                    Require(!bootstrap.IsLocalOwnerBound && bridge.Collector == null, "Unbind retained owner capabilities.");
                    bridge.OnStartAuthority();
                    weaponAdapter.OnStartAuthority();
                    bootstrap.OnStartAuthority();
                    selection.OnStartAuthority();
                    healthAdapter.OnStartAuthority();
                    while (build.EquipmentCount != 1 || owner.GetComponent<ModifierSelectionController>().Offers.Count == 0)
                        yield return null;
                    Require(bridge.EventIds.Next().Value > previousEvent.Value, "Authority rebind reused event sequences.");
                    Require(build.InitialWeapon.DamageValue == (id == 1 ? 20 : 23), "Authority cycle duplicated a modifier.");
                    Require(combatant.CurrentHealth == 50 + (int)id * 10, "Authority cycle reset health.");
                }
            }
            if (GameDirector.Instance != null) GameDirector.Instance.SetPlayer(null);
            owner.GetComponent<NetworkPlayerBootstrap>().EnsurePlayerRuntimeInitialized();
            Require(combatant.CurrentHealth == 50 + (int)id * 10, "Initialization reset HP or used global Player.");
            Debug.Log("[RuntimeBoundary] event=owner-verified role=" + role + " resumed=" + resumed + " participant=" + id);
        }

        private bool Has(string name) => File.Exists(Path.Combine(directory, name));
        private void Mark(string name) => File.WriteAllText(Path.Combine(directory, name), "ready");
        private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true;
            SceneManager.sceneLoaded -= PrepareGameplay;
            Debug.Log("[RuntimeBoundary] result=" + (passed ? "PASS" : "FAIL") + " role=" + role);
            Application.Quit(passed ? 0 : 1);
        }
    }
}
