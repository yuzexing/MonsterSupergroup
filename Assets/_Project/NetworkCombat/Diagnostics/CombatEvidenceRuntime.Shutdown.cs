using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using MonsterSupergroup.GAS;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public sealed partial class CombatEvidenceRuntime
    {
        private const double ShutdownDrainTimeoutSeconds = 30;
        private const int FallbackCloseMilliseconds = 2000;
        private bool quitRequested, quitAllowed, quitReissued, shutdownTimedOut;
        private double shutdownStartedSeconds;
        private string reportedShutdownStatus;
        private Thread diagnosticFinalizer, abandonmentWriter;
        private volatile DiagnosticShutdownResult diagnosticResult;
        private volatile string diagnosticPhase = "写入";
        private volatile bool abandonmentWritten;
        private int shutdownAbandoned;
        private bool abandonConfirmation, shutdownStalled;
        private double shutdownProgressSeconds, abandonmentStartedSeconds;
        private long shutdownProgressPending = -1, shutdownProgressTicks = -1;
        private const double AbandonmentGraceSeconds = 1;
        private bool DiagnosticShutdown => store != null && store.Profile == EvidenceProfile.Diagnostic;
        private static double ShutdownClockSeconds => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

        private bool WantsToQuit()
        {
            if (quitAllowed) return true;
            quitRequested = true;
            BeginShutdown();
            if (DiagnosticShutdown) ShowDiagnosticShutdownCursor();
            return false;
        }

        private void BeginShutdown()
        {
            if (shuttingDown) return;
            shutdownProgressSeconds = shutdownStartedSeconds = ShutdownClockSeconds;
            FlushExceptions(); CombatEvidence.Event("Process", "process.stop", "Stopped", null);
            shuttingDown = true;
            Application.logMessageReceivedThreaded -= CaptureException;
            if (ReferenceEquals(CombatEvidence.Sink, this)) CombatEvidence.Sink = null;
            try
            {
                try { replicator?.Dispose(); }
                finally { transport?.Dispose(); }
            }
            catch (Exception failure) { Debug.LogWarning("[CombatEvidence] Shutdown cleanup: " + failure.GetType().Name); }
            finally
            {
                try
                {
                    if (runtimeBudgetHeld) { store.Memory.Release(RuntimeCacheBytes); runtimeBudgetHeld = false; }
                    store?.QueueObservation?.MarkPhase(EvidenceQueuePhase.Close, EvidenceQueueObservation.Now);
                }
                finally { store?.RequestClose(); }
            }
        }

        private void UpdateShutdown()
        {
            if (!PollShutdown(ShutdownClockSeconds) || !quitRequested || quitReissued) return;
            quitReissued = true;
            // Unity does not expose the original Quit(int) argument in wantsToQuit. Preserve the known process code.
            Application.Quit(Environment.ExitCode);
        }

        private bool PollShutdown(double nowSeconds)
        {
            if (!shuttingDown) return false;
            if (quitAllowed) return true;
            if (DiagnosticShutdown) return PollDiagnosticShutdown(nowSeconds);
            bool joined = store == null || store.WaitForClose(0);
            if (!joined && nowSeconds - shutdownStartedSeconds < ShutdownDrainTimeoutSeconds) return false;
            shutdownTimedOut = !joined;
            FinishShutdown(joined);
            quitAllowed = true;
            return true;
        }

        private void OnGUI()
        {
            if (!quitRequested || quitAllowed) return;
            if (!DiagnosticShutdown)
            {
                GUI.Box(new Rect((Screen.width - 340) * .5f, (Screen.height - 60) * .5f, 340, 60),
                    "正在保存本次记录，请稍候…");
                return;
            }
            // Gameplay and menu code can hide/lock the pointer after Update. Claim it at the
            // actual dialog presentation step so acknowledgement and abandonment remain usable.
            ShowDiagnosticShutdownCursor();
            var box = new Rect((Screen.width - 500) * .5f, (Screen.height - 220) * .5f, 500, 220);
            GUI.Box(box, "正在保存本次记录");
            var result = diagnosticResult;
            string message = result != null && !result.complete ? "记录不完整。详情已尽力保存到诊断目录。" :
                $"待处理缓存：{Math.Max(0, store.ReadShutdownProgress().pendingBytes) / 1048576d:F1} MiB\n" +
                $"已等待 {Math.Max(0, ShutdownClockSeconds - shutdownStartedSeconds):F0} 秒 · {diagnosticPhase}";
            if (shutdownStalled && result == null) message += "\n已有 30 秒未见进展，仍在等待保存。";
            GUI.Label(new Rect(box.x + 20, box.y + 35, 460, 85), message);
            if (result != null && !result.complete)
            {
                if (GUI.Button(new Rect(box.x + 100, box.y + 150, 300, 35), "已知记录不完整，退出")) AcknowledgeIncompleteShutdown();
            }
            else if (Volatile.Read(ref shutdownAbandoned) != 0)
                GUI.Label(new Rect(box.x + 20, box.y + 145, 460, 50), "正在尽力保存主动放弃标记，即将退出…");
            else if (abandonConfirmation)
            {
                GUI.Label(new Rect(box.x + 20, box.y + 120, 460, 30), "确认退出？未保存的记录将无法恢复。" );
                if (GUI.Button(new Rect(box.x + 30, box.y + 165, 210, 30), "继续等待")) abandonConfirmation = false;
                if (GUI.Button(new Rect(box.x + 260, box.y + 165, 210, 30), "确认放弃并退出")) ConfirmAbandonShutdown(ShutdownClockSeconds);
            }
            else if (GUI.Button(new Rect(box.x + 100, box.y + 155, 300, 35), "放弃未保存记录并退出")) abandonConfirmation = true;
        }

        private static void ShowDiagnosticShutdownCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnApplicationQuit() => Shutdown();
        private void OnDestroy() => Shutdown();

        // Forced destruction and platforms that do not support wantsToQuit retain a bounded fallback.
        // Repeated callbacks may finish a late join; they never enqueue process.stop twice.
        private void Shutdown()
        {
            Application.wantsToQuit -= WantsToQuit;
            BeginShutdown();
            if (DiagnosticShutdown)
            {
                // Explicit abandonment and platform-forced destruction must never run file I/O or
                // a blocking join on Unity's main thread. The process can end before the marker lands.
                if (!quitAllowed && Volatile.Read(ref shutdownAbandoned) == 0)
                    StartAbandonmentMarker("ForcedDestruction", ShutdownClockSeconds);
                if (Instance == this) Instance = null;
                quitAllowed = true;
                return;
            }
            bool joined = store == null || store.WaitForClose(quitAllowed ? 0 : FallbackCloseMilliseconds);
            FinishShutdown(joined);
            quitAllowed = true;
        }

        private sealed class DiagnosticShutdownResult
        {
            public bool complete;
            public string status, observationStatus, error;
            public EvidenceShutdownAssessment assessment;
        }

        private bool PollDiagnosticShutdown(double nowSeconds)
        {
            if (Volatile.Read(ref shutdownAbandoned) != 0)
            {
                if (!abandonmentWritten && nowSeconds - abandonmentStartedSeconds < AbandonmentGraceSeconds) return false;
                quitAllowed = true; return true;
            }
            var result = diagnosticResult;
            if (result != null)
            {
                if (!result.complete) return false;
                quitAllowed = true;
                if (Instance == this) Instance = null;
                return true;
            }
            var progress = store.ReadShutdownProgress();
            if (progress.pendingBytes != shutdownProgressPending || progress.writeTicks != shutdownProgressTicks)
            {
                shutdownProgressPending = progress.pendingBytes; shutdownProgressTicks = progress.writeTicks;
                shutdownProgressSeconds = nowSeconds;
            }
            shutdownStalled = nowSeconds - shutdownProgressSeconds >= ShutdownDrainTimeoutSeconds;
            if (!progress.writerJoined)
            {
                diagnosticPhase = progress.pendingBytes == 0 ? "刷新" : "写入";
                return false;
            }
            if (diagnosticFinalizer == null)
            {
                diagnosticPhase = "导出"; shutdownProgressSeconds = nowSeconds; shutdownStalled = false;
                diagnosticFinalizer = new Thread(FinalizeDiagnosticShutdown) { IsBackground = true, Name = "Combat evidence finalization" };
                diagnosticFinalizer.Start();
            }
            return false;
        }

        private void FinalizeDiagnosticShutdown()
        {
            var result = new DiagnosticShutdownResult();
            try
            {
                result.assessment = store.AssessShutdownCompleteness();
                result.observationStatus = ExportWriterObservationCore(store, capture, true, false, result.assessment);
                bool observationSaved = result.observationStatus == "Exported" || result.observationStatus == "Disabled" ||
                    (result.observationStatus == "StatusAlreadyExists" && File.Exists(Path.Combine(store.Root, "writer-observation-" + capture + ".json")));
                result.complete = result.assessment.complete && observationSaved && Volatile.Read(ref shutdownAbandoned) == 0;
            }
            catch (Exception failure)
            {
                result.complete = false; result.error = failure.GetType().Name + ":" + failure.Message;
            }
            bool abandoned = Volatile.Read(ref shutdownAbandoned) != 0;
            if (abandoned) result.complete = false;
            result.status = abandoned ? "UserAborted" : result.complete ? "Complete" : "Incomplete";
            try
            {
                // An independently durable abort marker takes precedence if the user aborts during this write.
                // Even a failed observation/status read must publish its reason when this separate path is writable.
                EvidenceJson.AtomicWrite(Path.Combine(store.Root, "shutdown-status-" + capture + ".json"), JsonConvert.SerializeObject(new {
                    schemaVersion = 1, captureId = capture, profile = "diagnostic", result.status, result.complete,
                    result.observationStatus, result.assessment, result.error, abortedMarkerOverrides = "shutdown-aborted-" + capture + ".json" }));
            }
            catch (Exception failure)
            {
                result.complete = false; result.status = abandoned ? "UserAborted" : "Incomplete";
                result.error = (result.error == null ? "" : result.error + ";") + "StatusWriteFailed:" + failure.GetType().Name + ":" + failure.Message;
            }
            diagnosticResult = result;
        }

        private void AcknowledgeIncompleteShutdown()
        {
            if (diagnosticResult == null || diagnosticResult.complete) return;
            quitAllowed = true;
            if (Instance == this) Instance = null;
        }

        private void ConfirmAbandonShutdown(double nowSeconds) => StartAbandonmentMarker("UserAborted", nowSeconds);

        private void StartAbandonmentMarker(string reason, double nowSeconds)
        {
            if (Interlocked.CompareExchange(ref shutdownAbandoned, 1, 0) != 0) return;
            abandonmentStartedSeconds = nowSeconds;
            var progress = store.ReadShutdownProgress();
            abandonmentWriter = new Thread(() => {
                try
                {
                    EvidenceJson.AtomicWrite(Path.Combine(store.Root, "shutdown-aborted-" + capture + ".json"), JsonConvert.SerializeObject(new {
                        schemaVersion = 1, captureId = capture, profile = "diagnostic", status = reason, complete = false,
                        pendingBytes = progress.pendingBytes, writerJoined = progress.writerJoined,
                        elapsedSeconds = Math.Max(0, nowSeconds - shutdownStartedSeconds),
                        overridesOtherShutdownStatus = true, evidenceCompleteness = "Incomplete" }));
                }
                catch { /* The one-second grace is bounded even when the output disk cannot write a marker. */ }
                finally { abandonmentWritten = true; }
            }) { IsBackground = true, Name = "Combat evidence abandonment" };
            abandonmentWriter.Start();
        }

        private void FinishShutdown(bool joined)
        {
            string status;
            try { status = ExportWriterObservationCore(store, capture, joined, shutdownTimedOut); }
            catch (Exception failure) { status = "ExportFailed:" + failure.GetType().Name; }
            if (status != reportedShutdownStatus)
            {
                if (status == "Exported") Debug.Log("[CombatEvidence] Writer observation exported; evidence completeness remains unverified.");
                else if (status != "Disabled" && status != "StatusAlreadyExists") Debug.LogWarning("[CombatEvidence] Writer observation: " + status);
                reportedShutdownStatus = status;
            }
            if (Instance == this) Instance = null;
        }

        private sealed class WriterExportState { public string completedPath; }
        private static readonly ConditionalWeakTable<CombatEvidenceStore, WriterExportState> writerExports = new();

        // Preserve the existing bridge used by tools/tests. All lifecycle callers use the same per-store export gate.
        private static string ExportWriterObservation(CombatEvidenceStore target, string captureId, bool consumerJoined)
            => ExportWriterObservationCore(target, captureId, consumerJoined, false);

        private static string ExportWriterObservationCore(CombatEvidenceStore target, string captureId, bool consumerJoined, bool timedOut,
            EvidenceShutdownAssessment assessment = null)
        {
            if (target == null || !target.ObservationRequested) return "Disabled";
            var attempt = writerExports.GetValue(target, _ => new WriterExportState());
            lock (attempt)
            {
                string path = Path.Combine(target.Root, "writer-observation-" + captureId + ".json");
                string statusPath = Path.Combine(target.Root, "writer-observation-status-" + captureId + ".json");
                string firstFailurePath = statusPath + ".first-failure.json";
                bool hasPrevious = File.Exists(statusPath);
                JObject previous = null;
                if (hasPrevious)
                {
                    try { previous = JObject.Parse(File.ReadAllText(statusPath)); }
                    catch (JsonException) { /* Preserve malformed prior status bytes if this attempt succeeds. */ }
                    if (previous?["exported"]?.Type == JTokenType.Boolean && previous["exported"].Value<bool>() &&
                        previous["status"]?.Type == JTokenType.String && previous["status"].Value<string>() == "Exported") return "StatusAlreadyExists";
                }
                bool joined = consumerJoined && target.WaitForClose(0), exported = false;
                string status, error = null, partialPath = previous?["partialObservationPath"]?.Type == JTokenType.String
                    ? previous["partialObservationPath"].Value<string>() : null, partialError = null;
                string partial = Path.Combine(target.Root, "writer-observation-partial-" + captureId + ".json");
                if (File.Exists(partial)) partialPath = Path.GetFileName(partial);
                if (!joined)
                {
                    status = "WriterNotJoined";
                    if (!File.Exists(partial))
                    {
                        try { if (target.ExportPartialQueueObservation(partial)) partialPath = Path.GetFileName(partial); }
                        catch (Exception failure) { partialError = failure.GetType().Name + ":" + failure.Message; }
                    }
                }
                else if (target.QueueObservation == null) { status = "Unavailable"; error = target.ObservationUnavailableReason; }
                else
                {
                    try
                    {
                        exported = attempt.completedPath == path || target.ExportQueueObservation(path);
                        if (exported) attempt.completedPath = path;
                        status = exported ? "Exported" : "ExportFailed";
                    }
                    catch (Exception failure) { status = "ExportFailed"; error = failure.GetType().Name + ":" + failure.Message; }
                }
                // A failure is immutable. Only a completed numeric export may replace the canonical status,
                // after preserving the exact previous bytes in a separate, never-overwritten first-failure file.
                if (hasPrevious && !exported) return status;
                try
                {
                    Directory.CreateDirectory(target.Root);
                    if (hasPrevious && !File.Exists(firstFailurePath)) File.Copy(statusPath, firstFailurePath, false);
                    if (hasPrevious)
                    {
                        using var preserved = new FileStream(firstFailurePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                        preserved.Flush(true);
                    }
                    string json = JsonConvert.SerializeObject(new {
                        schemaVersion = 1, captureId, requested = true, writerJoined = joined, exported, status, error,
                        observationPath = exported ? Path.GetFileName(path) : null,
                        partialObservationPath = partialPath, partialObservationError = partialError,
                        firstFailurePath = File.Exists(firstFailurePath) ? Path.GetFileName(firstFailurePath) : null,
                        shutdownTimedOut = timedOut, shutdownTimeoutSeconds = timedOut ? (double?)ShutdownDrainTimeoutSeconds : null,
                        evidenceCompleteness = assessment == null ? "NotEvaluated" : assessment.complete ? "Complete" : "Incomplete",
                        evidenceAssessment = assessment, mainThreadTimingEnabled = false, cpuProbeInvokedByRuntime = false });
                    if (hasPrevious) EvidenceJson.AtomicWrite(statusPath, json);
                    else
                    {
                        using var file = new FileStream(statusPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                        using var stream = new StreamWriter(file);
                        stream.Write(json);
                        stream.Flush(); file.Flush(true);
                    }
                }
                catch (Exception failure) { return "StatusWriteFailed:" + failure.GetType().Name; }
                return status;
            }
        }
    }
}
