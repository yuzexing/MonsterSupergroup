using System;
using System.Linq;
using MonsterSupergroup.Builds;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace MonsterSupergroup.EditorTools
{
    public enum BuildNetwork { Steam, Kcp }
    public enum BuildDistribution { Steam, Direct }
    public enum BuildDiagnostics { Normal, Evidence }

    // Only business intent lives here. Unity settings belong to the containing BuildProfile.
    public sealed class MonsterBuildSettings : ScriptableObject
    {
        public int SchemaVersion = 1;
        public BuildKind BuildKind = BuildKind.Test;
        public BuildNetwork Network = BuildNetwork.Steam;
        public BuildDistribution Distribution = BuildDistribution.Steam;
        public BuildDiagnostics Diagnostics = BuildDiagnostics.Normal;
        public string PurposeId = "product";
    }

    public static class ProjectBuildPurposes
    {
        public static readonly string[] Ids = { "product", "gameplay-validation", "wisp-validation", "options-validation", "handoff-validation", "sandbox", "nordic" };
        public static ProjectBuildProfile Get(string id)
        {
            if (!Ids.Contains(id)) throw new BuildFailedException("未知构建用途：" + id);
            bool product = id == "product", sample = id == "sandbox" || id == "nordic";
            return new ProjectBuildProfile {
                id = id, name = id, product = product, testAssemblies = !product && !sample,
                scenes = id == "sandbox" ? new[] { "Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity" } :
                    id == "nordic" ? new[] { "Assets/_Project/Scenes/NordicStaticSample.unity" } :
                    new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/MainMenu.unity", "Assets/_Project/Scenes/Gameplay.unity" },
                defines = product || sample ? Array.Empty<string>() : id == "options-validation" ? new[] { "MONSTER_MENU_VALIDATION", "MONSTER_OPTIONS_VALIDATION" } :
                    id == "handoff-validation" ? new[] { "MONSTER_MENU_VALIDATION", "MONSTER_ENEMY_HANDOFF_VALIDATION" } : new[] { "MONSTER_MENU_VALIDATION" },
                validations = id == "sandbox" ? new[] { "validate.gas" } : id == "nordic" ? new[] { "sample.nordic-validate" } :
                    id == "gameplay-validation" ? new[] { "validate.all", "validate.nordic-gameplay" } : new[] { "validate.all" }
            };
        }
    }

    public static class ProjectBuildDefines
    {
        private static readonly string[] Managed = {
            "MONSTER_BUILD_DEV", "MONSTER_BUILD_TEST", "MONSTER_BUILD_SHIPPING", "MONSTER_BUILD_TOOLS", "MONSTER_BUILD_EVIDENCE",
            "MONSTER_KCP_DEVELOPMENT_BUILD", "MONSTER_COMBAT_EVIDENCE", "MONSTER_MENU_VALIDATION", "MONSTER_OPTIONS_VALIDATION", "MONSTER_ENEMY_HANDOFF_VALIDATION"
        };
        public static bool IsManaged(string symbol) => Managed.Contains(symbol);
        public static bool IsReserved(string symbol) => IsManaged(symbol) || symbol == "UNITY_INCLUDE_TESTS" || symbol.StartsWith("MONSTER_BUILD_", StringComparison.Ordinal);
        public static string[] Expected(MonsterBuildSettings settings)
        {
            var purpose = ProjectBuildPurposes.Get(settings.PurposeId);
            return purpose.defines.Concat(new[] { "MONSTER_BUILD_" + settings.BuildKind.ToString().ToUpperInvariant() })
                .Concat(settings.BuildKind == BuildKind.Dev || !purpose.product ? new[] { "MONSTER_BUILD_TOOLS" } : Array.Empty<string>())
                .Concat(settings.Network == BuildNetwork.Kcp ? new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" } : Array.Empty<string>())
                .Concat(settings.Diagnostics == BuildDiagnostics.Evidence ? new[] { "MONSTER_BUILD_EVIDENCE", "MONSTER_COMBAT_EVIDENCE" } : Array.Empty<string>())
                .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }
        public static string[] Applied(string[] current, string[] expected) => (current ?? Array.Empty<string>()).Where(s => !IsManaged(s))
            .Concat(expected).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        public static string Difference(string[] current, string[] expected)
        {
            current ??= Array.Empty<string>();
            var missing = expected.Except(current).ToArray();
            var extra = current.Where(IsReserved).Except(expected).ToArray();
            return missing.Length == 0 && extra.Length == 0 ? "" : "缺少：" + string.Join(", ", missing) + "；多余／冲突：" + string.Join(", ", extra);
        }
        public static void Apply(BuildProfile profile)
        {
            var plan = ProjectBuildResolver.Resolve(profile);
            Undo.RecordObject(profile, "应用项目构建符号");
            profile.scriptingDefines = Applied(profile.scriptingDefines, plan.Defines);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }
    }

    [CustomEditor(typeof(MonsterBuildSettings))]
    public sealed class MonsterBuildSettingsEditor : Editor
    {
        internal const string PreparationHint = "业务配置保存后，请在构建配置窗口应用受管理符号并保存，再激活 Profile，等待编译完成。查看和编辑不会自动切换 Profile 或应用符号。";
        public override void OnInspectorGUI()
        {
            DrawFields(serializedObject);
            EditorGUILayout.HelpBox(PreparationHint, MessageType.Info);
        }
        internal static void DrawFields(SerializedObject settings)
        {
            settings.Update();
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(settings.FindProperty("SchemaVersion"));
            var purpose = settings.FindProperty("PurposeId");
            int index = Array.IndexOf(ProjectBuildPurposes.Ids, purpose.stringValue);
            if (index < 0) EditorGUILayout.PropertyField(purpose);
            else purpose.stringValue = ProjectBuildPurposes.Ids[EditorGUILayout.Popup("用途", index, ProjectBuildPurposes.Ids)];
            foreach (string field in new[] { "BuildKind", "Network", "Distribution", "Diagnostics" }) EditorGUILayout.PropertyField(settings.FindProperty(field));
            settings.ApplyModifiedProperties();
        }
    }
}
