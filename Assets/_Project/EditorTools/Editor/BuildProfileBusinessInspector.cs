using System;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace MonsterSupergroup.EditorTools
{
    [InitializeOnLoad]
    internal static class BuildProfileBusinessInspector
    {
        private static bool savePending;
        private static BuildProfile feedbackProfile;
        private static string feedback;
        private static MessageType feedbackType;

        static BuildProfileBusinessInspector()
        {
            Editor.finishedDefaultHeaderGUI -= DrawHeader;
            Editor.finishedDefaultHeaderGUI += DrawHeader;
        }

        private static bool IsBusy => EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || ProjectBuildService.ActivePlan != null;

        private static void DrawHeader(Editor editor)
        {
            if (editor.target is not BuildProfile profile || !EditorUtility.IsPersistent(profile)) return;
            EditorGUILayout.LabelField("项目业务配置", EditorStyles.boldLabel);
            if (editor.targets.Length != 1)
            {
                EditorGUILayout.HelpBox("请单选一个 Profile 查看和编辑业务配置。", MessageType.Info);
                return;
            }
            var settings = profile.GetComponent<MonsterBuildSettings>();
            if (settings == null)
            {
                EditorGUILayout.HelpBox("此 Profile 尚未附加项目业务配置；如需添加，请使用构建配置窗口。", MessageType.Warning);
                return;
            }
            if (settings.SchemaVersion != 1)
            {
                EditorGUILayout.HelpBox("不支持的业务配置 SchemaVersion：" + settings.SchemaVersion + "。未修改此资产。", MessageType.Error);
                return;
            }

            bool busy = IsBusy;
            using (new EditorGUI.DisabledScope(busy || savePending))
            {
                using var serializedSettings = new SerializedObject(settings);
                MonsterBuildSettingsEditor.DrawFields(serializedSettings);
                if (GUILayout.Button("保存 Profile")) QueueSave(profile, editor);
            }
            if (busy) EditorGUILayout.HelpBox("编译、导入、运行或构建期间不能编辑和保存业务配置。", MessageType.Info);
            EditorGUILayout.HelpBox(MonsterBuildSettingsEditor.PreparationHint, MessageType.Info);
            if (feedbackProfile == profile && !string.IsNullOrEmpty(feedback)) EditorGUILayout.HelpBox(feedback, feedbackType);
        }

        private static void QueueSave(BuildProfile profile, Editor editor)
        {
            if (savePending) return;
            savePending = true;
            feedbackProfile = profile;
            feedback = "准备保存…";
            feedbackType = MessageType.Info;
            EditorApplication.delayCall += () => {
                try
                {
                    if (profile == null || !EditorUtility.IsPersistent(profile) || !AssetDatabase.Contains(profile))
                        throw new InvalidOperationException("待保存的 Profile 已不存在，请重新选择。");
                    if (IsBusy) throw new InvalidOperationException("编辑器正在编译、导入、运行或构建，请等待结束后重新保存。");
                    var settings = profile.GetComponent<MonsterBuildSettings>();
                    if (settings == null || settings.SchemaVersion != 1)
                        throw new InvalidOperationException("业务配置已缺失或 SchemaVersion 不受支持，未执行保存。");
                    // Save only the containing asset, after the Inspector has finished drawing.
                    AssetDatabase.SaveAssetIfDirty(settings);
                    AssetDatabase.SaveAssetIfDirty(profile);
                    if (EditorUtility.IsDirty(settings) || EditorUtility.IsDirty(profile))
                        throw new InvalidOperationException("Profile 仍有未保存修改，请检查 Console 中的保存错误。");
                    feedback = "已保存：" + AssetDatabase.GetAssetPath(profile);
                    feedbackType = MessageType.Info;
                }
                catch (Exception exception)
                {
                    feedback = "保存失败：" + exception.Message;
                    feedbackType = MessageType.Error;
                    Debug.LogException(exception);
                }
                finally
                {
                    savePending = false;
                    if (editor != null) editor.Repaint();
                }
            };
        }
    }
}
