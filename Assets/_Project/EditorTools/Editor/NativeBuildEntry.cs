using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace MonsterSupergroup.EditorTools
{
    [InitializeOnLoad]
    public static class NativeBuildEntry
    {
        public const BuildOptions ContentMask = BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler |
            BuildOptions.EnableDeepProfilingSupport | BuildOptions.CompressWithLz4 |
            BuildOptions.CompressWithLz4HC | BuildOptions.IncludeTestAssemblies;
        static NativeBuildEntry() => BuildPlayerWindow.RegisterBuildPlayerHandler(Build);
        public static void ValidateOptions(BuildOptions options, ResolvedProjectBuild plan, bool fromNativeButton)
        {
            var mask = fromNativeButton ? ContentMask & ~BuildOptions.IncludeTestAssemblies : ContentMask;
            if ((options & mask) != (plan.ExpectedOptions & mask) || (options & BuildOptions.BuildScriptsOnly) != 0)
                throw new BuildFailedException("原生构建选项与 Profile 计划不一致，不能使用过时参数或 ScriptsOnly。");
        }
        public static void Build(BuildPlayerOptions options)
        {
            var profile = BuildProfile.GetActiveBuildProfile();
            if (profile == null) throw new BuildFailedException("请先选择并激活带业务组件的原生 Build Profile。");
            // A rejected native attempt invalidates the same result as any other entry point.
            ProjectBuildResults.Invalidate(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile)));
            var plan = ProjectBuildResolver.Resolve(profile, true);
            ValidateOptions(options.options, plan, true);
            if (options.target != BuildTarget.StandaloneWindows64 || options.subtarget != (int)StandaloneBuildSubtarget.Player ||
                !options.scenes.SequenceEqual(plan.Native.scenes) || (options.extraScriptingDefines?.Length ?? 0) != 0)
                throw new BuildFailedException("原生按钮的平台、场景或附加符号与所选 Profile 不一致。");
            var accepted = ContentMask | BuildOptions.AutoRunPlayer | BuildOptions.CleanBuildCache | BuildOptions.StrictMode | BuildOptions.ShowBuiltPlayer;
            if ((options.options & ~accepted) != 0) throw new BuildFailedException("尚未支持的原生执行选项：" + (options.options & ~accepted));
            ProjectBuildService.Build(profile, new BuildExecutionRequest {
                output = options.locationPathName, runAfterBuild = (options.options & BuildOptions.AutoRunPlayer) != 0,
                cleanBuildCache = (options.options & BuildOptions.CleanBuildCache) != 0,
                expectedContentHash = plan.ContentHash, expectedInputHash = plan.InputHash
            });
        }
        public static void Batch()
        {
            int code = 1;
            try
            {
                var args = Environment.GetCommandLineArgs();
                string Value(string key) { int at = Array.IndexOf(args, key); return at >= 0 && at + 1 < args.Length ? args[at + 1] : null; }
                if (args.Any(a => new[] { "-toolProfile", "-toolBuildKind", "-toolDevelopment", "-toolNetwork", "-toolDistribution", "-toolDiagnostics", "-toolScriptsOnly", "-toolUniqueOutput" }.Contains(a)))
                    throw new BuildFailedException(ProjectBuildResolver.MigrationMessage);
                string path = Value("-activeBuildProfile");
                var result = ProjectToolRunner.Run("build.player", new ProjectToolRequest {
                    buildProfile = path, output = Value("-toolOutput"), resultPath = Value("-toolResult"),
                    cleanBuildCache = args.Contains("-toolCleanBuildCache"), runAfterBuild = args.Contains("-toolRunAfterBuild")
                });
                code = result.success ? 0 : 1;
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { if (Application.isBatchMode) EditorApplication.Exit(code); }
        }
    }
}
