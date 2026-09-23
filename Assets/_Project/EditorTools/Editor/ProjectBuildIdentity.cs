using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using MonsterSupergroup.Builds;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.EditorTools
{
    public static class ProjectBuildIdentity
    {
        public static BuildInfo Active { get; private set; }
        public static string ActiveOutput { get; private set; }
        public static string LastInfoPath { get; private set; }
        public static event Action BuildFinished;
        public static void ResetLastInfo() => LastInfoPath = null;

        public static BuildKind ResolveKind(ProjectBuildProfile profile, string requested = null)
        {
            string kind = requested ?? profile.kind;
            if (string.IsNullOrEmpty(kind)) return profile.id == "player-release" ? BuildKind.Shipping : profile.development ? BuildKind.Dev : BuildKind.Test;
            if (!Enum.TryParse(kind, true, out BuildKind parsed) || !Enum.IsDefined(typeof(BuildKind), parsed)) throw new ArgumentException("未知构建类型: " + kind);
            return parsed;
        }
        public static void ValidateOptions(BuildKind kind, bool development, bool tests, string[] defines)
        {
            if ((defines ?? Array.Empty<string>()).Any(d => d.StartsWith("MONSTER_BUILD_", StringComparison.Ordinal)))
                throw new BuildFailedException("构建类型编译符号由构建工具生成，请移除项目或配置中手工填写的 MONSTER_BUILD_*。");
            if (kind != BuildKind.Shipping) return;
            if (development) throw new BuildFailedException("Shipping 必须关闭 Development Build。");
            if (tests) throw new BuildFailedException("Shipping 不允许包含测试程序集。");
            string[] blocked = (defines ?? Array.Empty<string>()).Where(d => d.Contains("VALIDATION") || d == "UNITY_INCLUDE_TESTS" || d == "MONSTER_COMBAT_EVIDENCE" || d.StartsWith("MONSTER_BUILD_", StringComparison.Ordinal)).ToArray();
            if (blocked.Length > 0) throw new BuildFailedException("Shipping 不允许验证或冲突编译符号: " + string.Join(", ", blocked));
        }
        public static string NewId() => DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        public static BuildInfo Capture(ProjectBuildProfile profile, BuildKind kind, bool development)
        {
            string version = GameVersion.Parse(NativeBuildProfileSettings.GlobalVersion).ToString();
            string commit = null, status = null, gitError = null;
            try
            {
                commit = Git("rev-parse --verify HEAD").Trim();
                if (commit.Length != 40 || !commit.All(Uri.IsHexDigit)) throw new InvalidDataException("Git HEAD 无效。");
                status = Git("status --porcelain=v1 --untracked-files=all");
            }
            catch (Exception exception) { gitError = exception.Message; }
            bool valid = gitError == null, dirty = !valid || !string.IsNullOrWhiteSpace(status);
            if (kind == BuildKind.Shipping && (!valid || dirty))
                throw new BuildFailedException(!valid ? "Shipping 需要有效 Git 信息：" + gitError : "Shipping 要求干净工作区，请先提交以下更改：\n" + status);
            return new BuildInfo(version, kind, NewId(), valid ? commit : null, valid, dirty,
                DateTime.UtcNow.ToString("O"), UnityEngine.Application.unityVersion, "StandaloneWindows64", "x86_64", development, profile.id);
        }
        private static string Git(string arguments)
        {
            using var process = Process.Start(new ProcessStartInfo("git", "-c core.quotepath=false " + arguments) {
                WorkingDirectory = ProjectToolCatalog.ProjectRoot, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true });
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("Git 检查超时。"); }
            if (process.ExitCode != 0) throw new InvalidOperationException(stderr.GetAwaiter().GetResult());
            return stdout.GetAwaiter().GetResult();
        }
        public static void Begin(BuildInfo info, string output)
        {
            if (Active != null) throw new InvalidOperationException("已有构建上下文。");
            Active = info; ActiveOutput = Path.GetFullPath(output); LastInfoPath = null;
            // A failed overwrite must never leave a previous successful identity behind.
            string old = InfoPath(output); if (File.Exists(old)) File.Delete(old);
            string marker = Path.Combine(Path.GetDirectoryName(ActiveOutput), "build-complete.json"); if (File.Exists(marker)) File.Delete(marker);
        }
        public static string InfoPath(string output) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)), Path.GetFileNameWithoutExtension(output) + "_Data", "StreamingAssets", "BuildInfo.json");
        public static void Complete(BuildReport report)
        {
            ValidateReport(report);
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("构建未成功，不能生成交付标识。");
            try
            {
            ValidateShippingSource(Active);
            string path = InfoPath(ActiveOutput); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Active.ToJson(), new System.Text.UTF8Encoding(false));
            string error = BuildInfo.FromJson(File.ReadAllText(path)).Validate(NativeBuildProfileSettings.GlobalVersion,
                (report.summary.options & BuildOptions.Development) != 0, Enum.Parse<BuildKind>(Active.Kind, true));
            if (error != null) { File.Delete(path); throw new BuildFailedException(error); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(ActiveOutput), "build-complete.json"), Active.ToJson(), new System.Text.UTF8Encoding(false));
            LastInfoPath = path;
            }
            catch { InvalidateOutput(); throw; }
        }

        public static void InvalidateOutput(string output = null)
        {
            LastInfoPath = null;
            output ??= ActiveOutput;
            if (output == null) return;
            string path = InfoPath(output);
            if (File.Exists(path)) File.Delete(path);
            string marker = Path.Combine(Path.GetDirectoryName(output), "build-complete.json");
            if (File.Exists(marker)) File.Delete(marker);
        }
        public static void ValidateShippingSource(BuildInfo info)
        {
            if (info.Kind != "shipping") return;
            if (Git("rev-parse --verify HEAD").Trim() != info.GitCommit ||
                !string.IsNullOrWhiteSpace(Git("status --porcelain=v1 --untracked-files=all")))
                throw new BuildFailedException("Shipping 构建期间源码、资源或配置发生变化；本次包不发布，请检查并重新构建。");
        }
        public static void ValidateReport(BuildReport report)
        {
            if (Active == null || !string.Equals(Path.GetFullPath(report.summary.outputPath), ActiveOutput, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("请通过 MonsterSupergroup → 构建与验收 → 构建配置，或 build.player 构建。禁止复用过期构建信息。");
            if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
                (report.summary.options & BuildOptions.BuildScriptsOnly) != 0 ||
                ((report.summary.options & BuildOptions.Development) != 0) != Active.Development)
                throw new BuildFailedException("实际构建参数与已确认配置不一致。");
            var plan = ProjectBuildService.ActivePlan;
            if (plan == null) throw new BuildFailedException("缺少原生 Profile 构建计划，禁止绕过统一服务。");
            NativeBuildEntry.ValidateOptions(report.summary.options, plan, false);
        }
        public static void SetLastInfoPath(string output) => LastInfoPath = InfoPath(output);
        public static void Cleanup()
        {
            var handlers = BuildFinished;
            BuildFinished = null;
            var errors = new System.Collections.Generic.List<Exception>();
            if (handlers != null)
                foreach (Action handler in handlers.GetInvocationList())
                    try { handler(); } catch (Exception e) { errors.Add(e); }
            if (errors.Count != 0) throw new AggregateException("构建清理失败。", errors);
        }
        public static void ResetContext() { Active = null; ActiveOutput = null; }
        public static void End()
        {
            try { Cleanup(); }
            catch { InvalidateOutput(); throw; }
            finally { ResetContext(); }
        }
    }
    public sealed class ProjectBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;
        public void OnPreprocessBuild(BuildReport report) => ProjectBuildIdentity.ValidateReport(report);
    }
}
