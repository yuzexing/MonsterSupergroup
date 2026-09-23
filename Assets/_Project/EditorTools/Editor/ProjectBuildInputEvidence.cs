using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace MonsterSupergroup.EditorTools
{
    // File-only diagnostics: never imports, saves, or restores project settings.
    internal sealed class ProjectBuildInputEvidence : IDisposable
    {
        internal const string WatchedPath = "ProjectSettings/ProjectSettings.asset";
        internal const string InitialPlan = "initial-plan";
        internal const string BeforeUnity = "before-unity";
        internal const string AfterUnity = "after-unity";
        internal const string AfterIdentityCleanup = "after-identity-cleanup";
        internal const string AfterTmpRestore = "after-tmp-restore";
        internal const string FinalPlan = "final-plan";
        private static readonly string[] Stages = {
            InitialPlan, BeforeUnity, AfterUnity, AfterIdentityCleanup, AfterTmpRestore, FinalPlan
        };

        [DataContract]
        internal sealed class Snapshot
        {
            [DataMember] public string stage, utc, status = "NotReached", sha256, file, error;
            [DataMember] public long length;
        }
        [DataContract]
        private sealed class Comparison
        {
            [DataMember] public string from, to, kind, status, textStatus;
        }
        [DataContract]
        private sealed class Manifest
        {
            [DataMember] public int schema = 1;
            [DataMember] public string attemptId, startedUtc, finishedUtc, profileGuid, profilePath, buildId;
            [DataMember] public string source = WatchedPath, state = "Collecting", firstObservedChangeFrom, firstObservedChangeTo;
            [DataMember] public Snapshot[] snapshots;
            [DataMember] public Comparison[] comparisons;
            [DataMember] public List<string> errors = new List<string>();
        }

        private readonly string sourcePath;
        private readonly Action<string> warning;
        private readonly Manifest manifest;
        private readonly Dictionary<string, byte[]> captured = new Dictionary<string, byte[]>();
        private bool finished;
        private int reportNumber;
        internal string DirectoryPath { get; private set; }

        internal static ProjectBuildInputEvidence Start(string projectRoot, string profileGuid, string profilePath, Action<string> warning)
        {
            try { return new ProjectBuildInputEvidence(projectRoot, profileGuid, profilePath, warning); }
            catch (Exception e)
            {
                Warn(warning, "Cannot create input evidence; evidence is incomplete: " + e.Message);
                return null;
            }
        }

        private ProjectBuildInputEvidence(string projectRoot, string profileGuid, string profilePath, Action<string> warning)
        {
            this.warning = warning;
            sourcePath = Path.Combine(projectRoot, WatchedPath);
            string id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N");
            DirectoryPath = Path.Combine(projectRoot, "Logs/BuildProfilesResume", "input-" + id);
            if (Directory.Exists(DirectoryPath)) throw new IOException("Evidence directory already exists: " + DirectoryPath);
            Directory.CreateDirectory(DirectoryPath);
            Directory.CreateDirectory(Path.Combine(DirectoryPath, "reports"));
            manifest = new Manifest {
                attemptId = id, startedUtc = DateTime.UtcNow.ToString("O"), profileGuid = profileGuid, profilePath = profilePath,
                snapshots = Stages.Select(s => new Snapshot { stage = s }).ToArray()
            };
            SaveReports();
        }

        internal void BindBuildId(string buildId)
        {
            manifest.buildId = buildId;
            SaveReports();
        }

        // Called with the same bytes that InputFiles hashes, not a second disk read.
        internal void ObserveInput(string stage, string path, byte[] bytes)
        {
            if (path.Replace('\\', '/') != WatchedPath) return;
            CaptureBytes(stage, bytes);
        }

        internal void CompletePlanRead(string stage)
        {
            var snapshot = Find(stage);
            if (snapshot.status != "NotReached") return;
            snapshot.utc = DateTime.UtcNow.ToString("O");
            snapshot.status = "Missing";
            snapshot.error = "The watched file was absent from the completed input enumeration.";
            SaveReports();
        }

        internal void InputReadFailed(string stage, string path, Exception error)
        {
            if (path.Replace('\\', '/') != WatchedPath) return;
            var snapshot = Find(stage);
            snapshot.utc = DateTime.UtcNow.ToString("O");
            snapshot.status = error is FileNotFoundException || error is DirectoryNotFoundException ? "Missing" : "ReadFailed";
            snapshot.error = error.GetType().Name + ": " + error.Message;
            SaveReports();
        }

        internal void CaptureFile(string stage)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(sourcePath); }
            catch (Exception e) { InputReadFailed(stage, WatchedPath, e); return; }
            CaptureBytes(stage, bytes);
        }

        private Snapshot Find(string stage) => manifest.snapshots.Single(s => s.stage == stage);

        private void CaptureBytes(string stage, byte[] bytes)
        {
            var snapshot = Find(stage);
            if (snapshot.status != "NotReached")
            {
                RecordError("Duplicate stage was not overwritten: " + stage);
                SaveReports();
                return;
            }
            snapshot.utc = DateTime.UtcNow.ToString("O");
            try
            {
                byte[] copy = (byte[])bytes.Clone();
                snapshot.length = copy.LongLength;
                using (var hash = SHA256.Create())
                    snapshot.sha256 = BitConverter.ToString(hash.ComputeHash(copy)).Replace("-", "").ToLowerInvariant();
                string file = stage + ".asset";
                using (var stream = new FileStream(Path.Combine(DirectoryPath, file), FileMode.CreateNew, FileAccess.Write))
                    stream.Write(copy, 0, copy.Length);
                snapshot.file = file;
                snapshot.status = "Captured";
                captured.Add(stage, copy);
            }
            catch (Exception e)
            {
                snapshot.status = "WriteFailed";
                snapshot.error = e.GetType().Name + ": " + e.Message;
                RecordError("Snapshot " + stage + " failed: " + snapshot.error);
            }
            SaveReports();
        }

        public void Dispose()
        {
            if (finished) return;
            finished = true;
            manifest.finishedUtc = DateTime.UtcNow.ToString("O");
            SaveReports();
        }

        private void SaveReports()
        {
            // Diagnostics must not replace a build exception or interrupt any cleanup.
            try
            {
                var text = new StringBuilder();
                text.AppendLine("Input snapshots locate an observed interval, not a responsible callback.");
                text.AppendLine("Source: " + WatchedPath);
                text.AppendLine("Attempt: " + manifest.attemptId + "; BuildId: " + manifest.buildId);
                text.AppendLine();
                var comparisons = new List<Comparison>();
                manifest.firstObservedChangeFrom = manifest.firstObservedChangeTo = null;
                for (int i = 1; i < Stages.Length; i++)
                    comparisons.Add(Compare(Stages[i - 1], Stages[i], "Adjacent", text));
                comparisons.Add(Compare(InitialPlan, FinalPlan, "FirstLast", text));
                manifest.comparisons = comparisons.ToArray();
                // A missing stage leaves an unknown interval, even if all surviving snapshots match.
                bool complete = manifest.errors.Count == 0 && manifest.snapshots.All(s => s.status == "Captured");
                manifest.state = !finished ? "Collecting" : complete ? "Complete" : "Incomplete";
                string heading = "Evidence: " + manifest.state + "\nFirst observed difference: " +
                    (manifest.firstObservedChangeFrom == null ? "none observed (check completeness)" :
                        manifest.firstObservedChangeFrom + " -> " + manifest.firstObservedChangeTo) + "\n\n";
                foreach (var snapshot in manifest.snapshots)
                    text.AppendLine(snapshot.stage + ": " + snapshot.status + "; " + snapshot.error);
                foreach (string error in manifest.errors) text.AppendLine("Evidence error: " + error);
                // Immutable checkpoints also survive an interrupted build. Final reports are written once.
                string prefix = finished ? "" : Path.Combine("reports", (reportNumber++).ToString("D4") + "-");
                WriteReport(prefix + "diff.txt", stream => {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                        writer.Write(heading + text);
                });
                WriteReport(prefix + "manifest.json", stream => new DataContractJsonSerializer(typeof(Manifest)).WriteObject(stream, manifest));
            }
            catch (Exception e) { RecordError("Evidence report could not be saved: " + e.Message); }
        }

        private Comparison Compare(string from, string to, string kind, StringBuilder report)
        {
            var result = new Comparison { from = from, to = to, kind = kind, status = "Unknown", textStatus = "Unavailable" };
            report.AppendLine(kind + ": " + from + " -> " + to);
            if (!captured.TryGetValue(from, out var before) || !captured.TryGetValue(to, out var after))
            {
                report.AppendLine("Unknown: " + Find(from).status + " / " + Find(to).status);
                report.AppendLine();
                return result;
            }
            if (before.SequenceEqual(after))
            {
                result.status = "Unchanged";
                result.textStatus = "SameBytes";
                report.AppendLine("Unchanged (identical bytes).");
            }
            else
            {
                result.status = "Changed";
                if (manifest.firstObservedChangeFrom == null)
                {
                    manifest.firstObservedChangeFrom = from;
                    manifest.firstObservedChangeTo = to;
                }
                try
                {
                    string a = Decode(before), b = Decode(after);
                    result.textStatus = a == b ? "SameTextDifferentBytes" : "TextChanged";
                    report.AppendLine(result.textStatus);
                    if (a != b) AppendLineDiff(a, b, report);
                }
                catch (DecoderFallbackException)
                {
                    result.textStatus = "Undecodable";
                    report.AppendLine("Bytes differ; text decoding failed. Inspect the raw snapshots.");
                }
            }
            report.AppendLine();
            return result;
        }

        private static string Decode(byte[] bytes)
        {
            Encoding encoding = new UTF8Encoding(false, true);
            int skip = 0;
            if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0)
            { encoding = new UTF32Encoding(false, true, true); skip = 4; }
            else if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff)
            { encoding = new UTF32Encoding(true, true, true); skip = 4; }
            else if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
            { encoding = new UnicodeEncoding(false, true, true); skip = 2; }
            else if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
            { encoding = new UnicodeEncoding(true, true, true); skip = 2; }
            else if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) skip = 3;
            return encoding.GetString(bytes, skip, bytes.Length - skip).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        // One bounded hunk between the common prefix/suffix; no YAML interpretation or rewrite.
        private static void AppendLineDiff(string before, string after, StringBuilder report)
        {
            string[] a = before.Split('\n'), b = after.Split('\n');
            int first = 0, endA = a.Length, endB = b.Length;
            while (first < endA && first < endB && a[first] == b[first]) first++;
            while (endA > first && endB > first && a[endA - 1] == b[endB - 1]) { endA--; endB--; }
            report.AppendLine("@@ old " + (first + 1) + "," + (endA - first) + " new " + (first + 1) + "," + (endB - first) + " @@");
            for (int i = Math.Max(0, first - 2); i < first; i++) report.AppendLine("  " + (i + 1) + "/" + (i + 1) + ": " + a[i]);
            for (int i = first; i < endA; i++) report.AppendLine("- " + (i + 1) + ": " + a[i]);
            for (int i = first; i < endB; i++) report.AppendLine("+ " + (i + 1) + ": " + b[i]);
            for (int i = 0; i < 2 && endA + i < a.Length; i++)
                report.AppendLine("  " + (endA + i + 1) + "/" + (endB + i + 1) + ": " + a[endA + i]);
        }

        private void WriteReport(string name, Action<Stream> write)
        {
            string path = Path.Combine(DirectoryPath, name), temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) write(stream);
            File.Move(temporary, path);
        }
        private void RecordError(string message)
        {
            manifest.errors.Add(message);
            Warn(warning, DirectoryPath + ": Evidence is incomplete. " + message);
        }
        private static void Warn(Action<string> warning, string message)
        {
            try { warning?.Invoke(message); } catch { /* A log listener must not change build behavior. */ }
        }
    }
}
