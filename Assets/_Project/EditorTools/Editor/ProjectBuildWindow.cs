using System;
using System.Linq;
using MonsterSupergroup.Builds;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.EditorTools
{
    public sealed class ProjectBuildWindow : EditorWindow
    {
        [SerializeField] private VersionUpdate update = VersionUpdate.Patch;
        [SerializeField] private BuildKind kind = BuildKind.Test;
        [SerializeField] private BuildNetwork network = BuildNetwork.Steam;
        [SerializeField] private BuildDistribution distribution = BuildDistribution.Steam;
        [SerializeField] private BuildDiagnostics diagnostics;
        [SerializeField] private int page;
        [SerializeField] private bool advanced, overrideDevelopment, development;
        [SerializeField] private string recipe = "gameplay-validation";
        [SerializeField] private Vector2 scroll;
        private string feedback;

        [MenuItem("MonsterSupergroup/构建与验收/构建配置…", priority = 29)]
        public static void Open() { var window = GetWindow<ProjectBuildWindow>("构建配置"); window.minSize = new Vector2(570, 570); }
        public static void OpenValidation(BuildKind type = BuildKind.Dev)
        {
            Open(); var window = GetWindow<ProjectBuildWindow>(); window.page = 1; window.kind = type;
            window.network = BuildNetwork.Kcp; window.distribution = BuildDistribution.Direct;
            window.overrideDevelopment = false; window.diagnostics = BuildDiagnostics.Normal;
        }
        public static string PreviewVersion(string current, VersionUpdate update) => GameVersion.Parse(current).Increment(update).ToString();
        public static void ApplyVersion(string expectedCurrent, VersionUpdate update)
        {
            if (PlayerSettings.bundleVersion != expectedCurrent) throw new InvalidOperationException("当前版本已改变，请重新查看预览。");
            PlayerSettings.bundleVersion = PreviewVersion(expectedCurrent, update);
            AssetDatabase.SaveAssets();
        }
        private void OnGUI()
        {
            using var view = new EditorGUILayout.ScrollViewScope(scroll); scroll = view.scrollPosition;
            DrawVersion();
            int nextPage = GUILayout.Toolbar(page, new[] { "日常打包", "专项验收" });
            if (nextPage != page)
            {
                page = nextPage; kind = page == 0 ? BuildKind.Test : BuildKind.Dev;
                network = page == 0 ? BuildNetwork.Steam : BuildNetwork.Kcp;
                distribution = page == 0 ? BuildDistribution.Steam : BuildDistribution.Direct;
                diagnostics = BuildDiagnostics.Normal; overrideDevelopment = false;
            }
            var profiles = ProjectToolCatalog.Load().builds.Where(b => !b.product).ToArray();
            if (page == 1)
            {
                int index = Math.Max(0, Array.FindIndex(profiles, p => p.id == recipe));
                recipe = profiles[EditorGUILayout.Popup("专项配方", index, profiles.Select(p => p.name).ToArray())].id;
                EditorGUILayout.HelpBox("专项包含测试能力，不用于正式发行。敌人、武器及 Limbo 片段在运行时选择。", MessageType.Warning);
            }
            var selectedKind = page == 0 ? (BuildKind)EditorGUILayout.EnumPopup("构建类型", kind) :
                (BuildKind)EditorGUILayout.Popup("构建类型", Math.Min((int)kind, 1), new[] { "Dev", "Test" });
            if (selectedKind != kind) { kind = selectedKind; overrideDevelopment = false; }
            if (kind == BuildKind.Shipping)
            { network = BuildNetwork.Steam; distribution = BuildDistribution.Steam; diagnostics = BuildDiagnostics.Normal; overrideDevelopment = false; }
            using (new EditorGUI.DisabledScope(kind == BuildKind.Shipping))
            {
                network = (BuildNetwork)EditorGUILayout.EnumPopup("网络模式", network);
                if (network == BuildNetwork.Kcp) distribution = BuildDistribution.Direct;
                using (new EditorGUI.DisabledScope(network == BuildNetwork.Kcp))
                    distribution = (BuildDistribution)EditorGUILayout.Popup("启动／分发方式", (int)distribution, new[] { "Steam 分发（从 Steam 启动）", "本地直接启动" });
                if (kind != BuildKind.Test) diagnostics = BuildDiagnostics.Normal;
                if (page == 0)
                    using (new EditorGUI.DisabledScope(kind != BuildKind.Test))
                        diagnostics = (BuildDiagnostics)EditorGUILayout.Popup("记录用途", (int)diagnostics, new[] { "普通日志", "故障取证（产品 Test，无作弊）" });
                advanced = EditorGUILayout.Foldout(advanced, "高级选项");
                if (advanced)
                {
                    bool previous = overrideDevelopment;
                    overrideDevelopment = EditorGUILayout.Toggle("覆盖 Development 默认", overrideDevelopment);
                    if (!previous && overrideDevelopment) development = kind == BuildKind.Dev;
                    using (new EditorGUI.DisabledScope(!overrideDevelopment))
                        development = EditorGUILayout.Toggle("Development Build", overrideDevelopment ? development : kind == BuildKind.Dev);
                    EditorGUILayout.HelpBox("Development 用于调试信息和性能分析。普通 Test 开启它，也不会获得作弊或机制验证能力。", MessageType.Info);
                }
            }
            ResolvedProjectBuild resolved = null;
            string dev = overrideDevelopment ? development.ToString() : null;
            try { resolved = ProjectBuildResolver.Resolve(page == 0 ? "product" : recipe, kind.ToString(), dev, network.ToString(), distribution.ToString(), diagnostics.ToString()); }
            catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
            if (resolved != null)
            {
                EditorGUILayout.Space(); EditorGUILayout.LabelField("本次实际构建", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"v{PlayerSettings.bundleVersion}-{kind.ToString().ToLowerInvariant()} · BuildId 在构建时生成", EditorStyles.wordWrappedLabel);
                EditorGUILayout.HelpBox(resolved.Summary, MessageType.Info);
                EditorGUILayout.LabelField("输出", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(resolved.Recipe.output), "MonsterSupergroup-v…-<BuildId>", System.IO.Path.GetFileName(resolved.Recipe.output)), EditorStyles.wordWrappedLabel);
                if (kind == BuildKind.Shipping) EditorGUILayout.HelpBox("发行前提交版本、源码和资源更改；工具只检查，不会替你提交或上传 Steam。", MessageType.Warning);
                string unavailable = ProjectToolRunner.Availability(ProjectToolCatalog.Find("build.player"));
                if (unavailable.Length != 0) EditorGUILayout.HelpBox(unavailable, MessageType.Info);
                using (new EditorGUI.DisabledScope(unavailable.Length != 0))
                    if (GUILayout.Button("使用当前版本构建", GUILayout.Height(32)))
                    {
                        var result = ProjectToolRunner.Run("build.player", new ProjectToolRequest { profile = resolved.Recipe.id, buildKind = kind.ToString(), development = dev,
                            network = network.ToString(), distribution = distribution.ToString(), diagnostics = diagnostics.ToString() });
                        feedback = result.success ? "构建成功，请使用本次唯一目录：\n" + string.Join("\n", result.artifacts) : result.error;
                    }
            }
            if (GUILayout.Button("打包速查与旧配置映射")) EditorUtility.RevealInFinder(System.IO.Path.Combine(ProjectToolCatalog.ProjectRoot, "docs/build-guide.md"));
            if (!string.IsNullOrEmpty(feedback)) EditorGUILayout.HelpBox(feedback, MessageType.Info);
        }
        private void DrawVersion()
        {
            string current = PlayerSettings.bundleVersion;
            EditorGUILayout.LabelField("游戏版本（构建不自动递增）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("当前版本", current);
            update = (VersionUpdate)EditorGUILayout.Popup("更新类型", (int)update, new[] { "普通小更新（末位 +1）", "重大更新（中位 +1，末位归零）", "大版本更新（首位 +1，其余归零）" });
            string next = null;
            try { next = PreviewVersion(current, update); }
            catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
            EditorGUILayout.LabelField("更新后版本", next ?? "无效");
            using (new EditorGUI.DisabledScope(next == null || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer))
                if (GUILayout.Button("应用版本更新"))
                    try { ApplyVersion(current, update); feedback = "已应用版本 " + PlayerSettings.bundleVersion + "；Shipping 前须提交此配置修改。"; }
                    catch (Exception error) { feedback = error.Message; }
            EditorGUILayout.Space();
        }
    }
}
