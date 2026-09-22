using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerCombatGateway
    {
        public void StopCombat()
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_StopCombat(); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "StopCombat", new object[] {  }, o => ((ServerCombatGateway)o).CaptureReplayState());
            EvidenceCore_StopCombat();
            evidence.Complete();

        }
        public void ResetForNextRun()
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_ResetForNextRun(); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "ResetForNextRun", new object[] {  }, o => ((ServerCombatGateway)o).CaptureReplayState());
            EvidenceCore_ResetForNextRun();
            evidence.Complete();

        }
        public void RegisterClientIdentity(
            uint playerId,
            ushort sourceSlot,
            ushort connectionEpoch)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_RegisterClientIdentity(playerId, sourceSlot, connectionEpoch); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "RegisterClientIdentity", new object[] { playerId, sourceSlot, connectionEpoch }, o => ((ServerCombatGateway)o).CaptureReplayState());
            EvidenceCore_RegisterClientIdentity(playerId, sourceSlot, connectionEpoch);
            evidence.Complete();

        }
        public void UnregisterClientIdentity(uint playerId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_UnregisterClientIdentity(playerId); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "UnregisterClientIdentity", new object[] { playerId }, o => ((ServerCombatGateway)o).CaptureReplayState());
            EvidenceCore_UnregisterClientIdentity(playerId);
            evidence.Complete();

        }
        public CanonicalWorldBatch Advance(double serverTime)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_Advance(serverTime); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "Advance", new object[] { serverTime }, o => ((ServerCombatGateway)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_Advance(serverTime);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CanonicalWorldBatch HandleSourceDisconnected(
            uint sourcePlayerId,
            double serverTime)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_HandleSourceDisconnected(sourcePlayerId, serverTime); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "HandleSourceDisconnected", new object[] { sourcePlayerId, serverTime }, o => ((ServerCombatGateway)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_HandleSourceDisconnected(sourcePlayerId, serverTime);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CanonicalWorldBatch UnregisterEntity(uint entityId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_UnregisterEntity(entityId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "UnregisterEntity", new object[] { entityId }, o => ((ServerCombatGateway)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_UnregisterEntity(entityId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CanonicalWorldBatch CreateSnapshot()
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_CreateSnapshot(); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "CreateSnapshot", new object[] {  }, o => ((ServerCombatGateway)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_CreateSnapshot();
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CanonicalWorldBatch ResetEnemyCondition(uint entityId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_ResetEnemyCondition(entityId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "ResetEnemyCondition", new object[] { entityId }, o => ((ServerCombatGateway)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_ResetEnemyCondition(entityId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CanonicalWorldBatch CreateEntityUpdate(CanonicalEntityState state)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_CreateEntityUpdate(state); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "CreateEntityUpdate", new object[] { state }, o => ((ServerCombatGateway)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_CreateEntityUpdate(state);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
    }
}
