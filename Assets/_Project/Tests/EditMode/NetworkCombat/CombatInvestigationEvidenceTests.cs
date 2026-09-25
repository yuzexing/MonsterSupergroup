using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatInvestigationEvidenceTests
    {
        private sealed class Sink : IDiagnosticSink, IDiagnosticIntegritySink
        {
            public readonly List<DiagnosticRecord> rows = new();
            public bool TryWrite(DiagnosticRecord record) { rows.Add(record.Copy()); return true; }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture) => domain;
            public void ReportCaptureFailure(DiagnosticRecord record) => rows.Add(record.Copy());
        }
        private Sink sink;
        private IDiagnosticSink previousSink;
        private readonly List<GameObject> objects = new();
        private bool serverActive;
        [SetUp] public void SetUp()
        {
            previousSink = CombatEvidence.Sink; sink = new Sink(); CombatEvidence.Sink = sink;
            serverActive = NetworkServer.active;
            CombatInvestigationEvidence.Configure(EvidenceProfile.Diagnostic, Array.Empty<string>());
        }
        [TearDown] public void TearDown()
        {
            CombatInvestigationEvidence.Configure(EvidenceProfile.Standard, Array.Empty<string>());
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, serverActive);
            foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear(); CombatEvidence.Sink = previousSink;
        }
        [TestCase(EvidenceProfile.Standard, null, false)]
        [TestCase(EvidenceProfile.Standard, "on", false)]
        [TestCase(EvidenceProfile.Diagnostic, null, true)]
        [TestCase(EvidenceProfile.Diagnostic, "off", false)]
        public void ProfileGatePreventsPayloadCapture(EvidenceProfile profile, string setting, bool expected)
        {
            CombatInvestigationEvidence.Configure(profile, setting == null ? Array.Empty<string>() : new[] { "--combat-evidence-investigation=" + setting });
            int captured = 0;
            CombatInvestigationEvidence.Capture("probe", "Observed", () => { captured++; return new { value = 1 }; });
            Assert.That(captured, Is.EqualTo(expected ? 1 : 0));
            Assert.That(sink.rows.Count, Is.EqualTo(captured));
        }
        [Test] public void CaptureFailurePreservesIntegrityAndCannotChangeGameplayOutcome()
        {
            Assert.That(CombatInvestigationEvidence.Capture("state", "Observed", () => throw new InvalidOperationException("fixture")), Is.False);
            Assert.That(sink.rows.Single().stage, Is.EqualTo("evidence.gap"));
            Assert.That(sink.rows.Single().reason, Does.StartWith("investigation.state:"));
        }
        [Test] public void InvalidOrDuplicateSwitchCannotSilentlyChangeCapabilities()
        {
            Assert.Throws<ArgumentException>(() => CombatInvestigationEvidence.Configure(EvidenceProfile.Diagnostic,
                new[] { "--combat-evidence-investigation=typo" }));
            Assert.Throws<ArgumentException>(() => CombatInvestigationEvidence.Configure(EvidenceProfile.Diagnostic,
                new[] { "--combat-evidence-investigation=on", "--combat-evidence-investigation=off" }));
        }
        [Test] public void NestedPayloadIsDetachedAndAccountedBeforeReturningToCaller()
        {
            var state = new PlayerBuildSnapshot { Weapons = new[] { new PlayerBuildWeaponSnapshot { SlotIndex = 0, WeaponId = 17 } } };
            CombatInvestigationEvidence.Record("state", "Applied", input: new { state });
            state.Weapons[0].WeaponId = 99;
            JToken captured = (JToken)sink.rows.Single().input;
            Assert.That((uint)captured["state"]["Weapons"][0]["WeaponId"], Is.EqualTo(17));
            Assert.That(sink.rows.Single().estimatedBytes, Is.GreaterThanOrEqualTo(4096));
        }
        [Test] public void BuildMutationAndFailurePublishActualStateWithoutSwallowingFailure()
        {
            var selection = Selection(); var build = selection.GetComponent<PlayerBuildRuntime>();
            Invoke(selection, "AttachInvestigation");
            build.ConfigureInitialWeapon(17);
            Assert.Throws<InvalidOperationException>(() => build.EquipWeapon(99u));
            var rows = sink.rows.Where(r => r.stage == "investigation.state").Select(r => (JToken)r.input).ToArray();
            Assert.That(rows.Length, Is.EqualTo(2));
            Assert.That((uint)rows[0]["state"]["definition"]["InitialWeaponId"], Is.EqualTo(17));
            Assert.That((uint)rows[1]["state"]["definition"]["InitialWeaponId"], Is.EqualTo(17));
            Assert.That((string)rows[1]["cause"]["outcome"], Is.EqualTo("Failed"));
            Assert.That((string)rows[1]["stateRevision"], Is.EqualTo("2"));
            var begins = sink.rows.Where(r => r.stage == "investigation.build.begin").Select(r => (JToken)r.input).ToArray();
            Assert.That(begins, Has.Length.EqualTo(2));
            Assert.That((string)begins[0]["operationId"], Is.EqualTo((string)rows[0]["operationId"]));
            Assert.That((string)begins[1]["operationId"], Is.EqualTo((string)rows[1]["operationId"]));
            Assert.That(sink.rows[0].stage, Is.EqualTo("investigation.build.begin"));
        }
        [Test] public void DisabledInvestigationDoesNotSubscribeToBuildMutations()
        {
            CombatInvestigationEvidence.Configure(EvidenceProfile.Diagnostic, new[] { "--combat-evidence-investigation=off" });
            var selection = Selection(); Invoke(selection, "AttachInvestigation");
            selection.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(17);
            Assert.That(sink.rows, Is.Empty);
            Assert.That(Get(selection, "investigationSubscribed"), Is.False);
        }
        [Test] public void InvalidSelectionPublishesRejectionAndNeverPublishesAppliedBuild()
        {
            var selection = Selection();
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);
            Assert.That(selection.ServerSelect(null, 77, 99, out string error), Is.False);
            Assert.That(error, Is.Not.Null);
            var row = sink.rows.Single(r => r.stage == "investigation.state" && (string)((JToken)r.input)["domain"] == "selection");
            Assert.That(row.outcome, Is.EqualTo("Rejected"));
            Assert.That((string)((JToken)row.input)["cause"]["eventId"], Is.EqualTo("77"));
            Assert.That(sink.rows.Any(r => r.outcome == "Applied" || r.outcome == "Committed"), Is.False);
        }
        [Test] public void SyncVarApplicationRecordsActualPartialObservationWithoutSharedRevision()
        {
            var selection = Selection(); Set(selection, "level", 4); Set(selection, "experience", 2.5f);
            Invoke(selection, "OnLevelApplied", 3, 4);
            var data = (JToken)sink.rows.Single().input;
            Assert.That((int)data["state"]["level"], Is.EqualTo(4));
            Assert.That((float)data["state"]["experience"], Is.EqualTo(2.5f));
            Assert.That((bool)data["continuous"], Is.False);
            Assert.That((bool)data["state"]["atomicSnapshot"], Is.False);
            Assert.That(data["progressionRevision"], Is.Null);
        }
        [Test] public void ServerProgressionRestorationRecordsActualCommitAsObservedFullSnapshot()
        {
            var selection = Selection();
            Set(selection, "player", selection.gameObject.AddComponent<AstralShift.HellMaiden.Player.PlayerMovement>());
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);
            var restored = new PlayerProgressionSnapshot {
                Level = 4, Experience = 2.5f, PendingUpgradeCount = 1, BuildRevision = 7,
                OfferSequence = 5, Stage = UpgradeSelectionStage.Reward,
                Rewards = new[] { new PendingUpgradeReward { EarnedLevel = 4, Kind = UpgradeRewardKind.Equipment } }
            };
            selection.RestoreProgression(restored);
            var data = (JToken)sink.rows.Single(r => r.stage == "investigation.state" &&
                (string)((JToken)r.input)["domain"] == "progression").input;
            Assert.That((bool)data["continuous"], Is.False,
                "The full snapshot includes selection recovery fields without complete mutation boundaries.");
            Assert.That((bool)data["state"]["authoritative"], Is.True);
            Assert.That((string)data["perspective"], Is.EqualTo("Server"));
            Assert.That((string)data["cause"]["outcome"], Is.EqualTo("Committed"));
            Assert.That((int)data["state"]["snapshot"]["Level"], Is.EqualTo(selection.Level));
            Assert.That((float)data["state"]["snapshot"]["Experience"], Is.EqualTo(selection.Experience));
            Assert.That(data["state"]["snapshot"]["Rewards"].Count(), Is.EqualTo(selection.PendingUpgradeCount));
            Assert.That((uint)data["state"]["snapshot"]["OfferSequence"], Is.EqualTo(5));
            Assert.That(data["progressionRevision"], Is.Null);
        }
        [Test] public void DistinctAvatarComponentsDoNotReuseBirthIdentity()
        {
            var one = Selection(); var two = Selection();
            one.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(17);
            Invoke(one, "AttachInvestigation"); Invoke(two, "AttachInvestigation");
            one.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(18);
            two.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(18);
            var births = sink.rows.Where(r => r.stage == "investigation.state").Select(r => (string)((JToken)r.input)["identity"]["birth"]).ToArray();
            Assert.That(births, Has.Length.EqualTo(2)); Assert.That(births[0], Is.Not.EqualTo(births[1]));
        }
        [Test] public void ReusedAvatarSharesHostBirthUntilFinalStopAndStartsNewBaseline()
        {
            var selection = Selection(); var build = selection.GetComponent<PlayerBuildRuntime>();
            var participant = selection.gameObject.AddComponent<NetworkRunParticipant>();
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.netIdentity)).SetValue(participant, selection.netIdentity);
            Invoke(selection, "AttachInvestigation");
            participant.OnStartServer(); participant.OnStartClient(); participant.OnStartAuthority();
            build.ConfigureInitialWeapon(17);
            participant.OnStopAuthority(); participant.OnStopServer();
            build.ConfigureInitialWeapon(18);
            participant.OnStopClient(); participant.OnStopClient();
            // Other component OnStop callbacks still belong to the ending avatar.
            build.ConfigureInitialWeapon(19);
            // Mirror may invoke Selection before Participant during the next spawn.
            selection.OnStartClient(); participant.OnStartClient();
            build.ConfigureInitialWeapon(20);
            var states = sink.rows.Where(r => r.stage == "investigation.state" && (string)((JToken)r.input)["domain"] == "build")
                .Select(r => (JToken)r.input).ToArray();
            Assert.That(states, Has.Length.EqualTo(4));
            string original = (string)states[0]["identity"]["birth"];
            Assert.That((string)states[1]["identity"]["birth"], Is.EqualTo(original));
            Assert.That((string)states[2]["identity"]["birth"], Is.EqualTo(original));
            Assert.That((string)states[3]["identity"]["birth"], Is.Not.EqualTo(original));
            Assert.That(states.Select(s => (string)s["stateRevision"]), Is.EqualTo(new[] { "1", "2", "3", "1" }));
            Assert.That(states.Select(s => (bool)s["baseline"]), Is.EqualTo(new[] { true, false, false, true }));
            var initialSpawns = sink.rows.Where(r => r.stage == "investigation.identity" && r.outcome == "Spawned").Take(2);
            Assert.That(initialSpawns.All(r => (string)((JToken)r.input)["birth"] == original), Is.True);
        }
        [Test] public void CaptureSinkChangeRestartsLocalStateRevisionAndBaseline()
        {
            var selection = Selection(); Invoke(selection, "AttachInvestigation");
            selection.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(17);
            CombatEvidence.Sink = sink = new Sink();
            selection.GetComponent<PlayerBuildRuntime>().ConfigureInitialWeapon(18);
            var state = (JToken)sink.rows.Single(r => r.stage == "investigation.state").input;
            Assert.That((string)state["stateRevision"], Is.EqualTo("1"));
            Assert.That((bool)state["baseline"], Is.True);
        }
        [TestCase(EvidenceProfile.Standard, "on", false)]
        [TestCase(EvidenceProfile.Diagnostic, "off", false)]
        [TestCase(EvidenceProfile.Diagnostic, "on", true)]
        public void PerformanceEmissionGatesClockMetadataWithoutZeroValuedFakeAnchors(EvidenceProfile profile, string setting, bool expected)
        {
            CombatInvestigationEvidence.Configure(profile, new[] { "--combat-evidence-investigation=" + setting });
            var obj = new GameObject("Investigation performance fixture"); obj.SetActive(false); objects.Add(obj);
            var observer = obj.AddComponent<NetworkDiagnosticsObservation>();
            ((NetworkDiagnosticsWindow)Get(observer, "window")).Add(16);
            Invoke(observer, "Emit", Time.realtimeSinceStartupAsDouble + 1);
            var row = JObject.Parse((string)sink.rows.Single(r => r.stage == "performance.snapshot").input);
            Assert.That(row["frameCount"].Value<int>(), Is.EqualTo(1));
            Assert.That(row["clockDomain"] != null, Is.EqualTo(expected));
            Assert.That(row["clockReadStartedTicks"] != null, Is.EqualTo(expected));
            Assert.That(row["pendingDeathAgeClock"] != null, Is.EqualTo(expected));
            if (expected)
            {
                Assert.That((long)row["clockReadStartedTicks"], Is.GreaterThan(0));
                Assert.That((long)row["clockReadEndedTicks"], Is.GreaterThanOrEqualTo((long)row["clockReadStartedTicks"]));
                Assert.That((string)row["pendingDeathAgeClock"], Is.EqualTo("Unity.UnscaledTime"));
                Assert.That((int)row["pendingDeathAgeClockVersion"], Is.EqualTo(1));
            }
            else Assert.That(Get(observer, "investigationConnections"), Is.Null);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void OwnerStateReadsActualPresentationAfterClearOrRelease(bool release)
        {
            var selection = Selection(); var presentation = selection.GetComponent<ModifierSelectionController>();
            var equipment = UnityEditor.AssetDatabase.LoadAssetAtPath<AstralShift.HellMaiden.Data.Cards.EquipmentData>(
                "Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset");
            Assert.That(equipment, Is.Not.Null);
            typeof(ModifierSelectionController).GetProperty(nameof(ModifierSelectionController.Offers)).SetValue(presentation,
                Array.AsReadOnly(new[] { new ModifierOffer(17, equipment, 0, 1) }));
            typeof(ModifierSelectionController).GetProperty(nameof(ModifierSelectionController.Stage)).SetValue(presentation, UpgradeSelectionStage.EquipmentTarget);
            typeof(ModifierSelectionController).GetProperty(nameof(ModifierSelectionController.EarnedLevel)).SetValue(presentation, 5);
            Invoke(selection, "TraceSelection", "fixture", "Observed", 0UL, null, "Owner");
            var before = (JToken)sink.rows.Single().input;
            Assert.That((string)before["state"]["stage"], Is.EqualTo("EquipmentTarget"));
            Assert.That((int)before["state"]["offeredLevel"], Is.EqualTo(5));
            Assert.That((uint)before["state"]["options"][0]["ContentId"], Is.EqualTo(equipment.ID));
            if (release) Invoke(selection, "ReleaseOwner"); else presentation.ClearOffers();
            Invoke(selection, "TraceSelection", "fixture", "Observed", 0UL, null, "Owner");
            var after = (JToken)sink.rows.Last().input;
            Assert.That((string)after["state"]["stage"], Is.EqualTo(presentation.Stage.ToString()));
            Assert.That((int)after["state"]["offeredLevel"], Is.EqualTo(presentation.EarnedLevel));
            Assert.That(after["state"]["options"].Count(), Is.Zero);
            Assert.That((bool)after["continuous"], Is.False);
            // Earlier observations stay detached when the actual controller clears its cards.
            Assert.That(before["state"]["options"].Count(), Is.EqualTo(1));
        }
        private NetworkModifierSelection Selection()
        {
            var obj = new GameObject("Investigation fixture"); obj.SetActive(false); objects.Add(obj);
            var identity = obj.AddComponent<NetworkIdentity>(); var selection = obj.AddComponent<NetworkModifierSelection>();
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.netIdentity)).SetValue(selection, identity);
            Set(selection, "build", obj.GetComponent<PlayerBuildRuntime>());
            Set(selection, "presentation", obj.GetComponent<ModifierSelectionController>());
            return selection;
        }
        private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}
