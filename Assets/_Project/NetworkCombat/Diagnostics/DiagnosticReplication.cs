using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public interface IDiagnosticReplicationTransport
    {
        int[] Peers { get; }
        bool IsHost { get; }
        event Action<int, ArraySegment<byte>> Received;
        bool Send(int peer, byte[] packet);
    }

    /// <summary>File replication with bounded windows and durable acknowledgements. All file and codec work uses the disk worker.</summary>
    public sealed class DiagnosticReplicator : IDisposable
    {
        public const int Version = 1, SendBytesPerSecond = 2 << 20, InflightBytesPerPeer = 256 << 10;
        private const int BlockBytes = 32 << 10, MaximumPacketBytes = 96 << 10;
        [Serializable] public sealed class Packet
        {
            public int version = Version;
            public string kind, capture, run, path, revision, hash, failure;
            public long length, offset;
            public bool replace;
            public byte[] data;
        }
        private sealed class Transfer { public EvidenceFile file; public byte[] metadata; public Packet packet; public double sentAt; public long acknowledged; }
        private sealed class Peer
        {
            public int id; public string capture, run; public bool online;
            public readonly Dictionary<string, Transfer> sending = new();
            public readonly Dictionary<string, string> completed = new(), pruned = new();
            public readonly Dictionary<string, string> failures = new();
            public readonly Dictionary<string, Packet> receiving = new();
            public long sent, acknowledged; public double helloAt;
        }
        private readonly CombatEvidenceStore store;
        private readonly IDiagnosticReplicationTransport transport;
        private readonly string capture;
        private readonly Dictionary<int, Peer> peers = new(); // Disk thread only.
        private readonly ConcurrentQueue<(int peer, byte[] bytes)> outbound = new();
        private long outboundBytes, mainTicks, workerTicks;
        public double MainMilliseconds => System.Threading.Interlocked.Read(ref mainTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        public double WorkerMilliseconds => System.Threading.Interlocked.Read(ref workerTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        private EvidenceFile[] catalog = Array.Empty<EvidenceFile>();
        private string catalogCursor, catalogRun;
        private double catalogAt, watermarksAt, lastTick, budget = SendBytesPerSecond;
        private int serviceQueued;
        private bool disposed;
        public long SentBytes { get; private set; }
        public long RejectedPackets { get; private set; }
        public string LastFailure { get; private set; }

        public DiagnosticReplicator(CombatEvidenceStore store, IDiagnosticReplicationTransport transport, string capture)
        { this.store = store; this.transport = transport; this.capture = capture; transport.Received += Receive; }

        public void Tick(double now, string run)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { TickCore(now, run); }
            finally { System.Threading.Interlocked.Add(ref mainTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started); }
        }
        private void TickCore(double now, string run)
        {
            if (disposed) return;
            budget = Math.Min(SendBytesPerSecond, budget + Math.Max(0, now - lastTick) * SendBytesPerSecond); lastTick = now;
            while (outbound.TryPeek(out var item) && budget >= item.bytes.Length)
            {
                outbound.TryDequeue(out item); System.Threading.Interlocked.Add(ref outboundBytes, -item.bytes.Length);
                if (transport.Send(item.peer, item.bytes)) { budget -= item.bytes.Length; SentBytes += item.bytes.Length; }
            }
            if (System.Threading.Interlocked.CompareExchange(ref serviceQueued, 1, 0) != 0) return;
            var connected = transport.Peers; bool host = transport.IsHost;
            if (!store.Schedule(4096, () => { long started = System.Diagnostics.Stopwatch.GetTimestamp(); try { Service(now, run, connected, host); } catch (Exception e) { LastFailure = e.Message; }
                finally { System.Threading.Interlocked.Add(ref workerTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started); System.Threading.Volatile.Write(ref serviceQueued, 0); } })) System.Threading.Volatile.Write(ref serviceQueued, 0);
        }
        private void Receive(int peer, ArraySegment<byte> packet)
        {
            if (disposed || packet.Count > MaximumPacketBytes) { RejectedPackets++; return; }
            byte[] copy = new byte[packet.Count]; Buffer.BlockCopy(packet.Array, packet.Offset, copy, 0, copy.Length);
            bool host = transport.IsHost;
            if (!store.Schedule(copy.Length * 4 + 4096, () => {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                try { Handle(peer, EvidenceJson.Decode<Packet>(Encoding.UTF8.GetString(copy)), host); }
                catch (Exception error) { LastFailure = error.Message; RejectedPackets++; }
                finally { System.Threading.Interlocked.Add(ref workerTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started); }
            })) RejectedPackets++;
        }
        private Peer GetPeer(int id)
        {
            if (!peers.TryGetValue(id, out var peer))
            {
                // Retain a bounded offline summary rather than every connection ever opened.
                if (peers.Count >= 16) foreach (var key in peers.Where(p => !p.Value.online).Select(p => p.Key).Take(1).ToArray()) peers.Remove(key);
                if (peers.Count >= 16) throw new InvalidDataException("Diagnostic peer limit.");
                peers.Add(id, peer = new Peer { id = id });
            }
            return peer;
        }
        private void Service(double now, string run, int[] connected, bool host)
        {
            foreach (var peer in peers.Values) peer.online = false;
            foreach (int id in connected.Take(4)) GetPeer(id).online = true;
            if (catalogRun != run)
            {
                catalogRun = run; catalogCursor = null; catalog = Array.Empty<EvidenceFile>(); catalogAt = now;
                foreach (var peer in peers.Values)
                { peer.sending.Clear(); peer.receiving.Clear(); peer.completed.Clear(); peer.pruned.Clear(); peer.failures.Clear(); peer.helloAt = now; }
            }
            if (now >= catalogAt)
            {
                // Finish advertising this page before advancing so slow links cannot starve later files.
                bool exhausted = peers.Values.Where(p => p.online && p.capture != null).All(p => catalog.All(f =>
                    f.path.Contains("/sources/" + p.capture + "/") || p.completed.ContainsKey(f.path) || p.pruned.ContainsKey(f.path) || p.failures.ContainsKey(f.path) || p.sending.ContainsKey(f.path)));
                if (catalog.Length == 0 || exhausted)
                {
                    catalog = store.Catalog(run, host ? null : capture, catalogCursor);
                    if (catalog.Length == 0) { catalogCursor = null; catalog = store.Catalog(run, host ? null : capture); }
                    if (catalog.Length > 0) catalogCursor = catalog[catalog.Length - 1].path;
                    foreach (var peer in peers.Values) if (peer.completed.Count > 2048) peer.completed.Clear();
                }
                catalogAt = now + 2;
            }
            foreach (var peer in peers.Values.Where(p => p.online))
            {
                if (now >= peer.helloAt) { Queue(peer.id, new Packet { kind = "hello", capture = capture, run = run }); peer.helloAt = now + 2; }
                if (peer.capture == null || peer.run != run) continue;
                foreach (var transfer in peer.sending.Values.ToArray())
                    if (now - transfer.sentAt >= 3)
                    { Queue(peer.id, new Packet { kind = "offer", path = transfer.file.path, length = transfer.file.length,
                        revision = transfer.packet.revision, replace = transfer.metadata != null }); transfer.sentAt = now; }
                foreach (var file in catalog)
                {
                    if (peer.sending.Count >= 4) break;
                    if (file.path.Contains("/sources/" + peer.capture + "/") || peer.sending.ContainsKey(file.path)) continue;
                    string revision = file.length + ":" + file.revision;
                    if (peer.completed.TryGetValue(file.path, out var saved) && saved == revision || peer.pruned.TryGetValue(file.path, out var pruned) && pruned == revision) continue;
                    var offer = new Packet { kind = "offer", path = file.path, length = file.length, revision = revision,
                        replace = file.path.EndsWith(".json", StringComparison.Ordinal) };
                    if (offer.replace && file.length > CombatEvidenceStore.MaximumBlockBytes)
                    {
                        LastFailure = "ReplicationMetadataTooLarge:" + file.path;
                        if (peer.failures.Count >= 512) peer.failures.Clear();
                        peer.failures[file.path] = "MetadataTooLarge:" + revision; continue;
                    }
                    peer.failures.Remove(file.path);
                    byte[] metadata = offer.replace ? store.ReadBlock(file.path, 0, CombatEvidenceStore.MaximumBlockBytes) : null;
                    if (metadata != null) { offer.length = metadata.Length; file.length = metadata.Length; }
                    peer.sending.Add(file.path, new Transfer { file = file, metadata = metadata, packet = offer, sentAt = now }); Queue(peer.id, offer);
                }
                if (peer.completed.Count > 2048) peer.completed.Clear();
            }
            if (now >= watermarksAt)
            {
                store.SaveReplication(new { version = Version, captureId = capture, runId = run, utc = DateTime.UtcNow.ToString("o"),
                    failure = LastFailure, sentBytes = SentBytes, rejected = RejectedPackets,
                    peers = peers.Values.Select(p => new { peer = p.id, captureId = p.capture, p.online, p.run,
                        status = p.online ? "Connected" : "SourceOffline", p.sent, p.acknowledged,
                        files = p.completed, prunedFiles = p.pruned, failedFiles = p.failures, pending = p.sending.Select(t => new { path = t.Key, acknowledged = t.Value.acknowledged, length = t.Value.file.length }).ToArray() }).ToArray() });
                watermarksAt = now + 1;
            }
        }
        private void Handle(int id, Packet packet, bool host)
        {
            if (packet == null || packet.version != Version) throw new InvalidDataException("Unsupported replication version.");
            var peer = GetPeer(id);
            if (packet.kind == "hello")
            {
                if (!Guid.TryParseExact(packet.capture, "N", out _) || packet.capture == capture || packet.run == null || packet.run.Length > 100)
                    throw new InvalidDataException("Invalid diagnostic handshake.");
                if (peer.capture != packet.capture || peer.run != packet.run) { peer.sending.Clear(); peer.receiving.Clear(); peer.completed.Clear(); peer.pruned.Clear(); peer.failures.Clear(); }
                peer.capture = packet.capture; peer.run = packet.run; return;
            }
            if (peer.capture == null) return;
            if (packet.kind == "offer")
            {
                ValidatePath(packet.path, peer, host);
                if (store.WasPruned(packet.path))
                { Queue(id, new Packet { kind = "pruned", path = packet.path, revision = packet.revision, failure = "ReceiverCapacityRetention" }); return; }
                if (packet.length < 0 || packet.length > (8L << 30) || (packet.replace && packet.length > CombatEvidenceStore.MaximumBlockBytes))
                    throw new InvalidDataException("Invalid replicated file size.");
                if (!peer.receiving.ContainsKey(packet.path) && peer.receiving.Count >= 4) return;
                peer.receiving[packet.path] = packet;
                long length = store.StoredLength(packet.path);
                if (packet.replace) length = 0; // Metadata is small and atomically replaced, never appended.
                if (length > packet.length) throw new InvalidDataException("Source file regressed; evidence range needs repair.");
                if (!packet.replace && length == packet.length) store.CompleteImport(packet.path, length);
                Queue(id, new Packet { kind = "ack", path = packet.path, revision = packet.revision, offset = length });
                if (!packet.replace && length == packet.length) peer.receiving.Remove(packet.path);
                return;
            }
            if (packet.kind == "pruned")
            {
                if (peer.sending.TryGetValue(packet.path, out var removed) && removed.packet.revision == packet.revision)
                { peer.pruned[packet.path] = packet.revision; peer.sending.Remove(packet.path); if (peer.pruned.Count > 2048) peer.pruned.Clear(); }
                return;
            }
            if (packet.kind == "ack")
            {
                if (!peer.sending.TryGetValue(packet.path, out var transfer) || transfer.packet.revision != packet.revision) return;
                if (packet.offset < 0 || packet.offset > transfer.file.length) throw new InvalidDataException("Invalid acknowledgement.");
                peer.acknowledged += Math.Max(0, packet.offset - transfer.acknowledged); transfer.acknowledged = packet.offset;
                if (packet.offset == transfer.file.length)
                { peer.completed[packet.path] = packet.revision; peer.sending.Remove(packet.path); return; }
                bool replace = transfer.file.path.EndsWith(".json", StringComparison.Ordinal);
                byte[] bytes = transfer.metadata ?? store.ReadBlock(packet.path, packet.offset, (int)Math.Min(replace ? CombatEvidenceStore.MaximumBlockBytes : BlockBytes, transfer.file.length - packet.offset));
                var block = new Packet { kind = "data", path = packet.path, revision = packet.revision, offset = packet.offset,
                    length = transfer.file.length, replace = replace, hash = EvidenceJson.Hash(bytes), data = EvidenceJson.Compress(bytes) };
                transfer.packet = block; transfer.sentAt = lastTick; peer.sent += bytes.Length; Queue(id, block); return;
            }
            if (packet.kind == "data")
            {
                if (!peer.receiving.TryGetValue(packet.path, out var offer) || offer.revision != packet.revision) return;
                if (packet.data == null || packet.offset < 0 || packet.replace != offer.replace) throw new InvalidDataException("Invalid diagnostic block.");
                byte[] bytes = EvidenceJson.Decompress(packet.data, CombatEvidenceStore.MaximumBlockBytes);
                if (packet.offset + bytes.Length > offer.length) throw new InvalidDataException("Diagnostic block exceeds offered range.");
                long durable = store.ImportBlock(packet.path, packet.offset, bytes, packet.hash, packet.replace, offer.length);
                Queue(id, new Packet { kind = "ack", path = packet.path, revision = packet.revision, offset = durable });
                // Keep the completed offer until replaced so a lost final ack can be resent.
                if (durable == offer.length) peer.receiving.Remove(packet.path);
                return;
            }
            throw new InvalidDataException("Unknown diagnostic operation.");
        }
        private void ValidatePath(string path, Peer peer, bool host)
        {
            store.Resolve(path);
            if (!path.StartsWith(peer.run + "/", StringComparison.Ordinal) || path.Contains("/sources/" + capture + "/"))
                throw new InvalidDataException("Diagnostic origin mismatch.");
            var parts = path.Split('/');
            bool sourceFile = parts.Length >= 5 && parts[2] == "sources" && Guid.TryParseExact(parts[3], "N", out _);
            if (!sourceFile || (host && parts[3] != peer.capture)) throw new InvalidDataException("Unowned diagnostic source.");
            if (path.EndsWith(".local.json")) throw new InvalidDataException("Local retention is machine-owned.");
            if (!(path.EndsWith(".json") || path.EndsWith(".jsonl") || path.EndsWith(".json.gz"))) throw new InvalidDataException("Unsupported diagnostic file.");
        }
        private void Queue(int id, Packet packet)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(EvidenceJson.Encode(packet));
            if (bytes.Length > MaximumPacketBytes || System.Threading.Interlocked.Read(ref outboundBytes) + bytes.Length > (2 << 20))
            { LastFailure = "ReplicationQueueOverload"; return; }
            System.Threading.Interlocked.Add(ref outboundBytes, bytes.Length); outbound.Enqueue((id, bytes));
        }
        public void Dispose() { disposed = true; transport.Received -= Receive; }
    }
}
