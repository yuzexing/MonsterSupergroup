using System;

namespace MonsterSupergroup.GAS
{
    public enum EnemyStatusID : uint
    {
        None = 0,
        Burn = 2
    }

    public enum StatusStackMode
    {
        Add = 0,
        Replace = 1,
        HighestPriority = 2
    }

    public enum StatusApplicationResult
    {
        Added = 0,
        Replaced = 1,
        Refreshed = 2,
        Rejected = 3
    }

    public readonly struct StatusDefinition : IEquatable<StatusDefinition>
    {
        public StatusDefinition(EnemyStatusID id, StatusStackMode stackMode, int maxStacks)
        {
            if (id == EnemyStatusID.None)
            {
                throw new ArgumentException("A status definition must have a non-zero ID.", nameof(id));
            }

            if (maxStacks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxStacks), "Max stacks must be at least one.");
            }

            if (!Enum.IsDefined(typeof(StatusStackMode), stackMode))
            {
                throw new ArgumentOutOfRangeException(nameof(stackMode), stackMode, "Unknown status stack mode.");
            }

            Id = id;
            StackMode = stackMode;
            MaxStacks = maxStacks;
        }

        public EnemyStatusID Id { get; }

        public StatusStackMode StackMode { get; }

        public int MaxStacks { get; }

        public bool Equals(StatusDefinition other)
        {
            return Id == other.Id && StackMode == other.StackMode && MaxStacks == other.MaxStacks;
        }

        public override bool Equals(object obj)
        {
            return obj is StatusDefinition other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Id * 397) ^ ((int)StackMode * 31) ^ MaxStacks;
            }
        }
    }

    public readonly struct StatusApplication
    {
        public StatusApplication(
            StatusDefinition definition,
            int tickDamage,
            int numberOfHits,
            float hitIntervalDuration,
            float priority,
            uint damageSourceId = 0)
        {
            if (definition.Id == EnemyStatusID.None)
            {
                throw new ArgumentException("Status application must use a valid definition.", nameof(definition));
            }

            if (tickDamage < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickDamage), "Tick damage cannot be negative.");
            }

            if (numberOfHits < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(numberOfHits), "A status must tick at least once.");
            }

            if (float.IsNaN(hitIntervalDuration) || float.IsInfinity(hitIntervalDuration) || hitIntervalDuration <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(hitIntervalDuration), "Tick interval must be finite and greater than zero.");
            }

            if (float.IsNaN(priority) || float.IsInfinity(priority))
            {
                throw new ArgumentOutOfRangeException(nameof(priority), "Priority must be finite.");
            }

            Definition = definition;
            TickDamage = tickDamage;
            NumberOfHits = numberOfHits;
            HitIntervalDuration = hitIntervalDuration;
            Priority = priority;
            DamageSourceId = damageSourceId;
        }

        public StatusDefinition Definition { get; }

        public int TickDamage { get; }

        public int NumberOfHits { get; }

        public float HitIntervalDuration { get; }

        public float Priority { get; }

        public uint DamageSourceId { get; }
    }

    public readonly struct StatusTick
    {
        public StatusTick(EnemyStatusID statusId, DamageInfo damage, int tickIndex, bool isFinalTick)
        {
            StatusId = statusId;
            Damage = damage;
            TickIndex = tickIndex;
            IsFinalTick = isFinalTick;
        }

        public EnemyStatusID StatusId { get; }

        public DamageInfo Damage { get; }

        public int TickIndex { get; }

        public bool IsFinalTick { get; }
    }
}
