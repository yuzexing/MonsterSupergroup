using System;
using MonsterSupergroup.GAS;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerEnemySimulationRegistry
    {
        public void ConfirmProjectileLaunch(EnemyProjectileLaunch launch)
        {
            if (!CombatEvidence.Enabled) { EvidenceCore_ConfirmProjectileLaunch(launch); return; }
            using var evidence = CombatEvidence.Begin(this, "authority", "ConfirmProjectileLaunch", new object[] { launch }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            EvidenceCore_ConfirmProjectileLaunch(launch); evidence.Complete();
        }
        public void RegisterEnemy(
            uint enemyEntityId,
            Vector2 initialPosition,
            double serverTime)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_RegisterEnemy(enemyEntityId, initialPosition, serverTime); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "RegisterEnemy", new object[] { enemyEntityId, initialPosition, serverTime }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            EvidenceCore_RegisterEnemy(enemyEntityId, initialPosition, serverTime);
            evidence.Complete();

        }
        public void UnregisterEnemy(uint enemyEntityId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_UnregisterEnemy(enemyEntityId); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "UnregisterEnemy", new object[] { enemyEntityId }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            EvidenceCore_UnregisterEnemy(enemyEntityId);
            evidence.Complete();

        }
        public EnemySimulationAssignment AssignClientOwner(
            uint enemyEntityId,
            uint ownerPlayerId,
            uint targetPlayerId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_AssignClientOwner(enemyEntityId, ownerPlayerId, targetPlayerId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "AssignClientOwner", new object[] { enemyEntityId, ownerPlayerId, targetPlayerId }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_AssignClientOwner(enemyEntityId, ownerPlayerId, targetPlayerId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public EnemySimulationAssignment AssignServerFallback(
            uint enemyEntityId,
            uint targetPlayerId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_AssignServerFallback(enemyEntityId, targetPlayerId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "AssignServerFallback", new object[] { enemyEntityId, targetPlayerId }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_AssignServerFallback(enemyEntityId, targetPlayerId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public EnemySimulationAssignment AssignServerAuthoritative(
            uint enemyEntityId,
            uint targetPlayerId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_AssignServerAuthoritative(enemyEntityId, targetPlayerId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "AssignServerAuthoritative", new object[] { enemyEntityId, targetPlayerId }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_AssignServerAuthoritative(enemyEntityId, targetPlayerId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public EnemySimulationAssignment Freeze(uint enemyEntityId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_Freeze(enemyEntityId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "Freeze", new object[] { enemyEntityId }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_Freeze(enemyEntityId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public EnemyTargetState SetAggroTarget(uint enemyId, uint playerId, uint controllerPlayerId = 0)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_SetAggroTarget(enemyId, playerId, controllerPlayerId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "SetAggroTarget", new object[] { enemyId, playerId, controllerPlayerId }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_SetAggroTarget(enemyId, playerId, controllerPlayerId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public EnemyTargetState SetDecoyTarget(uint enemyId, uint owner, ulong cast, Vector2 position, double expires)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_SetDecoyTarget(enemyId, owner, cast, position, expires); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "SetDecoyTarget", new object[] { enemyId, owner, cast, position, expires }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_SetDecoyTarget(enemyId, owner, cast, position, expires);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public void RecordCheckpoint(EnemySimulationCheckpoint checkpoint)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_RecordCheckpoint(checkpoint); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "RecordCheckpoint", new object[] { checkpoint }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            EvidenceCore_RecordCheckpoint(checkpoint);
            evidence.Complete();

        }
        public EnemySnapshotRejectionReason TryAcceptClientSnapshot(
            uint senderPlayerId,
            EnemySimulationSnapshot snapshot)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_TryAcceptClientSnapshot(senderPlayerId, snapshot); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "TryAcceptClientSnapshot", new object[] { senderPlayerId, snapshot }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            entries.TryGetValue(snapshot.EnemyEntityId, out var prior);
            var before = new { assignment = prior?.Assignment ?? default, sequence = prior?.LastAcceptedSequence ?? 0u, movementTime = prior?.LastAcceptedMovementTime ?? 0d };
            var evidenceResult = EvidenceCore_TryAcceptClientSnapshot(senderPlayerId, snapshot);
            MonsterSupergroup.GAS.CombatEvidence.Write(new MonsterSupergroup.GAS.DiagnosticRecord { role = "Server", stage = "authority.movement",
                outcome = evidenceResult == EnemySnapshotRejectionReason.None ? "Accepted" : "Rejected", reason = evidenceResult.ToString(),
                source = senderPlayerId, target = snapshot.EnemyEntityId, assignmentEpoch = snapshot.AssignmentEpoch,
                engine = CombatEvidence.CurrentEngine, input = new { snapshot.Sequence, snapshot.AssignmentEpoch, snapshot.SampleNetworkTime }, before = before,
                after = new { assignment = prior?.Assignment ?? default, sequence = prior?.LastAcceptedSequence ?? 0u, movementTime = prior?.LastAcceptedMovementTime ?? 0d },
                critical = evidenceResult != EnemySnapshotRejectionReason.None });
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public void RecordServerSnapshot(EnemySimulationSnapshot snapshot)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_RecordServerSnapshot(snapshot); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "RecordServerSnapshot", new object[] { snapshot }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            EvidenceCore_RecordServerSnapshot(snapshot);
            evidence.Complete();

        }
        public void RecordServerAttackPresentation(
            EnemyAttackPresentationEdge edge)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_RecordServerAttackPresentation(edge); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "RecordServerAttackPresentation", new object[] { edge }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            EvidenceCore_RecordServerAttackPresentation(edge);
            evidence.Complete();

        }
        public EnemySimulationAssignment RenewAssignment(uint enemyEntityId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_RenewAssignment(enemyEntityId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "authority", "RenewAssignment", new object[] { enemyEntityId }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_RenewAssignment(enemyEntityId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
    }
}
