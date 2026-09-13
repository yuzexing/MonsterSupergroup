#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class PreparationMenuValidationBuild
    {
        public static void Build()
        {
            if (!UnityEditor.EditorApplication.ExecuteMenuItem("MonsterSupergroup/Localization/Validate tables and content"))
                throw new InvalidOperationException("Localization validation entry is missing.");
            bool release = Environment.GetEnvironmentVariable("MENU_RELEASE") == "1";
            string path = release ? "Builds/MenuRelease/MonsterSupergroup.exe" : "Builds/MenuDevelopment/MonsterSupergroup.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/MainMenu.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                locationPathName = path, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.IncludeTestAssemblies | (release ? BuildOptions.None : BuildOptions.Development),
                extraScriptingDefines = new[] { "MONSTER_MENU_VALIDATION" }
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Menu build: " + report.summary.result);
        }
    }
}
#endif
