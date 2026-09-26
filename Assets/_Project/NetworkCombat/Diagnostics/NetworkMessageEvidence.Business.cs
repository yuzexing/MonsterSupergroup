using System;
using System.Collections.Generic;
using Mirror;
using Mirror.RemoteCalls;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public static partial class NetworkMessageEvidence
    {
        private static bool Named(string actual, string expected) => actual == "InvokeUserCode_" + expected || actual.StartsWith("InvokeUserCode_" + expected + "__", StringComparison.Ordinal);
        // Generated Mirror readers only: never invoke the registered remote-call delegate.
        private static void DescribeBusiness(ref Member member, ArraySegment<byte> payload)
        {
            var function = RemoteProcedureCalls.GetDelegate(member.function);
            if (function == null) { member.correlation = "UnknownFunction"; return; }
            string owner = function.Method.DeclaringType?.FullName, method = function.Method.Name;
            var entities = new List<BusinessEntity>();
            try
            {
                using var reader = NetworkReaderPool.Get(payload);
                if (owner == typeof(MirrorNetworkCombatBridge).FullName && Named(method, "CmdSubmit"))
                {
                    var value = reader.Read<CombatSubmissionBatch>(); member.business = "CombatSubmission"; member.round = value.Round; member.batch = value.BatchSequence;
                    if (value.Results != null) foreach (var x in value.Results) entities.Add(new BusinessEntity { entity = x.TargetEntityId, source = x.SourceEntityId, eventId = x.EventId.ToString(), version = x.TargetStateVersion, sequence = x.Sequence, role = "DamageTarget" });
                    if (value.StatusMutations != null) foreach (var x in value.StatusMutations) entities.Add(new BusinessEntity { entity = x.TargetEntityId, source = x.SourceEntityId, eventId = x.EventId.ToString(), version = x.BaseVersion, sequence = x.Sequence, role = "StatusTarget" });
                    if (value.PlayerHealthReports != null) foreach (var x in value.PlayerHealthReports) entities.Add(new BusinessEntity { entity = x.EntityId, source = x.PlayerId, eventId = x.EventId.ToString(), version = x.StateVersion, sequence = x.Sequence, role = "PlayerHealth" });
                    if (value.EnemyDeathReports != null) foreach (var x in value.EnemyDeathReports) entities.Add(new BusinessEntity { entity = x.TargetEntityId, source = x.SourceEntityId, eventId = x.EventId.ToString(), sequence = x.Sequence, role = "DeathReport" });
                }
                else if (owner == typeof(MirrorNetworkCombatBridge).FullName && Named(method, "TargetConfirmEnemyDeaths"))
                {
                    var receipts = reader.Read<EnemyDeathReceipt[]>(); member.round = reader.ReadUInt(); member.business = "EnemyDeathReceipts";
                    if (receipts != null) foreach (var x in receipts) entities.Add(new BusinessEntity { entity = x.TargetEntityId, source = x.Kill.KillerPlayerId, eventId = x.ReportEventId.ToString(), version = x.Kill.TargetStateVersion, role = "DeathReceipt" });
                }
                else if (owner == typeof(NetworkCombatWorld).FullName && (Named(method, "RpcApplyCanonical") || Named(method, "TargetApplyCanonical")))
                {
                    var value = reader.Read<CanonicalWorldBatch>(); member.round = reader.ReadUInt(); member.business = "CanonicalState"; member.server = value.ServerSequence;
                    if (value.Entities != null) foreach (var x in value.Entities) entities.Add(new BusinessEntity { entity = x.EntityId, source = x.OwnerPlayerId, version = x.StateVersion, role = "CanonicalEntity" });
                    if (value.Statuses != null) foreach (var x in value.Statuses) entities.Add(new BusinessEntity { entity = x.TargetEntityId, source = x.SourceEntityId, eventId = x.SourceEventId.ToString(), version = x.Version, role = "CanonicalStatus" });
                    if (value.ConfirmedKills != null) foreach (var x in value.ConfirmedKills) entities.Add(new BusinessEntity { entity = x.TargetEntityId, source = x.KillerPlayerId, eventId = x.CauseEventId.ToString(), version = x.TargetStateVersion, role = "ConfirmedKill" });
                    if (value.EnemyHitPresentations != null) foreach (var x in value.EnemyHitPresentations) entities.Add(new BusinessEntity { entity = x.TargetEntityId, source = x.SourcePlayerId, eventId = x.DamageEventId.ToString(), version = x.TargetStateVersion, role = "HitPresentation" });
                }
                else if ((owner == typeof(NetworkEnemySimulationEndpoint).FullName && (Named(method, "CmdSubmitLargeSnapshot") || Named(method, "CmdSubmitSnapshots"))) ||
                    (owner == typeof(NetworkEnemySimulationWorld).FullName && (Named(method, "TargetApplySnapshot") || Named(method, "TargetApplyLargeSnapshot") || Named(method, "RpcApplyLargeSnapshot") || Named(method, "RpcApplySnapshots"))))
                {
                    var value = reader.Read<EnemySimulationSnapshotBatch>(); member.business = "EnemyMovement"; member.round = value.Round; member.batch = value.BatchSequence;
                    member.reliableFallback = method.Contains("LargeSnapshot");
                    if (value.Snapshots != null) foreach (var x in value.Snapshots) entities.Add(new BusinessEntity { entity = x.EnemyEntityId, epoch = x.AssignmentEpoch, sequence = x.Sequence, role = "Movement" });
                }
                else if (owner == typeof(NetworkEnemySimulationEndpoint).FullName && Named(method, "CmdSubmitRuntimeCheckpoint"))
                {
                    var value = reader.Read<EnemySimulationCheckpoint>(); member.business = "SimulationCheckpoint";
                    entities.Add(new BusinessEntity { entity = value.Movement.EnemyEntityId, epoch = value.Movement.AssignmentEpoch, sequence = value.Movement.Sequence, role = "Checkpoint" });
                }
                else if (owner == typeof(NetworkEnemySimulationEndpoint).FullName && Named(method, "CmdReportSimulationReady"))
                {
                    var value = reader.Read<uint[]>(); member.business = "SimulationReady";
                    if (value != null) foreach (uint id in value) entities.Add(new BusinessEntity { entity = id, role = "Ready" });
                }
                else { member.correlation = "OutsideBusinessWhitelist"; return; }
                if (reader.Remaining != 0) throw new System.IO.InvalidDataException("BusinessPayloadTrailingBytes");
                member.entities = entities.ToArray(); member.correlation = "TypedBusinessIndex";
            }
            catch (Exception error)
            {
                member.entities = null; member.correlation = "BusinessParseFailed"; member.parseFailure = error.GetType().Name;
            }
        }
        private static int EstimateMembers(Member[] members)
        {
            long bytes = 1024;
            foreach (var value in members) bytes += 512L + (value.entities?.Length ?? 0) * 160L;
            return (int)Math.Min(int.MaxValue, bytes);
        }
    }
}
