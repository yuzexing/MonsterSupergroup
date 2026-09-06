using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Included only by the validation build's IncludeTestAssemblies option.
    public sealed class HealthHUDProcessProbe : MonoBehaviour
    {
        private const string Prefix = "[HealthHUDProcess]";
        private bool host;
        private float deadline;
        private BootGameplayNetworkManager manager;
        private int expectedCurrent;
        private int expectedMaximum;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string role = Environment.GetCommandLineArgs()
                .FirstOrDefault(arg => arg.StartsWith("--health-hud-role="));
            if (role == null) return;
            var probe = new GameObject("Health HUD Process Probe").AddComponent<HealthHUDProcessProbe>();
            probe.host = role == "--health-hud-role=host";
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            deadline = Time.realtimeSinceStartup + 70f;
            Application.runInBackground = true;
            SceneManager.sceneLoaded += PrepareGameplay;
            IEnumerator run = Run();
            while (true)
            {
                object next;
                try
                {
                    if (!run.MoveNext()) break;
                    next = run.Current;
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
            if (deadline > 0f && Time.realtimeSinceStartup > deadline) Finish(false);
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Boot must contain the real NetworkManager.");
            var backend = manager.GetComponent<NetworkBackendBootstrap>();
            Require(backend.TryPrepareKcp("127.0.0.1", 7892, false, out string error), error);
            if (host) manager.StartHost(); else manager.StartClient();
            while (NetworkClient.localPlayer == null || Roots().Length != 1) yield return null;
            yield return null;
            SetOwnerHealth(host ? 71 : 129, host ? 151 : 263);
            ValidateDisplay();
            Debug.Log($"{Prefix} event=ready role={(host ? "Host" : "Client")}");
            while (!RemoteHasMaximum(host ? 263 : 151))
            {
                ValidateDisplay();
                yield return null;
            }
            ValidateDisplay();
            Debug.Log($"{Prefix} event=two-players hp={expectedCurrent}/{expectedMaximum}");
            string artifactArgument = Environment.GetCommandLineArgs()
                .FirstOrDefault(arg => arg.StartsWith("--health-hud-artifacts="));
            if (artifactArgument != null)
                ScreenCapture.CaptureScreenshot(Path.Combine(artifactArgument.Substring(
                    "--health-hud-artifacts=".Length), host ? "host.png" : "client.png"));

            if (host)
            {
                while (!RemoteHasMaximum(233))
                {
                    ValidateDisplay();
                    yield return null;
                }
                Debug.Log($"{Prefix} event=client-reconnected hp={expectedCurrent}/{expectedMaximum}");
                while (NetworkClient.spawned.Values.Count(IsPlayer) > 1)
                {
                    ValidateDisplay();
                    yield return null;
                }
                manager.StopHost();
            }
            else
            {
                float until = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < until) { ValidateDisplay(); yield return null; }
                uint oldId = NetworkClient.localPlayer.netId;
                GameplayUIRoot oldRoot = Roots()[0];
                manager.StopClient();
                while (manager.IsGameplayLoaded || NetworkClient.active) yield return null;
                Require(oldRoot == null && Roots().Length == 0, "UI leaked after Client disconnect.");
                yield return null;
                manager.StartClient();
                while (NetworkClient.localPlayer == null || Roots().Length != 1) yield return null;
                yield return null;
                Require(NetworkClient.localPlayer.netId != oldId, "Reconnect must create a new player.");
                SetOwnerHealth(83, 233);
                until = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < until) { ValidateDisplay(); yield return null; }
                Debug.Log($"{Prefix} event=reconnected hp={expectedCurrent}/{expectedMaximum}");
                manager.StopClient();
            }
            while (manager.IsGameplayLoaded || NetworkClient.active) yield return null;
            Require(Roots().Length == 0, "UI leaked after Gameplay unload.");
        }

        private void SetOwnerHealth(int current, int maximum)
        {
            NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>()?.ClearBuild();
            CombatantBehaviour combatant = NetworkClient.localPlayer.GetComponent<CombatantBehaviour>();
            combatant.SetMaximumHealthPreservingMissingHealth(maximum);
            combatant.RestoreHealth(maximum);
            combatant.ReceiveDamage(new DamageInfo(1, maximum - current, false));
            expectedCurrent = current;
            expectedMaximum = maximum;
        }

        private void ValidateDisplay()
        {
            GameplayUIRoot[] roots = Roots();
            Require(roots.Length == 1, "Each process must have exactly one HUD root.");
            Require(roots[0].gameObject.scene.path == manager.GameplayScene, "HUD is in the wrong scene.");
            CombatHUDController hud = roots[0].GetComponentInChildren<CombatHUDController>();
            CombatantBehaviour local = NetworkClient.localPlayer.GetComponent<CombatantBehaviour>();
            Require(hud.BoundCombatant == local, "HUD is not bound to the local player.");
            Require(hud.GetComponent<CanvasGroup>().alpha == 1f, "Bound HUD is hidden.");
            PlayerHealthHUD health = roots[0].GetComponentInChildren<PlayerHealthHUD>();
            Require(Field<TMP_Text>(health, "currentHealthText").text == expectedCurrent.ToString(), "Wrong current HP.");
            Require(Field<TMP_Text>(health, "maxHealthText").text == expectedMaximum.ToString(), "Wrong maximum HP.");
            OverflowBar bar = Field<OverflowBar>(health, "healthBar");
            float expected = (float)expectedCurrent / expectedMaximum;
            Require(Mathf.Abs(Field<Image>(bar, "topBar").fillAmount - expected) < 0.0001f, "Wrong top fill.");
            Require(Mathf.Abs(Field<Image>(bar, "bottomBar").fillAmount - expected) < 0.0001f, "Wrong bottom fill.");
        }

        private bool RemoteHasMaximum(int maximum) => NetworkClient.spawned.Values.Any(
            identity => IsPlayer(identity) && identity != NetworkClient.localPlayer &&
                identity.GetComponent<CombatantBehaviour>().MaxHealth == maximum);

        private static bool IsPlayer(NetworkIdentity identity) => identity != null &&
            identity.GetComponent<NetworkPlayerBootstrap>() != null;

        private static GameplayUIRoot[] Roots() => FindObjectsByType<GameplayUIRoot>(FindObjectsSortMode.None);

        private static T Field<T>(object instance, string name)
        {
            for (Type type = instance.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) return (T)field.GetValue(instance);
            }
            throw new MissingFieldException(name);
        }

        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            // Keep HP deterministic while exercising the real Boot/player/UI path.
            // Enemy combat has its own process fixture; do not rewrite scene assets.
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                    spawner.enabled = false;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private void Finish(bool passed)
        {
            Debug.Log($"{Prefix} result={(passed ? "PASS" : "FAIL")} role={(host ? "Host" : "Client")}");
            SceneManager.sceneLoaded -= PrepareGameplay;
            deadline = 0f;
            Application.Quit(passed ? 0 : 1);
        }
    }
}
