using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerAttackRegistry
    {
        public void RegisterPlayer(uint playerId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_RegisterPlayer(playerId); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "attacks", "RegisterPlayer", new object[] { playerId }, o => ((ServerAttackRegistry)o).CaptureReplayState());
            EvidenceCore_RegisterPlayer(playerId);
            evidence.Complete();

        }
        public void UnregisterPlayer(uint playerId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_UnregisterPlayer(playerId); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "attacks", "UnregisterPlayer", new object[] { playerId }, o => ((ServerAttackRegistry)o).CaptureReplayState());
            EvidenceCore_UnregisterPlayer(playerId);
            evidence.Complete();

        }
        public CombatRejectionReason Admit(uint playerId, uint sourceEntityId, uint weaponId,
            uint buildRevision, ulong rootEventId, EnemyKnockbackSettings knockback = default)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_Admit(playerId, sourceEntityId, weaponId, buildRevision, rootEventId, knockback); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "attacks", "Admit", new object[] { playerId, sourceEntityId, weaponId, buildRevision, rootEventId, knockback }, o => ((ServerAttackRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_Admit(playerId, sourceEntityId, weaponId, buildRevision, rootEventId, knockback);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        public bool Retire(uint playerId, ulong rootEventId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_Retire(playerId, rootEventId); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "attacks", "Retire", new object[] { playerId, rootEventId }, o => ((ServerAttackRegistry)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_Retire(playerId, rootEventId);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
    }
}
