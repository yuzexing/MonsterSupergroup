#if UNITY_EDITOR
using UnityEditor;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class EnemyHitFlashValidationBuild
    {
        public static void Build() => Build(BuildOptions.None);
        public static void BuildScriptsOnly() => Build(BuildOptions.BuildScriptsOnly);

        private static void Build(BuildOptions incrementalOptions)
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("enemy-hit-flash", scriptsOnly: incrementalOptions == BuildOptions.BuildScriptsOnly);
        }
    }
}
#endif
