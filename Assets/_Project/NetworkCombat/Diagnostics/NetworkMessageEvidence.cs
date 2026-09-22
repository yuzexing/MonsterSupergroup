using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Mirror;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    /// <summary>Correlates a business submission with Mirror's queued message and Steam's actual result.
    /// Payload hashes are content identities, not unique delivery IDs; repeated sends remain separate records.</summary>
    public static class NetworkMessageEvidence
    {
        [Serializable] public struct Member
        {
            public ushort messageId, function;
            public uint entity;
            public byte component;
            public int bytes;
            public string payloadHash, kind, correlation;
        }
        private struct BusinessContext { public string role, kind; public uint source, batch, server, epoch; }
        [ThreadStatic] private static BusinessContext context;
        private static ulong packetSequence;

        public readonly struct Scope : IDisposable
        {
            private readonly BusinessContext previous;
            internal Scope(string role, string kind, uint source, uint batch, uint server, uint epoch)
            {
                previous = context;
                context = new BusinessContext { role = role, kind = kind, source = source, batch = batch, server = server, epoch = epoch };
            }
            public void Dispose() { context = previous; }
        }
        public static Scope Begin(string role, string kind, uint source = 0, uint batch = 0, uint server = 0, uint epoch = 0) =>
            new Scope(role, kind, source, batch, server, epoch);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            packetSequence = 0;
            Install();
        }
        // Mirror resets its diagnostic delegates at AfterSceneLoad. The observer's Start reinstalls
        // these subscriptions after all runtime initializers, including domain-reload-disabled play.
        internal static void Install()
        {
            NetworkDiagnostics.OutMessageEvent -= Queued;
            NetworkDiagnostics.OutMessageEvent += Queued;
            NetworkDiagnostics.InMessageEvent -= Received;
            NetworkDiagnostics.InMessageEvent += Received;
#if !DISABLESTEAMWORKS
            Mirror.FizzySteam.SteamTransportDiagnostics.SendResult -= Sent;
            Mirror.FizzySteam.SteamTransportDiagnostics.SendResult += Sent;
            Mirror.FizzySteam.SteamTransportDiagnostics.ConnectionState -= Connection;
            Mirror.FizzySteam.SteamTransportDiagnostics.ConnectionState += Connection;
#endif
        }
        private static void Queued(NetworkDiagnostics.MessageInfo info) => Message(info, false);
        private static void Received(NetworkDiagnostics.MessageInfo info) => Message(info, true);
        private static void Message(NetworkDiagnostics.MessageInfo info, bool received)
        {
            if (!CombatEvidence.Enabled) return;
            try
            {
                Member member;
                if (info.message is CommandMessage command) member = Describe(command);
                else if (info.message is RpcMessage rpc) member = Describe(rpc);
                else return; // Other messages remain visible in transport metadata with explicitly limited correlation.
                CombatEvidence.Write(new DiagnosticRecord {
                    role = received ? "Receiver" : context.role ?? "Sender", stage = "network.message",
                    outcome = received ? "Received" : "Enqueued", reason = received || context.kind == null ? "PayloadIdentityOnly" : "BusinessBatchLinked",
                    source = received ? 0 : context.source, batchSequence = received ? 0 : context.batch, serverSequence = received ? 0 : context.server,
                    connectionEpoch = received ? 0 : context.epoch,
                    input = new { business = received ? null : context.kind, info.channel, info.bytes, info.count, member }, estimatedBytes = 1024 });
            }
            catch (Exception error) { CombatEvidence.Event("Process", "network.evidence", "Gap", "MessageMetadataCaptureFailed", input: error.GetType().Name, bytes: 512); }
        }
        public static Member Describe(CommandMessage value) => new Member {
            messageId = NetworkMessageId<CommandMessage>.Id, kind = "Command", entity = value.netId, component = value.componentIndex,
            function = value.functionHash, bytes = value.payload.Count, payloadHash = Hash(value.payload), correlation = "PayloadIdentity" };
        public static Member Describe(RpcMessage value) => new Member {
            messageId = NetworkMessageId<RpcMessage>.Id, kind = "Rpc", entity = value.netId, component = value.componentIndex,
            function = value.functionHash, bytes = value.payload.Count, payloadHash = Hash(value.payload), correlation = "PayloadIdentity" };
        private static string Hash(ArraySegment<byte> value)
        {
            using var hash = SHA256.Create();
            byte[] result = hash.ComputeHash(value.Array ?? Array.Empty<byte>(), value.Offset, value.Count);
            return BitConverter.ToString(result).Replace("-", "").ToLowerInvariant();
        }
        // Reads only Mirror headers and borrowed payload spans. Never deserializes gameplay or keeps packet buffers.
        public static Member[] DescribeBatch(ArraySegment<byte> payload, out double senderTime, out bool complete)
        {
            using var reader = NetworkReaderPool.Get(payload);
            senderTime = reader.ReadDouble(); complete = true;
            var result = new List<Member>(8);
            while (reader.Remaining > 0)
            {
                if (result.Count >= 1024) { complete = false; break; }
                ulong length = Compression.DecompressVarUInt(reader);
                if (length < 2 || length > (ulong)reader.Remaining) throw new InvalidDataException("InvalidMirrorMessageLength");
                using var message = NetworkReaderPool.Get(reader.ReadBytesSegment((int)length));
                ushort id = message.ReadUShort();
                if (id == NetworkMessageId<CommandMessage>.Id) result.Add(Describe(message.Read<CommandMessage>()));
                else if (id == NetworkMessageId<RpcMessage>.Id) result.Add(Describe(message.Read<RpcMessage>()));
                else result.Add(new Member { messageId = id, bytes = (int)length, kind = "Other", correlation = "NoBusinessIdentifier" });
            }
            return result.ToArray();
        }
#if !DISABLESTEAMWORKS
        private static void Connection(uint connection, int connectionId, string role, string previous, string current, int endReason)
        {
            if (!CombatEvidence.Enabled) return;
            CombatEvidence.Write(new DiagnosticRecord { role = role, stage = "network.connection", outcome = "Changed", reason = current,
                input = new { steamConnection = connection.ToString(), connectionId, endReason }, before = previous, after = current, critical = true });
        }
        private static void Sent(uint connection, ArraySegment<byte> payload, int channel, Steamworks.EResult result)
        {
            if (!CombatEvidence.Enabled) return;
            string sequence = (++packetSequence).ToString();
            try
            {
                Member[] members = DescribeBatch(payload, out double senderTime, out bool complete);
                CombatEvidence.Write(new DiagnosticRecord { role = "Transport", stage = "network.transport", outcome = result == Steamworks.EResult.k_EResultOK ? "Sent" : "Failed",
                    reason = result.ToString(), critical = result != Steamworks.EResult.k_EResultOK,
                    input = new { packetSequence = sequence, steamConnection = connection.ToString(), channel, bytes = payload.Count + 1, senderTime, complete, members },
                    estimatedBytes = 1024 + members.Length * 512 });
            }
            catch (Exception error) { CombatEvidence.Event("Transport", "network.transport", "MetadataUnavailable", "PacketMetadataCaptureFailed",
                input: new { packetSequence = sequence, steamConnection = connection.ToString(), channel, bytes = payload.Count + 1, result = result.ToString(), error = error.GetType().Name }, bytes: 1024); }
        }
#endif
    }
}
