using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerStatusDamageAdmissions
    {
        public void Observe(StatusMutation mutation, CanonicalStatusState state, double serverTime)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_Observe(mutation, state, serverTime); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "admissions", "Observe", new object[] { mutation, state, serverTime }, o => ((ServerStatusDamageAdmissions)o).CaptureReplayState());
            EvidenceCore_Observe(mutation, state, serverTime);
            evidence.Complete();

        }
        public void Commit(CombatResult result)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_Commit(result); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "admissions", "Commit", new object[] { result }, o => ((ServerStatusDamageAdmissions)o).CaptureReplayState());
            EvidenceCore_Commit(result);
            evidence.Complete();

        }
        public void RemovePlayer(uint playerId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_RemovePlayer(playerId); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "admissions", "RemovePlayer", new object[] { playerId }, o => ((ServerStatusDamageAdmissions)o).CaptureReplayState());
            EvidenceCore_RemovePlayer(playerId);
            evidence.Complete();

        }
        public void Prune(double serverTime)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_Prune(serverTime); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "admissions", "Prune", new object[] { serverTime }, o => ((ServerStatusDamageAdmissions)o).CaptureReplayState());
            EvidenceCore_Prune(serverTime);
            evidence.Complete();

        }
    }
}
