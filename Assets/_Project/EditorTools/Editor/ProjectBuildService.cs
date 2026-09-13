using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

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

        public static string Build(string profileId, string output = null, bool scriptsOnly = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("请先停止 Play Mode 或当前构建。");
            var profile = ProjectToolCatalog.Load().builds.SingleOrDefault(p => p.id == profileId)
                ?? throw new ArgumentException("未知构建配置: " + profileId);
            ValidateProfile(profile);
            foreach (string validation in profile.validations ?? Array.Empty<string>()) ProjectToolRunner.InvokeReadOnly(validation);
            string path = string.IsNullOrWhiteSpace(output) ? profile.output : output;
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("输出必须是 .exe 文件。");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            if (scriptsOnly && !File.Exists(path)) throw new FileNotFoundException("增量脚本构建要求已有完整包: " + path);
            try
            {
                ActiveProfile = profileId;
                var options = (profile.development ? BuildOptions.Development : BuildOptions.None) |
                    (profile.testAssemblies ? BuildOptions.IncludeTestAssemblies : BuildOptions.None) |
                    (scriptsOnly ? BuildOptions.BuildScriptsOnly : BuildOptions.None);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = profile.scenes, locationPathName = path, target = BuildTarget.StandaloneWindows64,
                    options = options, extraScriptingDefines = profile.defines ?? Array.Empty<string>()
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"构建 {profileId} 失败: {report.summary.result} ({report.summary.totalErrors} errors)");
                Debug.Log($"[ProjectTools] Build {profileId}: {Path.GetFullPath(path)}");
                return Path.GetFullPath(path);
            }
            finally { ActiveProfile = null; }
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
}
