using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class EnemyHandoffProcessProbe : MonoBehaviour
    {
        private string role, directory, profile;
        private bool host, finished;
        private float deadline, duration;
        private BootGameplayNetworkManager manager;
        private GameObject normalPrefab, skeletonPrefab;
        private readonly Dictionary<uint, uint> loggedEpochs = new Dictionary<uint, uint>();
        private StreamWriter trace;
        private System.Threading.Mutex signalMutex;
        private double nextTrace;
        private int lastAttackTrigger;
        private bool ultimateTriggered;
        private bool simulationBlocked;
        private int positionToken;
        private string verifiedStage;
        private readonly HashSet<uint> configuredMelee = new HashSet<uint>();
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkEnemySimulationWorld World => NetworkEnemySimulationWorld.Instance;
        private NetworkCombatWorld Combat => NetworkCombatWorld.Instance;
        private uint Avatar(string name) => uint.Parse(Read("avatar-" + name));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string role = Argument("--enemy-handoff-role=");
            if (role == null) return;
            var probe = new GameObject("Enemy handoff process validation").AddComponent<EnemyHandoffProcessProbe>();
            probe.role = role; probe.host = role == "host";
            probe.directory = Argument("--enemy-handoff-artifacts=");
            probe.profile = Argument("--enemy-handoff-profile=") ?? "normal";
            probe.duration = float.Parse(Argument("--enemy-handoff-duration=") ?? "120", CultureInfo.InvariantCulture);
            DontDestroyOnLoad(probe.gameObject);
            probe.gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            signalMutex = new System.Threading.Mutex(false, "MonsterEnemyHandoff_" + Path.GetFileName(directory));
            deadline = Time.realtimeSinceStartup + duration * 2 + 240;
            trace = new StreamWriter(Path.Combine(directory, role + "-trace.csv"));
            trace.WriteLine("time,enemy,epoch,owner,target,role,action,requests,merged,completed,lastDuration,maxGap,correction,rejectedOwner,rejectedEpoch");
            SceneManager.sceneLoaded += PrepareScene;
            var routines = new Stack<IEnumerator>(); routines.Push(Run());
            while (routines.Count > 0)
            {
                object next;
                try
                {
                    var current = routines.Peek();
                    if (!current.MoveNext()) { routines.Pop(); continue; }
                    next = current.Current;
                    if (next is IEnumerator nested) { routines.Push(nested); continue; }
                }
                catch (Exception error) { Debug.LogException(error); Finish(false); yield break; }
                yield return next;
            }
            Finish(true);
        }

        private void Update()
        {
            if (finished) return;
            if (deadline > 0 && Time.realtimeSinceStartup > deadline) { Debug.LogError("Handoff probe timeout: " + role); Finish(false); return; }
            if (World == null || !NetworkClient.isConnected || NetworkTime.time < nextTrace) return;
            nextTrace = NetworkTime.time + .1;
            if (Seen("verify-peers"))
            {
                var lines = Read("verify-peers").Split('\n');
                if (lines[0] != verifiedStage)
                {
                    bool matches = true;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var fields = lines[i].Split(',');
                        uint id = uint.Parse(fields[0]), epoch = uint.Parse(fields[1]), target = uint.Parse(fields[2]);
                        if (!NetworkClient.spawned.TryGetValue(id, out var identity) || identity == null)
                        { matches = false; break; }
                        var agent = identity.GetComponent<NetworkEnemySimulationAgent>();
                        if (agent == null || agent.AppliedHandoffEpoch != epoch || agent.Assignment.AggroTargetPlayerId != target ||
                            agent.Assignment.SimulationOwnerPlayerId != target)
                        { matches = false; break; }
                    }
                    if (matches) { verifiedStage = lines[0]; Mark("verified-" + verifiedStage + "-" + role); }
                }
            }
            if (Owner != null)
            {
                bool block = Seen("block-" + role) && Read("block-" + role) == "1";
                if (simulationBlocked != block)
                {
                    simulationBlocked = block;
                    Owner.GetComponent<NetworkEnemySimulationEndpoint>().enabled = !block;
                    Mark("blocked-" + role, block ? "1" : "0");
                }
                if (Seen("reset-positions") && int.TryParse(Read("reset-positions"), out int reset) && reset != positionToken)
                { positionToken = reset; PlaceOwner(); Mark("positioned-" + role, reset.ToString()); }
            }
            foreach (var identity in NetworkClient.spawned.Values)
            {
                if (identity == null || !identity.TryGetComponent(out NetworkEnemySimulationAgent agent)) continue;
                var melee = identity.GetComponent<EnemyAttackMelee>();
                var activeAttack = identity.GetComponent<EnemyController>()?.attackScript;
                if (activeAttack != null && configuredMelee.Add(identity.netId))
                {
                    foreach (string field in new[] { "warningTime", "attackTime", "recoveryTime" })
                        typeof(EnemyAttack).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(activeAttack, 1.5f);
                }
                var replica = identity.GetComponent<NetworkEnemyMeleeReplica>();
                if (melee != null && melee.HasSimulationAttackInstance && replica != null && replica.HasReplicaAttackInstance)
                { Debug.LogError("Duplicate native/replica attack instance: " + identity.netId); Finish(false); return; }
                var assigned = agent.Assignment;
                if (assigned.Epoch == 0 || loggedEpochs.TryGetValue(identity.netId, out uint previous) && previous == assigned.Epoch) continue;
                loggedEpochs[identity.netId] = assigned.Epoch;
                World.TryReadHandoff(identity.netId, out var stats);
                trace.WriteLine(FormattableString.Invariant($"{NetworkTime.time:0.000},{identity.netId},{assigned.Epoch},{assigned.SimulationOwnerPlayerId},{assigned.AggroTargetPlayerId},{agent.Authority.Role},{agent.Handoff.Checkpoint.Movement.Runtime.Action.ActionId},{stats.Requests},{stats.Coalesced},{stats.Completed},{stats.LastDuration:0.000},{stats.MaximumSnapshotGap:0.000},{agent.LastHandoffCorrection:0.000},{stats.WrongOwner},{stats.WrongEpoch}"));
            }
            if (Seen("attack-command"))
            {
                var values = Read("attack-command").Split(',');
                if (values.Length == 3 && int.TryParse(values[0], out int token) && uint.TryParse(values[1], out uint id) &&
                    Owner != null && values[2] == Owner.netId.ToString() && !Seen("attack-started-" + token) &&
                    token != lastAttackTrigger && NetworkClient.spawned.TryGetValue(id, out var identity))
                {
                    var agent = identity.GetComponent<NetworkEnemySimulationAgent>();
                    var controller = identity.GetComponent<EnemyController>();
                    if (agent != null && controller != null && agent.Authority.RunsCombatDecisions &&
                        controller.StateMachine?.GetState() == controller.Moving)
                    {
                        lastAttackTrigger = token;
                        controller.Attack();
                        Mark("attack-started-" + token, controller.CaptureSimulationAction(NetworkTime.time).ActionId.ToString());
                    }
                }
            }
            if (role == "a" && !ultimateTriggered && Seen("use-ultimate-a") && Owner != null)
            {
                var ultimate = Owner.GetComponent<NetworkPlayerUltimate>();
                if (ultimate.TryReadDebugState(false, out var state) && state.State.HasCharge)
                {
                    ultimateTriggered = ultimate.RequestUse();
                    if (ultimateTriggered) Debug.Log("[EnemyHandoffProcess] Ultimate requested at " + Owner.transform.position);
                }
            }
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            // This fixture enters Gameplay directly, without the interactive preparation menu.
            manager.ConfigurePreparationFlow(false);
            var backend = manager.GetComponent<NetworkBackendBootstrap>();
            bool impaired = profile == "impaired";
            Require(backend.TryPrepareKcp("127.0.0.1", ushort.Parse(Argument("--enemy-handoff-port=") ?? "7993"), impaired, out var error), error);
            if (impaired)
            {
                var latency = (LatencySimulation)backend.ConfiguredLatencySimulation;
                latency.latency = 100; latency.jitter = .02f; latency.unreliableLoss = 10; latency.unreliableScramble = 10;
            }
            normalPrefab = manager.spawnPrefabs.First(p => p.name == "NetworkEnemyBase");
            string skeletonName = Argument("--enemy-handoff-prefab=") ?? "NetworkEnemySkeleton";
            skeletonPrefab = manager.spawnPrefabs.First(p => p.name == skeletonName);
            Debug.Log("[EnemyHandoffProcess] melee prefab=" + skeletonName + " assetId=" + skeletonPrefab.GetComponent<NetworkIdentity>().assetId);
            if (host) manager.StartHost(); else manager.StartClient();
            yield return Wait(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline, "owner baseline");
            yield return Wait(() => FindFirstObjectByType<NetworkEnemyDebugPanel>() is { isActiveAndEnabled: true }, "visible debug panel in validation build");
            PlaceOwner();
            Mark("avatar-" + role, Owner.netId.ToString());
            Mark("participant-" + role, Owner.GetComponent<NetworkRunParticipant>().ParticipantId.ToString());
            Mark("ready-" + role);
            if (!host) { yield return RunClient(); yield break; }
            yield return Wait(() => Seen("ready-a") && Seen("ready-b") && World.HasEligiblePlayer, "three peers");
            uint a = Avatar("a"), b = Avatar("b"), h = Owner.netId;

            var normal = Spawn(normalPrefab, a, new Vector2(4, 6));
            var elite = Spawn(normalPrefab, a, new Vector2(5, 6), EnemySimulationMode.EliteClient);
            var boss = Spawn(normalPrefab, a, new Vector2(6, 6), EnemySimulationMode.BossServer);
            foreach (uint target in new[] { b, h, a })
            {
                foreach (var enemy in new[] { normal, elite, boss }) World.RequestTargetChange(enemy.netId, target, EnemyTargetChangeReason.Forced);
                yield return Wait(() => Settled(normal, target) && Settled(elite, target) && boss.Assignment.AggroTargetPlayerId == target, "ownership ring");
                Require(boss.Assignment.Host == EnemySimulationHost.ServerAuthoritative && boss.Assignment.SimulationOwnerPlayerId == 0, "Boss left server simulation");
            }
            Require(World.Registry.TryGetLatestSnapshot(normal.netId, out var moved) && Vector2.Distance(moved.Position, normal.ServerSpawnPosition) > .02f, "No actual accepted motion in ring");
            Stage("ownership-ring");
            foreach (var enemy in new[] { normal, elite, boss }) NetworkServer.Destroy(enemy.gameObject);

            yield return ActionHandoffs(a, b, h);
            yield return KnockbackHandoff(a, b);
            yield return TimeoutAndPacketRaces(a, b);
            yield return AliveDisconnect(a, b, h);
            a = Avatar("a");

            yield return Stress(20, 20, a, b, h);
            yield return Stress(100, 5, a, b, h);

            Mark("reset-positions", "1");
            yield return Wait(() => new[] { "host", "a", "b" }.All(r => Seen("positioned-" + r) && Read("positioned-" + r) == "1"), "positions reset");
            yield return new WaitForSecondsRealtime(.5f);

            normal = Spawn(normalPrefab, a, new Vector2(9, 3));
            elite = Spawn(normalPrefab, a, new Vector2(-9, 3), EnemySimulationMode.EliteClient);
            var untouched = Spawn(normalPrefab, b, new Vector2(13, 4));
            yield return Wait(() => Settled(normal, a) && Settled(elite, a) && Settled(untouched, b), "before downed");
            uint untouchedEpoch = untouched.Assignment.Epoch;
            World.RequestTargetChange(normal.netId, b, EnemyTargetChangeReason.Forced);
            yield return Wait(() => Settled(normal, b), "before interrupted transfer");
            Mark("block-a", "1");
            yield return Wait(() => Seen("blocked-a") && Read("blocked-a") == "1", "block A before downed transfer");
            World.RequestTargetChange(normal.netId, a, EnemyTargetChangeReason.Forced);
            yield return Wait(() => normal.Assignment.AggroTargetPlayerId == a && World.TryReadHandoff(normal.netId, out var state) && state.AwaitingFirstSnapshot, "in-flight A before downed");
            Mark("down-a");
            yield return Wait(() => Combat.Gateway.Ledger.TryGetState(a, out var state) && !state.Alive, "server accepted downed");
            double downedAt = NetworkTime.time;
            yield return Wait(() => Settled(normal, b) && Settled(elite, h), "nearest surviving players");
            double elapsed = NetworkTime.time - downedAt;
            Require(impaired || elapsed <= .2, "Downed transfer first frame exceeded 0.2 seconds: " + elapsed);
            Require(untouched.Assignment.Epoch == untouchedEpoch, "Unrelated enemy was reassigned");
            Stage("downed-nearest", elapsed.ToString("0.000", CultureInfo.InvariantCulture));
            Mark("block-a", "0");
            Mark("down-seen");
            yield return Wait(() => Seen("a-offline"), "client disconnect");
            yield return Wait(() => manager.Session.Participants.Any(p => p.Id == ulong.Parse(Read("participant-a")) && p.ConnectionState == RunConnectionState.Disconnected), "server offline");
            Mark("offline-seen");
            yield return Wait(() => Seen("a-rejoined"), "downed reconnect");
            uint newA = Avatar("a");
            Require(newA != a && Combat.Gateway.Ledger.TryGetState(newA, out var rejoined) && !rejoined.Alive, "Rejoined life/avatar incorrect");
            Require(!World.TryGetEligiblePlayer(newA, out _), "Downed reconnect became eligible");
            Require(World.RequestTargetChange(normal.netId, newA, EnemyTargetChangeReason.Forced) == EnemyTargetChangeResult.InvalidTarget, "Downed target accepted");
            Stage("downed-reconnect");
            Mark("down-all");
            Owner.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
            yield return Wait(() => new[] { normal, elite, untouched }.All(e => e.Assignment.Host == EnemySimulationHost.Frozen), "all downed frozen");
            Stage("all-frozen"); Mark("complete");
            yield return Wait(() => Seen("done-a") && Seen("done-b"), "peer shutdown");
            manager.StopHost();
            yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "host unload");
        }

        private IEnumerator Stress(int count, int hz, uint a, uint b, uint h)
        {
            var enemies = new List<NetworkEnemySimulationAgent>();
            for (int i = 0; i < count; i++) enemies.Add(Spawn(normalPrefab, a, new Vector2(i % 10 - 5, 6 + i / 10 * .4f)));
            yield return Wait(() => enemies.All(e => Settled(e, a)), "stress ready");
            double started = NetworkTime.time, next = started;
            int index = 0;
            uint[] targets = { b, h, a };
            while (NetworkTime.time - started < duration)
            {
                if (NetworkTime.time >= next)
                {
                    next += 1d / hz;
                    foreach (var enemy in enemies) World.RequestTargetChange(enemy.netId, targets[index % 3], EnemyTargetChangeReason.Forced);
                    index++;
                }
                yield return null;
            }
            foreach (var enemy in enemies) World.RequestTargetChange(enemy.netId, b, EnemyTargetChangeReason.Forced);
            double stopped = NetworkTime.time;
            yield return Wait(() => enemies.All(e => Settled(e, b)), "stress final convergence", 2);
            double convergence = NetworkTime.time - stopped;
            foreach (var enemy in enemies)
            {
                Require(World.TryReadHandoff(enemy.netId, out var state) && state.Completed > 2, "No repeated first-frame evidence");
                Require(NetworkTime.time - state.LastSnapshotAt < 1, "Movement stream starved");
                Require(World.Registry.TryGetLatestSnapshot(enemy.netId, out var pose) && pose.AssignmentEpoch == enemy.Assignment.Epoch && pose.Sequence > 0, "No accepted final-owner movement");
            }
            var metrics = enemies.Select(e => { World.TryReadHandoff(e.netId, out var value); return value; }).ToArray();
            string stage = $"stress-{count}-{hz}";
            Mark("verify-peers", stage + "\n" + string.Join("\n", enemies.Select(e => $"{e.netId},{e.Assignment.Epoch},{e.Assignment.AggroTargetPlayerId}")));
            yield return Wait(() => new[] { "host", "a", "b" }.All(r => Seen("verified-" + stage + "-" + r)), "all peers applied final assignment", 2);
            Stage(stage, FormattableString.Invariant($"duration={duration} requested={index * count} completed={metrics.Sum(v => v.Completed)} merged={metrics.Sum(v => v.Coalesced)} maxGap={metrics.Max(v => v.MaximumSnapshotGap):0.000} convergence={convergence:0.000} rejectedOwner={metrics.Sum(v => v.WrongOwner)} rejectedEpoch={metrics.Sum(v => v.WrongEpoch)} peers=3"));
            foreach (var enemy in enemies) NetworkServer.Destroy(enemy.gameObject);
            yield return new WaitForSecondsRealtime(.5f);
        }

        private IEnumerator TimeoutAndPacketRaces(uint a, uint b)
        {
            var enemy = Spawn(normalPrefab, b, new Vector2(8, 6));
            yield return Wait(() => Settled(enemy, b), "timeout setup");
            Mark("block-a", "1");
            yield return Wait(() => Seen("blocked-a") && Read("blocked-a") == "1", "block simulator A");
            World.RequestTargetChange(enemy.netId, a, EnemyTargetChangeReason.Forced);
            yield return Wait(() => enemy.Assignment.Host == EnemySimulationHost.ServerFallback, "first-frame timeout fallback", 3);
            Require(enemy.Assignment.AggroTargetPlayerId == a, "Timeout lost valid target");
            uint fallbackEpoch = enemy.Assignment.Epoch;
            yield return Wait(() => World.Registry.TryGetLatestSnapshot(enemy.netId, out var pose) && pose.AssignmentEpoch == fallbackEpoch && pose.Sequence > 0,
                "accepted fallback movement");
            Mark("block-a", "0");
            yield return Wait(() => enemy.Assignment.Host == EnemySimulationHost.ClientPlayer && Settled(enemy, a), "ready simulator regains ownership", 3);
            Stage("timeout-recovery");

            World.Registry.TryGetLatestSnapshot(enemy.netId, out var old);
            int received = enemy.AcceptedRemoteSnapshotCount;
            var early = old;
            early.AssignmentEpoch = unchecked(enemy.Assignment.Epoch + 1u);
            if (early.AssignmentEpoch == 0) early.AssignmentEpoch = 1;
            early.Sequence = 2;
            early.SampleNetworkTime = NetworkTime.time;
            enemy.ReceiveRemoteSnapshot(early); // Unreliable motion arriving before reliable assignment.
            World.RequestTargetChange(enemy.netId, b, EnemyTargetChangeReason.Forced);
            yield return Wait(() => Settled(enemy, b), "replica to replica handoff");
            Require(enemy.Authority.Role == EnemySimulationRole.Replica && enemy.AcceptedRemoteSnapshotCount > received,
                "Observer failed to consume a buffered future-epoch snapshot");
            World.TryReadHandoff(enemy.netId, out var counters);
            World.Registry.TryGetLatestSnapshot(enemy.netId, out var current);
            var poison = current;
            poison.Position = new Vector2(999, 999); poison.Sequence += 100;
            var fromA = NetworkServer.spawned[a].GetComponent<NetworkEnemySimulationEndpoint>();
            var fromB = NetworkServer.spawned[b].GetComponent<NetworkEnemySimulationEndpoint>();
            World.SubmitClientSnapshots(fromA, new EnemySimulationSnapshotBatch { BatchSequence = 1, Snapshots = new[] { poison } });
            poison.AssignmentEpoch = old.AssignmentEpoch;
            World.SubmitClientSnapshots(fromB, new EnemySimulationSnapshotBatch { BatchSequence = 2, Snapshots = new[] { poison } });
            poison = current; poison.Position = new Vector2(999, 999);
            World.SubmitClientSnapshots(fromB, new EnemySimulationSnapshotBatch { BatchSequence = 3, Snapshots = new[] { poison, poison } });
            poison.Sequence += 100; poison.SampleNetworkTime -= 1;
            World.SubmitClientSnapshots(fromB, new EnemySimulationSnapshotBatch { BatchSequence = 4, Snapshots = new[] { poison } });
            World.Registry.TryGetLatestSnapshot(enemy.netId, out var after);
            World.TryReadHandoff(enemy.netId, out var rejected);
            Require(after.Position == current.Position && after.Sequence == current.Sequence &&
                rejected.WrongOwner > counters.WrongOwner && rejected.WrongEpoch > counters.WrongEpoch,
                "Obsolete, duplicate or out-of-order message overwrote accepted movement");
            Stage("packet-races", $"rejectedOwner={rejected.WrongOwner - counters.WrongOwner} rejectedEpoch={rejected.WrongEpoch - counters.WrongEpoch}");
            uint id = enemy.netId;
            NetworkServer.Destroy(enemy.gameObject);
            Require(!World.TryReadHandoff(id, out _), "Destroyed enemy retained an in-flight handoff");
        }

        private IEnumerator AliveDisconnect(uint a, uint b, uint h)
        {
            var enemy = Spawn(normalPrefab, a, new Vector2(8, 5));
            yield return Wait(() => Settled(enemy, a), "alive disconnect source");
            Mark("alive-disconnect-a");
            yield return Wait(() => Seen("alive-offline-a") && enemy.Assignment.AggroTargetPlayerId != a &&
                (Settled(enemy, b) || Settled(enemy, h)), "online survivor after disconnect");
            uint survivor = enemy.Assignment.AggroTargetPlayerId, epoch = enemy.Assignment.Epoch;
            Mark("alive-server-transfer");
            yield return Wait(() => Seen("alive-rejoined-a"), "alive avatar restored");
            uint restored = Avatar("a");
            yield return Wait(() => World.TryGetEligiblePlayer(restored, out _), "restored candidate ready");
            Require(restored != a && enemy.Assignment.AggroTargetPlayerId == survivor && enemy.Assignment.Epoch == epoch,
                "Reconnect reclaimed a healthy monster or reused the old avatar");
            Stage("alive-disconnect-reconnect");
            NetworkServer.Destroy(enemy.gameObject);
        }

        private IEnumerator ActionHandoffs(uint a, uint b, uint h)
        {
            var enemy = Spawn(skeletonPrefab, a, new Vector2(0, 5));
            yield return Wait(() => Settled(enemy, a), "melee initial owner");
            yield return new WaitForSecondsRealtime(.4f);
            int token = 0;
            uint[] targets = { b, h, a };
            foreach (var phase in new[] { EnemyAttackPresentationPhase.Warning, EnemyAttackPresentationPhase.Active, EnemyAttackPresentationPhase.Recovery })
            {
                token++;
                Mark("attack-command", token + "," + enemy.netId + "," + enemy.Assignment.SimulationOwnerPlayerId);
                yield return Wait(() => Seen("attack-started-" + token), "attack initiated");
                ulong actionId = ulong.Parse(Read("attack-started-" + token));
                yield return Wait(() => World.Registry.TryGetLatestSnapshot(enemy.netId, out var snapshot) &&
                    snapshot.Runtime.Action.ActionId == actionId && snapshot.Runtime.Action.Phase == phase, "source phase " + phase);
                World.Registry.TryGetLatestSnapshot(enemy.netId, out var before);
                uint target = targets[token - 1];
                Require(World.RequestTargetChange(enemy.netId, target, EnemyTargetChangeReason.Forced) == EnemyTargetChangeResult.Accepted, "phase transfer request");
                yield return Wait(() => Settled(enemy, target), "phase transfer first snapshot");
                World.Registry.TryGetLatestSnapshot(enemy.netId, out var after);
                Require(after.Runtime.Action.ActionId == actionId, "Action identity restarted on transfer");
                Require(Math.Abs(after.Runtime.Action.WarningUntil - before.Runtime.Action.WarningUntil) < .0001 &&
                    Math.Abs(after.Runtime.Action.ActiveUntil - before.Runtime.Action.ActiveUntil) < .0001 &&
                    Math.Abs(after.Runtime.Action.RecoveryUntil - before.Runtime.Action.RecoveryUntil) < .0001 &&
                    after.Runtime.Action.Facing == before.Runtime.Action.Facing && after.Runtime.Action.TargetPosition == before.Runtime.Action.TargetPosition,
                    "Action timing or locked aim changed on transfer");
                Stage("action-" + phase, "action=" + actionId);
                yield return Wait(() => NetworkTime.time > after.Runtime.Action.RecoveryUntil + .1, "action completion");
            }
            NetworkServer.Destroy(enemy.gameObject);
            Mark("attack-command", "0,0");

            var boss = Spawn(skeletonPrefab, a, new Vector2(-10, 5), EnemySimulationMode.BossServer);
            yield return Wait(() => boss.ProductEnemyInitialized && boss.Authority.Role == EnemySimulationRole.ServerAuthoritative, "Boss combat ready");
            yield return new WaitForSecondsRealtime(.3f);
            var bossController = boss.GetComponent<EnemyController>();
            bossController.Attack();
            var bossAction = bossController.CaptureSimulationAction(NetworkTime.time);
            foreach (uint target in new[] { b, h, a })
            {
                World.RequestTargetChange(boss.netId, target, EnemyTargetChangeReason.Forced);
                yield return Wait(() => boss.Assignment.AggroTargetPlayerId == target, "Boss target changes");
                var continued = bossController.CaptureSimulationAction(NetworkTime.time);
                Require(boss.Authority.Role == EnemySimulationRole.ServerAuthoritative && continued.ActionId == bossAction.ActionId &&
                    continued.WarningUntil == bossAction.WarningUntil && continued.RecoveryUntil == bossAction.RecoveryUntil,
                    "Boss action restarted while changing target");
            }
            Stage("boss-action-continuity");
            NetworkServer.Destroy(boss.gameObject);
        }

        private IEnumerator KnockbackHandoff(uint a, uint b)
        {
            var origin = (Vector2)NetworkServer.spawned[a].transform.position;
            var enemy = Spawn(normalPrefab, a, origin + Vector2.right);
            yield return Wait(() => Settled(enemy, a), "knockback owner");
            Require(NetworkServer.spawned[a].GetComponent<NetworkPlayerUltimate>().ServerGrantCharge(), "grant real Ultimate charge");
            Mark("use-ultimate-a");
            double nextDiagnostic = 0;
            yield return Wait(() =>
            {
                bool found = World.Registry.TryGetLatestSnapshot(enemy.netId, out var snapshot);
                if (NetworkTime.time >= nextDiagnostic)
                {
                    nextDiagnostic = NetworkTime.time + 1;
                    var ultimate = NetworkServer.spawned[a].GetComponent<NetworkPlayerUltimate>();
                    Debug.Log($"[EnemyHandoffProcess] impulse accepted={ultimate.AcceptedUseCount} rejected={ultimate.RejectedUseCount} routed={World.RoutedUltimateKnockbackCount} enemy={enemy.netId} alive={enemy.IsCanonicalAlive} pos={snapshot.Position} ownerPos={NetworkServer.spawned[a].transform.position} sequence={snapshot.Sequence} epoch={snapshot.AssignmentEpoch}");
                }
                return found && snapshot.Runtime.Knockback.Active;
            }, "real Ultimate impulse");
            World.Registry.TryGetLatestSnapshot(enemy.netId, out var before);
            World.RequestTargetChange(enemy.netId, b, EnemyTargetChangeReason.Forced);
            yield return Wait(() => Settled(enemy, b), "knockback handoff");
            var seed = enemy.Handoff.Checkpoint.Movement.Runtime;
            Require(seed.Knockback.Start == before.Runtime.Knockback.Start && seed.Knockback.End == before.Runtime.Knockback.End,
                "Knockback trajectory was regenerated");
            World.Registry.TryGetLatestSnapshot(enemy.netId, out var after);
            if (after.Runtime.Knockback.Active)
                Require(after.Runtime.Knockback.Elapsed >= before.Runtime.Knockback.Elapsed && after.Runtime.Knockback.End == seed.Knockback.End,
                    "Knockback elapsed time restarted");
            Stage("ultimate-knockback");
            NetworkServer.Destroy(enemy.gameObject);
        }

        private IEnumerator RunClient()
        {
            if (role == "a")
            {
                yield return Wait(() => Seen("alive-disconnect-a"), "alive disconnect command", 100);
                ulong originalParticipant = Owner.GetComponent<NetworkRunParticipant>().ParticipantId;
                manager.StopClient();
                yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning && !NetworkClient.active, "alive disconnect cleanup");
                Mark("alive-offline-a");
                yield return Wait(() => Seen("alive-server-transfer"), "survivor transfer");
                manager.StartClient();
                yield return Wait(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline, "alive restored baseline");
                Require(Owner.GetComponent<NetworkRunParticipant>().ParticipantId == originalParticipant, "Alive reconnect changed participant");
                PlaceOwner();
                Mark("avatar-a", Owner.netId.ToString()); Mark("alive-rejoined-a");
                yield return Wait(() => Seen("down-a"), "death command", duration * 2 + 150);
                ulong participant = Owner.GetComponent<NetworkRunParticipant>().ParticipantId;
                Owner.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
                yield return Wait(() => Seen("down-seen"), "death observed");
                manager.StopClient();
                yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning && !NetworkClient.active, "disconnect cleanup");
                Mark("a-offline");
                yield return Wait(() => Seen("offline-seen"), "server checkpoint");
                manager.StartClient();
                yield return Wait(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline, "rejoined baseline");
                Require(Owner.GetComponent<NetworkRunParticipant>().ParticipantId == participant, "Participant changed on reconnect");
                yield return Wait(() => !Owner.GetComponent<CombatantBehaviour>().IsAlive, "restored owner Downed health");
                var movement = Owner.GetComponent<AstralShift.HellMaiden.Player.PlayerMovement>();
                var body = Owner.GetComponent<Rigidbody2D>();
                Vector2 downedPosition = body.position;
                for (int i = 0; i < 10; i++)
                {
                    movement.SetDirection(Vector2.right);
                    movement.SetDirectionImmediate(Vector2.up);
                    movement.Dash();
                    yield return new WaitForFixedUpdate();
                }
                Require(body.linearVelocity.sqrMagnitude < .0001f && Vector2.Distance(body.position, downedPosition) < .01f,
                    "Downed reconnect accepted movement/dash input");
                var panel = FindFirstObjectByType<NetworkPlayerDebugPanel>();
                yield return Wait(() => panel != null && panel.Rows.Any(row => row.ParticipantId == participant &&
                    row.Summary.Contains("ParticipantId: " + participant) && row.Summary.Contains("Avatar netId: " + Owner.netId)),
                    "visible stable participant and new avatar labels");
                Stage("downed-owner-input-blocked", "participant=" + participant + " avatar=" + Owner.netId);
                Mark("avatar-a", Owner.netId.ToString()); Mark("a-rejoined");
            }
            else
            {
                yield return Wait(() => Seen("down-all"), "all downed command", duration * 2 + 180);
                Owner.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
            }
            yield return Wait(() => Seen("complete"), "complete");
            manager.StopClient();
            yield return Wait(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "client unload");
            Mark("done-" + role);
        }

        private void PlaceOwner()
        {
            Vector3 position = new Vector3(role == "host" ? -12 : role == "a" ? 0 : 12, 0, 0);
            Owner.transform.position = position;
            if (Owner.TryGetComponent(out Rigidbody2D body)) body.position = position;
            var combatant = Owner.GetComponent<CombatantBehaviour>();
            combatant.SetMaximumHealthPreservingMissingHealth(1000000); combatant.RestoreHealth(1000000);
        }
        private NetworkEnemySimulationAgent Spawn(GameObject prefab, uint target, Vector2 position, EnemySimulationMode mode = EnemySimulationMode.NormalClient)
        {
            var instance = Instantiate(prefab, position, Quaternion.identity);
            var agent = instance.GetComponent<NetworkEnemySimulationAgent>();
            agent.ConfigureInitialServerTarget(target);
            // Keep real Ultimate damage from destroying the motion fixture mid-handoff.
            agent.ConfigureRuntimeMinimumHealthOverride(1000000);
            agent.Authority.ConfigureNetworkManaged(mode, !agent.ProductMovementOnly);
            NetworkServer.Spawn(instance);
            return agent;
        }
        private bool Settled(NetworkEnemySimulationAgent enemy, uint target) => enemy != null && enemy.Assignment.AggroTargetPlayerId == target &&
            World.TryReadHandoff(enemy.netId, out var state) && !state.AwaitingFirstSnapshot && state.LastSnapshotAt > 0;
        private IEnumerator Wait(Func<bool> condition, string stage, float timeout = 25)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Require(condition(), role + " timed out: " + stage);
        }
        private void Stage(string stage, string extra = "")
        {
            string line = $"[EnemyHandoffProcess] stage={stage} profile={profile} role={role} {extra}";
            Debug.Log(line); File.AppendAllText(Path.Combine(directory, "results.txt"), line + Environment.NewLine); trace.Flush();
        }
        private void PrepareScene(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true)) spawner.enabled = false;
        }
        private string Read(string name)
        {
            if (!signalMutex.WaitOne(1000)) throw new IOException("Timed out reading validation signal " + name);
            try { return File.ReadAllText(Path.Combine(directory, name)); }
            finally { signalMutex.ReleaseMutex(); }
        }
        private void Mark(string name, string value = "ready")
        {
            if (!signalMutex.WaitOne(1000)) throw new IOException("Timed out writing validation signal " + name);
            try { File.WriteAllText(Path.Combine(directory, name), value); }
            finally { signalMutex.ReleaseMutex(); }
        }
        private bool Seen(string name) => File.Exists(Path.Combine(directory, name));
        private static string Argument(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix))?.Substring(prefix.Length);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true; SceneManager.sceneLoaded -= PrepareScene; trace?.Dispose(); signalMutex?.Dispose();
            Debug.Log($"[EnemyHandoffProcess] result={(passed ? "PASS" : "FAIL")} role={role}");
            Application.Quit(passed ? 0 : 1);
        }
    }
}
