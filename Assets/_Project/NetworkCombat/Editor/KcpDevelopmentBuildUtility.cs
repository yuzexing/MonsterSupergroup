using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class KcpDevelopmentBuildUtility
    {
        public const string OutputPath =
            "Builds/KcpDevelopment/MonsterSupergroupKcp.exe";
        public const string BuildDefine =
            "MONSTER_KCP_DEVELOPMENT_BUILD";

        [MenuItem(
            "Monster Supergroup/Network Combat/" +
            "Build KCP Development Player")]
        public static void BuildWindowsPlayer()
        {
            string[] scenes = GetValidatedEnabledScenes();
            string outputDirectory = Path.GetDirectoryName(OutputPath);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new InvalidOperationException(
                    "KCP development output path is invalid.");
            }
            Directory.CreateDirectory(outputDirectory);

            BuildReport report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = OutputPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development,
                    extraScriptingDefines = new[] { BuildDefine }
                });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "KCP development Player build failed: " +
                    report.summary.result);
            }

            Debug.Log($"KCP development Player built: {OutputPath}");
        }

        public static void BuildWindowsPlayerBatch()
        {
            try
            {
                BuildWindowsPlayer();
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }
                throw;
            }
        }

        internal static string[] GetValidatedEnabledScenes()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0 || !string.Equals(
                    scenes[0],
                    NetworkCombatSetupUtility.BootScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Boot.unity must be the first enabled Build Settings scene.");
            }
            if (!scenes.Contains(
                    NetworkCombatSetupUtility.GameplayScenePath,
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Gameplay.unity must be enabled in Build Settings.");
            }

            return scenes;
        }
    }
}
