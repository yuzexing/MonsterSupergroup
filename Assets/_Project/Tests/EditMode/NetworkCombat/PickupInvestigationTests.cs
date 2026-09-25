using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using AstralShift.HellMaiden.Player;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class PickupInvestigationTests
    {
        private sealed class Sink : IDiagnosticSink
        {
            public readonly List<DiagnosticRecord> Records = new();
            public bool TryWrite(DiagnosticRecord record) { Records.Add(record); return true; }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture) => null;
        }
        private Sink sink;
        private IDiagnosticSink previous;
        private readonly List<GameObject> objects = new();
        [SetUp] public void Setup()
        {
            previous = CombatEvidence.Sink; CombatEvidence.Sink = sink = new Sink();
            CombatInvestigationEvidence.Configure(EvidenceProfile.Diagnostic, Array.Empty<string>());
        }
        [TearDown] public void Teardown()
        {
            foreach (var item in objects) UnityEngine.Object.DestroyImmediate(item);
            objects.Clear(); CombatEvidence.Sink = previous;
            CombatInvestigationEvidence.Configure(EvidenceProfile.Standard, Array.Empty<string>());
        }
        private T Create<T>() where T : Component
        {
            var obj = new GameObject(typeof(T).Name); obj.SetActive(false); objects.Add(obj);
            var identity = obj.AddComponent<NetworkIdentity>();
            var component = obj.AddComponent<T>();
            // EditMode does not run NetworkIdentity.Awake's behaviour discovery.
            // Match Mirror's initialization for every required sibling as well.
            foreach (var behaviour in obj.GetComponents<NetworkBehaviour>())
                typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.netIdentity)).SetValue(behaviour, identity);
            return component;
        }
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);
        private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

        [Test] public void UnreadyWorldPreservesCollectionDecisionAndStandardRemainsSilent()
        {
            var world = Create<NetworkExperienceWorld>();
            Assert.That(world.TryCollect(null, null, "requested-run", ulong.MaxValue, out string diagnosticReason), Is.False);
            var record = sink.Records.Single(r => r.stage == "investigation.pickup.decision");
            var payload = (JObject)record.input;
            Assert.That((string)payload["dropId"], Is.EqualTo("18446744073709551615"));
            Assert.That((string)payload["requestedRun"], Is.EqualTo("requested-run"));
            Assert.That((bool)payload["data"]["benefitConfirmed"], Is.False);
            CombatInvestigationEvidence.Configure(EvidenceProfile.Standard, Array.Empty<string>());
            sink.Records.Clear();
            Assert.That(world.TryCollect(null, null, "requested-run", ulong.MaxValue, out string standardReason), Is.False);
            Assert.That(standardReason, Is.EqualTo(diagnosticReason)); Assert.That(sink.Records, Is.Empty);
        }
        [Test] public void CommittedReceiptValidatesRoundAvatarClaimAndEpochWithoutRegranting()
        {
            var world = Create<NetworkExperienceWorld>(); Set(world, "round", 3u); Set(world, "runId", "run");
            var claims = (Dictionary<ulong, (uint avatar, uint claim, ushort epoch)>)typeof(NetworkExperienceWorld)
                .GetField("committedClaims", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
            claims.Add(12, (10, 4, 7));
            var ids = new SequentialCombatEventIdSource(1, 7);
            var report = new PlayerHealthReport { EventId = ids.Next().Value, PickupDropId = 12, PickupClaimVersion = 4, PickupRound = 3 };
            Assert.That(Invoke(world, "ValidateHealthReceipt", 10u, report), Is.True);
            report.PickupClaimVersion++;
            Assert.That(Invoke(world, "ValidateHealthReceipt", 10u, report), Is.False);
            report.PickupClaimVersion--; report.PickupRound--;
            Assert.That(Invoke(world, "ValidateHealthReceipt", 10u, report), Is.False);
            Assert.That(claims.Count, Is.EqualTo(1));
            var records = sink.Records.Where(r => r.stage == "investigation.pickup.receipt_decision").ToArray();
            Assert.That(records.Select(r => r.outcome), Is.EqualTo(new[] { "Accepted", "Rejected", "Rejected" }));
            Assert.That(records.All(r => !(bool)((JObject)r.input)["data"]["benefitConfirmed"]), Is.True);
        }
        [Test] public void DespawnAfterAnotherClaimDoesNotInventLocalBenefit()
        {
            var gem = Create<NetworkExperienceGem>(); gem.Initialize("run", 9007199254740993UL, 12, PickupEffect.RestoreHealth);
            gem.BeginHealthFlight(27); gem.OnStopClient();
            var record = sink.Records.Single(r => r.stage == "investigation.pickup.disappearance");
            var payload = (JObject)record.input;
            Assert.That((string)payload["dropId"], Is.EqualTo("9007199254740993"));
            Assert.That((uint)payload["claimVersion"], Is.EqualTo(1));
            Assert.That(record.source, Is.EqualTo(27));
            Assert.That((bool)payload["data"]["benefitConfirmed"], Is.False);
        }
        [Test] public void WaitingReasonChangesRetainClaimIdentityWithoutPerFrameDuplicates()
        {
            var world = Create<NetworkExperienceWorld>(); var gem = Create<NetworkExperienceGem>();
            var type = typeof(NetworkExperienceWorld).GetNestedType("HealthClaim", BindingFlags.NonPublic);
            var claim = Activator.CreateInstance(type, true); Set(claim, "Item", gem); Set(claim, "Avatar", 23u);
            Set(claim, "Version", 4u); Set(claim, "Member", 77UL);
            Invoke(world, "ObserveClaimWait", 12UL, claim, "SelectingOrUnready");
            for (int i = 0; i < 60; i++) Invoke(world, "ObserveClaimWait", 12UL, claim, "SelectingOrUnready");
            Invoke(world, "ObserveClaimWait", 12UL, claim, "AwaitingOtherReceipt");
            var records = sink.Records.Where(r => r.stage == "investigation.pickup.wait").ToArray();
            Assert.That(records.Length, Is.EqualTo(2));
            Assert.That((string)((JObject)records[1].input)["data"]["previousReason"], Is.EqualTo("SelectingOrUnready"));
            Assert.That((string)((JObject)records[1].input)["participantId"], Is.EqualTo("77"));
        }
        [Test] public void DuplicateOwnerGrantResubmitsExistingReceiptWithoutRecordingNegativeHealingOrRejection()
        {
            var adapter = Create<NetworkCombatantAdapter>(); var obj = adapter.gameObject;
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isOwned)).SetValue(adapter.netIdentity, true);
            var bridge = obj.AddComponent<MirrorNetworkCombatBridge>();
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.netIdentity)).SetValue(bridge, adapter.netIdentity);
            var ids = new SequentialCombatEventIdSource(1, 1);
            using var collector = new ClientCombatCollector(27, ids);
            Set(bridge, "eventIds", ids); Set(bridge, "collector", collector); Set(adapter, "ownerBridge", bridge);
            var player = obj.AddComponent<PlayerMovement>(); var binding = obj.AddComponent<PlayerCombatantBinding>();
            Set(player, "combatantBinding", binding);
            var pickup = obj.AddComponent<NetworkExperienceCollector>(); Set(pickup, "player", player);
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.netIdentity)).SetValue(pickup, adapter.netIdentity);
            var applied = (HashSet<(string, ulong, uint)>)typeof(NetworkCombatantAdapter)
                .GetField("appliedPickups", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(adapter);
            var reports = (Dictionary<(string, ulong, uint), PlayerHealthReport>)typeof(NetworkCombatantAdapter)
                .GetField("pickupReports", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(adapter);
            applied.Add(("run", 12, 4));
            reports.Add(("run", 12, 4), new PlayerHealthReport { PlayerId = 27, EntityId = 27, PickupDropId = 12,
                PickupClaimVersion = 4, PickupRestoredHealth = 12, Health = 82 });
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int result = adapter.RestorePickupHealth("run", 12, 4, 12);
                Assert.That(result, Is.EqualTo(-1));
                Invoke(pickup, "ObserveOwnerResult", "run", 12UL, 4u, 12, result, binding.CurrentHealth);
            }
            Assert.That(collector.PendingPlayerHealthReportCount, Is.EqualTo(2));
            Assert.That(applied.Count, Is.EqualTo(1)); Assert.That(reports.Count, Is.EqualTo(1));
            var records = sink.Records.Where(r => r.stage == "investigation.pickup.owner_result").ToArray();
            Assert.That(records.Length, Is.EqualTo(2));
            foreach (var record in records)
            {
                var data = (JObject)((JObject)record.input)["data"];
                Assert.That(record.outcome, Is.EqualTo("ReceiptResubmitted"));
                Assert.That((int)data["actualRestored"], Is.Zero);
                Assert.That((int)data["adapterResult"], Is.EqualTo(-1));
                Assert.That((bool)data["duplicate"], Is.True);
                Assert.That((bool)data["awaitingServerCommit"], Is.True);
                Assert.That((bool)data["benefitConfirmed"], Is.False);
                Assert.That((int)data["afterHealth"], Is.EqualTo((int)data["beforeHealth"]));
            }
        }
    }
}
