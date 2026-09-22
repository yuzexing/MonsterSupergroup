#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player;
using Com.LuisPedroFonseca.ProCamera2D;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Installed only by the validation command line. File messages synchronize test phases;
    // casts, view reports, movement snapshots and takeover use production Mirror routes.
    public sealed class AllurePrototypeProcessProbe : MonoBehaviour
    {
        private string role, artifacts, failure;
        private ushort port;
        private bool impaired, finished;
        private double deadline;
        private BootGameplayNetworkManager manager;
        private EnemyDefinitionRuntimeFixture enemies;
        private uint hostId, clientId;
        private int commandSequence;
        private MotionAudit audit;
        private readonly List<string> evidence = new List<string>();
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkPlayerAllure Allure => Owner.GetComponent<NetworkPlayerAllure>();
        private NetworkPlayerPrototypeAbilities Abilities => Owner.GetComponent<NetworkPlayerPrototypeAbilities>();
        private NetworkEnemySimulationWorld World => NetworkEnemySimulationWorld.Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            string selected = args.FirstOrDefault(a => a.StartsWith("--allure-role="));
            if (selected == null) return;
            var probe = new GameObject("Allure two-process validation").AddComponent<AllurePrototypeProcessProbe>();
            probe.role = selected.Substring("--allure-role=".Length);
            probe.artifacts = Path.GetFullPath(args.First(a => a.StartsWith("--allure-artifacts=")).Substring("--allure-artifacts=".Length));
            string requestedPort = args.FirstOrDefault(a => a.StartsWith("--allure-port="));
            probe.port = requestedPort == null ? (ushort)7999 : ushort.Parse(requestedPort.Substring("--allure-port=".Length));
            probe.impaired = args.Contains("--allure-impaired");
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 120;
            Application.logMessageReceived += ObserveLog;
            deadline = Time.realtimeSinceStartupAsDouble + 220;
            yield return Guard(Run());
            if (!finished) Finish(true);
        }

        private void Update()
        {
            if (finished || deadline == 0) return;
            if (failure == null && audit != null)
            {
                try { audit.Sample(World); }
                catch (Exception error) { failure = error.ToString(); }
            }
            if (failure != null) Finish(false);
            else if (Time.realtimeSinceStartupAsDouble > deadline)
            { failure = "Timed out; inspect the last phase marker and role log."; Finish(false); }
            else if (Has("failed-" + (role == "host" ? "client" : "host")))
            { failure = "Peer failed: " + Read("failed-" + (role == "host" ? "client" : "host")); Finish(false); }
        }

        private IEnumerator Run()
        {
            Require(role == "host" || role == "client", "Expected --allure-role=host or client.");
            Directory.CreateDirectory(artifacts);
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Allure validation must start through Boot.");
            manager.ConfigurePreparationFlow(false);
            enemies = new EnemyDefinitionRuntimeFixture(manager, resetOnReposition: false);
            gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            var backend = manager.GetComponent<NetworkBackendBootstrap>();
            Require(backend.TryPrepareKcp("127.0.0.1", port, impaired, out string error), error);
            if (impaired)
            {
                var simulation = backend.ConfiguredLatencySimulation as LatencySimulation;
                Require(simulation != null, "Missing latency simulation.");
                simulation.latency = 100; simulation.jitter = .02f;
                simulation.unreliableLoss = 0; simulation.unreliableScramble = 0;
            }
            if (role == "host")
            {
                manager.StartHost();
                var gluttony = GluttonyParameters.Defaults; gluttony.PassiveEnabled = false;
                NetworkCombatWorld.Instance.ServerConfigureGluttony(gluttony, false);
                Mark("listening");
            }
            else
            {
                while (!Has("listening")) yield return null;
                manager.StartClient();
            }
            StartCoroutine(Guard(RunOwner()));
            if (role == "host")
            {
                yield return RunServer();
                while (!Has("stopped-client")) yield return null;
                manager.StopHost();
            }
            else
            {
                while (!Has("server-verified")) yield return null;
                if (NetworkClient.active) manager.StopClient();
            }
            while (NetworkClient.active || NetworkServer.active || manager.IsGameplayLoaded || manager.IsGameplayTransitioning)
                yield return null;
            Require(FindObjectsByType<NetworkPlayerAllure>(FindObjectsSortMode.None).Length == 0,
                "An Allure module survived Gameplay shutdown.");
            if (!Has("stopped-" + role)) Mark("stopped-" + role);
        }

        private IEnumerator RunOwner()
        {
            while (Owner == null || !Abilities.OwnerReady) yield return null;
            Owner.GetComponent<PlayerMovement>().SetDirection(Vector2.zero);
            yield return Select(PrototypeAbilityId.Allure);
            Mark("ready-" + role, Owner.netId.ToString());
            for (int sequence = 1; !finished; sequence++)
            {
                string name = "command-" + sequence;
                while (!Has(name)) yield return null;
                var command = JsonUtility.FromJson<OwnerCommand>(Read(name));
                if (command.role != role) continue;
                if (command.kind == "move")
                {
                    var player = Owner.GetComponent<PlayerMovement>();
                    player.SetDirection(Vector2.zero);
                    player.body.position = new Vector2(command.x, command.y);
                    player.transform.position = new Vector3(command.x, command.y, player.transform.position.z);
                    Physics2D.SyncTransforms();
                    var camera = FindFirstObjectByType<GameplayCameraRig>();
                    if (camera != null) camera.GetComponent<ProCamera2D>().Reset();
                    // Allow normal movement replication and camera reports to traverse the transport.
                    yield return new WaitForSecondsRealtime(.8f);
                }
                else if (command.kind == "select") yield return Select((PrototypeAbilityId)command.value);
                else if (command.kind == "pause-views" || command.kind == "resume-views")
                {
                    // Pause this outgoing stream only; keep its real camera and simulation stream active.
                    typeof(NetworkEnemySimulationEndpoint).GetField("nextViewReport",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .SetValue(Owner.GetComponent<NetworkEnemySimulationEndpoint>(),
                            command.kind == "pause-views" ? Time.unscaledTimeAsDouble + 60 : 0d);
                }
                else if (command.kind == "cast")
                {
                    var action = (AllureAction)command.value;
                    while (Allure.CooldownRemaining(action) > 0) yield return null;
                    Require(Allure.RequestAction(action), role + " could not send " + action);
                    ulong request = Allure.LastRequestId;
                    while (Allure.LastResolvedRequestId != request) yield return null;
                    Require(Allure.LastRequestAccepted, role + " server rejected " + action + ": " + Allure.LastResult);
                    Require(Allure.State.LastAffectedCount > 0, "Accepted cast affected no enemy.");
                    Debug.Log($"[AllureProcess] cast role={role} action={action} request={request} cast={Allure.State.LastCastId} count={Allure.State.LastAffectedCount}");
                }
                else if (command.kind == "disconnect")
                {
                    manager.StopClient();
                    while (NetworkClient.active || manager.IsGameplayLoaded || manager.IsGameplayTransitioning) yield return null;
                    Mark("stopped-client");
                }
                else throw new InvalidOperationException("Unknown owner command " + command.kind);
                Mark("done-" + sequence);
                if (command.kind == "disconnect") yield break;
            }
        }

        private IEnumerator RunServer()
        {
            while (!Has("ready-host") || !Has("ready-client") || !manager.CanBeginRun(out _)) yield return null;
            hostId = uint.Parse(Read("ready-host")); clientId = uint.Parse(Read("ready-client"));
            Require(hostId != clientId, "Owners share an identity.");
            var parameters = AllureParameters.Defaults;
            parameters.ThrowCooldown = parameters.TakeCooldown = parameters.DecoyCooldown = .35f;
            NetworkCombatWorld.Instance.ServerConfigureAllure(parameters, true);
            Evidence("configuration: 2 ordinary definitions, three cooldowns=.35s for repeated scenarios; decoy retains default5s; real camera reports; normal enemy speed");
            manager.BeginRun();
            while (!enemies.PairReady()) yield return null;
            yield return Move("host", Vector2.zero);
            yield return Move("client", Vector2.right * .5f);
            yield return PrepareEnemies(hostId, new Vector2(4, 2));
            var tracked = enemies.Agents().OrderBy(a => a.netId).First();

            // Overlapping screens may hand off immediately, but must produce an accepted first frame.
            uint before = tracked.Assignment.Epoch;
            yield return Cast("host", AllureAction.Throw);
            yield return Wait(() => Settled(tracked, clientId), "overlap client first snapshot");
            Require(tracked.Assignment.Epoch > before, "Overlap handoff did not renew the simulator epoch.");
            Evidence($"overlap: enemy={tracked.netId}, epoch={before}->{tracked.Assignment.Epoch}, client first snapshot={Pose(tracked).Sequence}");

            // The second accepted action must replace the first target, including pending intents.
            yield return Cast("host", AllureAction.Take);
            yield return Wait(() => Settled(tracked, hostId), "take restores host");
            uint takeVersion = tracked.TargetState.Revision;
            yield return Cast("host", AllureAction.Throw);
            yield return Cast("client", AllureAction.Throw);
            yield return Wait(() => Settled(tracked, hostId), "last throw wins");
            Require(tracked.TargetState.Revision > takeVersion, "Accepted consecutive casts did not update target version.");
            Evidence("R/T and R/R: last accepted target remains host, first snapshots acknowledged");

            // F only updates targeting. Switching leaves it alive; a later R supersedes its expiry.
            before = tracked.Assignment.Epoch;
            yield return Cast("host", AllureAction.Decoy);
            Require(tracked.HasAllureDecoy && tracked.Assignment.Epoch == before, "Decoy renewed simulation or failed to bind.");
            double expiredAt = tracked.TargetState.DecoyExpiresAt;
            yield return Command("host", "select", (int)PrototypeAbilityId.Music);
            Require(tracked.HasAllureDecoy, "Changing ability erased a live decoy.");
            yield return Command("host", "select", (int)PrototypeAbilityId.Allure);
            yield return Cast("host", AllureAction.Throw);
            yield return Wait(() => Settled(tracked, clientId), "throw overrides decoy");
            uint supersedingVersion = tracked.TargetState.Revision;
            while (NetworkTime.time <= expiredAt + .25) yield return null;
            Require(!tracked.HasAllureDecoy && tracked.TargetState.AggroPlayerId == clientId &&
                tracked.TargetState.Revision == supersedingVersion, "Old decoy expiry overwrote the later transfer.");
            Evidence("F/switch/R: switch retains F; later R clears override; old5s expiry cannot pull target back");

            // Separate by more than one actual screen. Do not move enemies after beginning audit.
            yield return Move("host", new Vector2(-18, 0));
            yield return Move("client", new Vector2(18, 0));
            yield return Wait(() => World.TryGetPlayerView(hostId, out var h) && World.TryGetPlayerView(clientId, out var c) && h.max.x < c.min.x,
                "non-overlapping camera reports");
            World.TryGetPlayerView(hostId, out var hostView);
            yield return PrepareEnemies(hostId, new Vector2(hostView.max.x - 1.5f, 0));
            Require(!World.IsInPlayerView(clientId, tracked), "Cross-screen fixture starts visible to receiver.");
            before = tracked.Assignment.Epoch;
            audit = new MotionAudit("cross-screen", tracked, hostId, clientId, before, Path.Combine(artifacts, "cross-screen-snapshots.csv"));
            yield return Cast("host", AllureAction.Throw);
            Require(tracked.TargetState.AggroPlayerId == clientId && tracked.Assignment.Epoch == before &&
                tracked.Assignment.SimulationOwnerPlayerId == hostId && tracked.AllureHandoffPending,
                "Cross-screen target did not update immediately while retaining old simulator.");
            yield return Wait(() => Settled(tracked, clientId), "walk into receiver view and accept first new simulator frame", 45);
            yield return new WaitForSecondsRealtime(.6f);
            audit.RequireContinuous();
            Evidence(audit.Summary()); audit = null;

            // Missing/stale receiver reports cannot be replaced by an estimated remote camera.
            yield return PrepareEnemies(hostId, new Vector2(hostView.max.x - 1.5f, 0));
            World.TryGetPlayerView(clientId, out var staleView);
            yield return Command("client", "pause-views");
            yield return Wait(() => !World.TryGetPlayerView(clientId, out _), "receiver report exceeds .5s age");
            before = tracked.Assignment.Epoch;
            audit = new MotionAudit("stale-view", tracked, hostId, clientId, before, Path.Combine(artifacts, "stale-view-snapshots.csv"));
            yield return Cast("host", AllureAction.Throw);
            yield return Wait(() => PlayerViewReport.Intersects(staleView, World.ServerEnemyBodyBounds(tracked)), "enemy enters actual but stale receiver view", 30);
            yield return new WaitForSecondsRealtime(.65f);
            Require(tracked.Assignment.Epoch == before && tracked.Assignment.SimulationOwnerPlayerId == hostId && tracked.AllureHandoffPending,
                "Stale receiver view allowed a normal simulator handoff.");
            yield return Command("client", "resume-views");
            yield return Wait(() => Settled(tracked, clientId), "fresh report resumes pending transfer");
            yield return new WaitForSecondsRealtime(.6f);
            audit.RequireContinuous(); Evidence(audit.Summary()); audit = null;

            // Exercise the targeting core at the pending boundary independently of F's screen selection.
            // A valid owner F is already exercised above; here we deliberately retain an in-flight intent.
            yield return PrepareEnemies(hostId, new Vector2(hostView.max.x - 1.5f, 0));
            before = tracked.Assignment.Epoch;
            audit = new MotionAudit("pending-decoy", tracked, hostId, clientId, before, Path.Combine(artifacts, "pending-decoy-snapshots.csv"));
            yield return Cast("host", AllureAction.Throw);
            // Actual camera footprints vary with the saved window settings. Cover the boundary
            // crossing with the five-second decoy, instead of spending its lifetime crossing the gap.
            yield return Wait(() => World.TryGetPlayerView(clientId, out var receiverView) &&
                GameplayCameraGeometry.MinimumOutsideDistance(World.ServerEnemyBodyBounds(tracked), new[] { receiverView }) <= 2,
                "pending enemy approaches the actual receiver view", 30);
            Require(tracked.AllureHandoffPending && tracked.Assignment.Epoch == before && !World.IsInPlayerView(clientId, tracked),
                "Pending decoy fixture missed the screen boundary.");
            World.TryGetPlayerView(clientId, out var decoyView);
            double pendingStartedAt = NetworkTime.time, pendingExpiry = pendingStartedAt + 5;
            ulong coreCast = NetworkCombatWorld.Instance.Gateway.NextServerEventId();
            Require(World.ServerApplyAllureDecoy(tracked.netId, clientId, coreCast,
                new Vector2(decoyView.min.x + 3, 0), pendingExpiry), "Pending decoy core application failed.");
            yield return Wait(() => World.IsInPlayerView(clientId, tracked), "decoy-guided enemy enters receiver view", 15);
            Require(tracked.HasAllureDecoy && tracked.Assignment.Epoch == before && tracked.Assignment.SimulationOwnerPlayerId == hostId,
                "Pending decoy failed to hold the original simulator inside receiver view.");
            Evidence($"pending-decoy boundary: started={pendingStartedAt:0.000}, expiry={pendingExpiry:0.000}, enteredView={NetworkTime.time:0.000}, decoyActive={tracked.HasAllureDecoy}, epoch={before}");
            while (NetworkTime.time < pendingExpiry - .1)
            {
                Require(tracked.Assignment.Epoch == before, "Simulator changed while the pending decoy still existed.");
                yield return null;
            }
            yield return Wait(() => !tracked.HasAllureDecoy && Settled(tracked, clientId), "expired pending decoy resumes handoff");
            yield return new WaitForSecondsRealtime(.6f);
            audit.RequireContinuous(); Evidence(audit.Summary()); audit = null;

            // Reverse the pending transfer, then remove its original simulator before it reaches host view.
            World.TryGetPlayerView(clientId, out var clientView);
            yield return PrepareEnemies(clientId, new Vector2(clientView.min.x + 1.5f, 0));
            Require(!World.IsInPlayerView(hostId, tracked), "Disconnect fixture starts visible to receiver.");
            before = tracked.Assignment.Epoch;
            audit = new MotionAudit("disconnect", tracked, clientId, hostId, before, Path.Combine(artifacts, "disconnect-snapshots.csv"), true);
            yield return Cast("client", AllureAction.Throw);
            Require(tracked.TargetState.AggroPlayerId == hostId && tracked.Assignment.SimulationOwnerPlayerId == clientId &&
                tracked.AllureHandoffPending, "Disconnect fixture did not retain old simulator.");
            yield return Command("client", "disconnect");
            yield return Wait(() => tracked.Assignment.SimulationOwnerPlayerId != clientId &&
                tracked.Assignment.Host != EnemySimulationHost.Frozen && Pose(tracked).Sequence > 0 &&
                Pose(tracked).AssignmentEpoch == tracked.Assignment.Epoch, "old simulator disconnect fallback", 5);
            audit.Sample(World);
            Require(audit.FirstNewSnapshotWasOutsideReceiver, "Fallback waited until the target entered receiver screen.");
            yield return new WaitForSecondsRealtime(.6f);
            audit.RequireContinuous(false);
            Evidence(audit.Summary()); audit = null;
            Mark("server-verified", string.Join(Environment.NewLine, evidence));
        }

        private IEnumerator PrepareEnemies(uint target, Vector2 position)
        {
            foreach (var enemy in enemies.Agents())
            {
                var result = World.RequestTargetChange(enemy.netId, target, EnemyTargetChangeReason.Forced);
                Require(result == EnemyTargetChangeResult.Accepted || result == EnemyTargetChangeResult.Unchanged, "Fixture target rejected: " + result);
            }
            yield return Wait(() => enemies.Agents().All(e => Settled(e, target)), "fixture initial simulator");
            int index = 0;
            foreach (var enemy in enemies.Agents().OrderBy(a => a.netId))
                Require(World.RepositionReferenceEnemy(enemy, position + Vector2.up * index++ * 2), "Fixture placement failed.");
            yield return Wait(() => enemies.Agents().All(e => Settled(e, target) && World.IsInPlayerView(target, e)), "fixture position acknowledged in source view");
        }

        private bool Settled(NetworkEnemySimulationAgent enemy, uint target) =>
            enemy.TargetState.AggroPlayerId == target && enemy.Assignment.SimulationOwnerPlayerId == target &&
            enemy.Assignment.Host == EnemySimulationHost.ClientPlayer && World.TryReadHandoff(enemy.netId, out var progress) &&
            !progress.AwaitingFirstSnapshot && progress.Completed > 0 && Pose(enemy).Sequence > 0 &&
            Pose(enemy).AssignmentEpoch == enemy.Assignment.Epoch;

        private EnemySimulationSnapshot Pose(NetworkEnemySimulationAgent enemy)
        { Require(World.Registry.TryGetLatestSnapshot(enemy.netId, out var pose), "Missing canonical enemy pose."); return pose; }

        private IEnumerator Move(string owner, Vector2 position) => Command(owner, "move", 0, position);
        private IEnumerator Cast(string owner, AllureAction action) => Command(owner, "cast", (int)action);
        private IEnumerator Command(string owner, string kind, int value = 0, Vector2 position = default)
        {
            int sequence = ++commandSequence;
            Mark("command-" + sequence, JsonUtility.ToJson(new OwnerCommand { role = owner, kind = kind, value = value, x = position.x, y = position.y }));
            yield return Wait(() => Has("done-" + sequence), "owner " + owner + " " + kind, 30);
        }

        private IEnumerator Select(PrototypeAbilityId ability)
        {
            Require(Abilities.RequestSelect(ability), "Owner selection rejected " + ability);
            while (Abilities.SelectedAbility != ability || Abilities.SelectionPending) yield return null;
        }

        private static IEnumerator Wait(Func<bool> predicate, string phase, float seconds = 30) => EnemyDefinitionRuntimeFixture.Wait(predicate, phase, seconds);
        private bool Has(string name) => File.Exists(Path.Combine(artifacts, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(artifacts, name));
        private void Mark(string name, string value = "ready")
        {
            string destination = Path.Combine(artifacts, name), temporary = destination + "." + role + ".pending";
            File.WriteAllText(temporary, value); File.Move(temporary, destination);
        }
        private void Evidence(string value) { evidence.Add(value); Debug.Log("[AllureProcess] " + value); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void ObserveLog(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure ??= message + "\n" + trace; }
        private IEnumerator Guard(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            while (stack.Count > 0 && !finished)
            {
                object current;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    current = stack.Peek().Current;
                    if (current is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception error) { failure = error.ToString(); Debug.LogException(error); Finish(false); yield break; }
                yield return current;
            }
        }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true; Application.logMessageReceived -= ObserveLog;
            enemies?.Dispose();
            string result = passed ? "PASS" : failure ?? "FAIL";
            if (!passed && !Has("failed-" + role)) Mark("failed-" + role, result);
            Mark("result-" + role, result);
            Debug.Log($"[AllureProcess] role={role} result={result}"); Application.Quit(passed ? 0 : 1);
        }

        [Serializable] private sealed class OwnerCommand { public string role, kind; public int value; public float x, y; }

        private sealed class MotionAudit
        {
            private readonly string label, path;
            private readonly NetworkEnemySimulationAgent enemy;
            private readonly uint initialOwner, receiver, initialEpoch;
            private readonly bool disconnect;
            private bool started;
            private EnemySimulationSnapshot previous;
            private int oldSamples, newSamples;
            private float distance, oldDistance, newDistance, maxStep;
            private uint firstNewSequence;
            private bool firstNewReceiverVisible;
            public bool FirstNewSnapshotWasOutsideReceiver => firstNewSequence > 0 && !firstNewReceiverVisible;
            public MotionAudit(string label, NetworkEnemySimulationAgent enemy, uint initialOwner, uint receiver, uint initialEpoch, string path, bool disconnect = false)
            {
                this.label = label; this.enemy = enemy; this.initialOwner = initialOwner; this.receiver = receiver;
                this.initialEpoch = initialEpoch; this.path = path; this.disconnect = disconnect;
                File.WriteAllText(path, "time,epoch,sequence,simulator,target,x,y,pending,receiverVisible\n");
            }
            public void Sample(NetworkEnemySimulationWorld world)
            {
                if (enemy == null || world == null || !world.Registry.TryGetLatestSnapshot(enemy.netId, out var pose) || pose.Sequence == 0) return;
                if (started && previous.AssignmentEpoch == pose.AssignmentEpoch && previous.Sequence == pose.Sequence) return;
                bool visible = world.IsInPlayerView(receiver, enemy);
                if (pose.AssignmentEpoch != initialEpoch)
                {
                    Require(disconnect || visible, label + ": simulator handed off before entering receiver's actual screen.");
                    if (firstNewSequence == 0)
                    {
                        firstNewSequence = pose.Sequence;
                        firstNewReceiverVisible = visible;
                    }
                    newSamples++;
                }
                else
                {
                    Require(enemy.Assignment.SimulationOwnerPlayerId == initialOwner, "Simulator identity changed without a new epoch.");
                    oldSamples++;
                }
                if (started)
                {
                    float step = Vector2.Distance(pose.Position, previous.Position);
                    double elapsed = Math.Max(.02, pose.SampleNetworkTime - previous.SampleNetworkTime);
                    float allowed = Mathf.Max(1.25f, (float)elapsed * 12 + .5f);
                    Require(step <= allowed, $"{label}: discontinuous movement {step:0.000} in {elapsed:0.000}s (allowed {allowed:0.000}).");
                    distance += step; maxStep = Mathf.Max(maxStep, step);
                    if (pose.AssignmentEpoch == initialEpoch && previous.AssignmentEpoch == initialEpoch) oldDistance += step;
                    else if (pose.AssignmentEpoch != initialEpoch && previous.AssignmentEpoch != initialEpoch) newDistance += step;
                }
                else started = true;
                previous = pose;
                File.AppendAllText(path, FormattableString.Invariant($"{pose.SampleNetworkTime:0.000},{pose.AssignmentEpoch},{pose.Sequence},{enemy.Assignment.SimulationOwnerPlayerId},{enemy.TargetState.AggroPlayerId},{pose.Position.x:0.000},{pose.Position.y:0.000},{enemy.AllureHandoffPending},{visible}\n"));
            }
            public void RequireContinuous(bool requireOldSamples = true)
            {
                Require(!requireOldSamples || oldSamples >= 3, label + ": too few old-simulator movement samples.");
                Require(!requireOldSamples || oldDistance > .3f, label + ": old simulator did not actually move the enemy.");
                Require(newSamples >= 2 && firstNewSequence > 0, label + ": no valid snapshots from the new simulator.");
                // A delayed target command can make the enemy turn around at takeover. Measure
                // movement produced by the new simulator; whole-path net displacement can cancel out.
                Require(newDistance > .3f, label + ": new simulator did not actually move the enemy.");
            }
            public string Summary() => $"{label}: enemy={enemy.netId}, oldSamples={oldSamples}, newSamples={newSamples}, firstNewSequence={firstNewSequence}, firstNewReceiverVisible={firstNewReceiverVisible}, oldSimulatorDistance={oldDistance:0.000}, newSimulatorDistance={newDistance:0.000}, pathDistance={distance:0.000}, maxStep={maxStep:0.000}, finalEpoch={previous.AssignmentEpoch}";
        }
    }
}
#endif
