using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class NetworkLightweightCaptureTests
    {
        private IDiagnosticSink previous;
        private Action<DiagnosticRecord> previousLight, previousGateway;
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        [SetUp] public void SetUp()
        {
            previous = CombatEvidence.Sink; CombatEvidence.Sink = null;
            previousLight = NetworkMessageEvidence.LightweightSink; previousGateway = GatewayEvidenceDecision.LightweightSink;
            NetworkLightEvidence.Reset();
        }
        [TearDown] public void TearDown()
        {
            CombatEvidence.Sink = previous; NetworkMessageEvidence.LightweightSink = previousLight;
            GatewayEvidenceDecision.LightweightSink = previousGateway; NetworkLightEvidence.Reset();
        }
        [TestCase(true)] [TestCase(false)]
        public void StandaloneCaptureDetachesPayloadAndRequiresNormalClose(bool normalExit)
        {
            var root = new GameObject("lightweight-test");
            var observer = root.AddComponent<NetworkDiagnosticsObservation>();
            var stream = new MemoryStream();
            var writer = new LimboObservationLog(stream, () => 0);
            try
            {
                typeof(NetworkDiagnosticsObservation).GetField("log", PrivateInstance).SetValue(observer, writer);
                typeof(NetworkDiagnosticsObservation).GetField("captureId", PrivateInstance).SetValue(observer, "light-fixture");
                typeof(NetworkDiagnosticsObservation).GetMethod("BeginLightweightCapture", PrivateInstance).Invoke(observer, null);
                var value = new JObject { ["target"] = 17 };
                NetworkMessageEvidence.Write(new DiagnosticRecord { stage = "network.transport", input = value });
                value["target"] = 99;
                NetworkMessageEvidence.Write(new DiagnosticRecord { stage = "network.batch", input = new { complete = false, parseFailure = "Truncated" } });
                Assert.That(CombatEvidence.Sink, Is.Null);
                typeof(NetworkDiagnosticsObservation).GetMethod(normalExit ? "OnApplicationQuit" : "Close", PrivateInstance).Invoke(observer, null);
                var rows = Encoding.UTF8.GetString(stream.ToArray()).Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)).Select(JObject.Parse).ToArray();
                Assert.That(rows[0]["record"]["captureId"].Value<string>(), Is.EqualTo("light-fixture"));
                Assert.That(rows[0]["record"]["input"]["target"].Value<int>(), Is.EqualTo(17));
                Assert.That(rows.Select(x => (string)(x["record"]?["recordSequence"] ?? x["recordSequence"])), Is.EqualTo(new[] { "1", "2", "3" }));
                Assert.That(rows.Last()["normalClose"].Value<bool>(), Is.EqualTo(normalExit));
                Assert.That(rows.Last()["parseFailures"].Value<int>(), Is.EqualTo(1));
                Assert.That(rows.Last()["captureFailures"].Value<int>(), Is.Zero);
                Assert.That(NetworkMessageEvidence.LightweightSink, Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); writer.Dispose(); }
        }
        [Test] public void HostLifecycleSharesBirthUntilBothRolesEndAndReuseGetsNewBirth()
        {
            var records = new List<DiagnosticRecord>(); NetworkMessageEvidence.LightweightSink = x => records.Add(x);
            NetworkLightEvidence.Lifecycle(42, 1, true, "Server", "enemy", "Enemy");
            NetworkLightEvidence.Lifecycle(42, 2, true, "Replica", "enemy", "Enemy");
            string birth = NetworkLightEvidence.Birth(42);
            NetworkLightEvidence.Lifecycle(42, 1, false, "Server", "enemy", "Enemy");
            Assert.That(NetworkLightEvidence.Birth(42), Is.EqualTo(birth));
            NetworkLightEvidence.Lifecycle(42, 2, false, "Replica", "enemy", "Enemy");
            NetworkLightEvidence.Lifecycle(42, 2, true, "Replica", "enemy", "Enemy");
            Assert.That(records.Take(4).Select(x => x.entityGeneration), Is.All.EqualTo(birth));
            Assert.That(NetworkLightEvidence.Birth(42), Is.Not.EqualTo(birth));
        }
        [Test] public void InterruptedMovementNeverClaimsApplicationCompletedAndPreservesException()
        {
            var records = new List<DiagnosticRecord>(); NetworkMessageEvidence.LightweightSink = x => records.Add(x);
            Assert.Throws<InvalidOperationException>(() => {
                using var scope = NetworkLightEvidence.BeginMovement("Replica", 0, new EnemySimulationSnapshotBatch { BatchSequence = 5 });
                NetworkLightEvidence.MovementDecision(new EnemySimulationSnapshot { EnemyEntityId = 42, Sequence = 10 }, "Accepted", "None");
                throw new InvalidOperationException("original-gameplay-failure");
            });
            Assert.That(records.Single().outcome, Is.EqualTo("Interrupted"));
            Assert.That(records.Single().batchSequence, Is.EqualTo(5));
        }
        [Test] public void GatewayRejectionIsCapturedWithoutReplayOrAFullEvidenceSink()
        {
            var records = new List<DiagnosticRecord>();
            GatewayEvidenceDecision.LightweightSink = x => records.Add(x.Copy());
            var gateway = new ServerCombatGateway(); gateway.StopCombat();
            gateway.ProcessBatch(3, new CombatSubmissionBatch { BatchSequence = 9 }, 1);
            Assert.That(records.Single().outcome, Is.EqualTo("Rejected"));
            Assert.That(records.Single().reason, Is.EqualTo("CombatStopped"));
            Assert.That(records.Single().batchSequence, Is.EqualTo(9));
            Assert.That(CombatEvidence.Enabled, Is.False);
        }
    }
}
