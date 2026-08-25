namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>
    /// Deterministic payload budget for the transport-neutral contracts. Mirror
    /// headers, encryption and transport framing are intentionally excluded.
    /// </summary>
    public static class CombatBandwidthEstimator
    {
        public const int CombatResultBytes = 66;
        public const int StatusMutationBytes = 125;
        public const int PlayerHealthReportBytes = 33;
        public const int BatchAndArrayHeadersBytes = 16;

        public static long EstimatePayloadBytes(CombatSubmissionBatch batch)
        {
            return BatchAndArrayHeadersBytes +
                (long)batch.ResultCount * CombatResultBytes +
                (long)batch.StatusMutationCount * StatusMutationBytes +
                (long)batch.PlayerHealthReportCount * PlayerHealthReportBytes;
        }
    }
}
