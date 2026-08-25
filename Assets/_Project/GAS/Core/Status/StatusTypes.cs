using System;

namespace MonsterSupergroup.GAS
{
    [Flags]
    public enum EnemyStatusID : uint
    {
        None = 0,
        Slow = 1,
        Burn = 2,
        Poison = 4,
        Bleed = 8,
        Weaken = 16,
        Fragile = 32,
        Stun = 64
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
            uint damageSourceId = 0,
            StatusInstanceId instanceId = default,
            int stack = 1,
            uint sourcePlayerId = 0,
            uint sourceEntityId = 0,
            uint targetEntityId = 0,
            double startTime = double.NaN,
            StatusExecutionAuthority executionAuthority = StatusExecutionAuthority.SourceClient,
            CombatContext sourceContext = default)
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

            if (stack < 1 || stack > definition.MaxStacks)
            {
                throw new ArgumentOutOfRangeException(nameof(stack));
            }

            if (!double.IsNaN(startTime) && double.IsInfinity(startTime))
            {
                throw new ArgumentOutOfRangeException(nameof(startTime));
            }

            if (!Enum.IsDefined(typeof(StatusExecutionAuthority), executionAuthority))
            {
                throw new ArgumentOutOfRangeException(nameof(executionAuthority));
            }

            float duration = numberOfHits * hitIntervalDuration;
            if (float.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(nameof(numberOfHits), "Status duration overflowed.");
            }

            Definition = definition;
            TickDamage = tickDamage;
            NumberOfHits = numberOfHits;
            HitIntervalDuration = hitIntervalDuration;
            Priority = priority;
            DamageSourceId = damageSourceId;
            InstanceId = instanceId;
            Stack = stack;
            SourcePlayerId = sourcePlayerId;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            StartTime = startTime;
            ExecutionAuthority = executionAuthority;
            SourceContext = sourceContext;
        }

        public StatusDefinition Definition { get; }

        public int TickDamage { get; }

        public int NumberOfHits { get; }

        public float HitIntervalDuration { get; }

        public float Priority { get; }

        public uint DamageSourceId { get; }

        public StatusInstanceId InstanceId { get; }

        public int Stack { get; }

        public uint SourcePlayerId { get; }

        public uint SourceEntityId { get; }

        public uint TargetEntityId { get; }

        public double StartTime { get; }

        public bool HasExplicitStartTime => !double.IsNaN(StartTime);

        public float Duration => NumberOfHits * HitIntervalDuration;

        public StatusExecutionAuthority ExecutionAuthority { get; }

        public CombatContext SourceContext { get; }
    }

    public readonly struct StatusTick
    {
        public StatusTick(EnemyStatusID statusId, DamageInfo damage, int tickIndex, bool isFinalTick)
        {
            Instance = default;
            StatusId = statusId;
            Damage = damage;
            TickIndex = tickIndex;
            IsFinalTick = isFinalTick;
        }

        public StatusTick(StatusInstance instance, DamageInfo damage, int tickIndex, bool isFinalTick)
        {
            Instance = instance;
            StatusId = instance.DefinitionId;
            Damage = damage;
            TickIndex = tickIndex;
            IsFinalTick = isFinalTick;
        }

        public StatusInstance Instance { get; }

        public StatusInstanceId InstanceId => Instance.InstanceId;

        public EnemyStatusID StatusId { get; }

        public DamageInfo Damage { get; }

        public int TickIndex { get; }

        public bool IsFinalTick { get; }
    }
}
