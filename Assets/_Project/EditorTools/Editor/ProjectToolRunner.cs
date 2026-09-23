using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MonsterSupergroup.EditorTools
{
    [Serializable] public sealed class ProjectToolRequest
    {
        public string profile, assetPath, source, output, resultPath, buildKind, development, network, distribution, diagnostics;
        public bool apply, scriptsOnly, uniqueOutput;
        public string buildProfile;
        public bool cleanBuildCache, runAfterBuild;
    }
    [Serializable] public sealed class ProjectToolResult
    {
        public string id, startedUtc, error, report, log;
        public bool success, pending;
        public double durationSeconds;
        public string[] artifacts = Array.Empty<string>();
    }
    [Serializable] internal sealed class AssetDigest { public string path, hash; }
    [Serializable] internal sealed class AssetDigests { public AssetDigest[] files; }

    public static class ProjectToolRunner
    {
        private const string PendingKey = "Monster.ProjectTool.Pending";
        public static bool Busy { get; private set; }
        public static bool IsPending => SessionState.GetString(PendingKey, "").Length > 0;
        public static ProjectToolResult LastResult { get; private set; }
        public static string Availability(ProjectToolDescriptor tool)
        {
            if (Busy || IsPending || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer) return "工具或资源导入正在运行";
            if (tool.interactive && Application.isBatchMode) return "需要在已打开的 Editor 中操作";
            if (tool.graphics && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return "需要图形设备，请勿使用 -nographics";
            if (tool.asynchronous && !Application.isBatchMode && !EditorApplication.isPlaying)
                return "请先在空场景进入 Play Mode；批处理入口会自动进入并等待完成";
            if (tool.condition == "play-host")
            {
                var server = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Mirror.NetworkServer")).FirstOrDefault(t => t != null);
                var client = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Mirror.NetworkClient")).FirstOrDefault(t => t != null);
                if (!EditorApplication.isPlaying || !(bool)(server?.GetProperty("active")?.GetValue(null) ?? false) ||
                    !(bool)(client?.GetProperty("active")?.GetValue(null) ?? false)) return "需要 Play Mode 中的 Host";
            }
            else if (tool.condition == "play" && !EditorApplication.isPlaying) return "需要 Play Mode";
            else if (tool.condition == "edit" && EditorApplication.isPlayingOrWillChangePlaymode) return "请先停止 Play Mode";
            if (tool.writesAssets && Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                .Any(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)) return "请先保存或关闭未保存场景";
            return "";
        }

        public static ProjectToolResult Run(string id, ProjectToolRequest request = null)
        {
            request ??= new ProjectToolRequest();
            string folder = Path.Combine(ProjectToolCatalog.ProjectRoot, "Logs/ProjectTools", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + id);
            Directory.CreateDirectory(folder);
            var result = new ProjectToolResult { id = id, startedUtc = DateTime.UtcNow.ToString("O"),
                report = string.IsNullOrEmpty(request.resultPath) ? Path.Combine(folder, "result.json") : Path.GetFullPath(request.resultPath), log = Path.Combine(folder, "tool.log") };
            var watch = Stopwatch.StartNew();
            string previousSource = Environment.GetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT");
            AssetDigests before = null;
            void Log(string message, string stack, LogType type) => File.AppendAllText(result.log, $"[{type}] {message}\n{stack}\n");
            Application.logMessageReceived += Log;
            try
            {
                var tool = ProjectToolCatalog.Find(id);
                string unavailable = Availability(tool);
                if (unavailable.Length != 0) throw new InvalidOperationException(unavailable);
                if (tool.writesAssets && !request.apply) throw new InvalidOperationException("该维护操作会修改资产；请明确传入 -Apply。影响: " + tool.impact);
                if (!string.IsNullOrWhiteSpace(request.source)) Environment.SetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT", Path.GetFullPath(request.source));
                if (tool.externalSource) ProjectToolPaths.HellMaiden();
                if (!tool.writesAssets && !tool.interactive && tool.id != "build.player" && string.IsNullOrEmpty(tool.profile)) before = CaptureAssets();
                Busy = true;
                if (tool.asynchronous && Application.isBatchMode)
                {
                    File.WriteAllText(Path.Combine(folder, "before.json"), JsonUtility.ToJson(before));
                    result.artifacts = string.IsNullOrEmpty(tool.output) ? Array.Empty<string>() : new[] { tool.output };
                    result.pending = true;
                    SessionState.SetString(PendingKey, JsonUtility.ToJson(result));
                    SessionState.SetString(PendingKey + ".folder", folder);
                }
                Execute(tool, request, result);
                if (!result.pending) result.success = true;
            }
            catch (Exception error)
            {
                if (error is TargetInvocationException wrapper && wrapper.InnerException != null) error = wrapper.InnerException;
                result.error = error.ToString(); result.pending = false;
                SessionState.EraseString(PendingKey); Debug.LogException(error);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT", previousSource);
                if (!result.pending && before != null) CheckAssets(before, result);
                result.durationSeconds = watch.Elapsed.TotalSeconds;
                Application.logMessageReceived -= Log; Busy = false;
                Save(result);
            }
            return result;
        }

        private static void Execute(ProjectToolDescriptor tool, ProjectToolRequest request, ProjectToolResult result)
        {
            if (!string.IsNullOrEmpty(tool.script)) throw new InvalidOperationException("进程验收请使用 Tools/Invoke-ProjectTool.ps1；可在工具中心复制命令。");
            if (tool.id == "build.player" || !string.IsNullOrEmpty(tool.profile))
            {
                if (request.profile != null || tool.profile != null || request.buildKind != null || request.development != null || request.network != null ||
                    request.distribution != null || request.diagnostics != null || request.scriptsOnly || request.uniqueOutput)
                    throw new InvalidOperationException(ProjectBuildResolver.MigrationMessage);
                result.artifacts = new[] { ProjectBuildService.Build(ProjectBuildResolver.Load(request.buildProfile), new BuildExecutionRequest {
                    output = request.output, cleanBuildCache = request.cleanBuildCache, runAfterBuild = request.runAfterBuild
                }), ProjectBuildIdentity.LastInfoPath };
                return;
            }
            if (tool.id == "preview.attack")
            {
                string profile = request.profile ?? "projectile";
                if (!new[] { "projectile", "beam", "circling", "dash", "melee", "summon" }.Contains(profile))
                    throw new ArgumentException("预览配置无效；大招请单独使用 preview.ultimate。");
                InvokeReadOnly("preview." + profile); return;
            }
            if (tool.id == "validate.all" || tool.id == "validate.waves-and-enemies")
            {
                var ids = tool.id == "validate.all" ? new[] { "validate.gas", "validate.localization", "validate.waves", "validate.enemies" } : new[] { "validate.waves", "validate.enemies" };
                foreach (string id in ids) InvokeReadOnly(id);
                return;
            }
            if (!string.IsNullOrEmpty(tool.asset))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(tool.asset) ?? throw new FileNotFoundException("资源缺失，请显式使用维护区创建: " + tool.asset);
                Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
                if (tool.id == "open.waves" || tool.id == "open.localization") AssetDatabase.OpenAsset(asset);
                result.artifacts = new[] { tool.asset }; return;
            }
            if (tool.id == "validate.tools") { ProjectToolCatalog.Validate(); return; }
            var method = ProjectToolCatalog.Resolve(tool.method);
            if (method.GetParameters().Length == 1)
            {
                string path = request.assetPath;
                if (string.IsNullOrEmpty(path) && !Application.isBatchMode) path = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请指定 -AssetPath，不依赖批处理中的 Editor Selection。");
                method.Invoke(null, new object[] { path });
            }
            else method.Invoke(null, null);
            if (!string.IsNullOrEmpty(tool.output)) result.artifacts = new[] { tool.output };
        }

        public static void InvokeReadOnly(string id)
        {
            var tool = ProjectToolCatalog.Find(id);
            if (tool.writesAssets || !string.IsNullOrEmpty(tool.script)) throw new InvalidOperationException("只读调用不允许修改资产: " + id);
            Execute(tool, new ProjectToolRequest(), new ProjectToolResult());
        }

        public static void CompletePending(Exception error)
        {
            string json = SessionState.GetString(PendingKey, "");
            if (json.Length == 0) throw new InvalidOperationException("No pending tool operation.");
            var result = JsonUtility.FromJson<ProjectToolResult>(json);
            string folder = SessionState.GetString(PendingKey + ".folder", "");
            result.pending = false; result.success = error == null; result.error = error?.ToString();
            result.durationSeconds = (DateTime.UtcNow - DateTime.Parse(result.startedUtc).ToUniversalTime()).TotalSeconds;
            var before = JsonUtility.FromJson<AssetDigests>(File.ReadAllText(Path.Combine(folder, "before.json")));
            CheckAssets(before, result); Save(result); SessionState.EraseString(PendingKey);
            if (Application.isBatchMode) EditorApplication.Exit(result.success ? 0 : 1);
        }

        private static void Save(ProjectToolResult result)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(result.report));
            File.WriteAllText(result.report, JsonUtility.ToJson(result, true));
            File.WriteAllText(Path.Combine(ProjectToolCatalog.ProjectRoot, "Logs/ProjectTools/last-result.json"), JsonUtility.ToJson(result, true));
            File.WriteAllText(Path.Combine(ProjectToolCatalog.ProjectRoot, "Logs/ProjectTools/" + result.id + ".json"), JsonUtility.ToJson(result, true));
            LastResult = result;
        }
        internal static AssetDigests CaptureAssets()
        {
            string[] roots = { "Assets/_Project/Content", "Assets/_Project/Localization", "Assets/_Project/Scenes", "Assets/_Project/UI", "Assets/Resources", "Assets/MonoBehaviour", "Assets/Settings", "Assets/TextMesh Pro" };
            using var hash = SHA256.Create();
            return new AssetDigests { files = roots.Where(Directory.Exists).SelectMany(r => Directory.GetFiles(r, "*", SearchOption.AllDirectories))
                .OrderBy(p => p, StringComparer.Ordinal).Select(p => new AssetDigest { path = p, hash = Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(p))) }).ToArray() };
        }
        private static void CheckAssets(AssetDigests before, ProjectToolResult result)
        {
            if (before == null) return;
            try
            {
                AssertAssetsUnchanged(before);
            }
            catch (Exception error) { result.success = false; result.error += "\n" + error; }
        }

        internal static void AssertAssetsUnchanged(AssetDigests before)
        {
            var original = before.files.ToDictionary(f => f.path, f => f.hash);
            var current = CaptureAssets().files.ToDictionary(f => f.path, f => f.hash);
            string[] changed = original.Keys.Union(current.Keys).Where(p => !original.TryGetValue(p, out var a) || !current.TryGetValue(p, out var b) || a != b).ToArray();
            if (changed.Length != 0) throw new InvalidOperationException("只读构建／校验改动正式资源: " + string.Join("\n", changed));
        }

        public static void Batch()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Value(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var result = Run(Value("-toolId") ?? "validate.tools", new ProjectToolRequest {
                profile = Value("-toolProfile"), assetPath = Value("-toolAsset"), source = Value("-toolSource"), output = Value("-toolOutput"),
                resultPath = Value("-toolResult"), apply = args.Contains("-toolApply"), scriptsOnly = args.Contains("-toolScriptsOnly"),
                buildKind = Value("-toolBuildKind"), development = Value("-toolDevelopment"), uniqueOutput = args.Contains("-toolUniqueOutput"),
                network = Value("-toolNetwork"), distribution = Value("-toolDistribution"), diagnostics = Value("-toolDiagnostics")
                , buildProfile = Value("-activeBuildProfile"), cleanBuildCache = args.Contains("-toolCleanBuildCache"), runAfterBuild = args.Contains("-toolRunAfterBuild")
            });
            if (!result.pending) EditorApplication.Exit(result.success ? 0 : 1);
        }

        public static void LegacyBatch(string id)
        {
            Debug.LogWarning("[ProjectTools] 旧批处理入口兼容一版；请改用 Invoke-ProjectTool.ps1 -ToolId " + id);
            var result = Run(id, new ProjectToolRequest { apply = Environment.GetCommandLineArgs().Contains("-toolApply") || Environment.GetCommandLineArgs().Contains("-Apply") });
            if (Application.isBatchMode) EditorApplication.Exit(result.success ? 0 : 1);
            else if (!result.success) throw new InvalidOperationException(result.error);
        }

        public static void CheckLegacyMaintenance(string id, string entry)
        {
            if (Busy) return;
            var args = Environment.GetCommandLineArgs();
            int execute = Array.IndexOf(args, "-executeMethod");
            if (execute < 0 || execute + 1 == args.Length || args[execute + 1] != entry) return;
            Debug.LogWarning("[ProjectTools] 旧维护入口兼容一版；请改用 " + id + " -Apply。");
            if (!args.Contains("-Apply") && !args.Contains("-toolApply"))
                throw new InvalidOperationException("旧维护批处理也需要明确提供 -Apply: " + id);
        }
    }

    public static class ProjectToolPaths
    {
        public static string HellMaiden()
        {
            string path = Environment.GetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(Path.Combine(path, "Assets")))
                throw new DirectoryNotFoundException("请用 -Source 或 HELLMAIDEN_SOURCE_PROJECT 指定参考工程根目录，其中必须包含 Assets。");
            return Path.GetFullPath(path).Replace('\\', '/');
        }
    }
}
