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
    public static class ManualPlayFixAssets
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
            var targets = new HashSet<string>(seeds);
            foreach (string seed in seeds)
                foreach (string dependency in AssetDatabase.GetDependencies(seed, true))
                    if (dependency.EndsWith(".prefab") && dependency.StartsWith("Assets/_Project/") && !dependency.Contains("/Art/Imported/")) targets.Add(dependency);
            Directory.CreateDirectory(Root + "/Materials"); AssetDatabase.Refresh();
            var report = new List<string>();
            foreach (string path in targets.OrderBy(p => p))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var materials = renderer.sharedMaterials;
                        for (int i = 0; i < materials.Length; i++)
                        {
                            var original = materials[i];
                            if (original == null || original.HasProperty("_GameplayPlanar")) continue;
                            string shader = original.shader.name == "AllIn1SpriteShader/AllIn1SpriteShader" ? "MonsterSupergroup/PlanarSprite" :
                                original.shader.name == "AllIn1Vfx/AllIn1VfxURPCompat" ? "MonsterSupergroup/PlanarVfx" : null;
                            if (shader == null) continue;
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
                        }
                        renderer.sharedMaterials = materials;
                    }
                    if (changed || root.GetComponentsInChildren<Renderer>(true).Any(GameplayPlanarEffect.UsesPlanarMaterial))
                    {
                        GameplayPlanarEffect.Attach(root);
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
            Debug.Log("Planar presentation adaptations: " + report.Count);
        }
    }
}
