#if UNITY_EDITOR

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class GameplayWaveValidationBuild
    {
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("waves");
        }
    }
}
#endif
