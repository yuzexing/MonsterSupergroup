#if !DISABLESTEAMWORKS
using System;
using Steamworks;

namespace Mirror.FizzySteam
{
    [Serializable]
    public struct SteamConnectionSample
    {
        public int connectionId, pingMs, sendRateBytesPerSecond;
        public int pendingReliableBytes, pendingUnreliableBytes, unacknowledgedBytes;
        public float sentBytesPerSecond, receivedBytesPerSecond, localQuality, remoteQuality;
        public double queueMilliseconds;
    }

    [Serializable]
    public struct SteamConnectionInvestigationSample
    {
        public string steamConnection, connectionInstance, readStatus, queueMicrosecondsRaw, queueValidity;
        public bool readSucceeded, queueValid, pendingValid, localQualityValid, remoteQualityValid;
        public int connectionId, pingMs, sendRateBytesPerSecond;
        public int pendingReliableBytes, pendingUnreliableBytes, unacknowledgedBytes;
        public float sentBytesPerSecond, receivedBytesPerSecond, localQuality, remoteQuality;
        public double queueMilliseconds;
    }

    // Enabled only by the opt-in gameplay observer; counts include the channel byte.
    public static partial class SteamTransportDiagnostics
    {
        public static bool Enabled;
        public static readonly long[] SentBytes = new long[2], ReceivedBytes = new long[2];
        public static readonly long[] SentMessages = new long[2], ReceivedMessages = new long[2];
        public static readonly int[] MaximumSentMessage = new int[2];
        public static long SendFailures;
        // Observers must consume this borrowed segment synchronously. No payload is retained.
        public static event Action<uint, ArraySegment<byte>, int, EResult> SendResult;
        public static event Action<uint, int, string, string, string, int> ConnectionState;
        internal static void RecordConnection(uint connection, int connectionId, string role, string previous, string current, int endReason, string remoteIdentity = null)
        {
            ObserveConnection(connection, connectionId, role, previous, current, endReason, remoteIdentity);
            try { ConnectionState?.Invoke(connection, connectionId, role, previous, current, endReason); }
            catch { /* An observer may not change connection lifecycle. */ }
        }
        private static readonly System.Collections.Generic.Dictionary<int, double> nextFailureNotice = new();
        private static readonly System.Diagnostics.Stopwatch failureClock = System.Diagnostics.Stopwatch.StartNew();
        internal static void RecordSendResult(uint connection, ArraySegment<byte> payload, int channel, EResult result)
        {
            RecordSend(payload.Count + 1, channel, result == EResult.k_EResultOK);
            try { SendResult?.Invoke(connection, payload, channel, result); }
            catch { /* An observer may not change delivery or disconnect the game. */ }
            if (result == EResult.k_EResultOK) return;
            int key = (int)result;
            double now = failureClock.Elapsed.TotalSeconds;
            if (nextFailureNotice.TryGetValue(key, out double next) && now < next) return;
            if (nextFailureNotice.Count >= 64) nextFailureNotice.Clear();
            nextFailureNotice[key] = now + 10;
            UnityEngine.Debug.LogFormat(UnityEngine.LogType.Warning, UnityEngine.LogOption.NoStacktrace, null,
                "Steam send failed: {0}. Repeated notices are limited to one per reason every 10 seconds; structured evidence records individual results.", result);
        }
        public static void Reset()
        {
            Array.Clear(SentBytes, 0, 2); Array.Clear(ReceivedBytes, 0, 2);
            Array.Clear(SentMessages, 0, 2); Array.Clear(ReceivedMessages, 0, 2);
            Array.Clear(MaximumSentMessage, 0, 2); SendFailures = 0;
            nextFailureNotice.Clear();
        }
        internal static void RecordSend(int bytes, int channel, bool success)
        {
            if (!Enabled) return;
            if (!success) { SendFailures++; return; }
            if ((uint)channel >= 2) return;
            SentBytes[channel] += bytes; SentMessages[channel]++;
            MaximumSentMessage[channel] = Math.Max(MaximumSentMessage[channel], bytes);
        }
        internal static void RecordReceive(int bytes, int channel)
        {
            if (!Enabled || (uint)channel >= 2) return;
            ReceivedBytes[channel] += bytes; ReceivedMessages[channel]++;
        }
        internal static bool TrySample(HSteamNetConnection connection, int id, out SteamConnectionSample sample,
            System.Collections.Generic.List<SteamConnectionInvestigationSample> investigation = null)
        {
            sample = default;
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
#if UNITY_SERVER
            var result = SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes);
#else
            var result = SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes);
#endif
            // The optional caller-owned list selects expanded observations without a dependency
            // from Mirror back into the game's diagnostic profile. Legacy reads allocate no strings.
            if (investigation != null) investigation.Add(DescribeSample(connection.m_HSteamNetConnection, id, result, status));
            return TryDescribeLegacySample(id, result, status, out sample);
        }
        public static bool TryDescribeLegacySample(int id, EResult result, SteamNetConnectionRealTimeStatus_t status, out SteamConnectionSample sample)
        {
            sample = default;
            if (result != EResult.k_EResultOK) return false;
            sample = new SteamConnectionSample {
                connectionId = id, pingMs = status.m_nPing, sendRateBytesPerSecond = status.m_nSendRateBytesPerSecond,
                pendingReliableBytes = status.m_cbPendingReliable, pendingUnreliableBytes = status.m_cbPendingUnreliable,
                unacknowledgedBytes = status.m_cbSentUnackedReliable,
                sentBytesPerSecond = status.m_flOutBytesPerSec, receivedBytesPerSecond = status.m_flInBytesPerSec,
                localQuality = status.m_flConnectionQualityLocal, remoteQuality = status.m_flConnectionQualityRemote,
                queueMilliseconds = status.m_usecQueueTime.m_SteamNetworkingMicroseconds / 1000d };
            return true;
        }
        public static SteamConnectionInvestigationSample DescribeSample(uint connection, int id, EResult result, SteamNetConnectionRealTimeStatus_t status)
        {
            bool read = result == EResult.k_EResultOK;
            long raw = status.m_usecQueueTime.m_SteamNetworkingMicroseconds;
            // A plausibility bound, not an SDK sentinel interpretation. Preserve exact raw data.
            string validity = !read ? "ReadFailed" : raw < 0 ? "Negative" : raw > 3600000000L ? "AboveOneHourUnverified" : "Valid";
            return new SteamConnectionInvestigationSample
            {
                steamConnection = connection.ToString(System.Globalization.CultureInfo.InvariantCulture),
                connectionInstance = GetConnectionInstance(connection),
                connectionId = id, readStatus = result.ToString(), readSucceeded = read,
                pendingValid = read && status.m_cbPendingReliable >= 0 && status.m_cbPendingUnreliable >= 0 && status.m_cbSentUnackedReliable >= 0,
                localQualityValid = read && status.m_flConnectionQualityLocal >= 0 && status.m_flConnectionQualityLocal <= 1,
                remoteQualityValid = read && status.m_flConnectionQualityRemote >= 0 && status.m_flConnectionQualityRemote <= 1,
                queueMicrosecondsRaw = read ? raw.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                queueValidity = validity, queueValid = validity == "Valid",
                pingMs = read ? status.m_nPing : -1, sendRateBytesPerSecond = read ? status.m_nSendRateBytesPerSecond : -1,
                pendingReliableBytes = read ? status.m_cbPendingReliable : -1, pendingUnreliableBytes = read ? status.m_cbPendingUnreliable : -1,
                unacknowledgedBytes = read ? status.m_cbSentUnackedReliable : -1,
                sentBytesPerSecond = read ? status.m_flOutBytesPerSec : -1, receivedBytesPerSecond = read ? status.m_flInBytesPerSec : -1,
                localQuality = read ? status.m_flConnectionQualityLocal : -1, remoteQuality = read ? status.m_flConnectionQualityRemote : -1,
                queueMilliseconds = validity == "Valid" ? raw / 1000d : -1
            };
        }
    }
}
#endif
