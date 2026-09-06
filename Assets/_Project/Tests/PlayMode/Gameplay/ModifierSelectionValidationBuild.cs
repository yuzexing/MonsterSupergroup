#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class ModifierSelectionValidationBuild
    {
        public static void Build()
        {
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = Environment.GetEnvironmentVariable("MODIFIER_SELECTION_VALIDATION_OUTPUT") ??
                    "Builds/ModifierSelectionValidation/ModifierSelectionValidation.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.IncludeTestAssemblies,
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" }
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Modifier selection validation build failed: " + report.summary.result);
        }
    }
}
#endif
