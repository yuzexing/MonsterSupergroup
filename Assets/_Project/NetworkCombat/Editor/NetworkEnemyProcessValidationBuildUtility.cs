using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class NetworkEnemyProcessValidationBuildUtility
    {
        public const string OutputPath =
            "Builds/EnemySimulationValidation/" +
            "MonsterSupergroupEnemySimulationValidation.exe";

        [MenuItem(
            "Monster Supergroup/Network Combat/Build Enemy Simulation " +
            "Process Validation Player")]
        public static void BuildWindowsPlayer()
        {
            NetworkCombatSetupUtility.BuildSandboxAssets();

            string outputDirectory = Path.GetDirectoryName(OutputPath);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new InvalidOperationException("Validation output path is invalid.");
            }
            Directory.CreateDirectory(outputDirectory);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { NetworkCombatSetupUtility.SandboxScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Enemy Simulation process validation Player build failed: " +
                    report.summary.result);
            }

            Debug.Log(
                $"Enemy Simulation process validation Player built: {OutputPath}");
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
