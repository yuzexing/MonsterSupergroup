#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class OrdinaryKnockbackValidationBuild
    {
        public static void Build()
        {
            string output = "Builds/M3Knockback/M3Knockback.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = output, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.IncludeTestAssemblies,
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" }
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("M3 validation build failed.");
        }
    }
}
#endif
