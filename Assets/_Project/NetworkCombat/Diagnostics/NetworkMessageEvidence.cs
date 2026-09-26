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
    public static partial class NetworkMessageEvidence
    {
        public static Action<DiagnosticRecord> LightweightSink;
        public static bool Enabled => CombatEvidence.Enabled || LightweightSink != null;
        public static void Write(DiagnosticRecord record)
        {
            if (CombatEvidence.Enabled) CombatEvidence.Write(record);
            else LightweightSink?.Invoke(record);
        }
        [Serializable] public struct Member
        {
            public ushort messageId, function;
            public uint entity;
            public byte component;
            public int bytes;
            public string payloadHash, kind, correlation, business, parseFailure;
            public uint round, batch, server;
            public bool reliableFallback;
            public BusinessEntity[] entities;
        }
        [Serializable] public struct BusinessEntity
        {
            public uint entity, source, version, epoch, sequence;
            public string eventId, role;
        }
        private struct BusinessContext { public string role, kind; public uint source, batch, server, epoch; }
        [ThreadStatic] private static BusinessContext context;

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
            Install();
        }
        // Mirror resets its diagnostic delegates at AfterSceneLoad. The observer's Start reinstalls
        // these subscriptions after all runtime initializers, including domain-reload-disabled play.
        public static void Install()
        {
            NetworkDiagnostics.OutMessageEvent -= Queued;
            NetworkDiagnostics.OutMessageEvent += Queued;
            NetworkDiagnostics.InMessageEvent -= Received;
            NetworkDiagnostics.InMessageEvent += Received;
#if !DISABLESTEAMWORKS
            Mirror.FizzySteam.SteamTransportDiagnostics.ObservationRequested = () => Enabled;
            Mirror.FizzySteam.SteamTransportDiagnostics.DetailedSend -= Sent;
            Mirror.FizzySteam.SteamTransportDiagnostics.DetailedSend += Sent;
            Mirror.FizzySteam.SteamTransportDiagnostics.DetailedReceive -= TransportReceived;
            Mirror.FizzySteam.SteamTransportDiagnostics.DetailedReceive += TransportReceived;
            Mirror.FizzySteam.SteamTransportDiagnostics.DetailedConnection -= Connection;
            Mirror.FizzySteam.SteamTransportDiagnostics.DetailedConnection += Connection;
#endif
        }
        private static void Queued(NetworkDiagnostics.MessageInfo info) => Message(info, false);
        private static void Received(NetworkDiagnostics.MessageInfo info) => Message(info, true);
        private static void Message(NetworkDiagnostics.MessageInfo info, bool received)
        {
            if (!Enabled) return;
            // The transport batch already contains the compact decoded member index in light mode.
            // Keep only explicit business submission scopes here; generic Received is not application.
            if (!CombatEvidence.Enabled && (received || context.kind == null)) return;
            try
            {
                Member member;
                if (info.message is CommandMessage command) member = Describe(command);
                else if (info.message is RpcMessage rpc) member = Describe(rpc);
                else return; // Other messages remain visible in transport metadata with explicitly limited correlation.
                Write(new DiagnosticRecord {
                    role = received ? "Receiver" : context.role ?? "Sender", stage = "network.message",
                    outcome = received ? "Received" : "Enqueued", reason = received || context.kind == null ? "PayloadIdentityOnly" : "BusinessBatchLinked",
                    source = received ? 0 : context.source, batchSequence = received ? 0 : context.batch, serverSequence = received ? 0 : context.server,
                    connectionEpoch = received ? 0 : context.epoch,
                    input = new { business = received ? null : context.kind, info.channel, info.bytes, info.count, member }, estimatedBytes = 1024 });
            }
            catch (Exception error) { Write(new DiagnosticRecord { role = "Process", stage = "network.evidence", outcome = "Gap", reason = "MessageMetadataCaptureFailed", input = error.GetType().Name, estimatedBytes = 512 }); }
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
                if (id == NetworkMessageId<CommandMessage>.Id) { var command = message.Read<CommandMessage>(); var member = Describe(command); DescribeBusiness(ref member, command.payload); result.Add(member); }
                else if (id == NetworkMessageId<RpcMessage>.Id) { var rpc = message.Read<RpcMessage>(); var member = Describe(rpc); DescribeBusiness(ref member, rpc.payload); result.Add(member); }
                else if (id == NetworkMessageId<ObjectDestroyMessage>.Id) { var destroy = message.Read<ObjectDestroyMessage>(); result.Add(new Member { messageId = id, bytes = (int)length, kind = "Destroy", business = "Destroy", correlation = "TypedBusinessIndex", entities = new[] { new BusinessEntity { entity = destroy.netId, role = "Destroyed" } } }); }
                else if (id == NetworkMessageId<EntityStateMessage>.Id) { var state = message.Read<EntityStateMessage>(); result.Add(new Member { messageId = id, bytes = (int)length, kind = "EntityState", business = "EntityState", correlation = "EntityKnownPayloadNotDecoded", entities = new[] { new BusinessEntity { entity = state.netId, role = "NetworkObjectState" } } }); }
                else result.Add(new Member { messageId = id, bytes = (int)length, kind = "Other", correlation = "NoBusinessIdentifier" });
            }
            foreach (var item in result) if (item.parseFailure != null) complete = false;
            return result.ToArray();
        }
#if !DISABLESTEAMWORKS
        private static void Connection(Mirror.FizzySteam.SteamConnectionIdentity identity, string previous, int endReason)
        {
            if (!Enabled) return;
            Write(new DiagnosticRecord { role = identity.role, stage = "network.connection", outcome = "Changed", reason = identity.state,
                input = new { identity.connectionInstance, identity.steamConnection, identity.connectionId, identity.remoteIdentity, identity.role, identity.closed, endReason }, before = previous, after = identity.state, critical = true });
        }
        private static void Sent(Mirror.FizzySteam.SteamSendObservation observation, ArraySegment<byte> payload)
        {
            if (!Enabled) return;
            bool accepted = observation.result == Steamworks.EResult.k_EResultOK;
            // Persist the failure fact before parsing. A malformed batch never erases a Steam rejection.
            Write(new DiagnosticRecord { role = observation.role, stage = "network.transport", outcome = accepted ? "Accepted" : "Rejected", reason = observation.result.ToString(), critical = !accepted,
                input = new { observation.connectionInstance, observation.steamConnection, observation.connectionId, observation.remoteIdentity, observation.role, direction = "Send", observation.attempt,
                    packetSequence = observation.attempt, observation.messageNumber, observation.channel, observation.lane, observation.bytes, result = observation.result.ToString(), observation.injected }, estimatedBytes = 1024 });
            try
            {
                Member[] members = DescribeBatch(payload, out double senderTime, out bool complete);
                foreach (var member in members) Mirror.FizzySteam.SteamTransportDiagnostics.RecordBusinessTraffic(observation.connectionInstance, observation.channel, member.business ?? "Unknown", observation.result, member.bytes, member.reliableFallback);
                Write(new DiagnosticRecord { role = observation.role, stage = "network.batch", outcome = "Indexed", reason = "SendBatch", critical = !accepted,
                    input = new { observation.connectionInstance, observation.steamConnection, direction = "Send", observation.attempt, observation.channel, observation.lane, observation.messageNumber, batchHash = Hash(payload), senderTime, complete, members },
                    estimatedBytes = EstimateMembers(members) });
            }
            catch (Exception error) { Write(new DiagnosticRecord { role = observation.role, stage = "network.batch", outcome = "MetadataUnavailable", reason = "PacketMetadataCaptureFailed", critical = !accepted,
                input = new { observation.connectionInstance, observation.attempt, direction = "Send", complete = false, parseFailure = error.GetType().Name }, estimatedBytes = 512 }); }
        }
        private static void TransportReceived(Mirror.FizzySteam.SteamReceiveObservation observation, ArraySegment<byte> payload)
        {
            if (!Enabled) return;
            Write(new DiagnosticRecord { role = observation.role, stage = "network.receive", outcome = "ReceivedFromSteam", reason = "NotYetApplied",
                input = new { observation.connectionInstance, observation.steamConnection, observation.connectionId, observation.role, observation.remoteIdentity, direction = "Receive", observation.receiveSequence, observation.messageNumber, observation.channel, observation.lane, observation.bytes }, estimatedBytes = 1024 });
            try
            {
                var members = DescribeBatch(payload, out double senderTime, out bool complete);
                foreach (var member in members) Mirror.FizzySteam.SteamTransportDiagnostics.RecordBusinessReceived(observation.connectionInstance, observation.channel, member.business ?? "Unknown", member.bytes);
                Write(new DiagnosticRecord { role = observation.role, stage = "network.batch", outcome = "Indexed", reason = "ReceiveBatch",
                    input = new { observation.connectionInstance, observation.steamConnection, direction = "Receive", observation.receiveSequence, observation.messageNumber, observation.channel, observation.lane, batchHash = Hash(payload), senderTime, complete, members }, estimatedBytes = EstimateMembers(members) });
            }
            catch (Exception error) { Write(new DiagnosticRecord { role = observation.role, stage = "network.batch", outcome = "MetadataUnavailable", reason = "PacketMetadataCaptureFailed",
                input = new { observation.connectionInstance, observation.receiveSequence, direction = "Receive", complete = false, parseFailure = error.GetType().Name }, estimatedBytes = 512 }); }
        }
#endif
    }
}
