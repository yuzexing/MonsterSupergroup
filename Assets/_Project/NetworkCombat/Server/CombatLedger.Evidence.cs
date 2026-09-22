using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class CombatLedger
    {
        public void RegisterSource(uint sourceEntityId, uint ownerPlayerId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_RegisterSource(sourceEntityId, ownerPlayerId); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "RegisterSource", new object[] { sourceEntityId, ownerPlayerId }, o => ((CombatLedger)o).CaptureReplayState());
            EvidenceCore_RegisterSource(sourceEntityId, ownerPlayerId);
            evidence.Complete();

        }
        public bool UnregisterSource(uint sourceEntityId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_UnregisterSource(sourceEntityId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "UnregisterSource", new object[] { sourceEntityId }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_UnregisterSource(sourceEntityId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CanonicalEntityState RegisterEntity(
            uint entityId,
            int maximumHealth,
            CombatEntityKind kind,
            CombatEntityAuthority authority,
            uint ownerPlayerId = 0)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_RegisterEntity(entityId, maximumHealth, kind, authority, ownerPlayerId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "RegisterEntity", new object[] { entityId, maximumHealth, kind, authority, ownerPlayerId }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_RegisterEntity(entityId, maximumHealth, kind, authority, ownerPlayerId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public bool UnregisterEntity(uint entityId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_UnregisterEntity(entityId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "UnregisterEntity", new object[] { entityId }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_UnregisterEntity(entityId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CanonicalEntityState RestoreEntityState(
            uint entityId,
            ServerEntityCheckpoint checkpoint)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_RestoreEntityState(entityId, checkpoint); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "RestoreEntityState", new object[] { entityId, checkpoint }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_RestoreEntityState(entityId, checkpoint);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public bool SetAbsoluteInvulnerable(uint entityId, bool value)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_SetAbsoluteInvulnerable(entityId, value); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "SetAbsoluteInvulnerable", new object[] { entityId, value }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_SetAbsoluteInvulnerable(entityId, value);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public bool SetPlayerUltimateInvulnerable(uint playerId, bool value)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_SetPlayerUltimateInvulnerable(playerId, value); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "SetPlayerUltimateInvulnerable", new object[] { playerId, value }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_SetPlayerUltimateInvulnerable(playerId, value);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public bool SetPlayerTrapInvulnerable(uint playerId, bool value)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_SetPlayerTrapInvulnerable(playerId, value); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "SetPlayerTrapInvulnerable", new object[] { playerId, value }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_SetPlayerTrapInvulnerable(playerId, value);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public bool SetPlayerUpgradeSelectionState(uint playerId, bool value)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_SetPlayerUpgradeSelectionState(playerId, value); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "SetPlayerUpgradeSelectionState", new object[] { playerId, value }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_SetPlayerUpgradeSelectionState(playerId, value);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CombatApplyResult Apply(uint senderPlayerId, CombatResult result)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_Apply(senderPlayerId, result); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "Apply", new object[] { senderPlayerId, result }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_Apply(senderPlayerId, result);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        internal CombatApplyResult ApplyGluttonyDevour(uint player, uint source, uint targetId, ulong eventId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_ApplyGluttonyDevour(player, source, targetId, eventId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "ApplyGluttonyDevour", new object[] { player, source, targetId, eventId }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_ApplyGluttonyDevour(player, source, targetId, eventId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CombatApplyResult ApplyServerStatusDamage(
            uint targetEntityId,
            int damage,
            ulong causeEventId,
            uint sourcePlayerId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_ApplyServerStatusDamage(targetEntityId, damage, causeEventId, sourcePlayerId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "ApplyServerStatusDamage", new object[] { targetEntityId, damage, causeEventId, sourcePlayerId }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_ApplyServerStatusDamage(targetEntityId, damage, causeEventId, sourcePlayerId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CombatApplyResult ApplyOwnerFinalReport(
            uint senderPlayerId,
            PlayerHealthReport report)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_ApplyOwnerFinalReport(senderPlayerId, report); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "ApplyOwnerFinalReport", new object[] { senderPlayerId, report }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_ApplyOwnerFinalReport(senderPlayerId, report);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public CombatApplyResult ApplyEnemyDeath(uint senderPlayerId, EnemyDeathReport report)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_ApplyEnemyDeath(senderPlayerId, report); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "ledger", "ApplyEnemyDeath", new object[] { senderPlayerId, report }, o => ((CombatLedger)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_ApplyEnemyDeath(senderPlayerId, report);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
    }
}
