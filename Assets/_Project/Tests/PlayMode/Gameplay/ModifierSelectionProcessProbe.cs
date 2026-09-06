using System;
using System.Collections;
using System.Linq;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Opt-in fixture in IncludeTestAssemblies builds. Uses the real Boot/player path.
    public sealed class ModifierSelectionProcessProbe : MonoBehaviour
    {
        private const string Prefix = "[ModifierSelectionProcess]";
        private const uint FirstTarget = 0x7FFFF001u;
        private bool host, keyboard, gameplayUnloaded, keyboardArmed;
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
            deadline = Time.realtimeSinceStartup + (keyboard ? 300f : 75f);
            Application.runInBackground = true;
            SceneManager.sceneLoaded += PrepareGameplay;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            // Flatten nested enumerators so all assertion failures quit with a failure code.
            var stack = new System.Collections.Generic.Stack<IEnumerator>();
            stack.Push(Run());
            while (stack.Count > 0)
            {
                object next = null;
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
            if (deadline > 0f && Time.realtimeSinceStartup > deadline)
            {
                Debug.LogError($"{Prefix} timed out: {instruction}");
                Finish(false);
            }
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "The real Boot scene must provide NetworkManager.");
            Require(manager.GetComponent<NetworkBackendBootstrap>()
                .TryPrepareKcp("127.0.0.1", 7893, false, out string error), error);
            if (host) manager.StartHost(); else manager.StartClient();
            yield return WaitForOwner();
            if (host)
                for (uint i = 0; i < 3; i++)
                    NetworkCombatWorld.Instance.RegisterEntity(FirstTarget + i, 1000,
                        CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            Debug.Log($"{Prefix} event=ready role={(host ? "Host" : "Client")}");
            yield return PickAndHit(host ? 0 : 2, host ? FirstTarget : FirstTarget + 1);

            if (host)
            {
                instruction = "Host: waiting for Client and reconnect verification";
                while (!WasHit(FirstTarget + 2)) { ValidateRemoteBuilds(); yield return null; }
                while (NetworkClient.spawned.Values.Count(IsPlayer) > 1)
                { ValidateRemoteBuilds(); yield return null; }
                manager.StopHost();
            }
            else
            {
                while (!WasHit(FirstTarget)) yield return null;
                ModifierSelectionController oldSelection = OwnerSelection();
                PlayerBuildRuntime oldBuild = oldSelection.BoundBuild;
                uint oldPlayer = NetworkClient.localPlayer.netId;
                manager.StopClient();
                while (!gameplayUnloaded || NetworkClient.active) yield return null;
                Require(oldSelection == null || (oldSelection.BoundBuild == null && oldSelection.Offers.Count == 0),
                    "Disconnected player retained selection.");
                Require(oldBuild == null || !oldBuild.IsBuildActive, "Disconnected player retained build.");
                yield return null;
                manager.StartClient();
                yield return WaitForOwner();
                Require(NetworkClient.localPlayer.netId != oldPlayer, "Reconnect must rebuild the player.");
                yield return PickAndHit(1, FirstTarget + 2);
                Debug.Log($"{Prefix} event=reconnect-verified");
                yield return new WaitForSecondsRealtime(1f);
                manager.StopClient();
            }
            // isLoaded becomes false when unloading begins; destruction completes
            // with sceneUnloaded and Unity's deferred Destroy at the frame boundary.
            while (!gameplayUnloaded || NetworkClient.active) yield return null;
            yield return null;
            Require(FindObjectsByType<ModifierSelectionController>(FindObjectsSortMode.None).Length == 0,
                "Selection leaked after Gameplay unload.");
        }

        private IEnumerator WaitForOwner()
        {
            while (NetworkClient.localPlayer == null || OwnerSelection() == null ||
                OwnerSelection().Offers.Count != 3 ||
                NetworkClient.localPlayer.GetComponent<MirrorNetworkCombatBridge>().Collector == null) yield return null;
            var selection = OwnerSelection();
            Require(selection.BoundBuild == NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>(),
                "Selection must bind the owned build.");
            Require(selection.BoundBuild.EquipmentCount == 0, "A new build must not inherit equipment.");
            Require(selection.Offers.Select(offer => offer.EquipmentId).Distinct().Count() == 3,
                "Expected three distinct card offers.");
            ValidateRemoteBuilds();
        }

        private IEnumerator PickAndHit(int index, uint targetId)
        {
            var selection = OwnerSelection();
            var build = selection.BoundBuild;
            ModifierOffer expected = selection.Offers[index];
            var input = selection.GetComponent<DebugModifierSelectionInput>();
            if (keyboard)
            {
                input.enabled = false;
                keyboardArmed = false;
            }
            instruction = $"{(host ? "HOST" : "CLIENT")} - press {index + 1} - {expected.Equipment.Title}";
            Debug.Log($"{Prefix} event=awaiting-key role={(host ? "Host" : "Client")} key={index + 1} card={expected.EquipmentId}");
            if (keyboard)
            {
                while (!keyboardArmed) yield return null;
                input.enabled = true;
            }
            if (!keyboard)
            {
                var result = selection.Select(index);
                Require(result.Succeeded, result.Error);
            }
            while (selection.Offers.Count > 0) yield return null;
            Require(build.EquipmentCount == 1, "Selection must add exactly one equipment card.");
            Require(build.InitialWeapon.NativeRuntime.RuntimeModifiers.StaticModifiers.Select(modifier => modifier.ID.Value)
                .SequenceEqual(expected.Modifiers.Select(application => application.ModifierIdValue)),
                "Keyboard selection applied a different card or lost a composite modifier.");
            Require(!selection.SelectOffer(expected.OfferId).Succeeded, "Repeated selection must fail.");
            ValidateRemoteBuilds();

            var world = NetworkCombatWorld.Instance;
            while (!world.Replica.TryGetEntity(targetId, out _)) yield return null;
            GameObject targetObject = new GameObject("Selection Network Damage Target");
            var target = targetObject.AddComponent<CombatantBehaviour>();
            target.Initialize(1000);
            target.ConfigureEntityId(targetId);
            int damage;
            using (AttackSnapshot attack = build.InitialWeapon.NativeRuntime.BeginAttack())
                damage = build.InitialWeapon.NativeRuntime.ResolveHitDetailed(attack, target).ResolvedDamage.Value;
            Require(damage > 0 && target.CurrentHealth == 1000 - damage, "Native hit must apply damage.");
            Destroy(targetObject);
            while (!world.Replica.TryGetEntity(targetId, out CanonicalEntityState state) ||
                state.Health != 1000 - damage) yield return null;
            Debug.Log($"{Prefix} event=network-hit-verified key={index + 1} card={expected.EquipmentId} damage={damage}");
            instruction = $"{(host ? "HOST" : "CLIENT")} - selected {index + 1}, network damage verified";
        }

        private static ModifierSelectionController OwnerSelection() => NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.GetComponent<ModifierSelectionController>() : null;

        private static bool WasHit(uint id) => NetworkCombatWorld.Instance.Replica
            .TryGetEntity(id, out CanonicalEntityState state) && state.Health < 1000;

        private static bool IsPlayer(NetworkIdentity identity) => identity != null &&
            identity.GetComponent<NetworkPlayerBootstrap>() != null;

        private static void ValidateRemoteBuilds()
        {
            foreach (NetworkIdentity player in NetworkClient.spawned.Values.Where(IsPlayer))
            {
                if (player == NetworkClient.localPlayer) continue;
                Require(player.GetComponent<ModifierSelectionController>().BoundBuild == null &&
                    player.GetComponent<ModifierSelectionController>().Offers.Count == 0,
                    "Remote player generated offers.");
                Require(!player.GetComponent<PlayerBuildRuntime>().IsBuildActive &&
                    player.GetComponent<PlayerBuildRuntime>().EquipmentCount == 0,
                    "Remote player ran an owner build.");
            }
        }

        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path == manager.GameplayScene) gameplayUnloaded = false;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                    spawner.enabled = false;
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            if (scene.path == manager.GameplayScene) gameplayUnloaded = true;
        }

        private void OnGUI()
        {
            if (keyboard) GUI.Label(new Rect(20, 30, 1200, 80), instruction ?? "Starting selection validation",
                new GUIStyle(GUI.skin.box) { fontSize = 24 });
            if (keyboard && !keyboardArmed && OwnerSelection() != null && OwnerSelection().Offers.Count > 0 &&
                GUI.Button(new Rect(470, 130, 300, 65), "Begin key test")) keyboardArmed = true;
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
            deadline = 0f;
            Application.Quit(passed ? 0 : 1);
        }
    }
}
