#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class GameplayCameraValidationBuild
    {
        public static void Build()
        {
            string output = Environment.GetEnvironmentVariable("CAMERA_VALIDATION_OUTPUT") ??
                "Builds/GameplayCameraValidation/GameplayCameraValidation.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.IncludeTestAssemblies,
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" }
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Gameplay camera validation build failed: " + report.summary.result);
        }
    }
}
#endif
