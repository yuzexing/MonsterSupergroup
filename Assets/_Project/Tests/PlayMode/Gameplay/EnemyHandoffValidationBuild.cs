#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class EnemyHandoffValidationBuild
    {
        public static void Build()
        {
            bool release = Environment.GetEnvironmentVariable("ENEMY_HANDOFF_RELEASE") == "1";
            string path = release ? "Builds/EnemyHandoffRelease/EnemyHandoff.exe" : "Builds/EnemyHandoff/EnemyHandoff.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = path, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.IncludeTestAssemblies | (release ? BuildOptions.None : BuildOptions.Development),
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD", "MONSTER_ENEMY_HANDOFF_VALIDATION" }
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Handoff build: " + report.summary.result);
        }
    }
}
#endif
