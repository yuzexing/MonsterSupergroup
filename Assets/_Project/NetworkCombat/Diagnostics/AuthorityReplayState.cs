using System;
using System.Linq;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable] public sealed class AuthorityReplayState { public int version = 1; public AuthorityReplayEntry[] entries; }
    [Serializable] public struct AuthorityReplayEntry
    {
        public uint id;
        public EnemySimulationAssignment assignment;
        public EnemyTargetState target;
        public EnemySimulationSnapshot snapshot;
        public bool hasSnapshot, checkpoint, hasAttack;
        public ulong lastProjectile;
        public uint lastSequence, lastAttackSequence;
        public double lastTime;
        public EnemyAttackPresentationEdge attack;
    }
    public sealed partial class ServerEnemySimulationRegistry
    {
        public AuthorityReplayState CaptureReplayState() => new AuthorityReplayState {
            entries = entries.OrderBy(p => p.Key).Select(p => new AuthorityReplayEntry {
                id = p.Key, assignment = p.Value.Assignment, target = p.Value.Target, snapshot = Diagnostics.DiagnosticPayload.Freeze(p.Value.LastSnapshot),
                hasSnapshot = p.Value.HasSnapshot, checkpoint = p.Value.LastSnapshotIsCheckpoint,
                hasAttack = p.Value.HasAttackPresentation, lastProjectile = p.Value.LastConfirmedProjectileAction,
                lastSequence = p.Value.LastAcceptedSequence, lastTime = p.Value.LastAcceptedMovementTime,
                attack = Diagnostics.DiagnosticPayload.Freeze(p.Value.LastAttackPresentation), lastAttackSequence = p.Value.LastAcceptedAttackStateSequence }).ToArray() };
        public static ServerEnemySimulationRegistry RestoreReplayState(AuthorityReplayState state)
        {
            if (state.version != 1) throw new InvalidOperationException("Unsupported authority checkpoint.");
            var result = new ServerEnemySimulationRegistry();
            foreach (var e in state.entries) result.entries.Add(e.id, new Entry {
                Assignment = e.assignment, Target = e.target, LastSnapshot = e.snapshot, HasSnapshot = e.hasSnapshot,
                LastSnapshotIsCheckpoint = e.checkpoint, HasAttackPresentation = e.hasAttack,
                LastConfirmedProjectileAction = e.lastProjectile, LastAcceptedSequence = e.lastSequence,
                LastAcceptedMovementTime = e.lastTime, LastAttackPresentation = e.attack, LastAcceptedAttackStateSequence = e.lastAttackSequence });
            return result;
        }
    }
}
