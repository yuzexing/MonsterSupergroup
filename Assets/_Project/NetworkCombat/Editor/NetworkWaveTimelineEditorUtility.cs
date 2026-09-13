using System;
using System.IO;
using System.Linq;
using System.Text;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class NetworkWaveTimelineEditorUtility
    {
        public const string RulesPath = "Assets/_Project/Content/NetworkCombat/GameplayWaveRules.asset";
        public const string TimelinePath = "Assets/_Project/Content/NetworkCombat/GameplayEnemyWaves.playable";
        private const string Content = "Assets/_Project/Content/NetworkCombat/";

        [MenuItem("Monster Supergroup/Network Combat/Waves/Create Default Timeline")]
        public static void EnsureDefault()
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.durationMode = TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration = 150;
                AssetDatabase.CreateAsset(timeline, TimelinePath);
                AddWave(1, "NetworkEnemyBase", null);
                AddWave(2, "NetworkEnemyBase", "NetworkEnemyLustSinner");
                AddWave(3, "NetworkEnemySkeleton", "NetworkEnemyLustSinner");
                AddWave(4, "NetworkEnemyImp", "NetworkEnemyLustSinner");
                AddWave(5, "NetworkEnemyLustSinner", null);
                EditorUtility.SetDirty(timeline);
            }
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(RulesPath);
            if (rules == null) throw new InvalidOperationException("Gameplay wave rules are missing.");
            if (rules.Timeline == null)
            {
                var serialized = new SerializedObject(rules);
                serialized.FindProperty("timeline").objectReferenceValue = timeline;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            ValidateConfigured();

            void AddWave(int wave, string first, string second)
            {
                var group = timeline.CreateTrack<GroupTrack>(null, "Wave " + wave + (wave == 5 ? " (repeat)" : ""));
                AddClip(group, first, (wave - 1) * 30, second == null ? 6 : 3);
                if (second != null) AddClip(group, second, (wave - 1) * 30 + 2, 3);
            }
            void AddClip(GroupTrack group, string name, double start, int count)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Content + name + ".prefab");
                if (prefab == null) throw new InvalidOperationException("Missing enemy " + name);
                var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(group, name);
                var clip = track.CreateClip<NetworkEnemySpawnClip>();
                clip.start = start; clip.duration = 12; clip.displayName = name + " x" + count;
                var spawn = (NetworkEnemySpawnClip)clip.asset; spawn.enemyPrefab = prefab; spawn.count = count;
                EditorUtility.SetDirty(spawn); EditorUtility.SetDirty(track);
            }
        }

        [MenuItem("Monster Supergroup/Network Combat/Waves/Open Timeline")]
        public static void Open()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(RulesPath);
            if (rules?.Timeline == null) { EnsureDefault(); rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(RulesPath); }
            Selection.activeObject = rules.Timeline; AssetDatabase.OpenAsset(rules.Timeline);
        }

        [MenuItem("Monster Supergroup/Network Combat/Waves/Validate and Export Preview")]
        public static void ValidateConfigured()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(RulesPath);
            if (rules == null) throw new InvalidOperationException("Wave rules missing.");
            if (!rules.TryCapture(out var captured, out var error)) throw new InvalidOperationException(error);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Boot.unity");
            try
            {
                var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkManager>(true)).Single();
                var identities = captured.Prefabs.Select(p => p.GetComponent<NetworkIdentity>()).ToArray();
                foreach (var prefab in captured.Prefabs)
                    if (!manager.spawnPrefabs.Contains(prefab) || prefab.GetComponent<NetworkEnemySimulationAgent>() == null ||
                        prefab.GetComponentsInChildren<NetworkIdentity>(true).Length != 1 ||
                        !(prefab.GetComponent<AstralShift.HellMaiden.AI.Enemy.EnemyController>()?.collider is CircleCollider2D))
                        throw new InvalidOperationException("Invalid/unregistered wave prefab: " + prefab.name);
                if (identities.Any(i => i == null || i.assetId == 0) || identities.Select(i => i.assetId).Distinct().Count() != identities.Length)
                    throw new InvalidOperationException("Wave Prefab asset identities are missing or duplicated.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Directory.CreateDirectory("Logs/TimelineWaves");
            var preview = new StringBuilder("sequence,wave,slot,time,prefab\n");
            long count = captured.Program.EventsBeforeWave(7);
            for (long i = 1; i <= count; i++)
            {
                var spawn = captured.Program.Get(i);
                preview.AppendLine($"{i},{spawn.Wave},{spawn.Index},{spawn.ScheduledTime.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{captured.Prefabs[spawn.PrefabIndex].name}");
            }
            File.WriteAllText("Logs/TimelineWaves/compiled-six-waves.csv", preview.ToString());
            Debug.Log("[TimelineWaves] Configuration and network registration PASS; six-wave preview exported.");
        }

        public static void VerifyRepeat()
        {
            EnsureDefault();
            var paths = new[] { TimelinePath, RulesPath, Content + "NetworkEnemyBase.prefab", Content + "NetworkEnemySkeleton.prefab",
                Content + "NetworkEnemySkeletonExample.prefab", Content + "NetworkEnemyLustSinner.prefab", Content + "NetworkEnemyImp.prefab" };
            var before = paths.ToDictionary(p => p, File.ReadAllBytes);
            EnsureDefault();
            foreach (var path in paths) if (!before[path].SequenceEqual(File.ReadAllBytes(path))) throw new InvalidOperationException("Repeated setup changed " + path);
            File.WriteAllText("Logs/TimelineWaves/repeat.txt", "PASS: repeated setup preserves authored Timeline, rules and all enemy Prefabs.");
        }
    }

    [CustomEditor(typeof(GameplayWaveRules))]
    public sealed class GameplayWaveRulesInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var rules = (GameplayWaveRules)target;
            if (rules.TryCapture(out var captured, out var error))
                EditorGUILayout.HelpBox($"{captured.Program.WaveCount} authored waves; the last {rules.WaveDuration} seconds repeat. Clip spacing = duration / count.", MessageType.Info);
            else EditorGUILayout.HelpBox(error, MessageType.Error);
            if (GUILayout.Button("Open Timeline") && rules.Timeline != null) AssetDatabase.OpenAsset(rules.Timeline);
            if (GUILayout.Button("Validate Default Gameplay / Export 6 Waves")) NetworkWaveTimelineEditorUtility.ValidateConfigured();
        }
    }
}
