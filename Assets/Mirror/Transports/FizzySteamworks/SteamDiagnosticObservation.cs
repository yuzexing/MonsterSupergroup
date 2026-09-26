#if !DISABLESTEAMWORKS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Steamworks;

namespace Mirror.FizzySteam
{
    [Serializable] public sealed class SteamConnectionIdentity
    {
        public string connectionInstance, steamConnection, role, remoteIdentity, state;
        public int connectionId;
        public bool closed;
    }
    [Serializable] public struct SteamSendObservation
    {
        public string connectionInstance, steamConnection, role, remoteIdentity, attempt, messageNumber;
        public int connectionId, channel, lane, bytes;
        public EResult result;
        public bool injected;
    }
    [Serializable] public struct SteamReceiveObservation
    {
        public string connectionInstance, steamConnection, role, remoteIdentity, receiveSequence, messageNumber;
        public int connectionId, channel, lane, bytes;
    }
    [Serializable] public sealed class SteamTrafficInterval
    {
        public string connectionInstance, direction, business, result, bytesScope;
        public int channel;
        public long attempts, accepted, rejected, bytes, acceptedBytes, rejectedBytes, reliableFallbacks, received, receivedBytes;
    }
    [Serializable] public sealed class SteamPollInterval
    {
        public string connectionInstance;
        public long calls, messages, limitHits, failedReads;
        public double maximumGapSeconds, totalProcessingSeconds, maximumProcessingSeconds;
    }
    [Serializable] public sealed class SteamDiagnosticInterval
    {
        public string clock = "Stopwatch";
        public long timestamp, frequency = Stopwatch.Frequency;
        public SteamTrafficInterval[] traffic;
        public SteamPollInterval[] receivePolls;
    }

    // These are local observation identities. A numeric Steam handle is never a lifetime identity.
    public static partial class SteamTransportDiagnostics
    {
        public delegate EResult NativeSend(HSteamNetConnection connection, IntPtr data, uint bytes, int flags, out long messageNumber);
        public static event Action<SteamSendObservation, ArraySegment<byte>> DetailedSend;
        public static event Action<SteamReceiveObservation, ArraySegment<byte>> DetailedReceive;
        public static event Action<SteamConnectionIdentity, string, int> DetailedConnection;
        public static Func<bool> ObservationRequested;
        private static bool ObserveEnabled => Enabled || (ObservationRequested?.Invoke() ?? false);
        private static readonly Dictionary<uint, SteamConnectionIdentity> connections = new();
        private static readonly Dictionary<string, SteamTrafficInterval> traffic = new();
        private static readonly Dictionary<string, SteamPollInterval> polls = new();
        private static readonly Dictionary<string, long> previousPoll = new();
        private static ulong connectionSerial, sendSerial, receiveSerial;

        public static SteamConnectionIdentity[] ConnectionInstances
        {
            get { var result = new List<SteamConnectionIdentity>(); foreach (var item in connections.Values) result.Add(Copy(item)); return result.ToArray(); }
        }
        private static SteamConnectionIdentity Copy(SteamConnectionIdentity value) => new SteamConnectionIdentity
        { connectionInstance = value.connectionInstance, steamConnection = value.steamConnection, role = value.role,
            remoteIdentity = value.remoteIdentity, connectionId = value.connectionId, state = value.state, closed = value.closed };
        private static SteamConnectionIdentity Identity(uint connection)
        {
            if (!connections.TryGetValue(connection, out var identity))
            {
                identity = new SteamConnectionIdentity { connectionInstance = (++connectionSerial).ToString(), steamConnection = connection.ToString(),
                    connectionId = -1, role = "Unknown", state = "ObservedWithoutLifecycle" };
                connections[connection] = identity;
            }
            return identity;
        }
        public static string GetConnectionInstance(uint connection) => Identity(connection).connectionInstance;
        public static string GetConnectionInstanceForMirror(int connectionId, string role)
        {
            foreach (var value in connections.Values)
                if (!value.closed && value.connectionId == connectionId && value.role == role) return value.connectionInstance;
            return null;
        }
        private static bool Terminal(string state) => state == "ClosedLocally" || state == "Shutdown" ||
            state.EndsWith("ClosedByPeer", StringComparison.Ordinal) || state.EndsWith("ProblemDetectedLocally", StringComparison.Ordinal);
        internal static void ObserveConnection(uint connection, int mirror, string role, string previous, string current, int endReason, string remote)
        {
            if (!ObserveEnabled) return;
            try
            {
                bool starts = current.EndsWith("Connecting", StringComparison.Ordinal) || current == "ConnectRequested";
                if (connections.TryGetValue(connection, out var old) && old.closed && starts) connections.Remove(connection);
                var identity = Identity(connection);
                identity.connectionId = mirror >= 0 ? mirror : identity.connectionId; identity.role = role;
                identity.remoteIdentity = remote ?? identity.remoteIdentity; identity.state = current; identity.closed |= Terminal(current);
                DetailedConnection?.Invoke(Copy(identity), previous, endReason);
            }
            catch { /* Observation must not affect connection lifecycle. */ }
        }
        private static SteamTrafficInterval Traffic(string instance, int channel, string business, string result, string direction)
        {
            string key = instance + ":" + channel + ":" + business + ":" + result + ":" + direction;
            if (!traffic.TryGetValue(key, out var value))
                traffic[key] = value = new SteamTrafficInterval { connectionInstance = instance, channel = channel, business = business, result = result, direction = direction,
                    bytesScope = business == "__transport_total__" ? "SteamMessageIncludingChannelByte" : "DecodedMirrorMemberPayload" };
            return value;
        }
        public static void RecordBusinessTraffic(string instance, int channel, string business, EResult result, int bytes, bool reliableFallback)
        {
            if (!Enabled) return;
            var value = Traffic(instance, channel, business, result.ToString(), "Send");
            value.attempts++; value.bytes += bytes;
            if (result == EResult.k_EResultOK) { value.accepted++; value.acceptedBytes += bytes; }
            else { value.rejected++; value.rejectedBytes += bytes; }
            if (reliableFallback) value.reliableFallbacks++;
        }
        public static SteamDiagnosticInterval DrainInterval()
        {
            var result = new SteamDiagnosticInterval { timestamp = Stopwatch.GetTimestamp(), traffic = new List<SteamTrafficInterval>(traffic.Values).ToArray(), receivePolls = new List<SteamPollInterval>(polls.Values).ToArray() };
            traffic.Clear(); polls.Clear(); return result;
        }
        public static void RecordBusinessReceived(string instance, int channel, string business, int bytes)
        {
            if (!Enabled) return;
            var value = Traffic(instance, channel, business, "ReceivedFromSteam", "Receive");
            value.received++; value.receivedBytes += bytes; value.bytes += bytes;
        }
        public static long BeginReceivePoll() => Enabled ? Stopwatch.GetTimestamp() : 0;
        public static void EndReceivePoll(uint connection, int count, int limit, long started)
        {
            if (!Enabled || started == 0) return;
            try
            {
            string instance = GetConnectionInstance(connection); long ended = Stopwatch.GetTimestamp();
            if (!polls.TryGetValue(instance, out var value)) polls[instance] = value = new SteamPollInterval { connectionInstance = instance };
            value.calls++; value.messages += Math.Max(0, count); if (count == limit) value.limitHits++; if (count < 0) value.failedReads++;
            if (previousPoll.TryGetValue(instance, out long previous)) value.maximumGapSeconds = Math.Max(value.maximumGapSeconds, (started - previous) / (double)Stopwatch.Frequency);
            previousPoll[instance] = started;
            double duration = (ended - started) / (double)Stopwatch.Frequency;
            value.totalProcessingSeconds += duration; value.maximumProcessingSeconds = Math.Max(value.maximumProcessingSeconds, duration);
            }
            catch { /* Sampling cannot change receive callbacks or disconnect behavior. */ }
        }
        internal static EResult SendObserved(HSteamNetConnection connection, ArraySegment<byte> payload, int channel, IntPtr data, int flags, NativeSend native)
        {
            bool observing = ObserveEnabled;
            SteamConnectionIdentity identity = observing ? Identity(connection.m_HSteamNetConnection) : null;
            string attempt = observing ? (++sendSerial).ToString() : null;
            long number = 0; bool injected = false; EResult result;
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
            injected = injection != null && injection.connectionInstance == identity?.connectionInstance && injection.channel == channel && injection.attempt == attempt;
            if (injected) { injection = null; result = EResult.k_EResultLimitExceeded; }
            else
#endif
            result = native(connection, data, (uint)(payload.Count + 1), flags, out number);
            RecordSendResult(connection.m_HSteamNetConnection, payload, channel, result);
            if (observing)
            {
                try
                {
                    if (Enabled) RecordBusinessTraffic(identity.connectionInstance, channel, "__transport_total__", result, payload.Count + 1, false);
                    DetailedSend?.Invoke(new SteamSendObservation { connectionInstance = identity.connectionInstance, steamConnection = identity.steamConnection,
                        connectionId = identity.connectionId, role = identity.role, remoteIdentity = identity.remoteIdentity, attempt = attempt,
                        channel = channel, lane = 0, bytes = payload.Count + 1, messageNumber = result == EResult.k_EResultOK ? number.ToString() : null,
                        result = result, injected = injected }, payload);
                }
                catch { /* Preserve the SDK result even if observation fails. */ }
            }
            return result;
        }
        internal static void ObserveReceive(SteamNetworkingMessage_t message, ArraySegment<byte> payload, int channel)
        {
            if (!ObserveEnabled) return;
            try
            {
                var identity = Identity(message.m_conn.m_HSteamNetConnection);
                RecordBusinessReceived(identity.connectionInstance, channel, "__transport_total__", message.m_cbSize);
                DetailedReceive?.Invoke(new SteamReceiveObservation { connectionInstance = identity.connectionInstance, steamConnection = identity.steamConnection,
                    connectionId = identity.connectionId, role = identity.role, remoteIdentity = identity.remoteIdentity, channel = channel,
                    lane = message.m_idxLane, bytes = message.m_cbSize, receiveSequence = (++receiveSerial).ToString(), messageNumber = message.m_nMessageNumber.ToString() }, payload);
            }
            catch { /* Receipt observations do not affect delivery. */ }
        }
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        private sealed class Injection { public string connectionInstance, attempt; public int channel; }
        private static Injection injection;
        public static string NextSendAttemptForTest => (sendSerial + 1).ToString();
        public static void InjectQueueFullOnceForTest(string connectionInstance, int channel, string attempt) => injection = new Injection { connectionInstance = connectionInstance, channel = channel, attempt = attempt };
        public static void ClearInjectionForTest() => injection = null;
        public static EResult SendForTest(uint connection, ArraySegment<byte> payload, int channel, NativeSend native) =>
            SendObserved(new HSteamNetConnection(connection), payload, channel, IntPtr.Zero, 0, native);
        public static object SendProbeForTest(uint connection, byte[] payload, int channel, int nativeResult, long nativeMessageNumber)
        {
            bool nativeCalled = false;
            EResult FakeNative(HSteamNetConnection c, IntPtr p, uint count, int flags, out long number)
            { nativeCalled = true; number = nativeMessageNumber; return (EResult)nativeResult; }
            var result = SendForTest(connection, new ArraySegment<byte>(payload), channel, FakeNative);
            return new { result = (int)result, nativeCalled };
        }
        public static void ConnectionForTest(uint connection, int mirror, string role, string previous, string current) =>
            ObserveConnection(connection, mirror, role, previous, current, 0, "test-peer");
        public static void ReceiveProbeForTest(uint connection, byte[] payload, int channel, long messageNumber) =>
            ObserveReceive(new SteamNetworkingMessage_t { m_conn = new HSteamNetConnection(connection), m_cbSize = payload.Length + 1,
                m_idxLane = 0, m_nMessageNumber = messageNumber }, new ArraySegment<byte>(payload), channel);
#endif
    }
}
#endif
