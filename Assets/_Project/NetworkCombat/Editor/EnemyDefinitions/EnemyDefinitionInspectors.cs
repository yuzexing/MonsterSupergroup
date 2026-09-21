using System;
using System.Collections.Generic;
using System.Linq;
using MonsterSupergroup.Gameplay.Combat.Content;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public sealed class EnemyDefinitionDropdown : AdvancedDropdown
    {
        private readonly EnemyDefinition[] choices;
        private readonly Action<EnemyDefinition> selected;
        private sealed class Choice : AdvancedDropdownItem
        {
            public readonly EnemyDefinition Definition;
            public Choice(string label, EnemyDefinition definition) : base(label) { Definition = definition; }
        }
        public EnemyDefinitionDropdown(AdvancedDropdownState state, EnemyDefinition[] choices, Action<EnemyDefinition> selected) : base(state)
        { this.choices = choices; this.selected = selected; minimumSize = new Vector2(380, 360); }
        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Enemy Definitions");
            root.AddChild(new Choice("None", null));
            for (int i = 0; i < choices.Length; i++)
            {
                var definition = choices[i];
                string label = choices.Count(d => d.DisplayName == definition.DisplayName) > 1
                    ? definition.DisplayName + " [" + definition.IdText.Substring(0, Math.Min(8, definition.IdText.Length)) + "]" : definition.DisplayName;
                root.AddChild(new Choice(label, definition) { icon = AssetPreview.GetMiniThumbnail(definition.Prefab) });
            }
            return root;
        }
        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            // Unity can change UI item IDs when parenting/searching. Persist the asset
            // on the item, never treat a dropdown UI ID as a content identity.
            if (item is Choice choice) selected(choice.Definition);
        }
    }

    [CustomEditor(typeof(NetworkEnemySpawnClip)), CanEditMultipleObjects]
    public sealed class EnemySpawnClipInspector : UnityEditor.Editor
    {
        private readonly AdvancedDropdownState dropdownState = new AdvancedDropdownState();
        private bool showProvenance;
        public static void SelectEnemy(UnityEngine.Object[] clips, EnemyDefinition definition)
        {
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                var serialized = new SerializedObject(clip); serialized.FindProperty("enemy").objectReferenceValue = definition; serialized.FindProperty("authoringVersion").intValue = 1;
                serialized.ApplyModifiedProperties();
            }
        }
        public override void OnInspectorGUI()
        {
            using var editing = new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode);
            serializedObject.Update(); var property = serializedObject.FindProperty("enemy");
            if (serializedObject.FindProperty("authoringVersion").intValue == 0)
                EditorGUILayout.HelpBox("Legacy schema 0: this clip has not been migrated. Choose a Definition or use Migration Preview / Apply. Definition schema 1 never falls back to old name fields.", MessageType.Warning);
            var definition = property.objectReferenceValue as EnemyDefinition;
            var rect = EditorGUILayout.GetControlRect(); rect = EditorGUI.PrefixLabel(rect, new GUIContent("Enemy"));
            string title = property.hasMultipleDifferentValues ? "Multiple definitions" : definition != null ? definition.DisplayName : "Select an enemy definition...";
            if (EditorGUI.DropdownButton(rect, new GUIContent(title), FocusType.Keyboard))
                new EnemyDefinitionDropdown(dropdownState, EnemyDefinitionEditorUtility.Choices(), selected => SelectEnemy(targets, selected)).Show(rect);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(definition == null))
                {
                    if (GUILayout.Button("Locate Definition")) EditorGUIUtility.PingObject(definition);
                    if (GUILayout.Button("Open Definition")) Selection.activeObject = definition;
                }
                if (GUILayout.Button("New Definition")) EnemyDefinitionEditorUtility.CreateFromMenu();
            }
            if (!property.hasMultipleDifferentValues)
            {
                if (definition == null) EditorGUILayout.HelpBox(serializedObject.FindProperty("authoringVersion").intValue == 0
                    ? "This clip still uses the explicit legacy schema. Review and apply migration to enable definition-only editing."
                    : "Choose an Enemy Definition. Schema 1 never falls back to legacy name/Prefab fields.",
                    serializedObject.FindProperty("authoringVersion").intValue == 0 ? MessageType.Warning : MessageType.Error);
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField("Prefab (from Enemy)", definition.Prefab, typeof(GameObject), false);
                        EditorGUILayout.ObjectField("Stats", definition.Stats, typeof(EnemyStatsDefinition), false);
                        EditorGUILayout.ObjectField("Appearance", definition.Appearance, typeof(EnemyAppearanceDefinition), false);
                        EditorGUILayout.TextField("Definition ID", definition.IdText);
                    }
                    try
                    {
                        var stats = definition.Stats.Capture();
                        EditorGUILayout.LabelField($"HP {stats.Health}    Damage {stats.Damage}    Speed {stats.Speed}    XP {stats.XP}");
                        EnemyDefinitionEditorUtility.ValidateDefinition(definition, false);
                        if (EnemyDefinitionEditorUtility.Catalog == null || !EnemyDefinitionEditorUtility.Catalog.Definitions.Contains(definition))
                            throw new ArgumentException("Selected enemy is not in the Boot catalog.");
                    }
                    catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
                }
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Spawn Rules", EditorStyles.boldLabel);
            DrawPropertiesExcluding(serializedObject, "m_Script", "authoringVersion", "enemy", "enemyPrefab", "sourceEnemy", "sourceVariant", "sourceLocation", "missingEvidence",
                "referenceReadiness", "referenceSpawnReadiness", "spawnReadinessNote", "recoveredEvidence");
            showProvenance = EditorGUILayout.Foldout(showProvenance, "Source / Migration Metadata", true);
            if (showProvenance)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceLocation"));
                EditorGUILayout.HelpBox("Source Location is optional provenance. It does not select an enemy or spawn position.", MessageType.Info);
                if (definition == null && targets.Length == 1) DrawLegacySelection();
                else using (new EditorGUI.DisabledScope(true))
                { EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceEnemy")); EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceVariant")); }
                foreach (string field in new[] { "referenceReadiness", "referenceSpawnReadiness", "missingEvidence", "spawnReadinessNote", "recoveredEvidence" })
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
            }
            serializedObject.ApplyModifiedProperties();
        }
        private void DrawLegacySelection()
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(AssetDatabase.GetAssetPath(target));
            var databases = EnemyDefinitionMigration.Find<GameplayWaveRules>().Where(r => r.Timeline == timeline && r.IsReferenceStage)
                .Select(r => new SerializedObject(r).FindProperty("referenceEnemies").objectReferenceValue as EnemyDatabase).Where(d => d != null).Distinct().ToArray();
            EditorGUILayout.HelpBox("Legacy import editing only. Migrate this clip before running it.", MessageType.Warning);
            if (databases.Length != 1) { EditorGUILayout.HelpBox("No unique source database for this Timeline. Resolve the migration context first.", MessageType.Warning); return; }
            var database = databases[0]; var names = database.EnemyNames;
            var name = serializedObject.FindProperty("sourceEnemy"); var variant = serializedObject.FindProperty("sourceVariant");
            int current = Array.IndexOf(names, name.stringValue); int next = EditorGUILayout.Popup("Legacy Enemy", current, names);
            if (next >= 0 && next != current) { name.stringValue = names[next]; variant.intValue = 0; }
            var entry = database.Enemies.FirstOrDefault(e => e.enemyName == name.stringValue);
            if (entry?.enemyData != null)
                variant.intValue = EditorGUILayout.Popup("Legacy Variant", variant.intValue, entry.enemyData.Select((d, i) => i + " / " + d.variantName).ToArray());
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enemyPrefab"), new GUIContent("Legacy Import Prefab"));
        }
    }

    [CustomEditor(typeof(EnemyDefinition)), CanEditMultipleObjects]
    public sealed class EnemyDefinitionInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update(); var definition = (EnemyDefinition)target;
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField("Definition ID", definition.IdText);
            if (GUILayout.Button("Copy ID")) EditorGUIUtility.systemCopyBuffer = definition.IdText;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                DrawPropertiesExcluding(serializedObject, "m_Script", "definitionId", "legacyEnemyName", "legacyVariant", "migrationKey");
            serializedObject.ApplyModifiedProperties();
            bool duplicate = EnemyDefinitionMigration.Find<EnemyDefinition>().Any(d => d != definition && d.IdText == definition.IdText);
            if (duplicate)
            {
                EditorGUILayout.HelpBox("Duplicate DefinitionId. The original asset will not be changed. Assign a NEW identity only when this asset is a new copy.", MessageType.Error);
                if (GUILayout.Button("Assign New ID to This Copy") && EditorUtility.DisplayDialog("New enemy identity", "This changes ONLY the selected asset identity. Continue only for a new copy, not an existing released definition.", "Assign new ID", "Cancel"))
                { Undo.RecordObject(definition, "New enemy copy identity"); definition.AssignNewIdentityForCopy(); EditorUtility.SetDirty(definition); AssetDatabase.SaveAssetIfDirty(definition); }
            }
            if (targets.Length == 1 && GUILayout.Button("Duplicate as New Enemy Definition"))
            {
                string path = EditorUtility.SaveFilePanelInProject("Duplicate enemy", definition.name + "_Copy", "asset", "Stats and Appearance remain shared; duplicate them separately to edit independently.");
                if (!string.IsNullOrEmpty(path)) Selection.activeObject = EnemyDefinitionEditorUtility.DuplicateDefinition(definition, path);
            }
            if (EnemyDefinitionEditorUtility.Catalog != null && !EnemyDefinitionEditorUtility.Catalog.Definitions.Contains(definition) && GUILayout.Button("Add to Boot Catalog"))
                EnemyDefinitionEditorUtility.AddToCatalog(definition);
            try { EnemyDefinitionEditorUtility.ValidateDefinition(definition, false); }
            catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
        }
    }
}
