
namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class NetworkEnemyProcessValidationBuildUtility
    {
        public const string OutputPath =
            "Builds/EnemySimulationValidation/" +
            "MonsterSupergroupEnemySimulationValidation.exe";


        public static void BuildWindowsPlayer()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("sandbox");
        }

        public static void BuildWindowsPlayerBatch()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.LegacyBatch("sandbox");
        }
    }
}
