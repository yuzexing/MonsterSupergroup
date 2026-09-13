#if UNITY_EDITOR

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class OrdinaryKnockbackValidationBuild
    {
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("knockback");
        }
    }
}
#endif
