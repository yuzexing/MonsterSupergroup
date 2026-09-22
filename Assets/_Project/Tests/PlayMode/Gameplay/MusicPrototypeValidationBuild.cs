#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public static class MusicPrototypeValidationBuild
    {
        public static void Build() => MonsterSupergroup.EditorTools.ProjectBuildService.Build(
            "knockback", "Builds/MusicPrototype/MusicPrototype.exe");

        public static void BuildBatch()
        {
            int code = 0;
            try { Build(); }
            catch (Exception error) { Debug.LogException(error); code = 1; if (!Application.isBatchMode) throw; }
            finally { if (Application.isBatchMode) EditorApplication.Exit(code); }
        }
    }
}
#endif
