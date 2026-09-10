using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>
    /// Admission records for owned native attacks, not a second attack simulation.
    /// A record lives until the owner has flushed all outcomes and released the attack's leases.
    /// Build changes do not invalidate an already admitted immutable attack.
    /// </summary>
    public sealed class ServerAttackRegistry
    {
        private readonly Dictionary<uint, PlayerAttacks> players = new Dictionary<uint, PlayerAttacks>();
        private readonly int maximumActiveRootsPerPlayer;

        public ServerAttackRegistry(int maximumActiveRootsPerPlayer = 4096)
        {
            if (maximumActiveRootsPerPlayer < 1) throw new ArgumentOutOfRangeException(nameof(maximumActiveRootsPerPlayer));
            this.maximumActiveRootsPerPlayer = maximumActiveRootsPerPlayer;
        }

        public void RegisterPlayer(uint playerId)
        {
            if (playerId == 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            if (!players.ContainsKey(playerId)) players.Add(playerId, new PlayerAttacks());
        }

        public void UnregisterPlayer(uint playerId) => players.Remove(playerId);
        public bool RequiresAdmission(uint playerId) => players.ContainsKey(playerId);
        public int ActiveCount(uint playerId) => players.TryGetValue(playerId, out var player) ? player.Roots.Count : 0;

        // The adapter supplies server-verified source, weapon and revision after checking its Build.
        public CombatRejectionReason Admit(uint playerId, uint sourceEntityId, uint weaponId,
            uint buildRevision, ulong rootEventId, EnemyKnockbackSettings knockback = default)
        {
            if (!players.TryGetValue(playerId, out var player)) return CombatRejectionReason.InvalidSender;
            var id = new CombatEventId(rootEventId);
            if (!id.IsValid || id.Sequence == 0 || id.Sequence <= player.LastRootSequence)
                return CombatRejectionReason.InvalidSequence;
            if (sourceEntityId == 0 || weaponId == 0 || buildRevision == 0)
                return CombatRejectionReason.InvalidAttackRoot;
            if (player.Roots.Count >= maximumActiveRootsPerPlayer)
                return CombatRejectionReason.AttackCapacityExceeded;
            player.Roots.Add(rootEventId, new Root(sourceEntityId, weaponId, buildRevision, knockback));
            player.LastRootSequence = id.Sequence;
            return CombatRejectionReason.None;
        }

        public bool Retire(uint playerId, ulong rootEventId) =>
            players.TryGetValue(playerId, out var player) && player.Roots.Remove(rootEventId);

        public bool Contains(uint playerId, ulong rootEventId, uint weaponId) =>
            players.TryGetValue(playerId, out var player) && player.Roots.TryGetValue(rootEventId, out var root) &&
            root.WeaponId == weaponId;

        public bool TryGetKnockback(uint playerId, ulong rootEventId, out EnemyKnockbackSettings preset)
        {
            if (players.TryGetValue(playerId, out var player) && player.Roots.TryGetValue(rootEventId, out var root))
            { preset = root.Knockback; return preset.IsValid; }
            preset = default;
            return false;
        }

        public CombatRejectionReason Validate(StatusMutation mutation)
        {
            if (!players.TryGetValue(mutation.SourcePlayerId, out var player) ||
                mutation.Kind == StatusMutationKind.Remove)
                return CombatRejectionReason.None;
            return player.Roots.TryGetValue(mutation.RootEventId, out var root) &&
                root.SourceEntityId == mutation.SourceEntityId && root.WeaponId == mutation.AbilityId
                ? CombatRejectionReason.None : CombatRejectionReason.InvalidAttackRoot;
        }

        public CombatRejectionReason Validate(CombatResult result)
        {
            // Other server gateways can still accept their non-weapon contracts. Production
            // NetworkPlayer prefabs register before their Native Build can execute.
            if (!players.TryGetValue(result.SourcePlayerId, out var player)) return CombatRejectionReason.None;
            if (!player.Roots.TryGetValue(result.RootEventId, out var root) ||
                root.SourceEntityId != result.SourceEntityId || root.WeaponId != result.AbilityId)
                return CombatRejectionReason.InvalidAttackRoot;
            var id = new CombatEventId(result.EventId);
            var rootId = new CombatEventId(result.RootEventId);
            var parentId = new CombatEventId(result.ParentEventId);
            if (id.SourceSlot != rootId.SourceSlot || id.ConnectionEpoch != rootId.ConnectionEpoch ||
                parentId.SourceSlot != rootId.SourceSlot || parentId.ConnectionEpoch != rootId.ConnectionEpoch ||
                parentId.Sequence <= rootId.Sequence || parentId.Sequence >= id.Sequence || result.ChainDepth < 2)
                return CombatRejectionReason.InvalidAttackRoot;
            // BuildId is a GAS Modifier ID; it is deliberately not compared with BuildRevision.
            // Deduplication remains per result EventId so multi-hit and derived outcomes survive.
            return CombatRejectionReason.None;
        }

        private sealed class PlayerAttacks
        {
            public uint LastRootSequence;
            public readonly Dictionary<ulong, Root> Roots = new Dictionary<ulong, Root>();
        }

        private readonly struct Root
        {
            public Root(uint sourceEntityId, uint weaponId, uint buildRevision, EnemyKnockbackSettings knockback)
            { SourceEntityId = sourceEntityId; WeaponId = weaponId; BuildRevision = buildRevision; Knockback = knockback; }
            public uint SourceEntityId { get; }
            public uint WeaponId { get; }
            public uint BuildRevision { get; }
            public EnemyKnockbackSettings Knockback { get; }
        }
    }
}
