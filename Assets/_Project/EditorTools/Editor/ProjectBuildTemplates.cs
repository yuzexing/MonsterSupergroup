using System;
using System.IO;
using System.Linq;
using MonsterSupergroup.Builds;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace MonsterSupergroup.EditorTools
{
    public static class ProjectBuildTemplates
    {
        public const string Root = "Assets/Settings/Build Profiles";
        // One-time migration. The existing native Windows asset supplies Unity's platform serialization.
        public static void Install()
        {
            NativeBuildProfileSettings.CheckVersion();
            string seed = Root + "/Window-test.asset";
            if (!File.Exists(seed)) throw new InvalidOperationException("一次性迁移已结束，或原生 Windows 种子缺失；已有模板请用 Unity 复制。");
            Create(seed, "Windows-Dev-Kcp", "product", BuildKind.Dev, BuildNetwork.Kcp);
            Create(seed, "Windows-Test-Steam", "product", BuildKind.Test, BuildNetwork.Steam);
            Create(seed, "Windows-Test-Evidence", "product", BuildKind.Test, BuildNetwork.Steam, evidence: true);
            Create(seed, "Windows-Test-Network", "product", BuildKind.Test, BuildNetwork.Steam, networkDiagnostics: true);
            Create(seed, "Windows-Test-Profiler", "product", BuildKind.Test, BuildNetwork.Steam, profiler: true);
            Create(seed, "Windows-Shipping", "product", BuildKind.Shipping, BuildNetwork.Steam);
            string[] names = { "Gameplay", "Wisp", "Options", "Handoff", "Sandbox", "Nordic" };
            for (int i = 1; i < ProjectBuildPurposes.Ids.Length; i++) Create(seed, "Windows-Dev-" + names[i - 1], ProjectBuildPurposes.Ids[i], BuildKind.Dev, BuildNetwork.Kcp);
            foreach (string old in new[] { "Window-dev", "Window-test" }) AssetDatabase.DeleteAsset(Root + "/" + old + ".asset");
            // Old result pointers are no longer a valid automatic selection source. Packages remain intact.
            var catalog = ProjectToolCatalog.Load();
            foreach (string id in catalog.builds.Select(b => b.id).Concat(catalog.buildAliases.Select(a => a.id)))
            {
                string pointer = "Library/ProjectTools/BuildResults/" + id + ".json";
                if (File.Exists(pointer)) File.Delete(pointer);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[BuildProfiles] Installed 12 templates; old profiles and recipe pointers removed.");
        }
        // Explicit, repeatable preparation entry points for projects that already completed migration.
        // These add independent profiles; they never activate a profile or alter the source template.
        public static void InstallNetwork() => InstallNetworkProfile("Windows-Test-Network", BuildDistribution.Steam);
        public static void InstallNetworkDirect() => InstallNetworkProfile("Windows-Test-Network-Direct", BuildDistribution.Direct);
        private static void InstallNetworkProfile(string name, BuildDistribution distribution)
        {
            NativeBuildProfileSettings.CheckVersion();
            string path = Root + "/" + name + ".asset";
            if (!File.Exists(path))
                Create(Root + "/Windows-Test-Steam.asset", name, "product", BuildKind.Test, BuildNetwork.Steam,
                    networkDiagnostics: true, distribution: distribution);
            var plan = ProjectBuildResolver.Resolve(ProjectBuildResolver.Load(path));
            if (plan.Kind != BuildKind.Test || plan.Purpose != "product" || plan.Network != BuildNetwork.Steam ||
                plan.Diagnostics != BuildDiagnostics.Network || plan.Distribution != distribution)
                throw new InvalidOperationException("已有网络模板配置不符，拒绝自动覆盖：" + path);
            ProjectBuildResolver.ValidateDefines(plan);
            Debug.Log("[BuildProfiles] Network profile ready (not activated): " + path);
        }
        private static void Create(string seed, string name, string purpose, BuildKind kind, BuildNetwork network, bool evidence = false,
            bool profiler = false, bool networkDiagnostics = false, BuildDistribution? distribution = null)
        {
            string path = Root + "/" + name + ".asset";
            if (File.Exists(path)) throw new IOException("拒绝覆盖已有模板：" + path);
            if (!AssetDatabase.CopyAsset(seed, path)) throw new IOException("无法复制原生 Windows Profile。");
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
            profile.name = name;
            profile.overrideGlobalScenes = true;
            profile.scenes = ProjectBuildPurposes.Get(purpose).scenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
            NativeBuildProfileSettings.SetTemplateOptions(profile, kind == BuildKind.Dev || profiler, profiler);
            var settings = profile.GetComponent<MonsterBuildSettings>() ?? profile.CreateComponent<MonsterBuildSettings>();
            settings.name = "MonsterBuildSettings";
            settings.PurposeId = purpose; settings.BuildKind = kind; settings.Network = network;
            settings.Distribution = distribution ?? (network == BuildNetwork.Kcp ? BuildDistribution.Direct : BuildDistribution.Steam);
            settings.Diagnostics = evidence ? BuildDiagnostics.Evidence : networkDiagnostics ? BuildDiagnostics.Network : BuildDiagnostics.Normal;
            profile.scriptingDefines = ProjectBuildDefines.Expected(settings);
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            ProjectBuildResolver.ValidateDefines(ProjectBuildResolver.Resolve(profile));
        }
    }
}
