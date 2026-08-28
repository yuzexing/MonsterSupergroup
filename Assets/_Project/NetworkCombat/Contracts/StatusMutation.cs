using System;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public enum StatusMutationKind : byte
    {
        ApplyOrRefresh = 0,
        Remove = 1
    }

    [Serializable]
    public struct StatusMutation
    {
        public ulong EventId;
        public ulong RootEventId;
        public ulong ParentEventId;
        public uint Sequence;
        public ushort ChainDepth;
        public StatusMutationKind Kind;
        public ulong InstanceId;
        public uint DefinitionId;
        public byte StackMode;
        public int MaxStacks;
        public uint SourcePlayerId;
        public uint SourceEntityId;
        public uint TargetEntityId;
        public uint AbilityId;
        public uint BuildId;
        public ulong Tags;
        public uint TargetStateVersion;
        public int StackDelta;
        public double StartTime;
        public float Duration;
        public byte ExecutionAuthority;
        public uint BaseVersion;
        public int TickDamage;
        public int TotalTicks;
        public int CompletedTicks;
        public float TickInterval;
        public float Priority;
        public float Magnitude;
        public uint DamageSourceId;

        public static StatusMutation From(
            StatusChange change,
            CombatEventId mutationEventId,
            int stackDelta)
        {
            if (!mutationEventId.IsValid)
            {
                throw new ArgumentException("Mutation event ID must be valid.", nameof(mutationEventId));
            }

            StatusInstance instance = change.Instance;
            CombatContext source = instance.SourceContext;
            return new StatusMutation
            {
                EventId = mutationEventId.Value,
                RootEventId = source.IsValid
                    ? source.RootEventId.Value
                    : mutationEventId.Value,
                ParentEventId = source.IsValid ? source.EventId.Value : 0UL,
                Sequence = mutationEventId.Sequence,
                ChainDepth = source.IsValid && source.ChainDepth < ushort.MaxValue
                    ? (ushort)(source.ChainDepth + 1)
                    : (ushort)0,
                Kind = change.Kind == StatusChangeKind.Removed
                    ? StatusMutationKind.Remove
                    : StatusMutationKind.ApplyOrRefresh,
                InstanceId = instance.InstanceId.Value,
                DefinitionId = (uint)instance.DefinitionId,
                StackMode = (byte)instance.Definition.StackMode,
                MaxStacks = instance.Definition.MaxStacks,
                SourcePlayerId = instance.SourcePlayerId,
                SourceEntityId = instance.SourceEntityId,
                TargetEntityId = instance.TargetEntityId,
                AbilityId = source.IsValid ? source.AbilityId : 0u,
                BuildId = source.IsValid ? source.BuildId : 0u,
                Tags = source.IsValid ? (ulong)source.Tags : (ulong)CombatTags.Status,
                TargetStateVersion = source.IsValid ? source.TargetStateVersion : 0u,
                StackDelta = stackDelta,
                StartTime = instance.StartTime,
                Duration = instance.Duration,
                ExecutionAuthority = (byte)instance.ExecutionAuthority,
                BaseVersion = instance.Version,
                TickDamage = instance.TickDamage,
                TotalTicks = instance.TotalTicks,
                CompletedTicks = instance.CompletedTicks,
                TickInterval = instance.TickInterval,
                Priority = instance.Priority,
                Magnitude = instance.Magnitude,
                DamageSourceId = instance.DamageSourceId
            };
        }
    }
}
