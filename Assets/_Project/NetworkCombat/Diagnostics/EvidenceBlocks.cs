using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MonsterSupergroup.GAS;
using Newtonsoft.Json;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    /// <summary>Independently checksummed JSONL blocks. No dictionary state crosses a disk/retention boundary.</summary>
    public static class EvidenceBlocks
    {
        public const int Format = 2, TargetBytes = 64 << 10, MaximumDecodedBytes = 1 << 20;
        // Raw <= 1 MiB, gzip capacity <= 2 MiB, header chars <= 2 MiB, plus bounded dictionaries/indices.
        internal const int MaximumRetainedWorkspaceBytes = 8 << 20;
        [Serializable] private sealed class Block
        {
            public int schemaVersion = Format, count;
            public string encoding = "gzip-jsonl-v1", first, last, hash, data;
            public string captureId, runId;
            public uint round;
        }
        // One bounded scratch workspace per storage worker, covered by the store's codec reservation.
        // Dictionaries are rebuilt and cleared for every block; no decoding dependency crosses a block boundary.
        private sealed class EncoderWorkspace
        {
            public readonly List<byte[][]> engines = new List<byte[][]>(128);
            public readonly Dictionary<(string role, string engine, string operation), int> engineIndex = new Dictionary<(string, string, string), int>(128);
            public readonly List<StatusReplayBoundary> boundaries = new List<StatusReplayBoundary>(128);
            public readonly Dictionary<(ushort slot, ushort epoch, uint next, int flags, uint player, uint owner), int> boundaryIndex = new Dictionary<(ushort, ushort, uint, int, uint, uint), int>(128);
            public readonly ushort[] engineRows = new ushort[128];
            public readonly short[] boundaryRows = new short[128];
            public readonly MemoryStream raw = new MemoryStream(TargetBytes), compressed = new MemoryStream(TargetBytes);
            public readonly BinaryWriter writer;
            public readonly StringBuilder header = new StringBuilder(4096);
            public bool busy;
            public EncoderWorkspace() { writer = new BinaryWriter(raw, Encoding.UTF8, true); }
            public void Clear()
            {
                engines.Clear(); engineIndex.Clear(); boundaries.Clear(); boundaryIndex.Clear();
                raw.SetLength(0); raw.Position = 0; compressed.SetLength(0); compressed.Position = 0; header.Clear();
                if (raw.Capacity > MaximumDecodedBytes) raw.Capacity = TargetBytes;
                if (compressed.Capacity > MaximumDecodedBytes * 2) compressed.Capacity = TargetBytes;
                if (header.Capacity > MaximumDecodedBytes * 2) header.Capacity = 4096;
                busy = false;
            }
        }
        [ThreadStatic] private static EncoderWorkspace cachedWorkspace;
        private static EncoderWorkspace RentWorkspace()
        {
            var value = cachedWorkspace ?? (cachedWorkspace = new EncoderWorkspace());
            if (value.busy) value = new EncoderWorkspace();
            value.busy = true; return value;
        }
        public static string EncodeAdvances(string capture, string run, uint round, DiagnosticAdvance[] entries, int count, EvidenceStageMetrics metrics = null)
        {
            long started = EvidenceStageMetrics.Now, compressedBefore = metrics?.CompressionTicks ?? 0;
            if (entries == null || count < 1 || count > 128 || count > entries.Length) throw new InvalidDataException("Invalid advance count.");
            var workspace = RentWorkspace();
            try
            {
            var engines = workspace.engines; var engineIndex = workspace.engineIndex;
            var boundaries = workspace.boundaries; var boundaryIndex = workspace.boundaryIndex;
            var engineRows = workspace.engineRows; var boundaryRows = workspace.boundaryRows;
            int rawLength = 10 + count * 49;
            for (int i = 0; i < count; i++)
            {
                var e = entries[i]; var key = (e.role, e.engine, e.operation);
                if ((e.role?.Length ?? 0) > 256 || (e.engine?.Length ?? 0) > 256 || (e.operation?.Length ?? 0) > 256) throw new InvalidDataException("AdvanceIdentityLimit");
                if (!engineIndex.TryGetValue(key, out int engine))
                {
                    engineIndex[key] = engine = engines.Count;
                    var identity = new[] { e.role == null ? null : Encoding.UTF8.GetBytes(e.role),
                        e.engine == null ? null : Encoding.UTF8.GetBytes(e.engine), e.operation == null ? null : Encoding.UTF8.GetBytes(e.operation) };
                    engines.Add(identity);
                    foreach (var text in identity) rawLength += 2 + (text?.Length ?? 0);
                }
                int boundary = -1;
                if (e.boundary != null)
                {
                    var b = e.boundary;
                    int flags = (b.eventIds ? 1 : 0) | (b.supported ? 2 : 0) | (b.executeAll ? 4 : 0) | (b.offline ? 8 : 0) | (b.server ? 16 : 0) | (b.ids != null ? 32 : 0);
                    var state = (b.ids?.slot ?? 0, b.ids?.epoch ?? 0, b.ids?.next ?? 0, flags, b.localPlayer, b.targetOwner);
                    if (!boundaryIndex.TryGetValue(state, out boundary))
                    {
                        boundaryIndex[state] = boundary = boundaries.Count; boundaries.Add(e.boundary);
                        rawLength += b.ids == null ? 9 : 17;
                    }
                }
                engineRows[i] = (ushort)engine; boundaryRows[i] = (short)boundary;
            }
            if (rawLength > MaximumDecodedBytes) throw new InvalidDataException("AdvanceBinarySizeLimit");
            var output = workspace.raw; var writer = workspace.writer;
            {
                writer.Write(0x32445641u); writer.Write((ushort)count); writer.Write((ushort)engines.Count); writer.Write((ushort)boundaries.Count);
                foreach (var engine in engines) foreach (var text in engine)
                {
                    if (text == null) writer.Write(ushort.MaxValue);
                    else { writer.Write((ushort)text.Length); writer.Write(text); }
                }
                foreach (var b in boundaries)
                {
                    writer.Write((byte)((b.eventIds ? 1 : 0) | (b.supported ? 2 : 0) | (b.executeAll ? 4 : 0) | (b.offline ? 8 : 0) | (b.server ? 16 : 0) | (b.ids != null ? 32 : 0)));
                    if (b.ids != null) { writer.Write(b.ids.slot); writer.Write(b.ids.epoch); writer.Write(b.ids.next); }
                    writer.Write(b.localPlayer); writer.Write(b.targetOwner);
                }
                for (int i = 0; i < count; i++)
                {
                    var e = entries[i]; writer.Write(e.sequence); writer.Write(e.utcTicks); writer.Write(e.monotonic); writer.Write(e.network);
                    writer.Write(e.frame); writer.Write(e.fixedStep); writer.Write((byte)e.phase); writer.Write(e.delta);
                    writer.Write(engineRows[i]); writer.Write(boundaryRows[i]);
                }
            }
            if (output.Position != rawLength) throw new InvalidDataException("AdvanceBinarySizeMismatch");
            metrics?.BinaryBuild(started);
            string encoded = EncodeBlock(output.GetBuffer(), rawLength, "advance-binary-v1", entries[0].sequence.ToString(CultureInfo.InvariantCulture),
                entries[count - 1].sequence.ToString(CultureInfo.InvariantCulture), count, capture, run, round, workspace, metrics);
            metrics?.Encoding(started, compressedBefore);
            return encoded;
            }
            finally { workspace.Clear(); }
        }
        private static string EncodeBlock(byte[] raw, int rawLength, string encoding, string first, string last, int count,
            string capture, string run, uint round, EncoderWorkspace workspace, EvidenceStageMetrics metrics)
        {
            if ((first?.Length ?? 0) > 4096 || (last?.Length ?? 0) > 4096 ||
                (capture?.Length ?? 0) > 4096 || (run?.Length ?? 0) > 4096) throw new InvalidDataException("EvidenceBlockIdentityLimit");
            long hashStarted = EvidenceStageMetrics.Now;
            string hash = EvidenceJson.Hash(raw, rawLength);
            metrics?.BlockHash(hashStarted);
            var compressed = workspace.compressed;
            EvidenceJson.CompressInto(raw, rawLength, compressed, metrics);
            long headerStarted = EvidenceStageMetrics.Now;
            // The fixed envelope has no arbitrary payloads or converters. Keep its v2 field order without per-block reflection/DTOs.
            using var text = new StringWriter(workspace.header, CultureInfo.InvariantCulture);
            using (var writer = new JsonTextWriter(text))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("schemaVersion"); writer.WriteValue(Format);
                writer.WritePropertyName("count"); writer.WriteValue(count);
                writer.WritePropertyName("encoding"); writer.WriteValue(encoding);
                if (first != null) { writer.WritePropertyName("first"); writer.WriteValue(first); }
                if (last != null) { writer.WritePropertyName("last"); writer.WriteValue(last); }
                writer.WritePropertyName("hash"); writer.WriteValue(hash);
                writer.WritePropertyName("data"); writer.WriteValue(Convert.ToBase64String(compressed.GetBuffer(), 0, (int)compressed.Length));
                if (capture != null) { writer.WritePropertyName("captureId"); writer.WriteValue(capture); }
                if (run != null) { writer.WritePropertyName("runId"); writer.WriteValue(run); }
                writer.WritePropertyName("round"); writer.WriteValue(round); writer.WriteEndObject();
            }
            string encoded = text.ToString();
            metrics?.BlockHeader(headerStarted);
            return encoded;
        }
        public static string Encode(byte[] utf8, string first, string last, int count, EvidenceStageMetrics metrics = null)
        {
            long started = EvidenceStageMetrics.Now, compressedBefore = metrics?.CompressionTicks ?? 0;
            if (utf8 == null || utf8.Length > MaximumDecodedBytes) throw new InvalidDataException("EvidenceBlockSizeLimit");
            var workspace = RentWorkspace();
            try
            {
            string encoded = EncodeBlock(utf8, utf8.Length, "gzip-jsonl-v1", first, last, count, null, null, 0, workspace, metrics);
            metrics?.Encoding(started, compressedBefore);
            return encoded;
            }
            finally { workspace.Clear(); }
        }

        public static IEnumerable<DiagnosticRecord> Decode(string line)
        {
            // v1 and low-volume v2 records (including checkpoints) remain directly readable.
            var header = Newtonsoft.Json.Linq.JObject.Parse(line);
            if (header["encoding"] == null)
            {
                int version = (int?)header["schemaVersion"] ?? 1;
                if (version != 1 && version != 2) throw new InvalidDataException("Unsupported evidence schema version.");
                yield return EvidenceJson.Decode<DiagnosticRecord>(line); yield break;
            }
            var block = EvidenceJson.Decode<Block>(line);
            if ((int?)header["schemaVersion"] != Format || block.count <= 0 || block.count > 65536) throw new InvalidDataException("Invalid evidence block header.");
            byte[] raw = EvidenceJson.Decompress(Convert.FromBase64String(block.data), MaximumDecodedBytes);
            if (EvidenceJson.Hash(raw) != block.hash) throw new InvalidDataException("Evidence block checksum mismatch.");
            var records = new List<DiagnosticRecord>();
            if (block.encoding == "advance-binary-v1")
            {
                using var input = new MemoryStream(raw, false); using var reader = new BinaryReader(input, Encoding.UTF8, true);
                if (reader.ReadUInt32() != 0x32445641u) throw new InvalidDataException("Invalid advance binary header.");
                int count = reader.ReadUInt16(), engineCount = reader.ReadUInt16(), boundaryCount = reader.ReadUInt16();
                if (count != block.count || count > 128 || engineCount > 128 || boundaryCount > 128) throw new InvalidDataException("Invalid advance binary count.");
                var engines = new string[engineCount][];
                for (int i = 0; i < engines.Length; i++)
                {
                    engines[i] = new string[3];
                    for (int j = 0; j < 3; j++)
                    {
                        int length = reader.ReadUInt16(); if (length == ushort.MaxValue) continue;
                        if (length > 1024 || length > input.Length - input.Position) throw new InvalidDataException("Invalid advance string length.");
                        engines[i][j] = new UTF8Encoding(false, true).GetString(reader.ReadBytes(length));
                    }
                }
                var boundaries = new StatusReplayBoundary[boundaryCount];
                for (int i = 0; i < boundaries.Length; i++)
                {
                    byte flags = reader.ReadByte(); if (flags > 63) throw new InvalidDataException("Invalid boundary flags.");
                    boundaries[i] = new StatusReplayBoundary { eventIds = (flags & 1) != 0, supported = (flags & 2) != 0,
                        executeAll = (flags & 4) != 0, offline = (flags & 8) != 0, server = (flags & 16) != 0,
                        ids = (flags & 32) == 0 ? null : new EventSequenceState { slot = reader.ReadUInt16(), epoch = reader.ReadUInt16(), next = reader.ReadUInt32() },
                        localPlayer = reader.ReadUInt32(), targetOwner = reader.ReadUInt32() };
                }
                for (int i = 0; i < count; i++)
                {
                    var e = new DiagnosticAdvance { sequence = reader.ReadUInt64(), utcTicks = reader.ReadInt64(), monotonic = reader.ReadDouble(), network = reader.ReadDouble(),
                        frame = reader.ReadInt32(), fixedStep = reader.ReadInt32(), phase = reader.ReadByte(), delta = reader.ReadSingle() };
                    int engine = reader.ReadUInt16(), boundary = reader.ReadInt16();
                    if (engine >= engineCount || boundary < -1 || boundary >= boundaryCount || e.phase > 2 || e.phase != 0 && boundary != -1)
                        throw new InvalidDataException("Invalid advance binary row.");
                    e.role = engines[engine][0]; e.engine = engines[engine][1]; e.operation = engines[engine][2]; e.boundary = boundary < 0 ? null : boundaries[boundary];
                    records.Add(e.Expand(block.captureId, block.runId, block.round));
                }
                if (input.Position != input.Length) throw new InvalidDataException("Advance binary trailing bytes.");
            }
            else if (block.encoding == "advance-columns-v1")
            {
                var data = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(raw));
                var engines = data["engines"].ToObject<string[][]>();
                var boundaries = data["boundaries"].ToObject<StatusReplayBoundary[]>(JsonSerializer.Create(EvidenceJson.Settings));
                var rows = (Newtonsoft.Json.Linq.JArray)data["rows"];
                if (rows.Count != block.count || rows.Count > 128 || engines.Length > 128 || boundaries.Length > 128) throw new InvalidDataException("Invalid advance block size.");
                foreach (Newtonsoft.Json.Linq.JArray row in rows)
                {
                    if (row.Count != 10) throw new InvalidDataException("Invalid advance row.");
                    int engine = (int)row[8], boundary = (int)row[9], phase = (int)row[6];
                    if (engine < 0 || engine >= engines.Length || engines[engine].Length != 3 || boundary < -1 || boundary >= boundaries.Length || phase < 0 || phase > 2)
                        throw new InvalidDataException("Invalid advance dictionary index or phase.");
                    if (phase != 0 && boundary != -1) throw new InvalidDataException("Completion cannot carry an input boundary.");
                    var e = new DiagnosticAdvance { sequence = ulong.Parse((string)row[0]), utcTicks = long.Parse((string)row[1]),
                        monotonic = (double)row[2], network = (double)row[3], frame = (int)row[4], fixedStep = (int)row[5], phase = phase, delta = (float)row[7],
                        role = engines[engine][0], engine = engines[engine][1], operation = engines[engine][2], boundary = boundary < 0 ? null : boundaries[boundary] };
                    records.Add(e.Expand(block.captureId, block.runId, block.round));
                }
            }
            else if (block.encoding == "gzip-jsonl-v1")
            {
            if (raw.Length == 0 || raw[raw.Length - 1] != 10) throw new InvalidDataException("Truncated evidence block tail.");
            using var reader = new StringReader(Encoding.UTF8.GetString(raw));
            string item;
            while ((item = reader.ReadLine()) != null)
            {
                var record = EvidenceJson.Decode<DiagnosticRecord>(item);
                if (record.schemaVersion != 1 && record.schemaVersion != 2) throw new InvalidDataException("Unsupported inner record schema version.");
                if (record.completion != null)
                {
                    var completion = record.completion; record.completion = null; record.stage = "replay.input";
                    records.Add(record);
                    var end = record.Copy(); end.stage = "replay.output"; end.input = null; end.before = null; end.inputRef = null;
                    end.recordSequence = completion.sequence; end.utc = completion.utc; end.monotonicTime = completion.monotonic;
                    end.networkTime = completion.network; end.frame = completion.frame; end.fixedStep = completion.fixedStep;
                    end.outcome = "Completed"; end.critical = false; end.estimatedBytes = completion.estimatedBytes;
                    records.Add(end);
                }
                else records.Add(record);
            }
            }
            else throw new InvalidDataException("Unsupported evidence block encoding.");
            if (records.Count != block.count || records[0].recordSequence != block.first || records[records.Count - 1].recordSequence != block.last)
                throw new InvalidDataException("Evidence block range mismatch.");
            ulong previous = 0;
            foreach (var record in records)
            {
                if (!ulong.TryParse(record.recordSequence, out ulong sequence) || sequence <= previous) throw new InvalidDataException("Unordered evidence block sequence.");
                previous = sequence;
            }
            foreach (var record in records) yield return record;
        }
    }

    /// <summary>Budget covers retained payloads, encoded blocks, I/O workspaces and replication buffers.</summary>
    public sealed class DiagnosticMemoryBudget
    {
        private readonly object gate = new();
        public long Limit { get; }
        private long used, peak;
        public long Used { get { lock (gate) return used; } }
        public long Peak { get { lock (gate) return peak; } }
        public DiagnosticMemoryBudget(long limit = 128L << 20) { Limit = limit; }
        public bool TryReserve(long bytes)
        {
            lock (gate) { if (bytes < 0 || bytes > Limit - used) return false; used += bytes; peak = Math.Max(peak, used); return true; }
        }
        public void Release(long bytes) { lock (gate) { if (bytes < 0 || bytes > used) throw new InvalidOperationException("Unbalanced diagnostic budget."); used -= bytes; } }
    }

    public interface IEvidenceStorage
    {
        Stream OpenAppend(string path);
        void Flush(Stream stream, bool durable);
    }
    public sealed class FileEvidenceStorage : IEvidenceStorage
    {
        public Stream OpenAppend(string path) => new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 64 << 10);
        public void Flush(Stream stream, bool durable) { if (stream is FileStream file) file.Flush(durable); else stream.Flush(); }
    }
}
