#if UNITY_EDITOR
using System;
using System.IO;
using AstralShift.HellMaiden.Data.Cards;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class WispValidationBuild : IProcessSceneWithReport
    {
        private static bool building;
        public int callbackOrder => 100;
        public static void Build()
        {
            if (!EditorApplication.ExecuteMenuItem("Tools/HellMaiden Migration/Import Dante Projectile Presentation"))
                throw new InvalidOperationException("Wisp import menu missing");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null &&
                !EditorApplication.ExecuteMenuItem("Tools/HellMaiden Migration/Capture Dante Projectile Presentation Preview"))
                throw new InvalidOperationException("Wisp preview menu missing");
            building = true;
            try
            {
                const string path = "Builds/WispValidation/MonsterSupergroup.exe";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/MainMenu.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                    locationPathName = path, target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.IncludeTestAssemblies | BuildOptions.Development,
                    extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD", "MONSTER_MENU_VALIDATION" }
                });
                if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Wisp validation build: " + report.summary.result);
            }
            finally { building = false; }
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!building || report == null || scene.name != "Boot") return;
            var root = new GameObject("Wisp validation (opt-in)");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<WispPresentationProcessProbe>().Weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(
                "Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset");
        }
    }
}
#endif
