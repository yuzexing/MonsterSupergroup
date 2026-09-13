#if UNITY_EDITOR
namespace MonsterSupergroup.Gameplay.Tests
{
    public static class GameplayExperienceValidationBuild
    {
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("experience");
        }
    }
}
#endif
