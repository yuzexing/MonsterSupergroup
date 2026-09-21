using System;
using System.IO;
using System.Linq;
using AstralShift.Rendering;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class EnemyDefinitionVerification
    {
        private static string Signature(EnemyDefinitionMigration.Row row) => string.Join("|", new[] {
            row.timeline, row.track, row.clip, row.prefab, row.source, row.variant.ToString(),
            row.statsJson, row.timing, row.lut,
            string.Join(";", row.mappings.Select(m => m.original + "=" + m.baked).OrderBy(x => x, StringComparer.Ordinal))
        });

        [MenuItem("Tools/MonsterSupergroup/Enemies/Verify Migration Baseline")]
        public static void VerifyMigrationBaseline()
        {
            string root = EnemyDefinitionMigration.ReportRoot;
            string baselinePath = root + "/migration-baseline.json";
            if (!File.Exists(baselinePath)) throw new FileNotFoundException("A reviewed pre-migration baseline is required.", baselinePath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var baseline = JsonUtility.FromJson<EnemyDefinitionMigration.Report>(File.ReadAllText(baselinePath));
            var current = EnemyDefinitionMigration.BuildPreview();
            if (current.rows.Any(r => !r.migrated)) throw new InvalidDataException("Unmigrated clips remain after reimport.");
            var expected = baseline.rows.Select(Signature).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var actual = current.rows.Select(Signature).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (!expected.SequenceEqual(actual))
            {
                File.WriteAllLines(root + "/migration-differences.txt", expected.Except(actual).Select(x => "BEFORE " + x)
                    .Concat(actual.Except(expected).Select(x => "AFTER " + x)));
                throw new InvalidDataException("Migration changed spawn data; see migration-differences.txt.");
            }
            foreach (var prefab in EnemyDefinitionEditorUtility.Catalog.Definitions.Select(d => d.Prefab).Distinct())
            {
                var agent = new SerializedObject(prefab.GetComponent<NetworkEnemySimulationAgent>());
                if (agent.FindProperty("referenceArtDatabase").objectReferenceValue != null)
                    throw new InvalidDataException("Legacy appearance database still set: " + prefab.name);
                foreach (var swapper in prefab.GetComponentsInChildren<SpriteRendererPaletteSwapper>(true))
                    if (new SerializedObject(swapper).FindProperty("bakedPalettes").arraySize != 0)
                        throw new InvalidDataException("Legacy baked palette authoring still set: " + prefab.name);
            }
            EnemyDefinitionEditorUtility.RefreshContentHashes();
            EnemyDefinitionEditorUtility.ValidateAll();
            File.WriteAllText(root + "/post-migration-preview.json", JsonUtility.ToJson(current, true));
            File.WriteAllText(root + "/migration-verification.txt", "PASS: " + current.rows.Length + " clips; Prefab, base values, LUT/mappings and timing/policy unchanged after disk reimport. Legacy Prefab sources cleared.");
            Debug.Log("[EnemyDefinitions] Disk migration baseline PASS: " + current.rows.Length + " clips.");
        }

        public static void FinalizeVerification()
        {
            VerifyMigrationBaseline();
            string[] Snapshot() => EnemyDefinitionEditorUtility.Catalog.Definitions.OrderBy(d => d.IdText, StringComparer.Ordinal)
                .Select(d => d.IdText + "|" + AssetDatabase.GetAssetPath(d) + "|" + JsonUtility.ToJson(d.Stats.Capture()) + "|" + AssetDatabase.GetAssetPath(d.Appearance)).ToArray();
            var before = Snapshot();
            EnemyDefinitionMigration.Preview();
            EnemyDefinitionMigration.Apply();
            if (!before.SequenceEqual(Snapshot())) throw new InvalidDataException("Repeated migration changed definition identity/content.");
            VerifyMigrationBaseline();
            File.WriteAllText(EnemyDefinitionMigration.ReportRoot + "/migration-idempotence.txt",
                "PASS: repeated reviewed migration preserved all definition IDs, asset paths, base values and appearances.");
            BuildValidationPlayer();
        }

        public static void BuildValidationPlayer()
        {
            EnemyDefinitionEditorUtility.RefreshContentHashes();
            EnemyDefinitionEditorUtility.ValidateAll();
            MonsterSupergroup.EditorTools.ProjectBuildService.Build("enemy-variants",
                "Builds/EnemyDefinitions20260920/MonsterSupergroup.exe");
        }
    }
}
