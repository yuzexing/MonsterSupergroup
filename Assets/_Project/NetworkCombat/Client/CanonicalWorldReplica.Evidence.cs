using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class CanonicalWorldReplica
    {
        public void ForgetEntity(uint entityId)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_ForgetEntity(entityId); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "replica", "ForgetEntity", new object[] { entityId }, o => ((CanonicalWorldReplica)o).CaptureReplayState());
            EvidenceCore_ForgetEntity(entityId);
            evidence.Complete();

        }
        public void Apply(CanonicalWorldBatch batch) => ApplyWithEvidence(batch, null);

        internal void ApplyWithEvidence(CanonicalWorldBatch batch, Diagnostics.SharedEvidencePayload shared)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_Apply(batch); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "replica", "Apply", new object[] { (object)shared ?? batch }, o => ((CanonicalWorldReplica)o).CaptureReplayState());
            EvidenceCore_Apply(batch);
            evidence.Complete();

        }
        public void Clear()
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { EvidenceCore_Clear(); return; }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "replica", "Clear", new object[] {  }, o => ((CanonicalWorldReplica)o).CaptureReplayState());
            EvidenceCore_Clear();
            evidence.Complete();

        }
    }
}
