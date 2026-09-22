using System;
using System.IO;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatEvidenceReplicationV2Tests
    {
        [Test] public void IncompatiblePeerIsExplainedWhileLocalWriterAndReplicationHaveIndependentQueues()
        {
            string directory = Path.Combine(Path.GetTempPath(), "evidence-v2-" + Guid.NewGuid().ToString("N"));
            var store = new CombatEvidenceStore(directory); var transport = new Transport();
            var replicator = new DiagnosticReplicator(store, transport, Guid.NewGuid().ToString("N"));
            using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            try
            {
                Assert.That(store.Schedule(1024, () => { held.Set(); release.Wait(5000); }), Is.True);
                Assert.That(held.Wait(5000), Is.True);
                transport.Deliver(new DiagnosticReplicator.Packet { version = 1, kind = "hello", capture = Guid.NewGuid().ToString("N"), run = "test" });
                replicator.Tick(1, "test");
                string watermarks = Path.Combine(directory, "replication.json");
                Assert.That(SpinWait.SpinUntil(() => File.Exists(watermarks), 3000), Is.True, "Replication must run while the capture queue is held.");
                var state = JObject.Parse(File.ReadAllText(watermarks));
                Assert.That((string)state["peers"][0]["status"], Is.EqualTo("IncompatibleDiagnosticVersion"));
                Assert.That(store.TryWrite(new DiagnosticRecord { captureId = "local", runId = "test", round = 1,
                    recordSequence = "1", stage = "local.input", critical = true }), Is.True);
                release.Set(); replicator.Dispose(); Assert.That(replicator.WaitForClose(), Is.True);
                store.Dispose(); Assert.That(store.WaitForClose(), Is.True);
                Assert.That(Directory.GetFiles(directory, "events-*.jsonl", SearchOption.AllDirectories).Length, Is.EqualTo(1));
                Assert.That(store.Memory.Peak, Is.LessThanOrEqualTo(128L << 20)); Assert.That(store.Memory.Used, Is.Zero);
            }
            finally
            {
                release.Set(); replicator.Dispose(); replicator.WaitForClose(); store.Dispose(); store.WaitForClose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
        private sealed class Transport : IDiagnosticReplicationTransport
        {
            public int[] Peers => new[] { 1 };
            public bool IsHost => true;
            public event Action<int, ArraySegment<byte>> Received;
            public bool Send(int peer, byte[] packet) => true;
            public void Deliver(DiagnosticReplicator.Packet packet) => Received?.Invoke(1, new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(EvidenceJson.Encode(packet))));
        }
    }
}
