using System;

namespace MonsterSupergroup.GAS
{
    [Flags]
    public enum CombatTags : ulong
    {
        None = 0,
        Attack = 1UL << 0,
        Projectile = 1UL << 1,
        Hit = 1UL << 2,
        Damage = 1UL << 3,
        Critical = 1UL << 4,
        Explosion = 1UL << 5,
        Fire = 1UL << 6,
        Poison = 1UL << 7,
        Burn = 1UL << 8,
        Periodic = 1UL << 9,
        Status = 1UL << 10,
        Build = 1UL << 11,
        PredictedLethalHit = 1UL << 12,
        ConfirmedKill = 1UL << 13
    }

    public readonly struct CombatEventId : IEquatable<CombatEventId>
    {
        private readonly ulong value;

        public CombatEventId(ulong value)
        {
            this.value = value;
        }

        public static CombatEventId None => default;

        public ulong Value => value;

        public bool IsValid => value != 0UL;

        public ushort SourceSlot => (ushort)(value >> 48);

        public ushort ConnectionEpoch => (ushort)(value >> 32);

        public uint Sequence => (uint)value;

        public static CombatEventId Compose(
            ushort sourceSlot,
            ushort connectionEpoch,
            uint sequence)
        {
            if (sequence == 0u)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequence),
                    "Combat event sequence zero is reserved for an invalid event ID.");
            }

            ulong packed = ((ulong)sourceSlot << 48) |
                ((ulong)connectionEpoch << 32) |
                sequence;
            return new CombatEventId(packed);
        }

        public bool Equals(CombatEventId other) => value == other.value;

        public override bool Equals(object obj) =>
            obj is CombatEventId other && Equals(other);

        public override int GetHashCode() => value.GetHashCode();

        public override string ToString() => IsValid
            ? $"{SourceSlot}:{ConnectionEpoch}:{Sequence}"
            : "None";

        public static bool operator ==(CombatEventId left, CombatEventId right) =>
            left.Equals(right);

        public static bool operator !=(CombatEventId left, CombatEventId right) =>
            !left.Equals(right);
    }

    public interface ICombatEventIdSource
    {
        CombatEventId Next();
    }

    public sealed class SequentialCombatEventIdSource : ICombatEventIdSource
    {
        private readonly ushort sourceSlot;
        private readonly ushort connectionEpoch;
        private uint nextSequence;

        public SequentialCombatEventIdSource(
            ushort sourceSlot = 0,
            ushort connectionEpoch = 0,
            uint firstSequence = 1)
        {
            if (firstSequence == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(firstSequence));
            }

            this.sourceSlot = sourceSlot;
            this.connectionEpoch = connectionEpoch;
            nextSequence = firstSequence;
        }

        public CombatEventId Next()
        {
            if (nextSequence == 0u)
            {
                throw new InvalidOperationException("Combat event sequence has overflowed.");
            }

            CombatEventId result = CombatEventId.Compose(
                sourceSlot,
                connectionEpoch,
                nextSequence);
            nextSequence = unchecked(nextSequence + 1u);
            return result;
        }
    }

    public readonly struct CombatContext
    {
        public CombatContext(
            CombatEventId eventId,
            CombatEventId rootEventId,
            CombatEventId parentEventId,
            uint sequence,
            ushort chainDepth,
            uint sourcePlayerId,
            uint sourceEntityId,
            uint targetEntityId,
            uint abilityId,
            uint buildId,
            CombatTags tags,
            uint targetStateVersion)
        {
            if (!eventId.IsValid)
            {
                throw new ArgumentException("Event ID must be valid.", nameof(eventId));
            }

            if (!rootEventId.IsValid)
            {
                throw new ArgumentException("Root event ID must be valid.", nameof(rootEventId));
            }

            EventId = eventId;
            RootEventId = rootEventId;
            ParentEventId = parentEventId;
            Sequence = sequence;
            ChainDepth = chainDepth;
            SourcePlayerId = sourcePlayerId;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            AbilityId = abilityId;
            BuildId = buildId;
            Tags = tags;
            TargetStateVersion = targetStateVersion;
        }

        public static CombatContext None => default;

        public CombatEventId EventId { get; }
        public CombatEventId RootEventId { get; }
        public CombatEventId ParentEventId { get; }
        public uint Sequence { get; }
        public ushort ChainDepth { get; }
        public uint SourcePlayerId { get; }
        public uint SourceEntityId { get; }
        public uint TargetEntityId { get; }
        public uint AbilityId { get; }
        public uint BuildId { get; }
        public CombatTags Tags { get; }
        public uint TargetStateVersion { get; }

        public bool IsValid => EventId.IsValid;

        public static CombatContext CreateRoot(
            CombatEventId eventId,
            uint sourcePlayerId,
            uint sourceEntityId,
            uint abilityId,
            CombatTags tags = CombatTags.Attack)
        {
            return new CombatContext(
                eventId,
                eventId,
                CombatEventId.None,
                eventId.Sequence,
                0,
                sourcePlayerId,
                sourceEntityId,
                0,
                abilityId,
                0,
                tags,
                0);
        }

        public CombatContext CreateChild(
            CombatEventId childEventId,
            CombatTags childTags,
            uint targetEntityId = 0,
            uint targetStateVersion = 0,
            uint buildId = 0)
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("Cannot create a child from an invalid combat context.");
            }

            if (ChainDepth == ushort.MaxValue)
            {
                throw new InvalidOperationException("Combat context chain depth has overflowed.");
            }

            return new CombatContext(
                childEventId,
                RootEventId,
                EventId,
                childEventId.Sequence,
                (ushort)(ChainDepth + 1),
                SourcePlayerId,
                SourceEntityId,
                targetEntityId,
                AbilityId,
                buildId != 0u ? buildId : BuildId,
                Tags | childTags,
                targetStateVersion);
        }

        public CombatContext WithBuild(uint buildId)
        {
            if (!IsValid)
            {
                return this;
            }

            return new CombatContext(
                EventId,
                RootEventId,
                ParentEventId,
                Sequence,
                ChainDepth,
                SourcePlayerId,
                SourceEntityId,
                TargetEntityId,
                AbilityId,
                buildId,
                Tags | CombatTags.Build,
                TargetStateVersion);
        }

        public CombatContext WithTags(CombatTags additionalTags)
        {
            if (!IsValid || additionalTags == CombatTags.None)
            {
                return this;
            }

            return new CombatContext(
                EventId,
                RootEventId,
                ParentEventId,
                Sequence,
                ChainDepth,
                SourcePlayerId,
                SourceEntityId,
                TargetEntityId,
                AbilityId,
                BuildId,
                Tags | additionalTags,
                TargetStateVersion);
        }
    }
}
