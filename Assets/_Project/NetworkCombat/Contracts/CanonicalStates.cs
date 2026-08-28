using System;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public enum CombatEntityKind : byte
    {
        Enemy = 0,
        Player = 1,
        OtherShared = 2
    }

    public enum CombatEntityAuthority : byte
    {
        ServerCanonical = 0,
        OwnerFinal = 1
    }

    [Serializable]
    public struct CanonicalEntityState
    {
        public uint EntityId;
        public uint OwnerPlayerId;
        public byte Kind;
        public byte Authority;
        public int Health;
        public int MaxHealth;
        public bool Alive;
        public bool AbsoluteInvulnerable;
        public uint StateVersion;
        public uint KillerPlayerId;
    }

    [Serializable]
    public struct CanonicalStatusState
    {
        public bool Removed;
        public ulong InstanceId;
        public uint DefinitionId;
        public byte StackMode;
        public int MaxStacks;
        public uint SourcePlayerId;
        public uint SourceEntityId;
        public uint TargetEntityId;
        public int Stack;
        public double StartTime;
        public float Duration;
        public byte ExecutionAuthority;
        public uint Version;
        public int TickDamage;
        public int TotalTicks;
        public int CompletedTicks;
        public float TickInterval;
        public float Priority;
        public float Magnitude;
        public uint DamageSourceId;
        public ulong SourceEventId;
        public ulong RootEventId;
        public ulong ParentEventId;
        public uint SourceSequence;
        public ushort SourceChainDepth;
        public uint AbilityId;
        public uint BuildId;
        public ulong SourceTags;
        public uint TargetStateVersion;

        public StatusInstance ToStatusInstance()
        {
            if (Removed)
            {
                throw new InvalidOperationException("A removed status has no live StatusInstance.");
            }

            CombatContext sourceContext = SourceEventId != 0UL
                ? new CombatContext(
                    new CombatEventId(SourceEventId),
                    new CombatEventId(RootEventId != 0UL ? RootEventId : SourceEventId),
                    new CombatEventId(ParentEventId),
                    SourceSequence,
                    SourceChainDepth,
                    SourcePlayerId,
                    SourceEntityId,
                    TargetEntityId,
                    AbilityId,
                    BuildId,
                    (CombatTags)SourceTags,
                    TargetStateVersion)
                : default;
            return new StatusInstance(
                new StatusInstanceId(InstanceId),
                new StatusDefinition(
                    (EnemyStatusID)DefinitionId,
                    (StatusStackMode)StackMode,
                    MaxStacks),
                SourcePlayerId,
                SourceEntityId,
                TargetEntityId,
                Stack,
                StartTime,
                Duration,
                (StatusExecutionAuthority)ExecutionAuthority,
                Version,
                TickDamage,
                TotalTicks,
                CompletedTicks,
                TickInterval,
                Priority,
                DamageSourceId,
                sourceContext,
                Magnitude);
        }

        public static CanonicalStatusState From(StatusInstance instance)
        {
            return new CanonicalStatusState
            {
                Removed = false,
                InstanceId = instance.InstanceId.Value,
                DefinitionId = (uint)instance.DefinitionId,
                StackMode = (byte)instance.Definition.StackMode,
                MaxStacks = instance.Definition.MaxStacks,
                SourcePlayerId = instance.SourcePlayerId,
                SourceEntityId = instance.SourceEntityId,
                TargetEntityId = instance.TargetEntityId,
                Stack = instance.Stack,
                StartTime = instance.StartTime,
                Duration = instance.Duration,
                ExecutionAuthority = (byte)instance.ExecutionAuthority,
                Version = instance.Version,
                TickDamage = instance.TickDamage,
                TotalTicks = instance.TotalTicks,
                CompletedTicks = instance.CompletedTicks,
                TickInterval = instance.TickInterval,
                Priority = instance.Priority,
                Magnitude = instance.Magnitude,
                DamageSourceId = instance.DamageSourceId,
                SourceEventId = instance.SourceContext.EventId.Value,
                RootEventId = instance.SourceContext.RootEventId.Value,
                ParentEventId = instance.SourceContext.ParentEventId.Value,
                SourceSequence = instance.SourceContext.Sequence,
                SourceChainDepth = instance.SourceContext.ChainDepth,
                AbilityId = instance.SourceContext.AbilityId,
                BuildId = instance.SourceContext.BuildId,
                SourceTags = (ulong)instance.SourceContext.Tags,
                TargetStateVersion = instance.SourceContext.TargetStateVersion
            };
        }

        public static CanonicalStatusState Removal(StatusInstanceId instanceId, uint version)
        {
            return new CanonicalStatusState
            {
                Removed = true,
                InstanceId = instanceId.Value,
                Version = version
            };
        }
    }

    [Serializable]
    public struct CanonicalWorldBatch
    {
        public uint ServerSequence;
        public CanonicalEntityState[] Entities;
        public CanonicalStatusState[] Statuses;
        public ConfirmedKill[] ConfirmedKills;
    }
}
