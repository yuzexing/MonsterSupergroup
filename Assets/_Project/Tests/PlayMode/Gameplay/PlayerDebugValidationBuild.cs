#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class PlayerDebugValidationBuild
    {
        public static void Build()
        {
            bool development = Environment.GetEnvironmentVariable("PLAYER_DEBUG_RELEASE") != "1";
            string output = development ? "Builds/PlayerDebugDevelopment/PlayerDebug.exe" : "Builds/PlayerDebugRelease/PlayerDebug.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = output, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.IncludeTestAssemblies | (development ? BuildOptions.Development : BuildOptions.None),
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" }
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Player Debug validation build failed: " + report.summary.result);
        }
    }
}
#endif
