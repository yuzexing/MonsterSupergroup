using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Explicit test-build probe. Files coordinate steps, never combat messages or state.
    public sealed class RegressionClosureProcessProbe : MonoBehaviour
    {
        private string role, folder, status;
        private ushort port;
        private bool finished;
        private BootGameplayNetworkManager manager;
        private NetworkGameplayEnemySpawner spawner;
        private GameObject enemyPrefab;
        private PlayerMovement protectedPlayer;
        private PreparationMenuView hiddenMenu;
        private readonly HashSet<uint> held = new HashSet<uint>();
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkEnemySimulationWorld World => NetworkEnemySimulationWorld.Instance;
        private bool Server => role == "host" || role == "server";
        private static string Arg(string key) => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(key))?.Substring(key.Length);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string role = Arg("--regression-role=");
            if (role == null) return;
            var probe = new GameObject("Regression closure technical probe").AddComponent<RegressionClosureProcessProbe>();
            probe.role = role; probe.folder = Arg("--regression-artifacts=");
            probe.port = ushort.Parse(Arg("--regression-port="));
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            Directory.CreateDirectory(folder);
            SceneManager.sceneLoaded += Prepare;
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
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
            Finish(true);
        }

        private void Prepare(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            spawner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true)).Single();
            enemyPrefab = spawner.EnemyPrefab;
            // The explicit per-member fixture uses the same server eligibility and spawn lifecycle.
            spawner.Configure(enemyPrefab, 5);
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Boot manager");
            manager.ConfigurePreparationFlow(false);
            // Runtime-only fixture choice; neither the authored player nor its database is saved.
            manager.playerPrefab.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(402);
            Require(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", port, false, out var error), error);
            Note("helpers: authored Ovid initial weapon 402, local invulnerability, legal map placement, per-member spawns, probe enemy minimum HP 10000, pulse target navigation held/gravity zero, explicit cleanup; no pressure conclusions");
            if (role == "host") manager.StartHost();
            else if (role == "server") manager.StartServer();
            else manager.StartClient();
            if (Server)
            {
                yield return Wait(() => manager.IsGameplayLoaded && World != null, "server world");
                if (role == "server")
                {
                    yield return new WaitForSecondsRealtime(.25f);
                    Require(!World.HasEligiblePlayer && FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None).Length == 0,
                        "Dedicated server generated before an eligible player existed");
                    Note("dedicated: no eligible players -> zero generated");
                }
                Mark("listening");
            }
            if (role != "server") yield return LocalStartup();
            if (Server) yield return ServerChecks();
            else yield return ClientChecks();
            ReleaseProtection();
            if (role == "host") manager.StopHost();
            else if (role == "server") manager.StopServer();
            else manager.StopClient();
            yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "scene cleanup");
            Require(FindObjectsByType<SummonAIBehaviour>(FindObjectsSortMode.None).Length == 0, "Pet survived shutdown");
            Require(FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None).Length == 0, "Enemy survived shutdown");
        }

        private IEnumerator LocalStartup()
        {
            yield return Wait(() => manager.IsGameplayLoaded && !manager.IsGameplayTransitioning &&
                Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline, "owner baseline and completed scene load");
            // This explicit legacy fixture bypasses preparation, so its room phase never becomes InGame.
            hiddenMenu = FindFirstObjectByType<PreparationMenuView>();
            if (hiddenMenu != null) hiddenMenu.gameObject.SetActive(false);
            foreach (var panel in FindObjectsByType<NetworkPlayerDebugPanel>(FindObjectsSortMode.None)) panel.enabled = false;
            foreach (var panel in FindObjectsByType<NetworkEnemyDebugPanel>(FindObjectsSortMode.None)) panel.enabled = false;
            Note("fixture presentation: preparation overlay and debug panels hidden; gameplay camera retained");
            var player = Owner.GetComponent<PlayerMovement>();
            protectedPlayer = player;
            player.SetInvulnerable(true);
            var map = GameplayMapContext.For(Owner.gameObject);
            yield return Wait(() => map != null && map.IsReady, "map");
            Vector2 requested = (Vector2)map.Bounds.center + (role == "host" ? Vector2.left : Vector2.right) * 7;
            Vector2 position = map.FindSpawn(requested, 7);
            map.Place(player.body, Owner.GetComponent<CircleCollider2D>(), position);
            player.SetDirection(Vector2.zero);
            var summon = Owner.GetComponent<PlayerBuildRuntime>().GetWeaponAtSlot(0) as SummonAttackBehaviour;
            Require(summon != null, "authored Summon equip");
            summon.PresentationTerminated += e => Note($"pet terminated id={e.PetId} enabled={summon.enabled}");
            yield return Wait(() => summon.ActiveSummon != null, "fresh pet");
            ulong pet = summon.PetId;
            Note($"fresh pet={pet} phase={summon.ActiveSummon.Phase} sourceDelay={summon.InitialMaturityDelay} maturity={summon.MaturityAt:R}");
            Require(summon.ActiveSummon.Phase == SummonPhase.Cocoon, "Fresh equipment skipped Cocoon");
            yield return Capture("cocoon");
            yield return new WaitForSeconds(.5f);
            Require(summon.PetId == pet && summon.ActiveSummon != null, "Cocoon was terminated by an execution race");
            Owner.GetComponent<PlayerBuildRuntime>().SetWeaponExecutionEnabled(false);
            var dash = Owner.GetComponent<NetworkPlayerDash>();
            yield return Wait(() => dash.HasOwnerBaseline, "dash baseline");
            Vector2 start = player.body.position;
            int charges = dash.OwnerRuntime.AvailableCharges;
            int starts = 0, rejected = 0;
            Action started = () => starts++;
            Action<ulong> denied = _ => rejected++;
            player.OnDashStart += started;
            dash.OwnerUseRejected += denied;
            player.SetDirection(Vector2.right); player.Dash();
            yield return Wait(() => starts == 1 && dash.PendingOwnerUseCount == 0 &&
                dash.OwnerRuntime.AvailableCharges == charges - 1, "owner dash prediction acknowledged");
            Require(rejected == 0, "Legal owner dash was rejected");
            player.OnDashStart -= started;
            dash.OwnerUseRejected -= denied;
            yield return Capture("dash");
            yield return Wait(() => dash.PendingOwnerUseCount == 0 && Vector2.Distance(start, player.body.position) > .2f, "dash moves inside map");
            var circle = Owner.GetComponent<CircleCollider2D>();
            Require(map.IsFree(player.body.position, GameplayMapContext.Radius(circle), GameplayMapContext.Offset(circle, player.transform)),
                "Dash escaped the legal map footprint");
            Note($"dash start={start} end={player.body.position} ownerCharges={dash.OwnerRuntime.AvailableCharges} pending={dash.PendingOwnerUseCount} rejected={rejected}");
            player.CancelDash(); player.SetDirection(Vector2.zero);
            Mark("ready-" + role, Owner.netId.ToString());
        }

        private IEnumerator ServerChecks()
        {
            yield return Wait(() => Seen("ready-client"), "real Client ready");
            uint client = uint.Parse(Read("ready-client"));
            Require(World.TryGetEligiblePlayer(client, out _), "Client not eligible after real socket registration");
            var canonicalDash = NetworkServer.spawned[client].GetComponent<NetworkPlayerDash>();
            Require(canonicalDash.AcceptedUseCount == 1 && canonicalDash.RejectedUseCount == 0,
                "Client dash must be accepted exactly once by the server");
            Note($"Client dash canonical accepted={canonicalDash.AcceptedUseCount} rejected={canonicalDash.RejectedUseCount}");
            int expected = role == "host" ? 2 : 1;
            yield return Wait(() => spawner.SpawnedPlayerCount == expected, "one server spawn per connected participant");
            Note($"spawned={spawner.SpawnedPlayerCount} participants={expected}");
            spawner.enabled = false;
            foreach (var existing in FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None)) NetworkServer.Destroy(existing.gameObject);
            if (!manager.Session.IsRunStarted) manager.BeginRun();

            var boss = Spawn(client, EnemySimulationMode.BossServer, Vector2.up * 4);
            yield return Wait(() => boss.ProductEnemyInitialized && boss.Authority.Role == EnemySimulationRole.ServerAuthoritative, "Boss server role");
            Vector2 bossStart = boss.transform.position;
            Mark("boss", boss.netId.ToString());
            yield return Wait(() => Vector2.Distance(bossStart, boss.transform.position) > .1f, "Boss server navigation");
            yield return Wait(() => Seen("boss-seen"), "Client sees Boss replica");
            Note($"boss id={boss.netId} host={boss.Assignment.Host} owner={boss.Assignment.SimulationOwnerPlayerId} movement={Vector2.Distance(bossStart,boss.transform.position)}");
            NetworkServer.Destroy(boss.gameObject);

            var normal = Spawn(client, EnemySimulationMode.NormalClient, Vector2.right * 2);
            Mark("pulse-enemy", normal.netId.ToString());
            yield return Wait(() => Settled(normal, client), "Client simulation acknowledged");
            NetworkServer.spawned[client].GetComponent<NetworkPlayerUltimate>().ServerGrantCharge();
            Mark("pulse");
            yield return Wait(() => World.Registry.TryGetLatestSnapshot(normal.netId, out var s) && s.Runtime.Knockback.Active, "real Ultimate impulse checkpoint");
            World.Registry.TryGetLatestSnapshot(normal.netId, out var before);
            Note($"ultimate id={normal.netId} start={before.Runtime.Knockback.Start} end={before.Runtime.Knockback.End} elapsed={before.Runtime.Knockback.Elapsed}");
            if (role == "host")
            {
                Require(World.RequestTargetChange(normal.netId, Owner.netId, EnemyTargetChangeReason.Forced) == EnemyTargetChangeResult.Accepted, "handoff request");
                yield return Wait(() => Settled(normal, Owner.netId), "impulse handoff acknowledged");
                var inherited = normal.Handoff.Checkpoint.Movement.Runtime.Knockback;
                Require(inherited.Start == before.Runtime.Knockback.Start && inherited.End == before.Runtime.Knockback.End,
                    "Handoff regenerated the impulse trajectory");
                Note($"handoff id={normal.netId} epoch={normal.Assignment.Epoch} inheritedElapsed={inherited.Elapsed}");
                yield return Capture("handoff");
            }
            yield return Wait(() => Seen("pulse-observed"), "Client pulse evidence");
            NetworkServer.Destroy(normal.gameObject);
            Mark("stop-snapshots");
            yield return Wait(() => Seen("snapshots-stopped"), "Client intentionally stops simulation endpoint");
            var fallback = Spawn(client, EnemySimulationMode.NormalClient, Vector2.up * 4);
            Mark("fallback", fallback.netId.ToString());
            Vector2 fallbackStart = fallback.transform.position;
            yield return Wait(() => fallback.Assignment.Host == EnemySimulationHost.ServerFallback, "missing first snapshot causes server fallback");
            yield return Wait(() => Vector2.Distance(fallbackStart, fallback.transform.position) > .1f, "fallback moves on server");
            Note($"fallback id={fallback.netId} epoch={fallback.Assignment.Epoch} role={fallback.Authority.Role}");
            Mark("fallback-running");
            yield return Wait(() => Seen("fallback-seen"), "Client sees fallback position");
            Mark("resume-snapshots");
            yield return Wait(() => Settled(fallback, client), "Client recovers simulation through readiness handshake");
            Note($"takeover id={fallback.netId} epoch={fallback.Assignment.Epoch} owner={client}");
            Mark("complete");
            yield return Wait(() => Seen("client-complete"), "Client completed observations");
        }

        private IEnumerator ClientChecks()
        {
            yield return Wait(() => Seen("boss"), "Boss spawned");
            uint bossId = uint.Parse(Read("boss"));
            yield return Wait(() => Agent(bossId) != null && Agent(bossId).AppliedHandoffEpoch == Agent(bossId).Assignment.Epoch, "Boss replication");
            var boss = Agent(bossId);
            Require(boss.Assignment.Host == EnemySimulationHost.ServerAuthoritative && !boss.Authority.RunsNavigation, "Client runs Boss simulation");
            yield return Capture("boss"); Mark("boss-seen");
            yield return Wait(() => Seen("pulse"), "Ultimate charge grant");
            var enemy = Agent(uint.Parse(Read("pulse-enemy")));
            yield return Wait(() => enemy != null && enemy.Authority.RunsNavigation, "client owns pulse target");
            var ultimate = Owner.GetComponent<NetworkPlayerUltimate>();
            yield return Wait(() => ultimate.HasCharge, "owner received canonical Ultimate charge");
            Require(ultimate.RequestUse(), "real Ultimate request");
            yield return Wait(() => enemy.AppliedUltimateKnockbackCount == 1, "one pulse execution");
            yield return Capture("ultimate");
            Require(enemy.AppliedUltimateKnockbackCount == 1, "duplicate pulse"); Mark("pulse-observed");
            yield return Wait(() => Seen("stop-snapshots"), "fallback phase");
            var endpoint = Owner.GetComponent<NetworkEnemySimulationEndpoint>(); endpoint.enabled = false;
            Note("test helper: endpoint disabled on Client only to exercise real server timeout"); Mark("snapshots-stopped");
            yield return Wait(() => Seen("fallback-running"), "server fallback");
            uint id = uint.Parse(Read("fallback"));
            yield return Wait(() => Agent(id) != null && Agent(id).Assignment.Host == EnemySimulationHost.ServerFallback, "replicated fallback");
            Require(!Agent(id).Authority.RunsNavigation, "Client duplicates fallback navigation");
            yield return Capture("fallback"); Mark("fallback-seen");
            yield return Wait(() => Seen("resume-snapshots"), "resume"); endpoint.enabled = true;
            yield return Wait(() => Seen("complete"), "server completion");
            Require(Agent(id).Assignment.SimulationOwnerPlayerId == Owner.netId, "Client did not regain simulation");
            yield return Capture("takeover"); Mark("client-complete");
        }

        private NetworkEnemySimulationAgent Spawn(uint target, EnemySimulationMode mode, Vector2 offset)
        {
            Vector3 position = NetworkServer.spawned[target].transform.position + (Vector3)offset;
            var instance = Instantiate(enemyPrefab, position, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(instance, NetworkServer.spawned[target].gameObject.scene);
            var controller = instance.GetComponent<EnemyController>();
            var footprint = controller.collider as CircleCollider2D;
            Require(footprint != null, "Probe prefab must have its authored body footprint");
            GameplayMapContext.For(instance).Place(instance.GetComponent<Rigidbody2D>(), footprint, position);
            instance.GetComponent<EnemySimulationAuthority>().ConfigureNetworkManaged(mode, false);
            var enemy = instance.GetComponent<NetworkEnemySimulationAgent>();
            enemy.ConfigureRuntimeMinimumHealthOverride(10000); enemy.ConfigureInitialServerTarget(target);
            NetworkServer.Spawn(instance); return enemy;
        }
        private bool Settled(NetworkEnemySimulationAgent enemy, uint target) => enemy != null &&
            enemy.Assignment.Host == EnemySimulationHost.ClientPlayer && enemy.Assignment.SimulationOwnerPlayerId == target &&
            World.TryReadHandoff(enemy.netId, out var state) && !state.AwaitingFirstSnapshot;
        private static NetworkEnemySimulationAgent Agent(uint id) => NetworkClient.spawned.TryGetValue(id, out var identity)
            ? identity.GetComponent<NetworkEnemySimulationAgent>() : null;
        private void Update()
        {
            if (finished) return;
            // Hold only the pulse target so navigation does not contaminate its impulse measurement.
            if (!Seen("pulse-enemy")) return;
            uint id = uint.Parse(Read("pulse-enemy"));
            var enemy = NetworkServer.active && NetworkServer.spawned.TryGetValue(id, out var entity)
                ? entity.GetComponent<NetworkEnemySimulationAgent>() : Agent(id);
            if (enemy == null || !enemy.ProductEnemyInitialized || !held.Add(id)) return;
            enemy.GetComponent<EnemyController>().Movement.StopMovement();
            enemy.GetComponent<Rigidbody2D>().gravityScale = 0;
        }
        private IEnumerator Capture(string step)
        {
            status = step; Note("capture " + step);
            if (role == "server") yield break;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, role + "-" + step + ".png"));
            yield return null;
        }
        private void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 640, 60), $"Regression technical verification | {role} | {status}\nExplicit fixture assistance; not a pressure test");
        }
        private IEnumerator Wait(Func<bool> condition, string message)
        {
            status = message; float until = Time.realtimeSinceStartup + 35;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Require(condition(), message); Note("passed " + message);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Note(string message) => Debug.Log($"[RegressionClosure] role={role} frame={Time.frameCount} time={NetworkTime.time:R} {message}");
        private bool Seen(string file) => folder != null && File.Exists(Path.Combine(folder, file));
        private string Read(string file) => File.ReadAllText(Path.Combine(folder, file));
        private void Mark(string file, string value = "ready")
        {
            string path = Path.Combine(folder, file);
            string pending = path + "." + role + ".pending";
            File.WriteAllText(pending, value);
            // Readers see only complete, closed step markers (Windows denies reads during WriteAllText).
            File.Move(pending, path);
        }
        private void Finish(bool passed)
        {
            ReleaseProtection();
            if (hiddenMenu != null) hiddenMenu.gameObject.SetActive(true);
            finished = true; SceneManager.sceneLoaded -= Prepare;
            Note("result=" + (passed ? "PASS" : "FAIL")); Mark(role + "-result", passed ? "PASS" : "FAIL");
            Application.Quit(passed ? 0 : 1);
        }
        private void ReleaseProtection()
        {
            if (protectedPlayer == null) return;
            protectedPlayer.SetInvulnerable(false);
            protectedPlayer = null;
        }
    }
}
