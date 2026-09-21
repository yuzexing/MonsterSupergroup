using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.Rendering;
using Mirror;
using MonsterSupergroup.Gameplay.Combat.Content;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class EnemyDefinitionTests
    {
        private readonly List<Object> owned = new List<Object>();
        private string temporaryFolder;
        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        private EnemyDefinition Definition(GameObject prefab, int health = 50, EnemyAppearanceDefinition appearance = null)
        {
            var stats = Own(ScriptableObject.CreateInstance<EnemyStatsDefinition>());
            stats.SetAuthoringValues(new EnemyStatsValues { Health = health, Damage = 10, Speed = 3, XP = 5, KnockBackMultiplier = 1, WindMultiplier = 1 });
            appearance ??= Own(ScriptableObject.CreateInstance<EnemyAppearanceDefinition>());
            var definition = Own(ScriptableObject.CreateInstance<EnemyDefinition>());
            definition.InitializeAuthoring(prefab, stats, appearance, "Test enemy"); return definition;
        }
        private TimelineAsset Timeline()
        {
            var timeline = Own(ScriptableObject.CreateInstance<TimelineAsset>());
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration = 30; return timeline;
        }
        private NetworkEnemySpawnClip Add(TimelineAsset timeline, EnemyDefinition definition, double start = 1)
        {
            var track = Own(timeline.CreateTrack<NetworkEnemySpawnTrack>()); var clip = track.CreateClip<NetworkEnemySpawnClip>(); Own(clip.asset);
            clip.start = start; clip.duration = 5; var spawn = (NetworkEnemySpawnClip)clip.asset; spawn.count = 1;
            spawn.referenceMode = ReferenceSpawnMode.CurveBudget;
            EnemySpawnClipInspector.SelectEnemy(new Object[] { spawn }, definition); return spawn;
        }
        private static ReferenceWaveProgram CompileReference(TimelineAsset timeline) => NetworkWaveTimelineCompiler.CompileReference(
            timeline, null, 30, 30, AnimationCurve.Linear(0, 1, 1, 1), 0, 1,
            2, 1.5f, 30, 5, 20, 6, 1.41f, 1, 1, Array.Empty<ReferenceBarrierDefinition>(), out _);

        [TearDown]
        public void Cleanup()
        {
            EnemyAppearanceCache.ClearAll();
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) { Undo.ClearUndo(owned[i]); Object.DestroyImmediate(owned[i]); }
            owned.Clear();
            if (!string.IsNullOrEmpty(temporaryFolder)) AssetDatabase.DeleteAsset(temporaryFolder);
            temporaryFolder = null;
        }
        [Test]
        public void DropdownSelectionUsesAssetReferenceEvenWhenUnityRewritesItemIds()
        {
            var definition = Definition(Own(new GameObject("body")));
            EnemyDefinition selected = null;
            var dropdown = new EnemyDefinitionDropdown(new UnityEditor.IMGUI.Controls.AdvancedDropdownState(),
                new[] { definition }, value => selected = value);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var root = (UnityEditor.IMGUI.Controls.AdvancedDropdownItem)typeof(EnemyDefinitionDropdown).GetMethod("BuildRoot", flags).Invoke(dropdown, null);
            var items = new List<UnityEditor.IMGUI.Controls.AdvancedDropdownItem>(root.children);
            items[1].id = -128156526;
            typeof(EnemyDefinitionDropdown).GetMethod("ItemSelected", flags).Invoke(dropdown, new object[] { items[1] });
            Assert.That(selected, Is.SameAs(definition));
            items[0].id = 9482;
            typeof(EnemyDefinitionDropdown).GetMethod("ItemSelected", flags).Invoke(dropdown, new object[] { items[0] });
            Assert.That(selected, Is.Null);
        }
        [Test]
        public void IdIsGeneratedOnceAndDoesNotDependOnDisplayNameOrConfiguration()
        {
            var definition = Definition(Own(new GameObject("body"))); var before = definition.Id;
            Assert.That(before, Is.Not.EqualTo(Guid.Empty)); definition.name = "RenamedAsset";
            definition.InitializeAuthoring(definition.Prefab, definition.Stats, definition.Appearance, "Changed display name");
            Assert.That(definition.Id, Is.EqualTo(before));
        }
        [Test]
        public void ExplicitNewCopyGetsNewIdWhileRawDuplicateIsRejected()
        {
            var definition = Definition(Own(new GameObject("body")));
            var copy = Own(Object.Instantiate(definition));
            Assert.Throws<ArgumentException>(() => new EnemyDefinitionRegistry(new[] { definition, copy }));
            var original = definition.Id; copy.AssignNewIdentityForCopy();
            Assert.That(copy.Id, Is.Not.EqualTo(original)); Assert.That(definition.Id, Is.EqualTo(original));
            Assert.DoesNotThrow(() => new EnemyDefinitionRegistry(new[] { definition, copy }));
        }
        [Test]
        public void IdSurvivesAssetRenameMoveAndReimport()
        {
            string folderName = "__EnemyDefinitionTests_" + Guid.NewGuid().ToString("N");
            temporaryFolder = "Assets/" + folderName; AssetDatabase.CreateFolder("Assets", folderName);
            var definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            definition.InitializeAuthoring(null, null, null, "Unconfigured identity fixture");
            string first = temporaryFolder + "/First.asset"; AssetDatabase.CreateAsset(definition, first); AssetDatabase.SaveAssetIfDirty(definition);
            var id = definition.Id; string moved = temporaryFolder + "/Renamed.asset";
            Assert.That(AssetDatabase.MoveAsset(first, moved), Is.Empty);
            AssetDatabase.ImportAsset(moved, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Assert.That(AssetDatabase.LoadAssetAtPath<EnemyDefinition>(moved).Id, Is.EqualTo(id));
        }
        [Test]
        public void StatsCaptureAndSpawnCopiesNeverMutateAuthoredValues()
        {
            var definition = Definition(Own(new GameObject("body")), 50); var snapshot = definition.Capture();
            var values = snapshot.Values; values.Health = 999;
            var enemy = snapshot.CreateStats(); enemy.BaseHealth = 123;
            definition.Stats.SetAuthoringValues(new EnemyStatsValues { Health = 80, Damage = 20, Speed = 4 });
            Assert.That(snapshot.Values.Health, Is.EqualTo(50)); Assert.That(definition.Stats.Capture().Health, Is.EqualTo(80));
        }
        [Test]
        public void SamePrefabDifferentDefinitionsHaveDifferentOrdinaryWaveIndices()
        {
            var prefab = Own(new GameObject("shared body")); var first = Definition(prefab, 20); var second = Definition(prefab, 60);
            var timeline = Timeline(); Add(timeline, first, 1); Add(timeline, second, 2);
            var program = NetworkWaveTimelineCompiler.Compile(timeline, 30, out var prefabs, out var definitions);
            Assert.That(prefabs.Length, Is.EqualTo(1)); Assert.That(definitions.Length, Is.EqualTo(2));
            Assert.That(program.Get(1).PrefabIndex, Is.EqualTo(program.Get(2).PrefabIndex));
            Assert.That(program.Get(1).DefinitionIndex, Is.Not.EqualTo(program.Get(2).DefinitionIndex));
            Assert.That(definitions[program.Get(1).DefinitionIndex].Values.Health, Is.EqualTo(20));
            Assert.That(definitions[program.Get(2).DefinitionIndex].Values.Health, Is.EqualTo(60));
        }
        [Test]
        public void ReferenceCompilerDoesNotNeedAnEnemyDatabaseAfterMigration()
        {
            var definition = Definition(Own(new GameObject("body")), 61); var timeline = Timeline(); var clip = Add(timeline, definition);
            clip.sourceEnemy = "deliberately-wrong-legacy-name"; clip.sourceVariant = 999; clip.enemyPrefab = null;
            var program = CompileReference(timeline);
            Assert.That(program.Clips[0].DefinitionId, Is.EqualTo(definition.Id));
            Assert.That(program.Clips[0].Stats.BaseHealth, Is.EqualTo(61));
        }
        [Test]
        public void MissingSchemaOneDefinitionNeverFallsBackToLegacyPrefab()
        {
            var timeline = Timeline(); var clip = Add(timeline, null);
            clip.enemyPrefab = Own(new GameObject("valid old field")); clip.sourceEnemy = "Brotchi";
            Assert.Throws<ArgumentException>(() => NetworkWaveTimelineCompiler.Compile(timeline, 30, out _));
            Assert.Throws<ArgumentException>(() => CompileReference(timeline));
        }
        [Test]
        public void DropdownSelectionPersistsObjectReferenceAndMarksDefinitionSchema()
        {
            var definition = Definition(Own(new GameObject("body"))); var clip = Add(Timeline(), definition);
            Assert.That(clip.Enemy, Is.SameAs(definition)); Assert.That(clip.AuthoringVersion, Is.EqualTo(1));
            Assert.That(clip.sourceEnemy, Is.Null.Or.Empty); Assert.That(clip.enemyPrefab, Is.Null);
        }
        [Test]
        public void CatalogRejectsUnknownIdsAndDetectsDifferentBaseValues()
        {
            var definition = Definition(Own(new GameObject("body")), 20); var first = new EnemyDefinitionRegistry(new[] { definition });
            Assert.Throws<ArgumentException>(() => first.Resolve(Guid.NewGuid()));
            definition.Stats.SetAuthoringValues(new EnemyStatsValues { Health = 30, Damage = 10, Speed = 3 });
            var second = new EnemyDefinitionRegistry(new[] { definition });
            Assert.That(second.Fingerprint, Is.Not.EqualTo(first.Fingerprint)); Assert.That(first.Resolve(definition.Id).Values.Health, Is.EqualTo(20));
        }
        [Test]
        public void GuidWireRoundTripPreservesExactDefinitionIdentity()
        {
            var id = Guid.NewGuid(); var writer = new NetworkWriter(); writer.WriteGuid(id);
            var reader = new NetworkReader(new ArraySegment<byte>(writer.ToArray()));
            Assert.That(reader.ReadGuid(), Is.EqualTo(id));
        }
        [Test]
        public void OrdinaryDefinitionBirthDoesNotEnableReferencePolicy()
        {
            var birth = new EnemyBirthParameters { DefinitionId = Guid.NewGuid(), Health = 27, Damage = 9, Speed = 3, SpeedMultiplier = 1 };
            Assert.That(birth.HasAttributes, Is.True); Assert.That(birth.Enabled, Is.False);
            var stats = new EnemyStats(); birth.Apply(stats); Assert.That(stats.BaseHealth, Is.EqualTo(27));
        }
        [Test]
        public void AppearanceValidationRejectsMissingDuplicateAndWrongSizeMappings()
        {
            var source = Own(new Texture2D(4, 4)); var lut = Own(new Texture2D(2, 2)); var baked = Own(new Texture2D(4, 4));
            var appearance = Own(ScriptableObject.CreateInstance<EnemyAppearanceDefinition>());
            appearance.SetAuthoringValues(lut, Array.Empty<EnemyAppearanceDefinition.TextureMapping>());
            Assert.Throws<ArgumentException>(() => appearance.Validate());
            var mapping = new EnemyAppearanceDefinition.TextureMapping { original = source, baked = baked };
            appearance.SetAuthoringValues(lut, new[] { mapping, mapping }); Assert.Throws<ArgumentException>(() => appearance.Validate());
            appearance.SetAuthoringValues(lut, new[] { new EnemyAppearanceDefinition.TextureMapping { original = source, baked = lut } });
            Assert.Throws<ArgumentException>(() => appearance.Validate());
        }
        [Test]
        public void SameLutDifferentAppearancesDoNotShareBakedSpritesOrReleaseOtherUsers()
        {
            var source = Own(new Texture2D(4, 4)); var lut = Own(new Texture2D(2, 2));
            var bakedA = Own(new Texture2D(4, 4)); var bakedB = Own(new Texture2D(4, 4));
            var sprite = Own(Sprite.Create(source, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 100));
            var first = Own(ScriptableObject.CreateInstance<EnemyAppearanceDefinition>()); var second = Own(ScriptableObject.CreateInstance<EnemyAppearanceDefinition>());
            first.SetAuthoringValues(lut, new[] { new EnemyAppearanceDefinition.TextureMapping { original = source, baked = bakedA } });
            second.SetAuthoringValues(lut, new[] { new EnemyAppearanceDefinition.TextureMapping { original = source, baked = bakedB } });
            var a = EnemyAppearanceCache.Acquire(first); var otherA = EnemyAppearanceCache.Acquire(first); var b = EnemyAppearanceCache.Acquire(second);
            try
            {
                var one = a.Map(sprite); var two = b.Map(sprite);
                Assert.That(one.texture, Is.SameAs(bakedA)); Assert.That(two.texture, Is.SameAs(bakedB));
                Assert.That(otherA.Map(sprite), Is.SameAs(one)); a.Dispose();
                Assert.That(one != null, Is.True); Assert.That(otherA.Original(one), Is.SameAs(sprite));
                otherA.Dispose(); Assert.That(one == null, Is.True); Assert.That(two != null, Is.True);
                Assert.That(bakedA != null && bakedB != null, Is.True);
            }
            finally { a.Dispose(); otherA.Dispose(); b.Dispose(); }
        }
    }
}
