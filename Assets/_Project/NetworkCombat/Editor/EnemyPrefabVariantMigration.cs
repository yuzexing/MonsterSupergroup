using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static partial class EnemyPrefabVariantMigration
    {
        public const string BasePath = NetworkCombatSetupUtility.ProductEnemyPrefabPath;
        public const string SkeletonPath = EnemySimulationPrefabMigrator.SkeletonNetworkPrefabPath;
        public const string ExamplePath = "Assets/_Project/Content/NetworkCombat/NetworkEnemySkeletonExample.prefab";
        private const string ReportFolder = "Logs/EnemyVariants";


        public static void Migrate()
        {
            MonsterSupergroup.EditorTools.ProjectToolRunner.CheckLegacyMaintenance("migrate.enemy-variants", "MonsterSupergroup.NetworkCombat.Editor.EnemyPrefabVariantMigration.Migrate");
            // Scene setup restoration cannot restore unsaved scene contents.
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scene edits before migrating enemy Prefabs.");
            Directory.CreateDirectory(ReportFolder);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                bool convert = !IsDirectVariant(SkeletonPath, BasePath);
                if (convert && !File.Exists(ReportFolder + "/prefabs-before.json"))
                    File.WriteAllText(ReportFolder + "/prefabs-before.json", CaptureBaseline());
                PrepareBase();
                if (convert) ConvertSkeleton();
                CreateExample();
                foreach (string path in EnemyVariantPaths()) RepairMissingBindings(path);
                RegisterInScenes();
                Validate();
                File.WriteAllText(ReportFolder + "/prefabs-after.json", CaptureBaseline());
                Debug.Log("[EnemyVariants] migration and reference validation PASS");
            }
            finally
            {
                if (setup.Length > 0 && setup.Any(s => s.isLoaded && s.isActive))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        public static bool IsDirectVariant(string path, string parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return asset != null && PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant &&
                AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(asset)) == parent;
        }

        private static void PrepareBase()
        {
            var root = PrefabUtility.LoadPrefabContents(BasePath);
            try
            {
                var contact = root.GetComponent<EnemyContactDamage>();
                if (contact != null) return;
                var thorns = root.GetComponent<ThornsEnemyAttack>();
                if (thorns == null || thorns.damageInteraction == null)
                    throw new InvalidOperationException("Base contact migration requires the original Thorns binding.");
                contact = root.AddComponent<EnemyContactDamage>();
                contact.Configure(thorns.damageInteraction, true);
                root.GetComponent<EnemyController>().attackScript = null;
                Object.DestroyImmediate(thorns);
                Save(root, BasePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConvertSkeleton()
        {
            var references = CaptureReferences(SkeletonPath);
            var source = PrefabUtility.LoadPrefabContents(SkeletonPath);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject target = null;
            try
            {
                target = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(BasePath), scene);
                CopyHierarchyDifferences(source, target);

                // The old Skeleton had no contact geometry. Inheriting Base must not add damage.
                var contact = target.GetComponent<EnemyContactDamage>();
                contact.Configure(contact.DamageInteraction, false);
                // Keep the inherited direct binding instead of the old lazy null fallback.
                var controller = new SerializedObject(target.GetComponent<EnemyController>());
                controller.FindProperty("combatantBinding").objectReferenceValue = target.GetComponent<EnemyCombatantBinding>();
                controller.ApplyModifiedPropertiesWithoutUndo();
                // Unload the old asset contents before replacing that asset. Reimporting
                // a live contents scene with a different Prefab ancestry invalidates it.
                PrefabUtility.UnloadPrefabContents(source);
                source = null;
                Save(target, SkeletonPath);
            }
            catch (Exception exception) { Debug.LogException(exception); throw; }
            finally
            {
                if (target != null) Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
                if (source != null) PrefabUtility.UnloadPrefabContents(source);
            }
            RepairReferences(references);
        }

        private static void CopyHierarchyDifferences(GameObject source, GameObject target)
        {
            var map = new Dictionary<Object, Object>();
            foreach (var from in source.GetComponentsInChildren<Transform>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(from, source.transform);
                Transform to = path.Length == 0 ? target.transform : target.transform.Find(path);
                if (to == null)
                {
                    to = new GameObject(from.name).transform;
                    to.SetParent((Transform)map[from.parent], false);
                }
                map[from] = to;
                map[from.gameObject] = to.gameObject;
                to.name = from.name;
                to.localPosition = from.localPosition;
                to.localRotation = from.localRotation;
                to.localScale = from.localScale;
                to.gameObject.layer = from.gameObject.layer;
                to.gameObject.tag = from.gameObject.tag;
                to.gameObject.SetActive(from.gameObject.activeSelf);
                GameObjectUtility.SetStaticEditorFlags(to.gameObject, GameObjectUtility.GetStaticEditorFlags(from.gameObject));
                var counts = new Dictionary<Type, int>();
                foreach (var component in from.GetComponents<Component>())
                {
                    if (component == null) throw new InvalidOperationException("Missing source Skeleton component.");
                    if (component is Transform) continue;
                    var type = component.GetType();
                    counts.TryGetValue(type, out int index);
                    var existing = to.GetComponents(type);
                    map[component] = existing.Length > index ? existing[index] : to.gameObject.AddComponent(type);
                    counts[type] = index + 1;
                }
            }
            foreach (var pair in map)
                if (pair.Key is Component && !(pair.Key is Transform))
                    CopyDifferences(pair.Key, pair.Value, map);
        }

        private static void CopyDifferences(Object from, Object to, Dictionary<Object, Object> map)
        {
            var input = new SerializedObject(from);
            var output = new SerializedObject(to);
            var property = input.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = false;
                string path = property.propertyPath;
                if (path == "m_Script" || path == "m_GameObject" || path == "m_ObjectHideFlags" ||
                    path == "m_CorrespondingSourceObject" || path.StartsWith("m_Prefab") || path == "m_EditorClassIdentifier") continue;
                var destination = output.FindProperty(path);
                if (destination == null) throw new InvalidOperationException($"Cannot map {from.GetType().Name}.{path}");
                if (property.isArray && property.propertyType != SerializedPropertyType.String)
                {
                    if (destination.arraySize != property.arraySize) destination.arraySize = property.arraySize;
                    enterChildren = true;
                    continue;
                }
                if (property.propertyType == SerializedPropertyType.Generic) { enterChildren = true; continue; }
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var value = property.objectReferenceValue;
                    if (value != null && map.TryGetValue(value, out var mapped)) value = mapped;
                    if (destination.objectReferenceValue != value) destination.objectReferenceValue = value;
                }
                else output.CopyFromSerializedPropertyIfDifferent(property);
            }
            output.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateExample()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ExamplePath) != null) return;
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonPath));
            try
            {
                root.name = "NetworkEnemySkeletonExample";
                var controller = new SerializedObject(root.GetComponent<EnemyController>());
                controller.FindProperty("stats.baseStats.hp").intValue = 2400;
                controller.ApplyModifiedPropertiesWithoutUndo();
                root.transform.Find("Sprite").localScale *= 1.1f;
                Save(root, ExamplePath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void RepairMissingBindings(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var controller = root.GetComponent<EnemyController>();
                var serialized = new SerializedObject(controller);
                bool changed = false;
                void BindMissing(string field, Object candidate)
                {
                    var property = serialized.FindProperty(field);
                    if (property.objectReferenceValue != null || candidate == null) return;
                    property.objectReferenceValue = candidate;
                    changed = true;
                }
                BindMissing("combatantBinding", root.GetComponent<EnemyCombatantBinding>());
                BindMissing("status", root.GetComponent<EnemyStatus>());
                BindMissing("rigidBody", root.GetComponent<Rigidbody2D>());
                BindMissing("enemyAnimator", root.GetComponentInChildren<EnemyAnimator>(true));
                BindMissing("spriteRenderer", root.transform.Find("Sprite")?.GetComponent<SpriteRenderer>());
                BindMissing("defaultMovement", root.GetComponentInChildren<EnemyDefaultMovement>(true));
                if (controller.usesPathfinding) BindMissing("aILerpMovement", root.GetComponentInChildren<EnemyAILerpMovement>(true));
                BindMissing("collider", root.transform.Find("Collider")?.GetComponent<Collider2D>());
                BindMissing("hurtBox", root.GetComponentInChildren<EnemyHurtbox>(true));
                if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
                var contact = root.GetComponent<EnemyContactDamage>();
                if (contact != null && contact.DamageInteraction == null)
                {
                    var interaction = root.transform.Find("AttackCollider")?.GetComponent<PlayerDamageInteraction>();
                    if (interaction == null) throw new InvalidOperationException("Missing contact geometry: " + path);
                    contact.Configure(interaction, contact.ContactEnabled);
                    changed = true;
                }
                if (changed) Save(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void Save(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success) throw new InvalidOperationException("Cannot save " + path);
            // A Variant's inherited NetworkIdentity still needs its own spawn asset ID.
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var identity = new SerializedObject(asset.GetComponent<NetworkIdentity>());
            identity.FindProperty("_assetId").longValue = NetworkIdentity.AssetGuidToUint(
                Guid.Parse(AssetDatabase.AssetPathToGUID(path)));
            identity.FindProperty("sceneId").longValue = 0;
            identity.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(asset);
        }

        [Serializable] private class ReferenceRecord
        {
            public string asset, owner, property, target;
        }
        [Serializable] private class ReferenceReport { public List<ReferenceRecord> references; }

        private static string Key(Object value, GameObject root)
        {
            var go = value as GameObject;
            var component = value as Component;
            if (component != null) go = component.gameObject;
            if (go == null) throw new InvalidOperationException("Unexpected Prefab reference " + value);
            string path = AnimationUtility.CalculateTransformPath(go.transform, root.transform);
            return component == null ? path + "|GameObject" : path + "|" + component.GetType().AssemblyQualifiedName +
                "|" + Array.IndexOf(go.GetComponents(component.GetType()), component);
        }

        private static Object Resolve(string key, GameObject root)
        {
            var parts = key.Split('|');
            var node = parts[0].Length == 0 ? root.transform : root.transform.Find(parts[0]);
            if (node == null) throw new InvalidOperationException("Missing mapped node " + key);
            return parts[1] == "GameObject" ? (Object)node.gameObject : node.GetComponents(Type.GetType(parts[1], true))[int.Parse(parts[2])];
        }

        private static List<ReferenceRecord> CaptureReferences(string targetPath)
        {
            var records = new List<ReferenceRecord>();
            string guid = AssetDatabase.AssetPathToGUID(targetPath);
            var target = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/") || path == targetPath ||
                    !(path.EndsWith(".unity") || path.EndsWith(".prefab") || path.EndsWith(".asset")) ||
                    !File.ReadAllText(path).Contains(guid)) continue;
                VisitObjects(path, (owner, root) =>
                {
                    var serialized = new SerializedObject(owner);
                    var p = serialized.GetIterator();
                    while (p.Next(true))
                    {
                        if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null ||
                            AssetDatabase.GetAssetPath(p.objectReferenceValue) != targetPath) continue;
                        records.Add(new ReferenceRecord { asset = path, owner = root != null ? root.name + "::" + Key(owner, root) : owner.name,
                            property = p.propertyPath, target = Key(p.objectReferenceValue, target) });
                    }
                    return false;
                });
            }
            File.WriteAllText(ReportFolder + "/references.json", JsonUtility.ToJson(new ReferenceReport { references = records }, true));
            return records;
        }

        private static void RepairReferences(List<ReferenceRecord> records)
        {
            var target = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonPath);
            foreach (var group in records.GroupBy(r => r.asset))
                VisitObjects(group.Key, (owner, root) =>
                {
                    string key = root != null ? root.name + "::" + Key(owner, root) : owner.name;
                    bool changed = false;
                    var serialized = new SerializedObject(owner);
                    foreach (var record in group.Where(r => r.owner == key))
                    {
                        var p = serialized.FindProperty(record.property);
                        if (p == null) throw new InvalidOperationException("Missing reference field " + record.property);
                        var expected = Resolve(record.target, target);
                        if (p.objectReferenceValue != expected) { p.objectReferenceValue = expected; changed = true; }
                    }
                    if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
                    return changed;
                });
        }

        private static void VisitObjects(string path, Func<Object, GameObject, bool> visit)
        {
            if (path.EndsWith(".unity"))
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool changed = false;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                        if (component != null) changed |= visit(component, root);
                if (changed) EditorSceneManager.SaveScene(scene);
            }
            else if (path.EndsWith(".prefab"))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                        if (component != null) changed |= visit(component, root);
                    if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            else
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset != null && visit(asset, null))
                    {
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                    }
            }
        }

        private static void RegisterInScenes()
        {
            foreach (string path in new[] { NetworkCombatSetupUtility.BootScenePath, NetworkCombatSetupUtility.SandboxScenePath })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool changed = false;
                foreach (var manager in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkManager>(true)))
                    foreach (string assetPath in EnemyVariantPaths())
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        if (prefab == null || manager.spawnPrefabs.Contains(prefab)) continue;
                        manager.spawnPrefabs.Add(prefab); changed = true;
                    }
                if (changed) EditorSceneManager.SaveScene(scene);
            }
        }

        public static void Validate()
        {
            if (!IsDirectVariant(SkeletonPath, BasePath) || !IsDirectVariant(ExamplePath, SkeletonPath))
                throw new InvalidOperationException("Enemy Prefab Variant parent mismatch.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LustSinnerPath) != null && !IsDirectVariant(LustSinnerPath, BasePath))
                throw new InvalidOperationException("LustSinner must directly inherit Base.");
            var ids = new HashSet<uint>();
            foreach (string path in EnemyVariantPaths())
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root.GetComponentsInChildren<Component>(true).Any(c => c == null) ||
                    root.GetComponentsInChildren<NetworkIdentity>(true).Length != 1 ||
                    root.GetComponentsInChildren<EnemyController>(true).Length != 1 ||
                    root.GetComponentsInChildren<MonsterSupergroup.Gameplay.Combat.CombatantBehaviour>(true).Length != 1)
                    throw new InvalidOperationException("Missing or duplicate enemy component: " + path);
                uint id = root.GetComponent<NetworkIdentity>().assetId;
                if (id != NetworkIdentity.AssetGuidToUint(Guid.Parse(AssetDatabase.AssetPathToGUID(path))) || !ids.Add(id))
                    throw new InvalidOperationException("Enemy assetId mismatch: " + path);
            }
        }

        [Serializable] private class Baseline
        {
            public string path, guid, parent;
            public long rootFileId;
            public uint assetId;
            public string controller;
            public float warning, active, recovery;
            public bool movementOnly, contact;
        }
        [Serializable] private class Baselines { public List<Baseline> prefabs = new List<Baseline>(); }
        private static string CaptureBaseline()
        {
            var result = new Baselines();
            foreach (string path in EnemyVariantPaths())
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;
                var controller = root.GetComponent<EnemyController>();
                var attack = controller.attackScript;
                if (attack != null) attack.enemyAnimator = controller.enemyAnimator;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(root, out string guid, out long fileId);
                result.prefabs.Add(new Baseline { path = path, guid = guid, rootFileId = fileId,
                    parent = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(root)),
                    assetId = root.GetComponent<NetworkIdentity>().assetId, controller = EditorJsonUtility.ToJson(controller),
                    warning = attack != null ? attack.WarningTime : 0, active = attack != null ? attack.AttackTime : 0,
                    recovery = attack != null ? attack.RecoveryTime : 0,
                    movementOnly = root.GetComponent<NetworkEnemySimulationAgent>().ProductMovementOnly,
                    contact = root.GetComponent<EnemyContactDamage>()?.ContactEnabled ?? root.GetComponent<ThornsEnemyAttack>() != null });
            }
            return JsonUtility.ToJson(result, true);
        }
    }
}
