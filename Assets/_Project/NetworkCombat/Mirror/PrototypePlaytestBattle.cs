using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Opt-in temporary ordinary-enemy waves, shared by editor and standalone manual playtests.</summary>
    public sealed class PrototypePlaytestBattle : IDisposable
    {
        private readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
        public string Error { get; private set; }
        public PrototypePlaytestBattle() => SceneManager.sceneLoaded += ConfigureBattle;
        private void ConfigureBattle(Scene scene, LoadSceneMode mode)
        {
            var manager = NetworkManager.singleton as BootGameplayNetworkManager;
            if (manager == null || scene.path != manager.GameplayScene) return;
            try
            {
                var definition = manager.EnemyCatalog.Definitions.Where(d => d.Prefab != null && d.Prefab.name == "ReferenceBrotchi")
                    .OrderByDescending(d => d.Stats.Capture().Health).FirstOrDefault();
                if (definition == null) throw new InvalidOperationException("The playtest requires the existing ReferenceBrotchi definition.");
                var timeline = ScriptableObject.CreateInstance<TimelineAsset>(); Keep(timeline);
                timeline.name = "Prototypes temporary normal battle";
                timeline.durationMode = TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration = 181;
                var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(null, "Normal enemies"); Keep(track);
                for (int wave = 0; wave < 3; wave++)
                {
                    var clip = track.CreateClip<NetworkEnemySpawnClip>(); clip.start = 1 + wave * 60; clip.duration = 60;
                    var spawn = (NetworkEnemySpawnClip)clip.asset; Keep(spawn);
                    Set(spawn, "enemy", definition); Set(spawn, "authoringVersion", 1);
                    spawn.referenceMode = ReferenceSpawnMode.CurveBudget; spawn.count = 48 + wave * 16;
                    spawn.spawnCurve = AnimationCurve.Constant(0, 1, 1); spawn.speedMultipliers = Vector2.one;
                    spawn.contactRadius = .6f; spawn.expiresOffscreen = false;
                }
                var rules = ScriptableObject.CreateInstance<GameplayWaveRules>(); Keep(rules);
                Set(rules, "timeline", timeline); Set(rules, "referenceStage", true);
                Set(rules, "referenceEndTime", 181d); Set(rules, "referenceSourceDuration", 181d);
                Set(rules, "referenceXpAmplitude", 0f); Set(rules, "positionAttempts", 100); Set(rules, "maximumAlive", 120);
                foreach (var spawner in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true)))
                    spawner.ConfigureWaveRules(rules);
            }
            catch (Exception error) { Error = error.Message; Debug.LogException(error); }
        }
        private void Keep(UnityEngine.Object value) { value.hideFlags = HideFlags.DontSave; temporary.Add(value); }
        private static void Set(UnityEngine.Object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }
        public void Dispose()
        {
            SceneManager.sceneLoaded -= ConfigureBattle;
            for (int i = temporary.Count - 1; i >= 0; i--)
                if (temporary[i] != null) UnityEngine.Object.DestroyImmediate(temporary[i]);
            temporary.Clear();
        }
    }
}
