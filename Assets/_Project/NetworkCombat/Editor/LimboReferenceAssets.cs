using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    /// <summary>Reconstruct data assets only; source Prefabs, art, audio and code are never imported.</summary>
    public static class LimboReferenceAssets
    {
        public const string Root = "Assets/_Project/Content/NetworkCombat/Limbo";
        public const string ResourcesRoot = Root + "/Resources/LimboReference";
        [Serializable] public class Key { public float time, value, inSlope, outSlope; }
        [Serializable] public class Variant { public string name; public int hp, damage, sourceLine; public float xp, speed, knockback, stun, wind; }
        [Serializable] public class Enemy { public string name; public Variant[] variants; }
        [Serializable] public class Clip
        {
            public double start, duration;
            public int mode, variant, count, sourceLine;
            public string enemy, sourceFile, sourceId, sourcePrefab, missingEvidence;
            public float cooldown, contactRadius;
            public Key[] keys;
            public bool expiresOffscreen, resetOnReposition;
        }
        [Serializable] public class Manifest
        {
            public double sourceDuration, stageEnd;
            public int maximumAlive;
            public float xpAmplitude;
            public Key[] xpKeys;
            public Clip[] clips;
            public Enemy[] enemies;
        }
        public static AnimationCurve Curve(Key[] keys) => new AnimationCurve(keys.Select(k =>
            new Keyframe(k.time, k.value, k.inSlope, k.outSlope)).ToArray())
            { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };

        [MenuItem("Tools/MonsterSupergroup/Limbo/Create reference assets")]
        public static void Create()
        {
            EnemyDefinitionMigration.EnsureLegacyWriterAllowed("LimboReferenceAssets.Create");
            var data = JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "/limbo-source.json"));
            if (data.clips.Length != 31) throw new InvalidDataException("Limbo requires exactly 31 source enemy clips.");
            Directory.CreateDirectory(ResourcesRoot); AssetDatabase.Refresh();
            CreateDatabase(data, "SourceEnemyDB");
            var adapted = CreateDatabase(data, "AdaptedEnemyDB");
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(Root + "/Limbo.playable");
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.durationMode = TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration = data.sourceDuration;
                AssetDatabase.CreateAsset(timeline, Root + "/Limbo.playable");
                foreach (var row in data.clips)
                {
                    var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(null, row.enemy + " v" + row.variant + " @" + row.start.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    var clip = track.CreateClip<NetworkEnemySpawnClip>(); clip.start = row.start; clip.duration = row.duration;
                    clip.displayName = row.enemy + " v" + row.variant + " " + (ReferenceSpawnMode)row.mode + " " + row.count;
                    var spawn = (NetworkEnemySpawnClip)clip.asset;
                    spawn.referenceMode = (ReferenceSpawnMode)row.mode; spawn.sourceEnemy = row.enemy; spawn.sourceVariant = row.variant;
                    if (spawn.referenceMode == ReferenceSpawnMode.FormationBurst)
                    {
                        spawn.referenceSpawnReadiness = ReferenceEnemyReadiness.ImplementationPending;
                        spawn.spawnReadinessNote = "B warning and network formation lifecycle are not implemented.";
                    }
                    spawn.sourceLocation = row.sourceFile + ":" + row.sourceLine + " fileID=" + row.sourceId;
                    spawn.missingEvidence = row.missingEvidence; spawn.count = row.count; spawn.spawnCooldown = row.cooldown;
                    spawn.spawnCurve = Curve(row.keys); spawn.contactRadius = row.contactRadius;
                    spawn.expiresOffscreen = row.expiresOffscreen; spawn.resetOnReposition = row.resetOnReposition;
                    spawn.speedMultipliers = row.mode == 3 ? new Vector2(.98f, 1.02f) : new Vector2(.9f, 1.1f);
                    if (string.IsNullOrEmpty(row.missingEvidence))
                        spawn.enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Content/NetworkCombat/NetworkEnemyBase.prefab");
                    EditorUtility.SetDirty(spawn); EditorUtility.SetDirty(track);
                }
                EditorUtility.SetDirty(timeline);
            }
            CreateRules(data, adapted, timeline, "Opening", 60);
            CreateRules(data, adapted, timeline, "Full", data.stageEnd);
            AssetDatabase.SaveAssets();
            Debug.Log("[LimboAssets] 31 clips, source/adapted databases, Opening=60s; Full is evidence-gated.");
        }

        private static EnemyDatabase CreateDatabase(Manifest data, string name)
        {
            string path = Root + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<EnemyDatabase>(path);
            if (existing != null) return existing; // Never overwrite subsequent adaptation tuning.
            var database = ScriptableObject.CreateInstance<EnemyDatabase>(); AssetDatabase.CreateAsset(database, path);
            var so = new SerializedObject(database); var enemies = so.FindProperty("enemies"); enemies.arraySize = data.enemies.Length;
            for (int i = 0; i < data.enemies.Length; i++)
            {
                var source = data.enemies[i]; var enemy = enemies.GetArrayElementAtIndex(i);
                enemy.FindPropertyRelative("enemyName").stringValue = source.name;
                var variants = enemy.FindPropertyRelative("enemyData"); variants.arraySize = source.variants.Length;
                for (int j = 0; j < source.variants.Length; j++)
                {
                    var row = source.variants[j]; var variant = variants.GetArrayElementAtIndex(j);
                    variant.FindPropertyRelative("variantName").stringValue = row.name;
                    variant.FindPropertyRelative("rubberbandMaxDistance").floatValue = 20;
                    var stats = variant.FindPropertyRelative("stats.baseStats");
                    stats.FindPropertyRelative("hp").intValue = row.hp; stats.FindPropertyRelative("damage").intValue = row.damage;
                    stats.FindPropertyRelative("xp").floatValue = row.xp; stats.FindPropertyRelative("speed").floatValue = row.speed;
                    stats.FindPropertyRelative("knockbackMultiplier").floatValue = row.knockback;
                    stats.FindPropertyRelative("stunTime").floatValue = row.stun; stats.FindPropertyRelative("windMultiplier").floatValue = row.wind;
                    foreach (string multiplier in new[] { "hpMultiplier", "damageMultiplier", "xpMultiplier", "speedMultiplier" })
                        variant.FindPropertyRelative("stats.multipliers." + multiplier).floatValue = 1;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo(); return database;
        }

        private static void CreateRules(Manifest data, EnemyDatabase database, TimelineAsset timeline, string name, double end)
        {
            string path = ResourcesRoot + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(path) != null) return;
            var rules = ScriptableObject.CreateInstance<GameplayWaveRules>(); AssetDatabase.CreateAsset(rules, path);
            var so = new SerializedObject(rules);
            so.FindProperty("timeline").objectReferenceValue = timeline;
            so.FindProperty("referenceStage").boolValue = true;
            so.FindProperty("referenceEnemies").objectReferenceValue = database;
            so.FindProperty("referenceEndTime").doubleValue = end;
            so.FindProperty("referenceSourceDuration").doubleValue = data.sourceDuration;
            so.FindProperty("referenceXpCurve").animationCurveValue = Curve(data.xpKeys);
            so.FindProperty("referenceXpAmplitude").floatValue = data.xpAmplitude;
            so.FindProperty("maximumAlive").intValue = data.maximumAlive;
            so.FindProperty("positionAttempts").intValue = 100;
            var barriers = so.FindProperty("barriers"); barriers.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                var b = barriers.GetArrayElementAtIndex(i);
                b.FindPropertyRelative("start").doubleValue = i == 0 ? 333 : 599.9666666666667;
                b.FindPropertyRelative("end").doubleValue = i == 0 ? 378 : 649.9666666666667;
                b.FindPropertyRelative("minimumRadius").floatValue = 10;
                b.FindPropertyRelative("maximumRadius").floatValue = 20;
                b.FindPropertyRelative("shrinkDuration").floatValue = 30;
                b.FindPropertyRelative("sides").intValue = 40;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void CreateAndBuildBatch()
        {
            int exit = 0;
            try { Create(); MonsterSupergroup.EditorTools.ProjectBuildService.Build("kcp-development", "Builds/LimboReference/MonsterSupergroupLimbo.exe"); }
            catch (Exception error) { Debug.LogException(error); exit = 1; }
            finally { EditorApplication.Exit(exit); }
        }
    }
}
