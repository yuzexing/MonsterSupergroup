using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MonsterSupergroup.Gameplay.Combat.Content;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    // Explicit GUI acceptance fixture. No automatic startup or production asset rewrites.
    public static class EnemyDefinitionGuiVerification
    {
        private const string Folder = "Assets/__EnemyDefinitionGuiVerification";
        private const string TimelinePath = Folder + "/GUI.playable";
        private const string Log = "Logs/EnemyDefinitions/Gui";
        private static double nextPoll;
        private static Type TimelineEditorType => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("UnityEditor.Timeline.TimelineEditor", false)).First(t => t != null);
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            Directory.CreateDirectory(Log);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__EnemyDefinitionGuiVerification");
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>(); timeline.durationMode = TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration = 30;
                AssetDatabase.CreateAsset(timeline, TimelinePath);
                var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(null, "GUI selection verification");
                var clip = track.CreateClip<NetworkEnemySpawnClip>(); clip.start = 1; clip.duration = 12;
                EditorUtility.SetDirty(timeline); AssetDatabase.SaveAssets();
            }
            SelectClip();
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }
        [MenuItem("Tools/MonsterSupergroup/Enemies/GUI Verification/Select Clip %&1")]
        public static void SelectClip()
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            AssetDatabase.OpenAsset(timeline);
            var clip = EnemyDefinitionMigration.Clips(timeline).Single();
            EditorApplication.delayCall += () => {
                TimelineEditorType.GetProperty("selectedClip", BindingFlags.Public | BindingFlags.Static).SetValue(null, clip);
                EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.InspectorWindow")).Focus();
            };
        }
        private static EnemyDefinition SelectedDefinition() => AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath)
            ?.GetRootTracks().SelectMany(t => t.GetClips()).Select(c => c.asset).OfType<NetworkEnemySpawnClip>().Single().Enemy;
        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            try
            {
                string commandPath = Log + "/command.txt";
                if (File.Exists(commandPath))
                {
                    string command = File.ReadAllText(commandPath).Trim(); File.Delete(commandPath);
                    if (command.StartsWith("click|", StringComparison.Ordinal))
                    {
                        var args = command.Split('|');
                        var window = Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.GetType().Name == args[1]);
                        var position = new Vector2(float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture));
                        window.Focus();
                        window.SendEvent(new Event { type = EventType.MouseDown, mousePosition = position, button = 0 });
                        window.SendEvent(new Event { type = EventType.MouseUp, mousePosition = position, button = 0 });
                    }
                    else if (command.StartsWith("type|", StringComparison.Ordinal))
                    {
                        var args = command.Split('|');
                        var window = Resources.FindObjectsOfTypeAll<EditorWindow>().First(w => w.GetType().Name == args[1]);
                        window.Focus();
                        foreach (char c in args[2]) window.SendEvent(new Event { type = EventType.KeyDown, character = c });
                        window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return });
                    }
                    else switch (command)
                    {
                        case "new": EnemyDefinitionEditorUtility.CreateFromMenu(); break;
                        case "clip": SelectClip(); break;
                        case "stats": Selection.activeObject = (Selection.activeObject as EnemyDefinition ?? SelectedDefinition())?.Stats; break;
                        case "definition": Selection.activeObject = SelectedDefinition(); break;
                        case "verify": Verify(); break;
                        case "cleanup": Cleanup(); EditorApplication.Exit(0); return;
                        case "quit": EditorApplication.update -= Poll; EditorApplication.Exit(0); return;
                        default: throw new ArgumentException("Unknown GUI probe command: " + command);
                    }
                }
                File.WriteAllLines(Log + "/windows.txt", Resources.FindObjectsOfTypeAll<EditorWindow>().Select(w => w.GetType().Name + " " + w.position));
                var definition = SelectedDefinition();
                File.WriteAllText(Log + "/state.json", JsonUtility.ToJson(new State {
                    selection = AssetDatabase.GetAssetPath(Selection.activeObject), enemy = definition?.DisplayName,
                    id = definition?.IdText, hp = definition?.Stats != null ? definition.Stats.Capture().Health : 0,
                    prefab = AssetDatabase.GetAssetPath(definition?.Prefab) }, true));
            }
            catch (Exception error) { File.WriteAllText(Log + "/error.txt", error.ToString()); Debug.LogException(error); }
        }
        private static void Verify()
        {
            var definition = SelectedDefinition() ?? throw new InvalidOperationException("Choose an enemy through the Timeline dropdown first.");
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            var clip = (NetworkEnemySpawnClip)EnemyDefinitionMigration.Clips(timeline).Single().asset;
            if (clip.AuthoringVersion != 1) throw new InvalidOperationException("Selection did not enable definition schema.");
            EnemyDefinitionEditorUtility.ValidateDefinition(definition);
            AssetDatabase.SaveAssetIfDirty(definition.Stats); AssetDatabase.SaveAssetIfDirty(definition);
            AssetDatabase.SaveAssetIfDirty(timeline); AssetDatabase.SaveAssetIfDirty(clip);
            string id = definition.IdText;
            AssetDatabase.ImportAsset(TimelinePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            if (SelectedDefinition()?.IdText != id) throw new InvalidOperationException("Saved dropdown reference did not survive import.");
            File.WriteAllText(Log + "/verified.json", JsonUtility.ToJson(new State { selection = TimelinePath,
                enemy = definition.DisplayName, id = id, hp = definition.Stats.Capture().Health,
                prefab = AssetDatabase.GetAssetPath(definition.Prefab) }, true));
            Debug.Log("[EnemyDefinitionsGUI] saved/reimported real Timeline selection: " + definition.DisplayName);
        }
        private static void Cleanup()
        {
            var catalog = EnemyDefinitionEditorUtility.Catalog;
            catalog.SetAuthoringDefinitions(catalog.Definitions.Where(d => d != null && !AssetDatabase.GetAssetPath(d).StartsWith(Folder + "/", StringComparison.Ordinal)).ToArray());
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
            Selection.activeObject = null; AssetDatabase.DeleteAsset(Folder);
            EnemyDefinitionEditorUtility.RefreshContentHashes();
            EditorApplication.update -= Poll;
        }
        [Serializable] private class State { public string selection, enemy, id, prefab; public int hp; }
    }
}
