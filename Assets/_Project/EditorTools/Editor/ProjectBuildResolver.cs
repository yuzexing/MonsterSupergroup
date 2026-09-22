using System;
using System.Linq;
using MonsterSupergroup.Builds;
using UnityEditor.Build;

namespace MonsterSupergroup.EditorTools
{
    public enum BuildNetwork { Steam, Kcp }
    public enum BuildDistribution { Steam, Direct }
    public enum BuildDiagnostics { Normal, Evidence }

    public sealed class ResolvedProjectBuild
    {
        public string RequestedId { get; internal set; }
        public ProjectBuildProfile Recipe { get; internal set; }
        public BuildKind Kind { get; internal set; }
        public BuildNetwork Network { get; internal set; }
        public BuildDistribution Distribution { get; internal set; }
        public BuildDiagnostics Diagnostics { get; internal set; }
        public bool Development { get; internal set; }
        public bool Tools => Kind == BuildKind.Dev || !Recipe.product;
        public bool Evidence => Diagnostics == BuildDiagnostics.Evidence;
        public bool IsAlias => RequestedId != Recipe.id;
        public string[] Defines => (Recipe.defines ?? Array.Empty<string>())
            .Concat(Network == BuildNetwork.Kcp ? new[] { "MONSTER_KCP_DEVELOPMENT_BUILD" } : Array.Empty<string>())
            .Concat(new[] { "MONSTER_BUILD_" + Kind.ToString().ToUpperInvariant() })
            .Concat(Tools ? new[] { "MONSTER_BUILD_TOOLS" } : Array.Empty<string>())
            .Concat(Evidence ? new[] { "MONSTER_BUILD_EVIDENCE", "MONSTER_COMBAT_EVIDENCE" } : Array.Empty<string>())
            .Distinct().ToArray();
        public string Summary => $"{Recipe.name} · {Kind} · Development={Development}\n" +
            $"网络：{Network} · 分发：{(Distribution == BuildDistribution.Steam ? "从 Steam 启动" : "本地直接启动")} · 日志：{Diagnostics}\n" +
            $"测试程序集：{Recipe.testAssemblies} · 开发／辅助：{Tools} · 故障取证：{Evidence}\n" +
            "场景：" + string.Join(", ", Recipe.scenes.Select(System.IO.Path.GetFileNameWithoutExtension));
    }

    /// <summary>UI, command line and legacy entry points resolve the same explicit capability matrix.</summary>
    public static class ProjectBuildResolver
    {
        public static ResolvedProjectBuild Resolve(string id = "product", string kind = null, string development = null,
            string network = null, string distribution = null, string diagnostics = null, ProjectToolManifest catalog = null)
        {
            catalog ??= ProjectToolCatalog.Load();
            var alias = catalog.buildAliases.FirstOrDefault(a => a.id == id);
            var recipe = catalog.builds.SingleOrDefault(b => b.id == (alias?.recipe ?? id))
                ?? throw new ArgumentException("未知构建配置：" + id);
            BuildKind resolvedKind = Parse<BuildKind>(kind ?? alias?.kind ?? recipe.kind);
            BuildNetwork resolvedNetwork = Parse<BuildNetwork>(network ?? alias?.network ?? recipe.network);
            var result = new ResolvedProjectBuild {
                RequestedId = id, Recipe = recipe, Kind = resolvedKind, Network = resolvedNetwork,
                Distribution = Parse<BuildDistribution>(distribution ?? (resolvedNetwork == BuildNetwork.Kcp ? "Direct" : alias?.distribution ?? recipe.distribution)),
                Diagnostics = Parse<BuildDiagnostics>(diagnostics ?? alias?.diagnostics ?? "Normal"),
                Development = development == null ? resolvedKind == BuildKind.Dev :
                    bool.TryParse(development, out bool parsed) ? parsed : throw new ArgumentException("Development 必须是 true 或 false。")
            };
            if (result.Network == BuildNetwork.Kcp && result.Distribution != BuildDistribution.Direct)
                throw new BuildFailedException("KCP 只用于本地开发／专项用途，请选择本地直接启动。");
            if (recipe.product && result.Network == BuildNetwork.Kcp && resolvedKind != BuildKind.Dev)
                throw new BuildFailedException("产品 KCP 仅供 Dev 开发；正常 Test 请选择 Steam，非 Development 程序验收请选择专项配方。");
            if (result.Kind == BuildKind.Shipping && (!recipe.product || result.Network != BuildNetwork.Steam ||
                result.Distribution != BuildDistribution.Steam || result.Evidence || result.Development || recipe.testAssemblies))
                throw new BuildFailedException("Shipping 只允许产品配方、Steam 网络、Steam 分发；必须关闭 Development、测试程序集和故障取证。");
            // The product recipe is also checked structurally, not merely trusted by its display name.
            if (recipe.product && (recipe.id != "product" || recipe.testAssemblies || recipe.scenes == null ||
                !recipe.scenes.SequenceEqual(new[] { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/MainMenu.unity", "Assets/_Project/Scenes/Gameplay.unity" }) ||
                (recipe.defines?.Length ?? 0) != 0))
                throw new BuildFailedException("产品配方场景或编译配置被修改，请恢复 Boot → MainMenu → Gameplay 的正式配方。");
            if (result.Evidence && (!recipe.product || resolvedKind != BuildKind.Test))
                throw new BuildFailedException("故障取证请使用产品 Test，避免混入开发或专项辅助行为。");
            return result;
        }

        public static void ValidateProjectDefines(ResolvedProjectBuild build, string[] projectDefines)
        {
            string[] conflicts = (projectDefines ?? Array.Empty<string>()).Where(d =>
                d.StartsWith("MONSTER_BUILD_", StringComparison.Ordinal) || d.Contains("VALIDATION") ||
                d == "UNITY_INCLUDE_TESTS" || d == "MONSTER_COMBAT_EVIDENCE" || d == "MONSTER_KCP_DEVELOPMENT_BUILD").ToArray();
            if (conflicts.Length > 0)
                throw new BuildFailedException("请移除全局中的构建专用符号，改由构建配置生成：" + string.Join(", ", conflicts));
            ProjectBuildIdentity.ValidateOptions(build.Kind, build.Development, build.Recipe.testAssemblies, build.Recipe.defines);
        }

        private static T Parse<T>(string value) where T : struct =>
            Enum.TryParse(value, true, out T parsed) && Enum.IsDefined(typeof(T), parsed) ? parsed :
                throw new ArgumentException("无效的 " + typeof(T).Name + "：" + value);
    }
}
