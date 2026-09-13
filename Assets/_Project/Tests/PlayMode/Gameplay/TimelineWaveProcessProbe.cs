using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class TimelineWaveProcessProbe : MonoBehaviour
    {
        [Serializable] private sealed class SpawnRow { public long sequence; public int wave, slot; public double time; public uint id, asset; public string prefab; }
        [Serializable] private sealed class Manifest { public SpawnRow[] rows; }
        private string role, directory;
        private bool server, dedicated, failed;
        private float deadline;
        private BootGameplayNetworkManager manager;
        private NetworkGameplayEnemySpawner spawner;
        private NetworkEnemySimulationWorld world;
        private readonly List<SpawnRow> rows = new List<SpawnRow>();
        private readonly Dictionary<uint, uint> observed = new Dictionary<uint, uint>();
        private readonly HashSet<string> melee = new HashSet<string>();
        private System.Threading.Mutex signals;
        private string[] ClientRoles => dedicated ? new[] { "a", "b" } : new[] { "host", "a" };
        private static string Arg(string key) => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(key))?.Substring(key.Length);
        private bool Has(string file) => File.Exists(Path.Combine(directory, file));
        private void Write(string file, string text = "1") { signals.WaitOne(); try { File.WriteAllText(Path.Combine(directory, file), text); } finally { signals.ReleaseMutex(); } }
        private string Read(string file) { signals.WaitOne(); try { return File.ReadAllText(Path.Combine(directory, file)); } finally { signals.ReleaseMutex(); } }
        private static void Check(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string role = Arg("--timeline-wave-role="); if (role == null) return;
            var probe = new GameObject("Timeline wave process validation").AddComponent<TimelineWaveProcessProbe>();
            probe.role = role; probe.server = role == "host" || role == "server";
            probe.dedicated = Arg("--timeline-wave-dedicated=") == "1"; probe.directory = Arg("--timeline-wave-output=");
            DontDestroyOnLoad(probe.gameObject); probe.gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            signals = new System.Threading.Mutex(false, "TimelineWaves_" + Path.GetFileName(directory));
            deadline = Time.realtimeSinceStartup + 250;
            Application.logMessageReceived += Log;
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0 && !failed)
            {
                object next;
                try { if (!stack.Peek().MoveNext()) { stack.Pop(); continue; } next = stack.Peek().Current; if (next is IEnumerator nested) { stack.Push(nested); continue; } }
                catch (Exception error) { Debug.LogException(error); failed = true; break; }
                yield return next;
            }
            Application.logMessageReceived -= Log;
            Debug.Log("[TimelineWaveProcess] result=" + (failed ? "FAIL" : "PASS") + " role=" + role);
            Application.Quit(failed ? 1 : 0);
        }
        private void Log(string text, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true; }
        private IEnumerator Until(Func<bool> predicate)
        { while (!predicate()) { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Timeline waves timeout: " + role); yield return null; } }

        private void Update()
        {
            if (NetworkClient.localPlayer != null) NetworkClient.localPlayer.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(true);
            if (NetworkClient.active)
                foreach (var identity in NetworkClient.spawned.Values)
                    if (identity != null && identity.GetComponent<NetworkEnemySimulationAgent>() != null)
                    {
                        if (observed.TryGetValue(identity.netId, out uint asset)) Check(asset == identity.assetId, "A live netId changed enemy type.");
                        else observed.Add(identity.netId, identity.assetId);
                    }
            if (!server || world == null || NetworkCombatWorld.Instance == null) return;
            foreach (var identity in NetworkServer.spawned.Values)
                if (identity != null)
                {
                    if (identity.GetComponent<PlayerCombatantBinding>() != null) NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(identity.netId, true);
                    if (identity.GetComponent<EnemyAttackMelee>() != null &&
                        world.Registry.TryGetLatestAttackPresentation(identity.netId, out var edge) && edge.Phase == EnemyAttackPresentationPhase.Active)
                        melee.Add(identity.name.Replace("(Clone)", ""));
                }
        }
        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>(); manager.ConfigurePreparationFlow(false);
            bool impaired = Arg("--timeline-wave-impaired=") == "1";
            var backend = manager.GetComponent<NetworkBackendBootstrap>();
            Check(backend.TryPrepareKcp("127.0.0.1", ushort.Parse(Arg("--timeline-wave-port=")), impaired, out var error), error);
            if (impaired) { var lag = (LatencySimulation)backend.ConfiguredLatencySimulation; lag.latency = 150; lag.jitter = .02f; lag.unreliableLoss = 10; }
            if (server) { if (dedicated) manager.StartServer(); else manager.StartHost(); Write("listening"); }
            else manager.StartClient();
            if (!server || !dedicated)
            {
                yield return Until(() => NetworkClient.localPlayer != null && manager.IsGameplayLoaded && NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline);
                Write("ready-" + role);
            }
            yield return Until(() => NetworkEnemySimulationWorld.Instance != null && FindFirstObjectByType<NetworkGameplayEnemySpawner>() != null);
            world = NetworkEnemySimulationWorld.Instance; spawner = FindFirstObjectByType<NetworkGameplayEnemySpawner>();
            if (server)
            {
                yield return Until(() => ClientRoles.All(r => Has("ready-" + r)) && manager.CanBeginRun(out _));
                Check(spawner.UsesWaves && spawner.CountCanonicalEnemies() == 0, "Expected the formal empty wave scene before start.");
                spawner.WaveEnemySpawned += (spawn, id) => {
                    var identity = NetworkServer.spawned[id];
                    rows.Add(new SpawnRow { sequence = spawn.Sequence, wave = spawn.Wave, slot = spawn.Index, time = spawn.ScheduledTime, id = id, asset = identity.assetId, prefab = identity.name.Replace("(Clone)", "") });
                    Debug.Log($"[TimelineWaveProcess] spawned wave={spawn.Wave} slot={spawn.Index} type={identity.name} id={id} asset={identity.assetId}");
                };
                Check(manager.TryBeginRun(out error), error);
                yield return Until(() => rows.Count >= 30);
                Check(spawner.CountCanonicalEnemies() == 30, "Earlier waves disappeared or exceeded capacity.");
                // Release exactly one wave's capacity; production limit stays 30.
                foreach (var row in rows.Take(6)) NetworkServer.Destroy(NetworkServer.spawned[row.id].gameObject);
                yield return Until(() => rows.Count >= 36);
                Check(rows.Count == 36 && spawner.ServerProgress.Wave == 6 && spawner.ServerProgress.TotalSkipped == 0, "Six-wave count/clock mismatch.");
                var firstTypes = new[] { "NetworkEnemyBase", "NetworkEnemyBase", "NetworkEnemySkeleton", "NetworkEnemyImp", "NetworkEnemyLustSinner", "NetworkEnemyLustSinner" };
                for (int i = 0; i < rows.Count; i++)
                {
                    int wave = i / 6, slot = i % 6; var row = rows[i];
                    string expected = wave >= 1 && wave <= 3 && slot % 2 == 1 ? "NetworkEnemyLustSinner" : firstTypes[wave];
                    Check(row.sequence == i + 1 && row.wave == wave + 1 && row.slot == slot + 1 && row.prefab == expected &&
                        Math.Abs(row.time - (wave * 30 + slot * 2)) < .00001, "Wrong Timeline event at " + (i + 1));
                }
                Check(melee.Contains("NetworkEnemySkeleton") && melee.Contains("NetworkEnemyLustSinner"), "A spawned melee enemy never entered its active phase.");
                Check(world.AcceptedProjectileCount > 0 && world.AcceptedProjectileTerminationCount > 0, "Imp did not launch and terminate network projectiles.");
                if (dedicated) Check(world.PresentedProjectileCount == 0 && world.ActiveEnemyProjectileCount == 0, "Dedicated instantiated bullets.");
                Write("manifest.json", JsonUtility.ToJson(new Manifest { rows = rows.ToArray() }, true));
                Debug.Log($"[TimelineWaveProcess] six-waves=36 melee={string.Join(",", melee)} acceptedLaunches={world.AcceptedProjectileCount} acceptedTerminations={world.AcceptedProjectileTerminationCount}");
            }
            if (!server || !dedicated)
            {
                yield return Until(() => Has("manifest.json") && NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.TotalSpawned == 36);
                var manifest = JsonUtility.FromJson<Manifest>(Read("manifest.json"));
                yield return Until(() => manifest.rows.All(r => observed.ContainsKey(r.id)));
                foreach (var row in manifest.rows) Check(observed[row.id] == row.asset, "Client prefab identity mismatch for " + row.id);
                yield return Until(() => manifest.rows.Take(6).All(r => !NetworkClient.spawned.ContainsKey(r.id)));
                Write("done-" + role);
            }
            if (server)
            {
                yield return Until(() => ClientRoles.All(r => Has("done-" + r)));
                Write("stop"); yield return new WaitForSeconds(1);
                if (dedicated) manager.StopServer(); else manager.StopHost();
            }
            else { yield return Until(() => Has("stop")); manager.StopClient(); }
        }
    }
}
