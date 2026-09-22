using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using MonsterSupergroup.Builds;
using MonsterSupergroup.EditorTools;

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
                ProjectBuildIdentity.Active);
        }

        public static void ConfigureAppIdFile(
            BuildTarget target,
            string builtPlayerPath,
            BuildInfo build)
        {
            if (target != BuildTarget.StandaloneWindows64)
            {
                return;
            }
            if (build == null) throw new BuildFailedException("Steam 启动配置缺失，请使用统一构建入口。");
            if ((build.Network != "Steam" && build.Network != "Kcp") ||
                (build.Distribution != "Steam" && build.Distribution != "Direct"))
                throw new BuildFailedException("无法识别构建的网络或分发方式。");

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

            // Launch distribution is independent of Unity's Development Build flag.
            // Steam supplies the AppID for depot builds; KCP never needs an override.
            if (build.Network != "Steam" || build.Distribution != "Direct")
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                Debug.Log($"[Build] network={build.Network} distribution={build.Distribution}; steam_appid.txt excluded.");
                return;
            }

            if (!File.Exists(source))
            {
                throw new BuildFailedException(
                    "Steam 本地直启包需要项目根目录的 steam_appid.txt。");
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
            Debug.Log($"[Build] Steam 本地直启 AppID: {destination}");
        }
    }
}
