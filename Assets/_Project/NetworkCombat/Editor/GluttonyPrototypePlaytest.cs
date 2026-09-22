using System;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MonsterSupergroup.NetworkCombat.Editor
{
    /// <summary>Editor-only prototype battle. Never saves Boot or changes the user's wave asset.</summary>
    [InitializeOnLoad]
    public static class GluttonyPrototypePlaytest
    {
        private const string Key = "GluttonyPrototype.ManualPlaytest";
        private const string Boot = "Assets/_Project/Scenes/Boot.unity";
        private static PrototypePlaytestBattle battle;
        private static int phase;
        private static double deadline;
        static GluttonyPrototypePlaytest()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Update;
        }
        [MenuItem("Tools/MonsterSupergroup/Prototypes/Play Normal Enemies (Offline)")]
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
                battle?.Dispose(); battle = new PrototypePlaytestBattle();
            }
            if (mode != PlayModeStateChange.ExitingPlayMode) return;
            SessionState.EraseBool(Key); phase = 0;
            battle?.Dispose(); battle = null;
        }
        private static void Update()
        {
            if (phase == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                if (battle?.Error != null) throw new InvalidOperationException(battle.Error);
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Prototype playtest startup timed out.");
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
                var abilities = NetworkClient.localPlayer.GetComponent<NetworkPlayerPrototypeAbilities>();
                if (skill == null) throw new InvalidOperationException("Install the Gluttony prototype on NetworkPlayer first.");
                if (abilities == null) throw new InvalidOperationException("Install the prototype ability selector on NetworkPlayer first.");
                if (!skill.Parameters.IsValid) return;
                var settings = skill.Parameters; settings.Enabled = settings.PassiveEnabled = settings.ActiveEnabled = true;
                abilities.ServerSetEnabled(true);
                NetworkCombatWorld.Instance.ServerConfigureGluttony(settings, true);
                phase = 0; SessionState.EraseBool(Key);
                Debug.Log("[Prototypes] Normal-enemy playtest ready. 1 = Gluttony, 2 = Music, 3 = Allure. R marks toward the mouse while Gluttony is selected. Music: R starts, Space plays ten beats, even after switching. Allure: R throw / T take require two players; F places a decoy in solo or multiplayer. Existing marks remain collectable after switching. Use the prototype panel for session settings.");
            }
            catch (Exception error)
            {
                phase = 0; SessionState.EraseBool(Key); battle?.Dispose(); battle = null;
                Debug.LogException(error);
            }
        }
    }
}
