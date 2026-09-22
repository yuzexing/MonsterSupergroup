using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerCombatGateway
    {
        public ulong NextServerEventId()
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_NextServerEventId(); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "NextServerEventId", new object[] {  }, o => ((ServerCombatGateway)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_NextServerEventId();
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
        internal bool TryAdmitMusicEffect(uint player, uint source, ulong eventId, double now)
        {
            if (!MonsterSupergroup.GAS.CombatEvidence.Enabled) { return EvidenceCore_TryAdmitMusicEffect(player, source, eventId, now); }
            using var evidence = MonsterSupergroup.GAS.CombatEvidence.Begin(this, "gateway", "TryAdmitMusicEffect", new object[] { player, source, eventId, now }, o => ((ServerCombatGateway)o).CaptureReplayState());
            var evidenceResult = EvidenceCore_TryAdmitMusicEffect(player, source, eventId, now);
            evidence.Complete(evidenceResult);
            return evidenceResult;
        }
    }
}
