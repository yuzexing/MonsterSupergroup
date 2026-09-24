using System;
using System.IO;
using MonsterSupergroup.Builds;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace MonsterSupergroup.EditorTools
{
    public sealed class ProjectBuildWindow : EditorWindow
    {
        [SerializeField] private BuildProfile profile;
        [SerializeField] private VersionUpdate update = VersionUpdate.Patch;
        [SerializeField] private string output;
        [SerializeField] private bool cleanBuildCache, runAfterBuild;
        [SerializeField] private Vector2 scroll;
        private ResolvedProjectBuild preview;
        private Editor settingsEditor;
        private string feedback;
        private EditorApplication.CallbackFunction pendingBuild;
        [MenuItem("MonsterSupergroup/构建与验收/构建配置…", priority = 29)]
        public static void Open() { var window = GetWindow<ProjectBuildWindow>("构建配置"); window.minSize = new Vector2(610, 650); }
        public static void OpenValidation(BuildKind kind = BuildKind.Dev)
        {
            Open(); var window = GetWindow<ProjectBuildWindow>();
            window.profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProjectBuildTemplates.Root + "/Windows-Dev-Gameplay.asset");
            window.preview = null;
            if (kind == BuildKind.Test) window.feedback = "专项 Test：请复制专项 Profile，将业务类型设为 Test，并在原生窗口关闭 Development。";
        }
        public static string PreviewVersion(string current, VersionUpdate update) => GameVersion.Parse(current).Increment(update).ToString();
        public static void ApplyVersion(string expectedCurrent, VersionUpdate update) => NativeBuildProfileSettings.UpdateVersion(expectedCurrent, PreviewVersion(expectedCurrent, update));
        private void OnDisable()
        {
            EditorApplication.delayCall -= pendingBuild;
            pendingBuild = null;
            if (settingsEditor != null) DestroyImmediate(settingsEditor);
        }
        private void Action(Action action) { try { action(); } catch (Exception e) { feedback = e.Message; Debug.LogException(e); } }
        private void Refresh() { preview = ProjectBuildResolver.Resolve(profile, true); feedback = "已刷新只读计划。"; }
        private void QueueBuild()
        {
            if (pendingBuild != null) return;
            var selectedProfile = profile;
            var request = new BuildExecutionRequest { output = output, cleanBuildCache = cleanBuildCache,
                runAfterBuild = runAfterBuild, expectedContentHash = preview.ContentHash, expectedInputHash = preview.InputHash };
            EditorApplication.CallbackFunction callback = null;
            callback = () => {
                if (this == null || pendingBuild != callback) return;
                try
                {
                    Action(() => {
                        string result = ProjectBuildService.Build(selectedProfile, request);
                        feedback = "成功：" + result + (ProjectBuildService.LastLaunchError == null ? "" : "\n启动失败：" + ProjectBuildService.LastLaunchError);
                    });
                }
                finally { pendingBuild = null; if (this != null) Repaint(); }
            };
            pendingBuild = callback;
            feedback = "准备构建…";
            // Building inside OnGUI can invalidate Unity's active layout groups.
            EditorApplication.delayCall += callback;
        }
        private void OnGUI()
        {
            using var view = new EditorGUILayout.ScrollViewScope(scroll); scroll = view.scrollPosition;
            using var pendingScope = new EditorGUI.DisabledScope(pendingBuild != null);
            try { DrawVersion(); } catch (Exception e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); }
            var selected = (BuildProfile)EditorGUILayout.ObjectField("原生 Build Profile", profile, typeof(BuildProfile), false);
            if (selected != profile) { profile = selected; preview = null; if (settingsEditor != null) DestroyImmediate(settingsEditor); }
            if (GUILayout.Button("打开 Unity Build Profiles（原生设置）")) EditorApplication.ExecuteMenuItem("File/Build Profiles");
            if (profile != null)
            {
                var settings = profile.GetComponent<MonsterBuildSettings>();
                if (settings == null)
                {
                    EditorGUILayout.HelpBox("此 Profile 尚未附加项目业务配置。", MessageType.Warning);
                    if (GUILayout.Button("附加 MonsterBuildSettings")) Action(() => { profile.CreateComponent<MonsterBuildSettings>(); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile); });
                }
                else
                {
                    Editor.CreateCachedEditor(settings, null, ref settingsEditor);
                    EditorGUI.BeginChangeCheck(); settingsEditor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck()) preview = null;
                    bool busy = EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode;
                    using (new EditorGUI.DisabledScope(busy))
                    {
                        if (GUILayout.Button("保存业务配置并刷新摘要")) Action(() => { AssetDatabase.SaveAssetIfDirty(settings); AssetDatabase.SaveAssetIfDirty(profile); Refresh(); });
                        if (GUILayout.Button("刷新只读计划")) Action(Refresh);
                        if (preview != null)
                        {
                            string difference = ProjectBuildDefines.Difference(profile.scriptingDefines, preview.Defines);
                            EditorGUILayout.HelpBox(difference.Length == 0 ? "受管理符号一致。" : difference, difference.Length == 0 ? MessageType.Info : MessageType.Warning);
                        }
                        if (GUILayout.Button("应用受管理符号并保存")) Action(() => { AssetDatabase.SaveAssetIfDirty(settings); ProjectBuildDefines.Apply(profile); preview = null; feedback = "已应用；请等待编译完成后刷新计划。"; });
                        using (new EditorGUI.DisabledScope(BuildProfile.GetActiveBuildProfile() == profile))
                            if (GUILayout.Button("激活此 Profile（等待编译，不自动构建）")) Action(() => { preview = null; BuildProfile.SetActiveBuildProfile(profile); });
                    }
                    EditorGUILayout.LabelField("状态", busy ? "编译／导入／运行中" : BuildProfile.GetActiveBuildProfile() == profile ? "当前已激活" : "尚未激活");
                    if (preview != null)
                    {
                        EditorGUILayout.HelpBox(preview.Summary, MessageType.Info);
                        output = EditorGUILayout.TextField("输出 EXE（留空使用默认）", output);
                        cleanBuildCache = EditorGUILayout.Toggle("清理构建缓存", cleanBuildCache);
                        runAfterBuild = EditorGUILayout.Toggle("完成后运行（仅 Direct）", runAfterBuild);
                        // Display-only placeholders must never be passed to Windows Path APIs.
                        EditorGUILayout.LabelField("交付目录", "Builds/" + profile.name + "/MonsterSupergroup-v…-<BuildId>/", EditorStyles.wordWrappedLabel);
                        using (new EditorGUI.DisabledScope(busy || BuildProfile.GetActiveBuildProfile() != profile))
                            if (GUILayout.Button("按此计划构建")) Action(QueueBuild);
                    }
                }
            }
            if (GUILayout.Button("构建指南与旧调用审查清单")) EditorUtility.RevealInFinder(Path.Combine(ProjectToolCatalog.ProjectRoot, "docs/build-guide.md"));
            if (!string.IsNullOrEmpty(feedback)) EditorGUILayout.HelpBox(feedback, MessageType.Info);
        }
        private void DrawVersion()
        {
            string current = NativeBuildProfileSettings.GlobalVersion;
            EditorGUILayout.LabelField("全局游戏版本（构建不递增）", current);
            update = (VersionUpdate)EditorGUILayout.EnumPopup("版本更新", update);
            EditorGUILayout.LabelField("更新后", PreviewVersion(current, update));
            if (GUILayout.Button("应用版本更新")) Action(() => { ApplyVersion(current, update); preview = null; });
            EditorGUILayout.Space();
        }
    }
}
