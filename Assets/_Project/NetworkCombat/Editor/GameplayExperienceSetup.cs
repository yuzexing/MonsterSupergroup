using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mirror;
using MonsterSupergroup.Gameplay.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    /// <summary>Explicit M6 migration from the inspected export. Does not initialize the old game.</summary>
    public static class GameplayExperienceSetup
    {
        public const string RulesPath = "Assets/_Project/Content/NetworkCombat/GameplayExperienceRules.asset";
        public const string GemPath = "Assets/_Project/Content/NetworkCombat/NetworkExperienceGem.prefab";
        private static string Source => MonsterSupergroup.EditorTools.ProjectToolPaths.HellMaiden() + "/Assets";
        private const string Output = "Assets/_Project/Content/HellMaiden/NativeGAS/Experience";
        private const string Network = "Assets/_Project/Content/NetworkCombat/";
        private static readonly Dictionary<string, string> sourcePaths = new Dictionary<string, string>();

        internal static string ImportHealthVisual()
        {
            sourcePaths.Clear();
            foreach (string meta in Directory.EnumerateFiles(Source, "*.meta", SearchOption.AllDirectories))
            {
                var match = Regex.Match(File.ReadAllText(meta), @"(?m)^guid: ([a-f0-9]{32})");
                if (match.Success) sourcePaths[match.Groups[1].Value] = meta.Substring(0, meta.Length - 5);
            }
            ImportAsset(Path.Combine(Source, "GameObject/WorldItem_Health.prefab"), new HashSet<string>());
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return Output + "/GameObject/WorldItem_Health.prefab";
        }


        public static void Apply()
        {
            MonsterSupergroup.EditorTools.ProjectToolRunner.CheckLegacyMaintenance("setup.experience-legacy", "MonsterSupergroup.NetworkCombat.Editor.GameplayExperienceSetup.Apply");
            if (!Directory.Exists(Source)) throw new DirectoryNotFoundException(Source);
            sourcePaths.Clear();
            foreach (string meta in Directory.EnumerateFiles(Source, "*.meta", SearchOption.AllDirectories))
            {
                var match = Regex.Match(File.ReadAllText(meta), @"(?m)^guid: ([a-f0-9]{32})");
                if (match.Success) sourcePaths[match.Groups[1].Value] = meta.Substring(0, meta.Length - 5);
            }
            ImportAsset(Path.Combine(Source, "GameObject/XP_0.prefab"), new HashSet<string>());
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureRules();
            ConfigureGem();
            EditPrefab(Network + "NetworkCombatWorld.prefab", root =>
            {
                var component = root.GetComponent<NetworkExperienceWorld>() ?? root.AddComponent<NetworkExperienceWorld>();
                var so = new SerializedObject(component);
                so.FindProperty("rules").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameplayExperienceRules>(RulesPath);
                so.FindProperty("gemPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(GemPath).GetComponent<NetworkExperienceGem>();
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            EditPrefab(Network + "NetworkPlayer.prefab", root =>
            {
                if (root.GetComponent<NetworkExperienceCollector>() == null) root.AddComponent<NetworkExperienceCollector>();
            });
            EditPrefab("Assets/_Project/UI/CombatUI/CombatUI.prefab", root =>
            {
                var hud = root.GetComponentInChildren<CombatHUDController>(true);
                if (hud == null) throw new InvalidOperationException("Formal CombatHUD is missing.");
                var xp = hud.GetComponent<PlayerExperienceHUD>() ?? hud.gameObject.AddComponent<PlayerExperienceHUD>();
                var so = new SerializedObject(xp);
                so.FindProperty("fillTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/UI/CombatUI/XPBar/UI_XPBar_Fill.png");
                so.FindProperty("backgroundTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/UI/CombatUI/XPBar/UI_XPBar_Dark.png");
                so.ApplyModifiedPropertiesWithoutUndo();
                so = new SerializedObject(hud); so.FindProperty("experienceHUD").objectReferenceValue = xp;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Boot.unity", OpenSceneMode.Single);
            var manager = UnityEngine.Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GemPath);
            if (!manager.spawnPrefabs.Contains(prefab)) manager.spawnPrefabs.Add(prefab);
            EditorUtility.SetDirty(manager);
            EditorSceneManager.SaveScene(scene);
            FinalizePresentationAssets();
            AssetDatabase.SaveAssets();
            Debug.Log("[M6] Exact Systems XP curve, XP_0 visual, server World, Owner collector and CombatHUD connected.");
        }

        public static void EnsurePrefabIds()
        {
            foreach (string path in new[] { GemPath, Network + "NetworkPlayer.prefab", Network + "NetworkCombatWorld.prefab" })
            {
                var identity = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkIdentity>();
                if (identity.assetId == 0) throw new InvalidOperationException("Prefab assetId was not assigned: " + path);
                EditorUtility.SetDirty(identity);
            }
            AssetDatabase.SaveAssets();
        }

        public static void FinalizePresentationAssets()
        {
            EditPrefab(GemPath, root =>
            {
                // The exported XP_0 root was saved at (36.35, -24.06). Only child visual offsets are reusable.
                root.transform.GetChild(0).localPosition = Vector3.zero;
            });
            EditPrefab("Assets/_Project/UI/CombatUI/CombatUI.prefab", root =>
            {
                var so = new SerializedObject(root.GetComponentInChildren<PlayerExperienceHUD>(true));
                so.FindProperty("fillTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/UI/CombatUI/XPBar/UI_XPBar_Fill.png");
                so.FindProperty("backgroundTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/UI/CombatUI/XPBar/UI_XPBar_Dark.png");
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            var material = AssetDatabase.LoadAssetAtPath<Material>(Output + "/Material/XP_Mat.mat");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Output + "/Sprite/orb_0_0.asset");
            // SpriteRenderer supplies its atlas automatically; particle sprite sheets also need the shared atlas.
            material.mainTexture = sprite.texture;
            EditorUtility.SetDirty(material);
            EnsurePrefabIds();
        }

        private static void ConfigureRules()
        {
            string yaml = File.ReadAllText(Path.Combine(Source, "Scenes/Game Scenes/Systems.unity"));
            string curve = Regex.Match(yaml, @"(?ms)^  xpCurve:\r?\n(.*?)(?=^  [A-Za-z_])").Groups[1].Value;
            var keys = new List<Keyframe>();
            foreach (Match match in Regex.Matches(curve, @"time: ([^\r\n]+)\s+value: ([^\r\n]+)\s+inSlope: ([^\r\n]+)\s+outSlope: ([^\r\n]+)"))
            {
                float Read(int i) => float.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture);
                keys.Add(new Keyframe(Read(1), Read(2), Read(3), Read(4)));
            }
            if (keys.Count != 7) throw new InvalidOperationException("Expected the audited seven-key Systems xpCurve.");
            var rules = AssetDatabase.LoadAssetAtPath<GameplayExperienceRules>(RulesPath);
            if (rules == null) { rules = ScriptableObject.CreateInstance<GameplayExperienceRules>(); AssetDatabase.CreateAsset(rules, RulesPath); }
            var so = new SerializedObject(rules);
            so.FindProperty("xpCurve").animationCurveValue = new AnimationCurve(keys.ToArray());
            so.FindProperty("initialAccumulator").floatValue = 2;
            so.FindProperty("killWeight").floatValue = .5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (!rules.TryCapture(out _, out string error)) throw new InvalidOperationException(error);
        }

        private static void ConfigureGem()
        {
            // Imported XP_0 contains only renderers/Animator/particles. No XPGem, LootManager or minimap hooks.
            var root = new GameObject("NetworkExperienceGem", typeof(NetworkIdentity), typeof(NetworkExperienceGem));
            try
            {
                var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/GameObject/XP_0.prefab");
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab);
                visual.name = "XP_0 Visual"; visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                var so = new SerializedObject(root.GetComponent<NetworkExperienceGem>());
                so.FindProperty("visual").objectReferenceValue = visual.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, GemPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            // The export's custom URP shader is unavailable; retain textures/renderers with installed AllIn1.
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Output }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                material.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader");
                material.shaderKeywords = Array.Empty<string>();
                material.SetFloat("_Alpha", 1); material.SetFloat("_Brightness", 0); material.SetFloat("_Contrast", 1);
                material.SetFloat("_MySrcMode", 5); material.SetFloat("_MyDstMode", 10); material.SetFloat("_ZWrite", 0);
                EditorUtility.SetDirty(material);
            }
        }

        private static void ImportAsset(string source, HashSet<string> visited)
        {
            string guid = Regex.Match(File.ReadAllText(source + ".meta"), @"(?m)^guid: ([a-f0-9]{32})").Groups[1].Value;
            if (!visited.Add(guid) || !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid))) return;
            string relative = source.Substring(Source.Length + 1).Replace('\\', '/');
            string destination = Output + "/" + relative;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            bool text = new[] { ".prefab", ".asset", ".mat", ".anim", ".controller" }.Contains(Path.GetExtension(source));
            if (text)
            {
                string yaml = File.ReadAllText(source);
                if (relative == "GameObject/XP_0.prefab" || relative == "GameObject/WorldItem_Health.prefab")
                {
                    var removed = new List<string>();
                    yaml = Regex.Replace(yaml, @"(?ms)^--- !u!114 &(-?\d+)\r?\n.*?(?=^--- !u!|\z)", m =>
                    { removed.Add(m.Groups[1].Value); return string.Empty; });
                    foreach (string id in removed)
                        yaml = Regex.Replace(yaml, @"(?m)^  - component: \{fileID: " + id + @"\}\r?\n", "");
                }
                yaml = yaml.Replace("159c7e9144365ce4590110a4cad76836",
                    AssetDatabase.AssetPathToGUID("Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader"));
                foreach (Match match in Regex.Matches(yaml, @"guid: ([a-f0-9]{32})"))
                {
                    string dependency = match.Groups[1].Value;
                    if (dependency.StartsWith("0000000000000000") || !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(dependency))) continue;
                    if (!sourcePaths.TryGetValue(dependency, out string path)) throw new FileNotFoundException("Missing XP dependency " + dependency);
                    ImportAsset(path, visited);
                }
                File.WriteAllText(destination, yaml, new UTF8Encoding(false));
            }
            else File.Copy(source, destination, false);
            File.Copy(source + ".meta", destination + ".meta", false);
        }

        private static void EditPrefab(string path, Action<GameObject> edit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
