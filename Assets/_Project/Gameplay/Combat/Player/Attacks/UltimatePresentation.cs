namespace AstralShift.HellMaiden.Player.Attacks
{
    public enum UltimateTerminationReason : byte { Completed = 0, Cancelled = 1 }

    public readonly struct UltimatePresentationSpawn
    {
        public UltimatePresentationSpawn(uint ultimateId, ulong attackEventId,
            ProjectilePresentationStats stats, AttackElement element = AttackElement.Default)
        { UltimateId = ultimateId; AttackEventId = attackEventId; Stats = stats; Element = element; }
        public uint UltimateId { get; }
        public ulong AttackEventId { get; }
        public ProjectilePresentationStats Stats { get; }
        public AttackElement Element { get; }
    }

    public readonly struct UltimatePresentationTermination
    {
        public UltimatePresentationTermination(uint ultimateId, ulong attackEventId, UltimateTerminationReason reason)
        { UltimateId = ultimateId; AttackEventId = attackEventId; Reason = reason; }
        public uint UltimateId { get; }
        public ulong AttackEventId { get; }
        public UltimateTerminationReason Reason { get; }
    }
}
