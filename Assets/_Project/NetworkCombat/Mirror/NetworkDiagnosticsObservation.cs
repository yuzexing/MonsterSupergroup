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
            window.Sort();
            var world = NetworkCombatWorld.Instance;
            var metrics = world?.Gateway?.Metrics;
            var owner = NetworkClient.localPlayer;
            var binding = owner != null ? owner.GetComponent<PlayerCombatantBinding>() : null;
            var collector = owner != null ? owner.GetComponent<MirrorNetworkCombatBridge>()?.Collector : null;
            var progress = world != null ? world.GetComponent<NetworkWaveProgress>() : null;
            var manager = NetworkManager.singleton as BootGameplayNetworkManager;
            connections.Clear();
            if (Transport.active is FizzySteamworks steam) steam.ReadConnectionDiagnostics(connections);
            uint timingCount = FrameTimingManager.GetLatestTimings(1, timing);
            long working = -1, privateBytes = -1;
            ReadProcessMemory(out working, out privateBytes);
            var row = new Row {
                captureId = captureId, processId = processId, utc = DateTime.UtcNow.ToString("o"), time = now,
                windowSeconds = now - windowStart, networkTime = NetworkTime.time, rttMs = NetworkTime.rtt * 1000,
                round = NetworkCombatWorld.CurrentRound,
                role = NetworkServer.active ? "host" : NetworkClient.active ? "client" : "offline",
                transport = Transport.active != null ? Transport.active.GetType().Name : "none",
                run = progress != null ? progress.Snapshot.RunId : "", alive = progress != null ? progress.Snapshot.Alive : 0,
                phase = progress != null ? progress.Snapshot.Phase.ToString() : "none",
                playerCount = NetworkServer.active ? manager?.Session?.Participants.Count ?? 0 : manager?.RoomSnapshot.Members?.Length ?? 0,
                localHealth = binding != null ? binding.CurrentHealth : -1, localAlive = binding != null && binding.IsAlive,
                frameCount = window.Count, frameOverflow = window.Overflow, frameMeanMs = window.Mean, frameMaxMs = window.Maximum,
                frameP95Ms = window.Percentile(.95), frameP99Ms = window.Percentile(.99), frameHistogram = window.Histogram,
                longFrames = longFrames.ToArray(), longFrameCount = window.LongFrames, omittedLongFrames = window.LongFrames - window.LongDetails,
                mainMaxMs = main.Valid ? maximumMainMs : -1,
                gpuMs = timingCount > 0 && timing[0].gpuFrameTime > 0 ? timing[0].gpuFrameTime : -1,
                renderMs = timingCount > 0 ? timing[0].cpuRenderThreadFrameTime : -1,
                mainWorkMs = timingCount > 0 ? timing[0].cpuMainThreadFrameTime : -1,
                presentWaitMs = timingCount > 0 ? timing[0].cpuMainThreadPresentWaitTime : -1,
                timingTimestamp = timingCount > 0 ? timing[0].frameStartTimestamp : 0,
                allocatedBytes = allocations.Valid ? allocatedBytes : -1, gcMs = gc.Valid ? gcMilliseconds : -1,
                managedBytes = GC.GetTotalMemory(false), unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
                workingSetBytes = working, privateBytes = privateBytes,
                gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
                warnings = Volatile.Read(ref warnings), errors = Volatile.Read(ref errors), deadWarnings = Volatile.Read(ref deadWarnings),
                focused = Application.isFocused, fullScreenMode = Screen.fullScreenMode.ToString(), width = Screen.width, height = Screen.height,
                focusChanges = focusChanges, pauseChanges = pauseChanges, displayChanges = displayChanges,
                overlayState = overlayState, overlayChanges = overlayChanges,
                areas = CombatPerformanceCounters.ReadAndReset(),
                enemyMotion = NetworkEnemySimulationWorld.Instance?.CaptureMotionDiagnostics(),
                sentBytes = SteamTransportDiagnostics.SentBytes, receivedBytes = SteamTransportDiagnostics.ReceivedBytes,
                sentMessages = SteamTransportDiagnostics.SentMessages, receivedMessages = SteamTransportDiagnostics.ReceivedMessages,
                maxSentMessage = SteamTransportDiagnostics.MaximumSentMessage, sendFailures = SteamTransportDiagnostics.SendFailures,
                connections = connections.ToArray(), snapshots = SnapshotCount, snapshotBytes = SnapshotBytes,
                reliableSnapshotPackets = ReliableSnapshotPackets,
                acceptedDamage = metrics?.AcceptedCombatResults ?? 0, receivedDamage = metrics?.ReceivedCombatResults ?? 0,
                deathReports = metrics?.ReceivedEnemyDeathReports ?? 0, deathReceipts = metrics?.ConfirmedEnemyDeathReports ?? 0,
                confirmedKills = metrics?.ConfirmedKills ?? 0, pendingDeaths = collector?.PendingEnemyDeathCount ?? 0,
                oldestPendingDeathSeconds = collector?.OldestPendingDeathAge(Time.unscaledTimeAsDouble) ?? 0,
                lastDeathConfirmationSeconds = collector?.LastDeathConfirmationSeconds ?? 0,
                maximumDeathConfirmationSeconds = collector?.MaximumDeathConfirmationSeconds ?? 0,
                logFailures = LimboObservationLog.FailureCount, logQueuedBytes = LimboObservationLog.PendingBytes };
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
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
        [Serializable] private sealed class Row
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
            public long[] sentBytes, receivedBytes, sentMessages, receivedMessages, rejections;
            public int[] maxSentMessage, frameHistogram;
            public LongFrame[] longFrames;
            public CombatPerformanceCounters.Sample areas;
            public EnemyMotionDiagnosticSample enemyMotion;
            public SteamConnectionSample[] connections;
            public long sendFailures, snapshots, snapshotBytes, reliableSnapshotPackets, acceptedDamage, receivedDamage, deathReports, deathReceipts, confirmedKills;
        }
    }
}
