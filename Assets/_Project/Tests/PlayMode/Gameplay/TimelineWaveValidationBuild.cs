#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using MonsterSupergroup.NetworkCombat.Editor;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class TimelineWaveValidationBuild
    {
        public static void Build()
        {
            NetworkWaveTimelineEditorUtility.VerifyRepeat();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = "Builds/TimelineWaves/TimelineWaves.exe", target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.IncludeTestAssemblies,
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD", "MONSTER_MENU_VALIDATION" }
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Timeline wave build failed.");
        }
    }
}
#endif
