using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class SteamAppIdBuildPostprocessor
    {
        [PostProcessBuild(100)]
        public static void CopyDevelopmentAppId(
            BuildTarget target,
            string builtPlayerPath)
        {
            if (target != BuildTarget.StandaloneWindows64)
            {
                return;
            }

            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string source = Path.Combine(projectRoot, "steam_appid.txt");
            if (!File.Exists(source))
            {
                throw new BuildFailedException(
                    "Windows Steam development build requires steam_appid.txt.");
            }

            string appId = File.ReadAllText(source).Trim();
            string expected = SteamLobbyService.DevelopmentAppId.ToString();
            if (!string.Equals(appId, expected, StringComparison.Ordinal))
            {
                throw new BuildFailedException(
                    $"steam_appid.txt must contain {expected}, but contains " +
                    $"'{appId}'.");
            }

            string outputDirectory = Path.GetDirectoryName(builtPlayerPath);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new BuildFailedException(
                    "Windows build output directory could not be resolved.");
            }

            string destination = Path.Combine(
                outputDirectory,
                "steam_appid.txt");
            if (!string.Equals(
                    Path.GetFullPath(source),
                    Path.GetFullPath(destination),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, destination, true);
            }
            Debug.Log($"Steam development AppID available at: {destination}");
        }
    }
}
