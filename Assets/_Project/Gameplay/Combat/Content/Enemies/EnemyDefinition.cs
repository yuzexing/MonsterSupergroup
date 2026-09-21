using System;
using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat.Content
{
    [CreateAssetMenu(menuName = "MonsterSupergroup/Enemies/Enemy Definition", fileName = "NewEnemy")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField, HideInInspector] private string definitionId;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;
        [SerializeField] private EnemyStatsDefinition stats;
        [SerializeField] private EnemyAppearanceDefinition appearance;
        // Provenance only. These fields never select runtime content.
        [SerializeField, HideInInspector] private string legacyEnemyName;
        [SerializeField, HideInInspector] private int legacyVariant;
        [SerializeField, HideInInspector] private string migrationKey;
        public string IdText => definitionId;
        public Guid Id => Guid.TryParseExact(definitionId, "N", out var id) && id != Guid.Empty ? id :
            throw new ArgumentException("Missing/invalid enemy DefinitionId: " + name);
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public GameObject Prefab => prefab;
        public EnemyStatsDefinition Stats => stats;
        public EnemyAppearanceDefinition Appearance => appearance;
        public string LegacyEnemyName => string.IsNullOrEmpty(legacyEnemyName) ? IdText : legacyEnemyName;
        public int LegacyVariant => legacyVariant;
        public string MigrationKey => migrationKey;
        public EnemyDefinitionSnapshot Capture()
        {
            if (prefab == null || stats == null || appearance == null)
                throw new ArgumentException("Enemy definition requires Prefab, Stats and Appearance: " + name);
            appearance.Validate();
            return new EnemyDefinitionSnapshot(Id, prefab, stats.Capture(), appearance, LegacyEnemyName, legacyVariant);
        }
#if UNITY_EDITOR
        // Only fill an empty identity. Reimport, rename and normal edits must retain it.
        private void OnValidate() { if (string.IsNullOrEmpty(definitionId)) definitionId = Guid.NewGuid().ToString("N"); }
        public void InitializeAuthoring(GameObject body, EnemyStatsDefinition values, EnemyAppearanceDefinition visual,
            string label, string sourceName = null, int sourceVariant = 0, string key = null)
        {
            if (string.IsNullOrEmpty(definitionId)) definitionId = Guid.NewGuid().ToString("N");
            prefab = body; stats = values; appearance = visual; displayName = label;
            legacyEnemyName = sourceName; legacyVariant = sourceVariant; migrationKey = key;
        }
        public void AssignNewIdentityForCopy() { definitionId = Guid.NewGuid().ToString("N"); migrationKey = null; }
#endif
    }

    public sealed class EnemyDefinitionSnapshot
    {
        public readonly Guid Id;
        public readonly GameObject Prefab;
        public readonly EnemyAppearanceDefinition Appearance;
        public readonly string LegacyEnemyName;
        public readonly int LegacyVariant;
        private readonly EnemyStatsValues values;
        public EnemyDefinitionSnapshot(Guid id, GameObject prefab, EnemyStatsValues stats, EnemyAppearanceDefinition appearance, string source, int variant)
        { Id = id; Prefab = prefab; values = stats.Clone(); Appearance = appearance; LegacyEnemyName = source; LegacyVariant = variant; }
        public EnemyStatsValues Values => values.Clone();
        public EnemyStats CreateStats() { var result = new EnemyStats(); result.Init(values); return result; }
    }
}
