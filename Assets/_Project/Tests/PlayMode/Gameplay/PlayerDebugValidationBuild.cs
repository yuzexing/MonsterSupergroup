#if UNITY_EDITOR
using System;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class PlayerDebugValidationBuild
    {
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("player-debug" + (Environment.GetEnvironmentVariable("PLAYER_DEBUG_RELEASE") == "1" ? "-release" : "-development"));
        }
    }
}
#endif
