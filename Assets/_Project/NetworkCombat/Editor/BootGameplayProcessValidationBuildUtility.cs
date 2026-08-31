using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class BootGameplayProcessValidationBuildUtility
    {
        public const string OutputPath =
            "Builds/BootGameplayValidation/" +
            "MonsterSupergroupBootGameplayValidation.exe";

        [MenuItem(
            "Monster Supergroup/Network Combat/Build Boot Gameplay " +
            "Process Validation Player")]
        public static void BuildWindowsPlayer()
        {
            NetworkCombatSetupUtility.BuildBootGameplayAssets();

            string outputDirectory = Path.GetDirectoryName(OutputPath);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new InvalidOperationException(
                    "Validation output path is invalid.");
            }
            Directory.CreateDirectory(outputDirectory);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[]
                {
                    NetworkCombatSetupUtility.BootScenePath,
                    NetworkCombatSetupUtility.GameplayScenePath
                },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Boot Gameplay process validation Player build failed: " +
                    report.summary.result);
            }

            Debug.Log(
                $"Boot Gameplay process validation Player built: {OutputPath}");
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
    }
}
