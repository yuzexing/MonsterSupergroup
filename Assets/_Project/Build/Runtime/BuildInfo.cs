using System;
using System.IO;
using UnityEngine;

namespace MonsterSupergroup.Builds
{
    public enum BuildKind { Dev, Test, Shipping }

    [Serializable]
    public sealed class BuildInfo
    {
        [SerializeField] private int schema = 2;
        [SerializeField] private string gameVersion, kind, buildId, gitCommit, builtUtc, unityVersion, platform, architecture, profile;
        [SerializeField] private bool gitValid, dirty, development;
        [SerializeField] private string network = "Steam", distribution = "Steam", diagnostics = "Normal";
        [SerializeField] private bool testAssemblies, developmentTools, evidence;
        public string GameVersion => gameVersion;
        public string Kind => kind;
        public string BuildId => buildId;
        public string GitCommit => gitCommit;
        public bool GitValid => gitValid;
        public bool Dirty => dirty;
        public bool Development => development;
        public string BuiltUtc => builtUtc;
        public string Profile => profile;
        public string Network => network;
        public string Distribution => distribution;
        public string Diagnostics => diagnostics;
        public bool TestAssemblies => testAssemblies;
        public bool DevelopmentTools => developmentTools;
        public bool Evidence => evidence;
        public string Display => $"v{gameVersion}-{kind} · {buildId}";
        public string ArtifactName => $"MonsterSupergroup-v{gameVersion}-{kind}-{buildId}";

        public BuildInfo(string version, BuildKind type, string id, string commit, bool validGit, bool isDirty,
            string utc, string unity, string target, string arch, bool isDevelopment, string buildProfile)
        {
            gameVersion = version; kind = type.ToString().ToLowerInvariant(); buildId = id;
            gitCommit = commit; gitValid = validGit; dirty = isDirty; builtUtc = utc;
            unityVersion = unity; platform = target; architecture = arch; development = isDevelopment; profile = buildProfile;
        }
        public void SetConfiguration(string recipe, string network, string distribution, string diagnostics, bool tests, bool tools, bool evidence)
        {
            profile = recipe; this.network = network; this.distribution = distribution; this.diagnostics = diagnostics;
            testAssemblies = tests; developmentTools = tools; this.evidence = evidence;
        }
        public string ToJson() => JsonUtility.ToJson(this, true);
        public static BuildInfo FromJson(string json) => JsonUtility.FromJson<BuildInfo>(json);
        public string Validate(string actualVersion, bool actualDevelopment, BuildKind compiledKind)
        {
            if (schema != 2 || !global::MonsterSupergroup.Builds.GameVersion.TryParse(gameVersion, out _) || string.IsNullOrWhiteSpace(buildId) ||
                !DateTimeOffset.TryParse(builtUtc, out _) || string.IsNullOrEmpty(unityVersion) ||
                string.IsNullOrEmpty(platform) || string.IsNullOrEmpty(architecture) || string.IsNullOrEmpty(profile)) return "构建信息缺失或格式无效。";
            if ((network != "Steam" && network != "Kcp") || (distribution != "Steam" && distribution != "Direct") ||
                (diagnostics != "Normal" && diagnostics != "Evidence") || evidence != (diagnostics == "Evidence"))
                return "构建用途或诊断配置缺失或无效。";
            if (gameVersion != actualVersion || development != actualDevelopment || kind != compiledKind.ToString().ToLowerInvariant())
                return "构建信息与 Player 的版本或构建配置不一致。";
            if (compiledKind == BuildKind.Shipping && (development || !gitValid || dirty || string.IsNullOrEmpty(gitCommit) ||
                testAssemblies || developmentTools || evidence || network != "Steam" || distribution != "Steam"))
                return "发行构建信息未通过校验。";
            return null;
        }
        public string ValidateCapabilities(bool compiledTools, bool compiledEvidence) =>
            developmentTools == compiledTools && evidence == compiledEvidence ? null : "包内能力信息与实际编译配置不一致。";
    }

    /// <summary>Compile-time restrictions cannot be enabled by changing BuildInfo or command-line arguments.</summary>
    public static class BuildFeatures
    {
        public static BuildKind CompiledKind =>
#if MONSTER_BUILD_SHIPPING && !UNITY_EDITOR
            BuildKind.Shipping;
#elif MONSTER_BUILD_TEST && !UNITY_EDITOR
            BuildKind.Test;
#else
            BuildKind.Dev;
#endif
        private static bool ToolsCompiled =>
#if MONSTER_BUILD_TOOLS
            true;
#else
            false;
#endif
        private static bool EvidenceCompiled =>
#if MONSTER_BUILD_EVIDENCE
            true;
#else
            false;
#endif
        public static bool DevelopmentToolsAllowed => CapabilityAllowed(CompiledKind, ToolsCompiled, Application.isEditor);
        public static bool EvidenceAllowed => CapabilityAllowed(CompiledKind, EvidenceCompiled, Application.isEditor);
        public static bool CapabilityAllowed(BuildKind kind, bool capabilityCompiled, bool isEditor) =>
            isEditor || (kind != BuildKind.Shipping && capabilityCompiled);
    }

    public static class RuntimeBuildInfo
    {
        private static bool loaded;
        private static BuildInfo current;
        private static string error;
        public static BuildInfo Current { get { EnsureLoaded(); return current; } }
        public static string Error { get { EnsureLoaded(); return error; } }
        public static bool CanConnect => Application.isEditor || string.IsNullOrEmpty(Error);
        public static string Version => Application.isEditor ? Application.version : Current?.GameVersion ?? "unknown";
        public static string Display => Application.isEditor ? $"v{Application.version}-dev · Editor" : Current != null && string.IsNullOrEmpty(Error) ? Current.Display : "BuildInfo invalid · 请重新安装完整游戏包";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { loaded = false; current = null; error = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            EnsureLoaded();
            Debug.Log("[BuildInfo] " + (Application.isEditor ? Display : current?.ToJson() ?? "missing") + (error == null ? "" : "\n" + error));
        }
        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            if (Application.isEditor) return;
            try
            {
                current = BuildInfo.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BuildInfo.json")));
                error = current == null ? "包内 BuildInfo 缺失。" : current.Validate(Application.version, Debug.isDebugBuild, BuildFeatures.CompiledKind);
                if (error == null) error = current.ValidateCapabilities(BuildFeatures.DevelopmentToolsAllowed, BuildFeatures.EvidenceAllowed);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            { error = "无法读取包内构建信息：" + exception.Message; }
        }
    }
}
