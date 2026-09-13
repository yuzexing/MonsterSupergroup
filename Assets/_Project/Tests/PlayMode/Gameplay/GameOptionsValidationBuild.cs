#if UNITY_EDITOR
using MonsterSupergroup.NetworkCombat.Editor;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class GameOptionsValidationBuild
    {
        public static void Build() => BuildPlayer(false);
        public static void BuildScriptsOnly() => BuildPlayer(true);
        private static void BuildPlayer(bool scriptsOnly)
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("options", scriptsOnly: scriptsOnly);
        }
    }
}
#endif
