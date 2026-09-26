using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    // Only the lightweight path needs these compact business facts. Full evidence already
    // records the same decisions with its existing payloads and integrity accounting.
    internal static class NetworkLightEvidence
    {
        public static bool Enabled => !CombatEvidence.Enabled && Diagnostics.NetworkMessageEvidence.LightweightSink != null;
        private static readonly Dictionary<uint, (string birth, int roles)> births = new();
        public static void Reset() => births.Clear();
        public static string Birth(uint entity) => births.TryGetValue(entity, out var entry) ? entry.birth : null;
        public static void Record(string role, string business, string outcome, string reason, Func<object> payload = null,
            uint source = 0, uint target = 0, uint batch = 0, uint server = 0, uint epoch = 0, ulong eventId = 0)
        {
            if (!Enabled) return;
            try { Diagnostics.NetworkMessageEvidence.Write(new DiagnosticRecord {
                role = role, stage = "network.application", outcome = outcome, reason = reason, operation = business,
                source = source, target = target, batchSequence = batch, serverSequence = server,
                assignmentEpoch = epoch, eventId = eventId == 0 ? null : eventId.ToString(),
                input = payload?.Invoke(), critical = outcome == "Rejected" || outcome == "Failed"
            }); }
            catch (Exception error) { NetworkDiagnosticsObservation.ReportNetworkCaptureFailure(business, error); }
        }
        public static void Lifecycle(uint entity, int roleFlag, bool started, string role, string name, string kind)
        {
            if (!Enabled || entity == 0) return;
            try
            {
                if (!births.TryGetValue(entity, out var value)) value = (started ? Guid.NewGuid().ToString("N") : null, 0);
                if (started) value.roles |= roleFlag;
                births[entity] = value;
                Diagnostics.NetworkMessageEvidence.Write(new DiagnosticRecord { role = role, stage = started ? "entity.spawn" : "entity.destroy",
                    outcome = "Observed", reason = "LocalLifecycleCallback", target = entity, entityGeneration = value.birth,
                    input = new { name, kind, callbackRole = role }, critical = true });
                if (!started) { value.roles &= ~roleFlag; if (value.roles == 0) births.Remove(entity); else births[entity] = value; }
            }
            catch (Exception error) { NetworkDiagnosticsObservation.ReportNetworkCaptureFailure("lifecycle", error); }
        }
        public static object Submission(CombatSubmissionBatch batch) => new {
            round = batch.Round, batch = batch.BatchSequence,
            results = batch.Results?.Select(x => new { eventId = x.EventId.ToString(), source = x.SourceEntityId, target = x.TargetEntityId, sequence = x.Sequence, version = x.TargetStateVersion }).ToArray(),
            deaths = batch.EnemyDeathReports?.Select(x => new { eventId = x.EventId.ToString(), causeEventId = x.CauseEventId.ToString(), target = x.TargetEntityId }).ToArray(),
            health = batch.PlayerHealthReports?.Select(x => new { eventId = x.EventId.ToString(), target = x.EntityId, version = x.StateVersion }).ToArray(),
            status = batch.StatusMutations?.Select(x => new { eventId = x.EventId.ToString(), target = x.TargetEntityId }).ToArray()
        };
        public static object Canonical(CanonicalWorldBatch batch) => new {
            server = batch.ServerSequence,
            entities = batch.Entities?.Select(x => new { target = x.EntityId, version = x.StateVersion, health = x.Health, alive = x.Alive }).ToArray(),
            kills = batch.ConfirmedKills?.Select(x => new { eventId = x.CauseEventId.ToString(), target = x.TargetEntityId, version = x.TargetStateVersion }).ToArray(),
            statuses = batch.Statuses?.Select(x => new { target = x.TargetEntityId, instance = x.InstanceId.ToString(), removed = x.Removed }).ToArray()
        };
        public static object CanonicalResult(CanonicalWorldBatch batch, CanonicalWorldReplica replica) => new {
            server = batch.ServerSequence, application = "ReturnedWithoutException",
            entities = batch.Entities?.Select(x => { bool found = replica.TryGetEntity(x.EntityId, out var current); return new {
                target = x.EntityId, incomingVersion = x.StateVersion, currentVersion = found ? (uint?)current.StateVersion : null,
                health = found ? (int?)current.Health : null, alive = found ? (bool?)current.Alive : null,
                outcome = !found ? "Unknown" : current.StateVersion > x.StateVersion ? "NewerStateRetained" : "CurrentStateObserved"
            }; }).ToArray()
        };
        [ThreadStatic] private static MovementScope currentMovement;
        internal sealed class MovementScope : IDisposable
        {
            private readonly MovementScope previous;
            private readonly string role;
            private readonly uint batch, source, round;
            private bool completed;
            internal readonly List<object> members = new();
            internal MovementScope(string role, uint source, EnemySimulationSnapshotBatch value)
            { previous = currentMovement; currentMovement = this; this.role = role; this.source = source; batch = value.BatchSequence; round = value.Round; }
            public void Complete() => completed = true;
            public void Dispose()
            {
                currentMovement = previous;
                Record(role, "EnemyMovement", completed ? "Processed" : "Interrupted", completed ? "PerEntityOutcomes" : "ApplicationDidNotReturn", () => new { round, members = members.ToArray() }, source: source, batch: batch);
            }
        }
        public static MovementScope BeginMovement(string role, uint source, EnemySimulationSnapshotBatch batch) => Enabled ? new MovementScope(role, source, batch) : null;
        public static void MovementDecision(EnemySimulationSnapshot snapshot, string outcome, string reason)
        {
            if (!Enabled) return;
            try {
                var value = new { target = snapshot.EnemyEntityId, assignmentEpoch = snapshot.AssignmentEpoch, sequence = snapshot.Sequence, outcome, reason };
                if (currentMovement != null) currentMovement.members.Add(value);
                else Record("Replica", "EnemyMovement", outcome, reason, () => value, target: snapshot.EnemyEntityId, epoch: snapshot.AssignmentEpoch);
            }
            catch (Exception error) { NetworkDiagnosticsObservation.ReportNetworkCaptureFailure("movement", error); }
        }
        public static object Movement(EnemySimulationSnapshot snapshot) => new {
            target = snapshot.EnemyEntityId, assignmentEpoch = snapshot.AssignmentEpoch, sequence = snapshot.Sequence,
            sampleNetworkTime = snapshot.SampleNetworkTime
        };
        public static void Identity(NetworkRunParticipant participant, string role, string outcome)
        {
            if (!Enabled || participant == null) return;
            try {
                var serverConnection = participant.connectionToClient;
                string bindingScope = serverConnection != null ? "ServerPlayerConnection" : participant.isOwned ? "OwnerHostTransport" : "RemoteAvatarOnly";
                string connectionInstance = null;
#if !DISABLESTEAMWORKS
                if (serverConnection != null) connectionInstance = Mirror.FizzySteam.SteamTransportDiagnostics.GetConnectionInstanceForMirror(serverConnection.connectionId, "Server");
                else if (participant.isOwned) connectionInstance = Mirror.FizzySteam.SteamTransportDiagnostics.GetConnectionInstanceForMirror(0, "Client");
#endif
                Diagnostics.NetworkMessageEvidence.Write(new DiagnosticRecord { role = role, stage = "network.identity", outcome = outcome,
                source = participant.AvatarId, target = participant.AvatarId, entityGeneration = Birth(participant.AvatarId),
                connectionEpoch = participant.GetComponent<MirrorNetworkCombatBridge>()?.ConnectionEpoch ?? 0,
                input = new { participantId = participant.ParticipantId.ToString(), avatar = participant.AvatarId, run = participant.RunId,
                    connectionId = serverConnection != null ? (int?)serverConnection.connectionId : null,
                    bindingScope, connectionInstance, isLocal = participant.isLocalPlayer }, critical = true }); }
            catch (Exception error) { NetworkDiagnosticsObservation.ReportNetworkCaptureFailure("identity", error); }
        }
    }
}
