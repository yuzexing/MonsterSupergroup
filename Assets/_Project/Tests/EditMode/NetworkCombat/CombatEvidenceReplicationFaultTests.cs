using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    // File/protocol correctness with a deterministic simulated Host and two Clients.
    // This does not exercise Steam sockets, game frames, real bandwidth, or capacity-pruning policy.
    public sealed class CombatEvidenceReplicationFaultTests
    {
        private string directory;
        [SetUp] public void SetUp() => Directory.CreateDirectory(directory = Path.Combine(Path.GetTempPath(), "combat-replication-fault-" + Guid.NewGuid().ToString("N")));
        [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Test, Timeout(20000)] public void ThreeSourcesAppendAndConvergeAfterDelayedReorderedDuplicatePacketsLostAcksAndReconnect()
        {
            using var rig = new Rig(directory); rig.hub.faults = true;
            for (int wave = 0; wave < 4; wave++)
            {
                if (wave == 1) rig.hub.clientTwoOffline = true;
                if (wave == 3) rig.hub.clientTwoOffline = false;
                rig.WriteWave();
                rig.Until(rig.OriginsFlushed, "Local records must flush even while a client is disconnected.");
                if (wave == 0)
                {
                    var firstFiles = rig.OriginFiles();
                    rig.Until(() => rig.AllCopiesMatch(firstFiles), "Initial copies must exist before the same files grow.");
                }
            }
            rig.hub.faults = false;
            var expected = rig.OriginFiles();
            rig.Until(() => rig.AllCopiesMatch(expected), "Every retained source file must converge after reconnection.");
            Assert.That(rig.hub.lostAcks, Is.GreaterThan(0));
            Assert.That(rig.hub.duplicates, Is.GreaterThan(0));
            Assert.That(rig.hub.reordered, Is.GreaterThan(0));
            Assert.That(rig.hub.delayed, Is.GreaterThan(0));
            foreach (var store in rig.stores)
            {
                var records = Directory.GetFiles(store.Root, "events-*.jsonl", SearchOption.AllDirectories)
                    .SelectMany(ReadLines).SelectMany(EvidenceBlocks.Decode).ToArray();
                Assert.That(records, Has.Length.EqualTo(24));
                Assert.That(records.Select(r => r.captureId + ":" + r.recordSequence).Distinct().Count(), Is.EqualTo(24));
                foreach (string capture in rig.captures)
                    Assert.That(records.Where(r => r.captureId == capture).Select(r => ulong.Parse(r.recordSequence)).OrderBy(s => s),
                        Is.EqualTo(Enumerable.Range(1, 8).Select(s => (ulong)s)));
                Assert.That(store.Dropped, Is.Zero);
            }
        }

        [Test, Timeout(20000)] public void IncompatiblePeerHasAnExplicitReplicationFailureWhileAllSourcesKeepWritingLocally()
        {
            using var rig = new Rig(directory); rig.hub.incompatiblePeer = 2;
            rig.WriteWave(); rig.Until(rig.OriginsFlushed, "First local wave did not flush.");
            rig.Until(() => rig.PeerMetadata(0, 2)?["status"]?.Value<string>() == "IncompatibleDiagnosticVersion", "Host must persist version incompatibility.");
            rig.WriteWave(); rig.Until(rig.OriginsFlushed, "Version mismatch must not stop subsequent local records.");
            var compatible = rig.OriginFiles().Where(f => !f.Key.Contains("/sources/" + rig.captures[2] + "/")).ToDictionary(p => p.Key, p => p.Value);
            rig.Until(() => rig.CopiesMatch(0, compatible) && rig.CopiesMatch(1, compatible), "Compatible peers must continue replicating.");
            Assert.That(rig.replication[0].LastFailure, Does.StartWith("IncompatibleDiagnosticVersion:"));
            Assert.That(rig.PeerMetadata(0, 2)["incompatibility"].Value<string>(), Does.Contain("replication=1"));
            Assert.That(rig.stores[0].Catalog(Rig.Run, rig.captures[2]), Is.Empty);
            foreach (var store in rig.stores) Assert.That(store.Dropped, Is.Zero);
            for (int peer = 0; peer < 3; peer++)
            {
                var coverage = rig.LocalCoverage(peer);
                Assert.That(coverage.flushed, Is.EqualTo("4")); Assert.That(coverage.gaps, Is.Empty);
            }
        }

        [Test, Timeout(20000)] public void ReceiverRetainedBoundaryIsReportedAsPrunedAndNeverAcknowledgedAsACompleteCopy()
        {
            using var rig = new Rig(directory);
            rig.WriteWave(); rig.Until(rig.OriginsFlushed, "Local fixture did not flush.");
            var expected = rig.OriginFiles(); rig.Until(() => rig.AllCopiesMatch(expected), "Initial copy did not converge.");
            string removed = expected.Keys.Single(p => p.Contains("/sources/" + rig.captures[0] + "/events-"));
            rig.hub.clientTwoOffline = true;
            // Fixture for a receiver that already deleted a prefix under its own capacity policy.
            // The local marker must remain local and cannot be replaced by a sender's coverage claims.
            rig.OnWriter(2, () => {
                string path = rig.stores[2].Resolve(removed);
                EvidenceJson.AtomicWrite(Path.Combine(Path.GetDirectoryName(path), "retention.local.json"),
                    "{\"reason\":\"CapacityRetention\",\"beforeSequence\":\"3\"}");
                File.Delete(path);
            });
            Assert.That(rig.stores[2].WasPruned(removed), Is.True);
            Assert.That(rig.stores[1].WasPruned(removed), Is.False);
            rig.RestartHostReplication(); rig.hub.clientTwoOffline = false;
            rig.Until(() => rig.PeerMetadata(0, 2)?["prunedFiles"]?[removed] != null, "Sender must retain an explicit pruned-file result.");
            var peer = rig.PeerMetadata(0, 2);
            Assert.That(peer["files"]?[removed], Is.Null, "Pruned bytes must not appear in completed-file watermarks.");
            Assert.That(File.Exists(rig.stores[2].Resolve(removed)), Is.False);
            Assert.That(HashFile(rig.stores[1].Resolve(removed)), Is.EqualTo(expected[removed]));
            Assert.That(rig.stores[2].Catalog(Rig.Run).Any(f => f.path.EndsWith(".local.json", StringComparison.Ordinal)), Is.False);
        }

        private static IEnumerable<string> ReadLines(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream); string line;
            while ((line = reader.ReadLine()) != null) yield return line;
        }
        private static string HashFile(string path)
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new MemoryStream(); input.CopyTo(output); return EvidenceJson.Hash(output.ToArray());
        }

        private sealed class Rig : IDisposable
        {
            public const string Run = "replication-fault";
            public readonly Hub hub = new();
            public readonly CombatEvidenceStore[] stores;
            public readonly DiagnosticReplicator[] replication;
            public readonly string[] captures = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
            private readonly Endpoint[] endpoints;
            private readonly int[] sequences = new int[3];
            private readonly Stopwatch clock = Stopwatch.StartNew();
            private double now;
            public Rig(string root)
            {
                stores = Enumerable.Range(0, 3).Select(i => new CombatEvidenceStore(Path.Combine(root, "peer" + i))).ToArray();
                endpoints = Enumerable.Range(0, 3).Select(i => new Endpoint(hub, i)).ToArray();
                replication = Enumerable.Range(0, 3).Select(i => new DiagnosticReplicator(stores[i], endpoints[i], captures[i])).ToArray();
            }
            public void WriteWave()
            {
                for (int peer = 0; peer < 3; peer++)
                    for (int item = 0; item < 2; item++)
                    {
                        int seq = ++sequences[peer]; byte[] data = new byte[seq == 1 ? 40000 : item == 0 ? 8192 : 512];
                        new Random(peer * 100 + seq).NextBytes(data); string payload = Convert.ToBase64String(data);
                        Assert.That(stores[peer].TryWrite(new DiagnosticRecord { schemaVersion = 2, captureId = captures[peer], runId = Run, round = 1,
                            recordSequence = seq.ToString(), role = "Fixture", stage = "fixture.append", input = payload,
                            estimatedBytes = 1024 + payload.Length * 2, critical = true }), Is.True);
                    }
            }
            public EvidenceCoverage LocalCoverage(int peer)
            {
                string path = stores[peer].Resolve(Run + "/1/sources/" + captures[peer] + "/coverage.json");
                try { return File.Exists(path) ? EvidenceJson.Decode<EvidenceCoverage>(File.ReadAllText(path)) : null; }
                catch (IOException) { return null; }
            }
            public bool OriginsFlushed() => Enumerable.Range(0, 3).All(i => LocalCoverage(i)?.flushed == sequences[i].ToString());
            public Dictionary<string, string> OriginFiles()
            {
                var files = new Dictionary<string, string>();
                for (int i = 0; i < 3; i++)
                    foreach (var file in stores[i].Catalog(Run, captures[i]).Where(f => f.path.EndsWith(".jsonl") || f.path.EndsWith(".json.gz")))
                        files.Add(file.path, HashFile(stores[i].Resolve(file.path)));
                Assert.That(files.Count, Is.GreaterThanOrEqualTo(6)); return files;
            }
            public bool AllCopiesMatch(Dictionary<string, string> files) => Enumerable.Range(0, 3).All(i => CopiesMatch(i, files));
            public bool CopiesMatch(int peer, Dictionary<string, string> files)
            {
                try { return files.All(f => File.Exists(stores[peer].Resolve(f.Key)) && HashFile(stores[peer].Resolve(f.Key)) == f.Value); }
                catch (IOException) { return false; }
            }
            public JObject PeerMetadata(int machine, int peer)
            {
                string path = Path.Combine(stores[machine].Root, "replication.json");
                try { return File.Exists(path) ? JObject.Parse(File.ReadAllText(path))["peers"]?.OfType<JObject>().FirstOrDefault(p => p["peer"].Value<int>() == peer) : null; }
                catch (IOException) { return null; }
            }
            public void Until(Func<bool> done, string message)
            {
                while (!done() && clock.Elapsed.TotalSeconds < 12)
                {
                    now += .1; foreach (var replicator in replication) replicator.Tick(now, Run);
                    hub.Deliver(); Thread.Sleep(3);
                }
                Assert.That(done(), Is.True, message + " failures=" + EvidenceJson.Encode(replication.Select(r => r.LastFailure).ToArray()));
            }
            public void OnWriter(int peer, Action action)
            {
                using var ready = new ManualResetEventSlim(); Exception error = null;
                Assert.That(stores[peer].Schedule(1024, () => { try { action(); } catch (Exception caught) { error = caught; } finally { ready.Set(); } }), Is.True);
                Assert.That(ready.Wait(2000), Is.True); Assert.That(error, Is.Null);
            }
            public void RestartHostReplication()
            {
                replication[0].Dispose(); Assert.That(replication[0].WaitForClose(2000), Is.True);
                hub.ClearPending(); replication[0] = new DiagnosticReplicator(stores[0], endpoints[0], captures[0]);
            }
            public void Dispose()
            {
                foreach (var replicator in replication) replicator.Dispose();
                foreach (var replicator in replication) replicator.WaitForClose(2000);
                foreach (var store in stores) store.Dispose();
                foreach (var store in stores) store.WaitForClose(2000);
            }
        }

        private sealed class Hub
        {
            private sealed class Delivery { public int from, to, due, order; public byte[] bytes; }
            public readonly Dictionary<int, Endpoint> peers = new();
            private readonly List<Delivery> pending = new();
            private int frame, serial;
            public bool faults, clientTwoOffline;
            public int incompatiblePeer = -1, lostAcks, duplicates, reordered, delayed;
            public bool Connected(int from, int to) => !clientTwoOffline || from != 2 && to != 2;
            public bool Send(int from, int to, byte[] bytes)
            {
                if (!Connected(from, to)) return false;
                var packet = EvidenceJson.Decode<DiagnosticReplicator.Packet>(Encoding.UTF8.GetString(bytes));
                int order = ++serial;
                if (from == incompatiblePeer) { packet.version = 1; bytes = Encoding.UTF8.GetBytes(EvidenceJson.Encode(packet)); }
                if (faults && packet.kind == "ack" && order % 3 == 0) { lostAcks++; return true; }
                int delay = faults ? 1 + order % 5 : 1;
                pending.Add(new Delivery { from = from, to = to, bytes = bytes, due = frame + delay, order = order });
                if (faults)
                {
                    delayed++;
                    if (order % 4 == 0) { pending.Add(new Delivery { from = from, to = to, bytes = bytes, due = frame + delay + 2, order = order }); duplicates++; }
                }
                return true;
            }
            public void Deliver()
            {
                frame++;
                var ready = pending.Where(p => p.due <= frame).OrderByDescending(p => p.order).ToArray();
                if (faults && ready.Length > 1) reordered++;
                foreach (var packet in ready)
                { pending.Remove(packet); if (Connected(packet.from, packet.to)) peers[packet.to].Receive(packet.from, packet.bytes); }
            }
            public void ClearPending() => pending.Clear();
        }
        private sealed class Endpoint : IDiagnosticReplicationTransport
        {
            private readonly Hub hub; private readonly int id;
            public Endpoint(Hub hub, int id) { this.hub = hub; this.id = id; hub.peers.Add(id, this); }
            public int[] Peers => (id == 0 ? new[] { 1, 2 } : new[] { 0 }).Where(peer => hub.Connected(id, peer)).ToArray();
            public bool IsHost => id == 0;
            public event Action<int, ArraySegment<byte>> Received;
            public bool Send(int peer, byte[] packet) => hub.Send(id, peer, packet);
            public void Receive(int peer, byte[] packet) => Received?.Invoke(peer, new ArraySegment<byte>(packet));
        }
    }
}
