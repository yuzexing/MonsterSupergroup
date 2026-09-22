using System;
using System.Collections.Generic;

namespace MonsterSupergroup.GAS
{
    public sealed partial class StatusController
    {
        public StatusApplicationResult Apply(StatusApplication application)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_Apply(application); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "status", "Apply", new object[] { application }, o => ((StatusController)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_Apply(application);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public bool ApplyPredictedStackDelta(StatusInstanceId instanceId, int stackDelta)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_ApplyPredictedStackDelta(instanceId, stackDelta); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "status", "ApplyPredictedStackDelta", new object[] { instanceId, stackDelta }, o => ((StatusController)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_ApplyPredictedStackDelta(instanceId, stackDelta);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public bool UpsertCanonical(StatusInstance snapshot)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_UpsertCanonical(snapshot); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "status", "UpsertCanonical", new object[] { snapshot }, o => ((StatusController)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_UpsertCanonical(snapshot);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public bool RemoveCanonical(StatusInstanceId instanceId, uint version, uint applicationRevision = 0u)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_RemoveCanonical(instanceId, version, applicationRevision); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "status", "RemoveCanonical", new object[] { instanceId, version, applicationRevision }, o => ((StatusController)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_RemoveCanonical(instanceId, version, applicationRevision);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public void Advance(float deltaSeconds)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_Advance(deltaSeconds); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.BeginAdvance(this, deltaSeconds);
            EvidenceCore_Advance(deltaSeconds);
            evidence.Complete();

        }
        public bool Consume(EnemyStatusID statusId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_Consume(statusId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "status", "Consume", new object[] { statusId }, o => ((StatusController)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_Consume(statusId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public int ConsumeAll(EnemyStatusID statusId, bool dispatchImmediateTicks)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_ConsumeAll(statusId, dispatchImmediateTicks); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "status", "ConsumeAll", new object[] { statusId, dispatchImmediateTicks }, o => ((StatusController)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_ConsumeAll(statusId, dispatchImmediateTicks);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public bool Clear(EnemyStatusID statusId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_Clear(statusId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "status", "Clear", new object[] { statusId }, o => ((StatusController)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_Clear(statusId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public void Clear()
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_Clear(); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "status", "Clear", new object[] {  }, o => ((StatusController)o).CaptureReplayState());
            EvidenceCore_Clear();
            evidence.Complete();

        }
    }
}
