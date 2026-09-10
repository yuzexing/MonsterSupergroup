using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Tests
{
    /// <summary>Opt-in formal Boot fixture. File markers coordinate assertions, never production wave state.</summary>
    public sealed class GameplayWaveProcessProbe : MonoBehaviour
    {
        private string role, directory;
        private ushort port;
        private bool dedicated, capture, simulation, finished;
        private float deadline;
        private int loggedErrors;
        private BootGameplayNetworkManager manager;
        private KcpLocalNetworkService service;
        private bool IsServer => role == "host" || role == "server";
        private string Other => dedicated ? "client2" : "host";
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkGameplayEnemySpawner Spawner => FindFirstObjectByType<NetworkGameplayEnemySpawner>();
        private NetworkWaveProgress Progress => NetworkCombatWorld.Instance != null ? NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>() : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            string value = args.FirstOrDefault(a => a.StartsWith("--m5-role="));
            if (value == null) return;
            var probe = new GameObject("M5 formal wave validation").AddComponent<GameplayWaveProcessProbe>();
            probe.role = value.Substring(10);
            probe.directory = args.First(a => a.StartsWith("--m5-artifacts=")).Substring(15);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--m5-port=")).Substring(10));
            probe.dedicated = args.Contains("--m5-dedicated");
            probe.capture = args.Contains("--m5-capture");
            probe.simulation = args.Contains("--m5-simulation");
            DontDestroyOnLoad(probe.gameObject);
        }
        private IEnumerator Start()
        {
            Application.logMessageReceived += ObserveLog;
            Application.runInBackground = true;
            deadline = Time.realtimeSinceStartup + 300;
            gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Start from formal Boot.");
            service = manager.GetComponent<KcpLocalNetworkService>();
            yield return Guard(Run());
            if (!finished) Finish(true);
        }
        private void Update()
        {
            if (!finished && deadline > 0 && Time.realtimeSinceStartup > deadline)
            { Debug.LogError("[M5Process] timeout role=" + role); Finish(false); }
            // Runtime-only protection keeps real enemies alive for precise wave count assertions.
            if (Owner != null) Owner.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(true);
            if (NetworkServer.active && NetworkCombatWorld.Instance != null)
                foreach (var player in Players())
                    NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(player.netId, true);
        }
        private IEnumerator Run()
        {
            if (role == "host") StartCoroutine(Guard(ClientScenario()));
            if (IsServer) yield return ServerScenario(); else yield return ClientScenario();
        }
        private void StartRole(string nextRole)
        {
            Require(service.TrySetConfiguration("127.0.0.1", port, simulation, out string error), error);
            if (simulation)
            {
                var lag = manager.GetComponent<NetworkBackendBootstrap>().ConfiguredLatencySimulation as LatencySimulation;
                Require(lag != null, "LatencySimulation missing.");
                lag.latency = 80;
                lag.unreliableLoss = 10;
            }
            if (nextRole == "host") service.StartHost();
            else if (nextRole == "server") service.StartServer();
            else service.StartClient();
        }
        private IEnumerator ServerScenario()
        {
            string previousRun = null;
            for (int round = 1; round <= 2; round++)
            {
                yield return Wait(() => service.CanStart, "server can start");
                StartRole(role);
                Mark("listening-" + round);
                if (round == 1) Mark("listening");
                yield return Wait(() => Has("ready-client-" + round) && Has("ready-" + Other + "-" + round), "party ready");
                Require(Players().Length == 2, "Exactly two avatars expected.");
                yield return Wait(() => manager.CanBeginRun(out _), "formal start boundary");
                yield return new WaitForSecondsRealtime(.5f);
                Require(Spawner.UsesWaves && Spawner.CountCanonicalEnemies() == 0, "Waiting phase generated legacy enemies.");
                if (capture && dedicated && round == 1) yield return Capture("waiting-start");
                Require(manager.Session.RunId != previousRun, "New session reused its run identity.");
                previousRun = manager.Session.RunId;
                Require(manager.TryBeginRun(out string error), error);
                yield return Wait(() => Spawner.ServerProgress.TotalSpawned == 1, "first wave enemy");
                var started = Spawner.ServerProgress;
                Require(manager.TryBeginRun(out error), error);
                Require(Spawner.ServerProgress.Equals(started), "Duplicate start reset the schedule.");
                if (round == 1)
                {
                    yield return Wait(() => Spawner.ServerProgress.TotalSpawned >= 18, "three default waves", 85);
                    Require(Spawner.ServerProgress.Wave == 3 && Spawner.ServerProgress.TotalSpawned == 18 &&
                        Spawner.ServerProgress.TotalSkipped == 0 && Spawner.CountCanonicalEnemies() == 18, "Default waves must produce 18 live enemies.");
                    if (capture && dedicated) yield return Capture("three-waves");
                    Mark("compare18", manager.Session.RunId);
                    yield return Wait(() => Has("compared-client") && Has("compared-" + Other), "both wave HUDs");
                    uint leaving = uint.Parse(Read("ready-client-1"));
                    var affected = Enemies().Where(e => e.Assignment.SimulationOwnerPlayerId == leaving).Select(e => e.netId).ToArray();
                    Require(affected.Length > 0, "Both members should receive initial simulation assignments.");
                    long total = Spawner.ServerProgress.TotalSpawned;
                    Mark("disconnect-single");
                    yield return Wait(() => Has("offline-single-client") && !NetworkServer.spawned.ContainsKey(leaving), "client disconnect");
                    Require(affected.All(id => NetworkServer.spawned[id].GetComponent<NetworkEnemySimulationAgent>().Assignment.Host == EnemySimulationHost.ServerFallback), "Disconnected simulator was not handed to the server.");
                    Mark("resume-single");
                    yield return Wait(() => Has("rejoined-single-client"), "client rejoin");
                    Require(Spawner.ServerProgress.TotalSpawned == total, "Reconnect produced an extra enemy before the next scheduled slot.");
                    Debug.Log("[M5Process] event=single-reconnect-no-duplicate");
                    if (dedicated)
                    {
                        Mark("disconnect-all");
                        yield return Wait(() => Has("offline-all-client") && Has("offline-all-client2") &&
                            Spawner.ServerProgress.Phase == WavePhase.Paused, "empty-server pause");
                        var paused = Spawner.ServerProgress;
                        yield return new WaitForSecondsRealtime(2);
                        Require(Spawner.ServerProgress.Elapsed == paused.Elapsed && Spawner.ServerProgress.TotalSpawned == paused.TotalSpawned,
                            "Empty server advanced the wave clock.");
                        Mark("resume-all");
                        yield return Wait(() => Has("rejoined-all-client") && Has("rejoined-all-client2"), "both original members rejoin");
                        Debug.Log("[M5Process] event=empty-server-pause-resume");
                    }
                    // Fill remaining capacity with explicit fixture enemies using the same formal prefab.
                    // This accelerates the cap test without changing the production wave settings or clock.
                    while (Spawner.CountCanonicalEnemies() < 30) SpawnCapacityFixture();
                    long skipped = Spawner.ServerProgress.TotalSkipped;
                    yield return Wait(() => Spawner.ServerProgress.TotalSkipped > skipped, "cap skips", 40);
                    Require(Spawner.CountCanonicalEnemies() == 30, "Global capacity exceeded.");
                    total = Spawner.ServerProgress.TotalSpawned;
                    GameplayWavePlayModeTests.SetCanonicalHealth(Enemies()[0].netId, 0);
                    Require(Spawner.CountCanonicalEnemies() == 29, "Canonical death did not release capacity.");
                    yield return Wait(() => Spawner.ServerProgress.TotalSpawned > total, "new slot uses released capacity", 35);
                    Require(Spawner.ServerProgress.TotalSpawned == total + 1 && Spawner.CountCanonicalEnemies() == 30, "Skipped slots burst after a death.");
                    Mark("cap-done");
                    yield return Wait(() => Has("cap-client") && Has("cap-" + Other), "cap replica");
                    Require(Players().All(p => p.GetComponent<NetworkModifierSelection>().BuildRevision == 2),
                        "Server must apply exactly one Equipment reward per player.");
                    foreach (var player in Players()) GameplayWavePlayModeTests.SetCanonicalHealth(player.netId, 0);
                    yield return Wait(() => Spawner.ServerProgress.Phase == WavePhase.Paused, "all-dead pause");
                    double elapsed = Spawner.ServerProgress.Elapsed;
                    total = Spawner.ServerProgress.TotalSpawned;
                    yield return new WaitForSecondsRealtime(1);
                    Require(Spawner.ServerProgress.Elapsed == elapsed && Spawner.ServerProgress.TotalSpawned == total, "All-dead clock advanced.");
                    Mark("death-paused");
                    yield return Wait(() => Has("death-client") && Has("death-" + Other), "paused replicas");
                }
                else yield return Wait(() => Has("fresh-client") && Has("fresh-" + Other), "fresh run snapshots");
                Mark("stop-" + round);
                service.Stop();
                yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning && service.CanStart, "server scene cleanup");
                yield return Wait(() => Has("cleared-client-" + round) && Has("cleared-" + Other + "-" + round), "client scene cleanup");
                Require(FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None).Length == 0, "Old spawner survived Stop.");
                Require(Enemies().Length == 0, "Old enemies survived Stop.");
                Debug.Log("[M5Process] event=stop-clean round=" + round);
            }
        }
        private IEnumerator ClientScenario()
        {
            string previous = null;
            for (int round = 1; round <= 2; round++)
            {
                yield return Wait(() => Has("listening-" + round), "server listening");
                if (role != "host") { yield return Wait(() => service.CanStart, "client can start"); StartRole("client"); }
                yield return Wait(OwnerReady, "owner baseline");
                if (role != "host") Require(!manager.TryBeginRun(out _), "Remote client started the run.");
                Mark("ready-" + role + "-" + round, Owner.netId.ToString());
                if (round == 1)
                {
                    yield return Wait(() => Has("compare18") && Progress != null && Progress.Snapshot.TotalSpawned == 18 && Progress.Snapshot.Alive == 18,
                        "three-wave snapshot", 100);
                    previous = Progress.Snapshot.RunId;
                    Require(previous == Read("compare18") && Progress.Snapshot.Wave == 3, "Replica disagrees on the run or wave.");
                    yield return null;
                    var hud = FindFirstObjectByType<NetworkWaveHUD>();
                    Require(hud != null && hud.DisplayedSnapshot.TotalSpawned == 18, "Gameplay HUD missed authoritative progress.");
                    var camera = FindFirstObjectByType<AstralShift.HellMaiden.CameraFX.GameplayCameraRig>();
                    Require(camera != null && camera.BoundPlayer == Owner.GetComponent<AstralShift.HellMaiden.Player.PlayerMovement>(), "M2 camera owner binding regressed.");
                    if (capture) yield return Capture("three-waves");
                    Mark("compared-" + role);
                    if (role == "client") yield return Reconnect("single");
                    if (dedicated) yield return Reconnect("all");
                    yield return Wait(() => Has("cap-done") && Progress != null && Progress.Snapshot.Alive == 30, "cap snapshot", 60);
                    Require(Progress.Snapshot.TotalSkipped > 0, "Replica lost skipped slots.");
                    if (capture) yield return Capture("capacity");
                    var selection = Owner.GetComponent<NetworkModifierSelection>();
                    var view = Owner.GetComponent<ModifierSelectionController>();
                    Require(selection.RequestDebugLevelUp(), "F5 owner request rejected.");
                    yield return Wait(() => selection.Level == 2 && view.Offers.Count > 0, "upgrade during waves");
                    double beforeSelection = Progress.Snapshot.Elapsed;
                    yield return new WaitForSecondsRealtime(.6f);
                    Require(Progress.Snapshot.Phase == WavePhase.Running && Progress.Snapshot.Elapsed > beforeSelection, "Selecting a reward paused global waves.");
                    if (capture) yield return Capture("upgrade-during-waves");
                    Require(view.Select(0).Succeeded, "Equipment card submission failed.");
                    yield return Wait(() => view.Stage == UpgradeSelectionStage.EquipmentTarget && !view.IsRequestPending, "Equipment target stage");
                    Require(view.Select(0).Succeeded, "Equipment target submission failed.");
                    yield return Wait(() => !selection.IsSelecting && selection.OwnerBuildRevision == 2, "Equipment owner baseline");
                    Require(selection.OwnerBuildRevision == 2, "Equipment did not align exactly once.");
                    Mark("cap-" + role);
                    yield return Wait(() => Has("death-paused") && Progress.Snapshot.Phase == WavePhase.Paused, "paused snapshot");
                    Mark("death-" + role);
                }
                else
                {
                    yield return Wait(() => Progress != null && Progress.Snapshot.TotalSpawned == 1, "fresh wave snapshot");
                    Require(Progress.Snapshot.RunId != previous && Progress.Snapshot.Wave == 1 && Progress.Snapshot.TotalSkipped == 0,
                        "New run inherited old wave data.");
                    Mark("fresh-" + role);
                }
                yield return Wait(() => Has("stop-" + round), "stop signal");
                if (role != "host") service.Stop();
                yield return Wait(() => !NetworkClient.active && !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "client unload");
                Require(FindObjectsByType<NetworkWaveHUD>(FindObjectsSortMode.None).Length == 0, "Wave HUD survived unload.");
                Mark("cleared-" + role + "-" + round);
            }
        }
        private IEnumerator Reconnect(string phase)
        {
            yield return Wait(() => Has("disconnect-" + phase), "disconnect " + phase);
            string run = Progress.Snapshot.RunId;
            ulong participant = Owner.GetComponent<NetworkRunParticipant>().ParticipantId;
            uint avatar = Owner.netId;
            service.Stop();
            yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning && service.CanStart, "reconnect cleanup");
            Require(FindObjectsByType<NetworkWaveHUD>(FindObjectsSortMode.None).Length == 0, "Reconnect leaked HUD.");
            Mark("offline-" + phase + "-" + role);
            yield return Wait(() => Has("resume-" + phase), "resume " + phase);
            StartRole("client");
            yield return Wait(() => OwnerReady() && Progress != null && Progress.Snapshot.RunId == run, "reconnect baseline");
            Require(Owner.netId != avatar && Owner.GetComponent<NetworkRunParticipant>().ParticipantId == participant, "Reconnect changed the stable member identity.");
            Require(Progress.Snapshot.Wave >= 3 && Progress.Snapshot.TotalSpawned >= 18, "Reconnect reset the wave.");
            Mark("rejoined-" + phase + "-" + role);
        }
        private void SpawnCapacityFixture()
        {
            var target = Players()[0];
            var enemy = Instantiate(Spawner.EnemyPrefab, target.transform.position + Vector3.right * 5, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(enemy, target.gameObject.scene);
            enemy.GetComponent<NetworkEnemySimulationAgent>().ConfigureInitialServerTarget(target.netId);
            NetworkServer.Spawn(enemy);
        }
        private bool OwnerReady() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline &&
            Owner.GetComponent<ModifierSelectionController>().IsPresentationReady;
        private static NetworkIdentity[] Players() => NetworkServer.spawned.Values.Where(p => p != null && p.GetComponent<NetworkRunParticipant>() != null).ToArray();
        private static NetworkEnemySimulationAgent[] Enemies() => FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None);
        private IEnumerator Wait(Func<bool> condition, string name, float seconds = 25)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Require(condition(), "Timed out: " + name);
        }
        private IEnumerator Guard(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            while (stack.Count > 0 && !finished)
            {
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    next = stack.Peek().Current;
                    if (next is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception error) { Debug.LogException(error); Finish(false); yield break; }
                yield return next;
            }
        }
        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, role + "-" + name + ".png"));
            yield return null;
        }
        private bool Has(string name) => File.Exists(Path.Combine(directory, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(directory, name));
        private void Mark(string name, string value = "ready") => File.WriteAllText(Path.Combine(directory, name), value);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void ObserveLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) loggedErrors++;
        }
        private void OnDestroy() => Application.logMessageReceived -= ObserveLog;
        private void Finish(bool success)
        {
            if (finished) return;
            finished = true;
            success &= loggedErrors == 0;
            Debug.Log("[M5Process] result=" + (success ? "PASS" : "FAIL") + " role=" + role + " loggedErrors=" + loggedErrors);
            Application.Quit(success ? 0 : 1);
        }
    }
}
