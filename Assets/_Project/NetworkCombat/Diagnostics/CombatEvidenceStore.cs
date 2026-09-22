using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public sealed class EvidenceStoreOptions
    {
        public long SessionBytes = 8L << 30, TotalBytes = 32L << 30;
        public int QueueBytes = 32 << 20, ReservedBytes = 4 << 20, SegmentBytes = 32 << 20;
        public double SegmentSeconds = 60;
    }

    [Serializable]
    public sealed class EvidenceCoverage
    {
        public int schemaVersion = 1;
        public string captureId, runId, produced = "0", written = "0", flushed = "0", failure;
        public uint round;
        public bool complete, tailUnknown = true;
        public long dropped, queuedBytes;
        public List<EvidenceGap> gaps = new();
    }
    [Serializable] public sealed class EvidenceGap { public string first, last, reason; public long count; }
    [Serializable] public sealed class EvidenceFile { public string path, revision; public long length; }

    /// <summary>Single writer owns files. Network, gameplay and exception callbacks only enqueue bounded work.</summary>
    public sealed class CombatEvidenceStore : IDisposable
    {
        public const int MaximumBlockBytes = 64 * 1024;
        private readonly EvidenceStoreOptions options;
        private readonly Queue<(int bytes, Action action)> queue = new();
        private readonly object gate = new();
        private readonly AutoResetEvent wake = new(false);
        private readonly Thread worker;
        private readonly Dictionary<string, Source> sources = new();
        private readonly Dictionary<string, EvidenceCoverage> health = new();
        private readonly Dictionary<string, long> usage = new();
        private long totalUsage = -1;
        private bool stopping, closed;
        private long pendingBytes, dropped, writeTicks;
        private double nextFlush, nextPrune;
        public string Root { get; }
        public string LastFailure { get; private set; }
        public long PendingBytes { get { lock (gate) return pendingBytes; } }
        public long Dropped => Interlocked.Read(ref dropped);
        public double WriteMilliseconds => Interlocked.Read(ref writeTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        public bool Closed => Volatile.Read(ref closed);

        public CombatEvidenceStore(string root, EvidenceStoreOptions options = null)
        {
            Root = Path.GetFullPath(root); this.options = options ?? new EvidenceStoreOptions();
            worker = new Thread(Consume) { IsBackground = true, Name = "Combat evidence disk" };
            worker.Start();
        }

        public bool TryWrite(DiagnosticRecord record)
        {
            string key = SourceKey(record.runId, record.round, record.captureId);
            lock (gate)
            {
                if (!health.TryGetValue(key, out var state)) health.Add(key, state = new EvidenceCoverage {
                    captureId = record.captureId, runId = record.runId, round = record.round });
                state.produced = record.recordSequence;
                int bytes = Math.Max(2048, record.estimatedBytes);
                if (stopping || bytes > options.QueueBytes || pendingBytes + bytes > options.QueueBytes - (record.critical ? 0 : options.ReservedBytes))
                {
                    state.dropped++; Interlocked.Increment(ref dropped);
                    Gap(state, record.recordSequence, "QueueOverload"); return false;
                }
                pendingBytes += bytes;
                queue.Enqueue((bytes, () => Append(key, record)));
            }
            wake.Set(); return true;
        }

        public bool Schedule(int retainedBytes, Action action)
        {
            lock (gate)
            {
                if (stopping || pendingBytes + retainedBytes > options.QueueBytes - options.ReservedBytes) return false;
                pendingBytes += retainedBytes; queue.Enqueue((retainedBytes, action));
            }
            wake.Set(); return true;
        }

        private static string SourceKey(string run, uint round, string capture) =>
            SafePart(run ?? "boot") + "/" + round + "/sources/" + SafePart(capture);
        private static string SafePart(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 100 || value.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new InvalidDataException("Invalid evidence identity.");
            return value;
        }
        public string Resolve(string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains(":") || relative.Split('/', '\\').Any(p => p == ".." || p.Length == 0))
                throw new InvalidDataException("Invalid evidence path.");
            string result = Path.GetFullPath(Path.Combine(Root, relative));
            if (!result.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Evidence path escaped root.");
            return result;
        }

        private void Append(string key, DiagnosticRecord record)
        {
            EvidenceCoverage state; lock (gate) state = health[key];
            try
            {
                if (!sources.TryGetValue(key, out var source))
                {
                    string directory = Resolve(key); Directory.CreateDirectory(directory);
                    Recover(directory);
                    // Closing a previous context frees its handles and makes the ended round eligible for retention.
                    foreach (var previous in sources.Where(p => p.Key.EndsWith("/sources/" + record.captureId, StringComparison.Ordinal) && p.Key != key).ToArray())
                    {
                        previous.Value.Writer?.Flush(); previous.Value.Stream?.Flush(true); previous.Value.Dispose();
                        lock (gate) { health[previous.Key].flushed = health[previous.Key].written; health[previous.Key].tailUnknown = false; health[previous.Key].complete = health[previous.Key].dropped == 0 && health[previous.Key].failure == null; }
                        try { EvidenceJson.AtomicWrite(Path.Combine(previous.Value.Directory, "coverage.json"), EvidenceJson.Encode(health[previous.Key])); }
                        catch (Exception error) { LastFailure = error.Message; lock (gate) health[previous.Key].failure = error.Message; }
                        sources.Remove(previous.Key);
                    }
                    sources.Add(key, source = new Source(directory));
                    string session = Directory.GetParent(Directory.GetParent(directory).FullName).FullName;
                    if (!File.Exists(Path.Combine(session, "manifest.json"))) EvidenceJson.AtomicWrite(Path.Combine(session, "manifest.json"),
                        EvidenceJson.Encode(new { schemaVersion = 1, runId = record.runId, round = record.round, capacityBytes = options.SessionBytes,
                            completeness = "Read each source coverage and this machine's replication watermarks; absent records are not proof of non-execution." }));
                }
                double now = Seconds;
                bool checkpoint = record.stage == "replay.checkpoint";
                // Begin a retained interval at its checkpoint. Old intervals may then be removed independently.
                if (source.Writer == null || source.Bytes >= options.SegmentBytes || now - source.Opened >= options.SegmentSeconds || checkpoint)
                    source.Rotate(now, record.recordSequence);
                Reserve(source.Directory, Math.Max(4096, record.estimatedBytes));
                if (record.input != null)
                {
                    string payload = EvidenceJson.EncodeBounded(record.input);
                    if (checkpoint || record.stage == "replay.engine_checkpoint" || Encoding.UTF8.GetByteCount(payload) > 4096)
                    {
                        byte[] raw = Encoding.UTF8.GetBytes(payload);
                        bool checkpointPayload = checkpoint || record.stage == "replay.engine_checkpoint";
                        string folder = checkpointPayload ? "checkpoints" : "inputs";
                        string relative = folder + "/" + EvidenceJson.Hash(raw) + ".json.gz";
                        Directory.CreateDirectory(Path.Combine(source.Directory, folder));
                        string path = Path.Combine(source.Directory, relative);
                        if (!File.Exists(path))
                        {
                            byte[] compressed = EvidenceJson.Compress(raw); Reserve(source.Directory, compressed.Length);
                            using var blob = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                            blob.Write(compressed, 0, compressed.Length); blob.Flush(true);
                        }
                        if (checkpointPayload) record.checkpointRef = relative; else record.inputRef = relative;
                        record.input = null;
                    }
                }
                string line = EvidenceJson.EncodeBounded(record);
                if (source.Bytes > 0 && source.Bytes + Encoding.UTF8.GetByteCount(line) + 1 > options.SegmentBytes)
                    source.Rotate(now, record.recordSequence);
                Reserve(source.Directory, Encoding.UTF8.GetByteCount(line) + 1);
                source.Writer.WriteLine(line); source.Bytes += Encoding.UTF8.GetByteCount(line) + 1;
                lock (gate) state.written = record.recordSequence;
            }
            catch (Exception error)
            {
                lock (gate) { state.failure = error.GetType().Name + ": " + error.Message; Gap(state, record.recordSequence, error is EvidenceCapacityException ? "CapacityExceeded" : "WriteFailure"); }
                LastFailure = state.failure;
                if (sources.TryGetValue(key, out var broken)) { broken.Dispose(); sources.Remove(key); }
            }
        }

        private static void Gap(EvidenceCoverage state, string sequence, string reason)
        {
            var last = state.gaps.Count > 0 ? state.gaps[state.gaps.Count - 1] : null;
            if (last != null && last.reason == reason && ulong.TryParse(last.last, out var previous) && ulong.TryParse(sequence, out var current) && current == previous + 1)
            { last.last = sequence; last.count++; }
            else if (state.gaps.Count < 128) state.gaps.Add(new EvidenceGap { first = sequence, last = sequence, reason = reason, count = 1 });
            else state.failure = "Gap list overflow; additional missing ranges unknown.";
        }

        private void Consume()
        {
            try { Directory.CreateDirectory(Root); RecoverExisting(); } catch (Exception error) { LastFailure = error.Message; }
            while (true)
            {
                (int bytes, Action action) work = default;
                lock (gate) { if (queue.Count != 0) work = queue.Dequeue(); else if (stopping) break; }
                if (work.action != null)
                {
                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    try { work.action(); } catch (Exception error) { LastFailure = error.GetType().Name + ": " + error.Message; }
                    finally { lock (gate) pendingBytes -= work.bytes; Interlocked.Add(ref writeTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started); }
                }
                else wake.WaitOne(50);
                if (Seconds >= nextFlush) { FlushSources(false); nextFlush = Seconds + 1; }
                if (Seconds >= nextPrune) { try { Prune(); } catch (Exception error) { LastFailure = error.Message; } nextPrune = Seconds + 5; }
            }
            FlushSources(true);
            foreach (var source in sources.Values) source.Dispose();
            Volatile.Write(ref closed, true);
        }

        private void FlushSources(bool complete)
        {
            string[] keys; lock (gate) keys = health.Keys.ToArray();
            foreach (string key in keys)
            {
                try
                {
                    bool active = sources.TryGetValue(key, out var source);
                    source?.Writer?.Flush(); source?.Stream?.Flush(true);
                    EvidenceCoverage snapshot;
                    lock (gate)
                    {
                        var state = health[key]; state.flushed = state.written; state.queuedBytes = pendingBytes;
                        if (active || complete)
                        {
                            state.complete = complete && state.failure == null && state.dropped == 0;
                            state.tailUnknown = !complete;
                        }
                        snapshot = EvidenceJson.Decode<EvidenceCoverage>(EvidenceJson.Encode(state));
                    }
                    string directory = Resolve(key); Directory.CreateDirectory(directory);
                    EvidenceJson.AtomicWrite(Path.Combine(directory, "coverage.json"), EvidenceJson.Encode(snapshot));
                }
                catch (Exception error) { LastFailure = error.Message; lock (gate) health[key].failure = error.Message; }
            }
        }
        private static double Seconds => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

        // Worker-only replication I/O. Only flushed bytes are advertised, and copies preserve their original path.
        public EvidenceFile[] Catalog(string run = null, string capture = null, string after = null, int maximum = 512)
        {
            FlushSources(false);
            if (!Directory.Exists(Root)) return Array.Empty<EvidenceFile>();
            // Bounded sorted page: enumerating a multi-GiB directory must not retain every filename in memory.
            var page = new SortedDictionary<string, EvidenceFile>(StringComparer.Ordinal);
            string scan = run == null ? Root : Resolve(run);
            if (!Directory.Exists(scan)) return Array.Empty<EvidenceFile>();
            foreach (string path in Directory.EnumerateFiles(scan, "*", SearchOption.AllDirectories))
            {
                string relative = path.Substring(Root.Length + 1).Replace('\\', '/');
                if (!relative.Contains("/sources/") || (capture != null && !relative.Contains("/sources/" + capture + "/")) ||
                    path.EndsWith(".tmp") || path.EndsWith(".partial") || path.EndsWith(".local.json") || (after != null && StringComparer.Ordinal.Compare(relative, after) <= 0)) continue;
                if (page.Count == maximum && StringComparer.Ordinal.Compare(relative, page.Last().Key) >= 0) continue;
                var info = new FileInfo(path);
                page[relative] = new EvidenceFile { path = relative, length = info.Length, revision = info.LastWriteTimeUtc.Ticks.ToString() };
                if (page.Count > maximum) page.Remove(page.Last().Key);
            }
            return page.Values.ToArray();
        }
        public byte[] ReadBlock(string relative, long offset, int count)
        {
            if (count < 0 || count > MaximumBlockBytes || offset < 0) throw new InvalidDataException("Invalid block range.");
            using var stream = new FileStream(Resolve(relative), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = offset; byte[] result = new byte[Math.Min(count, (int)Math.Min(int.MaxValue, Math.Max(0, stream.Length - offset)))];
            int read = 0; while (read < result.Length) { int n = stream.Read(result, read, result.Length - read); if (n == 0) break; read += n; }
            if (read != result.Length) Array.Resize(ref result, read); return result;
        }
        public bool WasPruned(string relative)
        {
            string path = Resolve(relative); string name = Path.GetFileName(path);
            if (!name.StartsWith("events-", StringComparison.Ordinal) || !name.EndsWith(".jsonl", StringComparison.Ordinal)) return false;
            if (!ulong.TryParse(name.Substring(7, name.Length - 13), out ulong sequence)) return false;
            foreach (string metadata in new[] { "retention.json", "retention.local.json" })
            {
                string retention = Path.Combine(Path.GetDirectoryName(path), metadata);
                if (!File.Exists(retention)) continue;
                var value = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(retention));
                if (ulong.TryParse((string)value["beforeSequence"], out ulong boundary) && sequence < boundary) return true;
            }
            return false;
        }
        public long StoredLength(string relative)
        { string path = Resolve(relative); if (File.Exists(path + ".partial")) path += ".partial"; return File.Exists(path) ? new FileInfo(path).Length : 0; }
        public void CompleteImport(string relative, long expectedLength)
        {
            string final = Resolve(relative), partial = final + ".partial";
            if (!File.Exists(partial) || new FileInfo(partial).Length != expectedLength) return;
            if (File.Exists(final)) File.Replace(partial, final, null); else File.Move(partial, final);
        }
        public long ImportBlock(string relative, long offset, byte[] bytes, string hash, bool replace, long expectedLength = -1)
        {
            if (bytes.Length > MaximumBlockBytes || EvidenceJson.Hash(bytes) != hash) throw new InvalidDataException("Evidence checksum mismatch.");
            string path = Resolve(relative);
            if (sources.Values.Any(s => path.StartsWith(s.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Cannot overwrite locally originated evidence.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Reserve(Path.GetDirectoryName(path), bytes.Length + 4096);
            if (replace)
            {
                if (offset != 0 || !relative.EndsWith(".json")) throw new InvalidDataException("Invalid metadata replacement.");
                EvidenceJson.AtomicWrite(path, Encoding.UTF8.GetString(bytes)); return bytes.Length;
            }
            string finalPath = path;
            if (expectedLength >= 0)
            {
                path += ".partial";
                if (!File.Exists(path) && File.Exists(finalPath)) { Reserve(Path.GetDirectoryName(path), new FileInfo(finalPath).Length); File.Copy(finalPath, path); }
            }
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            if (offset < stream.Length)
            {
                stream.Position = offset; int overlap = (int)Math.Min(bytes.Length, stream.Length - offset);
                for (int i = 0; i < overlap; i++) if (stream.ReadByte() != bytes[i]) throw new InvalidDataException("ConflictingImportedBytes");
                return stream.Length; // Lost acknowledgements do not create another execution record.
            }
            if (offset != stream.Length) return stream.Length;
            stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            long length = stream.Length;
            if (expectedLength >= 0 && length == expectedLength)
            { stream.Dispose(); if (File.Exists(finalPath)) File.Replace(path, finalPath, null); else File.Move(path, finalPath); }
            return length;
        }
        public void SaveReplication(object state) => EvidenceJson.AtomicWrite(Path.Combine(Root, "replication.json"), EvidenceJson.Encode(state));

        private void RecoverExisting()
        {
            foreach (string directory in Directory.EnumerateDirectories(Root, "sources", SearchOption.AllDirectories))
                foreach (string source in Directory.EnumerateDirectories(directory)) Recover(source);
        }
        private static void Recover(string directory)
        {
            foreach (string path in Directory.EnumerateFiles(directory, "events-*.jsonl"))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                if (stream.Length == 0) continue;
                stream.Position = stream.Length - 1; if (stream.ReadByte() == 10) continue;
                long end = stream.Length - 1;
                while (end >= 0) { stream.Position = end; if (stream.ReadByte() == 10) break; end--; }
                stream.SetLength(end + 1);
                EvidenceJson.AtomicWrite(Path.Combine(directory, "recovery.json"), EvidenceJson.Encode(new { tailUnknown = true, file = Path.GetFileName(path), retainedBytes = end + 1 }));
            }
        }
        private sealed class EvidenceCapacityException : IOException { public EvidenceCapacityException() : base("Evidence capacity reached; replay interval is incomplete.") { } }
        private string RunDirectory(string path)
        {
            string relative = Path.GetFullPath(path).Substring(Root.Length + 1);
            return Resolve(relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]);
        }
        private void Reserve(string path, long bytes)
        {
            string run = RunDirectory(path);
            if (!usage.TryGetValue(run, out long used)) usage[run] = used = Size(run);
            if (totalUsage < 0) totalUsage = Size(Root);
            long reserve = Math.Min(2 << 20, options.SessionBytes / 16);
            if (used + bytes + reserve > options.SessionBytes || totalUsage + bytes + reserve > options.TotalBytes)
            {
                Prune(bytes + reserve); used = Size(run); usage[run] = used; totalUsage = Size(Root);
                if (used + bytes + reserve > options.SessionBytes || totalUsage + bytes + reserve > options.TotalBytes) throw new EvidenceCapacityException();
            }
            usage[run] = used + bytes; totalUsage += bytes;
        }
        private void Prune(long needed = 0)
        {
            if (!Directory.Exists(Root)) return;
            foreach (var source in sources.Values) source.Writer?.Flush();
            var runs = Directory.GetDirectories(Root).Where(d => Directory.EnumerateDirectories(d, "sources", SearchOption.AllDirectories).Any()).ToArray();
            foreach (string run in runs)
            {
                if (Size(run) + needed <= options.SessionBytes) continue;
                foreach (string parent in Directory.EnumerateDirectories(run, "sources", SearchOption.AllDirectories))
                    foreach (string source in Directory.GetDirectories(parent))
                    {
                        var files = Directory.GetFiles(source, "events-*.jsonl").OrderBy(p => p, StringComparer.Ordinal).ToArray();
                        int boundary = -1;
                        for (int i = 0; i < files.Length; i++)
                            if (IsRetainableCheckpoint(files[i], source)) boundary = i;
                        if (boundary <= 0) continue;
                        var removed = new List<string>();
                        for (int i = 0; i < boundary; i++)
                        {
                            if (sources.Values.Any(s => s.Stream != null && s.Stream.Name == files[i])) continue;
                            File.Delete(files[i]); removed.Add(Path.GetFileName(files[i]));
                        }
                        // Content-addressed inputs are retained only while some retained record references them.
                        var referenced = new HashSet<string>(StringComparer.Ordinal);
                        foreach (string file in Directory.GetFiles(source, "events-*.jsonl"))
                            foreach (string line in ReadLinesShared(file))
                            {
                                try
                                {
                                    var record = EvidenceJson.Decode<DiagnosticRecord>(line);
                                    if (record.inputRef != null) referenced.Add(record.inputRef.Replace('\\', '/'));
                                    if (record.checkpointRef != null) referenced.Add(record.checkpointRef.Replace('\\', '/'));
                                }
                                catch { LastFailure = "Malformed record encountered during retention; evidence incomplete."; }
                            }
                        foreach (string folder in new[] { "inputs", "checkpoints" })
                            if (Directory.Exists(Path.Combine(source, folder)))
                                foreach (string blob in Directory.GetFiles(Path.Combine(source, folder), "*.json.gz"))
                                    if (!referenced.Contains(folder + "/" + Path.GetFileName(blob))) File.Delete(blob);
                        // Monotonic boundary is sufficient to describe every removed interval, including earlier cleanups.
                        bool local; lock (gate) local = health.ContainsKey(source.Substring(Root.Length + 1).Replace('\\', '/'));
                        if (removed.Count > 0) EvidenceJson.AtomicWrite(Path.Combine(source, local ? "retention.json" : "retention.local.json"), EvidenceJson.Encode(new {
                            reason = "CapacityRetention", firstRetainedFile = Path.GetFileName(files[boundary]),
                            beforeSequence = Path.GetFileNameWithoutExtension(files[boundary]).Substring(7),
                            interpretation = "All earlier records and unreferenced blobs may have been removed; do not infer non-execution." }));
                    }
            }
            long total = Size(Root);
            foreach (string run in runs.OrderBy(Directory.GetLastWriteTimeUtc))
            {
                if (total + needed <= options.TotalBytes) break;
                if (sources.Values.Any(s => s.Directory.StartsWith(run + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
                long bytes = Size(run);
                if (!Path.GetFullPath(run).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                string id = Path.GetFileName(run); Directory.Delete(run, true); total -= bytes;
                lock (gate)
                    foreach (string key in health.Keys.Where(k => k.StartsWith(id + "/", StringComparison.Ordinal)).ToArray()) health.Remove(key);
                File.AppendAllText(Path.Combine(Root, "retention.jsonl"), EvidenceJson.Encode(new { runId = id, reason = "GlobalCapacityRetention", utc = DateTime.UtcNow.ToString("o") }) + "\n");
            }
            usage.Clear(); totalUsage = Size(Root);
        }
        private static bool IsRetainableCheckpoint(string file, string source)
        {
            string line = ReadLinesShared(file).FirstOrDefault();
            if (line == null || !line.Contains("\"stage\":\"replay.checkpoint\"")) return false;
            try
            {
                var record = EvidenceJson.Decode<DiagnosticRecord>(line);
                if (record.checkpointRef == null) return false;
                string blob = Path.GetFullPath(Path.Combine(source, record.checkpointRef));
                if (!blob.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(blob)) return false;
                byte[] bytes = EvidenceJson.Decompress(File.ReadAllBytes(blob), 16 << 20);
                return Path.GetFileName(blob) == EvidenceJson.Hash(bytes) + ".json.gz";
            }
            catch { return false; }
        }
        private static IEnumerable<string> ReadLinesShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string line; while ((line = reader.ReadLine()) != null) yield return line;
        }
        private static long Size(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        public void Dispose() { lock (gate) stopping = true; wake.Set(); worker.Join(100); }
        public bool WaitForClose(int milliseconds = 5000) => worker.Join(milliseconds);

        private sealed class Source : IDisposable
        {
            public readonly string Directory;
            public FileStream Stream; public StreamWriter Writer;
            public long Bytes; public double Opened;
            public Source(string directory) { Directory = directory; }
            public void Rotate(double now, string sequence)
            {
                Dispose(); string name = "events-" + ulong.Parse(sequence).ToString("D20") + ".jsonl";
                Stream = new FileStream(Path.Combine(Directory, name), FileMode.Append, FileAccess.Write, FileShare.Read);
                Writer = new StreamWriter(Stream, new UTF8Encoding(false), 16 * 1024, true);
                Bytes = Stream.Length; Opened = now;
            }
            public void Dispose() { Writer?.Dispose(); Stream?.Dispose(); Writer = null; Stream = null; }
        }
    }
}
