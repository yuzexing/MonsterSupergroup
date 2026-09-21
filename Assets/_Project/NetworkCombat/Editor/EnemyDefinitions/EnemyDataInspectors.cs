using System;
using MonsterSupergroup.Gameplay.Combat.Content;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public abstract class EnemyDataInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Enemy content is captured for the running session. Stop Play Mode to edit the shared template.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) DrawDefaultInspector();
            foreach (var item in targets)
            {
                try
                {
                    if (item is EnemyStatsDefinition stats) stats.Capture();
                    if (item is EnemyAppearanceDefinition appearance) appearance.Validate();
                }
                catch (Exception error) { EditorGUILayout.HelpBox(item.name + ": " + error.Message, MessageType.Error); }
            }
        }
    }
    [CustomEditor(typeof(EnemyStatsDefinition)), CanEditMultipleObjects]
    public sealed class EnemyStatsInspector : EnemyDataInspector { }
    [CustomEditor(typeof(EnemyAppearanceDefinition)), CanEditMultipleObjects]
    public sealed class EnemyAppearanceInspector : EnemyDataInspector { }
}
