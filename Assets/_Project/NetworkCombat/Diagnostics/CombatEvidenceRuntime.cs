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
    public sealed partial class CombatEvidenceRuntime : MonoBehaviour, IDiagnosticSink, IDiagnosticAdvanceSink, IDiagnosticIntegritySink, IDiagnosticSharedPayloadSink
    {
        private sealed class Engine
        {
            public WeakReference target;
            public Func<object, object> capture;
            public string id, domain, context;
            public long bindingRevision;
        }
        private sealed class ErrorCount { public string message, stack, type; public long count, reported, first, last; }
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
        private double nextSnapshot, nextCheckpoint, nextRecoveryAttempt, captureTicks;
        private string run = "boot", context = "boot/0";
        private string contextRun = "boot";
        private uint contextRound;
        private uint round;
        private bool shuttingDown, engineRegistryIncomplete;
        private string capture;
        private const int RuntimeCacheBytes = 16 << 20;
        private bool runtimeBudgetHeld;
        public static CombatEvidenceRuntime Instance { get; private set; }
        public CombatEvidenceStore Store => store;
        public DiagnosticMemoryBudget Memory => store?.Memory;
        public string CaptureId => capture;
        public string RunId => run;
        public uint Round => round;
        public bool ReplicationEnabled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (!MonsterSupergroup.Builds.BuildFeatures.EvidenceAllowed) return;
            bool enabledByDefault = false;
#if MONSTER_BUILD_EVIDENCE && !UNITY_EDITOR
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
            store = new CombatEvidenceStore(path ?? Path.Combine(Application.persistentDataPath, "CombatDiagnostics"), CreateStoreOptions(args));
            runtimeBudgetHeld = store.Memory.TryReserve(RuntimeCacheBytes);
            if (!runtimeBudgetHeld) { store.Dispose(); enabled = false; return; }
            ReplicationEnabled = Array.IndexOf(args, "--combat-evidence-local-only") < 0;
            if (ReplicationEnabled) { transport = new SteamDiagnosticTransport(); replicator = new DiagnosticReplicator(store, transport, capture); }
            CombatEvidence.Sink = this;
            CombatInvestigationEvidence.Configure(store.Profile, args);
            Application.logMessageReceivedThreaded += CaptureException;
            Application.wantsToQuit += WantsToQuit;
            string executablePath = null; int processId = 0;
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                processId = process.Id; executablePath = process.MainModule?.FileName;
            }
            catch { /* Missing process identity is explicit in metadata and cannot verify a delivered package. */ }
            string investigationCatalog = CombatInvestigationEvidence.Enabled ? Resources.Load<TextAsset>("CombatInvestigationCatalog")?.text : null;
            buildMetadata = new {
                captureId = capture, buildGuid = Application.buildGUID, version = Application.version, buildInfo = MonsterSupergroup.Builds.RuntimeBuildInfo.Current?.ToJson(), unity = Application.unityVersion,
                executablePath, processId, executablePathVerified = !string.IsNullOrEmpty(executablePath),
                protocol = SteamLobbyMetadata.ProtocolValue, development = Debug.isDebugBuild,
                mode = ReplicationEnabled ? "replicated" : "local", width = Screen.width, height = Screen.height,
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()], gpu = SystemInfo.graphicsDeviceName,
                cpu = SystemInfo.processorType, platform = Application.platform.ToString(),
                evidenceConfiguration = store.Configuration,
                investigation = CombatInvestigationEvidence.Configuration,
                investigationCatalog, investigationCatalogAvailable = investigationCatalog != null,
                buildManifest = Resources.Load<TextAsset>("CombatEvidenceBuild")?.text };
            CombatEvidence.Event("Process", "process.start", "Started", null, input: buildMetadata);
            Debug.Log("[CombatEvidence] Automatic logs: " + store.Root);
        }
        private static EvidenceStoreOptions CreateStoreOptions(string[] args)
        {
            bool observe = args != null && Array.IndexOf(args, "--combat-evidence-observe-queue") >= 0;
            const string prefix = "--combat-evidence-profile=";
            var profiles = args?.Where(a => a.StartsWith(prefix, StringComparison.Ordinal)).ToArray() ?? Array.Empty<string>();
            if (profiles.Length > 1) throw new ArgumentException("Specify combat evidence profile only once.");
            string name = profiles.Length == 0 ? "standard" : profiles[0].Substring(prefix.Length);
            EvidenceProfile profile = name == "standard" ? EvidenceProfile.Standard : name == "diagnostic"
                ? EvidenceProfile.Diagnostic : throw new ArgumentException("Unknown combat evidence profile: " + name);
            // Player windows begin with the first run, so menu waiting does not consume them.
            // This opt-in does not install main-thread timing or run a CPU calibration probe.
            return EvidenceStoreOptions.ForProfile(profile, observe);
        }
        private void Start()
        {
            if (shuttingDown) return;
            if (FindFirstObjectByType<NetworkDiagnosticsObservation>() == null)
                gameObject.AddComponent<NetworkDiagnosticsObservation>();
        }
        private void FixedUpdate() => fixedStep++;
        private void Update()
        {
            if (shuttingDown) { UpdateShutdown(); return; }
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
            else if (Time.unscaledTimeAsDouble >= nextRecoveryAttempt && store.RecoveryCheckpointRequested(capture, run, round))
            {
                nextRecoveryAttempt = Time.unscaledTimeAsDouble + 1;
                CaptureCheckpointSet();
            }
        }
        private void RefreshContext()
        {
            string current = NetworkServer.active ? (NetworkManager.singleton as BootGameplayNetworkManager)?.Session?.RunId
                : NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<NetworkRunParticipant>()?.RunId : null;
            // Disconnection must not discard the last run identity.
            if (!string.IsNullOrEmpty(current)) run = current;
            if (NetworkServer.active || NetworkClient.active) round = NetworkCombatWorld.CurrentRound;
            if (run != contextRun || round != contextRound)
            {
                contextRun = run; contextRound = round; context = run + "/" + round; generations.Clear(); generationRoles.Clear(); nextCheckpoint = 0;
                if (run != "boot") store?.QueueObservation?.MarkPhase(EvidenceQueuePhase.Load, EvidenceQueueObservation.Now);
                TryWrite(new DiagnosticRecord { role = "Process", stage = "source.start", input = buildMetadata, critical = true, estimatedBytes = 1 << 20 });
            }
        }
        public bool TryWrite(DiagnosticRecord record)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return WriteCore(record); }
            finally { Interlocked.Add(ref sinkTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started); Interlocked.Increment(ref sinkCalls); }
        }
        private bool WriteCore(DiagnosticRecord record, Func<object> captureInput = null)
        {
            if (store == null || shuttingDown) return false;
            Stamp(record);
            record.estimatedBytes = (int)Math.Min(int.MaxValue, Math.Max(record.estimatedBytes, 512L + RetainedBytes(record.input) + RetainedBytes(record.before) + RetainedBytes(record.after)));
            return store.TryWrite(record, captureInput == null, captureInput);
        }
        public void ReportCaptureFailure(DiagnosticRecord record)
        {
            if (store == null || shuttingDown) return;
            if (record.captureId != capture || string.IsNullOrEmpty(record.recordSequence)) Stamp(record);
            store.ReportCaptureFailure(record);
        }
        private void Stamp(DiagnosticRecord record)
        {
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
        }
        public string RegisterEngine(object target, string domain, Func<object, object> snapshot)
        {
            RefreshContext();
            if (!engineLookup.TryGetValue(target, out var engine))
            {
                if (engines.Count >= 16384)
                {
                    engineRegistryIncomplete = true;
                    ReportCaptureFailure(new DiagnosticRecord { role = "Process", stage = "replay.engine_checkpoint",
                        reason = "EngineRegistryLimit", critical = true }); return null;
                }
                engine = new Engine { target = new WeakReference(target), capture = snapshot,
                    id = domain + "-" + (++nextEngine), domain = domain };
                engineLookup.Add(target, engine); engines.Add(engine);
            }
            long bindingRevision = CombatEvidence.BindingRevision(target);
            if (engine.context != context || engine.bindingRevision != bindingRevision)
            {
                engine.context = context; engine.bindingRevision = bindingRevision;
                Checkpoint(engine);
            }
            return engine.id;
        }
        private void Checkpoint(Engine engine)
        {
            object target = engine.target.Target; if (target == null) return;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                WriteCore(new DiagnosticRecord { role = engine.domain, stage = "replay.engine_checkpoint", engine = engine.id,
                    critical = true, estimatedBytes = 8 << 20 }, () => {
                        using (CombatEvidence.Suppress()) return new ReplayCheckpoint { engine = engine.id, domain = engine.domain, state = engine.capture(target) };
                    });
            }
            catch (Exception error)
            {
                ReportCaptureFailure(new DiagnosticRecord { role = engine.domain, engine = engine.id, stage = "evidence.gap",
                    reason = "CheckpointFailed:" + error.GetType().Name, critical = true });
            }
            captureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
        }
        private void CaptureCheckpointSet()
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                WriteCore(new DiagnosticRecord { role = "Process", stage = "replay.checkpoint", engine = "*", critical = true, estimatedBytes = 8 << 20 }, () => {
                if (engineRegistryIncomplete) throw new InvalidOperationException("EngineRegistryIncomplete");
                var snapshots = new List<ReplayCheckpoint>();
                using (CombatEvidence.Suppress())
                    for (int i = engines.Count - 1; i >= 0; i--)
                    {
                        var engine = engines[i]; var target = engine.target.Target;
                        if (target == null) { engines.RemoveAt(i); continue; }
                        if (engine.context == context && !CombatEvidence.HasParent(target)) snapshots.Add(new ReplayCheckpoint {
                            engine = engine.id, domain = engine.domain, state = engine.capture(target) });
                    }
                return new ReplayCheckpointSet { engines = snapshots.ToArray() };
                });
            }
            catch (Exception error) { ReportCaptureFailure(new DiagnosticRecord { role = "Process", stage = "evidence.gap", reason = "CheckpointSetFailed:" + error.GetType().Name, critical = true }); }
            captureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
        }
        public static int RetainedBytes(object payload)
        {
            if (payload == null) return 0;
            if (payload is bool || payload is int || payload is uint || payload is float || payload is double || payload is long || payload is ulong || payload is Enum) return 32;
            long size = 128;
            switch (payload)
            {
                case SharedEvidencePayload _: size = 128; break; // The detached value has its own reservation and lease lifetime.
                case CanonicalReceiveEvidence received: size += RetainedBytes(received.batch); break;
                case ReplayCheckpointSet set: foreach (var item in set.engines) size += RetainedBytes(item); break;
                case ReplayCheckpoint checkpoint: size += RetainedBytes(checkpoint.state); break;
                case object[] array: size = 32 + array.Length * 8L; foreach (var item in array) size += RetainedBytes(item); break;
                case StatusReplayBoundary _: size = 192; break;
                case DamageCalculationInput d: size = 256L + RetainedBytes(d.targetMultipliers) + RetainedBytes(d.modifiers); break;
                case AttackStatsEvidenceInput s: size = 256L + RetainedBytes(s.globalMultipliers) + RetainedBytes(s.staticModifiers) + RetainedBytes(s.dynamicModifiers) + 32L + 8L * (s.remaps?.Length ?? 0); break;
                case ModifierEvidence m: size = 128L + 2L * (m.type?.Length ?? 0) + 4L * (m.parameters?.Length ?? 0); break;
                case OutputStatisticInput i: size = 96L + 2L * (i.metric?.Length ?? 0); break;
                case OutputStatisticsState _: case DamageCalculationResult _: case CalculationReplayState _: size = 64; break;
                case AttackStatsSnapshot _: case AttackStatsMultipliers _: size = 128; break;
                case float[] a: size = 32L + 4L * a.Length; break;
                case CombatSubmissionBatch b: size += 1024L * ((b.Results?.Length ?? 0) + (b.StatusMutations?.Length ?? 0) + (b.EnemyDeathReports?.Length ?? 0) + (b.PlayerHealthReports?.Length ?? 0)); break;
                case CanonicalWorldBatch b: size += 1024L * ((b.Entities?.Length ?? 0) + (b.Statuses?.Length ?? 0) + (b.ConfirmedKills?.Length ?? 0) + (b.EnemyHitPresentations?.Length ?? 0)); break;
                case EnemySimulationSnapshot s: size = 512L + 32L * (s.Runtime.PredictedKnockbacks?.Length ?? 0) + 64L * (s.Runtime.KnockbackSettings.CurveKeys?.Length ?? 0); break;
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
                case AuthorityReplayState s:
                    size += 128L * (s.entries?.Length ?? 0);
                    if (s.entries != null) foreach (var entry in s.entries) size += RetainedBytes(entry.snapshot) + RetainedBytes(entry.attack);
                    break;
                case StatusControllerReplayState s: size += 1024L * ((s.active?.Length ?? 0) + (s.completed?.Length ?? 0) + (s.removals?.Length ?? 0) + (s.removedApplications?.Length ?? 0)); break;
                case string text: size += text.Length * 2L; break;
                case Array array: size += array.Length * 1024L; break;
            }
            return (int)Math.Min(int.MaxValue, size);
        }
        private void CaptureObservation()
        {
            int actors = NetworkServer.active ? NetworkServer.spawned.Count : NetworkClient.spawned.Count;
            WriteCore(new DiagnosticRecord { role = NetworkServer.active ? "Host" : "Client", stage = "observation.snapshot",
                estimatedBytes = (int)Math.Min(8L << 20, 16384L + actors * 4096L) }, CaptureObservationPayload);
        }
        private object CaptureObservationPayload()
        {
            var actors = new List<object>();
            var world = NetworkCombatWorld.Instance;
            var collector = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<MirrorNetworkCombatBridge>()?.Collector : null;
            foreach (var identity in NetworkServer.active ? NetworkServer.spawned.Values : NetworkClient.spawned.Values)
            {
                if (identity == null) continue;
                if (identity.TryGetComponent<MirrorNetworkCombatBridge>(out var player))
                {
                    var health = identity.GetComponent<MonsterSupergroup.Gameplay.Combat.CombatantBehaviour>();
                    var body = identity.GetComponent<Rigidbody2D>();
                    CanonicalEntityState? playerServer = world != null && NetworkServer.active && world.Gateway.Ledger.TryGetState(identity.netId, out var ps) ? ps : null;
                    CanonicalEntityState? playerReplica = world != null && world.Replica.TryGetEntity(identity.netId, out var pr) ? pr : null;
                    actors.Add(new { kind = "player", entity = identity.netId, name = identity.name, identity.isOwned,
                        position = identity.transform.position, bodyPosition = body != null ? (Vector2?)body.position : null,
                        velocity = body != null ? (Vector2?)body.linearVelocity : null, connectionEpoch = player.ConnectionEpoch,
                        health = health != null ? (int?)health.CurrentHealth : null, stateVersion = health?.StateVersion,
                        invulnerable = health?.IsInvulnerable, serverState = playerServer, replicaState = playerReplica });
                }
                if (!identity.TryGetComponent<NetworkEnemySimulationAgent>(out var enemy)) continue;
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
            return new {
                actors = actors.ToArray(), collector = collector == null ? null : new { collector.PendingResultCount, collector.PendingStatusMutationCount,
                    collector.PendingPlayerHealthReportCount, collector.PendingEnemyDeathCount, collector.DeathReceiptsReceived,
                    collector.LastDeathConfirmationSeconds, collector.MaximumDeathConfirmationSeconds,
                    oldestPendingDeathSeconds = collector.OldestPendingDeathAge(Time.unscaledTimeAsDouble) }, frameMs = Time.unscaledDeltaTime * 1000, pendingBytes = store.PendingBytes,
                replicatedBytes = replicator?.SentBytes ?? 0, replicationFailure = replicator?.LastFailure, rejectedReplicationPackets = replicator?.RejectedPackets ?? 0,
                replicationTransport = transport?.CaptureDiagnosticState(),
                sinkFailures = CombatEvidence.Failures, sinkCalls = Interlocked.Read(ref sinkCalls), sinkMs = Interlocked.Read(ref sinkTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency,
                retainedBytes = store.Memory.Used, peakRetainedBytes = store.Memory.Peak, peakQueueBytes = store.PeakPendingBytes,
                evidenceStages = store.Metrics.Snapshot(),
                replicationMainMs = replicator?.MainMilliseconds ?? 0, replicationWorkerMs = replicator?.WorkerMilliseconds ?? 0, dropped = store.Dropped, writerFailure = store.LastFailure, writerMs = store.WriteMilliseconds,
                checkpointMs = captureTicks * 1000d / System.Diagnostics.Stopwatch.Frequency };
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
            var snapshot = new List<(string type, object input)>();
            lock (errorGate)
            {
                foreach (var error in errors.Values)
                {
                    if (error.count == error.reported) continue;
                    snapshot.Add((error.type, new { error.message, error.type, stack = error.reported == 0 ? error.stack : null,
                        error.first, error.last, count = error.count - error.reported, totalCount = error.count }));
                    error.reported = error.count;
                }
            }
            foreach (var error in snapshot) TryWrite(new DiagnosticRecord { role = "Process", stage = "unity.exception", reason = error.type,
                input = error.input, critical = true, estimatedBytes = 64 * 1024 });
        }
    }
}
