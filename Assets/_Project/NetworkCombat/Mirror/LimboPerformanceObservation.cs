using System;
using System.IO;
using System.Threading;
using Unity.Profiling;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Passive counters plus long-frame events. No inputs, screenshots or enemy scans.</summary>
    public sealed class LimboPerformanceObservation : MonoBehaviour
    {
        private ProfilerRecorder main, allocations, gc, spawn, placement, offscreen, planarBounds;
        private readonly FrameTiming[] timing = new FrameTiming[1];
        // Exclusive bins: <=8.34, <=16.67, <=25, <=50, <=100, <=250, >250 ms.
        private readonly int[] frameHistogram = new int[7];
        private LimboObservationLog log;
        private double nextSample;
        private int warnings, errors, animationEvents, animationSpeed, staticBody, lastFailures;
        private void Start()
        {
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log = new LimboObservationLog(Path.Combine(LimboReferenceLaunch.OutputDirectory, "performance-detail.jsonl"));
            main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            // Register the shared marker names before the gameplay scene/first effect exists.
            spawn = ProfilerRecorder.StartNew(new ProfilerMarker("Limbo.Spawn"), 1);
            placement = ProfilerRecorder.StartNew(new ProfilerMarker("Limbo.Placement"), 1);
            offscreen = ProfilerRecorder.StartNew(new ProfilerMarker("Limbo.Offscreen"), 1);
            planarBounds = ProfilerRecorder.StartNew(new ProfilerMarker("Limbo.PlanarBounds"), 1);
            allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Collect", 1);
            Application.logMessageReceivedThreaded += CountMessage;
        }
        private void CountMessage(string message, string stack, LogType type)
        {
            if (type == LogType.Warning) Interlocked.Increment(ref warnings);
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Interlocked.Increment(ref errors);
            if (message.Contains("UselessEvent")) Interlocked.Increment(ref animationEvents);
            if (message.Contains("Animator.speed doesn't affect Animancer")) Interlocked.Increment(ref animationSpeed);
            if (message.Contains("linearVelocity") && message.Contains("static body")) Interlocked.Increment(ref staticBody);
        }
        private void LateUpdate()
        {
            if (log == null) return;
            FrameTimingManager.CaptureFrameTimings();
            double now = Time.realtimeSinceStartupAsDouble;
            float frameMs = Time.unscaledDeltaTime * 1000;
            frameHistogram[frameMs <= 8.34f ? 0 : frameMs <= 16.67f ? 1 : frameMs <= 25 ? 2 :
                frameMs <= 50 ? 3 : frameMs <= 100 ? 4 : frameMs <= 250 ? 5 : 6]++;
            if (now < nextSample && frameMs < 100) return;
            nextSample = now + 1;
            uint count = FrameTimingManager.GetLatestTimings(1, timing);
            var world = NetworkCombatWorld.Instance;
            var progress = world != null ? world.GetComponent<NetworkWaveProgress>().Snapshot : default;
            log.WriteLine(JsonUtility.ToJson(new Row {
                kind = frameMs >= 100 ? "long-frame" : "sample", realtime = now, elapsed = progress.Elapsed,
                frameHistogram = frameHistogram,
                run = progress.RunId, phase = (int)progress.Phase, frame = Time.frameCount, alive = progress.Alive, frameMs = frameMs,
                mainMs = main.Valid ? main.LastValue / 1e6 : -1,
                renderMs = count > 0 ? timing[0].cpuRenderThreadFrameTime : -1,
                mainWorkMs = count > 0 ? timing[0].cpuMainThreadFrameTime : -1,
                presentWaitMs = count > 0 ? timing[0].cpuMainThreadPresentWaitTime : -1,
                timingTimestamp = count > 0 ? timing[0].frameStartTimestamp : 0,
                spawnMs = spawn.Valid ? spawn.LastValue / 1e6 : -1, placementMs = placement.Valid ? placement.LastValue / 1e6 : -1,
                offscreenMs = offscreen.Valid ? offscreen.LastValue / 1e6 : -1, planarBoundsMs = planarBounds.Valid ? planarBounds.LastValue / 1e6 : -1,
                gpuMs = count > 0 && timing[0].gpuFrameTime > 0 ? timing[0].gpuFrameTime : -1,
                cpuMs = count > 0 ? timing[0].cpuFrameTime : -1,
                allocatedBytes = allocations.Valid ? allocations.LastValue : -1, gcMs = gc.Valid ? gc.LastValue / 1e6 : -1,
                gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
                queuedBytes = LimboObservationLog.PendingBytes, logFailures = LimboObservationLog.FailureCount,
                warnings = Volatile.Read(ref warnings), errors = Volatile.Read(ref errors),
                orphanEvents = Volatile.Read(ref animationEvents), invalidAnimatorSpeed = Volatile.Read(ref animationSpeed),
                staticBodyWrites = Volatile.Read(ref staticBody), width = Screen.width, height = Screen.height,
                targetFps = Application.targetFrameRate, timeScale = Time.timeScale }));
            Array.Clear(frameHistogram, 0, frameHistogram.Length);
            if (lastFailures != LimboObservationLog.FailureCount)
            {
                lastFailures = LimboObservationLog.FailureCount;
                Debug.LogError("[LimboEvidence] Observation file writing failed or overflowed. This run's evidence is incomplete; see *.status.json.");
            }
        }
        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= CountMessage;
            main.Dispose(); allocations.Dispose(); gc.Dispose(); spawn.Dispose(); placement.Dispose(); offscreen.Dispose(); planarBounds.Dispose(); log?.Dispose(); log = null;
        }
        [Serializable] private class Row
        {
            public string kind, run;
            public double realtime, elapsed, mainMs, renderMs, gpuMs, cpuMs, gcMs, mainWorkMs, presentWaitMs, spawnMs, placementMs, offscreenMs, planarBoundsMs;
            public ulong timingTimestamp;
            public int[] frameHistogram;
            public float frameMs, timeScale;
            public long allocatedBytes, queuedBytes;
            public int phase, frame, alive, gen0, gen1, gen2, logFailures, warnings, errors, orphanEvents, invalidAnimatorSpeed, staticBodyWrites, width, height, targetFps;
        }
    }
}
