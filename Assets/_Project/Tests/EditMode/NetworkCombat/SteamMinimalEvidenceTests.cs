#if !DISABLESTEAMWORKS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using Mirror.FizzySteam;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class SteamMinimalEvidenceTests
    {
        private readonly List<DiagnosticRecord> records = new();
        private Action<DiagnosticRecord> previousSink;
        private bool previouslyEnabled;
        [SetUp] public void SetUp()
        {
            previousSink = NetworkMessageEvidence.LightweightSink; previouslyEnabled = SteamTransportDiagnostics.Enabled;
            Assert.That(CombatEvidence.Enabled, Is.False, "The lightweight check must not rely on full combat capture.");
            NetworkMessageEvidence.LightweightSink = record => records.Add(record);
            SteamTransportDiagnostics.Enabled = true; SteamTransportDiagnostics.ClearInjectionForTest();
            NetworkMessageEvidence.Install(); records.Clear(); SteamTransportDiagnostics.DrainInterval();
        }
        [TearDown] public void TearDown()
        {
            SteamTransportDiagnostics.ClearInjectionForTest(); SteamTransportDiagnostics.Enabled = previouslyEnabled;
            NetworkMessageEvidence.LightweightSink = previousSink;
        }
        private static byte[] Batch<T>(T message) where T : struct, NetworkMessage
        {
            using var part = NetworkWriterPool.Get(); NetworkMessages.Pack(message, part);
            using var batch = NetworkWriterPool.Get(); batch.WriteDouble(12.5);
            Compression.CompressVarUInt(batch, (ulong)part.Position); batch.WriteBytes(part.ToArray(), 0, part.Position);
            return batch.ToArray();
        }
        private static JObject Probe(uint connection, byte[] payload, int channel, int nativeResult = 1, long number = 701) =>
            JObject.FromObject(typeof(SteamTransportDiagnostics).GetMethod("SendProbeForTest").Invoke(null, new object[] { connection, payload, channel, nativeResult, number }));
        private IEnumerable<JObject> Inputs(string stage) => records.Where(r => r.stage == stage).Select(r => JObject.FromObject(r.input));

        [TestCase(Channels.Reliable)] [TestCase(Channels.Unreliable)]
        public void TargetedQueueFullDoesNotCallSdkOrMergeIdenticalPayloadsAcrossAttemptsAndRecipients(int channel)
        {
            SteamTransportDiagnostics.ConnectionForTest(2101, 1, "Server", "None", "ConnectRequested");
            SteamTransportDiagnostics.ConnectionForTest(2102, 2, "Server", "None", "ConnectRequested");
            string instance = SteamTransportDiagnostics.GetConnectionInstance(2101);
            byte[] payload = Batch(new ObjectDestroyMessage { netId = 142 });
            string rejectedAttempt = SteamTransportDiagnostics.NextSendAttemptForTest;
            SteamTransportDiagnostics.InjectQueueFullOnceForTest(instance, channel, rejectedAttempt);
            var rejected = Probe(2101, payload, channel);
            var retried = Probe(2101, payload, channel);
            var otherRecipient = Probe(2102, payload, channel);
            Assert.That((bool)rejected["nativeCalled"], Is.False);
            Assert.That((int)rejected["result"], Is.EqualTo(25)); // k_EResultLimitExceeded
            Assert.That((bool)retried["nativeCalled"], Is.True);
            Assert.That((int)retried["result"], Is.EqualTo(1));
            Assert.That((int)otherRecipient["result"], Is.EqualTo(1));
            var headers = Inputs("network.transport").ToArray();
            Assert.That(headers.Length, Is.EqualTo(3));
            Assert.That(headers.Select(v => (string)v["attempt"]).Distinct().Count(), Is.EqualTo(3));
            Assert.That((string)headers[0]["connectionInstance"], Is.EqualTo(instance));
            Assert.That((string)headers[2]["connectionInstance"], Is.Not.EqualTo(instance));
            Assert.That(headers[0]["messageNumber"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That((string)headers[1]["messageNumber"], Is.EqualTo("701"));
            Assert.That((bool)headers[0]["injected"], Is.True);
            Assert.That(Inputs("network.batch").Select(v => (string)v["batchHash"]).Distinct().Count(), Is.EqualTo(1), "Identical batch bytes still have three distinct send attempts.");
            foreach (var index in Inputs("network.batch")) Assert.That((uint)index["members"][0]["entities"][0]["entity"], Is.EqualTo(142));
            typeof(SteamTransportDiagnostics).GetMethod("ReceiveProbeForTest").Invoke(null, new object[] { 2101u, payload, channel, 919L });
            var receipt = Inputs("network.receive").Single();
            Assert.That((string)receipt["messageNumber"], Is.EqualTo("919"));
            Assert.That((string)receipt["connectionInstance"], Is.EqualTo(instance));
            Assert.That((int)receipt["lane"], Is.Zero);
            Assert.That(records.Single(r => r.stage == "network.receive").reason, Is.EqualTo("NotYetApplied"));
        }

        [Test] public void ReconnectWithSameHandleAndMirrorNumberDoesNotInheritInjectionOrLifetime()
        {
            SteamTransportDiagnostics.ConnectionForTest(2111, 4, "Server", "None", "ConnectRequested");
            string oldInstance = SteamTransportDiagnostics.GetConnectionInstance(2111);
            SteamTransportDiagnostics.InjectQueueFullOnceForTest(oldInstance, Channels.Reliable, SteamTransportDiagnostics.NextSendAttemptForTest);
            SteamTransportDiagnostics.ConnectionForTest(2111, 4, "Server", "Connected", "ClosedLocally");
            SteamTransportDiagnostics.ConnectionForTest(2111, 4, "Server", "None", "ConnectRequested");
            string current = SteamTransportDiagnostics.GetConnectionInstance(2111);
            Assert.That(current, Is.Not.EqualTo(oldInstance));
            Assert.That((bool)Probe(2111, Batch(new ObjectDestroyMessage { netId = 9 }), Channels.Reliable)["nativeCalled"], Is.True);
            Assert.That((string)Inputs("network.transport").Single()["connectionInstance"], Is.EqualTo(current));
            Assert.That(records.All(r => r.outcome != "Recovered"), Is.True);
        }

        [Test] public void ParseFailureCannotEraseQueueFullAndObservationNeverChangesNativeFailures()
        {
            SteamTransportDiagnostics.ConnectionForTest(2121, 0, "Client", "None", "ConnectRequested");
            byte[] malformed = { 1, 2, 3 };
            var nativeFailure = Probe(2121, malformed, Channels.Reliable, 25);
            Assert.That((int)nativeFailure["result"], Is.EqualTo(25)); Assert.That((bool)nativeFailure["nativeCalled"], Is.True);
            int header = records.FindIndex(r => r.stage == "network.transport"), parsed = records.FindIndex(r => r.stage == "network.batch");
            Assert.That(header, Is.GreaterThanOrEqualTo(0)); Assert.That(parsed, Is.GreaterThan(header));
            Assert.That(records[header].outcome, Is.EqualTo("Rejected"));
            Assert.That(records[parsed].outcome, Is.EqualTo("MetadataUnavailable"));
            Assert.That((bool)Inputs("network.batch").Single()["complete"], Is.False);
            Assert.That((bool)Inputs("network.transport").Single()["injected"], Is.False);
            Assert.That(CombatEvidence.Enabled, Is.False);
        }

        [Test] public void CombatWhitelistReturnsActualTargetsAndEventsWithoutInvokingCommand()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MirrorNetworkCombatBridge).TypeHandle);
            ushort function = 0;
            for (int id = 0; id <= ushort.MaxValue; id++)
            {
                var call = Mirror.RemoteCalls.RemoteProcedureCalls.GetDelegate((ushort)id);
                if (call?.Method.DeclaringType == typeof(MirrorNetworkCombatBridge) && call.Method.Name.StartsWith("InvokeUserCode_CmdSubmit__", StringComparison.Ordinal)) { function = (ushort)id; break; }
            }
            Assert.That(function, Is.Not.EqualTo(0), "Mirror generated command must be registered.");
            using var writer = NetworkWriterPool.Get();
            writer.Write(new CombatSubmissionBatch { Round = 3, BatchSequence = 40, Results = new[] { new CombatResult { EventId = 9007199254740999UL, TargetEntityId = 142, SourceEntityId = 7, TargetStateVersion = 18 } },
                EnemyDeathReports = new[] { new EnemyDeathReport { TargetEntityId = 143, EventId = 9007199254741001UL } } });
            var members = NetworkMessageEvidence.DescribeBatch(new ArraySegment<byte>(Batch(new CommandMessage { netId = 777, functionHash = function, payload = writer.ToArraySegment() })), out _, out bool complete);
            Assert.That(complete, Is.True);
            Assert.That(members[0].entity, Is.EqualTo(777), "RPC host remains separate from business objects.");
            Assert.That(members[0].entities.Select(e => e.entity), Is.EqualTo(new uint[] { 142, 143 }));
            Assert.That(members[0].entities[0].eventId, Is.EqualTo("9007199254740999"));
            Assert.That(members[0].batch, Is.EqualTo(40)); Assert.That(members[0].round, Is.EqualTo(3));
        }
        [Test] public void ThrowingObserverCannotReplaceTheNativeSendResult()
        {
            NetworkMessageEvidence.LightweightSink = _ => throw new InvalidOperationException("SimulatedCollectorFailure");
            var result = Probe(2131, Batch(new ObjectDestroyMessage { netId = 44 }), Channels.Reliable, 3);
            Assert.That((bool)result["nativeCalled"], Is.True);
            Assert.That((int)result["result"], Is.EqualTo(3));
        }
    }
}
#endif
