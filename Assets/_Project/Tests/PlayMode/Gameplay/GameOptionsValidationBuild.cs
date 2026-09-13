#if UNITY_EDITOR
using System;
using System.IO;
using MonsterSupergroup.NetworkCombat.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class GameOptionsValidationBuild
    {
        public static void Build() => BuildPlayer(false);
        public static void BuildScriptsOnly() => BuildPlayer(true);
        private static void BuildPlayer(bool scriptsOnly)
        {
            if (!scriptsOnly) GameLocalizationAssets.Validate();
            const string path = "Builds/OptionsValidation/MonsterSupergroup.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/MainMenu.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = path, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.IncludeTestAssemblies | BuildOptions.Development | (scriptsOnly ? BuildOptions.BuildScriptsOnly : BuildOptions.None),
                extraScriptingDefines = new[] { "MONSTER_OPTIONS_VALIDATION", "MONSTER_MENU_VALIDATION", "MONSTER_KCP_DEVELOPMENT_BUILD" }
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Options validation build: " + report.summary.result);
        }
    }
}
#endif
