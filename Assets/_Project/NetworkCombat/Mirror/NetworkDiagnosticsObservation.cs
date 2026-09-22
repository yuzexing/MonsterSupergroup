using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using Mirror.FizzySteam;
using Unity.Profiling;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Passive observation of the ordinary Boot/Steam flow. Never selects a backend or supplies input.
    public sealed class NetworkDiagnosticsObservation : MonoBehaviour
    {
        private static bool enabledForRun;
        public static long SnapshotCount, SnapshotBytes, ReliableSnapshotPackets;
        private LimboObservationLog log;
        private ProfilerRecorder main, allocations, gc;
        private readonly List<ProfilerRecorderSample> gcSamples = new List<ProfilerRecorderSample>(1);
        private readonly FrameTiming[] timing = new FrameTiming[1];
        private readonly List<SteamConnectionSample> connections = new List<SteamConnectionSample>(4);
        private readonly float[] frames = new float[4096];
        private int frameCount, frameOverflow;
        private double nextSample, sumFrame, maxFrame, allocatedBytes, gcMilliseconds, maximumMainMs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            enabledForRun = Array.IndexOf(Environment.GetCommandLineArgs(), "--network-diagnostics") >= 0;
            SteamTransportDiagnostics.Enabled = enabledForRun;
            SnapshotCount = SnapshotBytes = ReliableSnapshotPackets = 0;
            if (!enabledForRun) return;
            SteamTransportDiagnostics.Reset();
            var root = new GameObject("Network diagnostics");
            DontDestroyOnLoad(root);
            root.AddComponent<NetworkDiagnosticsObservation>();
        }

        internal static void RecordSnapshot(int bytes)
        { if (enabledForRun) { SnapshotCount++; SnapshotBytes += bytes; } }
        internal static void RecordReliableSnapshot()
        { if (enabledForRun) ReliableSnapshotPackets++; }

        private void Start()
        {
            string directory = Path.Combine(Application.persistentDataPath, "NetworkDiagnostics");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
            log = new LimboObservationLog(path);
            log.WriteLine(JsonUtility.ToJson(new Header
            {
                buildGuid = Application.buildGUID, version = Application.version, unity = Application.unityVersion,
                development = Debug.isDebugBuild, protocol = SteamLobbyMetadata.ProtocolValue,
                width = Screen.width, height = Screen.height, targetFps = Application.targetFrameRate,
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                rejectionReasons = Enum.GetNames(typeof(CombatRejectionReason)),
                vSync = QualitySettings.vSyncCount, graphics = SystemInfo.graphicsDeviceType.ToString()
            }));
            main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Collect", 1);
            nextSample = Time.realtimeSinceStartupAsDouble + 1;
            Debug.Log("[NetworkDiagnostics] " + path);
        }

        private void LateUpdate()
        {
            if (log == null) return;
            FrameTimingManager.CaptureFrameTimings();
            float ms = Time.unscaledDeltaTime * 1000f;
            sumFrame += ms; maxFrame = Math.Max(maxFrame, ms);
            if (frameCount < frames.Length) frames[frameCount++] = ms; else frameOverflow++;
            if (allocations.Valid) allocatedBytes += allocations.LastValue;
            if (gc.Valid)
            {
                // Consume each GC marker once; LastValue alone can repeat an old collection.
                gc.CopyTo(gcSamples, true);
                foreach (var sample in gcSamples) gcMilliseconds += sample.Value / 1e6;
                if (!gc.IsRunning) gc.Start();
            }
            if (main.Valid) maximumMainMs = Math.Max(maximumMainMs, main.LastValue / 1e6);
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextSample) return;
            nextSample = now + 1;
            Array.Sort(frames, 0, frameCount);
            var world = NetworkCombatWorld.Instance;
            var metrics = world?.Gateway?.Metrics;
            var collector = NetworkClient.localPlayer?.GetComponent<MirrorNetworkCombatBridge>()?.Collector;
            var progress = world != null ? world.GetComponent<NetworkWaveProgress>() : null;
            connections.Clear();
            if (Transport.active is FizzySteamworks steam) steam.ReadConnectionDiagnostics(connections);
            uint timingCount = FrameTimingManager.GetLatestTimings(1, timing);
            var row = new Row
            {
                time = now, round = NetworkCombatWorld.CurrentRound,
                role = NetworkServer.active ? "host" : NetworkClient.active ? "client" : "offline",
                transport = Transport.active != null ? Transport.active.GetType().Name : "none",
                run = progress != null ? progress.Snapshot.RunId : "", alive = progress != null ? progress.Snapshot.Alive : 0,
                frameCount = frameCount + frameOverflow, frameOverflow = frameOverflow,
                frameMeanMs = sumFrame / Math.Max(1, frameCount + frameOverflow), frameMaxMs = maxFrame,
                frameP95Ms = Percentile(.95), frameP99Ms = Percentile(.99), mainMaxMs = main.Valid ? maximumMainMs : -1,
                gpuMs = timingCount > 0 && timing[0].gpuFrameTime > 0 ? timing[0].gpuFrameTime : -1,
                renderMs = timingCount > 0 ? timing[0].cpuRenderThreadFrameTime : -1,
                allocatedBytes = allocations.Valid ? allocatedBytes : -1, gcMs = gc.Valid ? gcMilliseconds : -1,
                gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
                sentBytes = SteamTransportDiagnostics.SentBytes, receivedBytes = SteamTransportDiagnostics.ReceivedBytes,
                sentMessages = SteamTransportDiagnostics.SentMessages, receivedMessages = SteamTransportDiagnostics.ReceivedMessages,
                maxSentMessage = SteamTransportDiagnostics.MaximumSentMessage, sendFailures = SteamTransportDiagnostics.SendFailures,
                connections = connections.ToArray(), snapshots = SnapshotCount, snapshotBytes = SnapshotBytes,
                reliableSnapshotPackets = ReliableSnapshotPackets,
                acceptedDamage = metrics?.AcceptedCombatResults ?? 0, receivedDamage = metrics?.ReceivedCombatResults ?? 0,
                deathReports = metrics?.ReceivedEnemyDeathReports ?? 0, deathReceipts = metrics?.ConfirmedEnemyDeathReports ?? 0,
                confirmedKills = metrics?.ConfirmedKills ?? 0,
                pendingDeaths = collector?.PendingEnemyDeathCount ?? 0,
                oldestPendingDeathSeconds = collector?.OldestPendingDeathAge(Time.unscaledTimeAsDouble) ?? 0,
                lastDeathConfirmationSeconds = collector?.LastDeathConfirmationSeconds ?? 0,
                maximumDeathConfirmationSeconds = collector?.MaximumDeathConfirmationSeconds ?? 0,
                logFailures = LimboObservationLog.FailureCount
            };
            row.rejections = new long[(int)CombatRejectionReason.RunLoading + 1];
            for (int i = 0; i < row.rejections.Length; i++) row.rejections[i] = metrics?.GetRejected((CombatRejectionReason)i) ?? 0;
            log.WriteLine(JsonUtility.ToJson(row));
            frameCount = frameOverflow = 0; sumFrame = maxFrame = allocatedBytes = gcMilliseconds = maximumMainMs = 0;
        }
        private double Percentile(double percentile) => frameCount == 0 ? 0 : frames[Math.Min(frameCount - 1, (int)Math.Ceiling(frameCount * percentile) - 1)];
        private void OnDestroy()
        { main.Dispose(); allocations.Dispose(); gc.Dispose(); log?.Dispose(); log = null; SteamTransportDiagnostics.Enabled = false; enabledForRun = false; }

        [Serializable] private sealed class Header
        {
            public string kind = "header", buildGuid, version, unity, protocol, graphics, quality;
            public string[] rejectionReasons;
            public bool development;
            public int width, height, targetFps, vSync;
        }
        [Serializable] private sealed class Row
        {
            public string kind = "sample", role, transport, run;
            public uint round;
            public int alive, frameCount, frameOverflow, gen0, gen1, gen2, pendingDeaths, logFailures;
            public double time, frameMeanMs, frameMaxMs, frameP95Ms, frameP99Ms, mainMaxMs, gpuMs, renderMs, allocatedBytes, gcMs;
            public double oldestPendingDeathSeconds, lastDeathConfirmationSeconds, maximumDeathConfirmationSeconds;
            public long[] sentBytes, receivedBytes, sentMessages, receivedMessages, rejections;
            public int[] maxSentMessage;
            public SteamConnectionSample[] connections;
            public long sendFailures, snapshots, snapshotBytes, reliableSnapshotPackets, acceptedDamage, receivedDamage, deathReports, deathReceipts, confirmedKills;
        }
    }
}
