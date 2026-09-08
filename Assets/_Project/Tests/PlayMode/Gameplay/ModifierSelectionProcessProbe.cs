using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Unity;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Opt-in two-process fixture. Production Boot, player prefab, menu, Mirror RPCs and GAS are exercised.
    public sealed class ModifierSelectionProcessProbe : MonoBehaviour
    {
        private const string Prefix = "[ModifierSelectionProcess]";
        private const uint FirstTarget = 0x7FFFF001u;
        private readonly HashSet<uint> granted = new HashSet<uint>();
        private readonly Dictionary<uint, ulong> firstEvents = new Dictionary<uint, ulong>();
        private readonly HashSet<uint> duplicateChecked = new HashSet<uint>();
        private bool host, keyboard, gameplayUnloaded, keyboardArmed, firstWave, initialOffersVerified;
        private float deadline;
        private BootGameplayNetworkManager manager;
        private string instruction;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            string role = args.FirstOrDefault(arg => arg.StartsWith("--modifier-selection-role="));
            if (role == null) return;
            var probe = new GameObject("Modifier Selection Process Probe").AddComponent<ModifierSelectionProcessProbe>();
            probe.host = role.EndsWith("=host");
            probe.keyboard = args.Contains("--modifier-selection-keyboard");
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            deadline = Time.realtimeSinceStartup + (keyboard ? 360f : 120f);
            Application.runInBackground = true;
            SceneManager.sceneLoaded += PrepareGameplay;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
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
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    Finish(false);
                    yield break;
                }
                yield return next;
            }
            Finish(true);
        }

        private void Update()
        {
            if (deadline <= 0) return;
            if (Time.realtimeSinceStartup > deadline)
            {
                Debug.LogError($"{Prefix} timed out: {instruction}");
                Finish(false);
                return;
            }
            if (!host || !NetworkServer.active) return;
            try { DriveServerProgression(); }
            catch (Exception exception) { Debug.LogException(exception); Finish(false); }
        }

        private void DriveServerProgression()
        {
            var players = NetworkServer.spawned.Values.Where(IsPlayer).ToArray();
            if (!firstWave)
                foreach (var identity in players)
                {
                    // The graphical Client can take longer to load. Keep the
                    // fixture's waiting player alive before beginning assertions.
                    NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(identity.netId, true);
                    identity.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(true);
                }
            if (!firstWave && players.Length < 2) return;
            if (players.Any(identity => !identity.GetComponent<PlayerBuildRuntime>().IsBuildActive)) return;
            bool simultaneous = !firstWave;
            firstWave = true;
            foreach (var identity in players)
            {
                var authority = identity.GetComponent<NetworkModifierSelection>();
                if (granted.Add(identity.netId))
                {
                    NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(identity.netId, false);
                    identity.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(false);
                    authority.ServerGrantExperience(authority.ExperiencePerLevel * (simultaneous ? 2 : 1));
                    Require(authority.PendingUpgradeCount == (simultaneous ? 2 : 1), "Consecutive XP levels were lost.");
                }
                // Owner readiness arrives over the actual connection, after server XP may already be queued.
                if (!firstEvents.ContainsKey(identity.netId) && authority.PendingEventId != 0)
                {
                    Require(authority.ServerOffers.Count == 3, "Server progression did not issue exactly 3 options.");
                    firstEvents.Add(identity.netId, authority.PendingEventId);
                    int before = identity.GetComponent<PlayerBuildRuntime>().EquipmentCount;
                    Require(!authority.ServerSelect(identity.connectionToClient, authority.PendingEventId, 3, out _),
                        "Invalid index was accepted.");
                    Require(!authority.ServerSelect(identity.connectionToClient, authority.PendingEventId + 10, 0, out _),
                        "Stale event was accepted.");
                    foreach (var other in players.Where(other => other != identity))
                        Require(!authority.ServerSelect(other.connectionToClient, authority.PendingEventId, 0, out _),
                            "Another player consumed the offer.");
                    Require(identity.GetComponent<PlayerBuildRuntime>().EquipmentCount == before,
                        "Invalid selections mutated the canonical build.");
                }
                if (firstEvents.ContainsKey(identity.netId) && authority.BuildRevision > 1 &&
                    duplicateChecked.Add(identity.netId))
                {
                    int before = identity.GetComponent<PlayerBuildRuntime>().EquipmentCount;
                    Require(!authority.ServerSelect(identity.connectionToClient, firstEvents[identity.netId], 0, out _),
                        "Consumed event was accepted again.");
                    Require(identity.GetComponent<PlayerBuildRuntime>().EquipmentCount == before,
                        "Duplicate applied an extra modifier.");
                    Debug.Log($"{Prefix} event=server-rejections-verified player={identity.netId}");
                }
            }
            if (!initialOffersVerified && firstEvents.Count == 2)
            {
                initialOffersVerified = true;
                Require(firstEvents.Values.Distinct().Count() == 2,
                    "Players share a pending identity.");
                Debug.Log($"{Prefix} event=simultaneous-independent-offers");
            }
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "The real Boot scene must provide NetworkManager.");
            Require(manager.GetComponent<NetworkBackendBootstrap>()
                .TryPrepareKcp("127.0.0.1", 7893, false, out string error), error);
            if (host) manager.StartHost(); else manager.StartClient();
            yield return WaitForOwnerBuild();
            if (host)
                for (uint i = 0; i < 3; i++)
                    NetworkCombatWorld.Instance.RegisterEntity(FirstTarget + i, 1000,
                        CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            Debug.Log($"{Prefix} event=ready role={(host ? "Host" : "Client")}");
            yield return PickRound(0);
            yield return PickRound(1);
            if (host)
            {
                yield return VerifyConfirmedKillProgression();
                yield return PickRound(2);
                yield return VerifyInterruptedSelectionCleanup();
                yield return VerifyReducedOfferPool();
            }
            yield return VerifyHit(host ? FirstTarget : FirstTarget + 1);

            if (host)
            {
                instruction = "Host: waiting for Client reconnect";
                while (!WasHit(FirstTarget + 2)) { ValidateRemoteBuilds(); yield return null; }
                while (NetworkClient.spawned.Values.Count(IsPlayer) > 1) yield return null;
                manager.StopHost();
            }
            else
            {
                while (!WasHit(FirstTarget)) yield return null;
                var oldSelection = OwnerSelection();
                var oldBuild = oldSelection.BoundBuild;
                uint oldPlayer = NetworkClient.localPlayer.netId;
                manager.StopClient();
                while (!gameplayUnloaded || NetworkClient.active) yield return null;
                Require(oldSelection == null || oldSelection.Offers.Count == 0, "Disconnect retained an offer.");
                Require(oldBuild == null || !oldBuild.IsBuildActive, "Disconnect retained an owner build.");
                yield return null;
                manager.StartClient();
                yield return WaitForOwnerBuild();
                Require(NetworkClient.localPlayer.netId != oldPlayer, "Reconnect reused stale player identity.");
                yield return PickRound(2);
                yield return VerifyHit(FirstTarget + 2);
                Debug.Log($"{Prefix} event=reconnect-verified");
                yield return new WaitForSecondsRealtime(0.5f);
                manager.StopClient();
            }
            while (!gameplayUnloaded || NetworkClient.active) yield return null;
            yield return null;
            Require(FindObjectsByType<ModifierSelectionController>(FindObjectsSortMode.None).Length == 0,
                "Selection leaked after Gameplay unload.");
        }

        private IEnumerator WaitForOwnerBuild()
        {
            instruction = "Waiting for owned Build";
            while (NetworkClient.localPlayer == null || OwnerSelection() == null ||
                OwnerSelection().BoundBuild == null || !OwnerSelection().BoundBuild.IsBuildActive ||
                NetworkClient.localPlayer.GetComponent<MirrorNetworkCombatBridge>().Collector == null) yield return null;
            Require(OwnerSelection().BoundBuild.EquipmentCount == 0, "New owner inherited previous equipment.");
        }

        private IEnumerator PickRound(int round)
        {
            instruction = "Waiting for server-issued offer";
            var selection = OwnerSelection();
            var authority = NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>();
            while (selection.Offers.Count != 3) yield return null;
            ulong currentEvent = authority.LocalEventId;
            var player = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            var combatant = NetworkClient.localPlayer.GetComponent<CombatantBehaviour>();
            var build = selection.BoundBuild;
            var menu = FindFirstObjectByType<CardPickMenu>();
            while (menu == null || !menu.IsOpen)
            { menu = FindFirstObjectByType<CardPickMenu>(); yield return null; }
            Require(menu.BoundSelection == selection, "Menu is bound to a different player.");
            Require(FindObjectsByType<CardPickMenu>(FindObjectsSortMode.None).Length == 1,
                "Gameplay created duplicate menus.");
            Require(selection.Offers.Select(o => o.EquipmentId).Distinct().Count() == 3, "Duplicate candidates.");
            Require(player.IsUpgradeSelectionLocked, "Owner movement/attack lock missing.");
            int health = combatant.CurrentHealth;
            combatant.ReceiveDamage(new DamageInfo(0u, 1, false));
            Require(combatant.CurrentHealth == health, "Selection damage immunity failed.");
            Vector3 position = player.transform.position;
            float worldTime = Time.time;
            yield return new WaitForSecondsRealtime(0.7f);
            Require(Time.time - worldTime > 0.3f && Time.timeScale == 1, "World paused during selection.");
            Require(Vector3.Distance(position, player.transform.position) < 0.02f, "Selecting player moved.");
            ValidateRemoteBuilds();

            int index = round % 3;
            ModifierOffer expected = selection.Offers[index];
            var buttons = menu.GetComponentsInChildren<Button>(true);
            Require(buttons.Length == 3, "Menu must contain exactly three Buttons.");
            if (keyboard)
            {
                keyboardArmed = false;
                var input = selection.GetComponent<DebugModifierSelectionInput>();
                input.enabled = false;
                instruction = $"{(host ? "HOST" : "CLIENT")} - choose {index + 1}: {expected.DisplayName}";
                Debug.Log($"{Prefix} event=awaiting-key role={(host ? "Host" : "Client")} key={index + 1} card={expected.EquipmentId}");
                while (!keyboardArmed) yield return null;
                input.enabled = true;
            }
            else buttons[index].onClick.Invoke();
            while (authority.LocalEventId == currentEvent) yield return null;
            Require(build.GetEquipmentStates().Any(s => s.EquipmentId == expected.EquipmentId &&
                s.LevelIndex == expected.LevelIndex), "Acknowledged upgrade not present in Owner Build.");
            Require(!selection.SelectOffer(expected.OfferId).Succeeded, "Old UI option remained selectable.");
            if (round != 0)
            {
                while (selection.Offers.Count > 0) yield return null;
                Require(!menu.IsOpen, "Acknowledged selection did not close UI.");
                Require(!player.IsUpgradeSelectionLocked, "Selection lock did not restore.");
                Require(build.InitialWeapon.enabled, "Owner weapon stayed disabled.");
            }
            Debug.Log($"{Prefix} event=ui-round-verified round={round} player={NetworkClient.localPlayer.netId}");
        }

        private IEnumerator VerifyHit(uint targetId)
        {
            var build = OwnerSelection().BoundBuild;
            var world = NetworkCombatWorld.Instance;
            while (!world.Replica.TryGetEntity(targetId, out _)) yield return null;
            var targetObject = new GameObject("Selection Network Damage Target");
            var target = targetObject.AddComponent<CombatantBehaviour>();
            target.Initialize(1000);
            target.ConfigureEntityId(targetId);
            int damage;
            using (AttackSnapshot attack = build.InitialWeapon.NativeRuntime.BeginAttack())
                damage = build.InitialWeapon.NativeRuntime.ResolveHitDetailed(attack, target).ResolvedDamage.Value;
            Require(damage > 0 && target.CurrentHealth == 1000 - damage, "Native hit failed.");
            Destroy(targetObject);
            while (!world.Replica.TryGetEntity(targetId, out CanonicalEntityState state) || state.Health != 1000 - damage)
                yield return null;
            Debug.Log($"{Prefix} event=network-hit-verified damage={damage}");
        }

        private IEnumerator VerifyConfirmedKillProgression()
        {
            var spawner = FindFirstObjectByType<NetworkGameplayEnemySpawner>();
            Require(spawner != null && spawner.EnemyPrefab != null, "Production enemy prefab is required.");
            var enemy = Instantiate(spawner.EnemyPrefab, NetworkClient.localPlayer.transform.position + Vector3.right * 2,
                Quaternion.identity);
            enemy.GetComponent<NetworkEnemySimulationAgent>().ConfigureInitialServerTarget(NetworkClient.localPlayer.netId);
            NetworkServer.Spawn(enemy);
            yield return null;
            var authority = NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>();
            int before = authority.Level;
            var combatant = enemy.GetComponent<CombatantBehaviour>();
            var weapon = OwnerSelection().BoundBuild.InitialWeapon.NativeRuntime;
            int hits = 0;
            while (combatant.IsAlive && hits++ < 100)
                using (AttackSnapshot attack = weapon.BeginAttack()) weapon.ResolveHitDetailed(attack, combatant);
            Require(!combatant.IsAlive, "Native attacks did not defeat the production enemy.");
            while (authority.Level == before) yield return null;
            Require(authority.Level == before + 1, "Confirmed kill XP must advance exactly one configured level.");
            Require(authority.PendingUpgradeCount == 1, "Confirmed kill failed to queue its upgrade.");
            Debug.Log($"{Prefix} event=production-enemy-kill-xp-verified hits={hits}");
        }

        private IEnumerator VerifyInterruptedSelectionCleanup()
        {
            var authority = NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>();
            // These assertions intentionally leave the test player idle and unlocked among live enemies.
            // Damage restoration is covered separately; protect this fixture during lifecycle/pool checks.
            NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(authority.netId, true);
            authority.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(true);
            var selection = OwnerSelection();
            var menu = FindFirstObjectByType<CardPickMenu>();
            yield return VerifyPresentationInterruption(selection, authority);
            yield return VerifyPresentationInterruption(authority, authority);
            yield return VerifyPresentationInterruption(menu, authority);
            Debug.Log($"{Prefix} event=component-interruption-cleanup-verified");
        }

        private IEnumerator VerifyPresentationInterruption(Behaviour component, NetworkModifierSelection authority)
        {
            var selection = OwnerSelection();
            var build = selection.BoundBuild;
            var player = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            int equipmentCount = build.EquipmentCount;
            authority.ServerQueueUpgrades();
            while (selection.Offers.Count != 3) yield return null;
            ulong expired = authority.PendingEventId;
            component.enabled = false;
            float cancelDeadline = Time.realtimeSinceStartup + 3f;
            while ((authority.PendingEventId != 0 || player.IsUpgradeSelectionLocked) &&
                Time.realtimeSinceStartup < cancelDeadline) yield return null;
            Require(authority.PendingEventId == 0 && !player.IsUpgradeSelectionLocked,
                $"Disabled {component.GetType().Name} left a pending offer or gameplay lock.");
            Require(build.InitialWeapon.enabled, "Canceled presentation left weapon execution disabled.");

            // XP arriving after shutdown must not trap a player behind an unavailable menu.
            // Host cancellation is immediate, but its Owner availability command still crosses Mirror.
            yield return new WaitForSecondsRealtime(0.3f);
            int previousLevel = authority.Level;
            authority.ServerGrantExperience(authority.ExperiencePerLevel);
            yield return new WaitForSecondsRealtime(0.3f);
            Require(authority.Level == previousLevel + 1 && authority.PendingUpgradeCount == 1,
                "Progression earned while presentation was unavailable was lost.");
            Require(authority.PendingEventId == 0 && !player.IsUpgradeSelectionLocked,
                $"New XP locked the player while {component.GetType().Name} was disabled.");

            if (component is CardPickMenu)
            {
                // Reinitializing another adapter must not claim that the closed view is ready.
                selection.enabled = false;
                yield return null;
                selection.enabled = true;
                yield return new WaitForSecondsRealtime(0.3f);
                authority.enabled = false;
                yield return null;
                authority.enabled = true;
                yield return new WaitForSecondsRealtime(0.3f);
                authority.ServerGrantExperience(authority.ExperiencePerLevel);
                yield return new WaitForSecondsRealtime(0.3f);
                Require(authority.PendingEventId == 0 && !player.IsUpgradeSelectionLocked,
                    "Re-enabling an adapter reopened an unavailable menu.");
            }

            component.enabled = true;
            while (selection.BoundBuild != build || selection.Offers.Count != 3) yield return null;
            Require(authority.PendingEventId != expired, "Resuming presentation revived a canceled event.");
            Require(!authority.ServerSelect(NetworkClient.localPlayer.connectionToClient, expired, 0, out _),
                "Interrupted offer remained valid.");
            Require(build.EquipmentCount == equipmentCount, "Cancellation applied or removed equipment.");
            authority.ServerCancelPending();
            while (selection.Offers.Count != 0 || player.IsUpgradeSelectionLocked) yield return null;
            Debug.Log($"{Prefix} event=disabled-xp-resume-verified component={component.GetType().Name}");
        }

        private IEnumerator VerifyReducedOfferPool()
        {
            var authority = NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>();
            var selection = OwnerSelection();
            var build = selection.BoundBuild;
            var menu = FindFirstObjectByType<CardPickMenu>();
            var player = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            foreach (var state in build.GetEquipmentStates()) build.RemoveEquipment(state.Handle);
            var cards = new EquipmentModifierOfferProvider(new SeededRandomSource(42))
                .Generate(build).Select(offer => offer.Equipment).ToArray();
            Require(cards.Length == 3, "Fixture requires three existing eligible definitions.");
            build.AddEquipment(build.InitialWeapon, cards[0], cards[0].Levels.Length - 1);
            build.AddEquipment(build.InitialWeapon, cards[1], cards[1].Levels.Length - 2);
            build.AddEquipment(build.InitialWeapon, cards[2], cards[2].Levels.Length - 2);
            authority.ServerQueueUpgrades(2);
            for (int expectedCount = 2; expectedCount >= 1; expectedCount--)
            {
                while (selection.Offers.Count != expectedCount || !menu.IsOpen) yield return null;
                var buttons = menu.GetComponentsInChildren<Button>();
                Require(buttons.Length == expectedCount, "Reduced pool displayed unissued buttons.");
                ulong eventId = authority.PendingEventId;
                Require(!authority.ServerSelect(NetworkClient.localPlayer.connectionToClient,
                    eventId, expectedCount, out _), "Unused option index was accepted.");
                ModifierOffer selected = selection.Offers[0];
                buttons[0].onClick.Invoke();
                while (authority.LocalEventId == eventId) yield return null;
                Require(build.GetEquipmentStates().Any(state => state.EquipmentId == selected.EquipmentId &&
                    state.LevelIndex == selected.LevelIndex), "Reduced offer did not update the existing Build.");
            }
            while (menu.IsOpen || player.IsUpgradeSelectionLocked) yield return null;
            authority.ServerQueueUpgrades();
            yield return new WaitForSecondsRealtime(0.3f);
            Require(authority.PendingUpgradeCount == 1 && authority.PendingEventId == 0 &&
                !menu.IsOpen && !player.IsUpgradeSelectionLocked, "Exhausted pool must remain pending and unlocked.");
            authority.ServerCancelPending();
            NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(authority.netId, false);
            authority.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(false);
            Debug.Log($"{Prefix} event=reduced-pool-2-1-0-verified");
        }
        private static ModifierSelectionController OwnerSelection() => NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.GetComponent<ModifierSelectionController>() : null;
        private static bool WasHit(uint id) => NetworkCombatWorld.Instance.Replica
            .TryGetEntity(id, out CanonicalEntityState state) && state.Health < 1000;
        private static bool IsPlayer(NetworkIdentity identity) => identity != null &&
            identity.GetComponent<NetworkPlayerBootstrap>() != null;
        private static void ValidateRemoteBuilds()
        {
            foreach (var identity in NetworkClient.spawned.Values.Where(IsPlayer))
            {
                if (identity == NetworkClient.localPlayer) continue;
                Require(identity.GetComponent<ModifierSelectionController>().BoundBuild == null &&
                    identity.GetComponent<ModifierSelectionController>().Offers.Count == 0,
                    "An observer received another player's offers.");
                var build = identity.GetComponent<PlayerBuildRuntime>();
                if (NetworkServer.active)
                    Require(build.IsBuildActive && !build.InitialWeapon.enabled,
                        "Remote canonical Build is missing or executing server auto-attacks.");
                else Require(!build.IsBuildActive, "Observer instantiated a gameplay Build.");
            }
        }
        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path == manager.GameplayScene) gameplayUnloaded = false;
            // Keep live enemies/world simulation. Avoid killing the finite smoke-test enemies during assertions.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                    typeof(NetworkGameplayEnemySpawner).GetMethod("ConfigureRuntimeMinimumSpawnHealth",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(spawner, new object[] { 100000 });
        }
        private void HandleSceneUnloaded(Scene scene)
        {
            if (scene.path == manager.GameplayScene) gameplayUnloaded = true;
        }
        private void OnGUI()
        {
            if (!keyboard) return;
            GUI.Label(new Rect(20, 20, 1200, 60), instruction ?? "Starting validation",
                new GUIStyle(GUI.skin.box) { fontSize = 22 });
            if (!keyboardArmed && OwnerSelection() != null && OwnerSelection().Offers.Count > 0 &&
                GUI.Button(new Rect(470, 95, 300, 50), "Begin input test")) keyboardArmed = true;
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        private void Finish(bool passed)
        {
            Debug.Log($"{Prefix} result={(passed ? "PASS" : "FAIL")} role={(host ? "Host" : "Client")}");
            SceneManager.sceneLoaded -= PrepareGameplay;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            deadline = 0;
            Application.Quit(passed ? 0 : 1);
        }
    }
}
