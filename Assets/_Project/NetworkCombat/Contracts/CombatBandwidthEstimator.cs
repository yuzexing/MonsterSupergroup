namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>
    /// Deterministic payload budget for the transport-neutral contracts. Mirror
    /// headers, encryption and transport framing are intentionally excluded.
    /// </summary>
    public static class CombatBandwidthEstimator
    {
        public const int CombatResultBytes = 117; // Base 66 + knockback/combination 33 + status instance/round 12 + presentation 6.
        public const int EnemyHitPresentationBytes = 43; // Identity/version 16 + damage/style/source 14 + position 13.
        public const int StatusMutationBytes = 133; // Includes magnitude and application revision.
        public const int PlayerHealthReportBytes = 33;
        public const int BatchAndArrayHeadersBytes = 16;
        public const int EnemyActionProjectileProgressBytes = 9;
        public const int EnemyActionDashProgressBytes = 33; // Boolean plus four Vector2 values in the existing checkpoint.
        public const int EnemyActionExplosionProgressBytes = 11; // Three booleans and the world-space explosion center.
        public const int EnemyActionSequenceProgressBytes = 43; // bool, double, two Vector3, two int, two byte (uncompressed).

        // Projectile-only batches. Mirror uses variable-length integer and array writers.
        // Checkpoints contain a variable number of knockback receipts, measured separately.
        public static long EstimateEnemyProjectilePayloadBytes(EnemyAttackPresentationBatch batch, long totalCheckpointBytes)
        {
            long bytes = Mirror.Compression.VarUIntSize(batch.Round) + Mirror.Compression.VarUIntSize(batch.BatchSequence) + 1 +
                Mirror.Compression.VarUIntSize(batch.ProjectileLaunches == null ? 0 : (ulong)batch.ProjectileLaunches.Length + 1) +
                Mirror.Compression.VarUIntSize(batch.ProjectileTerminations == null ? 0 : (ulong)batch.ProjectileTerminations.Length + 1) + totalCheckpointBytes;
            if (batch.ProjectileLaunches != null)
                foreach (var launch in batch.ProjectileLaunches)
                    bytes += 43 + Mirror.Compression.VarUIntSize(launch.Key.EnemyEntityId) + Mirror.Compression.VarUIntSize(launch.Key.ActionId) +
                        Mirror.Compression.VarUIntSize(launch.AssignmentEpoch) + Mirror.Compression.VarUIntSize(launch.EnemyPrefabAssetId) + Mirror.Compression.VarIntSize(launch.Damage) + Mirror.Compression.VarUIntSize(launch.ViewTargetPlayerId);
            if (batch.ProjectileTerminations != null)
                foreach (var terminal in batch.ProjectileTerminations)
                    bytes += 3 + Mirror.Compression.VarUIntSize(terminal.Key.EnemyEntityId) + Mirror.Compression.VarUIntSize(terminal.Key.ActionId) + Mirror.Compression.VarUIntSize(terminal.TargetPlayerId);
            return bytes;
        }

        public static long EstimatePayloadBytes(CombatSubmissionBatch batch)
        {
            return BatchAndArrayHeadersBytes +
                (long)batch.ResultCount * CombatResultBytes +
                (long)batch.StatusMutationCount * StatusMutationBytes +
                (long)batch.PlayerHealthReportCount * PlayerHealthReportBytes;
        }
    }
}
