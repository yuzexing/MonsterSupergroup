#if UNITY_EDITOR
using System;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class ModifierSelectionValidationBuild
    {
        public static void Build()
        {
            MonsterSupergroup.EditorTools.ProjectBuildService.Legacy("modifier-selection", Environment.GetEnvironmentVariable("MODIFIER_SELECTION_VALIDATION_OUTPUT"));
        }
    }
}
#endif
