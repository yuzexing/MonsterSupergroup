using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.EditorTools
{
    [Serializable]
    public sealed class NativeBuildSnapshot
    {
        public string platform, architecture, version, playerSource, graphicsSource, qualitySource;
        public bool development, deepProfiling, allowDebugging, connectProfiler, waitForDebugger;
        public int compression, debuggerPort;
        public string[] scenes, playerDefines, globalDefines, profileDefines, dependencies;
        public string playerSettings, graphicsSettings, qualitySettings;
        public BuildOptions ContentOptions => (development ? BuildOptions.Development : 0) |
            (deepProfiling ? BuildOptions.EnableDeepProfilingSupport : 0) | (allowDebugging ? BuildOptions.AllowDebugging : 0) |
            (connectProfiler ? BuildOptions.ConnectWithProfiler : 0) |
            (compression == 2 ? BuildOptions.CompressWithLz4 : compression == 3 ? BuildOptions.CompressWithLz4HC : 0);
    }

    // Version-bound serialization adapter. Reading never activates a profile or calls Unity internals.
    public static class NativeBuildProfileSettings
    {
        public const string SupportedVersion = "6000.3.21f1";
        public static void CheckVersion()
        {
            if (Application.unityVersion != SupportedVersion) throw new BuildFailedException("原生 Profile 适配层需要 Unity " + SupportedVersion + "；升级后请先验证字段和构建选项。");
        }
        public static SerializedProperty Required(SerializedObject serialized, string path) => serialized.FindProperty(path)
            ?? throw new BuildFailedException("Unity Profile 序列化字段不匹配：" + path);
        private static SerializedProperty Platform(SerializedObject serialized, string name) => Required(serialized, "m_PlatformBuildProfile." + name);
        public static PlayerSettings GlobalPlayerSettings()
        {
            var fallback = ScriptableObject.CreateInstance<BuildProfile>();
            try { return fallback.GetComponent<PlayerSettings>() ?? throw new BuildFailedException("无法读取全局 Player Settings。"); }
            finally { Object.DestroyImmediate(fallback); }
        }
        public static string GlobalVersion => Required(new SerializedObject(GlobalPlayerSettings()), "bundleVersion").stringValue;
        public static void UpdateVersion(string expected, string next)
        {
            if (GlobalVersion != expected) throw new InvalidOperationException("全局版本已改变，请刷新预览。");
            var global = GlobalPlayerSettings();
            Undo.RecordObject(global, "更新全局游戏版本");
            var serialized = new SerializedObject(global);
            Required(serialized, "bundleVersion").stringValue = next;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
        public static string[] Defines(PlayerSettings settings)
        {
            var map = Required(new SerializedObject(settings), "scriptingDefineSymbols");
            for (int i = 0; i < map.arraySize; i++)
            {
                var item = map.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("first").stringValue == "Standalone")
                    return item.FindPropertyRelative("second").stringValue.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
            }
            return Array.Empty<string>();
        }
        public static NativeBuildSnapshot Read(BuildProfile profile)
        {
            CheckVersion();
            var serialized = new SerializedObject(profile);
            if (Required(serialized, "m_BuildTarget").intValue != (int)BuildTarget.StandaloneWindows64 ||
                Required(serialized, "m_Subtarget").intValue != (int)StandaloneBuildSubtarget.Player || Platform(serialized, "m_Architecture").intValue != 0)
                throw new BuildFailedException("首版只支持 Windows x64 Player Profile。");
            if (Platform(serialized, "m_CreateSolution").boolValue || Platform(serialized, "m_InstallInBuildFolder").boolValue ||
                Platform(serialized, "m_WindowsBuildAndRunDeployTarget").intValue != 0)
                throw new BuildFailedException("统一交付需要直接生成本机 Player，请关闭 Create Solution、Install In Build Folder 和远程部署。");
            var global = GlobalPlayerSettings();
            var player = profile.GetComponent<PlayerSettings>();
            var dependencies = new SortedSet<string>(StringComparer.Ordinal);
            var assets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(profile));
            var graphics = assets.FirstOrDefault(a => a != null && a.GetType().FullName == "UnityEditor.Build.Profile.BuildProfileGraphicsSettings");
            var quality = assets.FirstOrDefault(a => a != null && a.GetType().FullName == "UnityEditor.Build.Profile.BuildProfileQualitySettings");
            var result = new NativeBuildSnapshot {
                platform = "StandaloneWindows64", architecture = "x86_64",
                version = Required(new SerializedObject(player), "bundleVersion").stringValue,
                development = Platform(serialized, "m_Development").boolValue,
                deepProfiling = Platform(serialized, "m_BuildWithDeepProfilingSupport").boolValue,
                allowDebugging = Platform(serialized, "m_AllowDebugging").boolValue,
                connectProfiler = Platform(serialized, "m_ConnectProfiler").boolValue,
                waitForDebugger = Platform(serialized, "m_WaitForManagedDebugger").boolValue,
                debuggerPort = Platform(serialized, "m_ManagedDebuggerFixedPort").intValue,
                compression = Platform(serialized, "m_CompressionType").intValue,
                scenes = profile.GetScenesForBuild().Where(s => s.enabled).Select(s => s.path).ToArray(),
                playerSource = player == global ? "ProjectSettings/ProjectSettings.asset" : "Profile Player Settings",
                graphicsSource = graphics == null ? "ProjectSettings/GraphicsSettings.asset" : "Profile Graphics Settings",
                qualitySource = quality == null ? "ProjectSettings/QualitySettings.asset" : "Profile Quality selection + ProjectSettings/QualitySettings.asset",
                playerSettings = CanonicalObject(player, dependencies),
                graphicsSettings = graphics == null ? SettingsFile("ProjectSettings/GraphicsSettings.asset", dependencies) : CanonicalObject(graphics, dependencies),
                qualitySettings = (quality == null ? "" : CanonicalObject(quality, dependencies)) + SettingsFile("ProjectSettings/QualitySettings.asset", dependencies),
                globalDefines = Defines(global), playerDefines = Defines(player),
                profileDefines = (profile.scriptingDefines ?? Array.Empty<string>()).OrderBy(s => s, StringComparer.Ordinal).ToArray()
            };
            if (result.version != GlobalVersion) throw new BuildFailedException("Profile 版本覆盖 " + result.version + " 与全局版本 " + GlobalVersion + " 不一致，请同步覆盖或恢复继承。");
            if (!result.development && (result.deepProfiling || result.allowDebugging || result.connectProfiler || result.waitForDebugger))
                throw new BuildFailedException("调试／Profiler 选项需要原生 Development Build，不能静默忽略。");
            if (result.waitForDebugger && !result.allowDebugging) throw new BuildFailedException("等待调试器需要开启脚本调试。");
            if (result.compression != 0 && result.compression != 2 && result.compression != 3) throw new BuildFailedException("未知压缩选项。");
            foreach (string scene in result.scenes) dependencies.Add(scene);
            result.dependencies = dependencies.Select(path => path + ":" + AssetDatabase.GetAssetDependencyHash(path)).ToArray();
            return result;
        }
        private static string SettingsFile(string path, SortedSet<string> dependencies)
        {
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            foreach (Match match in Regex.Matches(text, "guid: ([a-fA-F0-9]{32})")) AddDependency(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value), dependencies);
            return text;
        }
        private static void AddDependency(string path, SortedSet<string> dependencies) { if (!string.IsNullOrEmpty(path)) dependencies.Add(path); }
        private static string CanonicalObject(Object value, SortedSet<string> dependencies)
        {
            var text = new StringBuilder();
            var iterator = new SerializedObject(value).GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType == SerializedPropertyType.Generic || iterator.propertyPath == "m_ObjectHideFlags" || iterator.propertyPath == "m_Name") continue;
                string data;
                if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var obj = iterator.objectReferenceValue;
                    if (obj == null) data = "null";
                    else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long local))
                    { data = guid + ":" + local; AddDependency(AssetDatabase.GetAssetPath(obj), dependencies); }
                    else data = obj.GetType().FullName + ":" + obj.name;
                }
                else if (iterator.propertyType == SerializedPropertyType.ManagedReference) continue;
                else data = Convert.ToString(iterator.boxedValue, CultureInfo.InvariantCulture);
                text.Append(iterator.propertyPath).Append('=').Append(data).Append('\n');
            }
            return text.ToString();
        }
        public static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
        public static string Hash(byte[] bytes)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static string[] InputFiles() => InputFiles(null, null);
        internal static string[] InputFiles(ProjectBuildInputEvidence evidence, string evidenceStage)
        {
            var files = Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories)
                .Where(p => new[] { ".cs", ".asmdef", ".asmref", ".rsp", ".dll" }.Contains(Path.GetExtension(p)))
                .Concat(Directory.EnumerateFiles("ProjectSettings", "*", SearchOption.TopDirectoryOnly))
                .Concat(Directory.EnumerateFiles("Packages", "*", SearchOption.AllDirectories).Where(p => new[] { ".cs", ".asmdef", ".asmref", ".json", ".dll" }.Contains(Path.GetExtension(p))))
                .OrderBy(p => p, StringComparer.Ordinal);
            var result = files.Select(p => {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(p); }
                catch (Exception e) { evidence?.InputReadFailed(evidenceStage, p, e); throw; }
                string digest = Hash(bytes);
                evidence?.ObserveInput(evidenceStage, p, bytes);
                return p.Replace('\\', '/') + ":" + digest;
            }).ToArray();
            evidence?.CompletePlanRead(evidenceStage);
            return result;
        }
        public static void SetTemplateOptions(BuildProfile profile, bool development, bool profiler)
        {
            var serialized = new SerializedObject(profile);
            Platform(serialized, "m_Development").boolValue = development;
            Platform(serialized, "m_ConnectProfiler").boolValue = profiler;
            foreach (string name in new[] { "m_BuildWithDeepProfilingSupport", "m_AllowDebugging", "m_WaitForManagedDebugger", "m_CreateSolution", "m_InstallInBuildFolder", "m_CopyPDBFiles" }) Platform(serialized, name).boolValue = false;
            Platform(serialized, "m_CompressionType").intValue = development ? 2 : 3;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
