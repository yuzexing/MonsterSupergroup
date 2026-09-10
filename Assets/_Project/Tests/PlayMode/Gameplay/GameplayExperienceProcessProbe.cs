using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Tests
{
    /// <summary>Opt-in Boot fixture. Markers coordinate assertions; all pickups use the production Command.</summary>
    public sealed class GameplayExperienceProcessProbe : MonoBehaviour
    {
        private string role, directory;
        private ushort port;
        private bool dedicated, capture, simulation, finished, collecting;
        private int errors;
        private float deadline;
        private Vector2? holdPosition;
        private BootGameplayNetworkManager manager;
        private KcpLocalNetworkService service;
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkExperienceWorld World => NetworkExperienceWorld.Current;
        private NetworkModifierSelection Progression => Owner.GetComponent<NetworkModifierSelection>();
        private bool IsServer => role == "server" || role == "host";
        private string Other => dedicated ? "client2" : "host";
        private static NetworkIdentity[] Players() => NetworkServer.spawned.Values.Where(p => p != null && p.GetComponent<NetworkRunParticipant>() != null).ToArray();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            string value = args.FirstOrDefault(a => a.StartsWith("--m6-role="));
            if (value == null) return;
            var probe = new GameObject("M6 formal XP validation").AddComponent<GameplayExperienceProcessProbe>();
            probe.role = value.Substring(10);
            probe.directory = args.First(a => a.StartsWith("--m6-artifacts=")).Substring(15);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--m6-port=")).Substring(10));
            probe.dedicated = args.Contains("--m6-dedicated"); probe.capture = args.Contains("--m6-capture");
            probe.simulation = args.Contains("--m6-simulation");
            DontDestroyOnLoad(probe.gameObject);
        }
        private IEnumerator Start()
        {
            Application.logMessageReceived += ObserveLog;
            Application.runInBackground = true; deadline = Time.realtimeSinceStartup + 300;
            gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Formal Boot is required.");
            service = manager.GetComponent<KcpLocalNetworkService>();
            if (role == "host") StartCoroutine(Guard(ClientScenario()));
            yield return Guard(IsServer ? ServerScenario() : ClientScenario());
            if (!finished) Finish(true);
        }
        private void Update()
        {
            if (!finished && deadline > 0 && Time.realtimeSinceStartup > deadline) { Debug.LogError("[M6Process] timeout " + role); Finish(false); }
            if (Owner != null)
            {
                Owner.GetComponent<NetworkExperienceCollector>().enabled = collecting;
                Owner.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(true);
                if (holdPosition.HasValue) MoveOwner(holdPosition.Value);
            }
            if (NetworkServer.active && NetworkCombatWorld.Instance != null)
                foreach (var player in Players()) NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(player.netId, true);
            // Only the assigned simulator is stopped, leaving M3 and its snapshots in control of displacement.
            foreach (var agent in FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None))
                if (agent.ProductEnemyInitialized && agent.Authority.RunsNavigation)
                { var enemy = agent.GetComponent<EnemyController>(); enemy.Movement.StopMovement(); enemy.rigidBody.gravityScale = 0; }
        }
        private void StartRole(string next)
        {
            Require(service.TrySetConfiguration("127.0.0.1", port, simulation, out string error), error);
            if (simulation)
            {
                var lag = manager.GetComponent<NetworkBackendBootstrap>().ConfiguredLatencySimulation as LatencySimulation;
                Require(lag != null, "Latency simulator missing."); lag.latency = 80; lag.unreliableLoss = 10;
            }
            if (next == "host") service.StartHost(); else if (next == "server") service.StartServer(); else service.StartClient();
        }
        private IEnumerator ServerScenario()
        {
            StartRole(role); Mark("listening");
            yield return Wait(() => Has("ready-client-1") && Has("ready-" + Other + "-1") && manager.CanBeginRun(out _), "party");
            Require(World.UnclaimedCount == 0, "New run retained drops.");
            manager.BeginRun();
            uint attacker = uint.Parse(Read("ready-client-1"));
            var spawner = FindFirstObjectByType<NetworkGameplayEnemySpawner>();
            // This extra runtime target isolates one real Circling kill while the formal wave clock continues.
            var target = Instantiate(spawner.EnemyPrefab, new Vector3(18, 12, 0), Quaternion.identity);
            SceneManager.MoveGameObjectToScene(target, NetworkServer.spawned[attacker].gameObject.scene);
            target.GetComponent<NetworkEnemySimulationAgent>().ConfigureInitialServerTarget(attacker);
            NetworkServer.Spawn(target); Mark("kill", target.GetComponent<NetworkIdentity>().netId.ToString());
            yield return Wait(() => World.UnclaimedCount == 1, "real kill drop", 40);
            Require(Players().All(p => p.GetComponent<NetworkModifierSelection>().Experience == 0), "Direct killer XP remains.");
            var gem = World.Unclaimed.Single();
            Mark("drop", gem.DropId.ToString()); WritePosition("position", gem.transform.position);
            yield return Wait(() => Has("seen-client") && Has("seen-" + Other), "drop replicas");
            yield return ReconnectServer("before-request");
            Require(World.UnclaimedCount == 1, "Disconnect removed unclaimed XP.");
            Mark("approach");
            yield return Wait(() => Has("near-client") && Has("near-" + Other), "both contenders");
            yield return new WaitForSecondsRealtime(.8f); Mark("race");
            yield return Wait(() => World.UnclaimedCount == 0, "first legal claim");
            Require(Players().Sum(p => p.GetComponent<NetworkModifierSelection>().Experience) == 4, "Concurrent claims must award 4 XP total.");
            Mark("race-done");
            yield return Wait(() => Has("race-client") && Has("race-" + Other), "duplicate claims and XP HUDs");
            Require(Players().Sum(p => p.GetComponent<NetworkModifierSelection>().Experience) == 4, "Duplicate request awarded XP.");
            Debug.Log("[M6Process] event=two-player-race-single-award");

            // A second confirmed-death boundary fixture makes disconnect during the flight deterministic.
            uint current = uint.Parse(Read("rejoined-before-request"));
            var client = NetworkServer.spawned[current];
            CreateBoundaryDrop(client);
            Mark("flight");
            yield return Wait(() => Has("offline-flight") && !NetworkServer.spawned.ContainsKey(current), "disconnect in flight");
            Require(World.UnclaimedCount == 0, "Confirmed flight left an awardable gem.");
            CreateBoundaryDrop(Players().First()); // Must survive this client's reconnect.
            Mark("resume-flight");
            yield return Wait(() => Has("rejoined-flight"), "flight checkpoint");
            current = uint.Parse(Read("rejoined-flight")); client = NetworkServer.spawned[current];
            var progression = client.GetComponent<NetworkModifierSelection>();
            float previous = progression.Experience;
            progression.ServerGrantExperience(Enumerable.Range(progression.Level, 6).Sum(progression.ExperienceRequiredAtLevel) + .5f);
            Require(progression.Level == 7 && progression.Experience == previous + .5f && progression.PendingUpgradeCount == 6,
                "Bulk progression lost XP or rewards.");
            Mark("bulk");
            yield return Wait(() => Has("selection-blocked"), "selection pickup pause");
            Require(World.UnclaimedCount == 1, "Selecting player collected new XP.");
            yield return ReconnectServer("target-stage");
            yield return Wait(() => Has("restored-choice"), "reward restore and apply");
            Require(World.UnclaimedCount == 1, "Reconnect replayed an old pickup.");
            Mark("finish");
            yield return Wait(() => Has("finished-client") && Has("finished-" + Other), "clients finished");
            string run = World.RunId;
            service.Stop();
            yield return Wait(() => service.CanStart && !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Stop cleanup");
            Require(FindObjectsByType<NetworkExperienceGem>(FindObjectsSortMode.None).Length == 0, "Stop retained gems.");
            StartRole(role); yield return Wait(() => World != null && World.CanGrant(out _), "fresh World");
            Require(World.RunId != run && World.UnclaimedCount == 0, "New run reused loot state.");
            service.Stop(); yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "final Stop");
            Debug.Log("[M6Process] event=reconnect-and-stop-clean");
        }
        private IEnumerator ClientScenario()
        {
            yield return Wait(() => Has("listening"), "listening");
            if (role != "host") StartRole("client");
            yield return Wait(OwnerReady, "Owner baseline");
            Mark("ready-" + role + "-1", Owner.netId.ToString());
            if (role == "client")
            {
                yield return Wait(() => Has("kill"), "kill target");
                uint id = uint.Parse(Read("kill"));
                yield return Wait(() => NetworkClient.spawned.ContainsKey(id) && NetworkClient.spawned[id].GetComponent<NetworkEnemySimulationAgent>().ProductEnemyInitialized, "enemy ready");
                var enemy = NetworkClient.spawned[id].GetComponent<EnemyController>();
                var weapon = (CirclingAttackBehaviour)Owner.GetComponent<PlayerBuildRuntime>().InitialWeapon;
                weapon.baseSpeed = 0; float next = 0, until = Time.realtimeSinceStartup + 35;
                while (enemy != null && Time.realtimeSinceStartup < until)
                {
                    Vector2 offset = weapon.transform.TransformPoint(Vector3.right * (weapon.baseRadius * weapon.SizeValue)) - Owner.transform.position;
                    var orb = weapon.GetComponentsInChildren<AnimatedAttack>().FirstOrDefault(a => a.hitbox != null && a.hitbox.collider.enabled);
                    if (orb != null) offset = orb.hitbox.collider.bounds.center - Owner.transform.position;
                    MoveOwner((Vector2)enemy.hurtBox.GetBounds().center - offset);
                    if (Time.time >= next) { weapon.Attack(); next = Time.time + weapon.GetAttackSequenceDuration() + weapon.GetCooldown() + .3f; }
                    yield return new WaitForFixedUpdate();
                }
                Require(enemy == null, "Real Circling failed to kill the network target.");
            }
            yield return Wait(() => Has("drop") && NetworkExperienceGem.ClientGems.Any(), "XP spawn baseline");
            Require(Progression.Experience == 0, "Kill gave direct XP.");
            if (capture) yield return Capture("unclaimed");
            Mark("seen-" + role);
            if (role == "client") yield return ReconnectClient("before-request");
            yield return Wait(() => Has("approach"), "approach");
            holdPosition = ReadPosition("position") + Vector2.left;
            yield return new WaitForSecondsRealtime(.5f);
            if (capture) yield return Capture("before-race");
            Mark("near-" + role);
            yield return Wait(() => Has("race"), "race signal"); collecting = true;
            yield return Wait(() => Has("race-done") && !NetworkExperienceGem.ClientGems.Any(), "claim confirmed");
            yield return new WaitForSecondsRealtime(.5f); collecting = false;
            float xp = Progression.Experience;
            Require(xp == 0 || xp == 4, "Partial or duplicate race award.");
            var collector = Owner.GetComponent<NetworkExperienceCollector>();
            collector.enabled = true; collector.RequestCollection(World.RunId, ulong.Parse(Read("drop"))); collector.enabled = false;
            yield return new WaitForSecondsRealtime(.5f); Require(Progression.Experience == xp, "Replay changed XP.");
            Require(FindFirstObjectByType<PlayerExperienceHUD>().Content.Contains("/ 19"), "XP HUD missing server threshold.");
            if (capture) yield return Capture("race-award"); Mark("race-" + role);
            if (role == "client")
            {
                yield return Wait(() => Has("flight") && NetworkExperienceGem.ClientGems.Any(), "second gem");
                collecting = true;
                yield return Wait(() => Progression.Experience == xp + 4, "confirmed pickup");
                collecting = false; float awarded = Progression.Experience;
                service.Stop(); yield return Wait(CanRestart, "flight cleanup"); Mark("offline-flight");
                Require(FindObjectsByType<ExperienceCollectionFlight>(FindObjectsSortMode.None).Length == 0, "Flight survived disconnect.");
                yield return Wait(() => Has("resume-flight"), "flight resume"); StartRole("client");
                yield return Wait(OwnerReady, "flight restored"); Require(Progression.Experience == awarded, "Committed XP not restored.");
                Mark("rejoined-flight", Owner.netId.ToString());
                yield return Wait(() => Has("bulk") && Progression.Level == 7 && Progression.IsSelecting, "bulk rewards");
                Require(Progression.Experience == awarded + .5f, "Bulk remainder mismatch.");
                yield return Wait(() => NetworkExperienceGem.ClientGems.Any(), "unclaimed restore");
                holdPosition = NetworkExperienceGem.ClientGems.First().transform.position;
                collecting = true; yield return new WaitForSecondsRealtime(.6f); collecting = false;
                Require(NetworkExperienceGem.ClientGems.Any(), "Selection collected an unclaimed gem.");
                var view = Owner.GetComponent<ModifierSelectionController>();
                Require(view.Select(0).Succeeded, "Equipment card failed.");
                yield return Wait(() => view.Stage == UpgradeSelectionStage.EquipmentTarget && !view.IsRequestPending, "target phase");
                var options = view.Offers.Select(o => o.ContentId).ToArray();
                if (capture) yield return Capture("level-7-target"); Mark("selection-blocked");
                yield return ReconnectClient("target-stage");
                view = Owner.GetComponent<ModifierSelectionController>();
                yield return Wait(() => view.Stage == UpgradeSelectionStage.EquipmentTarget && view.Offers.Count > 0, "restored target phase");
                Require(view.Offers.Select(o => o.ContentId).SequenceEqual(options) && Progression.Level == 7 && Progression.Experience == awarded + .5f,
                    "Reconnect rerolled options or lost progression.");
                Require(view.Select(0).Succeeded, "Restored target submission failed.");
                yield return Wait(() => Progression.OwnerBuildRevision == 2, "restored Build"); Mark("restored-choice");
            }
            yield return Wait(() => Has("finish"), "finish");
            Mark("finished-" + role);
            if (role != "host") { service.Stop(); yield return Wait(CanRestart, "client Stop"); }
        }
        private IEnumerator ReconnectServer(string phase)
        {
            Mark("disconnect-" + phase);
            yield return Wait(() => Has("offline-" + phase) && Players().Length == 1, "offline " + phase);
            Mark("resume-" + phase); yield return Wait(() => Has("rejoined-" + phase), "rejoined " + phase);
        }
        private IEnumerator ReconnectClient(string phase)
        {
            yield return Wait(() => Has("disconnect-" + phase), "disconnect " + phase);
            string run = World.RunId; service.Stop(); yield return Wait(CanRestart, "cleanup " + phase); Mark("offline-" + phase);
            yield return Wait(() => Has("resume-" + phase), "resume " + phase); StartRole("client"); yield return Wait(OwnerReady, "baseline " + phase);
            Require(World.RunId == run, "Reconnect changed the run."); Mark("rejoined-" + phase, Owner.netId.ToString());
        }
        private void CreateBoundaryDrop(NetworkIdentity target)
        {
            var spawner = FindFirstObjectByType<NetworkGameplayEnemySpawner>();
            var enemy = Instantiate(spawner.EnemyPrefab, target.transform.position, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(enemy, target.gameObject.scene);
            enemy.GetComponent<NetworkEnemySimulationAgent>().ConfigureInitialServerTarget(target.netId); NetworkServer.Spawn(enemy);
            typeof(NetworkExperienceWorld).GetMethod("OnConfirmedKill", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(World,
                new object[] { new ConfirmedKill { TargetEntityId = enemy.GetComponent<NetworkIdentity>().netId, TargetStateVersion = 2, CauseEventId = 999 } });
            NetworkServer.Destroy(enemy);
        }
        private bool OwnerReady() => Owner != null && Progression.HasOwnerBaseline && Progression.ExperiencePerLevel > 0 &&
            Owner.GetComponent<ModifierSelectionController>().IsPresentationReady && World != null && !string.IsNullOrEmpty(World.RunId);
        private bool CanRestart() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning && service.CanStart;
        private void MoveOwner(Vector2 position)
        {
            var player = Owner.GetComponent<PlayerMovement>(); player.SetDirection(Vector2.zero);
            player.body.position = position; player.transform.position = position; Physics2D.SyncTransforms();
        }
        private IEnumerator Wait(Func<bool> test, string label, float seconds = 30)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!test() && Time.realtimeSinceStartup < end) yield return null;
            Require(test(), "Timed out: " + label);
        }
        private IEnumerator Guard(IEnumerator routine)
        {
            while (!finished)
            {
                object next;
                try { if (!routine.MoveNext()) yield break; next = routine.Current; }
                catch (Exception error) { Debug.LogException(error); Finish(false); yield break; }
                if (next is IEnumerator child) yield return Guard(child); else yield return next;
            }
        }
        private IEnumerator Capture(string name)
        { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(directory, role + "-" + name + ".png")); yield return null; }
        private bool Has(string name) => File.Exists(Path.Combine(directory, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(directory, name));
        private void Mark(string name, string value = "ready") => File.WriteAllText(Path.Combine(directory, name), value);
        private void WritePosition(string name, Vector2 p) => Mark(name, p.x.ToString(CultureInfo.InvariantCulture) + "," + p.y.ToString(CultureInfo.InvariantCulture));
        private Vector2 ReadPosition(string name) { var parts = Read(name).Split(','); return new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture)); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void ObserveLog(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
        private void OnDestroy() => Application.logMessageReceived -= ObserveLog;
        private void Finish(bool success)
        {
            if (finished) return; finished = true; success &= errors == 0;
            Debug.Log("[M6Process] result=" + (success ? "PASS" : "FAIL") + " role=" + role + " loggedErrors=" + errors);
            Application.Quit(success ? 0 : 1);
        }
    }
}
