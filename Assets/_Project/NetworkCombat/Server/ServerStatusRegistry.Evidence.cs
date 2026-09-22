using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerStatusRegistry
    {
        public void Clear()
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_Clear(); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "statuses", "Clear", new object[] {  }, o => ((ServerStatusRegistry)o).CaptureReplayState());
            EvidenceCore_Clear();
            evidence.Complete();

        }
        public void ForgetTargetHistory(uint targetEntityId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_ForgetTargetHistory(targetEntityId); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "statuses", "ForgetTargetHistory", new object[] { targetEntityId }, o => ((ServerStatusRegistry)o).CaptureReplayState());
            EvidenceCore_ForgetTargetHistory(targetEntityId);
            evidence.Complete();

        }
        public IReadOnlyList<CanonicalStatusState> RestoreTarget(
            uint previousTargetEntityId,
            uint targetEntityId,
            IReadOnlyList<CanonicalStatusState> checkpoint,
            double serverTime)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_RestoreTarget(previousTargetEntityId, targetEntityId, checkpoint, serverTime); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "statuses", "RestoreTarget", new object[] { previousTargetEntityId, targetEntityId, checkpoint, serverTime }, o => ((ServerStatusRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_RestoreTarget(previousTargetEntityId, targetEntityId, checkpoint, serverTime);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public StatusMutationResult Apply(
            uint senderPlayerId,
            StatusMutation mutation,
            double serverTime)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_Apply(senderPlayerId, mutation, serverTime); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "statuses", "Apply", new object[] { senderPlayerId, mutation, serverTime }, o => ((ServerStatusRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_Apply(senderPlayerId, mutation, serverTime);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CanonicalStatusState AddServerStatus(StatusInstance instance)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_AddServerStatus(instance); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "statuses", "AddServerStatus", new object[] { instance }, o => ((ServerStatusRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_AddServerStatus(instance);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public IReadOnlyList<CanonicalStatusState> HandleSourceDisconnected(
            uint sourcePlayerId,
            double serverTime,
            Func<StatusInstance, int> acceptedTicks = null)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_HandleSourceDisconnected(sourcePlayerId, serverTime, acceptedTicks); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "statuses", "HandleSourceDisconnected", new object[] { sourcePlayerId, serverTime, acceptedTicks }, o => ((ServerStatusRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_HandleSourceDisconnected(sourcePlayerId, serverTime, acceptedTicks);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public IReadOnlyList<CanonicalStatusState> RemoveTarget(uint targetEntityId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_RemoveTarget(targetEntityId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "statuses", "RemoveTarget", new object[] { targetEntityId }, o => ((ServerStatusRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_RemoveTarget(targetEntityId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public StatusAdvanceResult Advance(double serverTime)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_Advance(serverTime); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "statuses", "Advance", new object[] { serverTime }, o => ((ServerStatusRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_Advance(serverTime);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
    }
}
