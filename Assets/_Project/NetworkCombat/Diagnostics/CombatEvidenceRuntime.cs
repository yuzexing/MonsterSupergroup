using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Mirror;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    [Serializable] public sealed class ReplayCheckpoint
    {
        public int version = 1;
        public string domain, engine;
        public object state;
    }

    [Serializable] public sealed class ReplayCheckpointSet { public int version = 1; public ReplayCheckpoint[] engines; }

    [DefaultExecutionOrder(-32000)]
    public sealed class CombatEvidenceRuntime : MonoBehaviour, IDiagnosticSink
    {
        private sealed class Engine
        {
            public WeakReference target;
            public Func<object, object> capture;
            public string id, domain, context;
        }
        private sealed class ErrorCount { public string message, stack, type; public int count; public double first, last; }
        private readonly ConditionalWeakTable<object, Engine> engineLookup = new();
        private readonly List<Engine> engines = new();
        private readonly Dictionary<string, ErrorCount> errors = new();
        private readonly Dictionary<uint, string> generations = new();
        private object buildMetadata;
        private readonly Dictionary<uint, int> generationRoles = new();
        private readonly object errorGate = new();
        private CombatEvidenceStore store;
        private SteamDiagnosticTransport transport;
        private DiagnosticReplicator replicator;
        private long sequence, sinkTicks, sinkCalls;
        private int nextEngine, mainThread, fixedStep;
        private double nextSnapshot, nextCheckpoint, captureTicks;
        private string run = "boot", context = "boot/0";
        private uint round;
        private bool shuttingDown;
        private string capture;
        public static CombatEvidenceRuntime Instance { get; private set; }
        public CombatEvidenceStore Store => store;
        public string CaptureId => capture;
        public string RunId => run;
        public uint Round => round;
        public bool ReplicationEnabled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            bool enabledByDefault = false;
#if MONSTER_COMBAT_EVIDENCE
            enabledByDefault = true;
#endif
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--no-combat-evidence") >= 0 || (!enabledByDefault && Array.IndexOf(args, "--combat-evidence") < 0)) return;
            if (Instance != null) return;
            var root = new GameObject("Combat evidence"); DontDestroyOnLoad(root); root.AddComponent<CombatEvidenceRuntime>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; mainThread = Thread.CurrentThread.ManagedThreadId; capture = Guid.NewGuid().ToString("N");
            var args = Environment.GetCommandLineArgs();
            string path = args.FirstOrDefault(a => a.StartsWith("--combat-evidence-output=", StringComparison.Ordinal))?.Substring("--combat-evidence-output=".Length);
            store = new CombatEvidenceStore(path ?? Path.Combine(Application.persistentDataPath, "CombatDiagnostics"));
            ReplicationEnabled = Array.IndexOf(args, "--combat-evidence-local-only") < 0;
            if (ReplicationEnabled) { transport = new SteamDiagnosticTransport(); replicator = new DiagnosticReplicator(store, transport, capture); }
            CombatEvidence.Sink = this;
            Application.logMessageReceivedThreaded += CaptureException;
            buildMetadata = new {
                captureId = capture, buildGuid = Application.buildGUID, version = Application.version, unity = Application.unityVersion,
                protocol = SteamLobbyMetadata.ProtocolValue, development = Debug.isDebugBuild,
                mode = ReplicationEnabled ? "replicated" : "local", width = Screen.width, height = Screen.height,
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()], gpu = SystemInfo.graphicsDeviceName,
                cpu = SystemInfo.processorType, platform = Application.platform.ToString(),
                buildManifest = Resources.Load<TextAsset>("CombatEvidenceBuild")?.text };
            CombatEvidence.Event("Process", "process.start", "Started", null, input: buildMetadata);
            Debug.Log("[CombatEvidence] Automatic logs: " + store.Root);
        }
        private void Start()
        {
            if (FindFirstObjectByType<NetworkDiagnosticsObservation>() == null)
                gameObject.AddComponent<NetworkDiagnosticsObservation>();
        }
        private void FixedUpdate() => fixedStep++;
        private void Update()
        {
            RefreshContext();
            replicator?.Tick(Time.unscaledTimeAsDouble, run);
            if (Time.unscaledTimeAsDouble >= nextSnapshot)
            {
                CaptureObservation(); FlushExceptions(); nextSnapshot = Time.unscaledTimeAsDouble + 1;
            }
            if (Time.unscaledTimeAsDouble >= nextCheckpoint)
            {
                CaptureCheckpointSet();
                nextCheckpoint = Time.unscaledTimeAsDouble + 10;
            }
        }
        private void RefreshContext()
        {
            string current = NetworkServer.active ? (NetworkManager.singleton as BootGameplayNetworkManager)?.Session?.RunId
                : NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<NetworkRunParticipant>()?.RunId : null;
            // Disconnection must not discard the last run identity.
            if (!string.IsNullOrEmpty(current)) run = current;
            if (NetworkServer.active || NetworkClient.active) round = NetworkCombatWorld.CurrentRound;
            string next = run + "/" + round;
            if (next != context)
            {
                context = next; generations.Clear(); generationRoles.Clear(); nextCheckpoint = 0;
                TryWrite(new DiagnosticRecord { role = "Process", stage = "source.start", input = buildMetadata, critical = true, estimatedBytes = 1 << 20 });
            }
        }
        public bool TryWrite(DiagnosticRecord record)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return WriteCore(record); }
            finally { Interlocked.Add(ref sinkTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started); Interlocked.Increment(ref sinkCalls); }
        }
        private bool WriteCore(DiagnosticRecord record)
        {
            if (store == null || shuttingDown) return false;
            if (Thread.CurrentThread.ManagedThreadId == mainThread) RefreshContext();
            record.captureId = capture; record.runId = run; record.round = round;
            record.recordSequence = Interlocked.Increment(ref sequence).ToString();
            if (Thread.CurrentThread.ManagedThreadId == mainThread && record.target != 0)
            {
                generationRoles.TryGetValue(record.target, out int roles);
                int roleBit = record.role == "Server" ? 1 : 2;
                if (record.stage == "entity.spawn" && generations.Count < 65536)
                { if (roles == 0) generations[record.target] = record.recordSequence; generationRoles[record.target] = roles | roleBit; }
                if (record.stage == "entity.destroy" && generationRoles.ContainsKey(record.target)) generationRoles[record.target] = roles & ~roleBit;
                if (generations.TryGetValue(record.target, out var generation)) record.entityGeneration = capture + ":" + generation;
            }
            if (ulong.TryParse(record.eventId, out var eventId)) record.connectionEpoch = new CombatEventId(eventId).ConnectionEpoch;
            record.utc = DateTime.UtcNow.ToString("o");
            record.monotonicTime = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            if (Thread.CurrentThread.ManagedThreadId == mainThread)
            { record.networkTime = NetworkTime.time; record.frame = Time.frameCount; record.fixedStep = fixedStep; }
            record.estimatedBytes = (int)Math.Min(int.MaxValue, Math.Max(record.estimatedBytes, (long)RetainedBytes(record.input) + RetainedBytes(record.before) + RetainedBytes(record.after)));
            record.input = DiagnosticPayload.Freeze(record.input); record.before = DiagnosticPayload.Freeze(record.before); record.after = DiagnosticPayload.Freeze(record.after);
            return store.TryWrite(record);
        }
        public string RegisterEngine(object target, string domain, Func<object, object> snapshot)
        {
            RefreshContext();
            if (!engineLookup.TryGetValue(target, out var engine))
            {
                engine = new Engine { target = new WeakReference(target), capture = snapshot,
                    id = domain + "-" + (++nextEngine), domain = domain };
                engineLookup.Add(target, engine); engines.Add(engine);
            }
            if (engine.context != context) { engine.context = context; Checkpoint(engine); }
            return engine.id;
        }
        private void Checkpoint(Engine engine)
        {
            object target = engine.target.Target; if (target == null) return;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                object snapshot;
                using (CombatEvidence.Suppress()) snapshot = engine.capture(target);
                TryWrite(new DiagnosticRecord { role = engine.domain, stage = "replay.engine_checkpoint", engine = engine.id,
                    input = new ReplayCheckpoint { engine = engine.id, domain = engine.domain, state = snapshot },
                    critical = true, estimatedBytes = RetainedBytes(snapshot) });
            }
            catch (Exception error)
            {
                TryWrite(new DiagnosticRecord { role = engine.domain, engine = engine.id, stage = "evidence.gap",
                    reason = "CheckpointFailed", input = error.Message, critical = true });
            }
            captureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
        }
        private void CaptureCheckpointSet()
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var snapshots = new List<ReplayCheckpoint>();
                using (CombatEvidence.Suppress())
                    for (int i = engines.Count - 1; i >= 0; i--)
                    {
                        var engine = engines[i]; var target = engine.target.Target;
                        if (target == null) { engines.RemoveAt(i); continue; }
                        if (engine.context == context && !CombatEvidence.HasParent(target)) snapshots.Add(new ReplayCheckpoint {
                            engine = engine.id, domain = engine.domain, state = engine.capture(target) });
                    }
                if (snapshots.Count != 0) TryWrite(new DiagnosticRecord { role = "Process", stage = "replay.checkpoint", engine = "*",
                    input = new ReplayCheckpointSet { engines = snapshots.ToArray() }, critical = true });
            }
            catch (Exception error) { TryWrite(new DiagnosticRecord { role = "Process", stage = "evidence.gap", reason = "CheckpointSetFailed", input = error.Message, critical = true }); }
            captureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
        }
        public static int RetainedBytes(object payload)
        {
            long size = 2048;
            switch (payload)
            {
                case ReplayCheckpointSet set: foreach (var item in set.engines) size += RetainedBytes(item); break;
                case ReplayCheckpoint checkpoint: size += RetainedBytes(checkpoint.state); break;
                case object[] array: foreach (var item in array) size += RetainedBytes(item); break;
                case CombatSubmissionBatch b: size += 1024L * ((b.Results?.Length ?? 0) + (b.StatusMutations?.Length ?? 0) + (b.EnemyDeathReports?.Length ?? 0) + (b.PlayerHealthReports?.Length ?? 0)); break;
                case CanonicalWorldBatch b: size += 1024L * ((b.Entities?.Length ?? 0) + (b.Statuses?.Length ?? 0) + (b.ConfirmedKills?.Length ?? 0) + (b.EnemyHitPresentations?.Length ?? 0)); break;
                case EnemySimulationSnapshot s: size += 32L * (s.Runtime.PredictedKnockbacks?.Length ?? 0) + 64L * (s.Runtime.KnockbackSettings.CurveKeys?.Length ?? 0); break;
                case EnemySimulationSnapshotBatch b: if (b.Snapshots != null) foreach (var s in b.Snapshots) size += RetainedBytes(s); break;
                case EnemySimulationCheckpoint s: size += RetainedBytes(s.Movement); break;
                case EnemySimulationHandoff s: size += RetainedBytes(s.Checkpoint); break;
                case EnemyAttackPresentationEdge s: size += RetainedBytes(s.Checkpoint); break;
                case ReplayOutResult s: size += RetainedBytes(s.result) + RetainedBytes(s.outValues); break;
                case GatewayReplayOutput b: size += RetainedBytes(b.batch) + 256L * (b.receipts?.Length ?? 0); break;
                case GatewayReplayState s: size += 32L * (s.processed?.entries?.Length ?? 0) + RetainedBytes(s.ledger) + RetainedBytes(s.statuses) + 128L * (s.deaths?.Length ?? 0) + 512L * (s.admissions?.Length ?? 0); break;
                case LedgerReplayState s: size += 512L * ((s.entities?.Length ?? 0) + (s.sourceOwners?.Length ?? 0)); break;
                case StatusRegistryReplayState s: size += 1024L * ((s.instances?.Length ?? 0) + (s.removals?.Length ?? 0)); break;
                case ReplicaReplayState s: size += 1024L * ((s.entities?.Length ?? 0) + (s.statuses?.Length ?? 0) + (s.targets?.Length ?? 0) + (s.kills?.Length ?? 0)); foreach (var c in s.controllers) size += RetainedBytes(c.state); break;
                case AuthorityReplayState s: size += 16384L * (s.entries?.Length ?? 0); break;
                case StatusControllerReplayState s: size += 1024L * ((s.active?.Length ?? 0) + (s.completed?.Length ?? 0) + (s.removals?.Length ?? 0) + (s.removedApplications?.Length ?? 0)); break;
                case string text: size += text.Length * 2L; break;
                case Array array: size += array.Length * 1024L; break;
            }
            return (int)Math.Min(int.MaxValue, size);
        }
        private void CaptureObservation()
        {
            var actors = new List<object>();
            var world = NetworkCombatWorld.Instance;
            var collector = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<MirrorNetworkCombatBridge>()?.Collector : null;
            foreach (var identity in NetworkServer.active ? NetworkServer.spawned.Values : NetworkClient.spawned.Values)
            {
                if (identity == null || !identity.TryGetComponent<NetworkEnemySimulationAgent>(out var enemy)) continue;
                var controller = enemy.GetComponent<AstralShift.HellMaiden.AI.Enemy.EnemyController>();
                CanonicalEntityState? serverState = world != null && NetworkServer.active && world.Gateway.Ledger.TryGetState(enemy.netId, out var server) ? server : null;
                CanonicalEntityState? replicaState = world != null && world.Replica.TryGetEntity(enemy.netId, out var replica) ? replica : null;
                actors.Add(new { entity = enemy.netId, name = enemy.name, position = enemy.transform.position,
                    assignment = enemy.Assignment, canonicalAlive = enemy.IsCanonicalAlive, localAlive = enemy.IsLocallyAlive,
                    serverState, replicaState,
                    localHealth = controller != null ? controller.CurrentHealth : 0,
                    deathComplete = controller != null && controller.DeathPresentationComplete,
                    role = enemy.Authority.Role.ToString(), active = enemy.gameObject.activeInHierarchy,
                    renderers = controller?.enemyAnimator?.Renderers?.Where(r => r != null).Select(r => new {
                        name = r.name, r.enabled, r.forceRenderingOff, alpha = r.color.a }).ToArray() });
            }
            TryWrite(new DiagnosticRecord { role = NetworkServer.active ? "Host" : "Client", stage = "observation.snapshot", input = new {
                actors = actors.ToArray(), collector = collector?.CaptureDiagnosticState(), frameMs = Time.unscaledDeltaTime * 1000, pendingBytes = store.PendingBytes,
                replicatedBytes = replicator?.SentBytes ?? 0, replicationFailure = replicator?.LastFailure, rejectedReplicationPackets = replicator?.RejectedPackets ?? 0,
                replicationTransport = transport?.CaptureDiagnosticState(),
                sinkFailures = CombatEvidence.Failures, sinkCalls = Interlocked.Read(ref sinkCalls), sinkMs = Interlocked.Read(ref sinkTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency,
                replicationMainMs = replicator?.MainMilliseconds ?? 0, replicationWorkerMs = replicator?.WorkerMilliseconds ?? 0, dropped = store.Dropped, writerFailure = store.LastFailure, writerMs = store.WriteMilliseconds,
                checkpointMs = captureTicks * 1000d / System.Diagnostics.Stopwatch.Frequency }, estimatedBytes = 4096 + actors.Count * 2048 +
                    1024 * ((collector?.PendingResultCount ?? 0) + (collector?.PendingStatusMutationCount ?? 0) + (collector?.PendingPlayerHealthReportCount ?? 0) + (collector?.PendingEnemyDeathCount ?? 0)) });
        }
        private void CaptureException(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            if (message != null && message.Length > 8192) message = message.Substring(0, 8192);
            string key = type + ":" + message;
            lock (errorGate)
            {
                if (!errors.TryGetValue(key, out var item))
                {
                    if (errors.Count >= 128) key = "ExceptionOverflow";
                    if (!errors.TryGetValue(key, out item)) errors.Add(key, item = new ErrorCount { message = key,
                        stack = stack?.Substring(0, Math.Min(stack.Length, 16384)), type = type.ToString(), first = DateTime.UtcNow.Ticks });
                }
                item.count++; item.last = DateTime.UtcNow.Ticks;
            }
        }
        private void FlushExceptions()
        {
            ErrorCount[] snapshot;
            lock (errorGate) { snapshot = errors.Values.ToArray(); errors.Clear(); }
            foreach (var error in snapshot) TryWrite(new DiagnosticRecord { role = "Process", stage = "unity.exception", reason = error.type,
                input = error, critical = true, estimatedBytes = 64 * 1024 });
        }
        private void OnApplicationQuit() => Shutdown();
        private void OnDestroy() => Shutdown();
        private void Shutdown()
        {
            if (shuttingDown) return;
            FlushExceptions(); CombatEvidence.Event("Process", "process.stop", "Stopped", null);
            shuttingDown = true; Application.logMessageReceivedThreaded -= CaptureException;
            if (ReferenceEquals(CombatEvidence.Sink, this)) CombatEvidence.Sink = null;
            replicator?.Dispose(); transport?.Dispose();
            store?.Dispose(); store?.WaitForClose(2000); if (Instance == this) Instance = null;
        }
    }
}
