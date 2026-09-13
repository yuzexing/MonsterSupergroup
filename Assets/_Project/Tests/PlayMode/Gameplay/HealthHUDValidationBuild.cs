#if UNITY_EDITOR
using System;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class HealthHUDValidationBuild
    {
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("health-hud", Environment.GetEnvironmentVariable("HEALTH_HUD_VALIDATION_OUTPUT"));
        }
    }
}
#endif
