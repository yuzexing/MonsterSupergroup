using System;
using Mirror;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public sealed class SteamDiagnosticTransport : IDiagnosticReplicationTransport, IDisposable
    {
        public bool IsHost => NetworkServer.active;
        public event Action<int, ArraySegment<byte>> Received;
#if !DISABLESTEAMWORKS
        public SteamDiagnosticTransport() { Mirror.FizzySteam.SteamEvidenceLane.Received += Receive; }
        public int[] Peers => Mirror.FizzySteam.SteamEvidenceLane.Peers;
        public object CaptureDiagnosticState() => new {
            supported = true, peers = Peers, lane = 1,
            sentBytes = Mirror.FizzySteam.SteamEvidenceLane.SentBytes,
            receivedBytes = Mirror.FizzySteam.SteamEvidenceLane.ReceivedBytes,
            setupFailures = Mirror.FizzySteam.SteamEvidenceLane.SetupFailures,
            sendFailures = Mirror.FizzySteam.SteamEvidenceLane.SendFailures,
            backpressure = Mirror.FizzySteam.SteamEvidenceLane.BackpressureCount,
            failure = Mirror.FizzySteam.SteamEvidenceLane.LastFailure };
        public bool Send(int peer, byte[] packet) => Mirror.FizzySteam.SteamEvidenceLane.Send(peer, packet);
        private void Receive(int peer, ArraySegment<byte> packet)
        {
            if (IsHost && (!NetworkServer.connections.TryGetValue(peer, out var connection) || !connection.isAuthenticated)) return;
            Received?.Invoke(peer, packet);
        }
        public void Dispose() { Mirror.FizzySteam.SteamEvidenceLane.Received -= Receive; }
#else
        public int[] Peers => Array.Empty<int>();
        public object CaptureDiagnosticState() => new { supported = false, reason = "SteamworksDisabled" };
        public bool Send(int peer, byte[] packet) => false;
        public void Dispose() { }
#endif
    }
}
