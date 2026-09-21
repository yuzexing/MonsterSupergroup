using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
namespace MonsterSupergroup.NetworkCombat.Editor
{
    /// <summary>Opt-in, editor-only normal battle. Never saves Boot or changes the user's wave asset.</summary>
    [InitializeOnLoad]
    public static class GluttonyPrototypePlaytest
    {
        private const string Key = "GluttonyPrototype.ManualPlaytest";
        private const string Boot = "Assets/_Project/Scenes/Boot.unity";
        private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
        private static int phase;
        private static double deadline;
        static GluttonyPrototypePlaytest()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Update;
        }
        [MenuItem("Tools/MonsterSupergroup/Prototypes/Gluttony/Play Normal Enemies (Offline)")]
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play Mode and finish compiling first.");
            var boot = SceneManager.GetSceneByPath(Boot);
            if (!boot.IsValid() || !boot.isLoaded)
                throw new InvalidOperationException("Open Assets/_Project/Scenes/Boot.unity first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save your scene edits before starting this playtest.");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
        // Explicit command-line entry for a newly launched validation editor, not a background auto-start.
        public static void StartFromCommandLine()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene changes.");
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Boot);
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
            var game = EditorWindow.GetWindow(type); game.Show(); game.Focus();
            EditorApplication.delayCall += Start;
        }
        private static void OnPlayMode(PlayModeStateChange mode)
        {
            if (mode == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                phase = 1; deadline = EditorApplication.timeSinceStartup + 120;
                SceneManager.sceneLoaded += ConfigureBattle;
            }
            if (mode != PlayModeStateChange.ExitingPlayMode) return;
            SessionState.EraseBool(Key); phase = 0;
            SceneManager.sceneLoaded -= ConfigureBattle;
            // The compiled wave schedule owns copies, not these temporary authoring objects.
            for (int i = temporary.Count - 1; i >= 0; i--)
                if (temporary[i] != null) UnityEngine.Object.DestroyImmediate(temporary[i]);
            temporary.Clear();
        }
        private static void Update()
        {
            if (phase == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Gluttony playtest startup timed out.");
                var manager = NetworkManager.singleton as BootGameplayNetworkManager;
                if (manager == null) return;
                if (phase == 1)
                {
                    manager.ConfigurePreparationFlow(true);
                    if (!manager.TryStartOfflineRoom(out var error)) throw new InvalidOperationException(error);
                    phase = 2;
                }
                if (phase == 2 && manager.RoomSnapshot.Phase == PreparationPhase.Preparing)
                { manager.SetOwnLoadout(6); phase = 3; }
                if (phase == 3 && manager.RoomSnapshot.Members.Length > 0 && manager.RoomSnapshot.Members[0].WeaponId == 6)
                { manager.StartPreparedGame(); phase = 4; }
                if (phase != 4 || manager.RoomSnapshot.Phase != PreparationPhase.InGame || NetworkClient.localPlayer == null) return;
                var skill = NetworkClient.localPlayer.GetComponent<NetworkPlayerGluttony>();
                if (skill == null) throw new InvalidOperationException("Install the Gluttony prototype on NetworkPlayer first.");
                if (!skill.Parameters.IsValid) return;
                var settings = skill.Parameters; settings.Enabled = settings.PassiveEnabled = settings.ActiveEnabled = true;
                NetworkCombatWorld.Instance.ServerConfigureGluttony(settings, true);
                phase = 0; SessionState.EraseBool(Key);
                Debug.Log("[Gluttony] Manual normal-enemy playtest ready. R aims at the mouse; use the lower-right settings panel for OFF / Passive / Both.");
            }
            catch (Exception error)
            {
                phase = 0; SessionState.EraseBool(Key); SceneManager.sceneLoaded -= ConfigureBattle;
                Debug.LogException(error);
            }
        }
        private static void ConfigureBattle(Scene scene, LoadSceneMode mode)
        {
            var manager = NetworkManager.singleton as BootGameplayNetworkManager;
            if (manager == null || scene.path != manager.GameplayScene) return;
            var definition = manager.EnemyCatalog.Definitions.Where(d => d.Prefab != null && d.Prefab.name == "ReferenceBrotchi")
                .OrderByDescending(d => d.Stats.Capture().Health).FirstOrDefault();
            if (definition == null) throw new InvalidOperationException("The playtest requires the existing ReferenceBrotchi definition.");
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>(); Keep(timeline);
            timeline.name = "Gluttony temporary normal battle";
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
        private static void Keep(UnityEngine.Object value) { value.hideFlags = HideFlags.DontSave; temporary.Add(value); }
        private static void Set(UnityEngine.Object target, string name, object value)
        {
            var field = target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }
    }
}
