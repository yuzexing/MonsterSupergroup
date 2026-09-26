using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MonsterSupergroup.Builds;
using Newtonsoft.Json;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    /// <summary>One automatically collected process session. Raw writer formats remain unchanged.</summary>
    public sealed class AutomaticCaptureSession : MonoBehaviour
    {
        private static AutomaticCaptureSession instance;
        private static bool initialized;
        private readonly object gate = new();
        private readonly object fileGate = new();
        private int revision, persistedRevision;
        private string root, mode, source, state = "Starting", failure, fallbackReason, buildJson, sessionId;
        private string startedUtc, executable, buildGuid;
        private bool ready, saving, saved, allowIncompleteExit, abandonConfirmation, abandonRequested;
        private volatile bool abandonmentWritten;
        private double abandonmentStarted;
        private long pendingBytes;
        private int processId;
        public static bool Enabled { get { EnsureInitialized(); return instance != null; } }
        public static string Root { get { EnsureInitialized(); return instance?.root ?? throw new IOException("自动采证目录不可用。"); } }
        public static string NetworkDirectory => Path.Combine(Root, "network");
        public static string CombatDirectory => Path.Combine(Root, "combat");
        public static bool AllowIncompleteExit => instance != null && instance.allowIncompleteExit;
        public static bool HasFailure { get { var value = instance; if (value == null) return false; lock (value.gate) return value.failure != null; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionState() { initialized = false; instance = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            if (Application.isEditor || BuildFeatures.CompiledKind == BuildKind.Shipping) return;
            var args = Environment.GetCommandLineArgs();
            // Existing operator captures retain their explicit directories and lifecycle contracts.
            if (args.Any(a => a.StartsWith("--combat-evidence-output=", StringComparison.Ordinal) ||
                              a.StartsWith("--network-diagnostics-output=", StringComparison.Ordinal))) return;
            bool combat = BuildFeatures.EvidenceCompiled && !args.Contains("--no-combat-evidence");
            bool network = BuildFeatures.AutoNetworkDiagnostics;
            if (!combat && !network) return;
            var owner = new GameObject("Automatic capture session");
            DontDestroyOnLoad(owner);
            instance = owner.AddComponent<AutomaticCaptureSession>();
            instance.Initialize(combat ? "Evidence" : "Network");
        }

        private void Initialize(string selectedMode)
        {
            mode = selectedMode; source = mode == "Evidence" ? "combat" : "network";
            sessionId = DateTime.Now.ToString("MMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            startedUtc = DateTime.UtcNow.ToString("o"); buildJson = RuntimeBuildInfo.Current?.ToJson(); buildGuid = Application.buildGUID;
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            { processId = process.Id; executable = process.MainModule?.FileName; }
            try
            {
                string gameDirectory = Path.GetDirectoryName(Application.dataPath);
                string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonsterLogs");
                root = CreateDirectory(Path.Combine(gameDirectory, "Logs"), fallback, sessionId, mode == "Evidence" ? 180 : 60, out fallbackReason);
                Directory.CreateDirectory(Path.Combine(root, source));
                if (!string.IsNullOrEmpty(RuntimeBuildInfo.Error)) throw new InvalidDataException(RuntimeBuildInfo.Error);
                PersistStatus(CaptureStatus());
            }
            catch (Exception error) { failure = error.GetType().Name + ": " + error.Message; state = "Failed"; }
        }

        // Includes space for writer filenames, sidecars, and temporary metadata files.
        public static string CreateDirectory(string primary, string fallback, string id, int requiredSuffix, out string reason)
        {
            reason = null;
            try { return Probe(Path.Combine(primary, id), requiredSuffix); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { reason = error.GetType().Name + ": " + error.Message; }
            return Probe(Path.Combine(fallback, id), requiredSuffix);
        }
        private static string Probe(string path, int suffix)
        {
            path = Path.GetFullPath(path);
            if (path.Length + suffix >= 260) throw new PathTooLongException("日志目录未给状态文件及附件保留足够路径空间。");
            if (Directory.Exists(path)) throw new IOException("采证目录已存在，不能覆盖历史记录。");
            Directory.CreateDirectory(path);
            string probe = Path.Combine(path, ".write-check");
            using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { file.WriteByte(1); file.Flush(true); }
            File.Delete(probe);
            return path;
        }

        public static void ReportReady(string source)
        {
            var session = instance;
            if (session == null || !session.IsSource(source)) return;
            PendingStatus status;
            lock (session.gate)
            {
                if (session.ready || session.failure != null) return;
                session.ready = true; session.state = "Recording"; status = session.CaptureStatus();
            }
            ThreadPool.QueueUserWorkItem(_ => session.PersistStatus(status));
        }
        public static void ReportFailure(string source, string reason)
        {
            var session = instance;
            if (session == null) return;
            PendingStatus status;
            lock (session.gate)
            {
                if (session.failure != null) return;
                session.failure = source + ": " + reason; session.state = "Incomplete"; status = session.CaptureStatus();
            }
            ThreadPool.QueueUserWorkItem(_ => session.PersistStatus(status));
        }
        public static void ReportSaving(string source, long pendingBytes)
        {
            var session = instance;
            if (session == null || !session.IsSource(source)) return;
            lock (session.gate) { session.saving = true; session.pendingBytes = pendingBytes; }
        }
        public static void ReportClosed(string source, bool saved, string reason = null)
        {
            var session = instance;
            if (session == null || !session.IsSource(source)) return;
            PendingStatus status;
            lock (session.gate)
            {
                session.saving = true;
                saved = saved && session.failure == null && !session.abandonRequested;
                session.saved = false;
                if (!saved) session.failure ??= reason ?? "记录未完整保存。";
                session.state = "Finalizing";
                status = session.CaptureStatus(saved);
            }
            // Caller is the writer finalizer. No UI/state lock is held during disk I/O.
            session.PersistStatus(status);
            lock (session.gate)
            {
                session.saving = false; session.saved = saved && session.failure == null && !session.abandonRequested;
                session.state = session.saved ? "Saved" : "Incomplete";
            }
        }
        private bool IsSource(string value) => string.Equals(value, source, StringComparison.OrdinalIgnoreCase);
        private sealed class PendingStatus { public int revision; public string json, text; }
        private void PersistStatus(PendingStatus status)
        {
            if (root == null || status == null) return;
            lock (fileGate)
            {
                if (status.revision <= persistedRevision) return;
                try
                {
                    EvidenceJson.AtomicWrite(Path.Combine(root, "session.json"), status.json);
                    File.WriteAllText(Path.Combine(root, "本次记录说明.txt"), status.text, new UTF8Encoding(true));
                    persistedRevision = status.revision;
                }
                catch (Exception error)
                {
                    lock (gate) { failure ??= "SessionStatusWriteFailed: " + error.Message; saved = false; state = "Incomplete"; }
                }
            }
        }
        private PendingStatus CaptureStatus(bool? finalSaved = null)
        {
            bool snapshotSaved = finalSaved ?? saved;
            string snapshotState = finalSaved.HasValue ? (snapshotSaved ? "Saved" : "Incomplete") : state;
            string json = JsonConvert.SerializeObject(new {
                schemaVersion = 1, sessionId, startedUtc, updatedUtc = DateTime.UtcNow.ToString("o"),
                mode, actualSource = source, state = snapshotState, ready, localSaveCompleted = snapshotSaved,
                integrity = "RequiresOfflineVerification", failure, fallbackReason, processId, executable, buildGuid,
                buildInfo = buildJson, logReplicationEnabled = false,
                records = source, collectionUnit = "ProcessSessionWithRunAndRoundIdentity"
            }, Formatting.Indented);
            string text = "本次记录\r\n采集类型：" + (mode == "Evidence" ? "战斗取证（含网络观测）" : "网络专项") +
                "\r\n开始时间（UTC）：" + startedUtc + "\r\n保存位置：" + root +
                "\r\n状态：" + (snapshotSaved ? "本机保存结束，等待开发机完整性检查。" : failure != null ? "记录不完整：" + failure : ready ? "正在记录；请正常退出并等待保存。" : "正在准备采证。") +
                (fallbackReason == null ? "" : "\r\n游戏目录不可用，已切换用户日志目录。原因：" + fallbackReason) +
                "\r\n提交方式：正常退出后提交整个本次记录文件夹，无需运行导出脚本。\r\n异常退出或没有最终状态时，不代表记录完整。\r\n";
            return new PendingStatus { revision = ++revision, json = json, text = text };
        }

        private void Update()
        {
            if (!abandonRequested || allowIncompleteExit) return;
            if (!abandonmentWritten && Time.realtimeSinceStartupAsDouble - abandonmentStarted < 1) return;
            allowIncompleteExit = true; Application.Quit();
        }
        private void Abandon()
        {
            lock (gate) { if (abandonRequested) return; abandonRequested = true; failure = "用户主动放弃保存，记录可能不完整。"; saved = false; }
            abandonmentStarted = Time.realtimeSinceStartupAsDouble;
            // Independent marker: a stalled final metadata write must not block explicit exit.
            ThreadPool.QueueUserWorkItem(_ => {
                try { if (root != null) File.WriteAllText(Path.Combine(root, "abandoned.json"), "{\"complete\":false,\"reason\":\"UserAborted\",\"overridesSessionStatus\":true}", new UTF8Encoding(false)); }
                catch { /* Bounded grace; storage failure must not prevent an explicit exit. */ }
                finally { abandonmentWritten = true; }
            });
        }

        private void OnGUI()
        {
            string message, detail; bool failed, closing;
            lock (gate)
            {
                failed = failure != null; closing = saving;
                message = failed ? "采证异常：记录不完整" : saving ? $"正在保存记录 · 待处理 {pendingBytes / 1048576d:F1} MiB" :
                    saved ? "记录已保存" : ready ? (mode == "Evidence" ? "战斗采证已就绪" : "网络采证已就绪") : "正在准备采证…";
                detail = failure;
            }
            // Keep combat unobstructed; faults and exit saving remain visible.
            if (!failed && !closing && (Mirror.NetworkClient.active || Mirror.NetworkServer.active)) return;
            var box = new Rect(12, Screen.height - (failed ? 182 : 85), 440, failed ? 170 : 73);
            GUI.Box(box, message);
            if (failed) GUI.Label(new Rect(box.x + 12, box.y + 28, 416, 85), detail);
            float buttons = box.yMax - 35;
            if (root != null && GUI.Button(new Rect(box.x + 12, buttons, 160, 27), "打开日志目录"))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + root + "\"") { UseShellExecute = true }); }
                catch (Exception error) { ReportFailure(source, "无法打开目录：" + error.Message); }
            }
            if ((failed || closing) && mode == "Network")
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                if (abandonRequested) GUI.Label(new Rect(box.x + 184, buttons, 242, 27), "正在尽力保存放弃标记，即将退出…");
                else if (abandonConfirmation)
                {
                    if (GUI.Button(new Rect(box.x + 184, buttons, 115, 27), "继续等待")) abandonConfirmation = false;
                    if (GUI.Button(new Rect(box.x + 305, buttons, 123, 27), "确认放弃并退出")) Abandon();
                }
                else if (GUI.Button(new Rect(box.x + 184, buttons, 242, 27), failed ? "已知记录不完整，退出" : "放弃未保存记录并退出")) abandonConfirmation = true;
            }
        }
    }
}
