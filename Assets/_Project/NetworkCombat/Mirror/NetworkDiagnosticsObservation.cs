using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using AstralShift.DebugTools;
using AstralShift.HellMaiden.Player;
using Mirror;
using Mirror.FizzySteam;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace MonsterSupergroup.NetworkCombat
{
    // Opt-in observer of ordinary Boot/Steam play. No backend, gameplay or graphics overrides.
    public sealed class NetworkDiagnosticsObservation : MonoBehaviour
    {
        public const int SchemaVersion = 2;
        private static bool enabledForRun;
        public static bool Enabled => enabledForRun;
        public static long SnapshotCount, SnapshotBytes, ReliableSnapshotPackets;
        private static int overlayState = -1, overlayChanges;
        private LimboObservationLog log;
        private ProfilerRecorder main, allocations, gc;
        private readonly List<ProfilerRecorderSample> gcSamples = new(1);
        private readonly FrameTiming[] timing = new FrameTiming[1];
        private readonly List<SteamConnectionSample> connections = new(4);
        private List<SteamConnectionInvestigationSample> investigationConnections;
        private readonly NetworkDiagnosticsWindow window = new();
        private readonly List<LongFrame> longFrames = new(8);
        private System.Diagnostics.Process process;
        private string captureId, utcStart;
        private int processId, warnings, errors, deadWarnings, focusChanges, pauseChanges, displayChanges;
        private int lastWidth, lastHeight, lastMode;
        private double nextSample, windowStart, allocatedBytes, gcMilliseconds, maximumMainMs;
        private bool closed, unifiedEvidence;
        public string OutputPath { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            enabledForRun = Array.IndexOf(Environment.GetCommandLineArgs(), "--network-diagnostics") >= 0;
            SteamTransportDiagnostics.Enabled = CombatPerformanceCounters.Enabled = enabledForRun;
            SnapshotCount = SnapshotBytes = ReliableSnapshotPackets = 0;
            overlayState = -1; overlayChanges = 0;
            CombatPerformanceCounters.Reset();
            if (!enabledForRun) return;
            SteamTransportDiagnostics.Reset();
            var root = new GameObject("Network diagnostics");
            DontDestroyOnLoad(root);
            root.AddComponent<NetworkDiagnosticsObservation>();
        }
        internal static void RecordSnapshot(int bytes) { if (enabledForRun) { SnapshotCount++; SnapshotBytes += bytes; } }
        internal static void RecordReliableSnapshot() { if (enabledForRun) ReliableSnapshotPackets++; }
        public static void RecordOverlay(bool active) { if (enabledForRun) { overlayState = active ? 1 : 0; overlayChanges++; } }
        private static string Argument(string key)
        {
            foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith(key, StringComparison.Ordinal)) return arg.Substring(key.Length);
            return null;
        }
        private void Start()
        {
            Diagnostics.NetworkMessageEvidence.Install();
            // Also supports an explicitly added observer in diagnostics tests.
            enabledForRun = SteamTransportDiagnostics.Enabled = CombatPerformanceCounters.Enabled = true;
            string directory = Argument("--network-diagnostics-output=") ?? Path.Combine(Application.persistentDataPath, "NetworkDiagnostics");
            unifiedEvidence = MonsterSupergroup.GAS.CombatEvidence.Enabled;
            if (!unifiedEvidence) Directory.CreateDirectory(directory);
            captureId = Guid.NewGuid().ToString("N"); utcStart = DateTime.UtcNow.ToString("o");
            process = System.Diagnostics.Process.GetCurrentProcess(); processId = process.Id;
            OutputPath = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + processId + "-" + captureId + ".jsonl");
            if (!unifiedEvidence) log = new LimboObservationLog(OutputPath);
            else OutputPath = Diagnostics.CombatEvidenceRuntime.Instance?.Store.Root;
            lastWidth = Screen.width; lastHeight = Screen.height; lastMode = (int)Screen.fullScreenMode;
            string header = JsonUtility.ToJson(new Header {
                captureId = captureId, utcStart = utcStart, processId = processId,
                buildGuid = Application.buildGUID, version = Application.version, buildInfo = MonsterSupergroup.Builds.RuntimeBuildInfo.Current?.ToJson(), unity = Application.unityVersion,
                development = Debug.isDebugBuild, protocol = SteamLobbyMetadata.ProtocolValue,
                width = Screen.width, height = Screen.height, targetFps = Application.targetFrameRate,
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                rejectionReasons = Enum.GetNames(typeof(CombatRejectionReason)), areas = CombatPerformanceCounters.Names,
                vSync = QualitySettings.vSyncCount, graphics = SystemInfo.graphicsDeviceType.ToString(),
                gpu = SystemInfo.graphicsDeviceName, driver = SystemInfo.graphicsDeviceVersion,
                cpu = SystemInfo.processorType, systemMemoryMb = SystemInfo.systemMemorySize,
                commandLine = Environment.CommandLine, monotonicStart = Time.realtimeSinceStartupAsDouble });
            log?.WriteLine(header);
            if (unifiedEvidence) MonsterSupergroup.GAS.CombatEvidence.Event("Process", "performance.header", "Started", null, input: header, bytes: header.Length * 2 + 2048);
            main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Collect", 1);
            windowStart = Time.realtimeSinceStartupAsDouble; nextSample = windowStart + 1;
            Application.logMessageReceivedThreaded += CountLog;
            Debug.Log("[NetworkDiagnostics] " + OutputPath);
        }
        private void CountLog(string text, string stack, LogType type)
        {
            if (type == LogType.Warning) Interlocked.Increment(ref warnings);
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Interlocked.Increment(ref errors);
            if (text.Contains("Dead -> INVALID TRANSITION to: Hurt")) Interlocked.Increment(ref deadWarnings);
        }
        private void OnApplicationFocus(bool focused) { focusChanges++; }
        private void OnApplicationPause(bool paused) { pauseChanges++; }
        private void LateUpdate()
        {
            if ((log == null && !unifiedEvidence) || closed) return;
            FrameTimingManager.CaptureFrameTimings();
            double now = Time.realtimeSinceStartupAsDouble;
            float ms = Time.unscaledDeltaTime * 1000f;
            if (window.Add(ms)) longFrames.Add(new LongFrame { frame = Time.frameCount, time = now,
                utc = DateTime.UtcNow.ToString("o"), networkTime = NetworkTime.time, frameMs = ms,
                mainMs = main.Valid ? main.LastValue / 1e6 : -1,
                allocatedBytes = allocations.Valid ? allocations.LastValue : -1 });
            if (allocations.Valid) allocatedBytes += allocations.LastValue;
            if (gc.Valid)
            {
                gc.CopyTo(gcSamples, true);
                foreach (var sample in gcSamples) gcMilliseconds += sample.Value / 1e6;
                if (!gc.IsRunning) gc.Start();
            }
            if (main.Valid) maximumMainMs = Math.Max(maximumMainMs, main.LastValue / 1e6);
            if (Screen.width != lastWidth || Screen.height != lastHeight || (int)Screen.fullScreenMode != lastMode)
            { displayChanges++; lastWidth = Screen.width; lastHeight = Screen.height; lastMode = (int)Screen.fullScreenMode; }
            if (now >= nextSample) { Emit(now); nextSample = now + 1; }
        }
        private void Emit(double now)
        {
            if (window.Count == 0) return;
            bool investigation = Diagnostics.CombatInvestigationEvidence.Enabled;
            long clockReadStarted = 0, clockReadEnded = 0;
            double clockRealtime = 0, clockUnscaled = 0, clockNetwork = 0;
            if (investigation)
            {
                clockReadStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                clockRealtime = Time.realtimeSinceStartupAsDouble;
                clockUnscaled = Time.unscaledTimeAsDouble;
                clockNetwork = NetworkTime.time;
                clockReadEnded = System.Diagnostics.Stopwatch.GetTimestamp();
                (investigationConnections ??= new List<SteamConnectionInvestigationSample>(4)).Clear();
            }
            window.Sort();
            var world = NetworkCombatWorld.Instance;
            var metrics = world?.Gateway?.Metrics;
            var owner = NetworkClient.localPlayer;
            var binding = owner != null ? owner.GetComponent<PlayerCombatantBinding>() : null;
            var collector = owner != null ? owner.GetComponent<MirrorNetworkCombatBridge>()?.Collector : null;
            var progress = world != null ? world.GetComponent<NetworkWaveProgress>() : null;
            var manager = NetworkManager.singleton as BootGameplayNetworkManager;
            connections.Clear();
            if (Transport.active is FizzySteamworks steam) steam.ReadConnectionDiagnostics(connections, investigation ? investigationConnections : null);
            uint timingCount = FrameTimingManager.GetLatestTimings(1, timing);
            long working = -1, privateBytes = -1;
            ReadProcessMemory(out working, out privateBytes);
            var evidenceStore = MonsterSupergroup.NetworkCombat.Diagnostics.CombatEvidenceRuntime.Instance?.Store;
            // Use separate concrete rows so Standard/off retains its original JSON fields.
            Row row = investigation ? new InvestigationRow {
                clockDomain = "Stopwatch/UnityRealtime/UnityUnscaled/MirrorNetworkTime",
                clockReadStartedTicks = clockReadStarted, clockReadEndedTicks = clockReadEnded,
                stopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                clockRealtimeSeconds = clockRealtime, clockUnscaledSeconds = clockUnscaled, clockNetworkSeconds = clockNetwork,
                pendingDeathAgeClock = "Unity.UnscaledTime", pendingDeathAgeClockVersion = 1,
                connections = investigationConnections.ToArray()
            } : new StandardRow { connections = connections.ToArray() };
            row.captureId = captureId;
            row.processId = processId;
            row.utc = DateTime.UtcNow.ToString("o");
            row.time = now;
            row.windowSeconds = now - windowStart;
            row.networkTime = NetworkTime.time;
            row.rttMs = NetworkTime.rtt * 1000;
            row.round = NetworkCombatWorld.CurrentRound;
            row.role = NetworkServer.active ? "host" : NetworkClient.active ? "client" : "offline";
            row.transport = Transport.active != null ? Transport.active.GetType().Name : "none";
            row.run = progress != null ? progress.Snapshot.RunId : "";
            row.alive = progress != null ? progress.Snapshot.Alive : 0;
            row.phase = progress != null ? progress.Snapshot.Phase.ToString() : "none";
            row.playerCount = NetworkServer.active ? manager?.Session?.Participants.Count ?? 0 : manager?.RoomSnapshot.Members?.Length ?? 0;
            row.localHealth = binding != null ? binding.CurrentHealth : -1;
            row.localAlive = binding != null && binding.IsAlive;
            row.frameCount = window.Count;
            row.frameOverflow = window.Overflow;
            row.frameMeanMs = window.Mean;
            row.frameMaxMs = window.Maximum;
            row.frameP95Ms = window.Percentile(.95);
            row.frameP99Ms = window.Percentile(.99);
            row.frameHistogram = window.Histogram;
            row.longFrames = longFrames.ToArray();
            row.longFrameCount = window.LongFrames;
            row.omittedLongFrames = window.LongFrames - window.LongDetails;
            row.mainMaxMs = main.Valid ? maximumMainMs : -1;
            row.gpuMs = timingCount > 0 && timing[0].gpuFrameTime > 0 ? timing[0].gpuFrameTime : -1;
            row.renderMs = timingCount > 0 ? timing[0].cpuRenderThreadFrameTime : -1;
            row.mainWorkMs = timingCount > 0 ? timing[0].cpuMainThreadFrameTime : -1;
            row.presentWaitMs = timingCount > 0 ? timing[0].cpuMainThreadPresentWaitTime : -1;
            row.timingTimestamp = timingCount > 0 ? timing[0].frameStartTimestamp : 0;
            row.allocatedBytes = allocations.Valid ? allocatedBytes : -1;
            row.gcMs = gc.Valid ? gcMilliseconds : -1;
            row.managedBytes = GC.GetTotalMemory(false);
            row.unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
            row.workingSetBytes = working;
            row.privateBytes = privateBytes;
            row.systemAvailableMemoryBytes = ReadSystemAvailableMemory();
            row.evidenceQueuedBytes = evidenceStore?.ReadShutdownProgress().pendingBytes ?? -1;
            row.evidencePeakQueuedBytes = evidenceStore?.PeakPendingBytes ?? -1;
            row.evidenceBudgetBytes = evidenceStore?.Memory.Used ?? -1;
            row.evidencePeakBudgetBytes = evidenceStore?.Memory.Peak ?? -1;
            row.gen0 = GC.CollectionCount(0);
            row.gen1 = GC.CollectionCount(1);
            row.gen2 = GC.CollectionCount(2);
            row.warnings = Volatile.Read(ref warnings);
            row.errors = Volatile.Read(ref errors);
            row.deadWarnings = Volatile.Read(ref deadWarnings);
            row.focused = Application.isFocused;
            row.fullScreenMode = Screen.fullScreenMode.ToString();
            row.width = Screen.width;
            row.height = Screen.height;
            row.focusChanges = focusChanges;
            row.pauseChanges = pauseChanges;
            row.displayChanges = displayChanges;
            row.overlayState = overlayState;
            row.overlayChanges = overlayChanges;
            row.areas = CombatPerformanceCounters.ReadAndReset();
            row.enemyMotion = NetworkEnemySimulationWorld.Instance?.CaptureMotionDiagnostics();
            row.sentBytes = SteamTransportDiagnostics.SentBytes;
            row.receivedBytes = SteamTransportDiagnostics.ReceivedBytes;
            row.sentMessages = SteamTransportDiagnostics.SentMessages;
            row.receivedMessages = SteamTransportDiagnostics.ReceivedMessages;
            row.maxSentMessage = SteamTransportDiagnostics.MaximumSentMessage;
            row.sendFailures = SteamTransportDiagnostics.SendFailures;
            row.snapshots = SnapshotCount;
            row.snapshotBytes = SnapshotBytes;
            row.reliableSnapshotPackets = ReliableSnapshotPackets;
            row.acceptedDamage = metrics?.AcceptedCombatResults ?? 0;
            row.receivedDamage = metrics?.ReceivedCombatResults ?? 0;
            row.deathReports = metrics?.ReceivedEnemyDeathReports ?? 0;
            row.deathReceipts = metrics?.ConfirmedEnemyDeathReports ?? 0;
            row.confirmedKills = metrics?.ConfirmedKills ?? 0;
            row.pendingDeaths = collector?.PendingEnemyDeathCount ?? 0;
            row.oldestPendingDeathSeconds = collector?.OldestPendingDeathAge(Time.unscaledTimeAsDouble) ?? 0;
            row.lastDeathConfirmationSeconds = collector?.LastDeathConfirmationSeconds ?? 0;
            row.maximumDeathConfirmationSeconds = collector?.MaximumDeathConfirmationSeconds ?? 0;
            row.logFailures = LimboObservationLog.FailureCount;
            row.logQueuedBytes = LimboObservationLog.PendingBytes;
            row.rejections = new long[(int)CombatRejectionReason.RunLoading + 1];
            for (int i = 0; i < row.rejections.Length; i++) row.rejections[i] = metrics?.GetRejected((CombatRejectionReason)i) ?? 0;
            string serializedRow = JsonUtility.ToJson(row); // Detach mutable window arrays before resetting.
            log?.WriteLine(serializedRow);
            if (MonsterSupergroup.GAS.CombatEvidence.Enabled) MonsterSupergroup.GAS.CombatEvidence.Event("Process", "performance.snapshot", "Observed", null,
                input: serializedRow, bytes: serializedRow.Length * 2 + 2048);
            window.Reset(); longFrames.Clear(); windowStart = now;
            allocatedBytes = gcMilliseconds = maximumMainMs = 0;
        }
        private void OnApplicationQuit() => Close();
        private void OnDestroy() => Close();
        private void ReadProcessMemory(out long working, out long privateBytes)
        {
            working = privateBytes = -1;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Mono's Process memory properties return zero in some Windows Players.
            uint size = (uint)Marshal.SizeOf<ProcessMemory>();
            var memory = new ProcessMemory { size = size };
            if (GetProcessMemoryInfo(GetCurrentProcess(), ref memory, size))
            {
                working = (long)memory.workingSet.ToUInt64();
                privateBytes = (long)memory.privateUsage.ToUInt64();
            }
#else
            try { process.Refresh(); working = process.WorkingSet64; privateBytes = process.PrivateMemorySize64; }
            catch (Exception) { /* Unavailable on some platforms. */ }
#endif
            if (working <= 0) working = -1;
            if (privateBytes <= 0) privateBytes = -1;
        }
        private static long ReadSystemAvailableMemory()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var status = new SystemMemory { size = (uint)Marshal.SizeOf<SystemMemory>() };
            if (GlobalMemoryStatusEx(ref status) && status.availablePhysical <= long.MaxValue)
                return (long)status.availablePhysical;
#endif
            return -1;
        }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct SystemMemory
        {
            public uint size, load;
            public ulong totalPhysical, availablePhysical, totalPageFile, availablePageFile, totalVirtual, availableVirtual, availableExtendedVirtual;
        }
        [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref SystemMemory status);
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemory
        {
            public uint size, pageFaults;
            public UIntPtr peakWorkingSet, workingSet, peakPagedPool, pagedPool, peakNonPagedPool, nonPagedPool, pagefile, peakPagefile, privateUsage;
        }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")][return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(IntPtr processHandle, ref ProcessMemory memory, uint size);
#endif
        private void Close()
        {
            if (closed) return;
            closed = true;
            Application.logMessageReceivedThreaded -= CountLog;
            if (log != null || unifiedEvidence) Emit(Time.realtimeSinceStartupAsDouble);
            main.Dispose(); allocations.Dispose(); gc.Dispose(); log?.Dispose(); log = null; process?.Dispose();
            SteamTransportDiagnostics.Enabled = CombatPerformanceCounters.Enabled = enabledForRun = false;
        }
        [Serializable] private sealed class Header
        {
            public string kind = "header";
            public int schemaVersion = SchemaVersion;
            public string captureId, utcStart, buildInfo, buildGuid, version, unity, protocol, graphics, quality, gpu, driver, cpu, commandLine;
            public string[] rejectionReasons, areas;
            public bool development;
            public int processId, width, height, targetFps, vSync, systemMemoryMb;
            public double monotonicStart;
        }
        [Serializable] private sealed class LongFrame
        {
            public int frame; public string utc; public double time, networkTime, frameMs, mainMs; public long allocatedBytes;
        }
        [Serializable] private sealed class StandardRow : Row
        {
            public SteamConnectionSample[] connections;
        }
        [Serializable] private sealed class InvestigationRow : Row
        {
            public SteamConnectionInvestigationSample[] connections;
            public string clockDomain, pendingDeathAgeClock;
            public int pendingDeathAgeClockVersion;
            public long clockReadStartedTicks, clockReadEndedTicks, stopwatchFrequency;
            public double clockRealtimeSeconds, clockUnscaledSeconds, clockNetworkSeconds;
        }
        [Serializable] private class Row
        {
            public string kind = "sample", captureId, utc, role, transport, run, phase, fullScreenMode;
            public uint round;
            public int processId, playerCount, localHealth, alive, frameCount, frameOverflow, gen0, gen1, gen2, pendingDeaths, logFailures;
            public int warnings, errors, deadWarnings, longFrameCount, omittedLongFrames, width, height, focusChanges, pauseChanges, displayChanges, overlayState, overlayChanges;
            public bool localAlive, focused;
            public double time, networkTime, rttMs, windowSeconds, frameMeanMs, frameMaxMs, frameP95Ms, frameP99Ms, mainMaxMs, gpuMs, renderMs, mainWorkMs, presentWaitMs, allocatedBytes, gcMs;
            public ulong timingTimestamp;
            public double oldestPendingDeathSeconds, lastDeathConfirmationSeconds, maximumDeathConfirmationSeconds;
            public long managedBytes, unityAllocatedBytes, workingSetBytes, privateBytes, logQueuedBytes;
            public long systemAvailableMemoryBytes, evidenceQueuedBytes, evidencePeakQueuedBytes, evidenceBudgetBytes, evidencePeakBudgetBytes;
            public long[] sentBytes, receivedBytes, sentMessages, receivedMessages, rejections;
            public int[] maxSentMessage, frameHistogram;
            public LongFrame[] longFrames;
            public CombatPerformanceCounters.Sample areas;
            public EnemyMotionDiagnosticSample enemyMotion;
            public long sendFailures, snapshots, snapshotBytes, reliableSnapshotPackets, acceptedDamage, receivedDamage, deathReports, deathReceipts, confirmedKills;
        }
    }
}
