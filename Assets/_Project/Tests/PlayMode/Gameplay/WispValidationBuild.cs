#if UNITY_EDITOR
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
        public int callbackOrder => 100;
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("wisp");
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            string profile = MonsterSupergroup.EditorTools.ProjectBuildService.ActiveProfile;
            if ((profile != "wisp-validation" && profile != "wisp") || report == null || scene.name != "Boot") return;
            var root = new GameObject("Wisp validation (opt-in)");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<WispPresentationProcessProbe>().Weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(
                "Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset");
        }
    }
}
#endif
