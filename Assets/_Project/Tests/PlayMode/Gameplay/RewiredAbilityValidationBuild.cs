#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class RewiredAbilityValidationBuild
    {
        public static void Development() => Build(true);
        public static void Release() => Build(false);

        private static void Build(bool development)
        {
            string configuration = development ? "Development" : "Release";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = $"Builds/RewiredAbilities/{configuration}/RewiredAbilities.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.IncludeTestAssemblies | (development ? BuildOptions.Development : BuildOptions.None),
                // Enables the existing KCP test backend; does not make Debug.isDebugBuild true.
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" }
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Rewired {configuration} build failed: {report.summary.result}");
        }
    }
}
#endif
