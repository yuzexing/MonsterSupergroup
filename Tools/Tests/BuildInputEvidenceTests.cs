using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MonsterSupergroup.EditorTools;

// Compiled together with the production file-only component by Test-BuildInputEvidence.ps1.
internal static class BuildInputEvidenceTests
{
    private static readonly string[] Stages = {
        ProjectBuildInputEvidence.InitialPlan, ProjectBuildInputEvidence.BeforeUnity, ProjectBuildInputEvidence.AfterUnity,
        ProjectBuildInputEvidence.AfterIdentityCleanup, ProjectBuildInputEvidence.AfterTmpRestore, ProjectBuildInputEvidence.FinalPlan
    };
    private const string Original = "PlayerSettings:\n  bundleVersion: 0.0.1\n  setting: 1\n";
    private static string root;
    private static int assertions;
    private static int failed;
    private static readonly List<object> results = new List<object>();

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    private static string Source(string project) => Path.Combine(project, ProjectBuildInputEvidence.WatchedPath);
    private static void Set(string project, string text) => File.WriteAllText(Source(project), text, new UTF8Encoding(false));
    private static ProjectBuildInputEvidence Start(string project, Action<string> warning = null) =>
        ProjectBuildInputEvidence.Start(project, "0123456789abcdef0123456789abcdef", "Assets/Settings/Build Profiles/Test Profile.asset", warning);
    private static JsonElement Manifest(ProjectBuildInputEvidence evidence)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence.DirectoryPath, "manifest.json")));
        return doc.RootElement.Clone();
    }
    private static JsonElement Snapshot(JsonElement manifest, string stage) => manifest.GetProperty("snapshots").EnumerateArray().Single(s => s.GetProperty("stage").GetString() == stage);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Capture(string project, ProjectBuildInputEvidence evidence, string stage)
    {
        byte[] before = File.ReadAllBytes(Source(project));
        evidence.CaptureFile(stage);
        Check(before.SequenceEqual(File.ReadAllBytes(Source(project))), "Capture changed the source: " + stage);
    }
    private static void CaptureAll(string project, ProjectBuildInputEvidence evidence)
    {
        foreach (string stage in Stages) Capture(project, evidence, stage);
        evidence.Dispose();
    }
    private static void VerifySnapshots(ProjectBuildInputEvidence evidence)
    {
        foreach (var snapshot in Manifest(evidence).GetProperty("snapshots").EnumerateArray())
        {
            if (snapshot.GetProperty("status").GetString() != "Captured") continue;
            byte[] saved = File.ReadAllBytes(Path.Combine(evidence.DirectoryPath, snapshot.GetProperty("file").GetString()));
            Check(saved.LongLength == snapshot.GetProperty("length").GetInt64(), "Wrong raw byte length.");
            Check(Hash(saved) == snapshot.GetProperty("sha256").GetString(), "Snapshot hash differs from saved bytes.");
            Check(DateTimeOffset.TryParse(snapshot.GetProperty("utc").GetString(), out _), "Missing phase timestamp.");
        }
    }
    private static void Run(string name, Action<string> test)
    {
        string project = Path.Combine(root, (results.Count + 1).ToString("D2") + " " + name);
        Directory.CreateDirectory(Path.GetDirectoryName(Source(project)));
        Set(project, Original);
        try { test(project); results.Add(new { name, passed = true, error = "", project }); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; results.Add(new { name, passed = false, error = e.ToString(), project }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    private static void EncodingCase(string project, byte[] changed)
    {
        using var evidence = Start(project);
        Capture(project, evidence, Stages[0]);
        File.WriteAllBytes(Source(project), changed);
        foreach (string stage in Stages.Skip(1)) Capture(project, evidence, stage);
        evidence.Dispose();
        var manifest = Manifest(evidence);
        var comparison = manifest.GetProperty("comparisons")[0];
        Check(comparison.GetProperty("status").GetString() == "Changed", "Byte-only drift was lost.");
        Check(comparison.GetProperty("textStatus").GetString() == "SameTextDifferentBytes", "Encoding/newline drift was not distinguished.");
        VerifySnapshots(evidence);
    }

    public static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        Run("unchanged", project => {
            using var evidence = Start(project);
            evidence.BindBuildId("fixture-build-id");
            CaptureAll(project, evidence);
            var manifest = Manifest(evidence);
            Check(manifest.GetProperty("state").GetString() == "Complete", "Complete snapshots marked incomplete.");
            Check(manifest.GetProperty("buildId").GetString() == "fixture-build-id", "BuildId lost.");
            Check(manifest.GetProperty("profileGuid").GetString() == "0123456789abcdef0123456789abcdef", "Profile GUID lost.");
            Check(manifest.GetProperty("profilePath").GetString().Contains("Test Profile.asset"), "Profile path lost.");
            Check(manifest.GetProperty("comparisons").EnumerateArray().All(c => c.GetProperty("status").GetString() == "Unchanged"), "Invented drift.");
            VerifySnapshots(evidence);
        });
        Run("field change", project => {
            using var evidence = Start(project);
            Capture(project, evidence, Stages[0]);
            Set(project, Original.Replace("0.0.1", "0.0.2"));
            foreach (string stage in Stages.Skip(1)) Capture(project, evidence, stage);
            evidence.Dispose();
            Check(Manifest(evidence).GetProperty("firstObservedChangeTo").GetString() == Stages[1], "Wrong first changed stage.");
            string diff = File.ReadAllText(Path.Combine(evidence.DirectoryPath, "diff.txt"));
            Check(diff.Contains("- 2:   bundleVersion: 0.0.1") && diff.Contains("+ 2:   bundleVersion: 0.0.2"), "Missing line-numbered field difference.");
            VerifySnapshots(evidence);
        });
        Run("changed then restored", project => {
            using var evidence = Start(project);
            Capture(project, evidence, Stages[0]);
            Capture(project, evidence, Stages[1]);
            Set(project, Original.Replace("setting: 1", "setting: 2"));
            Capture(project, evidence, Stages[2]);
            Capture(project, evidence, Stages[3]);
            Set(project, Original);
            Capture(project, evidence, Stages[4]);
            Capture(project, evidence, Stages[5]);
            evidence.Dispose();
            var manifest = Manifest(evidence);
            Check(manifest.GetProperty("comparisons")[5].GetProperty("status").GetString() == "Unchanged", "Restoration not recognized.");
            Check(manifest.GetProperty("comparisons").EnumerateArray().Count(c => c.GetProperty("status").GetString() == "Changed") == 2, "Intermediate mutation lost.");
            Check(File.ReadAllText(Path.Combine(evidence.DirectoryPath, Stages[2] + ".asset")).Contains("setting: 2"), "Intermediate bytes lost.");
            VerifySnapshots(evidence);
        });
        Run("newline bytes", p => EncodingCase(p, Encoding.UTF8.GetBytes(Original.Replace("\n", "\r\n"))));
        Run("UTF8 BOM bytes", p => EncodingCase(p, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Original)).ToArray()));
        Run("UTF16 bytes", p => EncodingCase(p, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Original)).ToArray()));
        Run("UTF32 BE bytes", p => {
            var encoding = new UTF32Encoding(true, true, true);
            EncodingCase(p, encoding.GetPreamble().Concat(encoding.GetBytes(Original)).ToArray());
        });
        Run("missing source", project => {
            using var evidence = Start(project);
            Capture(project, evidence, Stages[0]);
            File.Delete(Source(project));
            evidence.CaptureFile(Stages[1]);
            evidence.Dispose();
            var manifest = Manifest(evidence);
            Check(Snapshot(manifest, Stages[1]).GetProperty("status").GetString() == "Missing", "Missing source mislabeled.");
            Check(manifest.GetProperty("state").GetString() == "Incomplete", "Missing source considered complete.");
            Check(manifest.GetProperty("comparisons")[0].GetProperty("status").GetString() == "Unknown", "Missing source treated as unchanged.");
        });
        Run("locked source", project => {
            using var evidence = Start(project);
            using (var held = new FileStream(Source(project), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) evidence.CaptureFile(Stages[0]);
            evidence.Dispose();
            Check(Snapshot(Manifest(evidence), Stages[0]).GetProperty("status").GetString() == "ReadFailed", "Sharing violation mislabeled.");
            Check(File.ReadAllText(Source(project)) == Original, "Locked source changed.");
        });
        Run("snapshot write failure", project => {
            var warnings = new List<string>();
            using var evidence = Start(project, warnings.Add);
            Directory.CreateDirectory(Path.Combine(evidence.DirectoryPath, Stages[1] + ".asset"));
            CaptureAll(project, evidence);
            var manifest = Manifest(evidence);
            Check(Snapshot(manifest, Stages[1]).GetProperty("status").GetString() == "WriteFailed", "Snapshot write failure hidden.");
            Check(Snapshot(manifest, Stages[4]).GetProperty("status").GetString() == "Captured", "Later capture skipped after error.");
            Check(manifest.GetProperty("state").GetString() == "Incomplete" && warnings.Count > 0, "Failed evidence not reported: " + string.Join("\n", warnings));
            VerifySnapshots(evidence);
        });
        Run("report write failure and throwing listener", project => {
            int warnings = 0;
            using var evidence = Start(project, message => { warnings++; throw new Exception("listener"); });
            string blocked = Path.Combine(evidence.DirectoryPath, "reports", "0001-diff.txt.tmp");
            Directory.CreateDirectory(blocked);
            Capture(project, evidence, Stages[0]);
            Directory.Delete(blocked);
            foreach (string stage in Stages.Skip(1)) Capture(project, evidence, stage);
            evidence.Dispose();
            var manifest = Manifest(evidence);
            Check(warnings > 0 && manifest.GetProperty("errors").GetArrayLength() > 0, "Report failure not retained.");
            Check(manifest.GetProperty("state").GetString() == "Incomplete", "Report recovery erased the prior failure.");
            VerifySnapshots(evidence);
        });
        Run("cannot create evidence directory", project => {
            File.WriteAllText(Path.Combine(project, "Logs"), "blocked");
            int warnings = 0;
            var evidence = Start(project, message => { warnings++; throw new Exception("listener"); });
            Check(evidence == null && warnings == 1, "Directory failure was not contained.");
            Check(File.ReadAllText(Source(project)) == Original, "Failed setup changed source.");
        });
        Run("not reached", project => {
            using var evidence = Start(project);
            Capture(project, evidence, Stages[0]);
            evidence.Dispose();
            var manifest = Manifest(evidence);
            Check(Snapshot(manifest, Stages[5]).GetProperty("status").GetString() == "NotReached", "Unexecuted phase mislabeled.");
            Check(manifest.GetProperty("state").GetString() == "Incomplete", "Early termination considered complete.");
            Check(manifest.GetProperty("buildId").ValueKind == JsonValueKind.Null, "Invented BuildId.");
        });
        Run("immutable checkpoints before finalization", project => {
            using var evidence = Start(project);
            Capture(project, evidence, Stages[0]);
            string checkpoint = Path.Combine(evidence.DirectoryPath, "reports", "0001-manifest.json");
            byte[] saved = File.ReadAllBytes(checkpoint);
            using (var doc = JsonDocument.Parse(saved))
            {
                Check(doc.RootElement.GetProperty("state").GetString() == "Collecting", "Unfinished evidence marked complete.");
                Check(Snapshot(doc.RootElement, Stages[0]).GetProperty("status").GetString() == "Captured", "Latest completed capture not persisted.");
            }
            Check(!File.Exists(Path.Combine(evidence.DirectoryPath, "manifest.json")), "Final report published before finalization.");
            Capture(project, evidence, Stages[1]);
            evidence.Dispose();
            Check(saved.SequenceEqual(File.ReadAllBytes(checkpoint)), "Later reports overwrote checkpoint.");
        });
        Run("separate attempts with spaces", project => {
            using var first = Start(project);
            CaptureAll(project, first);
            byte[] originalManifest = File.ReadAllBytes(Path.Combine(first.DirectoryPath, "manifest.json"));
            using var second = Start(project);
            Set(project, "different\n");
            CaptureAll(project, second);
            Check(first.DirectoryPath != second.DirectoryPath, "Attempts share output directory.");
            Check(originalManifest.SequenceEqual(File.ReadAllBytes(Path.Combine(first.DirectoryPath, "manifest.json"))), "New attempt overwrote old evidence.");
            Check(File.ReadAllText(Path.Combine(first.DirectoryPath, Stages[0] + ".asset")) == Original, "Old snapshot overwritten.");
        });
        Run("exact hash read bytes", project => {
            using var evidence = Start(project);
            byte[] readForHash = File.ReadAllBytes(Source(project));
            string hash = Hash(readForHash);
            Set(project, "changed after digest read\n");
            evidence.ObserveInput(Stages[0], ProjectBuildInputEvidence.WatchedPath, readForHash);
            readForHash[0] = 0;
            evidence.CompletePlanRead(Stages[0]);
            evidence.Dispose();
            var snapshot = Snapshot(Manifest(evidence), Stages[0]);
            Check(snapshot.GetProperty("sha256").GetString() == hash, "Observer reread the current disk instead of the hashed bytes.");
            Check(File.ReadAllText(Path.Combine(evidence.DirectoryPath, Stages[0] + ".asset")) == Original, "Caller buffer mutation changed snapshot.");
            Check(File.ReadAllText(Source(project)) == "changed after digest read\n", "Observer restored/overwrote source.");
        });
        Run("only watched input and enumeration missing", project => {
            using var evidence = Start(project);
            evidence.ObserveInput(Stages[0], "Assets/Other.cs", Encoding.UTF8.GetBytes("ignored"));
            evidence.CompletePlanRead(Stages[0]);
            evidence.InputReadFailed(Stages[5], ProjectBuildInputEvidence.WatchedPath, new IOException("fixture read failure"));
            evidence.Dispose();
            var manifest = Manifest(evidence);
            Check(Snapshot(manifest, Stages[0]).GetProperty("status").GetString() == "Missing", "Unrelated input captured as settings.");
            Check(Snapshot(manifest, Stages[5]).GetProperty("status").GetString() == "ReadFailed", "Digest-read failure hidden.");
            Check(Directory.GetFiles(evidence.DirectoryPath, "*.asset").Length == 0, "Unrelated source was archived.");
        });
        Run("undecodable bytes", project => {
            using var evidence = Start(project);
            Capture(project, evidence, Stages[0]);
            File.WriteAllBytes(Source(project), new byte[] { 0xc3, 0x28 });
            foreach (string stage in Stages.Skip(1)) Capture(project, evidence, stage);
            evidence.Dispose();
            var comparison = Manifest(evidence).GetProperty("comparisons")[0];
            Check(comparison.GetProperty("status").GetString() == "Changed" && comparison.GetProperty("textStatus").GetString() == "Undecodable", "Invalid text hid byte drift.");
            VerifySnapshots(evidence);
        });
        Run("duplicate capture preserves first", project => {
            using var evidence = Start(project);
            Capture(project, evidence, Stages[0]);
            Set(project, "second\n");
            Capture(project, evidence, Stages[0]);
            evidence.Dispose();
            Check(File.ReadAllText(Path.Combine(evidence.DirectoryPath, Stages[0] + ".asset")) == Original, "Duplicate phase overwrote evidence.");
            Check(Manifest(evidence).GetProperty("errors").GetArrayLength() == 1, "Duplicate not recorded.");
        });
        Run("original exception survives diagnostic failures", project => {
            Exception original = new InvalidOperationException("original build failure");
            int cleanups = 0;
            try
            {
                using var evidence = Start(project, message => { throw new Exception("log failure"); });
                Directory.CreateDirectory(Path.Combine(evidence.DirectoryPath, "diff.txt.tmp"));
                try { throw original; }
                finally
                {
                    evidence.CaptureFile(Stages[2]);
                    cleanups++;
                    evidence.CaptureFile(Stages[3]);
                    cleanups++;
                    evidence.CaptureFile(Stages[4]);
                }
            }
            catch (Exception e) { Check(ReferenceEquals(e, original), "Diagnostic error replaced original exception."); }
            Check(cleanups == 2, "Diagnostic error interrupted the test cleanup sequence.");
        });

        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new {
            scope = "Offline production file component; no Unity lifecycle execution", total = results.Count,
            passed = results.Count - failed, failed, assertions, cases = results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Input evidence: " + (results.Count - failed) + "/" + results.Count + " cases passed; " + assertions + " assertions.");
        return failed == 0 ? 0 : 1;
    }
}
