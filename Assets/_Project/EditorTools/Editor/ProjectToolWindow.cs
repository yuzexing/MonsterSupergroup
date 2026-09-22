using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MonsterSupergroup.EditorTools
{
    public sealed class ProjectToolWindow : EditorWindow
    {
        [SerializeField] private string search = "", selected = "validate.all", category = "全部";
        [SerializeField] private bool maintenance;
        [SerializeField] private Vector2 listScroll, detailScroll;
        private ProjectToolManifest manifest;
        private readonly Dictionary<string, string> parameters = new Dictionary<string, string>();
        private Process process;
        private string processReport;
        private ProjectToolResult processResult;

        public static void Open(string filter = "", bool showMaintenance = false)
        {
            var window = GetWindow<ProjectToolWindow>("项目工具中心", true, typeof(SceneView));
            window.minSize = new Vector2(820, 540);
            window.category = string.IsNullOrEmpty(filter) ? "全部" : filter;
            window.maintenance = showMaintenance;
            if (!string.IsNullOrEmpty(filter))
                window.selected = ProjectToolCatalog.Load().tools.First(t => t.category == filter).id;
            window.Show();
        }
        private void OnEnable() { manifest = ProjectToolCatalog.Load(); }
        private void OnInspectorUpdate()
        {
            if (process != null && process.HasExited)
            {
                processResult = File.Exists(processReport) ? JsonUtility.FromJson<ProjectToolResult>(File.ReadAllText(processReport)) :
                    new ProjectToolResult { id = selected, error = "验收进程退出但未生成结果。", success = false };
                if (process.ExitCode != 0) { processResult.success = false; processResult.error += "\n进程退出码: " + process.ExitCode; }
                process.Dispose(); process = null;
            }
            Repaint();
        }
        private string Value(string key, string fallback = "") => parameters.TryGetValue(key, out var value) ? value : fallback;
        private void OnGUI()
        {
            if (manifest == null) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search = EditorGUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
                maintenance = GUILayout.Toggle(maintenance, "包含维护与样例", EditorStyles.toolbarButton, GUILayout.Width(125));
                if (GUILayout.Button("刷新清单", EditorStyles.toolbarButton, GUILayout.Width(80))) manifest = ProjectToolCatalog.Load();
            }
            string[] categories = new[] { "全部" }.Concat(manifest.tools.Select(t => t.category).Distinct()).ToArray();
            int index = Math.Max(0, Array.IndexOf(categories, category));
            category = categories[EditorGUILayout.Popup("分类", index, categories)];
            using (new EditorGUILayout.HorizontalScope())
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(listScroll, GUILayout.Width(285)))
                {
                    listScroll = scroll.scrollPosition;
                    foreach (var tool in manifest.tools.Where(t => (maintenance || !t.maintenance) && (category == "全部" || t.category == category) &&
                        (t.name + t.id + t.description).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        if (GUILayout.Toggle(selected == tool.id, tool.name + "\n" + tool.id, "Button", GUILayout.Height(43)) && selected != tool.id)
                        { selected = tool.id; parameters.Clear(); detailScroll = Vector2.zero; }
                    }
                }
                using (var scroll = new EditorGUILayout.ScrollViewScope(detailScroll))
                {
                    detailScroll = scroll.scrollPosition;
                    var tool = manifest.tools.FirstOrDefault(t => t.id == selected);
                    if (tool == null) return;
                    EditorGUILayout.LabelField(tool.name, EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(tool.id, GUILayout.Height(19));
                    EditorGUILayout.LabelField(tool.description, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("推荐使用者", tool.audience);
                    EditorGUILayout.LabelField("影响范围", tool.impact, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("输出位置", tool.output, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("运行要求", string.Join(" · ", new[] { tool.writesAssets ? "修改资产，需确认" : "只读资产", tool.externalSource ? "外部参考工程" : "项目内资源", tool.graphics ? "图形设备 / 视觉核验" : "可无图形运行", tool.asynchronous ? "等待 Play Mode 完成" : "" }), EditorStyles.wordWrappedLabel);
                    EditorGUILayout.Space();
                    if (tool.id == "build.player")
                    {
                        EditorGUILayout.HelpBox("产品／专项构建统一在构建配置页选择；此处不再维护另一份 Profile 下拉框。", MessageType.Info);
                        if (GUILayout.Button("打开统一构建配置", GUILayout.Height(32))) ProjectBuildWindow.Open();
                        if (GUILayout.Button("复制默认产品 Test 命令")) EditorGUIUtility.systemCopyBuffer = "./Tools/Invoke-ProjectTool.ps1 -ToolId build.player -Profile product -BuildKind Test -Network Steam -Distribution Steam";
                        return;
                    }
                    foreach (string parameter in tool.parameters ?? Array.Empty<string>())
                    {
                        if (parameter == "Apply") continue;
                        parameters[parameter] = EditorGUILayout.TextField(parameter, Value(parameter));
                    }
                    string unavailable = ProjectToolRunner.Availability(tool);
                    if (process != null) unavailable = "验收进程正在运行；请等待日志与退出结果";
                    if (!string.IsNullOrEmpty(unavailable)) EditorGUILayout.HelpBox(unavailable, MessageType.Info);
                    using (new EditorGUI.DisabledScope(unavailable.Length != 0))
                        if (GUILayout.Button(tool.writesAssets ? "查看影响并执行…" : "执行", GUILayout.Height(30))) Execute(tool);
                    if (GUILayout.Button("复制 AI 命令")) EditorGUIUtility.systemCopyBuffer = Command(tool);
                    if (GUILayout.Button("使用文档")) ProjectToolMenus.Documentation();
                    var result = processResult?.id == selected ? processResult : ProjectToolRunner.LastResult?.id == selected ? ProjectToolRunner.LastResult : null;
                    string last = "Logs/ProjectTools/" + selected + ".json";
                    if (result == null && File.Exists(last)) result = JsonUtility.FromJson<ProjectToolResult>(File.ReadAllText(last));
                    if (result != null)
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.HelpBox((result.pending ? "等待完成" : result.success ? "成功" : "失败") + $" · {result.durationSeconds:F2} 秒\n" + result.error, result.success ? MessageType.Info : MessageType.Warning);
                        if (!string.IsNullOrEmpty(result.report) && GUILayout.Button("打开最近报告")) EditorUtility.RevealInFinder(result.report);
                    }
                }
            }
        }
        private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        private string Command(ProjectToolDescriptor tool)
        {
            string command = "./Tools/Invoke-ProjectTool.ps1 -ToolId " + tool.id;
            foreach (string p in tool.parameters ?? Array.Empty<string>())
            {
                if (p == "Apply" || string.IsNullOrWhiteSpace(Value(p))) continue;
                if (p == "ScriptsOnly" || p == "UniqueOutput")
                {
                    if (string.Equals(Value(p), "true", StringComparison.OrdinalIgnoreCase)) command += " -" + p;
                }
                else command += " -" + p + " " + Quote(Value(p));
            }
            if (tool.writesAssets) command += " -Apply";
            return command;
        }
        private void Execute(ProjectToolDescriptor tool)
        {
            if (tool.writesAssets && !EditorUtility.DisplayDialog("确认维护操作", tool.description + "\n\n将修改：" + tool.impact + "\n\n建议先保存并检查版本控制差异。", "执行", "取消")) return;
            if (!string.IsNullOrEmpty(tool.script))
            {
                // Use a JSON parameter file; user text never becomes PowerShell source code.
                string folder = Path.GetFullPath("Logs/ProjectTools/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-ui");
                Directory.CreateDirectory(folder); processReport = Path.Combine(folder, "result.json");
                string input = Path.Combine(folder, "parameters.json");
                File.WriteAllText(input, "{" + string.Join(",", parameters.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => Json(p.Key) + ":" + Json(p.Value))) + "}");
                var start = new ProcessStartInfo("powershell.exe", $"-NoProfile -File \"{Path.GetFullPath("Tools/Invoke-ProjectTool.ps1")}\" -ToolId {tool.id} -ParametersFile \"{input}\" -ResultPath \"{processReport}\"")
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = ProjectToolCatalog.ProjectRoot };
                process = Process.Start(start); return;
            }
            ProjectToolRunner.Run(tool.id, new ProjectToolRequest {
                apply = tool.writesAssets, profile = Empty(Value("Profile")), source = Empty(Value("Source")), assetPath = Empty(Value("AssetPath")),
                output = Empty(Value("Output")), scriptsOnly = string.Equals(Value("ScriptsOnly"), "true", StringComparison.OrdinalIgnoreCase),
                buildKind = Empty(Value("BuildKind")), development = Empty(Value("Development")), uniqueOutput = string.Equals(Value("UniqueOutput"), "true", StringComparison.OrdinalIgnoreCase)
            });
        }
        private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
        private static string Json(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }

    public static class ProjectToolMenus
    {
        public const string Root = "MonsterSupergroup/";
        private static void Run(string id, string profile = null) => ProjectToolRunner.Run(id, new ProjectToolRequest { profile = profile });
        [MenuItem(Root + "工具中心…", priority = 0)] public static void Center() => ProjectToolWindow.Open();
        [MenuItem(Root + "制作/波次时间轴", priority = 10)] public static void Waves() => Run("open.waves");
        [MenuItem(Root + "制作/准备房间配置", priority = 11)] public static void Preparation() => Run("open.preparation");
        [MenuItem(Root + "制作/本地化表格", priority = 12)] public static void Localization() => Run("open.localization");
        [MenuItem(Root + "校验/全部正式资源", priority = 20)] public static void All() => Run("validate.all");
        [MenuItem(Root + "校验/GAS", priority = 21)] public static void Gas() => Run("validate.gas");
        [MenuItem(Root + "校验/本地化", priority = 22)] public static void ValidateLocalization() => Run("validate.localization");
        [MenuItem(Root + "校验/波次与怪物", priority = 23)] public static void ValidateWaves() => Run("validate.waves-and-enemies");
        public static void Development() => ProjectBuildWindow.OpenValidation(MonsterSupergroup.Builds.BuildKind.Dev);
        public static void Release() => ProjectBuildWindow.OpenValidation(MonsterSupergroup.Builds.BuildKind.Test);
        [MenuItem(Root + "构建与验收/专项验收…", priority = 32)] public static void Validation() => ProjectToolWindow.Open("自动验收");
        [MenuItem(Root + "联机诊断/Editor 后端：Steam", priority = 40)] public static void Steam() => Run("diagnostic.backend-steam");
        [MenuItem(Root + "联机诊断/Editor 后端：KCP", priority = 41)] public static void Kcp() => Run("diagnostic.backend-kcp");
        [MenuItem(Root + "联机诊断/Steam 状态诊断", priority = 42)] public static void SteamState() => Run("diagnostic.steam");
        [MenuItem(Root + "联机诊断/Steam 叠加层诊断", priority = 43)] public static void Overlay() => Run("diagnostic.overlay");
        [MenuItem(Root + "联机诊断/仇恨交接控制", priority = 44)] public static void Handoff() => Run("diagnostic.enemy-handoff");
        [MenuItem(Root + "维护与样例…", priority = 60)] public static void Maintenance() => ProjectToolWindow.Open("维护与样例", true);
        [MenuItem(Root + "使用文档", priority = 61)] public static void Documentation() => EditorUtility.RevealInFinder(Path.Combine(ProjectToolCatalog.ProjectRoot, ProjectToolCatalog.DocumentationPath));
        [MenuItem(Root + "联机诊断/仇恨交接控制", true)] private static bool CanHandoff() => ProjectToolRunner.Availability(ProjectToolCatalog.Find("diagnostic.enemy-handoff")).Length == 0;
        [MenuItem(Root + "联机诊断/Steam 状态诊断", true)] private static bool CanSteam() => ProjectToolRunner.Availability(ProjectToolCatalog.Find("diagnostic.steam")).Length == 0;
        [MenuItem(Root + "联机诊断/Steam 叠加层诊断", true)] private static bool CanOverlay() => CanSteam();
        [MenuItem(Root + "联机诊断/Editor 后端：Steam", true)] private static bool CanSteamBackend() => !EditorApplication.isPlayingOrWillChangePlaymode;
        [MenuItem(Root + "联机诊断/Editor 后端：KCP", true)] private static bool CanKcpBackend() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
