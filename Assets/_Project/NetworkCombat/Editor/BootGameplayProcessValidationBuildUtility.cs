
namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class BootGameplayProcessValidationBuildUtility
    {
        public const string OutputPath =
            "Builds/BootGameplayValidation/" +
            "MonsterSupergroupBootGameplayValidation.exe";


        public static void BuildWindowsPlayer()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("boot-process");
        }

        public static void BuildWindowsPlayerBatch()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.LegacyBatch("boot-process");
        }
    }
}
