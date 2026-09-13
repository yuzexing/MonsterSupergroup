#if UNITY_EDITOR
using System;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class PreparationMenuValidationBuild
    {
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy(Environment.GetEnvironmentVariable("MENU_RELEASE") == "1" ? "menu-release" : "menu-development");
        }
    }
}
#endif
