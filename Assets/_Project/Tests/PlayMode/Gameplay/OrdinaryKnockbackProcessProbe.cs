using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Test assemblies only. Files coordinate phases; all hits, admission, handoff and positions use Boot/Mirror.
    public sealed class OrdinaryKnockbackProcessProbe : MonoBehaviour
    {
        private string role, artifacts;
        private bool dedicated, impaired, capture, finished;
        private ushort port;
        private float deadline;
        private BootGameplayNetworkManager manager;
        private GameObject enemyPrefab, assetCopies;
        private NetworkEnemySimulationAgent enemy, preparedEnemy;
        private Vector2 takeoverPosition;
        private string Second => dedicated ? "client2" : "host";
        private bool Server => role == "host" || role == "server";
        private NetworkEnemySimulationWorld World => NetworkEnemySimulationWorld.Instance;
        private EnemyController Controller => enemy.GetComponent<EnemyController>();
        private Rigidbody2D Body => enemy.GetComponent<Rigidbody2D>();
        private PlayerMovement Player => NetworkClient.localPlayer.GetComponent<PlayerMovement>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            string value = args.FirstOrDefault(a => a.StartsWith("--m3-role="));
            if (value == null) return;
            var probe = new GameObject("M3 ordinary knockback validation").AddComponent<OrdinaryKnockbackProcessProbe>();
            probe.role = value.Substring(10);
            probe.artifacts = args.First(a => a.StartsWith("--m3-artifacts=")).Substring(15);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--m3-port=")).Substring(10));
            probe.dedicated = args.Contains("--m3-dedicated"); probe.impaired = args.Contains("--m3-impaired");
            probe.capture = args.Contains("--m3-capture");
            DontDestroyOnLoad(probe.gameObject);
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true;
            deadline = Time.realtimeSinceStartup + 210;
            SceneManager.sceneLoaded += PrepareGameplay;
            yield return Guard(Run());
            if (!finished) Finish(true);
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
        private void Update()
        {
            if (finished) return;
            if (Time.realtimeSinceStartup > deadline && deadline > 0) { Finish(false); return; }
            if (enemy != null && enemy != preparedEnemy && enemy.ProductEnemyInitialized)
            {
                preparedEnemy = enemy;
                Controller.Movement.StopMovement();
                Body.gravityScale = 0; // Isolate impulses from gravity while the fixture pauses navigation.
                Body.linearVelocity = Vector2.zero;
            }
        }
        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                { enemyPrefab = spawner.EnemyPrefab; spawner.Configure(spawner.EnemyPrefab, 5); spawner.enabled = false; }
        }
        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Must start through Boot.");
            gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            ConfigureFixtureAssets();
            var backend = manager.GetComponent<NetworkBackendBootstrap>();
            Require(backend.TryPrepareKcp("127.0.0.1", port, impaired, out var error), error);
            if (impaired)
            {
                var simulation = (LatencySimulation)backend.ConfiguredLatencySimulation;
                simulation.latency = 100; simulation.jitter = .02f;
                simulation.unreliableLoss = 5; simulation.unreliableScramble = 5;
            }
            if (role == "host") manager.StartHost();
            else if (role == "server") manager.StartServer();
            else manager.StartClient();
            if (role != "server") StartCoroutine(Guard(RunClient()));
            if (Server)
            {
                Mark("listening");
                yield return RunServer();
                if (role == "host") manager.StopHost(); else manager.StopServer();
            }
            else
            {
                while (!Has("stop")) yield return null;
                while (!Has("client-done-" + role)) yield return null;
                manager.StopClient();
            }
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active || NetworkServer.active) yield return null;
            Require(FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None).Length == 0, "Enemy survived Gameplay shutdown.");
            Mark("stopped-" + role);
        }
        private IEnumerator RunServer()
        {
            while (!Has("ready-client") || !Has("ready-" + Second)) yield return null;
            uint primary = uint.Parse(Read("ready-client"));
            while (!NetworkServer.spawned.ContainsKey(primary) || !manager.IsGameplayLoaded) yield return null;
            manager.BeginRun();
            var instance = Instantiate(enemyPrefab, new Vector3(5, 5, 0), Quaternion.identity);
            SceneManager.MoveGameObjectToScene(instance, NetworkServer.spawned[primary].gameObject.scene);
            enemy = instance.GetComponent<NetworkEnemySimulationAgent>();
            enemy.ConfigureInitialServerTarget(primary); enemy.ConfigureRuntimeMinimumHealthOverride(10000);
            NetworkServer.Spawn(instance);
            while (preparedEnemy != enemy) yield return null;
            Mark("enemy-id", enemy.netId.ToString());
            while (!Has("enemy-ready-client") || !Has("enemy-ready-" + Second)) yield return null;
            var gateway = NetworkCombatWorld.Instance.Gateway;
            gateway.CombatResultAccepted += TraceAccepted;
            World.ServerPlayerUnregistered += departing =>
            {
                if (enemy != null && enemy.Assignment.SimulationOwnerPlayerId == departing.PlayerEntityId &&
                    World.Registry.TryGetLatestSnapshot(enemy.netId, out var snapshot))
                    takeoverPosition = snapshot.Position;
            };
            yield return ServerPhase("self", new[] { "client" }, new[] { "client", Second });
            yield return ServerPhase("cross", new[] { Second }, new[] { "client", Second });
            yield return ServerPhase("both", new[] { "client", Second }, new[] { "client", Second });

            // Disconnect the assigned simulator with another real root still active.
            long accepted = gateway.Metrics.AcceptedCombatResults;
            Mark("disconnect-fire");
            while (gateway.Metrics.AcceptedCombatResults == accepted) yield return null;
            Mark("disconnect-now");
            while (!Has("disconnected") || NetworkServer.spawned.ContainsKey(primary)) yield return null;
            Require(enemy.Assignment.Host == EnemySimulationHost.ServerFallback, "A surviving player must cause server fallback.");
            Debug.Log($"[M3Process] handoff lastSnapshot={takeoverPosition} body={Body.position} transform={enemy.transform.position}");
            Require(Vector2.Distance(Body.position, takeoverPosition) <= .02f, "Server takeover lost the last simulator position before its next attack.");
            Require(!gateway.Attacks.RequiresAdmission(primary), "Disconnected source retained old attack roots.");
            yield return ServerPhase("fallback", new[] { Second }, new[] { Second });
            Mark("reconnect");
            while (!Has("reconnected")) yield return null;
            uint secondary = uint.Parse(Read("ready-" + Second));
            enemy.SetServerAssignment(World.Registry.AssignServerAuthoritative(enemy.netId, secondary));
            // Also exercise the actual product navigation/physics configuration and its restoration after each impulse.
            Controller.Movement.ResumeMovement();
            Body.gravityScale = enemyPrefab.GetComponent<Rigidbody2D>().gravityScale;
            yield return ServerPhase("authoritative", new[] { Second }, new[] { "client", Second });
            Mark("stop");
            while (!Has("client-done-client") || !Has("client-done-" + Second)) yield return null;
        }
        private IEnumerator ServerPhase(string phase, string[] attackers, string[] viewers)
        {
            var gateway = NetworkCombatWorld.Instance.Gateway;
            gateway.Ledger.TryGetState(enemy.netId, out var before);
            int applications = enemy.AppliedOrdinaryKnockbackCount;
            Mark("fire-" + phase, enemy.Assignment.Epoch.ToString());
            foreach (string viewer in viewers) while (!Has(phase + "-" + viewer)) yield return null;
            int hits = attackers.Sum(attacker => JsonUtility.FromJson<Observation>(Read(phase + "-" + attacker)).hits);
            if (phase == "authoritative")
            {
                Require(Controller.Movement.CanMove && !enemy.HasActiveNetworkKnockback, "Ordinary recovery did not restore product navigation.");
                Require(Body.linearVelocity.sqrMagnitude > .001f, "Product navigation never resumed real movement.");
                Controller.Movement.StopMovement();
                Body.gravityScale = 0; // Stop the fixture only after proving authored physics/navigation recovered.
            }
            yield return new WaitForSeconds(.7f);
            gateway.Ledger.TryGetState(enemy.netId, out var after);
            Require(after.Health == before.Health - hits * 12, $"{phase}: canonical HP {after.Health}, expected {before.Health - hits * 12}; rejectedRoot={gateway.Metrics.GetRejected(CombatRejectionReason.InvalidAttackRoot)} rate={gateway.Metrics.GetRejected(CombatRejectionReason.InvalidAttackRate)}.");
            int totalApplications = enemy.AppliedOrdinaryKnockbackCount - applications;
            if (enemy.Assignment.Host == EnemySimulationHost.ClientPlayer)
                totalApplications += JsonUtility.FromJson<Observation>(Read(phase + "-client")).applications;
            Require(totalApplications >= 3 && totalApplications <= hits, $"{phase}: {totalApplications} applications for {hits} hits.");
            if (phase != "both") Require(totalApplications == hits, $"{phase}: {totalApplications}/{hits} impulses; rejected={World.RejectedOrdinaryKnockbackCount}, epoch={enemy.Assignment.Epoch}.");
            Require(World.Registry.TryGetLatestSnapshot(enemy.netId, out var latest), "Missing simulator snapshot.");
            Mark("position-" + phase, JsonUtility.ToJson(new Observation { x = latest.Position.x, y = latest.Position.y, health = after.Health }));
            foreach (string viewer in viewers) while (!Has("converged-" + phase + "-" + viewer)) yield return null;
            Debug.Log($"[M3Process] phase={phase} hits={hits} applications={totalApplications} hp={after.Health} host={enemy.Assignment.Host} epoch={enemy.Assignment.Epoch} position={latest.Position}");
            Mark("complete-" + phase);
        }
        private IEnumerator RunClient()
        {
            yield return AwaitOwner();
            Mark("ready-" + role, NetworkClient.localPlayer.netId.ToString());
            while (!Has("enemy-id")) yield return null;
            yield return AwaitEnemy();
            Mark("enemy-ready-" + role);
            yield return ClientPhase("self", role == "client");
            yield return ClientPhase("cross", role == Second);
            yield return ClientPhase("both", true);
            if (role == "client")
            {
                while (!Has("disconnect-fire")) yield return null;
                var weapon = (CirclingAttackBehaviour)NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().InitialWeapon;
                weapon.baseSpeed = 0;
                KeepContact(weapon); weapon.Attack();
                while (!Has("disconnect-now")) { KeepContact(weapon); yield return null; }
                uint old = NetworkClient.localPlayer.netId;
                manager.StopClient();
                while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
                Require(enemy == null && FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None).Length == 0, "Old enemy survived disconnect.");
                Mark("disconnected");
                while (!Has("reconnect")) yield return null;
                manager.StartClient();
                yield return AwaitOwner(); yield return AwaitEnemy();
                Require(NetworkClient.localPlayer.netId != old, "Reconnect reused old avatar.");
                Require(!enemy.HasActiveNetworkKnockback && enemy.AppliedOrdinaryKnockbackCount == 0, "Reconnect replayed old hit.");
                Mark("reconnected");
            }
            else yield return ClientPhase("fallback", true);
            yield return ClientPhase("authoritative", role == Second);
            while (!Has("stop")) yield return null;
            Mark("client-done-" + role);
        }
        private IEnumerator ClientPhase(string phase, bool attack)
        {
            while (!Has("fire-" + phase)) yield return null;
            uint phaseEpoch = uint.Parse(Read("fire-" + phase));
            // Old-epoch requests during handoff are deliberately dropped. Start the next acceptance case only after SyncVar delivery.
            while (enemy.Assignment.Epoch != phaseEpoch) yield return null;
            var bridge = NetworkClient.localPlayer.GetComponent<MirrorNetworkCombatBridge>();
            bridge.Trace.Clear();
            int before = enemy.AppliedOrdinaryKnockbackCount;
            var weapon = (CirclingAttackBehaviour)NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().InitialWeapon;
            weapon.baseSpeed = 0;
            int notifications = 0;
            Action<NativeGasHit, CombatResolution> observe = (hit, result) =>
            {
                notifications++;
                if (phase == "self") Require(enemy.AppliedOrdinaryKnockbackCount - before == notifications, "Simulator waited for network or replayed an echo.");
                if (!enemy.Authority.RunsNavigation) Require(enemy.AppliedOrdinaryKnockbackCount == before && !enemy.HasActiveNetworkKnockback, "Observer started a real impulse.");
                if (capture && notifications == 2) ScreenCapture.CaptureScreenshot(Path.Combine(artifacts, phase + "-hit-" + role + ".png"));
            };
            Controller.NativeHitKnockbackRequested += observe;
            if (attack) KeepContact(weapon);
            else MovePlayer(Controller.hurtBox.GetPosition() + Vector2.left * 3);
            // Let the existing local camera settle after fixture placement before recording a visible hit.
            if (capture) yield return new WaitForSeconds(.8f);
            if (attack) weapon.Attack();
            float until = Time.time + weapon.GetAttackSequenceDuration() + 1.2f;
            while (Time.time < until)
            {
                if (attack && weapon.ActiveOrbCount > 0) KeepContact(weapon);
                yield return null;
            }
            Controller.NativeHitKnockbackRequested -= observe;
            var hits = bridge.Trace.Snapshot().Where(e => e.Kind == CombatTraceKind.DamageResolved && e.TargetEntityId == enemy.netId).ToArray();
            if (attack)
            {
                Require(hits.Length >= 3 && hits.Length == notifications, phase + ": missing real repeated hits/feedback.");
                Require(hits.Select(e => e.EventId).Distinct().Count() == hits.Length && hits.Select(e => e.RootEventId).Distinct().Count() == 1, "Repeated hits lost individual event identities.");
            }
            else Require(hits.Length == 0, "Non-attacker ran GAS.");
            int delta = enemy.AppliedOrdinaryKnockbackCount - before;
            Mark(phase + "-" + role, JsonUtility.ToJson(new Observation { hits = hits.Length, applications = delta }));
            Debug.Log($"[M3Process] phase={phase} role={role} events={string.Join(",", hits.Select(e => e.EventId.Value))} feedback={notifications} applications={delta} simulator={enemy.Authority.RunsNavigation}");
            while (!Has("position-" + phase)) yield return null;
            var expected = JsonUtility.FromJson<Observation>(Read("position-" + phase));
            float settle = Time.realtimeSinceStartup + 2;
            while ((Vector2.Distance(Body.position, new Vector2(expected.x, expected.y)) > .02f || Controller.CurrentHealth != expected.health) && Time.realtimeSinceStartup < settle) yield return null;
            Require(Vector2.Distance(Body.position, new Vector2(expected.x, expected.y)) <= .02f, phase + ": observer did not converge within .02 world units.");
            Require(Controller.CurrentHealth == expected.health, phase + ": local and canonical HP did not converge.");
            Require(!enemy.HasActiveNetworkKnockback, phase + ": impulse did not finish.");
            if (capture) ScreenCapture.CaptureScreenshot(Path.Combine(artifacts, phase + "-settled-" + role + ".png"));
            Mark("converged-" + phase + "-" + role);
            while (!Has("complete-" + phase)) yield return null;
        }
        private IEnumerator AwaitOwner()
        {
            while (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                !NetworkClient.localPlayer.GetComponent<PlayerBuildRuntime>().IsBuildActive) yield return null;
            MovePlayer(new Vector2(-15, -15));
        }
        private IEnumerator AwaitEnemy()
        {
            uint id = uint.Parse(Read("enemy-id"));
            while (!NetworkClient.spawned.TryGetValue(id, out var value) || value == null) yield return null;
            enemy = NetworkClient.spawned[id].GetComponent<NetworkEnemySimulationAgent>();
            while (preparedEnemy != enemy || enemy.Assignment.Epoch == 0) yield return null;
        }
        private void KeepContact(CirclingAttackBehaviour weapon)
        {
            MovePlayer(Controller.hurtBox.GetPosition() - Vector2.right * (weapon.baseRadius * weapon.SizeValue + .15f));
        }
        private void MovePlayer(Vector2 position)
        {
            Player.SetDirection(Vector2.zero); Player.body.position = position; Player.transform.position = position;
            Physics2D.SyncTransforms();
        }
        private void ConfigureFixtureAssets()
        {
            var db = FindFirstObjectByType<RuntimeDB>();
            assetCopies = new GameObject("M3 runtime asset copies"); assetCopies.SetActive(false); DontDestroyOnLoad(assetCopies);
            var weapons = Instantiate(db.WeaponDB);
            weapons.Configure(weapons.Weapons.Select(definition =>
            {
                if (definition.ID != 6) return definition;
                var copy = Instantiate(definition);
                var emitter = Instantiate((CirclingAttackBehaviour)definition.WeaponPrefab, assetCopies.transform);
                emitter.attackPrefab = Instantiate(emitter.attackPrefab, assetCopies.transform);
                foreach (string name in new[] { "startSound", "loopSound", "endSound", "hitSound" })
                {
                    var field = typeof(AnimatedAttack).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    field.SetValue(emitter.attackPrefab, Activator.CreateInstance(field.FieldType));
                }
                foreach (var component in emitter.attackPrefab.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component != null && component.GetType().Namespace == "FMODUnity") DestroyImmediate(component);
                copy.WeaponPrefab = emitter;
                var stats = copy.BaseStats; stats.projectileCount = 1; stats.critRate = 0;
                copy.ConfigureNativeGas(stats, copy.AttackTags, copy.Presentation);
                return copy;
            }).ToArray());
            db.ConfigureWeaponDatabase(weapons);
        }
        [Serializable] private sealed class Observation { public int hits, applications, health; public float x, y; }
        private void TraceAccepted(CombatResult result, CombatApplyResult applied, double time)
        {
            if (enemy == null || result.TargetEntityId != enemy.netId) return;
            Debug.Log($"[M3Process] accepted event={result.EventId} source={result.SourcePlayerId} hp={applied.State.Health} requestEpoch={result.Knockback.AssignmentEpoch} currentEpoch={enemy.Assignment.Epoch} simulator={enemy.Assignment.Host}:{enemy.Assignment.SimulationOwnerPlayerId} routed={World.RoutedOrdinaryKnockbackCount} rejectedKnockback={World.RejectedOrdinaryKnockbackCount}");
        }
        private bool Has(string name) => File.Exists(Path.Combine(artifacts, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(artifacts, name));
        private void Mark(string name, string text = "ready")
        {
            string destination = Path.Combine(artifacts, name);
            string temporary = destination + "." + role + ".pending";
            File.WriteAllText(temporary, text);
            File.Move(temporary, destination); // Publish a closed, complete marker atomically to other processes.
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true; SceneManager.sceneLoaded -= PrepareGameplay;
            Debug.Log($"[M3Process] result={(passed ? "PASS" : "FAIL")} role={role}");
            Application.Quit(passed ? 0 : 1);
        }
    }
}
