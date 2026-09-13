#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class EnemyHitFlashValidationBuild
    {
        public static void Build() => Build(BuildOptions.None);
        public static void BuildScriptsOnly() => Build(BuildOptions.BuildScriptsOnly);

        private static void Build(BuildOptions incrementalOptions)
        {
            const string output = "Builds/EnemyHitFlash/EnemyHitFlash.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/MainMenu.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.IncludeTestAssemblies | incrementalOptions,
                // Include the menu scenario referenced by the shared PlayMode test assembly.
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD", "MONSTER_MENU_VALIDATION" }
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Enemy hit flash validation build failed.");
        }
    }
}
#endif
