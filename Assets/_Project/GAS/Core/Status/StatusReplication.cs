using System;

namespace MonsterSupergroup.GAS
{
    public readonly struct StatusInstanceId : IEquatable<StatusInstanceId>
    {
        private readonly ulong value;

        public StatusInstanceId(ulong value)
        {
            this.value = value;
        }

        public static StatusInstanceId None => default;
        public ulong Value => value;
        public bool IsValid => value != 0UL;

        public bool Equals(StatusInstanceId other) => value == other.value;
        public override bool Equals(object obj) =>
            obj is StatusInstanceId other && Equals(other);
        public override int GetHashCode() => value.GetHashCode();
        public override string ToString() => IsValid ? value.ToString() : "None";

        public static bool operator ==(StatusInstanceId left, StatusInstanceId right) =>
            left.Equals(right);
        public static bool operator !=(StatusInstanceId left, StatusInstanceId right) =>
            !left.Equals(right);
    }

    public interface IStatusInstanceIdSource
    {
        StatusInstanceId Next();
    }

    public sealed class SequentialStatusInstanceIdSource : IStatusInstanceIdSource
    {
        private readonly ushort sourceSlot;
        private readonly ushort connectionEpoch;
        private uint nextSequence;

        public SequentialStatusInstanceIdSource(
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

        public StatusInstanceId Next()
        {
            if (nextSequence == 0u)
            {
                throw new InvalidOperationException("Status instance sequence has overflowed.");
            }

            CombatEventId packed = CombatEventId.Compose(
                sourceSlot,
                connectionEpoch,
                nextSequence);
            nextSequence = unchecked(nextSequence + 1u);
            return new StatusInstanceId(packed.Value);
        }
    }

    /// <summary>
    /// Uses the owning client's combat-event sequence so status IDs stay unique
    /// across every target StatusController observed by that client.
    /// </summary>
    public sealed class CombatEventStatusInstanceIdSource : IStatusInstanceIdSource
    {
        private readonly ICombatEventIdSource eventIds;

        public CombatEventStatusInstanceIdSource(ICombatEventIdSource eventIds)
        {
            this.eventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));
        }

        public StatusInstanceId Next()
        {
            return new StatusInstanceId(eventIds.Next().Value);
        }
    }

    public enum StatusExecutionAuthority : byte
    {
        SourceClient = 0,
        Server = 1,
        TargetOwnerClient = 2
    }

    public interface IStatusExecutionPolicy
    {
        bool CanExecute(StatusInstance instance);
    }

    public sealed class StatusExecutionScope : IStatusExecutionPolicy
    {
        public StatusExecutionScope(
            bool isOffline,
            bool isServer,
            uint localPlayerId,
            uint targetOwnerPlayerId = 0)
        {
            IsOffline = isOffline;
            IsServer = isServer;
            LocalPlayerId = localPlayerId;
            TargetOwnerPlayerId = targetOwnerPlayerId;
        }

        public bool IsOffline { get; }
        public bool IsServer { get; }
        public uint LocalPlayerId { get; }
        public uint TargetOwnerPlayerId { get; }

        public bool CanExecute(StatusInstance instance)
        {
            if (IsOffline)
            {
                return true;
            }

            switch (instance.ExecutionAuthority)
            {
                case StatusExecutionAuthority.SourceClient:
                    return LocalPlayerId != 0 && LocalPlayerId == instance.SourcePlayerId;
                case StatusExecutionAuthority.Server:
                    return IsServer;
                case StatusExecutionAuthority.TargetOwnerClient:
                    return LocalPlayerId != 0 && LocalPlayerId == TargetOwnerPlayerId;
                default:
                    return false;
            }
        }
    }

    internal sealed class ExecuteAllStatusPolicy : IStatusExecutionPolicy
    {
        public static readonly ExecuteAllStatusPolicy Instance = new ExecuteAllStatusPolicy();

        private ExecuteAllStatusPolicy()
        {
        }

        public bool CanExecute(StatusInstance instance) => true;
    }

    public readonly struct StatusInstance
    {
        public StatusInstance(
            StatusInstanceId instanceId,
            StatusDefinition definition,
            uint sourcePlayerId,
            uint sourceEntityId,
            uint targetEntityId,
            int stack,
            double startTime,
            float duration,
            StatusExecutionAuthority executionAuthority,
            uint version,
            int tickDamage,
            int totalTicks,
            int completedTicks,
            float tickInterval,
            float priority,
            uint damageSourceId,
            CombatContext sourceContext = default,
            float magnitude = 0f)
        {
            if (!instanceId.IsValid)
            {
                throw new ArgumentException("Status instance ID must be valid.", nameof(instanceId));
            }

            if (definition.Id == EnemyStatusID.None)
            {
                throw new ArgumentException("Status definition must be valid.", nameof(definition));
            }

            if (stack < 1 || stack > definition.MaxStacks)
            {
                throw new ArgumentOutOfRangeException(nameof(stack));
            }

            if (double.IsNaN(startTime) || double.IsInfinity(startTime))
            {
                throw new ArgumentOutOfRangeException(nameof(startTime));
            }

            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            if (!Enum.IsDefined(typeof(StatusExecutionAuthority), executionAuthority))
            {
                throw new ArgumentOutOfRangeException(nameof(executionAuthority));
            }

            if (version == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            if (tickDamage < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickDamage));
            }

            if (totalTicks < 1 || completedTicks < 0 || completedTicks > totalTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(completedTicks));
            }

            if (float.IsNaN(tickInterval) || float.IsInfinity(tickInterval) || tickInterval <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(tickInterval));
            }

            if (float.IsNaN(priority) || float.IsInfinity(priority))
            {
                throw new ArgumentOutOfRangeException(nameof(priority));
            }

            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
            {
                throw new ArgumentOutOfRangeException(nameof(magnitude));
            }

            InstanceId = instanceId;
            Definition = definition;
            SourcePlayerId = sourcePlayerId;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            Stack = stack;
            StartTime = startTime;
            Duration = duration;
            ExecutionAuthority = executionAuthority;
            Version = version;
            TickDamage = tickDamage;
            TotalTicks = totalTicks;
            CompletedTicks = completedTicks;
            TickInterval = tickInterval;
            Priority = priority;
            DamageSourceId = damageSourceId;
            SourceContext = sourceContext;
            Magnitude = magnitude;
        }

        public StatusInstanceId InstanceId { get; }
        public StatusDefinition Definition { get; }
        public EnemyStatusID DefinitionId => Definition.Id;
        public uint SourcePlayerId { get; }
        public uint SourceEntityId { get; }
        public uint TargetEntityId { get; }
        public int Stack { get; }
        public double StartTime { get; }
        public float Duration { get; }
        public double EndTime => StartTime + Duration;
        public StatusExecutionAuthority ExecutionAuthority { get; }
        public uint Version { get; }
        public int TickDamage { get; }
        public int TotalTicks { get; }
        public int CompletedTicks { get; }
        public int RemainingTicks => TotalTicks - CompletedTicks;
        public float TickInterval { get; }
        public float Priority { get; }
        public uint DamageSourceId { get; }
        public CombatContext SourceContext { get; }
        public float Magnitude { get; }

        public StatusInstance WithProgress(int completedTicks)
        {
            return new StatusInstance(
                InstanceId,
                Definition,
                SourcePlayerId,
                SourceEntityId,
                TargetEntityId,
                Stack,
                StartTime,
                Duration,
                ExecutionAuthority,
                Version,
                TickDamage,
                TotalTicks,
                completedTicks,
                TickInterval,
                Priority,
                DamageSourceId,
                SourceContext,
                Magnitude);
        }

        public StatusInstance WithStack(int stack)
        {
            return new StatusInstance(
                InstanceId,
                Definition,
                SourcePlayerId,
                SourceEntityId,
                TargetEntityId,
                stack,
                StartTime,
                Duration,
                ExecutionAuthority,
                Version,
                TickDamage,
                TotalTicks,
                CompletedTicks,
                TickInterval,
                Priority,
                DamageSourceId,
                SourceContext,
                Magnitude);
        }
    }

    public enum StatusStateOrigin : byte
    {
        Predicted = 0,
        Canonical = 1
    }

    public enum StatusChangeKind : byte
    {
        Added = 0,
        Updated = 1,
        Removed = 2
    }

    public enum StatusRemovalReason : byte
    {
        None = 0,
        Expired = 1,
        Consumed = 2,
        Cleared = 3,
        Reconciled = 4
    }

    public readonly struct StatusChange
    {
        public StatusChange(
            StatusChangeKind kind,
            StatusStateOrigin origin,
            StatusInstance instance,
            StatusRemovalReason removalReason = StatusRemovalReason.None)
        {
            Kind = kind;
            Origin = origin;
            Instance = instance;
            RemovalReason = removalReason;
        }

        public StatusChangeKind Kind { get; }
        public StatusStateOrigin Origin { get; }
        public StatusInstance Instance { get; }
        public StatusRemovalReason RemovalReason { get; }
    }
}
