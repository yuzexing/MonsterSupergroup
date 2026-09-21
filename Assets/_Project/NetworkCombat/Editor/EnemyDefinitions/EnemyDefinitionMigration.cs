using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.Rendering;
using Mirror;
using MonsterSupergroup.Gameplay.Combat.Content;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class EnemyDefinitionMigration
    {
        public const string Root = "Assets/_Project/Content/NetworkCombat/EnemyDefinitions";
        public const string CatalogPath = Root + "/EnemyCatalog.asset";
        public const string ReportRoot = "Logs/EnemyDefinitions";
        private const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        [Serializable] public sealed class Report
        {
            public string inputDigest, startedUtc, backupDirectory, error;
            public bool applied;
            public Row[] rows;
        }
        [Serializable] public sealed class Row
        {
            public string timeline, track, clip, prefab, database, artDatabase, source, statsJson, timing, definitionPath, key, statsKey, appearanceKey;
            public int variant;
            public string variantName;
            public bool migrated;
            public string lut;
            public Mapping[] mappings;
            [NonSerialized] public NetworkEnemySpawnClip asset;
            [NonSerialized] public EnemyStatsValues values;
        }
        [Serializable] public sealed class Mapping { public string original, baked; }
        public static IEnumerable<TimelineClip> Clips(TimelineAsset timeline)
        {
            foreach (var root in timeline.GetRootTracks()) foreach (var clip in Clips(root)) yield return clip;
        }
        private static IEnumerable<TimelineClip> Clips(TrackAsset track)
        {
            if (track is NetworkEnemySpawnTrack) foreach (var clip in track.GetClips()) yield return clip;
            foreach (var child in track.GetChildTracks()) foreach (var clip in Clips(child)) yield return clip;
        }
        public static T[] Find<T>() where T : UnityEngine.Object => AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets/_Project" })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<T>).Where(x => x != null).ToArray();
        public static void EnsureLegacyWriterAllowed(string operation)
        {
            if (AssetDatabase.LoadAssetAtPath<EnemyDefinitionCatalog>(CatalogPath) != null ||
                Find<TimelineAsset>().SelectMany(Clips).Any(c => c.asset is NetworkEnemySpawnClip spawn && spawn.AuthoringVersion == 1))
                throw new InvalidOperationException(operation + " is a legacy import/restore writer and would overwrite migrated content. Edit Enemy Definitions or import into a separate source workspace instead.");
        }
        public static string Hash(string text)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        private static string GuidOf(UnityEngine.Object asset) => asset != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)) : "none";
        private static string Timing(TimelineClip clip, NetworkEnemySpawnClip spawn) =>
            FormattableString.Invariant($"{clip.start:R}|{clip.duration:R}|{clip.clipIn:R}|{clip.timeScale:R}|{spawn.count}|{(int)spawn.referenceMode}|{spawn.spawnCooldown:R}|{spawn.speedMultipliers.x:R}|{spawn.speedMultipliers.y:R}|{spawn.contactRadius:R}|{spawn.expiresOffscreen}|{spawn.resetOnReposition}|") +
            CurveSignature(spawn.spawnCurve) + "|" + spawn.referenceReadiness + "|" + spawn.referenceSpawnReadiness + "|" + spawn.missingEvidence + "|" + spawn.spawnReadinessNote;

        private static string CurveSignature(AnimationCurve curve) => curve == null ? "null" :
            curve.preWrapMode + "/" + curve.postWrapMode + "/" + string.Join(";", curve.keys.Select(k =>
                FormattableString.Invariant($"{k.time:R},{k.value:R},{k.inTangent:R},{k.outTangent:R},{k.inWeight:R},{k.outWeight:R},{(int)k.weightedMode}")));

        public static Report BuildPreview()
        {
            var rules = Find<GameplayWaveRules>();
            var rows = new List<Row>();
            var inputs = new SortedSet<string>(StringComparer.Ordinal) { BootPath };
            foreach (var rule in rules) inputs.Add(AssetDatabase.GetAssetPath(rule));
            var knownDatabases = rules.Where(r => r.IsReferenceStage).Select(r => new SerializedObject(r).FindProperty("referenceEnemies").objectReferenceValue as EnemyDatabase)
                .Where(d => d != null).Distinct().ToArray();
            foreach (var timeline in Find<TimelineAsset>())
            {
                var clips = Clips(timeline).Where(c => c.asset is NetworkEnemySpawnClip).ToArray();
                if (clips.Length == 0) continue;
                string timelinePath = AssetDatabase.GetAssetPath(timeline); inputs.Add(timelinePath);
                var owners = rules.Where(r => r.Timeline == timeline).ToArray();
                if (owners.Any(r => r.IsReferenceStage) && owners.Any(r => !r.IsReferenceStage))
                    throw new InvalidDataException("Timeline is used by both legacy normal and reference rules with different stat sources: " + timelinePath);
                var databases = owners.Where(r => r.IsReferenceStage).Select(r => new SerializedObject(r).FindProperty("referenceEnemies").objectReferenceValue as EnemyDatabase)
                    .Where(d => d != null).Distinct().ToArray();
                foreach (var clip in clips)
                {
                    var spawn = (NetworkEnemySpawnClip)clip.asset;
                    var row = new Row { timeline = timelinePath, track = clip.GetParentTrack().name, clip = clip.displayName, timing = Timing(clip, spawn),
                        source = spawn.sourceEnemy, variant = spawn.sourceVariant, asset = spawn, migrated = spawn.AuthoringVersion == 1 };
                    if (spawn.AuthoringVersion == 1)
                    {
                        if (spawn.Enemy == null) throw new InvalidDataException("Schema-1 clip has no Definition; never fall back to legacy: " + timelinePath + "/" + clip.displayName);
                        var current = spawn.Enemy.Capture(); row.prefab = AssetDatabase.GetAssetPath(current.Prefab); row.values = current.Values;
                        row.definitionPath = AssetDatabase.GetAssetPath(spawn.Enemy); row.key = spawn.Enemy.MigrationKey;
                        inputs.Add(row.definitionPath);
                        row.source = current.LegacyEnemyName; row.variant = current.LegacyVariant;
                        row.lut = AssetDatabase.GetAssetPath(current.Appearance.Lut);
                        row.mappings = current.Appearance.TextureMappings.Select(m => new Mapping { original = AssetDatabase.GetAssetPath(m.original), baked = AssetDatabase.GetAssetPath(m.baked) }).ToArray();
                    }
                    else
                    {
                        if (spawn.enemyPrefab == null) throw new InvalidDataException("Cannot migrate missing Prefab: " + timelinePath + "/" + clip.displayName);
                        var controller = spawn.enemyPrefab.GetComponent<EnemyController>();
                        if (controller == null) throw new InvalidDataException("Missing EnemyController: " + spawn.enemyPrefab.name);
                        row.prefab = AssetDatabase.GetAssetPath(spawn.enemyPrefab);
                        bool reference = owners.Length > 0 ? owners.Any(r => r.IsReferenceStage) : spawn.referenceMode != ReferenceSpawnMode.None;
                        EnemyStats stats;
                        if (reference)
                        {
                            var candidates = databases.Length == 0 ? knownDatabases : databases;
                            if (candidates.Length != 1) throw new InvalidDataException("Ambiguous enemy database for " + timelinePath + "; select a unique source before migration.");
                            var database = candidates[0]; row.database = AssetDatabase.GetAssetPath(database); inputs.Add(row.database);
                            var data = database.GetEnemyData(spawn.sourceEnemy, spawn.sourceVariant);
                            if (data == null) throw new InvalidDataException("Missing source variant " + spawn.sourceEnemy + "/" + spawn.sourceVariant + " in " + row.database);
                            row.variantName = data.variantName;
                            stats = data.Stats; stats.Reset();
                            row.statsKey = GuidOf(database) + "/" + row.source + "/" + row.variant;
                        }
                        else
                        {
                            stats = controller.stats.Clone(); stats.Reset(); row.source = controller.selectedName; row.variant = 0;
                            row.statsKey = "prefab/" + GuidOf(spawn.enemyPrefab);
                        }
                        row.values = new EnemyStatsValues { Health = stats.BaseHealth, Damage = stats.BaseDamage, Speed = stats.BaseSpeed,
                            XP = stats.BaseXP, KnockBackMultiplier = stats.KnockBackMultiplier, StunTime = stats.StunTime, WindMultiplier = stats.WindMultiplier };
                        var agent = spawn.enemyPrefab.GetComponent<NetworkEnemySimulationAgent>();
                        var artDatabase = agent != null ? new SerializedObject(agent).FindProperty("referenceArtDatabase")?.objectReferenceValue as EnemyDatabase : null;
                        var lut = reference && artDatabase != null ? artDatabase.GetEnemyData(row.source, row.variant)?.ColorLUT : null;
                        row.artDatabase = AssetDatabase.GetAssetPath(artDatabase);
                        if (artDatabase != null) inputs.Add(row.artDatabase);
                        row.lut = AssetDatabase.GetAssetPath(lut);
                        var mappings = new List<Mapping>();
                        if (lut != null)
                        {
                            var swapper = controller.enemyAnimator != null ? controller.enemyAnimator.PaletteSwapper : null;
                            if (swapper == null) throw new InvalidDataException("Authored LUT has no body PaletteSwapper: " + row.prefab);
                            var array = new SerializedObject(swapper).FindProperty("bakedPalettes");
                            for (int i = 0; i < array.arraySize; i++)
                            {
                                var item = array.GetArrayElementAtIndex(i);
                                if (item.FindPropertyRelative("lut").objectReferenceValue != lut) continue;
                                mappings.Add(new Mapping { original = AssetDatabase.GetAssetPath(item.FindPropertyRelative("original").objectReferenceValue),
                                    baked = AssetDatabase.GetAssetPath(item.FindPropertyRelative("baked").objectReferenceValue) });
                            }
                            if (mappings.Count == 0 || mappings.Any(m => string.IsNullOrEmpty(m.original) || string.IsNullOrEmpty(m.baked)))
                                throw new InvalidDataException("Missing baked appearance mapping: " + row.prefab + " / " + row.lut);
                        }
                        row.mappings = mappings.OrderBy(m => m.original, StringComparer.Ordinal).ToArray();
                        row.appearanceKey = Hash(row.lut + "|" + string.Join("|", row.mappings.Select(m => m.original + "=" + m.baked)));
                        row.key = Hash(GuidOf(spawn.enemyPrefab) + "|" + row.statsKey + "|" + row.appearanceKey);
                        string body = spawn.enemyPrefab.name.StartsWith("Reference") ? spawn.enemyPrefab.name.Substring(9) : spawn.enemyPrefab.name;
                        string variantLabel = string.IsNullOrWhiteSpace(row.variantName) ? "Variant" + row.variant : Safe(row.variantName);
                        var level = Regex.Match(variantLabel, @"^lvl(\d+)$", RegexOptions.IgnoreCase);
                        if (level.Success) variantLabel = "Level" + int.Parse(level.Groups[1].Value).ToString("00");
                        string label = reference ? body + "_" + variantLabel : body + "_Default";
                        var existing = Find<EnemyDefinition>().FirstOrDefault(d => d.MigrationKey == row.key);
                        row.definitionPath = existing != null ? AssetDatabase.GetAssetPath(existing) : Root + "/Definitions/" + Safe(label) + ".asset";
                    }
                    inputs.Add(row.prefab); if (!string.IsNullOrEmpty(row.lut)) inputs.Add(row.lut);
                    foreach (var mapping in row.mappings) { inputs.Add(mapping.original); inputs.Add(mapping.baked); }
                    row.statsJson = JsonUtility.ToJson(row.values); rows.Add(row);
                }
            }
            foreach (var group in rows.Where(r => !r.migrated).GroupBy(r => r.definitionPath).Where(g => g.Select(r => r.key).Distinct().Count() > 1))
                foreach (var row in group) row.definitionPath = row.definitionPath.Replace(".asset", "_" + row.key.Substring(0, 8) + ".asset");
            foreach (var row in rows.Where(r => !r.migrated))
            {
                var occupied = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(row.definitionPath);
                if (occupied != null && occupied.MigrationKey != row.key)
                    row.definitionPath = row.definitionPath.Replace(".asset", "_" + row.key.Substring(0, 8) + ".asset");
            }
            return new Report { startedUtc = DateTime.UtcNow.ToString("O"), rows = rows.ToArray(),
                inputDigest = Hash(string.Join("|", inputs.Select(p => p + ":" + AssetDatabase.GetAssetDependencyHash(p)))) };
        }
        private static string Safe(string value) => Regex.Replace(value ?? "Enemy", "[^a-zA-Z0-9_-]", "_");
        [MenuItem("Tools/MonsterSupergroup/Enemies/Migration Preview %&F10")]
        public static void Preview()
        {
            Directory.CreateDirectory(ReportRoot);
            try { var report = BuildPreview(); File.WriteAllText(ReportRoot + "/migration-preview.json", JsonUtility.ToJson(report, true));
                Debug.Log("[EnemyDefinitions] Preview: " + report.rows.Length + " clips; pending " + report.rows.Count(r => !r.migrated)); }
            catch (Exception error) { File.WriteAllText(ReportRoot + "/migration-preview-error.txt", error.ToString()); throw; }
        }
        [MenuItem("Tools/MonsterSupergroup/Enemies/Apply Reviewed Migration %&F11")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before migration.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save/close dirty scenes before migration.");
            string previewPath = ReportRoot + "/migration-preview.json";
            if (!File.Exists(previewPath)) throw new InvalidOperationException("Run Migration Preview and review it first.");
            var reviewed = JsonUtility.FromJson<Report>(File.ReadAllText(previewPath));
            var report = BuildPreview();
            if (report.inputDigest != reviewed.inputDigest) throw new InvalidOperationException("Migration inputs changed; run and review Preview again.");
            report.backupDirectory = ReportRoot + "/asset-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(report.backupDirectory);
            var changed = new HashSet<string>();
            var saveErrors = new List<string>();
            void ObserveSaveError(string condition, string stackTrace, LogType type)
            { if (type == LogType.Error || type == LogType.Exception) saveErrors.Add(condition); }
            void CheckSaveErrors()
            { if (saveErrors.Count > 0) throw new IOException("Unity reported a failed migration save: " + string.Join("; ", saveErrors)); }
            Application.logMessageReceived += ObserveSaveError;
            void Backup(string p)
            {
                if (string.IsNullOrEmpty(p) || !changed.Add(p) || !File.Exists(p)) return;
                string dest = Path.Combine(report.backupDirectory, p); Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.Copy(p, dest, false);
                if (File.Exists(p + ".meta")) File.Copy(p + ".meta", dest + ".meta", false);
            }
            try
            {
                var replacements = new Dictionary<NetworkEnemySpawnClip, EnemyDefinition>();
                var statsCache = new Dictionary<string, EnemyStatsDefinition>();
                var artCache = new Dictionary<string, EnemyAppearanceDefinition>();
                var catalog = AssetDatabase.LoadAssetAtPath<EnemyDefinitionCatalog>(CatalogPath);
                var all = catalog != null ? catalog.Definitions.Where(d => d != null).ToList() : new List<EnemyDefinition>();
                foreach (var row in report.rows.Where(r => !r.migrated))
                {
                    var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(row.definitionPath);
                    if (definition == null)
                    {
                        if (!statsCache.TryGetValue(row.statsKey, out var stats))
                        {
                            string p = Root + "/Stats/" + Safe(row.source) + "_" + row.variant + "_" + Hash(row.statsKey).Substring(0, 8) + ".asset";
                            stats = AssetDatabase.LoadAssetAtPath<EnemyStatsDefinition>(p);
                            if (stats == null) { stats = ScriptableObject.CreateInstance<EnemyStatsDefinition>(); stats.SetAuthoringValues(row.values); CreateAsset(stats, p); }
                            statsCache.Add(row.statsKey, stats);
                        }
                        if (!artCache.TryGetValue(row.appearanceKey, out var appearance))
                        {
                            string p = Root + "/Appearances/" + (string.IsNullOrEmpty(row.lut) ? "OriginalColor" : Path.GetFileNameWithoutExtension(row.lut) + "_" + row.appearanceKey.Substring(0, 8)) + ".asset";
                            appearance = AssetDatabase.LoadAssetAtPath<EnemyAppearanceDefinition>(p);
                            if (appearance == null)
                            {
                                appearance = ScriptableObject.CreateInstance<EnemyAppearanceDefinition>();
                                appearance.SetAuthoringValues(string.IsNullOrEmpty(row.lut) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(row.lut), row.mappings.Select(m => new EnemyAppearanceDefinition.TextureMapping {
                                    original = AssetDatabase.LoadAssetAtPath<Texture2D>(m.original), baked = AssetDatabase.LoadAssetAtPath<Texture2D>(m.baked) }).ToArray());
                                CreateAsset(appearance, p);
                            }
                            artCache.Add(row.appearanceKey, appearance);
                        }
                        definition = ScriptableObject.CreateInstance<EnemyDefinition>();
                        definition.InitializeAuthoring(AssetDatabase.LoadAssetAtPath<GameObject>(row.prefab), stats, appearance,
                            Path.GetFileNameWithoutExtension(row.definitionPath), row.source, row.variant, row.key);
                        CreateAsset(definition, row.definitionPath);
                    }
                    if (definition.MigrationKey != row.key) throw new InvalidDataException("Definition path conflict: " + row.definitionPath);
                    if (!all.Contains(definition)) all.Add(definition);
                    replacements.Add(row.asset, definition);
                    CheckSaveErrors();
                }
                // Create every definition first; otherwise CreateAsset can trigger an import
                // and an implicit save of a half-migrated multi-clip Timeline.
                foreach (var row in report.rows.Where(r => !r.migrated))
                {
                    Backup(row.timeline);
                    var serialized = new SerializedObject(row.asset);
                    serialized.FindProperty("enemy").objectReferenceValue = replacements[row.asset];
                    serialized.FindProperty("authoringVersion").intValue = 1;
                    serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(row.asset);
                }
                foreach (string path in report.rows.Where(r => !r.migrated).Select(r => r.timeline).Distinct())
                {
                    var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
                    EditorUtility.SetDirty(timeline); AssetDatabase.SaveAssetIfDirty(timeline); CheckSaveErrors();
                }
                foreach (var row in report.rows)
                {
                    var actual = row.asset.Enemy.Capture();
                    if (AssetDatabase.GetAssetPath(actual.Prefab) != row.prefab || JsonUtility.ToJson(actual.Values) != row.statsJson)
                        throw new InvalidDataException("Migration changed Prefab/base values: " + row.timeline + "/" + row.clip);
                    if (!all.Contains(row.asset.Enemy)) all.Add(row.asset.Enemy);
                    if (AssetDatabase.GetAssetPath(actual.Appearance.Lut) != row.lut ||
                        !actual.Appearance.TextureMappings.Select(m => AssetDatabase.GetAssetPath(m.original) + "=" + AssetDatabase.GetAssetPath(m.baked)).OrderBy(x => x, StringComparer.Ordinal)
                        .SequenceEqual(row.mappings.Select(m => m.original + "=" + m.baked).OrderBy(x => x, StringComparer.Ordinal)))
                        throw new InvalidDataException("Migration changed appearance: " + row.timeline + "/" + row.clip);
                    var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(row.timeline);
                    var actualClip = Clips(timeline).Single(c => c.asset == row.asset);
                    if (Timing(actualClip, row.asset) != row.timing)
                        throw new InvalidDataException("Migration changed spawn timing/policy: " + row.timeline + "/" + row.clip);
                }
                foreach (string p in report.rows.Select(r => r.prefab).Distinct())
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    var agent = prefab.GetComponent<NetworkEnemySimulationAgent>();
                    bool hasLegacy = agent != null && new SerializedObject(agent).FindProperty("referenceArtDatabase").objectReferenceValue != null;
                    hasLegacy |= prefab.GetComponentsInChildren<SpriteRendererPaletteSwapper>(true).Any(s => new SerializedObject(s).FindProperty("bakedPalettes").arraySize > 0);
                    if (!hasLegacy) continue;
                    Backup(p); var instance = PrefabUtility.LoadPrefabContents(p);
                    try
                    {
                        var a = instance.GetComponent<NetworkEnemySimulationAgent>();
                        if (a != null) { var so = new SerializedObject(a); so.FindProperty("referenceArtDatabase").objectReferenceValue = null; so.ApplyModifiedPropertiesWithoutUndo(); }
                        foreach (var s in instance.GetComponentsInChildren<SpriteRendererPaletteSwapper>(true))
                        { var so = new SerializedObject(s); so.FindProperty("bakedPalettes").arraySize = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
                        PrefabUtility.SaveAsPrefabAsset(instance, p, out bool saved);
                        if (!saved) throw new IOException("Could not save migrated Prefab: " + p);
                        CheckSaveErrors();
                    }
                    finally { PrefabUtility.UnloadPrefabContents(instance); }
                }
                Backup(CatalogPath);
                if (catalog == null) { catalog = ScriptableObject.CreateInstance<EnemyDefinitionCatalog>(); CreateAsset(catalog, CatalogPath); }
                catalog.SetAuthoringDefinitions(all); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
                Backup(BootPath); BindBootCatalog(catalog);
                EnemyDefinitionEditorUtility.RefreshContentHashes();
                CheckSaveErrors();
                EnemyDefinitionEditorUtility.ValidateAll(); report.applied = true;
                Debug.Log("[EnemyDefinitions] Migration applied: " + report.rows.Length + " clips. Backup: " + report.backupDirectory);
            }
            catch (Exception error) { report.error = error.ToString(); Debug.LogError("[EnemyDefinitions] Migration stopped. Backup: " + report.backupDirectory); throw; }
            finally { Application.logMessageReceived -= ObserveSaveError;
                File.WriteAllText(ReportRoot + "/migration-applied.json", JsonUtility.ToJson(report, true)); }
        }
        private static void CreateAsset(UnityEngine.Object asset, string path)
        {
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            string current = "Assets";
            foreach (string part in folder.Substring(7).Split('/')) { string next = current + "/" + part; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part); current = next; }
            AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssetIfDirty(asset);
        }
        private static void BindBootCatalog(EnemyDefinitionCatalog catalog)
        {
            var scene = SceneManager.GetSceneByPath(BootPath); bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Additive);
            try
            {
                var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BootGameplayNetworkManager>(true)).Single();
                var so = new SerializedObject(manager); so.FindProperty("enemyDefinitions").objectReferenceValue = catalog; so.ApplyModifiedPropertiesWithoutUndo();
                foreach (var prefab in catalog.Definitions.Select(d => d.Prefab).Distinct()) if (!manager.spawnPrefabs.Contains(prefab)) manager.spawnPrefabs.Add(prefab);
                EditorUtility.SetDirty(manager); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Boot enemy catalog binding.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
