#if !DISABLESTEAMWORKS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;

namespace Mirror.FizzySteam
{
    /// <summary>Independent reliable ordering lane. Lower priority than combat; no Mirror batching.</summary>
    public static class SteamEvidenceLane
    {
        private static readonly Dictionary<int, HSteamNetConnection> connections = new();
        private static readonly IntPtr[] message = new IntPtr[1];
        private static readonly long[] result = new long[1];
        public static event Action<int, ArraySegment<byte>> Received;
        public static int[] Peers { get { var ids = new int[connections.Count]; connections.Keys.CopyTo(ids, 0); return ids; } }
        public static long SentBytes, ReceivedBytes, SendFailures, SetupFailures, BackpressureCount;
        public static string LastFailure;
        public static void Connect(int id, HSteamNetConnection connection)
        {
#if UNITY_SERVER
            var status = SteamGameServerNetworkingSockets.ConfigureConnectionLanes(connection, 2, new[] { 0, 10 }, new ushort[] { 1, 1 });
#else
            var status = SteamNetworkingSockets.ConfigureConnectionLanes(connection, 2, new[] { 0, 10 }, new ushort[] { 1, 1 });
#endif
            if (status == EResult.k_EResultOK) connections[id] = connection;
            else { SetupFailures++; LastFailure = "ConfigureConnectionLanes:" + status; }
        }
        public static void Disconnect(int id) => connections.Remove(id);
        public static bool Send(int id, byte[] bytes)
        {
            if (bytes == null || bytes.Length > 96 * 1024 || !connections.TryGetValue(id, out var connection)) return false;
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lane = new SteamNetConnectionRealTimeLaneStatus_t();
#if UNITY_SERVER
            var sampled = SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lane);
#else
            var sampled = SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lane);
#endif
            // Counting all reliable bytes is conservative: combat congestion also postpones diagnostics.
            if (sampled != EResult.k_EResultOK || (long)status.m_cbPendingReliable + status.m_cbSentUnackedReliable + bytes.Length > 256 * 1024)
            { BackpressureCount++; if (sampled != EResult.k_EResultOK) LastFailure = "GetConnectionRealTimeStatus:" + sampled; return false; }
#if UNITY_SERVER
            var pointer = SteamGameServerNetworkingUtils.AllocateMessage(bytes.Length);
#else
            var pointer = SteamNetworkingUtils.AllocateMessage(bytes.Length);
#endif
            if (pointer == IntPtr.Zero) { SendFailures++; LastFailure = "AllocateMessageFailed"; return false; }
            var data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pointer);
            Marshal.Copy(bytes, 0, data.m_pData, bytes.Length);
            data.m_conn = connection; data.m_idxLane = 1; data.m_nFlags = Constants.k_nSteamNetworkingSend_Reliable;
            Marshal.StructureToPtr(data, pointer, false); message[0] = pointer;
#if UNITY_SERVER
            SteamGameServerNetworkingSockets.SendMessages(1, message, result);
#else
            SteamNetworkingSockets.SendMessages(1, message, result);
#endif
            // Steam owns and releases the allocation even if sending fails.
            bool success = result[0] > 0;
            if (success) SentBytes += bytes.Length; else { SendFailures++; LastFailure = "SendMessages:" + result[0]; }
            return success;
        }
        public static void Deliver(int peer, ArraySegment<byte> bytes)
        { ReceivedBytes += bytes.Count; Received?.Invoke(peer, bytes); }
    }
}
#endif
