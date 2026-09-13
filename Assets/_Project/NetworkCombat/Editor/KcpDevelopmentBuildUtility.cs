
namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class KcpDevelopmentBuildUtility
    {
        public const string OutputPath =
            "Builds/KcpDevelopment/MonsterSupergroupKcp.exe";
        public const string BuildDefine =
            "MONSTER_KCP_DEVELOPMENT_BUILD";


        public static void BuildWindowsPlayer()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("kcp-development");
        }

        public static void BuildWindowsPlayerBatch()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.LegacyBatch("kcp-development");
        }

    }
}
