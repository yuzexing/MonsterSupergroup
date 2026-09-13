using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static partial class EnemyPrefabVariantMigration
    {
        [Serializable] private class ContentComparison
        {
            public int propertiesCompared;
            public List<string> differences = new List<string>();
            public List<string> localFileIds = new List<string>();
        }

        public static void AuditAndBuild()
        {
            Migrate();
            CompareArchivedSkeleton();
            const string output = "Builds/EnemyVariants/EnemyVariants.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { NetworkCombatSetupUtility.BootScenePath, NetworkCombatSetupUtility.GameplayScenePath },
                locationPathName = output, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.IncludeTestAssemblies,
                // IncludeTestAssemblies also compiles the existing menu tests, whose
                // shared player-side helper is guarded by this validation symbol.
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD", "MONSTER_MENU_VALIDATION" }
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Enemy Variant build failed.");
        }

        public static void CompareArchivedSkeleton()
        {
            string archive = ReportFolder + "/before/" + SkeletonPath;
            const string temporary = "Assets/_Project/Content/NetworkCombat/VariantAuditOriginal.prefab";
            if (!File.Exists(archive)) throw new InvalidOperationException("Missing migration baseline archive.");
            if (File.Exists(temporary)) throw new InvalidOperationException("Audit staging path already exists.");
            var result = new ContentComparison();
            try
            {
                File.Copy(archive, temporary);
                AssetDatabase.ImportAsset(temporary, ImportAssetOptions.ForceSynchronousImport);
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(temporary);
                var variant = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonPath);
                foreach (var from in original.GetComponentsInChildren<Transform>(true))
                {
                    var to = (Transform)Resolve(Key(from, original), variant);
                    if (from.localPosition != to.localPosition || from.localRotation != to.localRotation ||
                        from.localScale != to.localScale || from.gameObject.activeSelf != to.gameObject.activeSelf ||
                        from.gameObject.layer != to.gameObject.layer || from.gameObject.tag != to.gameObject.tag)
                        result.differences.Add("Transform or GameObject: " + Key(from, original));
                    foreach (var component in from.GetComponents<Component>())
                    {
                        if (component is Transform) continue;
                        string key = Key(component, original);
                        var target = Resolve(key, variant);
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out string _, out long before);
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string _, out long after);
                        result.localFileIds.Add(key + ": " + before + " -> " + after);
                        var input = new SerializedObject(component);
                        var output = new SerializedObject(target);
                        var p = input.GetIterator();
                        bool enterChildren = true;
                        while (p.Next(enterChildren))
                        {
                            enterChildren = false;
                            string path = p.propertyPath;
                            if (path == "m_Script" || path == "m_GameObject" || path == "m_ObjectHideFlags" ||
                                path == "m_CorrespondingSourceObject" || path.StartsWith("m_Prefab") || path == "m_EditorClassIdentifier" ||
                                path == "_assetId" || path == "sceneId" || path == "combatantBinding") continue;
                            if (p.propertyType == SerializedPropertyType.Generic) { enterChildren = true; continue; }
                            var actual = output.FindProperty(path);
                            bool same = actual != null;
                            if (same && p.propertyType == SerializedPropertyType.ObjectReference)
                                same = ReferenceValue(p.objectReferenceValue, original) == ReferenceValue(actual.objectReferenceValue, variant);
                            else if (same) same = SerializedProperty.DataEquals(p, actual);
                            result.propertiesCompared++;
                            if (!same) result.differences.Add(key + "." + path);
                        }
                    }
                }
                File.WriteAllText(ReportFolder + "/authored-content-comparison.json", JsonUtility.ToJson(result, true));
                if (result.differences.Count != 0)
                    throw new InvalidOperationException("Authored Skeleton fields changed: " + string.Join(", ", result.differences));
                Debug.Log("[EnemyVariants] original authored properties preserved: " + result.propertiesCompared);
            }
            finally { AssetDatabase.DeleteAsset(temporary); }
        }

        private static string ReferenceValue(Object value, GameObject root)
        {
            if (value == null) return "null";
            var node = value is Component component ? component.transform : (value as GameObject)?.transform;
            if (node != null && (node == root.transform || node.IsChildOf(root.transform))) return Key(value, root);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id);
            return guid + ":" + id;
        }
    }
}
