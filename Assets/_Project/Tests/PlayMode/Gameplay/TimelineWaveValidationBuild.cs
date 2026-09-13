#if UNITY_EDITOR
using MonsterSupergroup.NetworkCombat.Editor;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class TimelineWaveValidationBuild
    {
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("timeline-waves");
        }
    }
}
#endif
