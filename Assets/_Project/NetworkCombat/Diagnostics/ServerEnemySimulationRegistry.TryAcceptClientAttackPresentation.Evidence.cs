using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerEnemySimulationRegistry
    {
        public EnemyAttackPresentationRejectionReason TryAcceptClientAttackPresentation(uint senderPlayerId, EnemyAttackPresentationEdge edge)
        {
            if (!CombatEvidence.Enabled) return EvidenceCore_TryAcceptClientAttackPresentation(senderPlayerId, edge);
            using var evidence = CombatEvidence.Begin(this, "authority", "TryAcceptClientAttackPresentation", new object[] { senderPlayerId, edge }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            var value = EvidenceCore_TryAcceptClientAttackPresentation(senderPlayerId, edge);
            evidence.Complete(value);
            return value;
        }
    }
}
