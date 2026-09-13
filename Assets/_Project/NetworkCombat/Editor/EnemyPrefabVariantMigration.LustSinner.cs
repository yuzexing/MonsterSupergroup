using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using AstralShift.Rendering;
using Mirror;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static partial class EnemyPrefabVariantMigration
    {
        public const string LustSinnerPath = "Assets/_Project/Content/NetworkCombat/NetworkEnemyLustSinner.prefab";
        public const string LustResources = "Assets/_Project/Content/HellMaiden/Enemies/LustSinner";
        public const string LustWarningPath = LustResources + "/GameObject/LustSinner_Warning Variant.prefab";
        private const string LustSource = "F:/DecomplieLatest/HellMaiden/ExportedProject/Assets";
        private const string LustTemplate = LustResources + "/GameObject/Enemy_LustSinner.prefab";
        private const string LustReports = "Logs/LustSinner";
        private const string CompatibleShader = "Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader";
        private const string FlatteningShader = "Packages/com.unity.render-pipelines.universal/Shaders/2D/RenderAs2D-Flattening.shader";

        private static IEnumerable<string> EnemyVariantPaths()
        {
            yield return BasePath; yield return SkeletonPath; yield return ExamplePath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LustSinnerPath) != null) yield return LustSinnerPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ImpPath) != null) yield return ImpPath;
        }

        [MenuItem("Monster Supergroup/Network Combat/Migrate LustSinner Variant")]
        public static void MigrateLustSinner()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene edits before migrating enemy Prefabs.");
            Directory.CreateDirectory(LustReports);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(LustSinnerPath) == null)
                {
                    ImportLustResources();
                    CreateLustVariant();
                }
                RepairMissingBindings(LustSinnerPath);
                RepairLustBindings();
                RegisterInScenes();
                Validate();
                ValidateLustSinner();
                Debug.Log("[LustSinner] Variant migration PASS");
            }
            finally
            {
                if (setup.Any(s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static void ImportLustResources() => ImportEnemyResources(LustResources, "Enemy_LustSinner.prefab", "LustSinner_Warning Variant.prefab", "lustsinner_*.anim", LustReports);

        private static void ImportEnemyResources(string resources, string template, string attackResource, string clipPattern, string reports)
        {
            var sources = new Dictionary<string, string>();
            foreach (string meta in Directory.EnumerateFiles(LustSource, "*.meta", SearchOption.AllDirectories))
            {
                var match = Regex.Match(File.ReadAllText(meta), @"(?m)^guid: ([a-f0-9]{32})");
                if (match.Success) sources[match.Groups[1].Value] = meta.Substring(0, meta.Length - 5).Replace('\\', '/');
            }
            var scripts = MonoImporter.GetAllRuntimeMonoScripts().Where(s => s.GetClass() != null)
                .GroupBy(s => s.GetClass().FullName).ToDictionary(g => g.Key, g => g.First());
            var visited = new HashSet<string>();
            var imported = new List<string>();
            var mappings = new List<string>();
            string Script(string type)
            {
                if (!scripts.TryGetValue(type, out var script)) throw new InvalidOperationException("Missing installed script " + type);
                return "{fileID: 11500000, guid: " + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script)) + ", type: 3}";
            }
            string ScriptReference(Match match)
            {
                string guid = match.Groups[2].Value;
                string id = match.Groups[1].Value;
                if (!sources.TryGetValue(guid, out string source)) throw new InvalidOperationException("Unknown source script " + guid);
                string type;
                if (guid == "a764a8f1aa53ec5f484d2a941db13b66") type = "Animancer.AnimancerComponent";
                else if (guid == "a3825d626d49b4e8366365dfd9c12e64")
                    type = id == "1526319846" ? "Pathfinding.Seeker" : id == "1839522475" ? "Pathfinding.FunnelModifier" : throw new InvalidOperationException("Unknown Astar script " + id);
                else
                {
                    string code = File.ReadAllText(source);
                    var ns = Regex.Match(code, @"namespace\s+([\w.]+)");
                    type = (ns.Success ? ns.Groups[1].Value + "." : "") + Path.GetFileNameWithoutExtension(source);
                }
                string replacement = Script(type);
                mappings.Add(guid + ":" + id + " -> " + type + " " + replacement);
                return "m_Script: " + replacement;
            }
            void Import(string source)
            {
                string guid = Regex.Match(File.ReadAllText(source + ".meta"), @"(?m)^guid: ([a-f0-9]{32})").Groups[1].Value;
                if (!visited.Add(guid)) return;
                string existing = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(existing)) { mappings.Add(guid + " reused " + existing); return; }
                string relative = source.Substring(LustSource.Length + 1);
                string destination = resources + "/" + relative;
                if (File.Exists(destination)) return;
                string extension = Path.GetExtension(source);
                if (extension == ".cs" || extension == ".dll" || extension == ".shader")
                    throw new InvalidOperationException("Unmapped code/shader dependency " + source);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (new[] { ".prefab", ".asset", ".mat", ".anim", ".controller" }.Contains(extension))
                {
                    string yaml = File.ReadAllText(source);
                    // This hook only supported the absent custom renderer's particle matrices.
                    // The installed sprite shader uses the ParticleSystem's normal geometry.
                    var removed = new List<string>();
                    yaml = Regex.Replace(yaml, @"(?ms)^--- !u!114 &(-?\d+)\r?\n.*?(?=^--- !u!|\z)", m =>
                    {
                        if (!m.Value.Contains("cffbb4d90507e888f705c8dc76ba6e21")) return m.Value;
                        removed.Add(m.Groups[1].Value); return "";
                    });
                    foreach (string id in removed) yaml = Regex.Replace(yaml, @"(?m)^  - component: \{fileID: " + id + @"\}\r?\n", "");
                    yaml = Regex.Replace(yaml, @"m_Script: \{fileID: (-?\d+), guid: ([a-f0-9]{32}), type: 3\}", ScriptReference);
                    if (extension == ".prefab") yaml = Regex.Replace(yaml, @"m_Controller: \{fileID: [^}]+\}", "m_Controller: {fileID: 0}");
                    yaml = yaml.Replace("46152a5fac4bf96438fa5425c5038590", AssetDatabase.AssetPathToGUID(FlatteningShader));
                    foreach (string shader in new[] { "159c7e9144365ce4590110a4cad76836", "6950bdc7bd6d2644ab9ae800371622e6", "ddc0a7b0e4084284c859e167cee4d130" })
                        yaml = yaml.Replace(shader, AssetDatabase.AssetPathToGUID(CompatibleShader));
                    foreach (Match reference in Regex.Matches(yaml, @"guid: ([a-f0-9]{32})"))
                    {
                        string dependency = reference.Groups[1].Value;
                        if (dependency.StartsWith("0000000000000000") || !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(dependency))) continue;
                        if (!sources.TryGetValue(dependency, out string path)) throw new FileNotFoundException("Missing LustSinner dependency " + dependency);
                        Import(path);
                    }
                    File.WriteAllText(destination, yaml);
                }
                else File.Copy(source, destination, false);
                File.Copy(source + ".meta", destination + ".meta", false);
                imported.Add(destination);
                mappings.Add(guid + " imported " + destination);
            }
            Import(LustSource + "/GameObject/" + template);
            Import(LustSource + "/GameObject/" + attackResource);
            foreach (string path in Directory.GetFiles(LustSource + "/AnimationClip", clipPattern)) Import(path.Replace('\\', '/'));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in imported.Where(p => p.EndsWith(".mat")))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material.shader == AssetDatabase.LoadAssetAtPath<Shader>(FlatteningShader)) continue;
                material.shaderKeywords = Array.Empty<string>();
                material.SetFloat("_Alpha", 1); material.SetFloat("_Brightness", 0); material.SetFloat("_Contrast", 1);
                material.SetFloat("_MySrcMode", 5); material.SetFloat("_MyDstMode", path.Contains("Slash-v7") ? 1 : 10);
                material.SetFloat("_ZWrite", 0);
                if (path.EndsWith("/Enemy_Brotchi_lvl2.mat"))
                {
                    material.EnableKeyword("HITEFFECT_ON");
                    material.SetFloat("_HitEffectBlend", 0);
                }
                EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            }
            File.WriteAllLines(reports + "/resource-mapping.txt", mappings.Distinct().OrderBy(s => s));
        }

        private static void CreateLustVariant()
        {
            var source = PrefabUtility.LoadPrefabContents(LustTemplate);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject target = null;
            try
            {
                ConfigureLustAnimator(source, false);
                target = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath), scene);
                CopyHierarchyDifferences(source, target);
                target.name = "NetworkEnemyLustSinner";
                target.GetComponent<NetworkEnemySimulationAgent>().ConfigureProductSimulation(false);
                var contact = target.GetComponent<EnemyContactDamage>();
                contact.Configure(contact.DamageInteraction, false);
                var controller = new SerializedObject(target.GetComponent<EnemyController>());
                controller.FindProperty("combatantBinding").objectReferenceValue = target.GetComponent<EnemyCombatantBinding>();
                controller.FindProperty("obstaclesLayerMask").intValue = LayerMask.GetMask("Obstacles");
                controller.ApplyModifiedPropertiesWithoutUndo();
                target.transform.Find("Collider").gameObject.layer = LayerMask.NameToLayer("EnemyCollision");
                target.GetComponentInChildren<EnemyHurtbox>(true).gameObject.layer = LayerMask.NameToLayer("EnemyHitbox");
                target.AddComponent<NetworkEnemyMeleeReplica>();
                BindLustReplica(target);
                Save(target, LustSinnerPath);
            }
            finally
            {
                if (target != null) Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
                PrefabUtility.UnloadPrefabContents(source);
            }
        }

        private static bool ConfigureLustAnimator(GameObject root, bool onlyMissing)
        {
            var animator = root.GetComponentInChildren<EnemyAnimator>(true);
            var serialized = new SerializedObject(animator);
            void Bind(string field, Object value)
            {
                var p = serialized.FindProperty(field);
                if (!onlyMissing || p.objectReferenceValue == null) p.objectReferenceValue = value;
            }
            Bind("animancer", animator.GetComponents<MonoBehaviour>().First(c => c.GetType().FullName == "Animancer.AnimancerComponent"));
            Bind("animator", animator.GetComponent<Animator>());
            Bind("paletteSwapper", animator.GetComponent<SpriteRendererPaletteSwapper>());
            var renderers = serialized.FindProperty("renderers");
            if (!onlyMissing || renderers.arraySize == 0)
            {
                var all = root.GetComponentsInChildren<SpriteRenderer>(true);
                renderers.arraySize = all.Length;
                for (int i = 0; i < all.Length; i++) renderers.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
            }
            string[] fields = { "move", "attackWarning", "attack", "recovery", "hurt", "dead" };
            string[] names = { "walk", "Warning", "attack", "Recovery", "Hurt", "death" };
            for (int i = 0; i < fields.Length; i++)
                foreach (string side in new[] { "Left", "Right" })
                    foreach (string elevation in new[] { "Up", "Down" })
                    {
                        string path = LustResources + "/AnimationClip/lustsinner_" + names[i] + "_" + side.ToLowerInvariant() + ".anim";
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                        if (clip == null) throw new InvalidOperationException("Missing clip " + path);
                        Bind(fields[i] + side + elevation + "._Clip", clip);
                    }
            return serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool BindLustReplica(GameObject root)
        {
            var replica = new SerializedObject(root.GetComponent<NetworkEnemyMeleeReplica>());
            void Bind(string field, Object value)
            {
                var p = replica.FindProperty(field);
                if (p.objectReferenceValue == null) p.objectReferenceValue = value;
            }
            Bind("simulationAgent", root.GetComponent<NetworkEnemySimulationAgent>());
            Bind("simulationAuthority", root.GetComponent<EnemySimulationAuthority>());
            Bind("controller", root.GetComponent<EnemyController>());
            Bind("meleeAttack", root.GetComponent<EnemyAttackMelee>());
            return replica.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RepairLustBindings()
        {
            var flattened = AssetDatabase.LoadAssetAtPath<Material>(LustResources + "/Material/RenderAs2D-Flattening.mat");
            if (flattened != null && flattened.shader == AssetDatabase.LoadAssetAtPath<Shader>(CompatibleShader))
            {
                flattened.shader = AssetDatabase.LoadAssetAtPath<Shader>(FlatteningShader);
                EditorUtility.SetDirty(flattened); AssetDatabase.SaveAssetIfDirty(flattened);
            }
            // The installed shader needs its hit-effect variant in the player build.
            var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(LustResources + "/Material/Enemy_Brotchi_lvl2.mat");
            if (bodyMaterial != null && !bodyMaterial.IsKeywordEnabled("HITEFFECT_ON"))
            {
                bodyMaterial.EnableKeyword("HITEFFECT_ON");
                bodyMaterial.SetFloat("_HitEffectBlend", 0);
                EditorUtility.SetDirty(bodyMaterial); AssetDatabase.SaveAssetIfDirty(bodyMaterial);
            }
            var root = PrefabUtility.LoadPrefabContents(LustSinnerPath);
            try
            {
                bool changed = ConfigureLustAnimator(root, true);
                if (root.GetComponent<NetworkEnemyMeleeReplica>() == null) { root.AddComponent<NetworkEnemyMeleeReplica>(); changed = true; }
                changed |= BindLustReplica(root);
                if (changed) Save(root, LustSinnerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            root = PrefabUtility.LoadPrefabContents(LustWarningPath);
            try
            {
                var attack = root.GetComponent<EnemyAttackPrefab>();
                var serialized = new SerializedObject(attack);
                if (attack.damageInteraction == null) serialized.FindProperty("damageInteraction").objectReferenceValue = root.GetComponentInChildren<PlayerDamageInteraction>(true);
                if (attack.attackWarning == null) serialized.FindProperty("attackWarning").objectReferenceValue = root.GetComponentInChildren<EnemyAttackWarning>(true);
                bool changed = serialized.ApplyModifiedPropertiesWithoutUndo();
                foreach (var trigger in root.GetComponentsInChildren<AstralShift.QTI.Triggers.Physics2D.StepOn2DTrigger>(true))
                {
                    if (trigger.interaction == null) { trigger.interaction = attack.damageInteraction; changed = true; }
                    int mask = LayerMask.GetMask("PlayerHitbox");
                    if (trigger.layerMask != mask) { trigger.layerMask = mask; changed = true; }
                }
                // Keep the exported static warning shape. The similarly named Path clips
                // target an unrelated hierarchy and are deliberately not assigned.
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, LustWarningPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Serializable] private class LustReport
        {
            public string guid, parent;
            public uint assetId;
            public int health, damage;
            public float speed, xp, distance, cooldown, warning, active, recovery;
            public string[] overrides;
        }

        public static void ValidateLustSinner()
        {
            Validate();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(LustSinnerPath);
            var controller = root.GetComponent<EnemyController>();
            var attack = root.GetComponent<EnemyAttackMelee>();
            if (controller.attackScript != attack || attack.attackPrefab == null ||
                attack.attackPrefab.damageInteraction == null || attack.attackPrefab.attackWarning == null)
                throw new InvalidOperationException("LustSinner attack binding is missing.");
            foreach (string path in new[] { LustSinnerPath, LustWarningPath })
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Component>(true).Any(c => c == null))
                    throw new InvalidOperationException("Missing script in " + path);
            var animator = controller.enemyAnimator;
            var so = new SerializedObject(animator);
            foreach (string prefix in new[] { "move", "attackWarning", "attack", "recovery", "hurt", "dead" })
                foreach (string side in new[] { "LeftUp", "LeftDown", "RightUp", "RightDown" })
                {
                    var clip = (AnimationClip)so.FindProperty(prefix + side + "._Clip").objectReferenceValue;
                    if (clip == null) throw new InvalidOperationException("Missing LustSinner clip " + prefix + side);
                    foreach (var curve in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                        if (AnimationUtility.GetAnimatedObject(animator.gameObject, curve) == null)
                            throw new InvalidOperationException("Missing animated object " + clip.name + ":" + curve.path);
                }
            attack.enemyAnimator = animator;
            var stats = new SerializedObject(controller);
            var report = new LustReport {
                guid = AssetDatabase.AssetPathToGUID(LustSinnerPath), parent = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(root)),
                assetId = root.GetComponent<NetworkIdentity>().assetId,
                health = stats.FindProperty("stats.baseStats.hp").intValue, damage = stats.FindProperty("stats.baseStats.damage").intValue,
                speed = stats.FindProperty("stats.baseStats.speed").floatValue, xp = stats.FindProperty("stats.baseStats.xp").floatValue,
                distance = controller.attackDistance, cooldown = controller.attackCooldown,
                warning = attack.WarningTime, active = attack.AttackTime, recovery = attack.RecoveryTime,
                overrides = PrefabUtility.GetPropertyModifications(root).Select(p => p.propertyPath + " = " + (p.objectReference != null ? p.objectReference.name : p.value)).ToArray()
            };
            Directory.CreateDirectory(LustReports);
            File.WriteAllText(LustReports + "/prefab-report.json", JsonUtility.ToJson(report, true));
        }

        public static void BuildLustSinnerValidation()
        {
            MigrateLustSinner();
            const string output = "Builds/LustSinner/LustSinner.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { NetworkCombatSetupUtility.BootScenePath, NetworkCombatSetupUtility.GameplayScenePath },
                locationPathName = output, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.IncludeTestAssemblies,
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD", "MONSTER_MENU_VALIDATION" }
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("LustSinner build failed.");
        }
    }
}
