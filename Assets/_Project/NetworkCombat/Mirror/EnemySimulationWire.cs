using System;
using System.Collections.Generic;
using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    internal static class EnemySimulationWire
    {
        // Checkpoints are larger than the original pose-only datagrams. Never exceed the transport MTU.
        internal static void SendBatches(List<EnemySimulationSnapshot> source, int maximumCount,
            Action<EnemySimulationSnapshotBatch, bool> send)
        {
            int budget = Math.Max(128, Math.Min(1000, Transport.active.GetMaxPacketSize(Channels.Unreliable) - 100));
            var packet = new List<EnemySimulationSnapshot>();
            int bytes = 20;
            foreach (var snapshot in source)
            {
                int size;
                using (var writer = NetworkWriterPool.Get()) { writer.Write(snapshot); size = writer.Position; }
                NetworkDiagnosticsObservation.RecordSnapshot(size);
                if (packet.Count > 0 && (bytes + size > budget || packet.Count >= maximumCount))
                {
                    send(new EnemySimulationSnapshotBatch { Snapshots = packet.ToArray() }, false);
                    packet.Clear(); bytes = 20;
                }
                if (size + 20 > budget)
                { NetworkDiagnosticsObservation.RecordReliableSnapshot(); send(new EnemySimulationSnapshotBatch { Snapshots = new[] { snapshot } }, true); continue; }
                packet.Add(snapshot); bytes += size;
            }
            if (packet.Count > 0) send(new EnemySimulationSnapshotBatch { Snapshots = packet.ToArray() }, false);
        }
    }
}
