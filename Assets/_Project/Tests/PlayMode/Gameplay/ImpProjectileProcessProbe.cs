using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    [DefaultExecutionOrder(10000)]
    public sealed class ImpProjectileProcessProbe : MonoBehaviour
    {
        private string role, directory;
        private bool server, dedicated, failed;
        private float deadline;
        private BootGameplayNetworkManager manager;
        private NetworkEnemySimulationWorld world;
        private readonly Dictionary<EnemyProjectileKey, BulletProjectile> bullets = new Dictionary<EnemyProjectileKey, BulletProjectile>();
        private readonly HashSet<EnemyProjectileKey> ended = new HashSet<EnemyProjectileKey>();
        private readonly HashSet<uint> warningFrames = new HashSet<uint>();
        private int attackToken, endToken;
        private System.Threading.Mutex signalMutex;
        private string[] ClientRoles => dedicated ? new[] { "a", "b" } : new[] { "host", "a", "b" };
        private static string Arg(string key) => Environment.GetCommandLineArgs().FirstOrDefault(s => s.StartsWith(key))?.Substring(key.Length);
        private string PathFor(string name) => Path.Combine(directory, name);
        private bool Seen(string name) => File.Exists(PathFor(name));
        private void Mark(string name, string text = "1")
        { signalMutex.WaitOne(); try { File.WriteAllText(PathFor(name), text); } finally { signalMutex.ReleaseMutex(); } }
        private string Read(string name)
        { signalMutex.WaitOne(); try { return File.ReadAllText(PathFor(name)); } finally { signalMutex.ReleaseMutex(); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var role = Arg("--imp-role="); if (role == null) return;
            var probe = new GameObject("Imp projectile process validation").AddComponent<ImpProjectileProcessProbe>();
            probe.role = role; probe.server = role == "host" || role == "server";
            probe.dedicated = Arg("--imp-dedicated=") == "1"; probe.directory = Arg("--imp-output=");
            DontDestroyOnLoad(probe.gameObject); probe.gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            signalMutex = new System.Threading.Mutex(false, "ImpProjectile_" + Path.GetFileName(directory));
            deadline = Time.realtimeSinceStartup + 160;
            Application.logMessageReceived += Log;
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0 && !failed)
            {
                object next;
                try { if (!stack.Peek().MoveNext()) { stack.Pop(); continue; } next = stack.Peek().Current; if (next is IEnumerator nested) { stack.Push(nested); continue; } }
                catch (Exception e) { Debug.LogException(e); failed = true; break; }
                yield return next;
            }
            Application.logMessageReceived -= Log;
            Debug.Log("[ImpProcess] result=" + (failed ? "FAIL" : "PASS") + " role=" + role);
            Application.Quit(failed ? 1 : 0);
        }
        private void Log(string text, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true; }
        private static void Check(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
        private IEnumerator Until(Func<bool> condition)
        { while (!condition()) { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Imp stage timeout: " + role); yield return null; } }

        private void Attach()
        {
            if (world != null || NetworkEnemySimulationWorld.Instance == null) return;
            world = NetworkEnemySimulationWorld.Instance;
            world.EnemyProjectilePresented += (launch, bullet) => {
                Check(!bullets.ContainsKey(launch.Key), "Duplicate projectile spawn " + launch.Key.ActionId);
                Check(bullet.transform.position == launch.Origin, "Launch origin was advanced on receipt.");
                Check(bullet.GetComponent<NetworkIdentity>() == null, "Bullet has network identity.");
                bullets.Add(launch.Key, bullet);
                File.AppendAllText(PathFor(role + "-launches.txt"), JsonUtility.ToJson(launch) + "\n");
                Mark("spawn-" + launch.Key.EnemyEntityId + "-" + role);
                if (Arg("--imp-graphics=") == "1") StartCoroutine(Capture(role + "-flight-" + bullets.Count));
            };
            world.EnemyProjectileAccepted += launch => Mark("accepted-" + launch.Key.EnemyEntityId, launch.Key.ActionId.ToString());
            world.EnemyProjectileTerminated += terminal => {
                Check(ended.Add(terminal.Key), "Duplicate termination event.");
                Mark("ended-" + terminal.Key.EnemyEntityId + "-" + role);
            };
        }
        private void Update()
        {
            Attach();
            if (world == null) return;
            foreach (var spawner in FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None))
                if (spawner.enabled) { spawner.Configure(spawner.EnemyPrefab, 5); spawner.enabled = false; }
            if (NetworkClient.localPlayer != null)
            {
                // Controlled collision cases invoke the real interaction explicitly;
                // physics geometry is tested separately in PlayMode.
                foreach (var hitbox in NetworkClient.localPlayer.GetComponentsInChildren<PlayerHitbox>(true))
                    foreach (var collider in hitbox.GetComponents<Collider2D>()) collider.enabled = false;
            }
            foreach (var identity in NetworkClient.spawned.Values)
                if (identity != null && identity.TryGetComponent<EnemyController>(out var enemy))
                {
                    enemy.attackDistance = 0;
                    if (Arg("--imp-graphics=") == "1" && enemy.CurrentAttackPresentationPhase == EnemyAttackPresentationPhase.Warning && warningFrames.Add(identity.netId))
                        StartCoroutine(Capture(role + "-warning-" + identity.netId));
                }
            if (Seen("attack"))
            {
                string[] values = Read("attack").Split(','); int token = int.Parse(values[0]); uint id = uint.Parse(values[1]);
                var spawned = server && dedicated ? NetworkServer.spawned : NetworkClient.spawned;
                if (token != attackToken && spawned.TryGetValue(id, out var identity) && identity.GetComponent<NetworkEnemySimulationAgent>().Authority.RunsCombatDecisions)
                { attackToken = token; identity.GetComponent<EnemyController>().Attack(); }
            }
            if (Seen("terminate"))
            {
                string[] values = Read("terminate").Split(','); int token = int.Parse(values[0]);
                if (token != endToken && values[2] == role)
                {
                    endToken = token; uint id = uint.Parse(values[1]); var pair = bullets.First(p => p.Key.EnemyEntityId == id);
                    Check(pair.Value != null && pair.Value.gameObject.activeInHierarchy, "Requested local bullet no longer active.");
                    if (values[3] == "hit")
                    {
                        var hitbox = NetworkClient.localPlayer.GetComponentInChildren<PlayerHitbox>();
                        var player = NetworkClient.localPlayer.GetComponent<PlayerCombatantBinding>();
                        int hp = player.CurrentHealth;
                        pair.Value.damageInteraction.Interact(hitbox); pair.Value.damageInteraction.Interact(hitbox);
                        Check(player.CurrentHealth == hp - 5, "Local projectile hit did not deduct exactly five once.");
                    }
                    else Destroy(pair.Value.gameObject);
                }
            }
        }
        private void LateUpdate()
        {
            if (Arg("--imp-graphics=") != "1" || Camera.main == null || NetworkClient.localPlayer == null) return;
            foreach (var identity in NetworkClient.spawned.Values)
                if (identity != null && identity.TryGetComponent<EnemyController>(out var enemy) && enemy.spriteRenderer != null)
                {
                    // Validation framing: include the whole character and its path to the player.
                    var center = (enemy.spriteRenderer.bounds.center + NetworkClient.localPlayer.transform.position) * .5f;
                    var camera = Camera.main; camera.orthographicSize = 7;
                    camera.transform.position = new Vector3(center.x, center.y, camera.transform.position.z);
                    break;
                }
        }
        private IEnumerator Capture(string name)
        {
            yield return new WaitForSeconds(.12f); yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture(); var pixels = texture.GetPixels32();
            Check(pixels.Count(p => p.r > 20 || p.g > 20 || p.b > 20) > pixels.Length / 100, "Black game frame.");
            Check(pixels.Count(p => p.r > 248 && p.g > 248 && p.b > 248) < pixels.Length * .6, "Opaque shader frame.");
            File.WriteAllBytes(PathFor(name + ".png"), texture.EncodeToPNG()); Destroy(texture);
        }
        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>(); manager.ConfigurePreparationFlow(false);
            manager.spawnPrefabs.Single(p => p.name == "NetworkEnemyImp").GetComponent<EnemyController>().attackDistance = 0;
            var backend = manager.GetComponent<NetworkBackendBootstrap>(); bool impaired = Arg("--imp-impaired=") == "1";
            Check(backend.TryPrepareKcp("127.0.0.1", ushort.Parse(Arg("--imp-port=")), impaired, out var error), error);
            if (impaired) { var latency = (LatencySimulation)backend.ConfiguredLatencySimulation; latency.latency = 150; latency.jitter = .02f; latency.unreliableLoss = 10; }
            if (server) { if (dedicated) manager.StartServer(); else manager.StartHost(); Mark("ready-server"); } else manager.StartClient();
            if (!server || !dedicated)
            {
                yield return Until(() => NetworkClient.localPlayer != null && manager.IsGameplayLoaded && NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline);
                Mark("ready-" + role, NetworkClient.localPlayer.netId.ToString());
            }
            yield return Until(() => world != null);
            if (!server)
            {
                yield return Until(() => Seen("complete"));
                Check(bullets.Count == 3 && ended.Count == 3, "Client launch/termination counts incorrect.");
                Mark("done-" + role);
                yield return Until(() => Seen("stop"));
                manager.StopClient(); yield break;
            }
            yield return Until(() => Seen("ready-a") && Seen("ready-b") && world.HasEligiblePlayer);
            manager.BeginRun();
            uint a = uint.Parse(Read("ready-a")), b = uint.Parse(Read("ready-b"));
            var prefab = manager.spawnPrefabs.Single(p => p.name == "NetworkEnemyImp");
            for (int step = 1; step <= 3; step++)
            {
                uint target = step == 2 ? b : a;
                Vector3 origin = NetworkServer.spawned[target].transform.position;
                var root = Instantiate(prefab, origin + new Vector3(step == 2 ? -4 : 4, 3, 0), Quaternion.identity);
                var agent = root.GetComponent<NetworkEnemySimulationAgent>();
                root.GetComponent<EnemyController>().attackDistance = 0;
                if (step == 3) agent.Authority.ConfigureNetworkManaged(EnemySimulationMode.BossServer, true);
                agent.ConfigureInitialServerTarget(target); NetworkServer.Spawn(root);
                yield return Until(() => agent.ProductEnemyInitialized && agent.AppliedHandoffEpoch == agent.Assignment.Epoch);
                Mark("attack", step + "," + agent.netId);
                yield return Until(() => Seen("accepted-" + agent.netId) && ClientRoles.All(r => Seen("spawn-" + agent.netId + "-" + r)));
                if (step < 3) Mark("terminate", step + "," + agent.netId + "," + (step == 1 ? "b,hit" : "a,destroy"));
                yield return Until(() => ClientRoles.All(r => Seen("ended-" + agent.netId + "-" + r)) && world.AcceptedProjectileTerminationCount >= step);
                NetworkServer.Destroy(root);
            }
            Check(world.AcceptedProjectileCount == 3 && world.AcceptedProjectileTerminationCount == 3, "Server event counts incorrect.");
            if (dedicated) Check(world.ActiveEnemyProjectileCount == 0 && world.PresentedProjectileCount == 0, "Dedicated created bullets.");
            else Check(bullets.Count == 3 && ended.Count == 3, "Host presentation count incorrect.");
            Mark("complete"); yield return Until(() => Seen("done-a") && Seen("done-b"));
            Mark("stop"); yield return new WaitForSeconds(1);
            if (dedicated) manager.StopServer(); else manager.StopHost();
        }
    }
}
