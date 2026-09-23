using System;
using System.IO;
using System.Linq;
using MonsterSupergroup.Builds;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace MonsterSupergroup.EditorTools
{
    [Serializable]
    public sealed class BuildExecutionRequest
    {
        public string output;
        public bool cleanBuildCache, runAfterBuild;
        public string expectedContentHash, expectedInputHash;
    }

    [Serializable]
    public sealed class ResolvedProjectBuild
    {
        [SerializeField] private string profileGuid, profilePath, purpose, contentHash, inputHash, gameVersion;
        [SerializeField] private BuildKind kind;
        [SerializeField] private BuildNetwork network;
        [SerializeField] private BuildDistribution distribution;
        [SerializeField] private BuildDiagnostics diagnostics;
        [SerializeField] private NativeBuildSnapshot native;
        [SerializeField] private string[] defines;
        [SerializeField] private string[] inputFiles;
        public string ProfileGuid => profileGuid;
        public string ProfilePath => profilePath;
        public string Purpose => purpose;
        public string ContentHash => contentHash;
        public string InputHash => inputHash;
        public string GameVersion => gameVersion;
        public BuildKind Kind => kind;
        public BuildNetwork Network => network;
        public BuildDistribution Distribution => distribution;
        public BuildDiagnostics Diagnostics => diagnostics;
        public bool Development => native.development;
        public bool Tools => kind == BuildKind.Dev || purpose != "product";
        public bool Evidence => diagnostics == BuildDiagnostics.Evidence;
        public ProjectBuildProfile Recipe => ProjectBuildPurposes.Get(purpose);
        public NativeBuildSnapshot Native => JsonUtility.FromJson<NativeBuildSnapshot>(JsonUtility.ToJson(native));
        public string[] Defines => (string[])defines.Clone();
        public BuildOptions ExpectedOptions => native.ContentOptions | (Recipe.testAssemblies ? BuildOptions.IncludeTestAssemblies : 0);
        public string Summary => $"{purpose} · {kind} · {network} / {distribution} / {diagnostics}\n" +
            $"v{gameVersion} · {native.platform}/{native.architecture} · Development={Development}\n" +
            $"Deep Profiling={native.deepProfiling} · Debugging={native.allowDebugging} · Profiler={native.connectProfiler} · Compression={native.compression}\n" +
            $"测试程序集={Recipe.testAssemblies} · 开发工具={Tools} · 取证={Evidence}\n" +
            "场景：" + string.Join(", ", native.scenes.Select(Path.GetFileNameWithoutExtension)) + "\n" +
            $"Player：{native.playerSource}\nGraphics：{native.graphicsSource}\nQuality：{native.qualitySource}\n" +
            "编译符号：" + string.Join(";", defines);
        public string ToJson() => JsonUtility.ToJson(this, true);
        internal ResolvedProjectBuild(BuildProfile profile, MonsterBuildSettings settings, NativeBuildSnapshot snapshot, bool inputs,
            ProjectBuildInputEvidence evidence = null, string evidenceStage = null)
        {
            profilePath = AssetDatabase.GetAssetPath(profile); profileGuid = AssetDatabase.AssetPathToGUID(profilePath);
            purpose = settings.PurposeId; kind = settings.BuildKind; network = settings.Network;
            distribution = settings.Distribution; diagnostics = settings.Diagnostics; native = snapshot;
            gameVersion = MonsterSupergroup.Builds.GameVersion.Parse(NativeBuildProfileSettings.GlobalVersion).ToString();
            defines = ProjectBuildDefines.Expected(settings);
            // Profile identity and execution details are separate from the effective content configuration.
            contentHash = NativeBuildProfileSettings.Hash($"{settings.SchemaVersion}|{purpose}|{kind}|{network}|{distribution}|{diagnostics}\n" +
                JsonUtility.ToJson(snapshot) + "\n" + string.Join(";", defines));
            inputFiles = inputs ? NativeBuildProfileSettings.InputFiles(evidence, evidenceStage) : null;
            inputHash = inputs ? NativeBuildProfileSettings.Hash(string.Join("\n", inputFiles) + "\n" + string.Join("\n", snapshot.dependencies)) : null;
        }
    }

    public static class ProjectBuildResolver
    {
        public const string MigrationMessage = "旧配方／业务参数已停用。请指定原生 Build Profile 路径（-BuildProfile），业务配置在 MonsterBuildSettings 中编辑并应用；旧脚本保留待审查。";
        public static BuildProfile Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new BuildFailedException("必须明确指定 Build Profile 路径。");
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) throw new BuildFailedException(MigrationMessage);
            return AssetDatabase.LoadAssetAtPath<BuildProfile>(path) ?? throw new BuildFailedException("找不到原生 Build Profile：" + path);
        }
        public static ResolvedProjectBuild Resolve(BuildProfile profile, bool captureInputs = false) => Resolve(profile, captureInputs, null, null);
        internal static ResolvedProjectBuild Resolve(BuildProfile profile, bool captureInputs, ProjectBuildInputEvidence evidence, string evidenceStage)
        {
            if (profile == null || !AssetDatabase.Contains(profile)) throw new BuildFailedException("请选择已保存的原生 Build Profile 资产。");
            var settings = profile.GetComponent<MonsterBuildSettings>();
            if (settings == null) throw new BuildFailedException("Profile 缺少 MonsterBuildSettings 子资产，请先附加业务配置。");
            var native = NativeBuildProfileSettings.Read(profile);
            ValidateBusiness(settings, native.development, native.scenes);
            foreach (string scene in native.scenes)
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null) throw new FileNotFoundException("场景缺失，请先执行明确的维护操作：" + scene);
            return new ResolvedProjectBuild(profile, settings, native, captureInputs, evidence, evidenceStage);
        }
        public static void ValidateBusiness(MonsterBuildSettings s, bool development, string[] scenes)
        {
            if (s.SchemaVersion != 1) throw new BuildFailedException("不支持的 MonsterBuildSettings SchemaVersion：" + s.SchemaVersion);
            if (!Enum.IsDefined(typeof(BuildKind), s.BuildKind) || !Enum.IsDefined(typeof(BuildNetwork), s.Network) ||
                !Enum.IsDefined(typeof(BuildDistribution), s.Distribution) || !Enum.IsDefined(typeof(BuildDiagnostics), s.Diagnostics)) throw new BuildFailedException("业务配置枚举无效。");
            var purpose = ProjectBuildPurposes.Get(s.PurposeId);
            if (!purpose.scenes.SequenceEqual(scenes ?? Array.Empty<string>())) throw new BuildFailedException("原生场景列表与用途 " + s.PurposeId + " 不符，预期：" + string.Join(", ", purpose.scenes));
            if (s.Network == BuildNetwork.Kcp && s.Distribution != BuildDistribution.Direct) throw new BuildFailedException("KCP 必须使用 Direct 分发。");
            if (purpose.product && s.Network == BuildNetwork.Kcp && s.BuildKind != BuildKind.Dev) throw new BuildFailedException("产品 KCP 只允许 Dev。");
            if (s.Diagnostics == BuildDiagnostics.Evidence && (!purpose.product || s.BuildKind != BuildKind.Test)) throw new BuildFailedException("取证只允许产品 Test。");
            if (s.BuildKind == BuildKind.Shipping && (!purpose.product || development || purpose.testAssemblies || s.Network != BuildNetwork.Steam ||
                s.Distribution != BuildDistribution.Steam || s.Diagnostics != BuildDiagnostics.Normal)) throw new BuildFailedException("Shipping 仅允许产品／Steam／Steam 分发／Normal，必须关闭 Development 和测试能力。");
        }
        public static void ValidateDefines(ResolvedProjectBuild plan)
        {
            var native = plan.Native;
            string[] leaked = native.globalDefines.Concat(native.playerDefines).Where(ProjectBuildDefines.IsReserved).Distinct().ToArray();
            if (leaked.Length > 0) throw new BuildFailedException("Player Settings 中残留构建专用符号，请移除：" + string.Join(", ", leaked));
            string difference = ProjectBuildDefines.Difference(native.profileDefines, plan.Defines);
            if (difference.Length != 0) throw new BuildFailedException("Profile 符号未就绪。" + difference + "。请先应用配置，等待编译完成。");
        }
        public static void ValidateExecution(ResolvedProjectBuild plan, BuildExecutionRequest request)
        {
            if (request.runAfterBuild && plan.Distribution != BuildDistribution.Direct) throw new BuildFailedException("Steam 分发不支持本地 Build and Run。请选择 Build，或显式使用 Direct Profile。");
            if (request.expectedContentHash != null && request.expectedContentHash != plan.ContentHash ||
                request.expectedInputHash != null && request.expectedInputHash != plan.InputHash) throw new BuildFailedException("配置或工程输入在预览后改变，请刷新计划再构建。");
        }
        public static ResolvedProjectBuild Resolve(string id = "product", string kind = null, string development = null, string network = null,
            string distribution = null, string diagnostics = null, ProjectToolManifest catalog = null) => throw new BuildFailedException(MigrationMessage);
    }
}
