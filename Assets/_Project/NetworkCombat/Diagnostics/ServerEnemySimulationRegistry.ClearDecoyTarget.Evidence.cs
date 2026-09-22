using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerEnemySimulationRegistry
    {
        public bool ClearDecoyTarget(uint enemyId, uint owner, ulong cast, out EnemyTargetState target)
        {
            if (!CombatEvidence.Enabled) return EvidenceCore_ClearDecoyTarget(enemyId, owner, cast, out target);
            using var evidence = CombatEvidence.Begin(this, "authority", "ClearDecoyTarget", new object[] { enemyId, owner, cast }, o => ((ServerEnemySimulationRegistry)o).CaptureReplayState());
            var value = EvidenceCore_ClearDecoyTarget(enemyId, owner, cast, out target);
            evidence.Complete(new Diagnostics.ReplayOutResult { result = value, outValues = new object[] { target } });
            return value;
        }
    }
}
