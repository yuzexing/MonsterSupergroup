using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Explicit manual prototype room. No input automation, combat overrides, or automatic exit.</summary>
    public sealed class PrototypePlaytestLaunch : MonoBehaviour
    {
        private string role, address, readyFile, failure;
        private ushort port;
        private int waitFor;
        private PrototypePlaytestBattle battle;
        private BootGameplayNetworkManager manager;
        private string status = "Starting prototype room...";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!MonsterSupergroup.Builds.BuildFeatures.DevelopmentToolsAllowed) return;
            string[] args = Environment.GetCommandLineArgs();
            string selected = Argument(args, "--prototype-playtest=");
            if (selected == null) return;
            var launch = new GameObject("Prototype manual playtest").AddComponent<PrototypePlaytestLaunch>();
            DontDestroyOnLoad(launch.gameObject);
            launch.role = selected; launch.address = Argument(args, "--prototype-address=") ?? "127.0.0.1";
            launch.readyFile = Argument(args, "--prototype-ready-file=");
            if ((selected != "host" && selected != "client") ||
                !ushort.TryParse(Argument(args, "--prototype-port=") ?? "8000", out launch.port) || launch.port == 0 ||
                !int.TryParse(Argument(args, "--prototype-wait-for=") ?? "2", out launch.waitFor) || launch.waitFor < 1 || launch.waitFor > 4)
                launch.failure = "Invalid prototype arguments: role host/client, port 1-65535, wait-for 1-4.";
        }
        private static string Argument(string[] args, string prefix) =>
            args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
        private IEnumerator Start()
        {
            if (failure != null) { Debug.LogError(failure); yield break; }
            Application.runInBackground = true; Application.targetFrameRate = 120;
            yield return null;
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            if (manager == null) { Fail("Prototype playtest must start from Boot."); yield break; }
            battle = new PrototypePlaytestBattle();
            manager.ConfigurePreparationFlow(true);
            var backend = manager.GetComponent<NetworkBackendBootstrap>();
            string error = null;
            if (backend == null || !backend.TryPrepareKcp(address, port, false, out error))
            { Fail(backend == null ? "Network backend is missing." : error); yield break; }
            if (role == "host")
            {
                manager.StartHost();
                if (!string.IsNullOrWhiteSpace(readyFile))
                {
                    try { File.WriteAllText(Path.GetFullPath(readyFile), "listening"); }
                    catch (Exception errorWritingReady) { Fail(errorWritingReady.Message); yield break; }
                }
            }
            else manager.StartClient();
            status = role == "host" ? $"Waiting for {waitFor} player(s) at {address}:{port}" : $"Connecting to {address}:{port}";
            // A manual Host can wait indefinitely for the teammate to launch; the room UI stays usable.
            while (manager.RoomSnapshot.Members == null || manager.RoomSnapshot.SelfId == 0) yield return null;
            manager.SetOwnLoadout(6);
            while (!manager.RoomSnapshot.Members.Any(m => m.ParticipantId == manager.RoomSnapshot.SelfId && m.WeaponId == 6))
                yield return null;
            manager.SetOwnReady(true);
            if (role == "host")
            {
                while (manager.RoomSnapshot.Members.Length < waitFor || !manager.RoomSnapshot.Members.All(m => m.Ready)) yield return null;
                manager.StartPreparedGame();
            }
            while (!manager.IsGameplayLoaded || NetworkClient.localPlayer == null)
            {
                if (battle.Error != null) { Fail(battle.Error); yield break; }
                yield return null;
            }
            status = "1 Gluttony | 2 Music (R / Space) | 3 Allure (R throw / T take / F decoy)";
            Debug.Log($"[Prototypes] Manual {role} ready at {address}:{port}. " + status);
        }
        private void Fail(string message) { failure = message; Debug.LogError("[Prototypes] " + message); }
        private void OnGUI()
        {
            GUI.Label(new Rect(12, Screen.height - 28, Screen.width - 380, 26),
                failure ?? $"Prototype playtest / {role} / {status}");
        }
        private void OnDestroy() { battle?.Dispose(); battle = null; }
    }
}
