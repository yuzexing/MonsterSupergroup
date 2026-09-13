#if UNITY_EDITOR
using System;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class EnemyHandoffValidationBuild
    {
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("enemy-handoff" + (Environment.GetEnvironmentVariable("ENEMY_HANDOFF_RELEASE") == "1" ? "-release" : "-development"));
        }
    }
}
#endif
