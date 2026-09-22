using System;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerCombatGateway
    {
        private void BindEvidence()
        {
            CombatEvidence.Bind(Ledger, this, "ledger.", "gateway", o => ((ServerCombatGateway)o).CaptureReplayState());
            CombatEvidence.Bind(Statuses, this, "statuses.", "gateway", o => ((ServerCombatGateway)o).CaptureReplayState());
            CombatEvidence.Bind(Attacks, this, "attacks.", "gateway", o => ((ServerCombatGateway)o).CaptureReplayState());
            CombatEvidence.Bind(StatusDamageAdmissions, this, "admissions.", "gateway", o => ((ServerCombatGateway)o).CaptureReplayState());
        }
        public CanonicalWorldBatch ProcessBatch(uint senderPlayerId, CombatSubmissionBatch batch,
            double serverTime, out EnemyDeathReceipt[] deathReceipts)
        {
            if (!CombatEvidence.Enabled) return EvidenceCore_ProcessBatch(senderPlayerId, batch, serverTime, out deathReceipts);
            using var evidence = CombatEvidence.Begin(this, "gateway", "ProcessBatch", new object[] { senderPlayerId, batch, serverTime }, o => ((ServerCombatGateway)o).CaptureReplayState());
            using var decisions = GatewayEvidenceDecision.Begin(this, senderPlayerId, batch);
            var result = EvidenceCore_ProcessBatch(senderPlayerId, batch, serverTime, out deathReceipts);
            decisions?.Link(result);
            evidence.Complete(new GatewayReplayOutput { batch = result, receipts = deathReceipts });
            return result;
        }
    }
    [Serializable] public struct GatewayReplayOutput { public CanonicalWorldBatch batch; public EnemyDeathReceipt[] receipts; }
}
