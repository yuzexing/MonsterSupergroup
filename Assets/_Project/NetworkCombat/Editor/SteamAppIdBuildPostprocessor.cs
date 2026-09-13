using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public sealed class SteamAppIdBuildPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            ConfigureAppIdFile(
                report.summary.platform,
                report.summary.outputPath,
                (report.summary.options & BuildOptions.Development) != 0);
        }

        public static void ConfigureAppIdFile(
            BuildTarget target,
            string builtPlayerPath,
            bool developmentBuild)
        {
            if (target != BuildTarget.StandaloneWindows64)
            {
                return;
            }

            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string source = Path.Combine(projectRoot, "steam_appid.txt");

            string outputDirectory = Path.GetDirectoryName(builtPlayerPath);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new BuildFailedException(
                    "Windows build output directory could not be resolved.");
            }

            string destination = Path.GetFullPath(Path.Combine(
                outputDirectory,
                "steam_appid.txt"));
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    "Build the Windows player into a separate directory, not the project root.");
            }

            // Steam supplies the AppID for depot builds. Remove leftovers from
            // development builds when reusing an output directory.
            if (!developmentBuild)
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                Debug.Log($"Steam depot build uses AppID {SteamLobbyService.SteamAppId}; steam_appid.txt excluded.");
                return;
            }

            if (!File.Exists(source))
            {
                throw new BuildFailedException(
                    "Windows Steam development build requires steam_appid.txt.");
            }

            string appId = File.ReadAllText(source).Trim();
            string expected = SteamLobbyService.SteamAppId.ToString();
            if (!string.Equals(appId, expected, StringComparison.Ordinal))
            {
                throw new BuildFailedException(
                    $"steam_appid.txt must contain {expected}, but contains " +
                    $"'{appId}'.");
            }

            File.Copy(source, destination, true);
            Debug.Log($"Steam development AppID available at: {destination}");
        }
    }
}
