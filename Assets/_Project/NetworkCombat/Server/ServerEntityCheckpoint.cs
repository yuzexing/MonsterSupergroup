namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>
    /// Server-retained facts for an absent avatar. The upgrade-selection gate is
    /// restored by the selection runtime, separately from durable invulnerability.
    /// This is checkpoint data, not another health simulation or a client request.
    /// </summary>
    public readonly struct ServerEntityCheckpoint
    {
        public ServerEntityCheckpoint(
            CanonicalEntityState state,
            bool absoluteInvulnerable)
        {
            State = state;
            AbsoluteInvulnerable = absoluteInvulnerable;
        }

        public CanonicalEntityState State { get; }
        public bool AbsoluteInvulnerable { get; }
    }
}
