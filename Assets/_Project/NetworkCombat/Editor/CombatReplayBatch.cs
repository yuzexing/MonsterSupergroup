using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using UnityEditor;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class CombatReplayBatch
    {
        [Serializable] public sealed class FixtureResult { public string path; public ReplayReport report; }
        [Serializable] public sealed class FixtureManifest { public string[] fixtures; }

        public static FixtureResult[] EvaluateFiles(IEnumerable<string> paths) => paths.Select(path => {
            ReplayReport report;
            try
            {
                var fixture = EvidenceJson.Decode<ReplayFixture>(File.ReadAllText(path));
                if (fixture == null) throw new InvalidDataException("NullFixture");
                report = CombatReplay.Run(fixture);
                if (report.reliable && report.passed && report.executed == 0)
                { report.reliable = false; report.passed = false; report.reason = "NoReplaySteps"; }
            }
            catch (Exception error) { report = new ReplayReport { reason = "FixtureReadFailed:" + error.GetType().Name + ":" + error.Message }; }
            return new FixtureResult { path = path, report = report };
        }).ToArray();

        // Explicitly listed .json files are supported without accidentally reading result/summary files.
        public static string[] ReadManifest(string path)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            var manifest = EvidenceJson.Decode<FixtureManifest>(File.ReadAllText(path));
            if (manifest?.fixtures == null) throw new InvalidDataException("MissingFixtureManifestEntries");
            return manifest.fixtures.Select(entry => {
                if (string.IsNullOrWhiteSpace(entry) || !entry.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("InvalidFixtureManifestEntry");
                return Path.GetFullPath(Path.Combine(directory, entry));
            }).ToArray();
        }

        public static void RunDirectory()
        {
            string directory = null;
            try
            {
                var args = Environment.GetCommandLineArgs();
                string input = args.FirstOrDefault(a => a.StartsWith("--combat-fixtures=", StringComparison.Ordinal))?.Substring("--combat-fixtures=".Length);
                string manifest = args.FirstOrDefault(a => a.StartsWith("--combat-fixture-manifest=", StringComparison.Ordinal))?.Substring("--combat-fixture-manifest=".Length);
                if ((input == null) == (manifest == null)) throw new ArgumentException("Specify one fixture directory or explicit fixture manifest.");
                directory = input != null ? Path.GetFullPath(input) : Path.GetDirectoryName(Path.GetFullPath(manifest));
                FixtureResult[] reports;
                try
                {
                    var paths = manifest != null ? ReadManifest(manifest) : Directory.GetFiles(input, "*.fixture.json").OrderBy(p => p, StringComparer.Ordinal).ToArray();
                    reports = EvaluateFiles(paths);
                }
                catch (Exception error)
                { reports = new[] { new FixtureResult { path = manifest ?? input, report = new ReplayReport { reason = "FixtureDiscoveryFailed:" + error.GetType().Name + ":" + error.Message } } }; }
                var summary = CombatReplayBatchSummary.Classify(reports.Select(r => r.report));
                Directory.CreateDirectory(directory);
                EvidenceJson.AtomicWrite(Path.Combine(directory, "replay-results.json"), EvidenceJson.Encode(reports));
                EvidenceJson.AtomicWrite(Path.Combine(directory, "replay-summary.json"), EvidenceJson.Encode(summary));
                EditorApplication.Exit(summary.exitCode);
            }
            catch (Exception error) { UnityEngine.Debug.LogException(error); EditorApplication.Exit(3); }
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
                if (report.reliable && report.passed && report.executed == 0)
                { report.reliable = false; report.passed = false; report.reason = "NoReplaySteps"; }
                EvidenceJson.AtomicWrite(output, EvidenceJson.Encode(report));
                EditorApplication.Exit(CombatReplayBatchSummary.Classify(new[] { report }).exitCode);
            }
            catch (Exception error)
            {
                EvidenceJson.AtomicWrite(output, EvidenceJson.Encode(new ReplayReport { reason = error.Message }));
                EditorApplication.Exit(3);
            }
        }
    }
}
