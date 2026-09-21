using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.Gameplay.Combat.Content;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class EnemyDefinitionEditorUtility
    {
        public static EnemyDefinitionCatalog Catalog => AssetDatabase.LoadAssetAtPath<EnemyDefinitionCatalog>(EnemyDefinitionMigration.CatalogPath);
        public static EnemyDefinition[] Choices() => Catalog != null
            ? Catalog.Definitions.Where(d => d != null).OrderBy(d => d.DisplayName, StringComparer.Ordinal).ToArray()
            : Array.Empty<EnemyDefinition>();
        public static void AddToCatalog(EnemyDefinition definition)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before changing enemy content.");
            if (Catalog == null) throw new InvalidOperationException("Create/migrate the Boot enemy catalog first.");
            if (Catalog.Definitions.Contains(definition)) return;
            Undo.RecordObject(Catalog, "Register enemy definition");
            Catalog.SetAuthoringDefinitions(Catalog.Definitions.Where(d => d != null).Append(definition));
            EditorUtility.SetDirty(Catalog); AssetDatabase.SaveAssetIfDirty(Catalog);
        }
        public static EnemyDefinition CreateDefinition(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before creating enemy content.");
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new ArgumentException("Asset path already exists: " + path);
            string prefix = path.Substring(0, path.Length - ".asset".Length);
            var stats = ScriptableObject.CreateInstance<EnemyStatsDefinition>();
            var appearance = ScriptableObject.CreateInstance<EnemyAppearanceDefinition>();
            var definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            AssetDatabase.CreateAsset(stats, AssetDatabase.GenerateUniqueAssetPath(prefix + "_Stats.asset"));
            AssetDatabase.CreateAsset(appearance, AssetDatabase.GenerateUniqueAssetPath(prefix + "_Appearance.asset"));
            definition.InitializeAuthoring(null, stats, appearance, Path.GetFileNameWithoutExtension(path));
            AssetDatabase.CreateAsset(definition, path); AssetDatabase.SaveAssetIfDirty(definition); AddToCatalog(definition);
            Undo.RegisterCreatedObjectUndo(definition, "Create enemy definition"); return definition;
        }
        public static EnemyDefinition DuplicateDefinition(EnemyDefinition source, string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before duplicating enemy content.");
            if (source == null || AssetDatabase.LoadMainAssetAtPath(path) != null) throw new ArgumentException("Select a source and a new asset path.");
            var clone = UnityEngine.Object.Instantiate(source); clone.name = Path.GetFileNameWithoutExtension(path);
            clone.AssignNewIdentityForCopy(); clone.InitializeAuthoring(clone.Prefab, clone.Stats, clone.Appearance, clone.name, clone.LegacyEnemyName, clone.LegacyVariant); AssetDatabase.CreateAsset(clone, path); AssetDatabase.SaveAssetIfDirty(clone); AddToCatalog(clone); return clone;
        }
        [MenuItem("Tools/MonsterSupergroup/Enemies/Create Enemy Definition")]
        public static void CreateFromMenu()
        {
            if (Catalog == null) { EditorUtility.DisplayDialog("Enemy Catalog", "Run Migration Preview / Apply first.", "OK"); return; }
            string path = EditorUtility.SaveFilePanelInProject("New Enemy Definition", "NewEnemy", "asset", "Choose a definition asset name.", EnemyDefinitionMigration.Root + "/Definitions");
            if (!string.IsNullOrEmpty(path)) Selection.activeObject = CreateDefinition(path);
        }
        [MenuItem("Tools/MonsterSupergroup/Enemies/Refresh Catalog")]
        public static void RefreshCatalog()
        {
            if (Catalog == null) throw new InvalidOperationException("Enemy catalog is missing.");
            Undo.RecordObject(Catalog, "Refresh enemy catalog");
            Catalog.SetAuthoringDefinitions(EnemyDefinitionMigration.Find<EnemyDefinition>());
            EditorUtility.SetDirty(Catalog); AssetDatabase.SaveAssetIfDirty(Catalog); RefreshContentHashes();
        }
        public static void RefreshContentHashes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Catalog == null) return;
            foreach (var appearance in Catalog.Definitions.Where(d => d != null && d.Appearance != null).Select(d => d.Appearance).Distinct())
            {
                var textures = appearance.TextureMappings.SelectMany(m => new[] { m.original, m.baked }).Append(appearance.Lut).Where(t => t != null).Distinct();
                string hash = EnemyDefinitionMigration.Hash(string.Join("|", textures.Select(t => AssetDatabase.GetAssetPath(t))
                    .OrderBy(p => p, StringComparer.Ordinal).Select(p => AssetDatabase.AssetPathToGUID(p) + ":" + AssetDatabase.GetAssetDependencyHash(p))));
                if (appearance.ContentHash == hash) continue;
                appearance.SetContentHash(hash); EditorUtility.SetDirty(appearance); AssetDatabase.SaveAssetIfDirty(appearance);
            }
            string catalogHash = EnemyDefinitionMigration.Hash(string.Join("|", Catalog.Definitions.Where(d => d != null)
                .OrderBy(d => d.IdText, StringComparer.Ordinal).Select(d => d.IdText + ":" + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(d)))));
            if (Catalog.ContentHash != catalogHash)
            { Catalog.SetContentHash(catalogHash); EditorUtility.SetDirty(Catalog); AssetDatabase.SaveAssetIfDirty(Catalog); }
        }
        public static void ValidateDefinition(EnemyDefinition definition, bool inspectAnimations = true)
        {
            var captured = definition.Capture(); var prefab = captured.Prefab;
            if (!EditorUtility.IsPersistent(prefab) || prefab.GetComponentsInChildren<NetworkIdentity>(true).Length != 1 ||
                prefab.GetComponent<NetworkIdentity>() == null || prefab.GetComponent<NetworkIdentity>().assetId == 0 ||
                prefab.GetComponent<NetworkEnemySimulationAgent>() == null ||
                prefab.GetComponent<EnemyController>() is not EnemyController enemy || enemy.collider is not CircleCollider2D)
                throw new ArgumentException("Enemy definition requires a registered-ready network Prefab with one identity and a circle body: " + definition.name);
            if (!captured.Appearance.UsesPalette) return;
            if (enemy.enemyAnimator == null || enemy.enemyAnimator.PaletteSwapper == null)
                throw new ArgumentException("Palette appearance requires a body PaletteSwapper: " + definition.name);
            var textures = new HashSet<Texture2D>();
            if (enemy.spriteRenderer != null && enemy.spriteRenderer.sprite != null) textures.Add(enemy.spriteRenderer.sprite.texture);
            if (inspectAnimations)
            {
                var iterator = new SerializedObject(enemy.enemyAnimator).GetIterator();
                while (iterator.Next(true))
                {
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference || iterator.propertyPath.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        iterator.objectReferenceValue is not AnimationClip animation) continue;
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(animation))
                        if (binding.type == typeof(SpriteRenderer))
                            foreach (var key in AnimationUtility.GetObjectReferenceCurve(animation, binding))
                                if (key.value is Sprite sprite) textures.Add(sprite.texture);
                }
            }
            foreach (var texture in textures)
                if (captured.Appearance.FindBaked(texture) == null)
                    throw new ArgumentException("Missing baked animation atlas: " + definition.name + " / " + texture.name);
        }
        [MenuItem("Tools/MonsterSupergroup/Enemies/Validate All %&F9")]
        public static void ValidateAll()
        {
            Directory.CreateDirectory(EnemyDefinitionMigration.ReportRoot);
            try
            {
                if (Catalog == null) throw new ArgumentException("Boot enemy catalog is missing.");
                var duplicateIds = EnemyDefinitionMigration.Find<EnemyDefinition>().GroupBy(d => d.Id).FirstOrDefault(g => g.Count() > 1);
                if (duplicateIds != null) throw new ArgumentException("Duplicate DefinitionId assets: " + string.Join(", ", duplicateIds.Select(AssetDatabase.GetAssetPath)));
                var registry = Catalog.Capture(); int clips = 0;
                foreach (var definition in Catalog.Definitions) ValidateDefinition(definition);
                foreach (var timeline in EnemyDefinitionMigration.Find<TimelineAsset>())
                    foreach (var clip in EnemyDefinitionMigration.Clips(timeline))
                    {
                        if (clip.asset is not NetworkEnemySpawnClip spawn) continue;
                        if (spawn.AuthoringVersion != 1 || spawn.Enemy == null || !registry.Contains(spawn.Enemy.Id))
                            throw new ArgumentException("Choose a catalog Enemy Definition: " + AssetDatabase.GetAssetPath(timeline) + "/" + clip.displayName);
                        clips++;
                    }
                foreach (var rules in EnemyDefinitionMigration.Find<GameplayWaveRules>())
                    if (!rules.TryCapture(out _, out var error)) throw new ArgumentException(AssetDatabase.GetAssetPath(rules) + ": " + error);
                string report = "PASS: " + Catalog.Definitions.Count + " definitions, " + clips + " clips. Fingerprint=" + registry.Fingerprint;
                File.WriteAllText(EnemyDefinitionMigration.ReportRoot + "/validation.txt", report); Debug.Log("[EnemyDefinitions] " + report);
            }
            catch (Exception error) { File.WriteAllText(EnemyDefinitionMigration.ReportRoot + "/validation.txt", "FAIL\n" + error); throw; }
        }
        public static void OpenFirstClip(string timelinePath)
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            if (timeline == null) throw new ArgumentException("Timeline is missing: " + timelinePath);
            AssetDatabase.OpenAsset(timeline); Selection.activeObject = EnemyDefinitionMigration.Clips(timeline).First().asset;
        }
    }
    public sealed class EnemyDefinitionBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(BuildReport report)
        {
            bool anyMigrated = EnemyDefinitionMigration.Find<TimelineAsset>().SelectMany(EnemyDefinitionMigration.Clips)
                .Any(c => c.asset is NetworkEnemySpawnClip spawn && spawn.AuthoringVersion == 1);
            if (anyMigrated || EnemyDefinitionEditorUtility.Catalog != null) EnemyDefinitionEditorUtility.ValidateAll();
        }
    }
    public sealed class EnemyDefinitionImportMetadata : AssetPostprocessor
    {
        private static bool queued;
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] oldPaths)
        {
            if (queued || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!imported.Concat(moved).Any(p => p.StartsWith("Assets/_Project/", StringComparison.Ordinal) &&
                p != EnemyDefinitionMigration.CatalogPath && (p.EndsWith(".asset") || p.EndsWith(".prefab") || p.EndsWith(".png")))) return;
            queued = true;
            EditorApplication.delayCall += () => { queued = false; EnemyDefinitionEditorUtility.RefreshContentHashes(); };
        }
    }
}
