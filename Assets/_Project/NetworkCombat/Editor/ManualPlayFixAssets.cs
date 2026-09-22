using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonsterSupergroup.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    /// <summary>Targeted adaptation of already migrated effects; source art and combat data are untouched.</summary>
    public static partial class ManualPlayFixAssets
    {
        private const string Root = "Assets/_Project/Content/Rendering/Planar";
        public static void ApplyBatch()
        {
            int exit = 0;
            try { Apply(); }
            catch (Exception e) { Debug.LogException(e); exit = 1; }
            finally { EditorApplication.Exit(exit); }
        }
        public static void Apply()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var names = new HashSet<string> { "PlayerAttack_Dante_Slash", "PlayerAttack_Dante_Projectile", "PlayerAttack_Dante_Projectile_Fire Variant",
                "PlayerAttack_Dante_Projectile_Poison Variant", "SkeletonAttack", "Elite_SkeletonAttack", "ReferenceDashArrow",
                "ReferenceLostSoulExplosion", "ReferenceGhoulAttack", "soul enemy warning", "Ghoul_Warning", "Enemy_Bomb_ExplosionAttack 1", "ReferenceFireParticles" };
            var seeds = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => names.Contains(Path.GetFileNameWithoutExtension(p)) && !p.Contains("/Art/Imported/")).ToArray();
            var targets = new HashSet<string>(seeds.Concat(WeaponPrefabPaths()));
            foreach (string seed in seeds)
                foreach (string dependency in AssetDatabase.GetDependencies(seed, true))
                    if (dependency.EndsWith(".prefab") && dependency.StartsWith("Assets/_Project/") && !dependency.Contains("/Art/Imported/")) targets.Add(dependency);
            Directory.CreateDirectory(Root + "/Materials"); AssetDatabase.Refresh();
            var report = new List<string>();
            var coverage = new List<CoverageRow>();
            foreach (string path in targets.OrderBy(p => p))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    string gameplayBefore = GameplayFingerprint(root);
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                        if (!IsEffectRenderer(root, renderer))
                        {
                            coverage.Add(new CoverageRow { prefab = path, renderer = rendererPath, status = "body-preserved" });
                            continue;
                        }
                        var materials = renderer.sharedMaterials;
                        for (int i = 0; i < materials.Length; i++)
                        {
                            var original = materials[i];
                            if (original == null) continue; // Empty optional material slots have no vertices to render.
                            if (original.HasProperty("_GameplayPlanar"))
                            {
                                coverage.Add(new CoverageRow { prefab = path, renderer = rendererPath, slot = i, status = "already-planar", material = AssetDatabase.GetAssetPath(original), shader = original.shader.name });
                                continue;
                            }
                            string shader = PlanarShader(original.shader.name);
                            if (shader == null)
                            {
                                coverage.Add(new CoverageRow { prefab = path, renderer = rendererPath, slot = i, status = "unsupported", material = AssetDatabase.GetAssetPath(original), shader = original.shader.name });
                                continue;
                            }
                            string sourcePath = AssetDatabase.GetAssetPath(original);
                            string destination = Root + "/Materials/" + AssetDatabase.AssetPathToGUID(sourcePath) + ".mat";
                            var adapted = AssetDatabase.LoadAssetAtPath<Material>(destination);
                            if (adapted == null)
                            {
                                adapted = new Material(original) { name = original.name + " (XY)" };
                                adapted.shader = Shader.Find(shader) ?? throw new InvalidDataException(shader);
                                AssetDatabase.CreateAsset(adapted, destination);
                            }
                            materials[i] = adapted; changed = true;
                            report.Add(path + " | " + sourcePath + " -> " + destination);
                            coverage.Add(new CoverageRow { prefab = path, renderer = rendererPath, slot = i, status = "adapted", material = destination, source = sourcePath, shader = shader,
                                depthFeatures = string.Join(";", original.shaderKeywords.Where(k => k.Contains("SOFTPART") || k.Contains("DEPTH") || k.Contains("SCREENDISTORT"))) });
                        }
                        renderer.sharedMaterials = materials;
                    }
                    bool attach = root.GetComponent<GameplayPlanarEffect>() == null && root.GetComponentsInChildren<Renderer>(true).Any(GameplayPlanarEffect.UsesPlanarMaterial);
                    if (changed || attach)
                    {
                        GameplayPlanarEffect.Attach(root);
                        if (gameplayBefore != GameplayFingerprint(root)) throw new InvalidDataException("Planar adaptation changed gameplay/geometry: " + path);
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            // GPU timing is required for the new diagnostic build; no rendering quality change.
            PlayerSettings.enableFrameTimingStats = true;
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/ManualPlayFix");
            File.WriteAllLines("Logs/ManualPlayFix/planar-adaptations.txt", report);
            File.WriteAllText("Logs/ManualPlayFix/weapon-planar-coverage.json", JsonUtility.ToJson(new Coverage { rows = coverage.ToArray() }, true));
            Debug.Log("Planar presentation adaptations: " + report.Count);
        }
    }
}
