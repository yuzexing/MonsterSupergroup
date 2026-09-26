using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonsterSupergroup.Builds;
using UnityEditor.Build;

namespace MonsterSupergroup.EditorTools
{
    public static class ProjectBuildPackage
    {
        public static void Validate(string executable, ResolvedProjectBuild plan, BuildInfo info)
        {
            if (!File.Exists(executable) || new FileInfo(executable).Length == 0) throw new BuildFailedException("本次构建缺少 EXE。");
            string root = Path.GetDirectoryName(executable);
            string data = Path.Combine(root, Path.GetFileNameWithoutExtension(executable) + "_Data");
            string managed = Path.Combine(data, "Managed");
            string assembly = Path.Combine(managed, "MonsterSupergroup.Build.dll");
            if (!File.Exists(assembly)) throw new BuildFailedException("无法验证本次 Player 的编译能力：缺少托管构建程序集。当前交付校验支持 Mono Player。");
            ValidateCompiledAssembly(assembly, plan.Kind, plan.Tools, plan.Evidence, plan.Network.ToString(), plan.NetworkDiagnostics);
            bool tests = File.Exists(Path.Combine(managed, "MonsterSupergroup.Gameplay.Tests.PlayMode.dll"));
            if (tests != plan.Recipe.testAssemblies || !tests && Directory.GetFiles(managed, "MonsterSupergroup.*Tests*.dll").Length > 0)
                throw new BuildFailedException("实际 Player 测试程序集与用途不一致。");
            string appId = Path.Combine(root, "steam_appid.txt");
            bool needsAppId = plan.Network == BuildNetwork.Steam && plan.Distribution == BuildDistribution.Direct;
            if (File.Exists(appId) != needsAppId || needsAppId && File.ReadAllText(appId).Trim() != File.ReadAllText("steam_appid.txt").Trim())
                throw new BuildFailedException("steam_appid.txt 与分发规则不一致。");
            string evidence = Path.Combine(root, "combat-build.json");
            if (File.Exists(evidence) != plan.Evidence || plan.Evidence && !File.ReadAllText(evidence).Contains(info.BuildId))
                throw new BuildFailedException("取证清单与本次构建身份不一致。");
            string embedded = File.ReadAllText(ProjectBuildIdentity.InfoPath(executable));
            if (embedded != info.ToJson() || File.ReadAllText(Path.Combine(root, "build-complete.json")) != embedded ||
                File.ReadAllText(Path.Combine(root, "build-plan.json")) != plan.ToJson()) throw new BuildFailedException("包内身份、计划或成功标记不一致。");
        }
        public static void ValidateCompiledAssembly(string path, BuildKind kind, bool tools, bool evidence, string network, bool networkDiagnostics = false)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var type = assembly.MainModule.GetType("MonsterSupergroup.Builds.BuildFeatures") ?? throw new BuildFailedException("Player 缺少 BuildFeatures。");
            if (Integer(type, "get_CompiledKind") != (int)kind || Integer(type, "get_ToolsCompiled") != (tools ? 1 : 0) ||
                Integer(type, "get_EvidenceCompiled") != (evidence ? 1 : 0) ||
                Integer(type, "get_NetworkDiagnosticsCompiled") != (networkDiagnostics ? 1 : 0) || evidence && networkDiagnostics ||
                type.Methods.Single(m => m.Name == "get_CompiledDiagnostics").Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr).Operand as string != (evidence ? "Evidence" : networkDiagnostics ? "Network" : "Normal") ||
                type.Methods.Single(m => m.Name == "get_CompiledNetwork").Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr).Operand as string != network)
                throw new BuildFailedException("实际 Player 编译能力与本次计划不一致。");
        }
        private static int Integer(TypeDefinition type, string name)
        {
            var constants = type.Methods.Single(m => m.Name == name).Body.Instructions.Where(i =>
                i.OpCode == OpCodes.Ldc_I4_0 || i.OpCode == OpCodes.Ldc_I4_1 || i.OpCode == OpCodes.Ldc_I4_2).ToArray();
            if (constants.Length != 1) throw new BuildFailedException("无法验证编译常量：" + name);
            return constants[0].OpCode == OpCodes.Ldc_I4_0 ? 0 : constants[0].OpCode == OpCodes.Ldc_I4_1 ? 1 : 2;
        }
    }
}
