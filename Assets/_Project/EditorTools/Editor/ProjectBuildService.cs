using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using MonsterSupergroup.Builds;

namespace MonsterSupergroup.EditorTools
{
    public static class ProjectBuildService
    {
        public static string ActiveProfile { get; private set; }

        public static void ValidateProfile(ProjectBuildProfile profile)
        {
            if (profile.scenes == null || profile.scenes.Length == 0) throw new InvalidDataException("构建未配置场景: " + profile.id);
            foreach (string scene in profile.scenes)
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                    throw new FileNotFoundException("场景缺失，请先显式运行对应维护工具: " + scene);
            if (string.IsNullOrWhiteSpace(profile.output) || !profile.output.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Windows 构建输出无效: " + profile.id);
        }

        public static string Build(string profileId, string output = null, bool scriptsOnly = false,
            string buildKind = null, string development = null, bool uniqueOutput = false,
            string network = null, string distribution = null, string diagnostics = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("请先停止 Play Mode 或当前构建。");
            // Invalidate before parameter validation, so a rejected attempt cannot select an older success.
            var catalog = ProjectToolCatalog.Load();
            string canonical = catalog.buildAliases.FirstOrDefault(a => a.id == profileId)?.recipe ?? profileId;
            ProjectBuildResults.Invalidate(profileId, canonical);
            ProjectBuildIdentity.ResetLastInfo();
            var resolved = ProjectBuildResolver.Resolve(profileId, buildKind, development, network, distribution, diagnostics);
            var profile = resolved.Recipe;
            if (scriptsOnly) throw new InvalidOperationException("ScriptsOnly 无法保证资源与版本一致，请执行完整构建。");
            BuildKind kind = resolved.Kind;
            bool dev = resolved.Development;
            string[] projectDefines = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Standalone).Split(';');
            ProjectBuildResolver.ValidateProjectDefines(resolved, projectDefines);
            ValidateProfile(profile);
            var assetsBefore = ProjectToolRunner.CaptureAssets();
            var info = ProjectBuildIdentity.Capture(profile, kind, dev);
            info.SetConfiguration(profile.id, resolved.Network.ToString(), resolved.Distribution.ToString(), resolved.Diagnostics.ToString(),
                profile.testAssemblies, resolved.Tools, resolved.Evidence);
            if (resolved.IsAlias) Debug.LogWarning($"[ProjectTools] 旧配置 {profileId} → {profile.id}（{kind}/{resolved.Network}/{resolved.Distribution}）。请改用新配方；具体测试在启动时选择。");
            Debug.Log("[ProjectTools] " + info.Display + "\n" + resolved.Summary);
            foreach (string validation in profile.validations ?? Array.Empty<string>()) ProjectToolRunner.InvokeReadOnly(validation);
            string path = string.IsNullOrWhiteSpace(output) ? profile.output : output;
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("输出必须是 .exe 文件。");
            // Even legacy callers may not overwrite a previously frozen package.
            path = Path.Combine(Path.GetDirectoryName(path), info.ArtifactName, Path.GetFileName(path));
            if (Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(path)))) throw new IOException("构建目录已存在，拒绝覆盖：" + path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            try
            {
                ActiveProfile = profile.id;
                ProjectBuildIdentity.Begin(info, path);
                var options = (dev ? BuildOptions.Development : BuildOptions.None) |
                    (profile.testAssemblies ? BuildOptions.IncludeTestAssemblies : BuildOptions.None);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = profile.scenes, locationPathName = path, target = BuildTarget.StandaloneWindows64,
                    options = options, extraScriptingDefines = resolved.Defines
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"构建 {profileId} 失败: {report.summary.result} ({report.summary.totalErrors} errors)");
                ProjectToolRunner.AssertAssetsUnchanged(assetsBefore);
                ProjectBuildIdentity.Complete(report);
            }
            finally { try { ProjectBuildIdentity.End(); } finally { ActiveProfile = null; } }
            try { ProjectBuildResults.Save(resolved, path, info); }
            catch { ProjectBuildIdentity.InvalidateOutput(path); throw; }
            Debug.Log($"[ProjectTools] Build {profileId}: {Path.GetFullPath(path)}");
            return Path.GetFullPath(path);
        }

        public static void Legacy(string profile, string output = null, bool scriptsOnly = false)
        {
            Debug.LogWarning($"[ProjectTools] 旧构建入口兼容一版，请改用 build.player -Profile {profile}。");
            var result = ProjectToolRunner.Run("build.player", new ProjectToolRequest { profile = profile, output = output, scriptsOnly = scriptsOnly });
            if (!result.success) throw new InvalidOperationException(result.error);
        }
        public static void LegacyBatch(string profile)
        {
            int code = 0;
            try { Legacy(profile); }
            catch (Exception error) { Debug.LogException(error); code = 1; if (!Application.isBatchMode) throw; }
            finally { if (Application.isBatchMode) EditorApplication.Exit(code); }
        }
    }

    [Serializable]
    public sealed class ProjectBuildResultPointer
    {
        public bool success;
        public string requestedProfile, recipe, executable, buildInfoPath, buildId;
    }

    public static class ProjectBuildResults
    {
        public static string PathFor(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Any(c => !(char.IsLetterOrDigit(c) || c == '-')))
                throw new ArgumentException("非法构建 ID。");
            return Path.Combine(ProjectToolCatalog.ProjectRoot, "Library/ProjectTools/BuildResults", id + ".json");
        }
        public static void Invalidate(params string[] ids)
        {
            foreach (string id in ids.Distinct()) { string path = PathFor(id); if (File.Exists(path)) File.Delete(path); }
        }
        public static void Save(ResolvedProjectBuild build, string executable, BuildInfo info)
        {
            var result = new ProjectBuildResultPointer { success = true, requestedProfile = build.RequestedId, recipe = build.Recipe.id,
                executable = Path.GetFullPath(executable), buildInfoPath = ProjectBuildIdentity.InfoPath(executable), buildId = info.BuildId };
            string[] ids = new[] { build.RequestedId, build.Recipe.id }.Distinct().ToArray();
            try
            {
                foreach (string id in ids)
                {
                    string path = PathFor(id); Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path + ".tmp", JsonUtility.ToJson(result, true), new System.Text.UTF8Encoding(false));
                }
                foreach (string id in ids)
                {
                    string path = PathFor(id);
                    if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
                }
            }
            catch { Invalidate(ids); throw; }
            finally
            {
                foreach (string id in ids) if (File.Exists(PathFor(id) + ".tmp")) File.Delete(PathFor(id) + ".tmp");
            }
        }
    }
}
