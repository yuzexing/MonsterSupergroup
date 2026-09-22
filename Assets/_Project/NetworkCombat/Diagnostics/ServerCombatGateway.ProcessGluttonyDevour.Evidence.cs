using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerCombatGateway
    {
        internal CombatApplyResult ProcessGluttonyDevour(uint player, uint source, uint target, ulong eventId, double now, out CanonicalWorldBatch batch)
        {
            if (!CombatEvidence.Enabled) return EvidenceCore_ProcessGluttonyDevour(player, source, target, eventId, now, out batch);
            using var evidence = CombatEvidence.Begin(this, "gateway", "ProcessGluttonyDevour", new object[] { player, source, target, eventId, now }, o => ((ServerCombatGateway)o).CaptureReplayState());
            var value = EvidenceCore_ProcessGluttonyDevour(player, source, target, eventId, now, out batch);
            evidence.Complete(new Diagnostics.ReplayOutResult { result = value, outValues = new object[] { batch } });
            return value;
        }
    }
}
