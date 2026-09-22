using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerCombatGateway
    {
        internal CombatApplyResult[] ProcessMusicDamage(uint player, uint source, uint[] targets, int damage, ulong rootId, double now, out CanonicalWorldBatch batch)
        {
            if (!CombatEvidence.Enabled) return EvidenceCore_ProcessMusicDamage(player, source, targets, damage, rootId, now, out batch);
            using var evidence = CombatEvidence.Begin(this, "gateway", "ProcessMusicDamage", new object[] { player, source, targets, damage, rootId, now }, o => ((ServerCombatGateway)o).CaptureReplayState());
            var value = EvidenceCore_ProcessMusicDamage(player, source, targets, damage, rootId, now, out batch);
            evidence.Complete(new Diagnostics.ReplayOutResult { result = value, outValues = new object[] { batch } });
            return value;
        }
    }
}
