#if UNITY_EDITOR

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class RewiredAbilityValidationBuild
    {
        public static void Development() => Build(true);
        public static void Release() => Build(false);

        private static void Build(bool development)
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy(development ? "rewired-development" : "rewired-release");
        }
    }
}
#endif
