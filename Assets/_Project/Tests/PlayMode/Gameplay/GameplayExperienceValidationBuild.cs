#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
namespace MonsterSupergroup.Gameplay.Tests
{
    public static class GameplayExperienceValidationBuild
    {
        public static void Build()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = "Builds/M6Experience/M6Experience.exe", target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.IncludeTestAssemblies,
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" }
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("M6 build failed: " + report.summary.result);
        }
    }
}
#endif
