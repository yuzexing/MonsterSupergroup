using System;
using System.IO;
using System.Linq;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using UnityEditor;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class CombatReplayBatch
    {
        public static void RunDirectory()
        {
            string input = Environment.GetCommandLineArgs().First(a => a.StartsWith("--combat-fixtures=", StringComparison.Ordinal)).Substring(18);
            var reports = Directory.GetFiles(input, "*.fixture.json").OrderBy(p => p, StringComparer.Ordinal)
                .Select(path => new { path, report = CombatReplay.Run(EvidenceJson.Decode<ReplayFixture>(File.ReadAllText(path))) }).ToArray();
            EvidenceJson.AtomicWrite(Path.Combine(input, "replay-results.json"), EvidenceJson.Encode(reports));
            EditorApplication.Exit(reports.All(r => r.report.passed || !r.report.reliable) ? 0 : 2);
        }
        public static void CompilePlayer()
        {
            int exit = 0;
            try
            {
                string output = Environment.GetCommandLineArgs().First(a => a.StartsWith("--combat-compile-output=", StringComparison.Ordinal)).Substring(24);
                Directory.CreateDirectory(output);
                var result = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(new UnityEditor.Build.Player.ScriptCompilationSettings {
                    target = BuildTarget.StandaloneWindows64, group = BuildTargetGroup.Standalone,
                    options = UnityEditor.Build.Player.ScriptCompilationOptions.None, extraScriptingDefines = new[] { "MONSTER_COMBAT_EVIDENCE" }
                }, output);
                if (result.assemblies == null || result.assemblies.Count == 0) throw new InvalidOperationException("Player script compilation produced no assemblies.");
                EvidenceJson.AtomicWrite(Path.Combine(output, "compile-result.json"), EvidenceJson.Encode(new { success = true, result.assemblies }));
            }
            catch (Exception error) { UnityEngine.Debug.LogException(error); exit = 1; }
            finally { EditorApplication.Exit(exit); }
        }
        // Unity -batchmode -nographics -executeMethod ...CombatReplayBatch.Run --combat-fixture=... --combat-result=...
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string input = args.First(a => a.StartsWith("--combat-fixture=", StringComparison.Ordinal)).Substring(17);
            string output = args.First(a => a.StartsWith("--combat-result=", StringComparison.Ordinal)).Substring(16);
            try
            {
                var fixture = EvidenceJson.Decode<ReplayFixture>(File.ReadAllText(input));
                if (Array.IndexOf(args, "--combat-minimize") >= 0)
                {
                    fixture = CombatReplay.Minimize(fixture);
                    EvidenceJson.AtomicWrite(output + ".fixture.json", EvidenceJson.Encode(fixture));
                }
                var report = CombatReplay.Run(fixture);
                EvidenceJson.AtomicWrite(output, EvidenceJson.Encode(report));
                EditorApplication.Exit(report.passed ? 0 : report.reliable ? 2 : 3);
            }
            catch (Exception error)
            {
                EvidenceJson.AtomicWrite(output, EvidenceJson.Encode(new ReplayReport { reason = error.Message }));
                EditorApplication.Exit(3);
            }
        }
    }
}
