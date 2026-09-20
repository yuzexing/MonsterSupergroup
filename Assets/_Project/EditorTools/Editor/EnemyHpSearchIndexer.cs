#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;

namespace MonsterSupergroup.EditorTools._Project.EditorTools.Editor
{
    internal static class EnemyHpSearchIndexer
    {
        private const string HpPath = "stats.baseStats.hp";
        private const int IndexVersion = 2;

        [CustomObjectIndexer(typeof(GameObject), version = IndexVersion)]
        internal static void IndexEnemyHp(
            CustomObjectIndexerTarget context,
            ObjectIndexer indexer)
        {
            if (context.target is not GameObject gameObject)
                return;

            // 检查标记 A：
            // 证明这个对象执行过当前版本的索引回调。
            indexer.IndexNumber(
                context.documentIndex,
                "enemyhpindexerversion",
                IndexVersion);

            // 保持原有语义：只检查当前 GameObject 上的组件。
            foreach (var component in gameObject.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                    continue;

                using var serializedObject = new SerializedObject(component);
                var hp = serializedObject.FindProperty(HpPath);

                if (hp == null ||
                    hp.propertyType != SerializedPropertyType.Integer)
                {
                    continue;
                }

                indexer.IndexNumber(
                    context.documentIndex,
                    "enemybasehp",
                    hp.intValue);

                string componentKey =
                    component.GetType().Name.ToLowerInvariant() + ".hp";

                indexer.IndexNumber(
                    context.documentIndex,
                    componentKey,
                    hp.intValue);

                // 检查标记 B：
                // 证明已经找到整数类型的 HP，并执行了数值写入。
                indexer.IndexProperty(
                    context.documentIndex,
                    "enemyhpindexed",
                    "true",
                    saveKeyword: true);
            }
        }

        [MenuItem(
            "Tools/MonsterSupergroup/Search/Check Selected Prefab HP")]
        private static void CheckSelectedPrefabHp()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);

            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(
                    ".prefab", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("请先在 Project 中选中一个敌人 Prefab 资源。");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            if (prefab == null)
            {
                Debug.LogError($"无法读取 Prefab：{assetPath}");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"[Enemy HP 诊断] {assetPath}");
            report.AppendLine($"目标字段：{HpPath}");

            int validHpCount = 0;

            // 诊断时检查整个 Prefab，包括未激活子节点。
            foreach (var component in
                     prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                {
                    report.AppendLine("发现 Missing Script。");
                    continue;
                }

                string nodePath = GetNodePath(
                    component.transform, prefab.transform);

                report.AppendLine();
                report.AppendLine($"节点：{nodePath}");
                report.AppendLine($"组件：{component.GetType().FullName}");

                using var serializedObject = new SerializedObject(component);
                var hp = serializedObject.FindProperty(HpPath);

                if (hp == null)
                {
                    report.AppendLine("结果：没有找到目标 HP 字段。");
                    continue;
                }

                if (hp.propertyType != SerializedPropertyType.Integer)
                {
                    report.AppendLine(
                        $"结果：找到字段，但类型为 {hp.propertyType}，不是 Integer。");
                    continue;
                }

                validHpCount++;

                report.AppendLine(
                    $"结果：读取成功，HP = {hp.intValue}，" +
                    $"位置 = {(component.gameObject == prefab ? "根节点" : "子节点")}");
            }

            report.AppendLine();
            report.AppendLine($"成功读取 HP 的组件数量：{validHpCount}");

            Debug.Log(report.ToString(), prefab);
        }

        private static string GetNodePath(Transform current, Transform root)
        {
            string path = current.name;

            while (current != root && current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }
    }
}
#endif