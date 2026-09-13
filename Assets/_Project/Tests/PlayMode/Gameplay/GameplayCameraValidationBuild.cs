#if UNITY_EDITOR
using System;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class GameplayCameraValidationBuild
    {
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("camera", Environment.GetEnvironmentVariable("CAMERA_VALIDATION_OUTPUT"));
        }
    }
}
#endif
